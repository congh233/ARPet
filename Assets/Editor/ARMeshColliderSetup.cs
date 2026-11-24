using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// 一次性迁移：给网格预制体加 MeshCollider + ARMesh 层，修复场景引用，
/// 并把 PlaceOnPlane 的网格吸附相关新字段设为默认值。
/// 完成后写标记文件，之后不再执行。
/// </summary>
[InitializeOnLoad]
static class ARMeshColliderSetup
{
    const string kScenePath = "Assets/Scenes/ARPlacement.unity";
    static string MarkerPath => System.IO.Path.Combine(Application.dataPath, "../ProjectSettings/ARMeshColliderSetup.v2.done");
    static readonly double s_StartTime = EditorApplication.timeSinceStartup;

    static ARMeshColliderSetup()
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
            Debug.LogWarning("[Mesh] 网格预制体升级等待超时；可手动点 Tools > 升级网格预制体（MeshCollider + Layer）");
            return;
        }

        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;

        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != kScenePath)
            return;

        if (!MeshVisualizationMenu.UpgradeMeshPrefab())
            return; // 预制体还没导入好，下一帧再试

        MeshVisualizationMenu.FixSceneMeshPrefabReferences();

        var placer = Object.FindObjectOfType<PlaceOnPlane>();
        if (placer != null)
        {
            var so = new SerializedObject(placer);
            SetBool(so, "useMeshSnapping", true);
            SetBool(so, "strictMeshMode", true);
            SetBool(so, "planeFallbackWhileMeshPending", true);
            SetBool(so, "showContactMarker", true);

            var distance = so.FindProperty("meshRayDistance");
            if (distance != null && distance.floatValue <= 0f)
                distance.floatValue = 20f;

            var minNormal = so.FindProperty("minSurfaceNormalY");
            if (minNormal != null && minNormal.floatValue <= 0f)
                minNormal.floatValue = 0.6f;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        WriteMarker();
        EditorApplication.update -= Tick;
        Debug.Log("[Mesh] 网格预制体升级完成（MeshCollider + Layer），点 Play 后放球会优先贴合可见表面");
    }

    static void SetBool(SerializedObject so, string propertyName, bool value)
    {
        var property = so.FindProperty(propertyName);
        if (property != null && !property.boolValue)
            property.boolValue = value;
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
