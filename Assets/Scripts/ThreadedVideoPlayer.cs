using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using UnityEngine;
using UnityEngine.UI;

public class ThreadedVideoPlayer : MonoBehaviour
{
    public string videoPath;

    private readonly List<Texture2D> textureFrames = new();
    private readonly List<float> framePts = new();

    private RawImage rawImage;
    private AudioSource audioSource;

    private Thread decodeThread;
    private bool isAudioReady = false;

    void Start()
    {
        rawImage = GetComponent<RawImage>();
        audioSource = GetComponent<AudioSource>();

        FFmpegBinariesHelper.RegisterFFmpegBinaries();
        Debug.Log($"FFmpeg version: {ffmpeg.av_version_info()}");

        SetupLogging();
        ConfigureHWDecoder(out var deviceType);

        UnityMainThreadDispatcher.Instance();
        decodeThread = new Thread(() => DecodeMedia(deviceType));
        decodeThread.IsBackground = true;
        decodeThread.Start();
    }

    void Update()
    {
        if (!isAudioReady || textureFrames.Count == 0 || framePts.Count == 0) return;

        float currentTime = audioSource.time;

        // 找到当前时间对应的图像帧
        for (int i = 0; i < framePts.Count - 1; i++)
        {
            if (framePts[i] <= currentTime && currentTime < framePts[i + 1])
            {
                rawImage.texture = textureFrames[i];
                break;
            }
        }
    }

    private void OnDestroy()
    {
        decodeThread?.Join();

        foreach (var texture in textureFrames)
            Destroy(texture);
            
        textureFrames.Clear();
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

        // 记录图像帧和对应的显示时间
        msd.videoFrameDelegate += (bytes, width, height, ptsSec) =>
        {
            UnityMainThreadDispatcher.Instance().Enqueue(() =>
            {
                Texture2D texture = new(width, height, TextureFormat.ARGB32, false);
                texture.LoadRawTextureData(bytes);
                texture.Apply();
                textureFrames.Add(texture);
                framePts.Add(ptsSec);
            });
        };

        msd.videoCompleteDelegate += (frameRate, width, height) =>
        {
            UnityMainThreadDispatcher.Instance().Enqueue(() =>
            {
                rawImage.rectTransform.sizeDelta = new Vector2(width, height);
            });
        };

        // 音频缓存
        List<float> allSamples = new();

        msd.audioFrameDelegate += (pcmSamples) =>
        {
            allSamples.AddRange(pcmSamples);
        };

        msd.audioCompleteDelegate += (channels, sampleRate) =>
        {
            int totalSamples = allSamples.Count / channels;
            UnityMainThreadDispatcher.Instance().Enqueue(() =>
            {
                AudioClip clip = AudioClip.Create("DecodedAudio", totalSamples, channels, sampleRate, false);
                clip.SetData(allSamples.ToArray(), 0);
                audioSource.clip = clip;
            });
        };

        msd.decodeCompleteDelegate += () =>
        {
            UnityMainThreadDispatcher.Instance().Enqueue(() =>
            {
                isAudioReady = true;
                audioSource.loop = true;
                audioSource.Play();
            });
        };
        msd.DecodeMedia(AVPixelFormat.AV_PIX_FMT_ARGB);
    }
}
