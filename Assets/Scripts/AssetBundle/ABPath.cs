using System.IO;
using UnityEngine;

/// <summary>
/// 路径工具
/// 运行时优先级：persistent（热更） → StreamingAssets（首包）
/// 构建产物输出到工程外干净目录
/// </summary>
public static class ABPath
{
    public static string PersistentRoot =>
        Path.Combine(Application.persistentDataPath, "AssetBundles", GetPlatformName());

    public static string StreamingRoot =>
        Path.Combine(Application.streamingAssetsPath, "AssetBundles", GetPlatformName());

#if UNITY_EDITOR
    public static string BuildOutputRoot
    {
        get
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot, "Build", "AssetBundles", GetPlatformName());
        }
    }
#endif

    public static string GetPlatformName()
    {
#if UNITY_ANDROID
        return "Android";
#elif UNITY_IOS
        return "iOS";
#elif UNITY_STANDALONE_WIN
        return "StandaloneWindows64";
#elif UNITY_STANDALONE_OSX
        return "StandaloneOSX";
#else
        return Application.platform.ToString();
#endif
    }

    public static string GetManifestPath(bool preferPersistent = true)
    {
        if (preferPersistent)
        {
            string hot = Path.Combine(PersistentRoot, "manifest.json");
            if (File.Exists(hot))
                return hot;
        }
        return Path.Combine(StreamingRoot, "manifest.json");
    }

    public static void EnsurePersistentDir()
    {
        if (!Directory.Exists(PersistentRoot))
            Directory.CreateDirectory(PersistentRoot);
    }

    /// <summary>
    /// 用系统文件管理器打开指定目录（不存在则先创建）
    /// </summary>
    public static void OpenInExplorer(string dir)
    {
        if (string.IsNullOrEmpty(dir))
        {
            Debug.LogError("[ABPath] 路径为空");
            return;
        }

        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // 统一成系统分隔符，避免混用
        dir = Path.GetFullPath(dir);

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        System.Diagnostics.Process.Start("explorer.exe", dir.Replace('/', '\\'));
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
    System.Diagnostics.Process.Start("open", dir);
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
    System.Diagnostics.Process.Start("xdg-open", dir);
#else
    Debug.LogWarning($"[ABPath] 当前平台不支持直接打开: {dir}");
    Debug.Log(dir);
#endif

        Debug.Log($"[ABPath] 已打开: {dir}");
    }

    /// <summary>打开热更下载目录（persistent）</summary>
    public static void OpenPersistentFolder()
    {
        EnsurePersistentDir();
        OpenInExplorer(PersistentRoot);
    }

    /// <summary>打开 StreamingAssets 首包目录</summary>
    public static void OpenStreamingFolder()
    {
        if (!Directory.Exists(StreamingRoot))
            Directory.CreateDirectory(StreamingRoot);
        OpenInExplorer(StreamingRoot);
    }

#if UNITY_EDITOR
    /// <summary>打开构建输出目录</summary>
    public static void OpenBuildOutputFolder()
    {
        if (!Directory.Exists(BuildOutputRoot))
            Directory.CreateDirectory(BuildOutputRoot);
        OpenInExplorer(BuildOutputRoot);
    }
#endif
}
