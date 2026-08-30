#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 打包流水线：
/// Clean Labels →（可选 AutoFix Atlas）→ Set Labels → Build → 写 manifest → Clean Labels
/// 只处理 Assets/Bundles 下资源的目录 Label；共享 Art 依赖需扩展规则或依赖 Checker 报告手工/后续脚本
/// </summary>
public static class ABBuilder
{
    const string MenuRoot = "Tools/AssetBundle/";
    const string BundlesRoot = "Assets/Bundles";

    [MenuItem(MenuRoot + "Set Labels Only")]
    public static void MenuSetLabels() => SetLabels();

    [MenuItem(MenuRoot + "Clean Labels Only")]
    public static void MenuCleanLabels() => CleanLabels();

    [MenuItem(MenuRoot + "Build (当前平台 + 同步 StreamingAssets)")]
    public static void BuildCurrent() => Build(EditorUserBuildSettings.activeBuildTarget, true);

    [MenuItem(MenuRoot + "Build Only (不同步 StreamingAssets)")]
    public static void BuildOnly() => Build(EditorUserBuildSettings.activeBuildTarget, false);

    /// <summary>
    /// 完整构建入口
    /// </summary>
    public static void Build(BuildTarget target, bool syncToStreamingAssets)
    {
        // 1. 先清干净，避免旧 Label 污染
        CleanLabels();

        // 2. 多引用 Sprite 压入 Common Atlas（不搬文件；无此脚本则跳过）
        TryAutoFixShared();

        // 3. 按 Bundles 目录结构打 Label
        SetLabels();

        // 4. Build
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

        // 5. 再清 Label，工程保持干净
        CleanLabels();

        if (syncToStreamingAssets)
            SyncToStreamingAssets(output);

        AssetDatabase.Refresh();
    }

    // -------------------------------------------------------------------------
    // Set Labels / Clean Labels
    // -------------------------------------------------------------------------

    /// <summary>
    /// 遍历 Assets/Bundles，按「一级目录/相对路径去扩展名」设 assetBundleName
    /// 例：Assets/Bundles/UI/UI_Home.prefab → ui/ui_home
    ///     Assets/Bundles/Fonts/Dosis.ttf → fonts/dosis
    ///     Assets/Bundles/Sprites/Atlas_UI_Common.spriteatlas → sprites/atlas_ui_common
    /// </summary>
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
            // 跳过脚本等
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".cs" || ext == ".dll" || ext == ".asmdef")
                continue;

            string bundleName = PathToBundleName(path);
            if (string.IsNullOrEmpty(bundleName))
                continue;

            var importer = AssetImporter.GetAtPath(path);
            if (importer == null)
                continue;

            if (importer.assetBundleName != bundleName || !string.IsNullOrEmpty(importer.assetBundleVariant))
            {
                importer.assetBundleVariant = string.Empty;
                importer.assetBundleName = bundleName;
                count++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[ABBuilder] Set Labels 完成，更新约 {count} 个资源");
    }

    /// <summary>
    /// 清除工程内全部 AssetBundle Name（打包后保持干净）
    /// </summary>
    public static void CleanLabels()
    {
        string[] names = AssetDatabase.GetAllAssetBundleNames();
        for (int i = 0; i < names.Length; i++)
            AssetDatabase.RemoveAssetBundleName(names[i], true);
        AssetDatabase.RemoveUnusedAssetBundleNames();
        AssetDatabase.Refresh();
        Debug.Log("[ABBuilder] Clean Labels 完成");
    }

    /// <summary>
    /// Assets/Bundles/{Type}/.../file.ext → type/相对路径去扩展名（小写）
    /// </summary>
    static string PathToBundleName(string assetPath)
    {
        string norm = assetPath.Replace('\\', '/');
        const string prefix = "Assets/Bundles/";
        if (!norm.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        string relative = norm.Substring(prefix.Length); // UI/UI_Home.prefab
        if (string.IsNullOrEmpty(relative))
            return null;

        // 去掉扩展名
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
            // 若工程里有 ABDependencyChecker 则调用
            var type = Type.GetType("ABDependencyChecker");
            if (type == null)
            {
                // 同程序集
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = asm.GetType("ABDependencyChecker");
                    if (type != null) break;
                }
            }
            if (type == null)
            {
                Debug.Log("[ABBuilder] 未找到 ABDependencyChecker，跳过 AutoFix");
                return;
            }
            var mi = type.GetMethod("AutoFixShared",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (mi != null)
            {
                mi.Invoke(null, new object[] { false });
                Debug.Log("[ABBuilder] 已调用 AutoFixShared");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ABBuilder] AutoFix 跳过: {e.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Manifest / 输出
    // -------------------------------------------------------------------------

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
            {
                // Unity 可能用子路径文件名
                string leaf = Path.GetFileName(info.name);
                src = Path.Combine(outputDir, leaf);
            }
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
