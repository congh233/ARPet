using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 一次性小助手：把 XR 模拟环境切换为官方示例的客厅。
/// 只在"还没选过环境"时生效，之后用户自己选的环境不会被覆盖。
/// </summary>
[InitializeOnLoad]
static class AutoSelectSimulationEnvironment
{
    const string kEnvPath =
        "Assets/UnityXRContent/ARFoundation/SimulationEnvironments/LivingRoom/LivingRoom_17.5ftx16.5ft.prefab";

    const double kGiveUpAfterSeconds = 180;
    static readonly double s_StartTime = EditorApplication.timeSinceStartup;
    static string MarkerPath => System.IO.Path.Combine(Application.dataPath, "../ProjectSettings/AutoSelectLivingRoom.done");

    static AutoSelectSimulationEnvironment()
    {
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup - s_StartTime > kGiveUpAfterSeconds)
        {
            EditorApplication.update -= Tick;
            Debug.LogWarning("[ARSetup] 等待示例环境导入超时，未自动切换（可以手动在 XR Environment 下拉框里选）");
            return;
        }

        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(kEnvPath);
        if (prefab == null)
            return; // 资源还没导入完，等下一帧

        if (!TryApply(prefab))
            return;

        EditorApplication.update -= Tick;
    }

    static bool TryApply(GameObject prefab)
    {
        // 已经自动切换过一次就收手，之后尊重用户自己的选择
        if (System.IO.File.Exists(MarkerPath))
            return true;

        Type prefsType = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            prefsType = asm.GetType("UnityEngine.XR.Simulation.XRSimulationPreferences");
            if (prefsType != null)
                break;
        }

        if (prefsType == null)
            return false;

        var instanceProp = prefsType.GetProperty("Instance",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        var instance = instanceProp?.GetValue(null);
        if (instance == null)
            return false;

        var envProp = prefsType.GetProperty("environmentPrefab", BindingFlags.Public | BindingFlags.Instance);
        if (envProp == null)
            return false;

        envProp.SetValue(instance, prefab);
        EditorUtility.SetDirty((UnityEngine.Object)instance);
        AssetDatabase.SaveAssets();
        WriteMarker();
        Debug.Log("[ARSetup] 模拟环境已切换为客厅：LivingRoom_17.5ftx16.5ft");
        return true;
    }

    static void WriteMarker()
    {
        try
        {
            System.IO.File.WriteAllText(MarkerPath, DateTime.Now.ToString("u"));
        }
        catch
        {
            // 写标记失败也无所谓
        }
    }
}
