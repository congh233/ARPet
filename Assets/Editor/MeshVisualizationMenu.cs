using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// 菜单：Tools > 开启/关闭网格可视化。
/// 开启后，Play 模式里会用蓝色半透明网格把环境表面"糊"出来，比平面色块更贴合真实物体。
/// 网格预制体带 MeshCollider + 独立 Layer，供放球时做"贴合表面"的射线吸附。
/// </summary>
static class MeshVisualizationMenu
{
    const string kMaterialPath = "Assets/Materials/AR_MeshVisual.mat";
    const string kPrefabPath = "Assets/Prefabs/ARMeshVisual.prefab";

    [MenuItem("Tools/开启网格可视化")]
    static void EnableMeshVisualization()
    {
        if (EditorApplication.isPlaying)
        {
            if (EditorUtility.DisplayDialog(
                    "网格可视化需要先停止 Play",
                    "现在正在 Play 模式。要停止 Play 并马上开启网格可视化吗？",
                    "停止并开启",
                    "取消"))
            {
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += RetryAfterPlayStops;
            }

            return;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += RetryAfterPlayStops;
            return;
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(kMaterialPath);
        if (material == null)
            material = EnsureMaterial();

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(kPrefabPath);
        if (prefab == null)
            prefab = EnsurePrefab(material);
        else if (prefab.GetComponent<MeshCollider>() == null)
        {
            UpgradeMeshPrefab();
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(kPrefabPath);
        }

        var meshFilter = prefab != null ? prefab.GetComponent<MeshFilter>() : null;
        if (meshFilter == null)
        {
            Debug.LogError("[Mesh] 预制体缺少 MeshFilter");
            return;
        }

        var origin = Object.FindObjectOfType<XROrigin>();
        if (origin == null)
        {
            Debug.LogError("[Mesh] 场景里没找到 XR Origin");
            return;
        }

        // 清掉之前挂错位置的旧组件（包括挂在 XR Origin 身上的）
        foreach (var existing in Object.FindObjectsOfType<ARMeshManager>(true))
        {
            var go = existing.gameObject;
            Object.DestroyImmediate(existing);
            if (go != null && go.name == "Mesh Manager" && go.GetComponents<Component>().Length <= 1)
                Object.DestroyImmediate(go);
        }

        // ARMeshManager 必须是 XR Origin 的子物体，单独建一个子节点挂它
        var managerGo = new GameObject("Mesh Manager");
        managerGo.transform.SetParent(origin.transform, false);
        managerGo.transform.localPosition = Vector3.zero;
        managerGo.transform.localScale = Vector3.one * 10f;
        var manager = managerGo.AddComponent<ARMeshManager>();
        manager.meshPrefab = meshFilter;
        manager.density = 0.5f;

        // 网格模式下把黄色平面色块藏起来，免得画面太乱（平面检测和点击放球仍然有效）
        var planeManager = Object.FindObjectOfType<ARPlaneManager>();
        if (planeManager != null)
            planeManager.planePrefab = null;

        var scene = origin.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Mesh] 已开启网格可视化：蓝色半透明网格会贴合墙面和家具（只在 Play 模式显示）");
        EditorUtility.DisplayDialog("网格可视化已开启", "现在按 Play 就能看到蓝色半透明网格。\n想关闭：Tools > 关闭网格可视化。", "好");
    }

