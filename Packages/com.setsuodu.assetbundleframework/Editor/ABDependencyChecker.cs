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
/// 依赖冗余检测 + AutoFix Atlas + 给 Bundles 外（含 Art）多引用资源打共享 Label
/// 不搬文件、不 Copy
/// </summary>
public static class ABDependencyChecker
{
    const string MenuRoot = "Tools/AssetBundle/";
    const string BundlesRoot = "Assets/Bundles";

    public static int SharedThreshold = 2;
    public const string CommonAtlasPath = "Assets/Bundles/Sprites/Atlas_UI_Common.spriteatlas";

    /// <summary>路径关键字 → 共享 AB 名（命中第一个）</summary>
    public static readonly (string pathContains, string bundleName)[] SharedLabelRules =
    {
        ("/Characters/ChibiGirls/", "characters/chibi_base"),
        ("/Props/Fast Food/", "props/fastfood_common"),
        ("/UI/Animations/Common/", "ui/anim_common"),
        ("/UI/Sprites/Common/", "ui/sprites_common"),
        ("/Bundles/Fonts/", "fonts/common"),
    };

    /// <summary>不打 AB Label（官方包 / 应交）</summary>
    public static readonly string[] LabelBlacklistSubstrings =
    {
        "/TextMesh Pro/",
        "/TextMeshPro/",
        "PackageCache",
        "/Editor/",
    };

