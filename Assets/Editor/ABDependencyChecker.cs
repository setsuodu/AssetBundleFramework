#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

/// <summary>
/// 依赖冗余检测 + 可选自动处理（不搬文件）
/// 原则：公用显式 / 独享隐式；只分析 Assets/Bundles
/// - 多引用 Sprite → 写入白名单对应的 SpriteAtlas（路径不变）
/// - 多引用 Font/Mat/Shader → 写入报告，由 Set Labels 阶段处理
/// </summary>
public static class ABDependencyChecker
{
    const string MenuRoot = "Tools/AssetBundle/";
    const string BundlesRoot = "Assets/Bundles";

    /// <summary>判定为公用的最小引用次数</summary>
    public static int SharedThreshold = 2;

    /// <summary>
    /// 全局 Common Atlas（自动创建/更新）
    /// 被 ≥ SharedThreshold 个不同 Prefab 引用的碎图进入此 Atlas
    /// </summary>
    public const string CommonAtlasPath = "Assets/Bundles/Sprites/Atlas_UI_Common.spriteatlas";

    /// <summary>
    /// 模块白名单：目录关键字 → 模块 Atlas 路径
    /// 仅名单内模块生成模块级 Atlas；不要为每个 Panel 建一张
    /// 在代码里按项目增删即可
    /// </summary>
    public static readonly Dictionary<string, string> ModuleAtlasWhitelist =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // 示例（按系统，不按 Panel）：
            // { "Shop",   "Assets/Bundles/Sprites/Atlas_UI_Shop.spriteatlas" },
            // { "Battle", "Assets/Bundles/Sprites/Atlas_UI_Battle.spriteatlas" },
        };

    [MenuItem(MenuRoot + "Check Shared Dependencies (仅报告)")]
    public static void MenuCheckOnly()
    {
        Analyze(out var sharedNoLabel, out var sharedHasLabel, out var singleRef, out var prefabCount);
        var sb = BuildReportHeader(prefabCount, sharedNoLabel.Count, sharedHasLabel.Count, singleRef);
        AppendSharedDetails(sb, sharedNoLabel);
        PrintAndSaveReport(sb.ToString(), sharedNoLabel);
    }

    [MenuItem(MenuRoot + "AutoFix Shared → Common Atlas (不搬文件)")]
    public static void MenuAutoFix()
    {
        AutoFixShared(true);
    }

    /// <summary>供打包流水线在 Set Labels / Build 前调用</summary>
    public static void AutoFixShared(bool showDialog = false)
    {
        Analyze(out var sharedNoLabel, out var sharedHasLabel, out var singleRef, out var prefabCount);

        var commonSprites = new List<string>();
        var moduleSprites = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var otherShared = new List<SharedEntry>();

        foreach (var e in sharedNoLabel)
        {
            if (IsSpriteLike(e.AssetPath))
            {
                string module = TryMatchModule(e.AssetPath);
                if (module != null && ModuleAtlasWhitelist.ContainsKey(module))
                {
                    if (!moduleSprites.TryGetValue(module, out var list))
                    {
                        list = new List<string>();
                        moduleSprites[module] = list;
                    }
                    list.Add(e.AssetPath);
                }
                else
                    commonSprites.Add(e.AssetPath);
            }
            else
                otherShared.Add(e);
        }

        EnsureFolder(Path.GetDirectoryName(CommonAtlasPath));
        int addedCommon = UpdateAtlas(CommonAtlasPath, commonSprites, true);

        int addedModule = 0;
        foreach (var kv in moduleSprites)
        {
            if (!ModuleAtlasWhitelist.TryGetValue(kv.Key, out string atlasPath))
                continue;
            EnsureFolder(Path.GetDirectoryName(atlasPath));
            addedModule += UpdateAtlas(atlasPath, kv.Value, true);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var sb = BuildReportHeader(prefabCount, sharedNoLabel.Count, sharedHasLabel.Count, singleRef);
        sb.AppendLine($"[AutoFix] Common Atlas 本次新增: {addedCommon} → {CommonAtlasPath}");
        sb.AppendLine($"[AutoFix] 模块 Atlas 本次新增: {addedModule}");
        sb.AppendLine($"[AutoFix] 非图类公用（Font/Mat/Shader 等）: {otherShared.Count}");
        foreach (var e in otherShared.Take(40))
            sb.AppendLine($"    [{e.TypeTag}] {e.AssetPath}  ref={e.PrefabRefCount}");
        sb.AppendLine();
        sb.AppendLine("未移动任何文件。Prefab 仍引用原路径。下一步: Set Labels → Build → Clean Labels");

        PrintAndSaveReport(sb.ToString(), null);

        if (showDialog)
        {
            EditorUtility.DisplayDialog("AB AutoFix",
                $"Common +{addedCommon}\n模块 +{addedModule}\n非图公用: {otherShared.Count}\n见 Console / AB_SharedDependency_Report.txt",
                "OK");
        }
    }

    static void Analyze(
        out List<SharedEntry> sharedNoLabel,
        out List<SharedEntry> sharedHasLabel,
        out int singleRef,
        out int prefabCount)
    {
        sharedNoLabel = new List<SharedEntry>();
        sharedHasLabel = new List<SharedEntry>();
        singleRef = 0;
        prefabCount = 0;

        if (!AssetDatabase.IsValidFolder(BundlesRoot))
        {
            Debug.LogError($"[ABDependencyChecker] 缺少目录: {BundlesRoot}");
            return;
        }

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { BundlesRoot });
        prefabCount = prefabGuids.Length;

        var refMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var abRefMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                EditorUtility.DisplayProgressBar("扫描 Prefab 依赖", prefabPath,
                    (float)i / Math.Max(1, prefabGuids.Length));

                string prefabAb = GetAssetBundleName(prefabPath);
                foreach (string dep in AssetDatabase.GetDependencies(prefabPath, true))
                {
                    if (string.Equals(dep, prefabPath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!IsTrackableDependency(dep))
                        continue;

                    if (!refMap.TryGetValue(dep, out var pset))
                    {
                        pset = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        refMap[dep] = pset;
                    }
                    pset.Add(prefabPath);

                    string abKey = string.IsNullOrEmpty(prefabAb) ? prefabPath : prefabAb;
                    if (!abRefMap.TryGetValue(dep, out var aset))
                    {
                        aset = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        abRefMap[dep] = aset;
                    }
                    aset.Add(abKey);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        foreach (var kv in refMap)
        {
            int pc = kv.Value.Count;
            int ac = abRefMap.TryGetValue(kv.Key, out var abs) ? abs.Count : 0;
            if (pc < SharedThreshold && ac < SharedThreshold)
            {
                singleRef++;
                continue;
            }

            string label = GetAssetBundleName(kv.Key);
            var entry = new SharedEntry
            {
                AssetPath = kv.Key,
                PrefabRefCount = pc,
                BundleRefCount = ac,
                OwnLabel = label,
                ReferencedByPrefabs = kv.Value.OrderBy(x => x).ToList()
            };
            if (string.IsNullOrEmpty(label))
                sharedNoLabel.Add(entry);
            else
                sharedHasLabel.Add(entry);
        }

        sharedNoLabel.Sort((a, b) =>
        {
            int c = b.BundleRefCount.CompareTo(a.BundleRefCount);
            return c != 0 ? c : b.PrefabRefCount.CompareTo(a.PrefabRefCount);
        });
    }

    static StringBuilder BuildReportHeader(int prefabCount, int noLabel, int hasLabel, int singleRef)
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== AB 依赖冗余检测 ==========");
        sb.AppendLine($"扫描: {BundlesRoot}");
        sb.AppendLine($"Prefab: {prefabCount}  阈值: ≥{SharedThreshold}");
        sb.AppendLine($"单引用(隐式): {singleRef}");
        sb.AppendLine($"公用有Label: {hasLabel}  公用无Label: {noLabel}");
        sb.AppendLine($"Common Atlas: {CommonAtlasPath}");
        sb.AppendLine($"模块白名单: {ModuleAtlasWhitelist.Count}");
        sb.AppendLine();
        return sb;
    }

    static void AppendSharedDetails(StringBuilder sb, List<SharedEntry> sharedNoLabel)
    {
        if (sharedNoLabel == null || sharedNoLabel.Count == 0) return;
        sb.AppendLine("---------- 公用但无 Label ----------");
        foreach (var e in sharedNoLabel)
        {
            sb.AppendLine($"[{e.TypeTag}] {e.AssetPath}");
            sb.AppendLine($"    ref={e.PrefabRefCount}  bundle≈{e.BundleRefCount}");
            if (e.ReferencedByPrefabs != null && e.ReferencedByPrefabs.Count <= 6)
                sb.AppendLine($"    by: {string.Join(", ", e.ReferencedByPrefabs)}");
            sb.AppendLine();
        }
    }

    static void PrintAndSaveReport(string text, List<SharedEntry> pingList)
    {
        Debug.Log(text);
        string reportPath = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            "AB_SharedDependency_Report.txt");
        File.WriteAllText(reportPath, text, Encoding.UTF8);
        Debug.Log($"[ABDependencyChecker] 报告 → {reportPath}");

        if (pingList != null && pingList.Count > 0)
        {
            var obj = AssetDatabase.LoadMainAssetAtPath(pingList[0].AssetPath);
            if (obj != null)
            {
                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
            }
        }
    }

    static int UpdateAtlas(string atlasPath, List<string> spritePaths, bool mergeWithExisting)
    {
        if (spritePaths == null || spritePaths.Count == 0) return 0;

        var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
        if (atlas == null)
        {
            atlas = new SpriteAtlas();
            atlas.SetPackingSettings(new SpriteAtlasPackingSettings
            {
                blockOffset = 1,
                padding = 2,
                enableRotation = false,
                enableTightPacking = false
            });
            AssetDatabase.CreateAsset(atlas, atlasPath);
            atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
        }

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (mergeWithExisting)
        {
            foreach (var o in atlas.GetPackables())
                if (o != null) existing.Add(AssetDatabase.GetAssetPath(o));
        }
        else
        {
            var cur = atlas.GetPackables();
            if (cur != null && cur.Length > 0)
                SpriteAtlasExtensions.Remove(atlas, cur);
        }

        var toAdd = new List<UnityEngine.Object>();
        foreach (var path in spritePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (existing.Contains(path)) continue;
            var obj = AssetDatabase.LoadMainAssetAtPath(path);
            if (obj != null) toAdd.Add(obj);
        }
        if (toAdd.Count > 0)
        {
            SpriteAtlasExtensions.Add(atlas, toAdd.ToArray());
            EditorUtility.SetDirty(atlas);
        }
        return toAdd.Count;
    }

    static string TryMatchModule(string assetPath)
    {
        string norm = assetPath.Replace('\\', '/');
        foreach (var key in ModuleAtlasWhitelist.Keys)
        {
            if (norm.IndexOf("/" + key + "/", StringComparison.OrdinalIgnoreCase) >= 0)
                return key;
        }
        return null;
    }

    static void EnsureFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder)) return;
        folder = folder.Replace('\\', '/');
        if (AssetDatabase.IsValidFolder(folder)) return;
        string[] parts = folder.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }

    static string GetAssetBundleName(string assetPath)
    {
        var importer = AssetImporter.GetAtPath(assetPath);
        if (importer == null) return string.Empty;
        string name = importer.assetBundleName;
        if (string.IsNullOrEmpty(name)) return string.Empty;
        string variant = importer.assetBundleVariant;
        return string.IsNullOrEmpty(variant) ? name : name + "." + variant;
    }

    static bool IsSpriteLike(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".png": case ".jpg": case ".jpeg": case ".tga": case ".psd": case ".gif": case ".bmp":
                return true;
            default: return false;
        }
    }

    static bool IsTrackableDependency(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) return false;
        if (path.StartsWith("Assets/Editor/", StringComparison.OrdinalIgnoreCase)) return false;
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".cs": case ".dll": case ".asmdef": case ".unity": case ".meta": case ".prefab":
                return false;
            case ".png": case ".jpg": case ".jpeg": case ".tga": case ".psd":
            case ".ttf": case ".otf": case ".asset": case ".mat": case ".anim": case ".controller":
            case ".wav": case ".mp3": case ".ogg": case ".shader":
            case ".spriteatlas": case ".spriteatlasv2":
                return true;
            default:
                return path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
        }
    }

    class SharedEntry
    {
        public string AssetPath;
        public int PrefabRefCount;
        public int BundleRefCount;
        public string OwnLabel;
        public List<string> ReferencedByPrefabs;
        public string TypeTag
        {
            get
            {
                string ext = Path.GetExtension(AssetPath).ToLowerInvariant();
                if (ext == ".ttf" || ext == ".otf") return "Font";
                if (ext == ".spriteatlas" || ext == ".spriteatlasv2") return "Atlas";
                if (ext == ".png" || ext == ".jpg" || ext == ".tga" || ext == ".psd") return "Sprite";
                if (ext == ".anim" || ext == ".controller") return "Anim";
                if (ext == ".mat") return "Mat";
                if (ext == ".shader") return "Shader";
                return "Asset";
            }
        }
    }
}
#endif
