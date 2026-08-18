using System.IO;
using UnityEngine;

/// <summary>
/// 路径工具
/// 运行时优先级：persistent（热更） → StreamingAssets（首包）
/// 构建产物永远输出到工程外干净目录，不产生 .meta
/// </summary>
public static class ABPath
{
    /// <summary>热更目录（persistentDataPath）</summary>
    public static string PersistentRoot
    {
        get
        {
            return Path.Combine(Application.persistentDataPath, "AssetBundles", GetPlatformName());
        }
    }

    /// <summary>首包目录（StreamingAssets）</summary>
    public static string StreamingRoot
    {
        get
        {
            return Path.Combine(Application.streamingAssetsPath, "AssetBundles", GetPlatformName());
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// 构建输出根目录（工程外，干净，无 .meta）
    /// 例如：Project/Build/AssetBundles/Android
    /// </summary>
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

    /// <summary>
    /// 获取某个 Bundle 的实际文件路径（优先热更目录）
    /// </summary>
    public static string GetBundleFilePath(string bundleNameOrHashFile)
    {
        // 支持直接传 hash 文件名，也支持逻辑名
        string fileName = bundleNameOrHashFile;
        if (!fileName.EndsWith(".unity3d") && !fileName.EndsWith(".ab"))
            fileName = bundleNameOrHashFile; // 由上层决定最终文件名

        string hot = Path.Combine(PersistentRoot, fileName);
        if (File.Exists(hot))
            return hot;

        string stream = Path.Combine(StreamingRoot, fileName);
        return stream; // 即使不存在也返回，让上层判断
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
