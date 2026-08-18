using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 热更新（UniTask 版）
/// version 快速判断 + Hash 差量下载，支持 CancellationToken
/// </summary>
public class ABUpdater : MonoBehaviour
{
    [Header("远程根地址，末尾不要斜杠")]
    public string remoteRoot = "https://your-cdn.com/AssetBundles";

    public async UniTask<bool> CheckAndUpdateAsync(
        IProgress<(float progress, string tip)> progress = null,
        CancellationToken token = default)
    {
        ABPath.EnsurePersistentDir();
        string platform = ABPath.GetPlatformName();
        string remoteVersionUrl = $"{remoteRoot}/{platform}/version.txt";
        string remoteManifestUrl = $"{remoteRoot}/{platform}/manifest.json";

        // 1. 远程 version
        string remoteVersion;
        using (var req = UnityWebRequest.Get(remoteVersionUrl))
        {
            await req.SendWebRequest().WithCancellation(token);
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[ABUpdate] version 失败: {req.error}");
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
            catch { }
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
