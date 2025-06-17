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
}