    static void RetryAfterPlayStops()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += RetryAfterPlayStops;
            return;
        }

        EnableMeshVisualization();
    }

    [MenuItem("Tools/关闭网格可视化")]
    static void DisableMeshVisualization()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[Mesh] 先停止 Play 再执行");
            return;
        }

        var managers = Object.FindObjectsOfType<ARMeshManager>(true);
        if (managers.Length == 0)
        {
            Debug.Log("[Mesh] 场景里没有 AR Mesh Manager，无需关闭");
            return;
        }

        var scene = managers[0].gameObject.scene;
        foreach (var manager in managers)
        {
            var go = manager.gameObject;
            Object.DestroyImmediate(manager);
            if (go != null && go.name == "Mesh Manager" && go.GetComponents<Component>().Length <= 1)
                Object.DestroyImmediate(go);
        }

        // 把黄色平面色块显示回来
        var planeManager = Object.FindObjectOfType<ARPlaneManager>();
        if (planeManager != null && planeManager.planePrefab == null)
        {
            var planePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/ARPlane.prefab");
            if (planePrefab != null)
                planeManager.planePrefab = planePrefab;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Mesh] 已关闭网格可视化");
    }

    [MenuItem("Tools/升级网格预制体（MeshCollider + Layer）")]
    static void UpgradeMeshPrefabMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[Mesh] 先停止 Play 再执行");
            return;
        }

        if (!UpgradeMeshPrefab())
        {
            Debug.LogError("[Mesh] 找不到 Assets/Prefabs/ARMeshVisual.prefab，请先开启网格可视化");
            return;
        }

        FixSceneMeshPrefabReferences();
        Debug.Log("[Mesh] 网格预制体已升级：MeshCollider + 独立 Layer。");
    }

    internal static Material EnsureMaterial()
    {
        EnsureFolder("Assets/Materials");
        var mat = new Material(Shader.Find("Standard"));
        mat.SetFloat("_Mode", 3f); // Transparent
        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)RenderQueue.Transparent;
        mat.color = new Color(0.15f, 0.55f, 1f, 0.25f);
        AssetDatabase.CreateAsset(mat, kMaterialPath);
        AssetDatabase.SaveAssets();
        return mat;
    }

    internal static GameObject EnsurePrefab(Material material)
    {
        EnsureFolder("Assets/Prefabs");
        var layer = EnsureMeshLayer();

        var go = new GameObject("ARMeshVisual");
        go.layer = layer;
        go.AddComponent<MeshFilter>();
        var collider = go.AddComponent<MeshCollider>();
        collider.convex = false;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, kPrefabPath);
        Object.DestroyImmediate(go);
        return prefab;
    }

    /// <summary>确保网格预制体带 MeshCollider，并放在 ARMesh 独立 Layer 上。</summary>
    internal static bool UpgradeMeshPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(kPrefabPath) == null)
            return false;

        var layer = EnsureMeshLayer();
        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(kPrefabPath);
            if (root == null)
                return false;

            root.layer = layer;

            if (root.GetComponent<MeshFilter>() == null)
                root.AddComponent<MeshFilter>();

            var collider = root.GetComponent<MeshCollider>();
            if (collider == null)
                collider = root.AddComponent<MeshCollider>();

            collider.convex = false;
            collider.enabled = true;

            PrefabUtility.SaveAsPrefabAsset(root, kPrefabPath);
        }
        finally
        {
            if (root != null)
                PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        return true;
    }

    /// <summary>把场景里 ARMeshManager 的 meshPrefab 引用重新指向升级后的 MeshFilter。</summary>
    internal static void FixSceneMeshPrefabReferences()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(kPrefabPath);
        var meshFilter = prefab != null ? prefab.GetComponent<MeshFilter>() : null;
        if (meshFilter == null)
            return;

        foreach (var manager in Object.FindObjectsOfType<ARMeshManager>(true))
        {
            if (manager.meshPrefab == meshFilter)
                continue;

            var scene = manager.gameObject.scene;
            manager.meshPrefab = meshFilter;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Mesh] 已修复场景里的 meshPrefab 引用");
        }
    }

    /// <summary>写入/查找 "ARMesh" 层；优先 Layer 8，被占用时找第一个空层。</summary>
    internal static int EnsureMeshLayer()
    {
        var tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (tagManagerAssets == null || tagManagerAssets.Length == 0)
            return PlacementLayers.MeshLayerIndex;

        var so = new SerializedObject(tagManagerAssets[0]);
        var layers = so.FindProperty("layers");
        if (layers == null)
            return PlacementLayers.MeshLayerIndex;

        for (var i = 8; i < layers.arraySize; i++)
        {
            if (layers.GetArrayElementAtIndex(i).stringValue == PlacementLayers.MeshLayerName)
                return i;
        }

        var target = -1;
        for (var i = PlacementLayers.MeshLayerIndex; i < layers.arraySize; i++)
        {
            if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
            {
                target = i;
                break;
            }
        }

        if (target < 0)
            return PlacementLayers.MeshLayerIndex;

        layers.GetArrayElementAtIndex(target).stringValue = PlacementLayers.MeshLayerName;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(tagManagerAssets[0]);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Mesh] 已把 Layer {target} 命名为 {PlacementLayers.MeshLayerName}");
        return target;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        var name = System.IO.Path.GetFileName(path);
        AssetDatabase.CreateFolder(parent, name);
    }
}
