using System.IO;
using FFmpeg.AutoGen;
using UnityEngine;

public static class FFmpegBinariesHelper
{
    public static void RegisterFFmpegBinaries()
    {
        string path = GetFFmpegBinaryPath();
        Debug.Log($"FFmpeg Path: {path}");
        ffmpeg.RootPath = path;
    }

    private static string GetFFmpegBinaryPath()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        return Path.Combine(Application.dataPath, "Plugins", "FFmpeg", "Windows");
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        return Path.Combine(Application.dataPath, "Plugins", "FFmpeg", "MacOS");
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
        return Path.Combine(Application.dataPath, "Plugins", "FFmpeg", "Linux");
#else
        throw new NotSupportedException("Platform not supported.");
#endif
    }
    // private static unsafe void DecodeAllFramesToImages(AVHWDeviceType HWDevice)
    // {
    //         // decode all frames from url, please not it might local resorce, e.g. string url = "../../sample_mpeg4.mp4";

    //         var url = "http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4"; // be advised this file holds 1440 frames
    //         using var vsd = new VideoStreamDecoder(url, HWDevice);

    //         Console.WriteLine($"codec name: {vsd.CodecName}");

    //         var info = vsd.GetContextInfo();
    //         info.ToList().ForEach(x => Console.WriteLine($"{x.Key} = {x.Value}"));

    //         var sourceSize = vsd.FrameSize;
    //         var sourcePixelFormat = HWDevice == AVHWDeviceType.AV_HWDEVICE_TYPE_NONE
    //             ? vsd.PixelFormat
    //             : GetHWPixelFormat(HWDevice);
    //         var destinationSize = sourceSize;
    //         var destinationPixelFormat = AVPixelFormat.@AV_PIX_FMT_BGRA;
    //         using var vfc = new VideoFrameConverter(sourceSize, sourcePixelFormat, destinationSize, destinationPixelFormat);

    //         var frameNumber = 0;

    //         while (vsd.TryDecodeNextFrame(out var frame))
    //         {
    //                 var convertedFrame = vfc.Convert(frame);
    //                 WriteFrame(convertedFrame, frameNumber);

    //                 Console.WriteLine($"frame: {frameNumber}");
    //                 frameNumber++;
    //                 if (frameNumber > 1000) break;
    //         }
    // }

    // private static AVPixelFormat GetHWPixelFormat(AVHWDeviceType hWDevice)
    // {
    //         return hWDevice switch
    //         {
    //                 AVHWDeviceType.AV_HWDEVICE_TYPE_NONE => AVPixelFormat.AV_PIX_FMT_NONE,
    //                 AVHWDeviceType.AV_HWDEVICE_TYPE_VDPAU => AVPixelFormat.AV_PIX_FMT_VDPAU,
    //                 AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA => AVPixelFormat.AV_PIX_FMT_CUDA,
    //                 AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI => AVPixelFormat.AV_PIX_FMT_VAAPI,
    //                 AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2 => AVPixelFormat.AV_PIX_FMT_NV12,
    //                 AVHWDeviceType.AV_HWDEVICE_TYPE_QSV => AVPixelFormat.AV_PIX_FMT_QSV,
    //                 AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX => AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX,
    //                 AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA => AVPixelFormat.AV_PIX_FMT_NV12,
    //                 AVHWDeviceType.AV_HWDEVICE_TYPE_DRM => AVPixelFormat.AV_PIX_FMT_DRM_PRIME,
    //                 AVHWDeviceType.AV_HWDEVICE_TYPE_OPENCL => AVPixelFormat.AV_PIX_FMT_OPENCL,
    //                 AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC => AVPixelFormat.AV_PIX_FMT_MEDIACODEC,
    //                 _ => AVPixelFormat.AV_PIX_FMT_NONE
    //         };
    // }
}
