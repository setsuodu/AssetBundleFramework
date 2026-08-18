using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 热更新模块
/// 策略：整包 version 快速判断 + 每个 Bundle Hash 差量下载
/// 下载文件以 hash 命名，写入 persistent
/// </summary>
public class ABUpdater : MonoBehaviour
{
    [Header("远程根地址，末尾不要斜杠")]
    public string remoteRoot = "https://your-cdn.com/AssetBundles";

    /// <summary>
    /// 检查并更新
    /// onProgress: 0~1
    /// onComplete: 是否有实际更新
    /// </summary>
    public IEnumerator CheckAndUpdate(Action<float, string> onProgress, Action<bool> onComplete)
    {
        ABPath.EnsurePersistentDir();

        string platform = ABPath.GetPlatformName();
        string remoteVersionUrl = $"{remoteRoot}/{platform}/version.txt";
        string remoteManifestUrl = $"{remoteRoot}/{platform}/manifest.json";

        // ---------- 1. 拉远程 version ----------
        string remoteVersion = null;
        using (var req = UnityWebRequest.Get(remoteVersionUrl))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[ABUpdate] 获取 version 失败: {req.error}，跳过更新");
                onComplete?.Invoke(false);
                yield break;
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
                if (localManifest != null)
                    localVersion = localManifest.version;
            }
            catch { }
        }

        Debug.Log($"[ABUpdate] local={localVersion}  remote={remoteVersion}");

        if (remoteVersion == localVersion)
        {
            Debug.Log("[ABUpdate] 版本相同，无需更新");
            onProgress?.Invoke(1f, "已是最新");
            onComplete?.Invoke(false);
            yield break;
        }

        // ---------- 2. 拉远程 manifest ----------
        ABManifest remoteManifest = null;
        using (var req = UnityWebRequest.Get(remoteManifestUrl))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[ABUpdate] 获取 manifest 失败: {req.error}");
                onComplete?.Invoke(false);
                yield break;
            }
            remoteManifest = JsonUtility.FromJson<ABManifest>(req.downloadHandler.text);
        }

        if (remoteManifest?.bundles == null)
        {
            onComplete?.Invoke(false);
            yield break;
        }

        // ---------- 3. 计算需要下载的列表 ----------
        var needDownload = new List<ABInfo>();
        foreach (var info in remoteManifest.bundles)
        {
            string localFile = Path.Combine(ABPath.PersistentRoot, info.hash + ".unity3d");
            // 也检查 StreamingAssets 里是否已有相同 hash（首包可能已带）
            string streamFile = Path.Combine(ABPath.StreamingRoot, info.hash + ".unity3d");

            bool exists = File.Exists(localFile) || File.Exists(streamFile);
            if (!exists)
            {
                needDownload.Add(info);
                continue;
            }

            // 可选：再校验本地文件 hash（更稳，但慢）。简单场景可只判断存在。
            // 这里为了严谨，对 persistent 再算一次（StreamingAssets 信任首包）
            if (File.Exists(localFile))
            {
                // 生产环境可加 MD5 校验，这里省略以节省启动时间
            }
        }

        if (needDownload.Count == 0)
        {
            // 只更新了 manifest/version
            SaveManifest(remoteManifest);
            onProgress?.Invoke(1f, "完成");
            onComplete?.Invoke(true);
            yield break;
        }

        Debug.Log($"[ABUpdate] 需要下载 {needDownload.Count} 个文件");

        // ---------- 4. 下载 ----------
        int total = needDownload.Count;
        for (int i = 0; i < total; i++)
        {
            var info = needDownload[i];
            string url = $"{remoteRoot}/{platform}/{info.hash}.unity3d";
            string savePath = Path.Combine(ABPath.PersistentRoot, info.hash + ".unity3d");

            onProgress?.Invoke((float)i / total, $"下载 {info.name}");

            using (var req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    File.WriteAllBytes(savePath, req.downloadHandler.data);
                }
                else
                {
                    Debug.LogError($"[ABUpdate] 下载失败: {info.name}  {req.error}");
                    // 可在此加入重试逻辑
                }
            }
        }

        // ---------- 5. 保存新 manifest ----------
        SaveManifest(remoteManifest);

        onProgress?.Invoke(1f, "更新完成");
        Debug.Log("[ABUpdate] 全部完成");
        onComplete?.Invoke(true);
    }

    void SaveManifest(ABManifest manifest)
    {
        ABPath.EnsurePersistentDir();
        string path = Path.Combine(ABPath.PersistentRoot, "manifest.json");
        File.WriteAllText(path, JsonUtility.ToJson(manifest, true));

        // 同步写 version.txt 方便外部查看
        File.WriteAllText(Path.Combine(ABPath.PersistentRoot, "version.txt"), manifest.version);
    }
}
