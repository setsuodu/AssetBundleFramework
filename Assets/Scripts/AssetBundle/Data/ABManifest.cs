using System;
using System.Collections.Generic;

/// <summary>
/// 单个 AssetBundle 信息
/// </summary>
[Serializable]
public class ABInfo
{
    public string name;          // 逻辑名，例如 prefabs/player.unity3d（小写）
    public string hash;          // 文件内容 MD5 / CRC
    public long size;            // 字节大小（可选，用于进度）
    public string[] depends;     // 依赖的 bundle 名列表

    public ABInfo()
    {
        name = string.Empty;
        hash = string.Empty;
        size = 0;
        depends = Array.Empty<string>();
    }
}

/// <summary>
/// 完整资源清单
/// </summary>
[Serializable]
public class ABManifest
{
    public string version;                   // 整包版本号，例如 20260818.1
    public List<ABInfo> bundles;             // 所有 bundle 信息

    public ABManifest()
    {
        version = "0";
        bundles = new List<ABInfo>();
    }

    /// <summary>
    /// 按名字快速查找
    /// </summary>
    public ABInfo Get(string bundleName)
    {
        if (bundles == null) return null;
        string key = bundleName.ToLowerInvariant();
        for (int i = 0; i < bundles.Count; i++)
        {
            if (bundles[i].name == key)
                return bundles[i];
        }
        return null;
    }
}
