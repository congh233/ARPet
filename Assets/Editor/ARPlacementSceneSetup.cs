using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// 一键生成 AR 放置示例场景（也可以在菜单 Tools > 一键搭好 AR 场景 手动执行）。
/// 生成内容：AR Session + XR Origin（带相机）+ 平面检测 + 射线检测 + 放置脚本。
/// </summary>
public static class ARPlacementSceneSetup
{
    const string kScenePath = "Assets/Scenes/ARPlacement.unity";
    const string kPlanePrefabPath = "Assets/Prefabs/ARPlane.prefab";
    const string kPackagePlaneMaterial =
        "Packages/com.unity.xr.arfoundation/Assets/Materials/DebugPlane.mat";

    [MenuItem("Tools/一键搭好 AR 场景")]
    public static void Setup()
    {
        // ===== 1. 新建空场景 =====
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ===== 2. AR Session：负责"启动 AR" =====
        var sessionGo = new GameObject("AR Session");
        sessionGo.AddComponent<ARSession>();
        sessionGo.AddComponent<ARInputManager>();

        // ===== 3. XR Origin：负责"追踪设备位置" =====
        var originGo = new GameObject("XR Origin");
        var origin = originGo.AddComponent<XROrigin>();

        var offsetGo = new GameObject("Camera Offset");
        offsetGo.transform.SetParent(originGo.transform, false);

        var cameraGo = new GameObject("Main Camera");
        cameraGo.transform.SetParent(offsetGo.transform, false);
        var cam = cameraGo.AddComponent<Camera>();
        cameraGo.AddComponent<AudioListener>();
        cameraGo.AddComponent<ARCameraManager>();
        cameraGo.AddComponent<ARCameraBackground>();

        // 让相机跟随手机/模拟器的头部位置
        var tpd = cameraGo.AddComponent<TrackedPoseDriver>();
        var posAction = new InputAction("Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
        posAction.AddBinding("<HandheldARInputDevice>/devicePosition");
        var rotAction = new InputAction("Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
        rotAction.AddBinding("<HandheldARInputDevice>/deviceRotation");
        tpd.positionInput = new InputActionProperty(posAction);
        tpd.rotationInput = new InputActionProperty(rotAction);

        cam.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.Color;
        cam.backgroundColor = Color.black;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 20f;

        origin.CameraFloorOffsetObject = offsetGo;
        origin.Camera = cam;

        // ===== 4. 识别到的平面长什么样（可视化预制体）=====
        EnsureFolder("Assets/Materials");
        EnsureFolder("Assets/Prefabs");
        var planePrefab = BuildPlanePrefab();

        // ===== 5. 平面检测 + 点击射线 + 放置逻辑 =====
        var planeManager = originGo.AddComponent<ARPlaneManager>();
        planeManager.planePrefab = planePrefab;
        originGo.AddComponent<ARRaycastManager>();
        originGo.AddComponent<PlaceOnPlane>();

        // ===== 6. 保存场景并加入构建列表 =====
        EnsureFolder("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, kScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(kScenePath, true) };

        Debug.Log($"[ARSetup] 场景生成完成：{kScenePath}");
    }

    static GameObject BuildPlanePrefab()
    {
        var go = new GameObject("ARPlane");
        go.AddComponent<ARPlaneMeshVisualizer>();
        go.AddComponent<MeshFilter>();
        var renderer = go.AddComponent<MeshRenderer>();

        var mat = AssetDatabase.LoadAssetAtPath<Material>(kPackagePlaneMaterial);
        if (mat != null)
            renderer.sharedMaterial = mat;

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, kPlanePrefabPath);
        Object.DestroyImmediate(go);
        return prefab;
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
