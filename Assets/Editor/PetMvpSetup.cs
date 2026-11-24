using System.IO;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// 一键搭建桌宠 MVP：
/// - 生成 Idle / Walk / Avoid 三段占位动画和 Animator
/// - 生成一个胶囊桌宠预制体（CharacterController + PetAI）
/// - 把场景里的“放球”脚本换成 PetSpawner（放球脚本只是禁用，不删除）
/// 菜单：Tools > 搭建桌宠 MVP
/// </summary>
public static class PetMvpSetup
{
    const string kScenePath = "Assets/Scenes/ARPlacement.unity";
    const string kPrefabPath = "Assets/Prefabs/ARPet.prefab";
    const string kAnimFolder = "Assets/Animations/Pet";
    const string kControllerPath = kAnimFolder + "/PetAnimator.controller";
    const string kIdlePath = kAnimFolder + "/PetIdle.anim";
    const string kWalkPath = kAnimFolder + "/PetWalk.anim";
    const string kAvoidPath = kAnimFolder + "/PetAvoid.anim";
    const string kBodyMaterialPath = "Assets/Materials/PetBody.mat";
    const string kNoseMaterialPath = "Assets/Materials/PetNose.mat";

    [MenuItem("Tools/搭建桌宠 MVP")]
    public static void Setup()
    {
        EnsureFolder("Assets/Animations");
        EnsureFolder(kAnimFolder);
        EnsureFolder("Assets/Prefabs");
        EnsureFolder("Assets/Materials");

        var petLayer = EnsureLayer("Pet", 9);

        // 每次重建动画资产，保证参数和状态机是最新的
        AssetDatabase.DeleteAsset(kControllerPath);
        AssetDatabase.DeleteAsset(kIdlePath);
        AssetDatabase.DeleteAsset(kWalkPath);
        AssetDatabase.DeleteAsset(kAvoidPath);

        var idle = CreateClip(kIdlePath, 2.0f,
            ConstantCurve(2.0f, 0f),
            BobCurve(2.0f, 0.008f));
        var walk = CreateClip(kWalkPath, 0.6f,
            SwayCurve(0.6f, 0.012f),
            BobCurve(0.6f, 0.018f));
        var avoid = CreateClip(kAvoidPath, 0.3f,
            ShakeCurve(0.3f, 0.035f),
            ConstantCurve(0.3f, 0f));

        var controller = CreateAnimatorController(idle, walk, avoid);
        var prefab = BuildPetPrefab(controller, petLayer);
        ConfigureScene(prefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[PetMVP] 完成！打开 Assets/Scenes/ARPlacement.unity，按 Play，" +
                  "用鼠标点地面放桌宠，再点别处让它走过去。");
    }

    // ---------- 动画 ----------

    static AnimationClip CreateClip(string path, float length, AnimationCurve x, AnimationCurve y)
    {
        var clip = new AnimationClip { frameRate = 30f };

        var bindingX = EditorCurveBinding.FloatCurve("Model", typeof(Transform), "m_LocalPosition.x");
        var bindingY = EditorCurveBinding.FloatCurve("Model", typeof(Transform), "m_LocalPosition.y");
        AnimationUtility.SetEditorCurve(clip, bindingX, x);
        AnimationUtility.SetEditorCurve(clip, bindingY, y);

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.startTime = 0f;
        settings.stopTime = length;
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    static AnimationCurve ConstantCurve(float length, float value)
    {
        return new AnimationCurve(
            new Keyframe(0f, value),
            new Keyframe(length, value));
    }

    static AnimationCurve BobCurve(float length, float amount)
    {
        return new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(length * 0.5f, amount),
            new Keyframe(length, 0f));
    }

