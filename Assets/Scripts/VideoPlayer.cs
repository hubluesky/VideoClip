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

    void Awake()
    {
        rawImage = GetComponent<RawImage>();

        FFmpegBinariesHelper.RegisterFFmpegBinaries();
        print($"FFmpeg version info: {ffmpeg.av_version_info()}");
        SetupLogging();

        ConfigureHWDecoder(out var deviceType);
        DecodeAllFramesToImages(videoPath, deviceType);
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

        Debug.Log("Select hardware decoder:");
        var type = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
        var number = 0;

        while ((type = ffmpeg.av_hwdevice_iterate_types(type)) != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
        {
            Debug.Log($"{++number}. {type}");
            availableHWDecoders.Add(number, type);
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

    private unsafe void DecodeAllFramesToImages(string url, AVHWDeviceType HWDevice)
    {
        // decode all frames from url, please not it might local resorce, e.g. string url = "../../sample_mpeg4.mp4";
        // var url = "http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4"; // be advised this file holds 1440 frames
        using var vsd = new VideoStreamDecoder(url, HWDevice);

        frameDuration = 1.0f / vsd.FrameRate;
        Debug.Log($"codec name: {vsd.CodecName}");

        var info = vsd.GetContextInfo();
        info.ToList().ForEach(x => Debug.Log($"{x.Key} = {x.Value}"));

        var sourceSize = vsd.FrameSize;
        var sourcePixelFormat = HWDevice == AVHWDeviceType.AV_HWDEVICE_TYPE_NONE ? vsd.PixelFormat : GetHWPixelFormat(HWDevice);
        var destinationSize = sourceSize;
        var destinationPixelFormat = AVPixelFormat.@AV_PIX_FMT_ARGB;
        using var vfc = new VideoFrameConverter(sourceSize, sourcePixelFormat, destinationSize, destinationPixelFormat);

        while (vsd.TryDecodeNextFrame(out var frame))
        {
            var convertedFrame = vfc.Convert(frame);
            var texture = WriteFrame(convertedFrame);
            textureFrames.Add(texture);
        }
    }

    private unsafe Texture2D WriteFrame(AVFrame convertedFrame)
    {
        int width = convertedFrame.width;
        int height = convertedFrame.height;

        int bytesPerPixel = 4; // BGRA 格式
        int stride = width * bytesPerPixel;
        int totalBytes = height * stride;

        byte[] rawData = new byte[totalBytes];

        Marshal.Copy((IntPtr)convertedFrame.data[0], rawData, 0, totalBytes);

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

        // 创建 Texture2D 并加载数据
        Texture2D texture = new(width, height, TextureFormat.ARGB32, false);
        texture.LoadRawTextureData(rawData);
        texture.Apply();

        return texture;
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
}
