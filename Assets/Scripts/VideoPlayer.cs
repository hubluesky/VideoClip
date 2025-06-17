using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using UnityEngine;
using UnityEngine.UI;

public class VideoPlayer : MonoBehaviour
{
    public string videoPath;
    private readonly List<Texture2D> textureFrames = new();
    private int indexFrame = 0;
    private float lastFrameTime = 0;
    private float curFrameTime = 0;
    private float frameDuration;
    private RawImage rawImage;
    private AudioSource audioSource;

    void Awake()
    {
        rawImage = GetComponent<RawImage>();
        audioSource = GetComponent<AudioSource>();

        FFmpegBinariesHelper.RegisterFFmpegBinaries();
        print($"FFmpeg version info: {ffmpeg.av_version_info()}");
        SetupLogging();

        ConfigureHWDecoder(out var deviceType);

        // decode all frames from url, please not it might local resorce, e.g. string url = "../../sample_mpeg4.mp4";
        // var url = "http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4"; // be advised this file holds 1440 frames
        // using var vsd = new VideoStreamDecoder(videoPath);

        // DecodeVideoToImages(vsd, deviceType);
        // DecodeAudioToPCM(vsd, deviceType);
        DecodeMedia(deviceType);

        audioSource.Play();
    }

    void Update()
    {
        if (textureFrames.Count <= 0) return;

        var lastTime = lastFrameTime;
        curFrameTime += Time.deltaTime;
        if (curFrameTime - lastTime > frameDuration)
        {
            lastFrameTime = curFrameTime;
            rawImage.texture = textureFrames[++indexFrame % textureFrames.Count];
        }
    }

    private static unsafe void SetupLogging()
    {
        ffmpeg.av_log_set_level(ffmpeg.AV_LOG_VERBOSE);

        // do not convert to local function
        av_log_set_callback_callback logCallback = (p0, level, format, vl) =>
        {
            if (level > ffmpeg.av_log_get_level()) return;

            var lineSize = 1024;
            var lineBuffer = stackalloc byte[lineSize];
            var printPrefix = 1;
            ffmpeg.av_log_format_line(p0, level, format, vl, lineBuffer, lineSize, &printPrefix);
            var line = Marshal.PtrToStringAnsi((IntPtr)lineBuffer);
            switch (level)
            {
                case ffmpeg.AV_LOG_ERROR:
                    Debug.LogError(line);
                    break;
                case ffmpeg.AV_LOG_WARNING:
                    Debug.LogWarning(line);
                    break;
                default:
                    Debug.Log(line);
                    break;
            }
        };

        ffmpeg.av_log_set_callback(logCallback);
    }

