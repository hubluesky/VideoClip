using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

public sealed unsafe class AVCodecContextWrapper : IDisposable
{
    public readonly AVCodecContext* pCodecContext;
    public readonly int streamIndex;
    public readonly float frameRate;
    public readonly string codecName;

    internal readonly AVFrame* pFrame;
    internal readonly AVPacket* pPacket;
    internal readonly AVFrame* receivedFrame;

    public AVCodecContextWrapper(AVCodecContext* pCodecContext, int streamIndex, float frameRate, string codecName)
    {
        this.pCodecContext = pCodecContext;
        this.streamIndex = streamIndex;
        this.frameRate = frameRate;
        this.codecName = codecName;

        pPacket = ffmpeg.av_packet_alloc();
        pFrame = ffmpeg.av_frame_alloc();
        if (pCodecContext->hw_device_ctx != null)
            receivedFrame = ffmpeg.av_frame_alloc();
    }

    public AVPixelFormat pixelFormat => pCodecContext->pix_fmt;
    public AVSampleFormat sampleFormat => pCodecContext->sample_fmt;
    public int sampleRate => pCodecContext->sample_rate;
    public int channels => pCodecContext->ch_layout.nb_channels;
    public int width => pCodecContext->width;
    public int height => pCodecContext->height;

    public void Dispose()
    {
        var pFrame = this.pFrame;
        ffmpeg.av_frame_free(&pFrame);

        var pPacket = this.pPacket;
        ffmpeg.av_packet_free(&pPacket);

        if (receivedFrame != null)
        {
            var pReceivedFrame = receivedFrame;
            ffmpeg.av_frame_free(&pReceivedFrame);
        }

        var pCodecContext = this.pCodecContext;
        ffmpeg.avcodec_free_context(&pCodecContext);

        UnityEngine.Debug.Log("AVCodecContextWrapper Disposed，CodecName：" + codecName);
    }
}

public sealed unsafe class VideoStreamDecoder : IDisposable
{
    private readonly AVFormatContext* _pFormatContext;

    public VideoStreamDecoder(string url)
    {
        _pFormatContext = ffmpeg.avformat_alloc_context();

        var pFormatContext = _pFormatContext;
        ffmpeg.avformat_open_input(&pFormatContext, url, null, null).ThrowExceptionIfError();
        ffmpeg.avformat_find_stream_info(_pFormatContext, null).ThrowExceptionIfError();
    }

    public float GetFrameRate(AVCodecContextWrapper acc)
    {
        AVRational rational = _pFormatContext->streams[acc.streamIndex]->avg_frame_rate;
        return rational.den != 0 ? rational.num / (float)rational.den : 0;
    }

    public AVCodecContextWrapper DecodeMedia(AVMediaType mediaType = AVMediaType.AVMEDIA_TYPE_VIDEO, AVHWDeviceType HWDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
    {
        AVCodec* codec = null;
        var _streamIndex = ffmpeg.av_find_best_stream(_pFormatContext, mediaType, -1, -1, &codec, 0).ThrowExceptionIfError();
        var _pCodecContext = ffmpeg.avcodec_alloc_context3(codec);

        if (HWDeviceType != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
            ffmpeg.av_hwdevice_ctx_create(&_pCodecContext->hw_device_ctx, HWDeviceType, null, null, 0).ThrowExceptionIfError();

        ffmpeg.avcodec_parameters_to_context(_pCodecContext, _pFormatContext->streams[_streamIndex]->codecpar).ThrowExceptionIfError();
        ffmpeg.avcodec_open2(_pCodecContext, codec, null).ThrowExceptionIfError();

        AVRational rational = _pFormatContext->streams[_streamIndex]->avg_frame_rate;
        var frameRate = rational.den != 0 ? rational.num / (float)rational.den : 0;
        return new AVCodecContextWrapper(_pCodecContext, _streamIndex, frameRate, ffmpeg.avcodec_get_name(codec->id));
    }

    public void Dispose()
    {
        var pFormatContext = _pFormatContext;
        ffmpeg.avformat_close_input(&pFormatContext);

        UnityEngine.Debug.Log("AudioStreamDecoder Disposed");
    }

    public bool TryDecodeNextFrame(AVCodecContextWrapper codecContext, out AVFrame frame)
    {
        ffmpeg.av_frame_unref(codecContext.pFrame);
        if (codecContext.receivedFrame != null)
            ffmpeg.av_frame_unref(codecContext.receivedFrame);
        int error;

        do
        {
            try
            {
                do
                {
                    ffmpeg.av_packet_unref(codecContext.pPacket); // 重置pkt的内容
                    error = ffmpeg.av_read_frame(_pFormatContext, codecContext.pPacket); // 读取的是一帧视频，数据存入一个AVPacket的结构中

                    if (error == ffmpeg.AVERROR_EOF)
                    {
                        frame = *codecContext.pFrame;
                        return false;
                    }

                    error.ThrowExceptionIfError();
                } while (codecContext.pPacket->stream_index != codecContext.streamIndex);

                ffmpeg.avcodec_send_packet(codecContext.pCodecContext, codecContext.pPacket).ThrowExceptionIfError();
            }
            finally
            {
                ffmpeg.av_packet_unref(codecContext.pPacket); // 重置pkt的内容
            }

            error = ffmpeg.avcodec_receive_frame(codecContext.pCodecContext, codecContext.pFrame);
        } while (error == ffmpeg.AVERROR(ffmpeg.EAGAIN));

        error.ThrowExceptionIfError();

        if (codecContext.pCodecContext->hw_device_ctx != null)
        {
            ffmpeg.av_hwframe_transfer_data(codecContext.receivedFrame, codecContext.pFrame, 0).ThrowExceptionIfError();
            frame = *codecContext.receivedFrame;
        }
        else
            frame = *codecContext.pFrame;

        return true;
    }

    public byte[] DecodeVideoFrame(AVFrame frame, bool flip = true)
    {
        int width = frame.width;
        int height = frame.height;

        int bytesPerPixel = 4; // ARGB 格式
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

    public bool DecodeAudioFrame(AVFrame frame, int channels, AVSampleFormat sampleFormat, out byte[] pcmBytes, out int frameSampleCount)
    {
        // 获取帧数据大小
        int dataSize = ffmpeg.av_samples_get_buffer_size(null, channels, frame.nb_samples, sampleFormat, 1).ThrowExceptionIfError();

        if (dataSize < 0)
        {
            pcmBytes = null;
            frameSampleCount = 0;
            return false;
        }

        // 拷贝PCM数据
        pcmBytes = new byte[dataSize];
        Marshal.Copy((IntPtr)frame.data[0], pcmBytes, 0, dataSize);
        frameSampleCount = frame.nb_samples;
        return true;
    }


    public IReadOnlyDictionary<string, string> GetContextInfo()
    {
        AVDictionaryEntry* tag = null;
        var result = new Dictionary<string, string>();

        while ((tag = ffmpeg.av_dict_get(_pFormatContext->metadata, "", tag, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
        {
            var key = Marshal.PtrToStringAnsi((IntPtr)tag->key);
            var value = Marshal.PtrToStringAnsi((IntPtr)tag->value);
            result.Add(key, value);
        }

        return result;
    }
}
