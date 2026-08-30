#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 打包：Clean → AutoFix → SetLabels(Bundles) → SetSharedLabels(Art依赖) → Build → Clean
/// 菜单 priority 分层：0 检测 / 50 标签与修复 / 100 构建
/// </summary>
public static class ABBuilder
{
    const string MenuRoot = "Tools/AssetBundle/";
    const string BundlesRoot = "Assets/Bundles";

    // ----- 50 段：Label -----
    [MenuItem(MenuRoot + "Set Labels (Bundles 目录)", false, 52)]
    public static void MenuSetLabels() => SetLabels();

    [MenuItem(MenuRoot + "Clean Labels", false, 53)]
    public static void MenuCleanLabels() => CleanLabels();

    // ----- 100 段：构建 -----
    [MenuItem(MenuRoot + "Build (当前平台 + 同步 StreamingAssets)", false, 100)]
    public static void BuildCurrent() => Build(EditorUserBuildSettings.activeBuildTarget, true);

    [MenuItem(MenuRoot + "Build Only (不同步 StreamingAssets)", false, 101)]
    public static void BuildOnly() => Build(EditorUserBuildSettings.activeBuildTarget, false);

    public static void Build(BuildTarget target, bool syncToStreamingAssets)
    {
        CleanLabels();
        TryAutoFixShared();
        SetLabels();
        TrySetSharedLabelsFromDeps();

        string output = ABPath.BuildOutputRoot;
        if (Directory.Exists(output))
            Directory.Delete(output, true);
        Directory.CreateDirectory(output);

        var options = BuildAssetBundleOptions.ChunkBasedCompression;
        AssetBundleManifest unityManifest = BuildPipeline.BuildAssetBundles(output, options, target);
        if (unityManifest == null)
        {
            Debug.LogError("[ABBuilder] 构建失败");
            CleanLabels();
            return;
        }

        var abManifest = GenerateManifest(output, unityManifest);
        RenameToHashAndWriteManifest(output, abManifest);
        File.WriteAllText(Path.Combine(output, "version.txt"), abManifest.version);
        Debug.Log($"[ABBuilder] 完成 → {output}  ver={abManifest.version}");

        CleanLabels();

        if (syncToStreamingAssets)
            SyncToStreamingAssets(output);

        AssetDatabase.Refresh();
    }

    public static void SetLabels()
    {
        if (!AssetDatabase.IsValidFolder(BundlesRoot))
        {
            Debug.LogError($"[ABBuilder] 目录不存在: {BundlesRoot}");
            return;
        }

        AssetDatabase.RemoveUnusedAssetBundleNames();
        int count = 0;
        string[] guids = AssetDatabase.FindAssets("", new[] { BundlesRoot });

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
                continue;
            if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                continue;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".cs" || ext == ".dll" || ext == ".asmdef")
                continue;

            string bundleName = PathToBundleName(path);
            if (string.IsNullOrEmpty(bundleName))
                continue;

            var importer = AssetImporter.GetAtPath(path);
            if (importer == null)
                continue;

