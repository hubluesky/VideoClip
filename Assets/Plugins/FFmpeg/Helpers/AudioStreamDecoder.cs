using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

// 音频比特率 (Bitrate)：在音频编码中，比特率表示每秒用于表示声音的比特数。较高的音频比特率通常意味着更好的音质，但也需要更多的存储空间。

// 比特率单位为 bps (bits per second，比特每秒) 或 kbps (kilobits per second，千比特每秒)。

// 视频比特率：在视频编码中，比特率表示每秒用于表示视频和声音的比特数。较高的视频比特率通常意味着更高的视频质量，但也需要更多的带宽和存储空间。

// 赫兹 (Hz)：是表示声音频率或振动频率的单位。它衡量了声波或振动的周期性，即每秒内发生的往复振动次数。

// 采样率 (Sample Rate)：它表示在一秒钟内对模拟信号进行采样 (采集样本) 的次数。采样频率通常以赫兹（Hz）为单位来表示。

// 以下是一些常见的采样频率和它们的应用领域：

// 44.1 kHz：这是 CD 音质的标准采样频率，用于音乐录制和播放。它能够准确地捕捉人类听觉范围内的声音。
// 48 kHz：广播、视频制作和数字音频设备常用的采样频率。
// 96 kHz 和 192 kHz：高保真音乐录制和音频制作中使用的高采样频率。它们提供更高的音频质量，但需要更多的存储空间。
// 8 kHz 和 16 kHz：电话通信中常用的采样频率。电话音质相对较低，所以较低的采样频率足以表示声音。
// 位深度 (Bit Depth)：也称为比特深度，是用来衡量音频或图像信号精度的参数。在音频领域，位深度表示每个音频样本的振幅级别可以用多少位的二进制数字来表示。位深度越高，样本的精度越高，能够表示的振幅级别就越多，从而提供更高的音频质量。

// 以下是一些常见的位深度值和它们的含义：

// 16 位深度：这是最常见的音频位深度，通常用于 CD 音质和音频文件。每个音频样本使用 16 个二进制位来表示，可以表示 2^16（约65,536）个不同的振幅级别。
// 24 位深度：24 位深度用于高保真音频录制和制作，以提供更高的音频质量。每个样本使用 24 个二进制位来表示，能够表示更大的动态范围。
// 32 位深度：32 位深度在某些音频处理应用中使用，尤其是在数字音频工作站中。它提供更高的精度，但需要更多的存储空间。
// 声道 (Channel)：声道是指音频中的声音通道或独立声音源。在音频领域，声道通常用于描述声音播放的方式，包括单声道 (Mono) 和多声道 (Stereo、立体声) 等。

// 以下是一些常见的声道配置：

// 单声道 (Mono)：单声道音频包含一个声道，所有声音从同一位置播放。这是最简单的声道配置，通常用于广播、电话通信等。

// 双声道 (Stereo)：双声道音频包含两个声道，通常分别称为左声道和右声道。左声道和右声道的声音可以分别听到，用于模拟空间感和立体声效果。

// 多声道 (Multichannel)：多声道音频可以包含三个或更多声道，用于音乐制作、电影制作和环绕声音响系统。

// 常见的多声道配置包括 5.1 声道（前置三声道+中置声道+后置两声道+低音炮声道）和 7.1 声道（前置三声道+中置声道+后置四声道+低音炮声道）等。

// 采样率、位深度、声道数和比特率之间的关系可以用公式表示：比特率 = ∑ (每个声道的采样率 × 位深度)。

public unsafe class AudioStreamDecoder : IDisposable
{
    private readonly AVFormatContext* _pFormatContext;
    private readonly AVCodecContext* _pCodecContext;
    private readonly AVPacket* _pPacket;
    private readonly AVFrame* _pFrame;
    private readonly int _streamIndex;

    public AVSampleFormat SampleFormat => _pCodecContext->sample_fmt;
    public int SampleRate => _pCodecContext->sample_rate;
    public int Channels => _pCodecContext->ch_layout.nb_channels;
    // public int Channels => _pCodecContext->channels;

    public AudioStreamDecoder(string url)
    {
        _pFormatContext = ffmpeg.avformat_alloc_context();
        var pFormatContext = _pFormatContext;
        if (ffmpeg.avformat_open_input(&pFormatContext, url, null, null) != 0)
            throw new ApplicationException("Could not open file.");

        if (ffmpeg.avformat_find_stream_info(_pFormatContext, null) != 0)
            throw new ApplicationException("Could not find stream info.");

        // 查找音频流索引
        AVCodec* codec = null;
        _streamIndex = ffmpeg.av_find_best_stream(_pFormatContext, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, &codec, 0);
        if (_streamIndex < 0) throw new ApplicationException("Could not find audio stream.");

        _pCodecContext = ffmpeg.avcodec_alloc_context3(codec);
        ffmpeg.avcodec_parameters_to_context(_pCodecContext, _pFormatContext->streams[_streamIndex]->codecpar).ThrowExceptionIfError();

        ffmpeg.avcodec_open2(_pCodecContext, codec, null).ThrowExceptionIfError();

        _pPacket = ffmpeg.av_packet_alloc();
        _pFrame = ffmpeg.av_frame_alloc();
    }

    public bool TryDecodeNextFrame(out byte[] pcmBytes, out int frameSampleCount)
    {
        // 初始化返回值
        pcmBytes = null;
        frameSampleCount = 0;

        ffmpeg.av_frame_unref(_pFrame);

        int error;
        while ((error = ffmpeg.av_read_frame(_pFormatContext, _pPacket)) >= 0)
        {
            if (_pPacket->stream_index != _streamIndex)
            {
                ffmpeg.av_packet_unref(_pPacket);
                continue;
            }

            error = ffmpeg.avcodec_send_packet(_pCodecContext, _pPacket);
            ffmpeg.av_packet_unref(_pPacket);
            if (error < 0)
            {
                ffmpeg.av_frame_unref(_pFrame);
                return false;
            }

            error = ffmpeg.avcodec_receive_frame(_pCodecContext, _pFrame);
            if (error == ffmpeg.AVERROR(ffmpeg.EAGAIN) || error == ffmpeg.AVERROR_EOF)
                return false;
            error.ThrowExceptionIfError();

            // 获取帧数据大小
            int dataSize = ffmpeg.av_samples_get_buffer_size(
                null,
                Channels,
                _pFrame->nb_samples,
                _pCodecContext->sample_fmt,
                1
            );

            if (dataSize < 0)
            {
                return false;
            }

            // 拷贝PCM数据
            pcmBytes = new byte[dataSize];
            Marshal.Copy((IntPtr)_pFrame->data[0], pcmBytes, 0, dataSize);
            frameSampleCount = _pFrame->nb_samples;
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        var pFrame = _pFrame;
        ffmpeg.av_frame_free(&pFrame);

        var pPacket = _pPacket;
        ffmpeg.av_packet_free(&pPacket);

        var pCodecContext = _pCodecContext;
        ffmpeg.avcodec_free_context(&pCodecContext);
        // ffmpeg.avcodec_close(_pCodecContext);

        var pFormatContext = _pFormatContext;
        ffmpeg.avformat_close_input(&pFormatContext);
    }
}