    static AnimationCurve SwayCurve(float length, float amount)
    {
        return new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(length * 0.25f, amount),
            new Keyframe(length * 0.5f, 0f),
            new Keyframe(length * 0.75f, -amount),
            new Keyframe(length, 0f));
    }

    static AnimationCurve ShakeCurve(float length, float amount)
    {
        return new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(length * 0.33f, amount),
            new Keyframe(length * 0.66f, -amount),
            new Keyframe(length, 0f));
    }

    static AnimatorController CreateAnimatorController(AnimationClip idle, AnimationClip walk, AnimationClip avoid)
    {
        var controller = AnimatorController.CreateAnimatorControllerAtPath(kControllerPath);
        controller.AddParameter("IsWalk", AnimatorControllerParameterType.Bool);
        controller.AddParameter("IsAvoid", AnimatorControllerParameterType.Bool);

        var stateMachine = controller.layers[0].stateMachine;

        var idleState = stateMachine.AddState("Idle");
        idleState.motion = idle;

        var walkState = stateMachine.AddState("Walk");
        walkState.motion = walk;

        var avoidState = stateMachine.AddState("Avoid");
        avoidState.motion = avoid;

        stateMachine.defaultState = idleState;

        AddTransition(idleState, walkState, ("IsWalk", AnimatorConditionMode.If));
        AddTransition(idleState, avoidState, ("IsAvoid", AnimatorConditionMode.If));

        // 避障优先：先判断 Avoid，再判断回 Idle
        AddTransition(walkState, avoidState, ("IsAvoid", AnimatorConditionMode.If));
        AddTransition(walkState, idleState, ("IsWalk", AnimatorConditionMode.IfNot));

        AddTransition(avoidState, walkState,
            ("IsAvoid", AnimatorConditionMode.IfNot),
            ("IsWalk", AnimatorConditionMode.If));
        AddTransition(avoidState, idleState,
            ("IsAvoid", AnimatorConditionMode.IfNot),
            ("IsWalk", AnimatorConditionMode.IfNot));

        EditorUtility.SetDirty(controller);
        return controller;
    }

    static void AddTransition(AnimatorState from, AnimatorState to,
        params (string parameter, AnimatorConditionMode mode)[] conditions)
    {
        var transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.duration = 0.08f;
        transition.canTransitionToSelf = false;

        foreach (var condition in conditions)
            transition.AddCondition(condition.mode, 0f, condition.parameter);
    }

    // ---------- 桌宠预制体 ----------

    static GameObject BuildPetPrefab(AnimatorController controller, int petLayer)
    {
        var meshLayer = PlacementLayers.ResolveMeshLayer();
        var bodyMaterial = EnsureMaterial(kBodyMaterialPath, new Color(1f, 0.55f, 0.15f));
        var noseMaterial = EnsureMaterial(kNoseMaterialPath, new Color(0.12f, 0.12f, 0.12f));

        var root = new GameObject("ARPet");
        root.layer = petLayer;

        var controllerComp = root.AddComponent<CharacterController>();
        controllerComp.height = 0.25f;
        controllerComp.radius = 0.075f;
        controllerComp.center = new Vector3(0f, 0.125f, 0f);
        controllerComp.skinWidth = 0.005f;
        controllerComp.stepOffset = 0.05f;
        controllerComp.slopeLimit = 45f;
        controllerComp.minMoveDistance = 0f;

        var petAI = root.AddComponent<PetAI>();
        petAI.moveSpeed = 0.5f;
        petAI.turnSpeed = 10f;
        petAI.arriveDistance = 0.12f;
        petAI.obstacleDistance = 0.3f;
        petAI.obstacleRayHeight = 0.12f;
        petAI.obstacleMask = 1 << meshLayer;

        // Visual 负责动画，Model 是胶囊本体
        var visual = new GameObject("Visual");
        visual.layer = petLayer;
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = new Vector3(0f, 0.125f, 0f);

        var animator = visual.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;

        var model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        model.name = "Model";
        model.layer = petLayer;
        model.transform.SetParent(visual.transform, false);
        model.transform.localPosition = Vector3.zero;
        model.transform.localScale = new Vector3(0.16f, 0.125f, 0.16f);
        DestroyImmediateSafe(model.GetComponent<Collider>());
        var modelRenderer = model.GetComponent<Renderer>();
        if (modelRenderer != null)
            modelRenderer.sharedMaterial = bodyMaterial;

        // 小鼻子，用来看清宠物朝向
        var nose = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        nose.name = "Nose";
        nose.layer = petLayer;
        nose.transform.SetParent(visual.transform, false);
        nose.transform.localPosition = new Vector3(0f, 0.02f, 0.09f);
        nose.transform.localScale = Vector3.one * 0.03f;
        DestroyImmediateSafe(nose.GetComponent<Collider>());
        var noseRenderer = nose.GetComponent<Renderer>();
        if (noseRenderer != null)
            noseRenderer.sharedMaterial = noseMaterial;

        petAI.animator = animator;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, kPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static void DestroyImmediateSafe(Object obj)
    {
        if (obj != null)
            Object.DestroyImmediate(obj);
    }

    static Material EnsureMaterial(string path, Color color)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, path);
        }

        material.color = color;
        EditorUtility.SetDirty(material);
        return material;
    }

    // ---------- 场景 ----------

    static void ConfigureScene(GameObject petPrefab)
    {
        var scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

        var origin = Object.FindObjectOfType<XROrigin>();
        if (origin == null)
        {
            Debug.LogError("[PetMVP] 场景里没找到 XR Origin，无法配置");
            return;
        }

        // 旧的放球脚本先禁用，避免和桌宠抢点击
        foreach (var placer in Object.FindObjectsOfType<PlaceOnPlane>())
            placer.enabled = false;

        var raycastManager = Object.FindObjectOfType<ARRaycastManager>();
        if (raycastManager == null)
            raycastManager = origin.gameObject.AddComponent<ARRaycastManager>();

        var spawner = Object.FindObjectOfType<PetSpawner>();
        if (spawner == null)
            spawner = origin.gameObject.AddComponent<PetSpawner>();

        spawner.petPrefab = petPrefab;
        spawner.raycastManager = raycastManager;
        spawner.minSurfaceNormalY = 0.5f;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[PetMVP] 场景已配置：{kScenePath}（放球脚本已禁用，PetSpawner 已启用）");
    }

    // ---------- 工具 ----------

    static int EnsureLayer(string layerName, int preferredIndex)
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0)
            return preferredIndex;

        var serialized = new SerializedObject(assets[0]);
        var layers = serialized.FindProperty("layers");
        if (layers == null)
            return preferredIndex;

        for (var i = 0; i < layers.arraySize; i++)
        {
            if (layers.GetArrayElementAtIndex(i).stringValue == layerName)
                return i;
        }

        var target = preferredIndex;
        if (target < 0 || target >= layers.arraySize ||
            !string.IsNullOrEmpty(layers.GetArrayElementAtIndex(target).stringValue))
        {
            target = -1;
            for (var i = 8; i < layers.arraySize; i++)
            {
                if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
                {
                    target = i;
                    break;
                }
            }
        }

        if (target < 0)
        {
            Debug.LogError($"[PetMVP] 找不到空 Layer 来创建 {layerName}");
            return preferredIndex;
        }

        layers.GetArrayElementAtIndex(target).stringValue = layerName;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(assets[0]);
        AssetDatabase.SaveAssets();
        Debug.Log($"[PetMVP] 已创建 Layer {target} = {layerName}");
        return target;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        var name = Path.GetFileName(path);
        AssetDatabase.CreateFolder(parent, name);
    }
}