            // 强制设置（避免之前的顺序问题和判断问题）
            importer.SetAssetBundleNameAndVariant(bundleName, string.Empty);
            count++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[ABBuilder] Set Labels (Bundles) 约 {count} 个");
    }

    public static void CleanLabels()
    {
        // 1. 强制清空 Bundles 目录下所有资源的 Label
        if (AssetDatabase.IsValidFolder(BundlesRoot))
        {
            string[] guids = AssetDatabase.FindAssets("", new[] { BundlesRoot });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
                    continue;
                if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;

                var importer = AssetImporter.GetAtPath(path);
                if (importer == null) continue;

                if (!string.IsNullOrEmpty(importer.assetBundleName) ||
                    !string.IsNullOrEmpty(importer.assetBundleVariant))
                {
                    importer.SetAssetBundleNameAndVariant(string.Empty, string.Empty);
                }
            }
        }

        // 2. 清理全局名称列表
        string[] names = AssetDatabase.GetAllAssetBundleNames();
        for (int i = 0; i < names.Length; i++)
            AssetDatabase.RemoveAssetBundleName(names[i], true);

        AssetDatabase.RemoveUnusedAssetBundleNames();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[ABBuilder] Clean Labels 完成（强制清空）");
    }

    static string PathToBundleName(string assetPath)
    {
        string norm = assetPath.Replace('\\', '/');
        const string prefix = "Assets/Bundles/";
        if (!norm.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        string relative = norm.Substring(prefix.Length);
        string withoutExt = Path.ChangeExtension(relative, null) ?? relative;
        withoutExt = withoutExt.Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(withoutExt))
            return null;
        return withoutExt.ToLowerInvariant();
    }

    static void TryAutoFixShared()
    {
        try
        {
            ABDependencyChecker.AutoFixShared(false);
            Debug.Log("[ABBuilder] AutoFixShared 完成");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ABBuilder] AutoFix 跳过: {e.Message}");
        }
    }

    static void TrySetSharedLabelsFromDeps()
    {
        try
        {
            int n = ABDependencyChecker.SetSharedLabelsFromDependencies();
            Debug.Log($"[ABBuilder] SetSharedLabels(Art依赖) {n} 个");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ABBuilder] SetSharedLabels 跳过: {e.Message}");
        }
    }

    static ABManifest GenerateManifest(string outputDir, AssetBundleManifest unityManifest)
    {
        var result = new ABManifest
        {
            version = DateTime.Now.ToString("yyyyMMdd.HHmmss"),
            bundles = new List<ABInfo>()
        };

        foreach (string name in unityManifest.GetAllAssetBundles())
        {
            string filePath = Path.Combine(outputDir, name);
            if (!File.Exists(filePath))
            {
                string leaf = Path.GetFileName(name);
                filePath = Path.Combine(outputDir, leaf);
            }
            if (!File.Exists(filePath)) continue;

            result.bundles.Add(new ABInfo
            {
                name = name.ToLowerInvariant().Replace('\\', '/'),
                hash = ComputeMD5(filePath),
                size = new FileInfo(filePath).Length,
                depends = unityManifest.GetAllDependencies(name)
            });
        }
        return result;
    }

    static void RenameToHashAndWriteManifest(string outputDir, ABManifest manifest)
    {
        foreach (var info in manifest.bundles)
        {
            string src = Path.Combine(outputDir, info.name);
            if (!File.Exists(src))
                src = Path.Combine(outputDir, Path.GetFileName(info.name));
            if (!File.Exists(src)) continue;

            string dst = Path.Combine(outputDir, info.hash + ".unity3d");
            if (File.Exists(dst)) File.Delete(dst);
            File.Copy(src, dst);
            File.Delete(src);
            string m = src + ".manifest";
            if (File.Exists(m)) File.Delete(m);
        }

        foreach (var f in Directory.GetFiles(outputDir))
        {
            string name = Path.GetFileName(f);
            if (name.EndsWith(".manifest") || name == Path.GetFileName(outputDir))
                File.Delete(f);
        }

        File.WriteAllText(Path.Combine(outputDir, "manifest.json"),
            JsonUtility.ToJson(manifest, true));
    }

    static void SyncToStreamingAssets(string sourceDir)
    {
        string target = Path.Combine(Application.dataPath, "StreamingAssets", "AssetBundles", ABPath.GetPlatformName());
        if (Directory.Exists(target))
            Directory.Delete(target, true);
        Directory.CreateDirectory(target);

        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string name = Path.GetFileName(file);
            if (name.EndsWith(".manifest")) continue;
            File.Copy(file, Path.Combine(target, name), true);
        }
        Debug.Log($"[ABBuilder] 首包已同步 → {target}");
        AssetDatabase.Refresh();
    }

    static string ComputeMD5(string filePath)
    {
        using (var md5 = MD5.Create())
        using (var stream = File.OpenRead(filePath))
        {
            byte[] hash = md5.ComputeHash(stream);
            var sb = new StringBuilder();
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
#endif
