using UnityEngine;

/// <summary>
/// AssetBundle 引用计数包装
/// 只有 RefCount 归零才允许真正 Unload
/// </summary>
public class ABRef
{
    public string Name { get; private set; }
    public AssetBundle Bundle { get; private set; }
    public int RefCount { get; private set; }

    public ABRef(string name, AssetBundle bundle)
    {
        Name = name;
        Bundle = bundle;
        RefCount = 0;
    }

    public void Retain()
    {
        RefCount++;
    }

    public void Release()
    {
        RefCount--;
        if (RefCount < 0)
        {
            Debug.LogError($"[AB] RefCount 异常: {Name} → {RefCount}");
            RefCount = 0;
        }
    }

    public bool CanUnload => RefCount <= 0;
}
