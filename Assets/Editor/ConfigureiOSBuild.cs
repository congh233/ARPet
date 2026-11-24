using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;

/// <summary>
/// 一次性配置：把工程设置成可在 CI 上编译的 iOS / ARKit 工程。
/// 菜单：Tools > 配置 iOS 构建设置
/// 也可以在命令行用 -executeMethod ConfigureiOSBuild.Run 调用。
/// </summary>
public static class ConfigureiOSBuild
{
    const string kBundleId = "com.congh233.arpet";
    const string kCameraUsage = "需要相机权限来扫描房间并放置桌宠";

    [MenuItem("Tools/配置 iOS 构建设置")]
    public static void Run()
    {
        ConfigurePlayerSettings();
        ConfigureXrLoaders();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[ConfigureiOS] 完成：iOS 玩家设置 + ARKit Loader 已配置");
    }

    static void ConfigurePlayerSettings()
    {
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, kBundleId);
        PlayerSettings.iOS.cameraUsageDescription = kCameraUsage;
        PlayerSettings.iOS.targetOSVersionString = "14.0";
        PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
        PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.iOS, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetArchitecture(BuildTargetGroup.iOS, 1); // 1 = ARM64
        PlayerSettings.bundleVersion = "0.1.0";
        PlayerSettings.iOS.buildNumber = "1";

        Debug.Log($"[ConfigureiOS] BundleId={kBundleId}, iOS>=14.0, IL2CPP/ARM64, ARKit required");
    }

    static void ConfigureXrLoaders()
    {
        if (!EditorBuildSettings.TryGetConfigObject(
                XRGeneralSettings.k_SettingsKey,
                out XRGeneralSettingsPerBuildTarget perBuildTarget) || perBuildTarget == null)
        {
            Debug.LogError("[ConfigureiOS] 找不到 XRGeneralSettingsPerBuildTarget，请先在 Project Settings > XR Plug-in Management 里打开一次");
            return;
        }

        if (!perBuildTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.iOS))
            perBuildTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.iOS);

        var manager = perBuildTarget.ManagerSettingsForBuildTarget(BuildTargetGroup.iOS);
        if (manager == null)
        {
            Debug.LogError("[ConfigureiOS] 创建 iOS XRManagerSettings 失败");
            return;
        }

        var assigned = XRPackageMetadataStore.AssignLoader(
            manager,
            "UnityEngine.XR.ARKit.ARKitLoader",
            BuildTargetGroup.iOS);

        Debug.Log($"[ConfigureiOS] ARKit Loader assigned = {assigned}");

        if (assigned)
        {
            var active = string.Join(", ", manager.activeLoaders);
            Debug.Log($"[ConfigureiOS] iOS 激活的 Loader: {active}");
        }
    }
}
