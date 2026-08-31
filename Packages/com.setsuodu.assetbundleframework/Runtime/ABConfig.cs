using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 运行时配置（方案 C）
/// 优先级：
///   1. 代码里手动 SetRemoteRoot / ApplyJson
///   2. 可选远端配置 URL（configRemoteUrl）
///   3. StreamingAssets/ab_config.json
///   4. 内置默认值
///
/// 本地 nginx 演示时，ab_config.json 里 remoteRoot 指向 http://127.0.0.1/AssetBundles 即可。
/// </summary>
[Serializable]
public class ABConfigData
{
    /// <summary>热更 CDN 根地址，末尾不要斜杠。例：http://127.0.0.1/AssetBundles</summary>
    public string remoteRoot = "http://127.0.0.1/AssetBundles";

    /// <summary>可选：再从该 URL 拉一份 JSON 覆盖本地（末尾不要斜杠的完整文件地址）</summary>
    public string configRemoteUrl = "";

    /// <summary>是否启用热更检查（false 则跳过下载，只用首包）</summary>
    public bool enableHotUpdate = true;

    /// <summary>下载超时秒数（预留，当前 UnityWebRequest 可按需扩展）</summary>
    public int timeoutSeconds = 30;
}

public static class ABConfig
{
    const string LocalFileName = "ab_config.json";

    static ABConfigData _data;
    static bool _loaded;

    public static ABConfigData Data
    {
        get
        {
            if (!_loaded)
                LoadLocalSync();
            return _data ?? (_data = new ABConfigData());
        }
    }

    public static string RemoteRoot
    {
        get => Data.remoteRoot;
        set
        {
            if (_data == null) _data = new ABConfigData();
            _data.remoteRoot = string.IsNullOrWhiteSpace(value)
                ? "http://127.0.0.1/AssetBundles"
                : value.TrimEnd('/');
            _loaded = true;
        }
    }

    public static bool EnableHotUpdate => Data.enableHotUpdate;

    /// <summary>同步读 StreamingAssets（Editor / 多数 Standalone 可用；Android 建议走 LoadAsync）</summary>
    public static void LoadLocalSync()
    {
        _data = new ABConfigData();
        _loaded = true;

        string path = Path.Combine(Application.streamingAssetsPath, LocalFileName);

#if UNITY_ANDROID && !UNITY_EDITOR
        // Android StreamingAssets 在 jar 内，同步 File 不可靠，留给 LoadAsync
        Debug.LogWarning("[ABConfig] Android 请使用 LoadAsync() 读取 StreamingAssets");
        return;
#else
        if (!File.Exists(path))
        {
            Debug.Log($"[ABConfig] 未找到 {path}，使用默认 remoteRoot={_data.remoteRoot}");
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            ApplyJson(json);
            Debug.Log($"[ABConfig] 已加载本地配置 remoteRoot={_data.remoteRoot}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[ABConfig] 读取本地配置失败: {e.Message}");
        }
#endif
    }

    /// <summary>
    /// 异步加载：先 StreamingAssets，若配置了 configRemoteUrl 再覆盖。
    /// 建议在 Initialize / 热更前 await 一次。
    /// </summary>
    public static async UniTask LoadAsync(CancellationToken token = default)
    {
        _data = new ABConfigData();
        _loaded = true;

        // 1) StreamingAssets
        string localUrl = Path.Combine(Application.streamingAssetsPath, LocalFileName);
#if UNITY_EDITOR || UNITY_STANDALONE
        localUrl = "file:///" + localUrl.Replace("\\", "/");
#endif
        try
        {
            using (var req = UnityWebRequest.Get(localUrl))
            {
                await req.SendWebRequest().WithCancellation(token);
                if (req.result == UnityWebRequest.Result.Success &&
                    !string.IsNullOrEmpty(req.downloadHandler.text))
                {
                    ApplyJson(req.downloadHandler.text);
                    Debug.Log($"[ABConfig] StreamingAssets 配置 OK remoteRoot={_data.remoteRoot}");
                }
                else
                {
                    Debug.Log($"[ABConfig] StreamingAssets 无配置或失败: {req.error}，用默认值");
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            Debug.LogWarning($"[ABConfig] 本地配置异常: {e.Message}");
        }

        // 2) 可选远端覆盖
        if (!string.IsNullOrWhiteSpace(_data.configRemoteUrl))
        {
            try
            {
                using (var req = UnityWebRequest.Get(_data.configRemoteUrl.Trim()))
                {
                    await req.SendWebRequest().WithCancellation(token);
                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        ApplyJson(req.downloadHandler.text);
                        Debug.Log($"[ABConfig] 远端配置覆盖 OK remoteRoot={_data.remoteRoot}");
                    }
                    else
                    {
                        Debug.LogWarning($"[ABConfig] 远端配置失败: {req.error}");
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                Debug.LogWarning($"[ABConfig] 远端配置异常: {e.Message}");
            }
        }
    }

    public static void ApplyJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        var parsed = JsonUtility.FromJson<ABConfigData>(json);
        if (parsed == null) return;

        if (_data == null) _data = new ABConfigData();

        if (!string.IsNullOrWhiteSpace(parsed.remoteRoot))
            _data.remoteRoot = parsed.remoteRoot.TrimEnd('/');
        if (parsed.configRemoteUrl != null)
            _data.configRemoteUrl = parsed.configRemoteUrl.Trim();
        _data.enableHotUpdate = parsed.enableHotUpdate;
        if (parsed.timeoutSeconds > 0)
            _data.timeoutSeconds = parsed.timeoutSeconds;

        _loaded = true;
    }

    /// <summary>测试或业务侧强制写入（不写磁盘）</summary>
    public static void SetRemoteRoot(string url)
    {
        RemoteRoot = url;
    }

#if UNITY_EDITOR
    /// <summary>Editor 下把当前 Data 写回 StreamingAssets，方便改完保存</summary>
    public static void SaveLocalToStreamingAssets()
    {
        if (_data == null) _data = new ABConfigData();
        string dir = Application.streamingAssetsPath;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, LocalFileName);
        File.WriteAllText(path, JsonUtility.ToJson(_data, true));
        UnityEditor.AssetDatabase.Refresh();
        Debug.Log($"[ABConfig] 已写入 {path}");
    }
#endif
}
