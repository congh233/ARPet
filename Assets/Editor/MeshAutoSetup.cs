using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// 一次性自动修复：确保场景里的 ARMeshManager 挂在 XR Origin 的子物体上。
/// 修好后写标记文件，之后不再插手（用户可以通过菜单自己开关）。
/// </summary>
[InitializeOnLoad]
static class MeshAutoSetup
{
    const string kScenePath = "Assets/Scenes/ARPlacement.unity";
    const string kMaterialPath = "Assets/Materials/AR_MeshVisual.mat";
    const string kPrefabPath = "Assets/Prefabs/ARMeshVisual.prefab";

    static string MarkerPath => System.IO.Path.Combine(Application.dataPath, "../ProjectSettings/MeshAutoSetup.done");
    static readonly double s_StartTime = EditorApplication.timeSinceStartup;

    static MeshAutoSetup()
    {
        if (System.IO.File.Exists(MarkerPath))
            return;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (System.IO.File.Exists(MarkerPath))
        {
            EditorApplication.update -= Tick;
            return;
        }

        if (EditorApplication.timeSinceStartup - s_StartTime > 600)
        {
            EditorApplication.update -= Tick;
            Debug.LogWarning("[Mesh] 自动修复等待超时（场景没打开？），可手动点 Tools > 开启网格可视化");
            return;
        }

        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;

        var active = EditorSceneManager.GetActiveScene();
        if (active.path != kScenePath)
            return;

        var origin = Object.FindObjectOfType<XROrigin>();
        if (origin == null)
            return;

        var changed = false;

        // 删掉位置不对的旧组件（ARMeshManager 必须在 XR Origin 的子孙节点上）
        foreach (var manager in Object.FindObjectsOfType<ARMeshManager>(true))
        {
            var parent = manager.transform.parent;
            var valid = parent != null && parent.GetComponentInParent<XROrigin>(true) != null;
            if (valid)
                continue;

            var go = manager.gameObject;
            Object.DestroyImmediate(manager);
            changed = true;
            if (go != null && go.name == "Mesh Manager" && go.GetComponents<Component>().Length <= 1)
                Object.DestroyImmediate(go);
        }

        var validManager = origin.GetComponentInChildren<ARMeshManager>(true);
        if (validManager == null)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(kMaterialPath);
            if (material == null)
                material = MeshVisualizationMenu.EnsureMaterial();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(kPrefabPath);
            if (prefab == null)
                prefab = MeshVisualizationMenu.EnsurePrefab(material);

            var meshFilter = prefab.GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                Debug.LogError("[Mesh] 网格可视化预制体缺少 MeshFilter");
                return;
            }

            var managerGo = new GameObject("Mesh Manager");
            managerGo.transform.SetParent(origin.transform, false);
            managerGo.transform.localPosition = Vector3.zero;
            managerGo.transform.localScale = Vector3.one * 10f;
            var manager = managerGo.AddComponent<ARMeshManager>();
            manager.meshPrefab = meshFilter;
            manager.density = 0.5f;
            changed = true;
        }

        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(active);
            EditorSceneManager.SaveScene(active);
        }

        WriteMarker();
        Debug.Log("[Mesh] 自动修复完成：Mesh Manager 已挂在 XR Origin 下，按 Play 查看网格");
    }

    static void WriteMarker()
    {
        try
        {
            System.IO.File.WriteAllText(MarkerPath, System.DateTime.Now.ToString("u"));
        }
        catch
        {
            // 忽略
        }
    }
}
