using UnityEngine;
using FFmpeg.AutoGen;
using System.Runtime.InteropServices;

public static class FFmpegManager
{
    unsafe public static void Init()
    {
        FFmpegBinariesHelper.RegisterFFmpegBinaries();
        ffmpeg.avformat_network_init(); // 可选：用于启用网络协议（如RTSP）

        AVFormatContext* formatContext = ffmpeg.avformat_alloc_context();
        // ffmpeg.avformat_open_input(&formatContext, inputPath, null, null);

        Debug.Log("FFmpeg version: " + ffmpeg.av_version_info());
    }
    private static GCHandle? _logCallbackHandle;

    private static unsafe void SetupLogging()
    {
        ffmpeg.av_log_set_level(ffmpeg.AV_LOG_VERBOSE);

        if (_logCallbackHandle is { IsAllocated: true })
            return; // 避免重复分配

        av_log_set_callback_callback logCallback = (p0, level, format, vl) =>
        {
            if (level > ffmpeg.av_log_get_level()) return;

            const int lineSize = 1024;
            byte* lineBuffer = stackalloc byte[lineSize];
            int printPrefix = 1;
            ffmpeg.av_log_format_line(p0, level, format, vl, lineBuffer, lineSize, &printPrefix);
            string message = Marshal.PtrToStringAnsi((System.IntPtr)lineBuffer);

            if (string.IsNullOrEmpty(message)) return;

            if (level <= ffmpeg.AV_LOG_ERROR)
                Debug.LogError("[FFmpeg] " + message);
            else if (level <= ffmpeg.AV_LOG_WARNING)
                Debug.LogWarning("[FFmpeg] " + message);
            else
                Debug.Log("[FFmpeg] " + message);
        };

        _logCallbackHandle = GCHandle.Alloc(logCallback);
        ffmpeg.av_log_set_callback(logCallback);
    }

    public static void ReleaseLogging()
    {
        if (_logCallbackHandle is { IsAllocated: true })
        {
            _logCallbackHandle.Value.Free();
            _logCallbackHandle = null;
        }
    }
}
