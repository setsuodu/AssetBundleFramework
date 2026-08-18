#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// AssetBundle 打包工具
/// 输出到工程外干净目录（Build/AssetBundles/平台），不产生 .meta
/// 可选：把完整当前版本同步到 StreamingAssets 作为首包
/// </summary>
public static class ABBuilder
{
    const string MenuRoot = "Tools/AssetBundle/";

    [MenuItem(MenuRoot + "Build (当前平台)")]
    public static void BuildCurrent()
    {
        Build(EditorUserBuildSettings.activeBuildTarget, true);
    }

    [MenuItem(MenuRoot + "Build Only (不拷贝到 StreamingAssets)")]
    public static void BuildOnly()
    {
        Build(EditorUserBuildSettings.activeBuildTarget, false);
    }

    public static void Build(BuildTarget target, bool syncToStreamingAssets)
    {
        string output = ABPath.BuildOutputRoot;
        if (Directory.Exists(output))
            Directory.Delete(output, true);
        Directory.CreateDirectory(output);

        // 使用 Deterministic + ChunkBased 便于增量
        var options = BuildAssetBundleOptions.ChunkBasedCompression;

        AssetBundleManifest unityManifest = BuildPipeline.BuildAssetBundles(
            output, options, target);

        if (unityManifest == null)
        {
            Debug.LogError("[ABBuilder] 构建失败");
            return;
        }

        // 生成我们自己的 manifest（带 hash + 依赖）
        var abManifest = GenerateManifest(output, unityManifest);

        // 把文件改名为 hash.unity3d，并写 manifest
        RenameToHashAndWriteManifest(output, abManifest);

        // 写 version.txt
        File.WriteAllText(Path.Combine(output, "version.txt"), abManifest.version);

        Debug.Log($"[ABBuilder] 构建完成 → {output}  version={abManifest.version}");

        if (syncToStreamingAssets)
        {
            SyncToStreamingAssets(output);
        }

        AssetDatabase.Refresh();
    }

    static ABManifest GenerateManifest(string outputDir, AssetBundleManifest unityManifest)
    {
        var result = new ABManifest();
        result.version = System.DateTime.Now.ToString("yyyyMMdd.HHmmss");
        result.bundles = new List<ABInfo>();

        string[] all = unityManifest.GetAllAssetBundles();
        foreach (string name in all)
        {
            string filePath = Path.Combine(outputDir, name);
            if (!File.Exists(filePath)) continue;

            string hash = ComputeMD5(filePath);
            string[] deps = unityManifest.GetAllDependencies(name);

            var info = new ABInfo
            {
                name = name.ToLowerInvariant().Replace('\\', '/'),
                hash = hash,
                size = new FileInfo(filePath).Length,
                depends = deps
            };
            result.bundles.Add(info);
        }
        return result;
    }

    static void RenameToHashAndWriteManifest(string outputDir, ABManifest manifest)
    {
        // 先复制为 hash 名，再删原名（保留 .manifest 可删）
        foreach (var info in manifest.bundles)
        {
            string src = Path.Combine(outputDir, info.name);
            // Unity 有时会生成不带扩展的名字，兼容一下
            if (!File.Exists(src))
            {
                // 尝试直接找
                continue;
            }

            string dst = Path.Combine(outputDir, info.hash + ".unity3d");
            if (File.Exists(dst))
                File.Delete(dst);
            File.Copy(src, dst);

            // 删除原始名字和 .manifest
            File.Delete(src);
            string manifestFile = src + ".manifest";
            if (File.Exists(manifestFile))
                File.Delete(manifestFile);
        }

        // 删除 Unity 生成的总 manifest
        string rootManifest = Path.Combine(outputDir, Path.GetFileName(outputDir));
        if (File.Exists(rootManifest)) File.Delete(rootManifest);
        if (File.Exists(rootManifest + ".manifest")) File.Delete(rootManifest + ".manifest");

        string json = JsonUtility.ToJson(manifest, true);
        File.WriteAllText(Path.Combine(outputDir, "manifest.json"), json);
    }

    /// <summary>
    /// 把干净构建产物完整拷贝到 StreamingAssets 作为首包
    /// 只拷贝 .unity3d 和 manifest/version，不会带 .meta
    /// </summary>
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

        Debug.Log($"[ABBuilder] 已同步首包到 StreamingAssets → {target}");
        AssetDatabase.Refresh();
    }

    static string ComputeMD5(string filePath)
    {
        using (var md5 = MD5.Create())
        using (var stream = File.OpenRead(filePath))
        {
            byte[] hash = md5.ComputeHash(stream);
            var sb = new StringBuilder();
            foreach (byte b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
#endif
