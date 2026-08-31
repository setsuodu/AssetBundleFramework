using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 热更新（UniTask 版）— 方案 C：remoteRoot 来自 ABConfig，不再依赖 Inspector 填 URL。
/// version 快速判断 + Hash 差量下载，支持 CancellationToken。
/// </summary>
public class ABUpdater : MonoBehaviour
{
    [Header("调试：勾选后忽略 ab_config，强制用下方 Override")]
    public bool useInspectorOverride;

    [Header("仅 useInspectorOverride 时生效，末尾不要斜杠")]
    public string inspectorRemoteRoot = "http://127.0.0.1/AssetBundles";

    /// <summary>实际使用的根地址</summary>
    public string EffectiveRemoteRoot =>
        useInspectorOverride && !string.IsNullOrWhiteSpace(inspectorRemoteRoot)
            ? inspectorRemoteRoot.TrimEnd('/')
            : ABConfig.RemoteRoot;

    /// <summary>
    /// 建议启动时：await ABConfig.LoadAsync(token) 后再调本方法。
    /// </summary>
    public async UniTask<bool> CheckAndUpdateAsync(
        IProgress<(float progress, string tip)> progress = null,
        CancellationToken token = default)
    {
        if (!ABConfig.EnableHotUpdate && !useInspectorOverride)
        {
            progress?.Report((1f, "热更已关闭"));
            Debug.Log("[ABUpdate] enableHotUpdate=false，跳过");
            return false;
        }

        string remoteRoot = EffectiveRemoteRoot;
        if (string.IsNullOrWhiteSpace(remoteRoot))
        {
            Debug.LogError("[ABUpdate] remoteRoot 为空，请检查 StreamingAssets/ab_config.json 或 Inspector Override");
            return false;
        }

        ABPath.EnsurePersistentDir();
        string platform = ABPath.GetPlatformName();
        string remoteVersionUrl = $"{remoteRoot}/{platform}/version.txt";
        string remoteManifestUrl = $"{remoteRoot}/{platform}/manifest.json";

        Debug.Log($"[ABUpdate] remoteRoot={remoteRoot} platform={platform}");

        // 1. 远程 version
        string remoteVersion;
        using (var req = UnityWebRequest.Get(remoteVersionUrl))
        {
            await req.SendWebRequest().WithCancellation(token);
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[ABUpdate] version 失败: {req.error} url={remoteVersionUrl}");
                return false;
            }
            remoteVersion = req.downloadHandler.text.Trim();
        }

        string localVersion = "0";
        string localManifestPath = Path.Combine(ABPath.PersistentRoot, "manifest.json");
        if (!File.Exists(localManifestPath))
            localManifestPath = Path.Combine(ABPath.StreamingRoot, "manifest.json");

        ABManifest localManifest = null;
        if (File.Exists(localManifestPath))
        {
            try
            {
                localManifest = JsonUtility.FromJson<ABManifest>(File.ReadAllText(localManifestPath));
                if (localManifest != null) localVersion = localManifest.version;
            }
            catch { /* ignore */ }
        }

        Debug.Log($"[ABUpdate] local={localVersion} remote={remoteVersion}");
        if (remoteVersion == localVersion)
        {
            progress?.Report((1f, "已是最新"));
            return false;
        }

        // 2. 远程 manifest
        ABManifest remoteManifest;
        using (var req = UnityWebRequest.Get(remoteManifestUrl))
        {
            await req.SendWebRequest().WithCancellation(token);
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[ABUpdate] manifest 失败: {req.error}");
                return false;
            }
            remoteManifest = JsonUtility.FromJson<ABManifest>(req.downloadHandler.text);
        }

        if (remoteManifest?.bundles == null) return false;

        // 3. 差量
        var need = new List<ABInfo>();
        foreach (var info in remoteManifest.bundles)
        {
            string localFile = Path.Combine(ABPath.PersistentRoot, info.hash + ".unity3d");
            string streamFile = Path.Combine(ABPath.StreamingRoot, info.hash + ".unity3d");
            if (!File.Exists(localFile) && !File.Exists(streamFile))
                need.Add(info);
        }

        if (need.Count == 0)
        {
            SaveManifest(remoteManifest);
            progress?.Report((1f, "完成"));
            return true;
        }

        Debug.Log($"[ABUpdate] 下载 {need.Count} 个");

        // 4. 下载
        for (int i = 0; i < need.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var info = need[i];
            string url = $"{remoteRoot}/{platform}/{info.hash}.unity3d";
            string save = Path.Combine(ABPath.PersistentRoot, info.hash + ".unity3d");

            progress?.Report(((float)i / need.Count, info.name));

            using (var req = UnityWebRequest.Get(url))
            {
                await req.SendWebRequest().WithCancellation(token);
                if (req.result == UnityWebRequest.Result.Success)
                    File.WriteAllBytes(save, req.downloadHandler.data);
                else
                    Debug.LogError($"[ABUpdate] 下载失败 {info.name}: {req.error}");
            }
        }

        SaveManifest(remoteManifest);
        progress?.Report((1f, "更新完成"));
        return true;
    }

    void SaveManifest(ABManifest manifest)
    {
        ABPath.EnsurePersistentDir();
        File.WriteAllText(Path.Combine(ABPath.PersistentRoot, "manifest.json"),
            JsonUtility.ToJson(manifest, true));
        File.WriteAllText(Path.Combine(ABPath.PersistentRoot, "version.txt"), manifest.version);
    }
}