    public static readonly Dictionary<string, string> ModuleAtlasWhitelist =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // { "Shop", "Assets/Bundles/Sprites/Atlas_UI_Shop.spriteatlas" },
        };

    // ========== 菜单 priority: 0 段检测 / 50 段修复 / 100 段由 ABBuilder 打包 ==========

    [MenuItem(MenuRoot + "Check Shared Dependencies", false, 0)]
    public static void MenuCheckOnly()
    {
        Analyze(out var sharedNoLabel, out var sharedHasLabel, out var singleRef, out var prefabCount);
        var sb = BuildReportHeader(prefabCount, sharedNoLabel.Count, sharedHasLabel.Count, singleRef);
        AppendSharedDetails(sb, sharedNoLabel);
        PrintAndSaveReport(sb.ToString(), sharedNoLabel);
    }

    [MenuItem(MenuRoot + "AutoFix Shared → Common Atlas", false, 50)]
    public static void MenuAutoFix()
    {
        AutoFixShared(true);
    }

    [MenuItem(MenuRoot + "Set Shared Labels (Art 依赖)", false, 51)]
    public static void MenuSetSharedLabels()
    {
        int n = SetSharedLabelsFromDependencies();
        EditorUtility.DisplayDialog("Set Shared Labels",
            $"已为 {n} 个多引用依赖设置共享 Label（含 Art，不搬文件）。\n详见 Console。", "OK");
    }

    // -------------------------------------------------------------------------
    // 对外 API
    // -------------------------------------------------------------------------

    public static void AutoFixShared(bool showDialog = false)
    {
        Analyze(out var sharedNoLabel, out _, out _, out var prefabCount);

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

        var sb = BuildReportHeader(prefabCount, sharedNoLabel.Count, 0, 0);
        sb.AppendLine($"[AutoFix] Common Atlas +{addedCommon}");
        sb.AppendLine($"[AutoFix] 模块 Atlas +{addedModule}");
        sb.AppendLine($"[AutoFix] 非图公用仍需 Shared Label: {otherShared.Count}");
        PrintAndSaveReport(sb.ToString(), null);

        if (showDialog)
            EditorUtility.DisplayDialog("AutoFix",
                $"Atlas Common +{addedCommon}, 模块 +{addedModule}\n非图: {otherShared.Count}", "OK");
    }

    /// <summary>
    /// 给多引用且无 Label 的依赖打共享 AB 名（含 Assets/Art/...）
    /// 不 Move / Copy。返回成功设置数量。
    /// </summary>
    public static int SetSharedLabelsFromDependencies()
    {
        Analyze(out var sharedNoLabel, out _, out _, out _);

        int setCount = 0;
        var log = new StringBuilder();
        log.AppendLine("========== Set Shared Labels (依赖) ==========");

        foreach (var e in sharedNoLabel)
        {
            if (IsBlacklisted(e.AssetPath))
            {
                log.AppendLine($"[跳过黑名单] {e.AssetPath}");
                continue;
            }

            // 已在 SpriteAtlas 内的碎图可跳过单独 Label（由 Atlas 进包）
            if (IsSpriteLike(e.AssetPath) && IsInAnyAtlas(e.AssetPath))
            {
                log.AppendLine($"[跳过已在Atlas] {e.AssetPath}");
                continue;
            }

            string bundleName = ResolveSharedBundleName(e);
            if (string.IsNullOrEmpty(bundleName))
            {
                log.AppendLine($"[无规则] {e.AssetPath} ref={e.PrefabRefCount}");
                continue;
            }

            var importer = AssetImporter.GetAtPath(e.AssetPath);
            if (importer == null)
                continue;

            if (importer.assetBundleName != bundleName || !string.IsNullOrEmpty(importer.assetBundleVariant))
            {
                // 正确顺序：先名称，后 variant（使用官方推荐方法）
                importer.SetAssetBundleNameAndVariant(bundleName, string.Empty);
                setCount++;
                log.AppendLine($"[OK] {bundleName} ← {e.AssetPath}");
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        log.AppendLine($"合计设置: {setCount}");
        Debug.Log(log.ToString());
        return setCount;
    }

    // -------------------------------------------------------------------------
    // 分析
    // -------------------------------------------------------------------------

    public static void Analyze(
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
            Debug.LogError($"[ABDependencyChecker] 缺少: {BundlesRoot}");
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
                EditorUtility.DisplayProgressBar("扫描依赖", prefabPath,
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

    static string ResolveSharedBundleName(SharedEntry e)
    {
        string norm = e.AssetPath.Replace('\\', '/');
        foreach (var rule in SharedLabelRules)
        {
            if (norm.IndexOf(rule.pathContains, StringComparison.OrdinalIgnoreCase) >= 0)
                return rule.bundleName;
        }

        // 兜底：按类型
        switch (e.TypeTag)
        {
            case "Shader": return "shared/shaders";
            case "Font": return "fonts/common";
            case "Mat": return "shared/materials";
            case "Anim": return "shared/anims";
            case "Sprite": return null; // 优先 Atlas，无规则则不单打
            default:
                // FBX 等：按 Art 下第一级模块
                if (norm.StartsWith("Assets/Art/", StringComparison.OrdinalIgnoreCase))
                {
                    string rest = norm.Substring("Assets/Art/".Length);
                    int slash = rest.IndexOf('/');
                    string mod = slash > 0 ? rest.Substring(0, slash) : rest;
                    return ("shared/" + mod).ToLowerInvariant();
                }
                return "shared/misc";
        }
    }

    static bool IsBlacklisted(string path)
    {
        string norm = path.Replace('\\', '/');
        foreach (var s in LabelBlacklistSubstrings)
        {
            if (norm.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    static bool IsInAnyAtlas(string spritePath)
    {
        string[] guids = AssetDatabase.FindAssets("t:SpriteAtlas");
        foreach (var g in guids)
        {
            string atlasPath = AssetDatabase.GUIDToAssetPath(g);
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            if (atlas == null) continue;
            foreach (var o in atlas.GetPackables())
            {
                if (o == null) continue;
                if (string.Equals(AssetDatabase.GetAssetPath(o), spritePath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    static StringBuilder BuildReportHeader(int prefabCount, int noLabel, int hasLabel, int singleRef)
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== AB 依赖冗余检测 ==========");
        sb.AppendLine($"扫描: {BundlesRoot}");
        sb.AppendLine($"Prefab: {prefabCount}  阈值: ≥{SharedThreshold}");
        sb.AppendLine($"单引用: {singleRef}  公用有Label: {hasLabel}  公用无Label: {noLabel}");
        sb.AppendLine();
        return sb;
    }

    static void AppendSharedDetails(StringBuilder sb, List<SharedEntry> list)
    {
        if (list == null || list.Count == 0) return;
        sb.AppendLine("---------- 公用无 Label ----------");
        foreach (var e in list)
        {
            sb.AppendLine($"[{e.TypeTag}] {e.AssetPath}");
            sb.AppendLine($"    ref={e.PrefabRefCount}");
        }
    }

    static void PrintAndSaveReport(string text, List<SharedEntry> pingList)
    {
        Debug.Log(text);
        string reportPath = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            "AB_SharedDependency_Report.txt");
        File.WriteAllText(reportPath, text, Encoding.UTF8);

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

    static int UpdateAtlas(string atlasPath, List<string> spritePaths, bool merge)
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
        if (merge)
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
            case ".overrideController":
            case ".wav": case ".mp3": case ".ogg": case ".shader": case ".fbx":
            case ".spriteatlas": case ".spriteatlasv2":
                return true;
            default:
                return path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
        }
    }

    public class SharedEntry
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
                if (ext == ".anim" || ext == ".controller" || ext == ".overrideController") return "Anim";
                if (ext == ".mat") return "Mat";
                if (ext == ".shader") return "Shader";
                if (ext == ".fbx") return "FBX";
                return "Asset";
            }
        }
    }
}
#endif
