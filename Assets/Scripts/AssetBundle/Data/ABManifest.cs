using System;
using System.Collections.Generic;

/// <summary>
/// 单个 AssetBundle 信息
/// </summary>
[Serializable]
public class ABInfo
{
    public string name;          // 逻辑名（小写）
    public string hash;          // 内容 MD5
    public long size;
    public string[] depends;     // 依赖列表

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
    public string version;
    public List<ABInfo> bundles;

    public ABManifest()
    {
        version = "0";
        bundles = new List<ABInfo>();
    }

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
