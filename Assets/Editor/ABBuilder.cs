#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 打包：输出工程外干净目录，可选同步完整首包到 StreamingAssets
/// </summary>
public static class ABBuilder
{
    const string MenuRoot = "Tools/AssetBundle/";

    [MenuItem(MenuRoot + "Build (当前平台 + 同步 StreamingAssets)")]
    public static void BuildCurrent() => Build(EditorUserBuildSettings.activeBuildTarget, true);

    [MenuItem(MenuRoot + "Build Only (不同步 StreamingAssets)")]
    public static void BuildOnly() => Build(EditorUserBuildSettings.activeBuildTarget, false);

    public static void Build(BuildTarget target, bool syncToStreamingAssets)
    {
        string output = ABPath.BuildOutputRoot;
        if (Directory.Exists(output))
            Directory.Delete(output, true);
        Directory.CreateDirectory(output);

        var options = BuildAssetBundleOptions.ChunkBasedCompression;

        AssetBundleManifest unityManifest = BuildPipeline.BuildAssetBundles(output, options, target);
        if (unityManifest == null)
        {
            Debug.LogError("[ABBuilder] 构建失败");
            return;
        }

        var abManifest = GenerateManifest(output, unityManifest);
        RenameToHashAndWriteManifest(output, abManifest);
        File.WriteAllText(Path.Combine(output, "version.txt"), abManifest.version);

        Debug.Log($"[ABBuilder] 完成 → {output}  ver={abManifest.version}");

        if (syncToStreamingAssets)
            SyncToStreamingAssets(output);

        AssetDatabase.Refresh();
    }

    static ABManifest GenerateManifest(string outputDir, AssetBundleManifest unityManifest)
    {
        var result = new ABManifest
        {
            version = System.DateTime.Now.ToString("yyyyMMdd.HHmmss"),
            bundles = new List<ABInfo>()
        };

        foreach (string name in unityManifest.GetAllAssetBundles())
        {
            string filePath = Path.Combine(outputDir, name);
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
            if (!File.Exists(src)) continue;

            string dst = Path.Combine(outputDir, info.hash + ".unity3d");
            if (File.Exists(dst)) File.Delete(dst);
            File.Copy(src, dst);
            File.Delete(src);

            string m = src + ".manifest";
            if (File.Exists(m)) File.Delete(m);
        }

        string root = Path.Combine(outputDir, Path.GetFileName(outputDir));
        if (File.Exists(root)) File.Delete(root);
        if (File.Exists(root + ".manifest")) File.Delete(root + ".manifest");

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
