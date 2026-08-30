#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TMPro;

/// <summary>
/// 【Issue #3】打包前自动检测并切断 TMP Font Asset 的双向/环形 Fallback，
/// 避免跨 Bundle 互相依赖导致 manifest 死锁。
/// 推荐配合 PathToBundleName 把 Fonts 全部打进 fonts/common 一起使用。
/// </summary>
public static class TMPFallbackValidator
{
    const string MenuRoot = "Tools/AssetBundle/";

    /// <summary>
    /// 自动清理所有 TMP 字体中的双向/环形 Fallback 依赖
    /// </summary>
    [MenuItem(MenuRoot + "自动修复 TMP 字体循环依赖", false, 54)]
    public static void AutoFixTMPCircularFallbacks()
    {
        // 优先扫 Bundles/Fonts，兼容历史 Assets/Fonts
        string[] searchRoots = { "Assets/Bundles/Fonts", "Assets/Fonts" };
        var fonts = new List<TMP_FontAsset>();
        var seen = new HashSet<string>();

        foreach (var root in searchRoots)
        {
            if (!AssetDatabase.IsValidFolder(root)) continue;
            string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { root });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!seen.Add(path)) continue;
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font != null) fonts.Add(font);
            }
        }

        // 兜底：全工程 TMP FontAsset
        if (fonts.Count == 0)
        {
            string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset");
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/TextMesh Pro/") || path.Contains("/TextMeshPro/")) continue;
                if (!seen.Add(path)) continue;
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font != null) fonts.Add(font);
            }
        }

        int fixedCount = 0;

        foreach (var fontA in fonts)
        {
            if (fontA.fallbackFontAssetTable == null) continue;

            for (int i = fontA.fallbackFontAssetTable.Count - 1; i >= 0; i--)
            {
                var fontB = fontA.fallbackFontAssetTable[i];
                if (fontB == null) continue;

                // 检测 fontB 的 Fallback 中是否包含 fontA（构成双向环）
                if (fontB.fallbackFontAssetTable != null && fontB.fallbackFontAssetTable.Contains(fontA))
                {
                    Debug.LogWarning(
                        $"[AB Build Safeguard] 检测到 TMP 循环引用: {fontA.name} <---> {fontB.name}！" +
                        $"自动移除 {fontB.name} 对 {fontA.name} 的反向引用。");

                    fontB.fallbackFontAssetTable.Remove(fontA);
                    EditorUtility.SetDirty(fontB);
                    fixedCount++;
                }
            }
        }

        if (fixedCount > 0)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"<color=green>[TMPFallbackValidator] 依赖检查完成，共修复 {fixedCount} 处循环引用环。</color>");
    }
}
#endif
