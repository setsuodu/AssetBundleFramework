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

    [MenuItem(MenuRoot + "Open Persistent (热更下载目录)", false, 200)]
    public static void MenuOpenPersistent() => ABPath.OpenPersistentFolder();

    [MenuItem(MenuRoot + "Open StreamingAssets (首包目录)", false, 201)]
    public static void MenuOpenStreaming() => ABPath.OpenStreamingFolder();

    [MenuItem(MenuRoot + "Open Build Output (构建产物)", false, 202)]
    public static void MenuOpenBuildOutput() => ABPath.OpenBuildOutputFolder();

    public static void Build(BuildTarget target, bool syncToStreamingAssets)
    {
        CleanLabels();
        TryAutoFixShared();
        SetLabels();
        TrySetSharedLabelsFromDeps();

        //string output = ABPath.BuildOutputRoot;
        string output = GetBuildOutputForTarget(target);   // 用上面的方法
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
        // 需要清理的目录（Bundles + Art）
        string[] rootsToClean = { "Assets/Bundles", "Assets/Art" };

        int cleared = 0;

        foreach (string root in rootsToClean)
        {
            if (!AssetDatabase.IsValidFolder(root))
                continue;

            string[] guids = AssetDatabase.FindAssets("", new[] { root });
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
                    cleared++;
                }
            }
        }

        // 再清理全局名称列表
        string[] names = AssetDatabase.GetAllAssetBundleNames();
        for (int i = 0; i < names.Length; i++)
            AssetDatabase.RemoveAssetBundleName(names[i], true);

        AssetDatabase.RemoveUnusedAssetBundleNames();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[ABBuilder] Clean Labels 完成，强制清空了 {cleared} 个资源（含 Art）");
    }

    static string PathToBundleName(string assetPath)
    {
        string norm = assetPath.Replace('\\', '/');
        const string prefix = "Assets/Bundles/";
        if (!norm.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        // 【Issue #3】同字体家族多字重 ttf 会被 TrueTypeFontImporter 按 Font Names
        // 自动互相写入 fallbackFontReferences（只读、Reimport 会再生），若按文件拆包
        // 会形成跨 Bundle 依赖环。Fonts 目录全部强制打进同一 AB。
        // 与 ABDependencyChecker.SharedLabelRules 中 fonts/common 规则保持一致。
        if (norm.StartsWith("Assets/Bundles/Fonts/", StringComparison.OrdinalIgnoreCase))
            return "fonts/common";

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

        // 【Issue #3】打包后检测并报告 AB 依赖环（双向/多向循环）
        ReportCircularDependencies(result);
        return result;
    }

    /// <summary>
    /// 检测 manifest 中的循环依赖并打 Warning，便于 CI / 本地一眼发现问题。
    /// </summary>
    static void ReportCircularDependencies(ABManifest manifest)
    {
        if (manifest?.bundles == null || manifest.bundles.Count == 0) return;

        var graph = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in manifest.bundles)
        {
            if (b == null || string.IsNullOrEmpty(b.name)) continue;
            graph[b.name] = b.depends ?? Array.Empty<string>();
        }

        var cycles = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();

        void Dfs(string node)
        {
            if (stack.Contains(node))
            {
                int idx = path.IndexOf(node);
                if (idx >= 0)
                    cycles.Add(string.Join(" → ", path.GetRange(idx, path.Count - idx)) + " → " + node);
                return;
            }
            if (visited.Contains(node)) return;
            visited.Add(node);
            stack.Add(node);
            path.Add(node);

            if (graph.TryGetValue(node, out var deps))
            {
                foreach (var d in deps)
                {
                    if (string.IsNullOrEmpty(d)) continue;
                    string dn = d.ToLowerInvariant().Replace('\\', '/');
                    if (graph.ContainsKey(dn))
                        Dfs(dn);
                }
            }

            path.RemoveAt(path.Count - 1);
            stack.Remove(node);
        }

        foreach (var key in graph.Keys)
            Dfs(key);

        if (cycles.Count > 0)
        {
            Debug.LogError($"[ABBuilder] 检测到 {cycles.Count} 处 AB 循环依赖（可能导致运行时加载死锁）:\n  - " +
                           string.Join("\n  - ", cycles) +
                           "\n建议：将互相引用的资源打进同一 Bundle（如同家族字体统一 fonts/common）。");
        }
        else
        {
            Debug.Log("[ABBuilder] 依赖环检查通过，无循环依赖。");
        }
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

    /// <summary>
    /// 无头 / CI 入口。用法：
    /// Unity -batchmode -quit -projectPath . -executeMethod ABBuilder.BuildFromCI -buildTarget StandaloneWindows64 -logFile -
    /// 可选参数：-syncStreaming true|false（默认 true）
    /// </summary>
    public static void BuildFromCI()
    {
        int exitCode = 0;
        try
        {
            // 1. 解析平台（优先 Unity 自带 -buildTarget，其次自定义 -abTarget）
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i] == "-buildTarget" || args[i] == "-abTarget") && i + 1 < args.Length)
                {
                    if (Enum.TryParse(args[i + 1], true, out BuildTarget parsed))
                        target = parsed;
                }
            }

            // 2. 是否同步到 StreamingAssets
            bool sync = true;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-syncStreaming" && i + 1 < args.Length)
                    sync = args[i + 1].Equals("true", StringComparison.OrdinalIgnoreCase);
            }

            // 3. 切换活动平台（避免部分导入器按错误平台处理）
            if (EditorUserBuildSettings.activeBuildTarget != target)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildPipeline.GetBuildTargetGroup(target), target);
            }

            Debug.Log($"[ABBuilder.CI] target={target}  syncStreaming={sync}");
            Build(target, sync);

            // 4. 简单校验产物
            string outDir = GetBuildOutputForTarget(target);
            string manifest = Path.Combine(outDir, "manifest.json");
            string version = Path.Combine(outDir, "version.txt");
            if (!File.Exists(manifest) || !File.Exists(version))
            {
                Debug.LogError("[ABBuilder.CI] 产物缺失：manifest.json 或 version.txt");
                exitCode = 1;
            }
            else
            {
                Debug.Log($"[ABBuilder.CI] 成功 → {outDir}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[ABBuilder.CI] 异常: {e}");
            exitCode = 1;
        }
        finally
        {
            // batchmode 下必须显式退出，否则进程可能挂起
            if (Application.isBatchMode)
                EditorApplication.Exit(exitCode);
        }
    }

    /// <summary>按 BuildTarget 返回输出目录（解决 GetPlatformName 编译期问题）</summary>
    public static string GetBuildOutputForTarget(BuildTarget target)
    {
        string platform = target switch
        {
            BuildTarget.Android => "Android",
            BuildTarget.iOS => "iOS",
            BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 => "StandaloneWindows64",
            BuildTarget.StandaloneOSX => "StandaloneOSX",
            BuildTarget.StandaloneLinux64 => "StandaloneLinux64",
            BuildTarget.WebGL => "WebGL",
            _ => target.ToString()
        };
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        return Path.Combine(projectRoot, "Build", "AssetBundles", platform);
    }
}
#endif