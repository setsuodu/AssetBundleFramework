using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CopyTransformPath
{
    [MenuItem("GameObject/Copy Find Path %#&c", false, 10)]
    public static void CopyFindPath()
    {
        Transform selected = Selection.activeTransform;
        if (selected == null) return;

        // 寻找界面的根节点：优先查找挂载了 UIBase 的节点，其次找 Prefab 根节点或 Canvas 下的第一层节点
        Transform panelRoot = FindPanelRoot(selected);

        // 如果选中的就是面板根节点本身，或者找不到根节点
        if (panelRoot == null || selected == panelRoot)
        {
            GUIUtility.systemCopyBuffer = "";
            Debug.Log($"[CopyPath] 当前选中的是面板根节点，路径复制为空");
            return;
        }

        // 向上拼接路径，直到 panelRoot 截止（不包含 panelRoot 自己的名字）
        string path = selected.name;
        Transform current = selected.parent;

        while (current != null && current != panelRoot)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        GUIUtility.systemCopyBuffer = path;
        Debug.Log($"[CopyPath] 已复制相对路径: \"{path}\"");
    }

    private static Transform FindPanelRoot(Transform current)
    {
        // 1. 如果是在 Prefab 编辑模式下，根节点就是 Prefab 场景的根
        var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (prefabStage != null)
        {
            return prefabStage.prefabContentsRoot.transform;
        }

        // 2. 向上查找最近的挂载了 UIBase 的节点
        var uiBase = current.GetComponentInParent<UIBase>();
        if (uiBase != null)
        {
            return uiBase.transform;
        }

        // 3. 兜底逻辑：如果没挂脚本，找到 Canvas 下的第一级节点（如 UI_Login）
        Transform target = current;
        while (target.parent != null && target.parent.GetComponent<Canvas>() == null)
        {
            target = target.parent;
        }
        return target;
    }

    [MenuItem("GameObject/Copy Find Path %#&c", true)]
    public static bool ValidateCopyFindPath()
    {
        return Selection.activeTransform != null;
    }
}