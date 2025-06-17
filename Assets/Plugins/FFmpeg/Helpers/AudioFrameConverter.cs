using System;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

public sealed unsafe class AudioFrameConverter : IDisposable
{
    public static readonly ulong MAX_AUDIO_FRAME_SIZE = 192000;
    public delegate void AudioFrameDelegate(float[] datas);
    public delegate void AudioCompleteDelegate(int channels, int sampleRate);
    public AudioFrameDelegate audioFrameDelegate;
    public AudioCompleteDelegate audioCompleteDelegate;
    private readonly AVHWDeviceType hwDevice;
    private readonly AVFormatContext* formatContext;

    public AudioFrameConverter(string url)
    {
        // 分配一个AVFormatContext，FFMPEG所有的操作都要通过这个AVFormatContext来进行
        formatContext = ffmpeg.avformat_alloc_context();

        var pFormatContext = formatContext;
        // 打开文件
        ffmpeg.avformat_open_input(&pFormatContext, url, null, null).ThrowExceptionIfError();
        // 获取视频流信息
        ffmpeg.avformat_find_stream_info(formatContext, null).ThrowExceptionIfError();

        // // 打印视频信息
        // ffmpeg.av_dump_format(formatContext, 0, url, 0);
    }

    public void Decode()
    {
        AVCodec* codec = null;
        // 查找视频/音频数据流解码器
        var streamIndex = ffmpeg.av_find_best_stream(formatContext, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, &codec, 0).ThrowExceptionIfError();
        var stream = formatContext->streams[streamIndex];
        // 根据解码器参数来创建解码器内容
        var codecContext = ffmpeg.avcodec_alloc_context3(codec);
        ffmpeg.avcodec_parameters_to_context(codecContext, stream->codecpar).ThrowExceptionIfError();

        // codecContext->pkt_timebase = stream->time_base;

        // 打开解码器 
        ffmpeg.avcodec_open2(codecContext, codec, null).ThrowExceptionIfError();

        SwrContext* swrCtx = ffmpeg.swr_alloc();
        AVChannelLayout inLayout = new AVChannelLayout();
        AVChannelLayout outLayout = new AVChannelLayout();

        // 设置输入/输出声道布局
        ffmpeg.av_channel_layout_default(&inLayout, codecContext->ch_layout.nb_channels);
        ffmpeg.av_channel_layout_default(&outLayout, 2); // stereo

        AVSampleFormat out_sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_FLT;
        int out_sample_rate = codecContext->sample_rate;
        int out_channels = outLayout.nb_channels;
        byte* audioOutBuffer = (byte*)ffmpeg.av_malloc(MAX_AUDIO_FRAME_SIZE * 2);

        ffmpeg.swr_alloc_set_opts2(
            &swrCtx,
            &outLayout, out_sample_fmt, out_sample_rate,
            &codecContext->ch_layout, codecContext->sample_fmt, codecContext->sample_rate,
            0, null
        ).ThrowExceptionIfError();

        ffmpeg.swr_init(swrCtx).ThrowExceptionIfError();


        AVPacket* pkt = ffmpeg.av_packet_alloc();
        AVFrame* frame = ffmpeg.av_frame_alloc();
        while (ffmpeg.av_read_frame(formatContext, pkt) >= 0)
        {
            if (pkt->stream_index == streamIndex)
            {
                if (ffmpeg.avcodec_send_packet(codecContext, pkt) >= 0)
                {
                    while (ffmpeg.avcodec_receive_frame(codecContext, frame) >= 0)
                    {
                        /*
                          Planar（平面），其数据格式排列方式为 (特别记住，该处是以点nb_samples采样点来交错，不是以字节交错）:
                          LLLLLLRRRRRRLLLLLLRRRRRRLLLLLLRRRRRRL...（每个LLLLLLRRRRRR为一个音频帧）
                          而不带P的数据格式（即交错排列）排列方式为：
                          LRLRLRLRLRLRLRLRLRLRLRLRLRLRLRLRLRLRL...（每个LR为一个音频样本）
                        */
                        if (ffmpeg.av_sample_fmt_is_planar(codecContext->sample_fmt) >= 0)
                        {
                            byte*[] inputDataArray = new byte*[8]; // 最多支持 8 通道
                            for (uint i = 0; i < 8; i++)
                                inputDataArray[i] = frame->data[i];

                            int convertedSamples;
                            fixed (byte** dataPtrs = inputDataArray)
                            {
                                convertedSamples = ffmpeg.swr_convert(
                                    swrCtx,
                                    &audioOutBuffer, (int)MAX_AUDIO_FRAME_SIZE,
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

                            // //pcm播放时是LRLRLR格式，所以要交错保存数据
                            // int numBytes = ffmpeg.av_get_bytes_per_sample(out_sample_fmt);
                            // for (int i = 0; i < frame->nb_samples; i++)
                            // {
                            //     for (int ch = 0; ch < 2; ch++)
                            //     {
                            //         // fwrite((char*)audioOutBuffer[ch] + numBytes * i, 1, numBytes, file);
                            //         IntPtr bufferPtr = new IntPtr((char*)audioOutBuffer[ch] + numBytes * i);
                            //         byte[] frameData = new byte[numBytes];
                            //         Marshal.Copy(bufferPtr, frameData, 0, numBytes);
                            //         audioFrameDelegate(frameData);
                            //     }
                            // }
                        }
                    }
                }
            }
            ffmpeg.av_packet_unref(pkt);
        }

        audioCompleteDelegate?.Invoke(out_channels, codecContext->sample_rate);

        ffmpeg.av_frame_free(&frame);
        ffmpeg.av_packet_free(&pkt);
        ffmpeg.avcodec_free_context(&codecContext);
    }

    public void Dispose()
    {
        var pFormatContext = formatContext;
        ffmpeg.avformat_close_input(&pFormatContext);
    }
}