    private static void ConfigureHWDecoder(out AVHWDeviceType HWtype)
    {
        HWtype = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
        var availableHWDecoders = new Dictionary<int, AVHWDeviceType>();

        var type = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
        var number = 0;

        while ((type = ffmpeg.av_hwdevice_iterate_types(type)) != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
        {
            availableHWDecoders.Add(++number, type);
            // Debug.Log($"{++number}. {type}");
        }

        if (availableHWDecoders.Count == 0)
        {
            Debug.Log("Your system have no hardware decoders.");
            HWtype = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
            return;
        }

        var decoderNumber = availableHWDecoders.SingleOrDefault(t => t.Value == AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2).Key;
        if (decoderNumber == 0)
            decoderNumber = availableHWDecoders.First().Key;
        // Debug.Log($"Selected [{decoderNumber}]");
        availableHWDecoders.TryGetValue(decoderNumber, out HWtype);
    }

    private void DecodeMedia(AVHWDeviceType deviceType)
    {
        var msd = new MediaStreamDecoder(videoPath, deviceType);

        msd.videoFrameDelegate += (bytes, width, height) =>
        {
            Texture2D texture = new(width, height, TextureFormat.ARGB32, false);
            texture.LoadRawTextureData(bytes);
            texture.Apply();
            textureFrames.Add(texture);
        };

        msd.videoCompleteDelegate += (frameRate, width, height) =>
        {
            frameDuration = 1 / frameRate;
             rawImage.GetComponent<RectTransform>().sizeDelta = new Vector2(width, height);
        };

        List<float> allSamples = new();
        msd.audioFrameDelegate += (pcms) =>
        {
            allSamples.AddRange(pcms);
        };

        msd.audioCompleteDelegate += (channels, sampleRate) =>
        {
            // 创建 AudioClip（采样数 = 样本总数 / 通道数）
            int totalSamples = allSamples.Count / channels;
            AudioClip clip = AudioClip.Create("DecodedAudio", totalSamples, channels, sampleRate, false);
            clip.SetData(allSamples.ToArray(), 0);

            audioSource.clip = clip;
            audioSource.loop = true;
        };

        msd.DecodeMedia(AVPixelFormat.AV_PIX_FMT_ARGB);
    }

    // private unsafe void DecodeMedia(AVHWDeviceType deviceType)
    // {
    //     // 缓存所有PCM float数据
    //     using var msd = new AudioFrameConverter(videoPath);
    //     List<float> allSamples = new();

    //     msd.audioFrameDelegate += (pcms) =>
    //     {
    //         allSamples.AddRange(pcms);
    //     };

    //     msd.audioCompleteDelegate += (channels, sampleRate) =>
    //     {
    //         // 创建 AudioClip（采样数 = 样本总数 / 通道数）
    //         int totalSamples = allSamples.Count / channels;
    //         AudioClip clip = AudioClip.Create("DecodedAudio", totalSamples, channels, sampleRate, false);
    //         clip.SetData(allSamples.ToArray(), 0);

    //         audioSource.clip = clip;
    //         audioSource.loop = true;
    //     };

    //     msd.Decode();
    // }

    // private unsafe void DecodeVideoToImages(VideoStreamDecoder vsd, AVHWDeviceType hwDevice)
    // {
    //     using var videoContext = vsd.DecodeMedia(AVMediaType.AVMEDIA_TYPE_VIDEO, hwDevice);
    //     frameDuration = 1.0f / vsd.GetFrameRate(videoContext);

    //     var sourceSize = new System.Drawing.Size(videoContext.width, videoContext.height);
    //     var sourcePixelFormat = hwDevice == AVHWDeviceType.AV_HWDEVICE_TYPE_NONE ? videoContext.pixelFormat : GetHWPixelFormat(hwDevice);
    //     using var vfc = new VideoFrameConverter(sourceSize, sourcePixelFormat, sourceSize, AVPixelFormat.@AV_PIX_FMT_ARGB);

    //     rawImage.GetComponent<RectTransform>().sizeDelta = new Vector2(sourceSize.Width, sourceSize.Height);

    //     while (vsd.TryDecodeNextFrame(videoContext, out var frame))
    //     {
    //         var convertedFrame = vfc.Convert(frame);
    //         Texture2D texture = new(convertedFrame.width, convertedFrame.height, TextureFormat.ARGB32, false);
    //         var bytes = vsd.DecodeVideoFrame(convertedFrame, true);
    //         texture.LoadRawTextureData(bytes);
    //         texture.Apply();
    //         textureFrames.Add(texture);
    //     }

    // }

    // private unsafe void DecodeAudioToPCM(VideoStreamDecoder vsd, AVHWDeviceType hWDevice)
    // {
    //     using var audioContext = vsd.DecodeMedia(AVMediaType.AVMEDIA_TYPE_AUDIO);
    //     int channels = audioContext.channels;
    //     var sampleFormat = audioContext.sampleFormat;
    //     // 缓存所有PCM float数据
    //     List<float> allSamples = new();

    //     while (vsd.TryDecodeNextFrame(audioContext, out var frame))
    //     {
    //         vsd.DecodeAudioFrame(frame, channels, sampleFormat, out var pcmBytes, out var frameSampleCount);
    //         // 将 byte[] 转 float[]（每个 float = 4 字节）
    //         float[] floatSamples = new float[frameSampleCount * channels];
    //         Buffer.BlockCopy(pcmBytes, 0, floatSamples, 0, pcmBytes.Length);
    //         allSamples.AddRange(floatSamples);
    //     }

    //     if (allSamples.Count == 0)
    //     {
    //         Debug.LogWarning("音频数据为空，无法播放");
    //         return;
    //     }

    //     // 创建 AudioClip（采样数 = 样本总数 / 通道数）
    //     int totalSamples = allSamples.Count / channels;
    //     AudioClip clip = AudioClip.Create("DecodedAudio", totalSamples, channels, audioContext.sampleRate, false);
    //     clip.SetData(allSamples.ToArray(), 0);

    //     audioSource.clip = clip;
    //     audioSource.loop = true;
    // }

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
}
