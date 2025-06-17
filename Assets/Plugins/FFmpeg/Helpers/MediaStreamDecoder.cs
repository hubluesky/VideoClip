using System;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

public unsafe class MediaStreamDecoder : IDisposable
{
    public static readonly int MAX_AUDIO_FRAME_SIZE = 192000;
    public delegate void VideoFrameDelegate(byte[] data, int width, int height, float pts);
    public delegate void VideoCompleteDelegate(float frameRate, int width, int height);
    public delegate void AudioFrameDelegate(float[] datas);
    public delegate void AudioCompleteDelegate(int channels, int sampleRate);
    public VideoFrameDelegate videoFrameDelegate;
    public VideoCompleteDelegate videoCompleteDelegate;
    public AudioFrameDelegate audioFrameDelegate;
    public AudioCompleteDelegate audioCompleteDelegate;
    public readonly AVHWDeviceType hwDevice;
    private readonly AVFormatContext* formatContext;
    private AVStream* videoStream;
    private AVStream* audioStream;

    public MediaStreamDecoder(string url, AVHWDeviceType hWDevice = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
    {
        hwDevice = hWDevice;
        // 分配一个AVFormatContext，FFMPEG所有的操作都要通过这个AVFormatContext来进行
        formatContext = ffmpeg.avformat_alloc_context();

        var pFormatContext = formatContext;
        // 打开文件
        ffmpeg.avformat_open_input(&pFormatContext, url, null, null).ThrowExceptionIfError();
        // 获取视频流信息
        ffmpeg.avformat_find_stream_info(formatContext, null).ThrowExceptionIfError();

        // // 打印视频信息
        // ffmpeg.av_dump_format(formatContext, 0, url, 0);

        FindMediaStreams(formatContext);
    }

    public void DecodeMedia(AVPixelFormat pixelFormat)
    {
        var packet = ffmpeg.av_packet_alloc();
        var frame = ffmpeg.av_frame_alloc();

        var videoCodecContext = CreateCodecContext(videoStream);
        DecodeVideo(videoCodecContext, videoStream->index, ffmpeg.av_q2d(videoStream->time_base), pixelFormat, packet, frame);
        ffmpeg.avcodec_free_context(&videoCodecContext);

        ffmpeg.av_seek_frame(formatContext, videoStream->index, 0, ffmpeg.AVSEEK_FLAG_BACKWARD);

        var audioCodecContext = CreateCodecContext(audioStream);
        audioCodecContext->pkt_timebase = audioStream->time_base;
        DecodeAudio(audioCodecContext, audioStream->index, packet, frame);
        ffmpeg.avcodec_free_context(&audioCodecContext);

        ffmpeg.av_frame_free(&frame);
        ffmpeg.av_packet_free(&packet);
    }

    private void FindMediaStreams(AVFormatContext* formatContext)
    {
        // 从格式化上下文获取流索引
        for (var i = 0; i < formatContext->nb_streams; i++)
        {
            switch (formatContext->streams[i]->codecpar->codec_type)
            {
                case AVMediaType.AVMEDIA_TYPE_VIDEO:
                    videoStream = formatContext->streams[i];
                    break;
                case AVMediaType.AVMEDIA_TYPE_AUDIO:
                    audioStream = formatContext->streams[i];
                    break;
            }
        }
    }

    private AVCodecContext* CreateCodecContext(AVStream* stream)
    {
        var codec = ffmpeg.avcodec_find_decoder(stream->codecpar->codec_id);
        if (codec == null) throw new ApplicationException("Unsupported codec.");
        // 根据解码器参数来创建解码器内容
        var codecContext = ffmpeg.avcodec_alloc_context3(codec);
        ffmpeg.avcodec_parameters_to_context(codecContext, stream->codecpar).ThrowExceptionIfError();
        // 打开解码器 
        ffmpeg.avcodec_open2(codecContext, codec, null).ThrowExceptionIfError();

        if (hwDevice != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
            ffmpeg.av_hwdevice_ctx_create(&codecContext->hw_device_ctx, hwDevice, null, null, 0).ThrowExceptionIfError();
        return codecContext;
    }

    public static AVFrame* ReadMediaFrame(AVFormatContext* formatContext, AVCodecContext* codecContext, int streamIndex, AVPacket* packet, AVFrame* frame)
    {
        ffmpeg.av_frame_unref(frame);

        int error;
        do
        {
            do
            {
                ffmpeg.av_packet_unref(packet); // 重置pkt的内容
                error = ffmpeg.av_read_frame(formatContext, packet); // 读取的是一帧视频，数据存入一个AVPacket的结构中
                if (error == ffmpeg.AVERROR_EOF)
                    return null;
                error.ThrowExceptionIfError();
            } while (packet->stream_index != streamIndex);

            ffmpeg.avcodec_send_packet(codecContext, packet).ThrowExceptionIfError();
            error = ffmpeg.avcodec_receive_frame(codecContext, frame);

        } while (error == ffmpeg.AVERROR(ffmpeg.EAGAIN));
        error.ThrowExceptionIfError();

        return frame;
    }

    public void DecodeVideo(AVCodecContext* codecContext, int streamIndex, double timeBase, AVPixelFormat pixelFormat, AVPacket* packet, AVFrame* frame)
    {
        var sourceSize = new System.Drawing.Size(codecContext->width, codecContext->height);
        var sourcePixelFormat = hwDevice == AVHWDeviceType.AV_HWDEVICE_TYPE_NONE ? codecContext->pix_fmt : GetHWPixelFormat(hwDevice);
        using var vfc = new VideoFrameConverter(sourceSize, sourcePixelFormat, sourceSize, pixelFormat);

        var receivedFrame = ffmpeg.av_frame_alloc();
        ffmpeg.av_frame_unref(receivedFrame);

        AVFrame* outFrame;
        while ((outFrame = ReadMediaFrame(formatContext, codecContext, streamIndex, packet, frame)) != null)
        {
            if (codecContext->hw_device_ctx != null)
            {
                ffmpeg.av_hwframe_transfer_data(receivedFrame, outFrame, 0).ThrowExceptionIfError();
                outFrame = receivedFrame;
            }

            var convertedFrame = vfc.Convert(*outFrame);
            var bytes = DecodeVideoFrameToBytes(convertedFrame);

            videoFrameDelegate?.Invoke(bytes, convertedFrame.width, convertedFrame.height, (float)(frame->pts * timeBase));
        }

        ffmpeg.av_frame_free(&receivedFrame);

        AVRational rational = formatContext->streams[streamIndex]->avg_frame_rate;
        var frameRate = rational.den != 0 ? rational.num / (float)rational.den : 0;
        videoCompleteDelegate?.Invoke(frameRate, codecContext->width, codecContext->height);
    }

    public void DecodeAudio(AVCodecContext* codecContext, int streamIndex, AVPacket* packet, AVFrame* frame)
    {
        var swrCtx = ffmpeg.swr_alloc();
        var inLayout = new AVChannelLayout();
        var outLayout = new AVChannelLayout();

        // 设置输入/输出声道布局
        ffmpeg.av_channel_layout_default(&inLayout, codecContext->ch_layout.nb_channels);
        ffmpeg.av_channel_layout_default(&outLayout, 2); // stereo

        AVSampleFormat out_sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_FLT;
        int out_sample_rate = codecContext->sample_rate;
        int out_channels = outLayout.nb_channels;
        byte* audioOutBuffer = (byte*)ffmpeg.av_malloc((ulong)MAX_AUDIO_FRAME_SIZE * 2);

        ffmpeg.swr_alloc_set_opts2(
            &swrCtx,
            &outLayout, out_sample_fmt, out_sample_rate,
            &codecContext->ch_layout, codecContext->sample_fmt, codecContext->sample_rate,
            0, null
        ).ThrowExceptionIfError();

        ffmpeg.swr_init(swrCtx).ThrowExceptionIfError();

        while (ReadMediaFrame(formatContext, codecContext, streamIndex, packet, frame) != null)
        {
            if (ffmpeg.av_sample_fmt_is_planar(codecContext->sample_fmt) >= 0)
            {
                byte*[] inputDataArray = new byte*[8]; // 最多支持 8 通道
                for (uint i = 0; i < codecContext->ch_layout.nb_channels; i++)
                    inputDataArray[i] = frame->data[i];

                int convertedSamples;
                fixed (byte** dataPtrs = &inputDataArray[0])
                {
                    convertedSamples = ffmpeg.swr_convert(
                        swrCtx,
                        &audioOutBuffer, MAX_AUDIO_FRAME_SIZE,
                        dataPtrs, frame->nb_samples
                    ).ThrowExceptionIfError();
                }

                int outBufferSize = ffmpeg.av_samples_get_buffer_size(
                    null, out_channels,
                    convertedSamples, out_sample_fmt, 1
                ).ThrowExceptionIfError();

                float[] buffer = new float[convertedSamples * out_channels];
                Marshal.Copy((IntPtr)audioOutBuffer, buffer, 0, buffer.Length);
                audioFrameDelegate?.Invoke(buffer);
            }
        }

        audioCompleteDelegate?.Invoke(out_channels, codecContext->sample_rate);

        ffmpeg.av_channel_layout_uninit(&inLayout);
        ffmpeg.av_channel_layout_uninit(&outLayout);

        ffmpeg.swr_free(&swrCtx);
        ffmpeg.av_free(audioOutBuffer);
    }

    public byte[] DecodeVideoFrameToBytes(AVFrame frame, int bytesPerPixel = 4, bool flip = true)
    {
        int width = frame.width;
        int height = frame.height;

        int stride = width * bytesPerPixel;
        int totalBytes = height * stride;

        byte[] rawData = new byte[totalBytes];
        Marshal.Copy((IntPtr)frame.data[0], rawData, 0, totalBytes);

        if (flip)
        {
            // 就地行交换：top <-> bottom
            byte[] tempRow = new byte[stride]; // 仅分配一个临时行缓冲区
            for (int y = 0; y < height / 2; y++)
            {
                int topOffset = y * stride;
                int bottomOffset = (height - 1 - y) * stride;

                // Swap: temp <-> top
                Buffer.BlockCopy(rawData, topOffset, tempRow, 0, stride);
                Buffer.BlockCopy(rawData, bottomOffset, rawData, topOffset, stride);
                Buffer.BlockCopy(tempRow, 0, rawData, bottomOffset, stride);
            }
        }

        return rawData;
    }

    private static AVPixelFormat GetHWPixelFormat(AVHWDeviceType hWDevice)
    {
        return hWDevice switch
        {
            AVHWDeviceType.AV_HWDEVICE_TYPE_NONE => AVPixelFormat.AV_PIX_FMT_NONE,
            AVHWDeviceType.AV_HWDEVICE_TYPE_VDPAU => AVPixelFormat.AV_PIX_FMT_VDPAU,
            AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA => AVPixelFormat.AV_PIX_FMT_CUDA,
            AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI => AVPixelFormat.AV_PIX_FMT_VAAPI,
            AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2 => AVPixelFormat.AV_PIX_FMT_NV12,
            AVHWDeviceType.AV_HWDEVICE_TYPE_QSV => AVPixelFormat.AV_PIX_FMT_QSV,
            AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX => AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX,
            AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA => AVPixelFormat.AV_PIX_FMT_NV12,
            AVHWDeviceType.AV_HWDEVICE_TYPE_DRM => AVPixelFormat.AV_PIX_FMT_DRM_PRIME,
            AVHWDeviceType.AV_HWDEVICE_TYPE_OPENCL => AVPixelFormat.AV_PIX_FMT_OPENCL,
            AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC => AVPixelFormat.AV_PIX_FMT_MEDIACODEC,
            _ => AVPixelFormat.AV_PIX_FMT_NONE
        };
    }

    public void Dispose()
    {
        var pFormatContext = formatContext;
        ffmpeg.avformat_close_input(&pFormatContext);

        UnityEngine.Debug.Log("AudioStreamDecoder Disposed");
    }
}
