using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// 一次性清理：已经开了网格可视化时，把黄色平面色块藏起来，避免两种可视化叠在一起。
/// 平面检测与点击放球不受影响。
/// </summary>
[InitializeOnLoad]
static class HidePlaneVisualsOnce
{
    const string kScenePath = "Assets/Scenes/ARPlacement.unity";
    static string MarkerPath => System.IO.Path.Combine(Application.dataPath, "../ProjectSettings/HidePlaneVisuals.done");
    static readonly double s_StartTime = EditorApplication.timeSinceStartup;

    static HidePlaneVisualsOnce()
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
            return;
        }

        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;

        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != kScenePath)
            return;

        // 只有已经开启网格可视化时才处理
        if (Object.FindObjectOfType<ARMeshManager>() == null)
            return;

        var planeManager = Object.FindObjectOfType<ARPlaneManager>();
        if (planeManager != null && planeManager.planePrefab != null)
        {
            planeManager.planePrefab = null;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Mesh] 网格模式下已隐藏黄色平面色块（点击放球仍然有效）");
        }

        WriteMarker();
        EditorApplication.update -= Tick;
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
