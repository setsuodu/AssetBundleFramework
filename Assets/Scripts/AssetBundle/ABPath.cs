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
}
