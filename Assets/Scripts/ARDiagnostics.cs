using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;
using Object = UnityEngine.Object;

/// <summary>
/// 临时体检脚本：在 Play 模式每秒记录一次 XR/AR 的运行状态到 D:/UnityProjects/ardiag.log
/// 排查完黑屏问题后可以整个删掉。
/// </summary>
public static class ARDiagnostics
{
    const string kLogPath = "D:/UnityProjects/ardiag.log";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        var go = new GameObject("AR Diagnostics");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<DiagRunner>();
    }

    class DiagRunner : MonoBehaviour
    {
        float m_Timer;
        int m_Tick;
        int m_FrameCount;

        // 自动扫描：进 Play 后自动带着模拟相机环顾房间，把平面"扫"出来
        const float kAutoScanSeconds = 8f;
        MonoBehaviour m_SimCam;
        MethodInfo m_UpdatePose;
        bool m_SimCamSearched;
        bool m_ScanStarted;
        Pose m_ScanStartPose;

        void OnEnable()
        {
            var cameraManager = Object.FindObjectOfType<ARCameraManager>();
            if (cameraManager != null)
                cameraManager.frameReceived += OnCameraFrame;
        }

        void OnDisable()
        {
            var cameraManager = Object.FindObjectOfType<ARCameraManager>();
            if (cameraManager != null)
                cameraManager.frameReceived -= OnCameraFrame;
        }

        void OnCameraFrame(ARCameraFrameEventArgs args) => m_FrameCount++;

        void UpdateAutoScan()
        {
            if (!m_SimCamSearched)
            {
                m_SimCamSearched = true;

                // 模拟相机是插件内部的类，只能反射找它
                Type type = Type.GetType("UnityEngine.XR.Simulation.SimulationCamera, UnityEngine.XR.Simulation");
                if (type == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        type = asm.GetType("UnityEngine.XR.Simulation.SimulationCamera");
                        if (type != null)
                            break;
                    }
                }

                if (type != null)
                {
                    m_SimCam = FindObjectOfType(type) as MonoBehaviour;
                    m_UpdatePose = type.GetMethod("UpdatePose", BindingFlags.Instance | BindingFlags.NonPublic);
                }

                if (m_SimCam == null || m_UpdatePose == null)
                {
                    Debug.LogWarning("[ARDiag] 没找到模拟相机，自动扫描不可用（手动右键+W 走动仍有效）");
                    return;
                }

                Debug.Log("[ARDiag] 找到模拟相机，开始自动扫描房间");
            }

            if (m_SimCam == null || m_UpdatePose == null)
                return;

            // 用户自己按右键时，让位给手动操作
            if (Mouse.current != null && Mouse.current.rightButton.isPressed)
                return;

            var time = Time.timeSinceLevelLoad;

            // 前 1 秒让模拟房间完成初始化，不要抢跑；扫描持续 8 秒
            if (time < 1f || time > 1f + kAutoScanSeconds)
                return;

            if (!m_ScanStarted)
            {
                m_ScanStarted = true;
                m_ScanStartPose = new Pose(m_SimCam.transform.position, m_SimCam.transform.rotation);

                // 万一相机还在地板高度，就用模拟房间的标准起始姿势兜底
                if (m_ScanStartPose.position.y < 0.1f)
                    m_ScanStartPose = new Pose(new Vector3(0.22f, 0.93f, -0.21f), Quaternion.Euler(21.35f, 310.1f, 0f));

                Debug.Log($"[ARDiag] 自动扫描开始，起点={m_ScanStartPose.position}");
            }

            var scanTime = time - 1f;
            var progress = Mathf.Clamp01(scanTime / kAutoScanSeconds);
            var yaw = m_ScanStartPose.rotation.eulerAngles.y + progress * 360f;
            var pitch = Mathf.LerpAngle(m_ScanStartPose.rotation.eulerAngles.x, 40f, Mathf.Clamp01(scanTime / 2f));
            var position = m_ScanStartPose.position + new Vector3(
                Mathf.Sin(progress * Mathf.PI * 2f) * 0.4f,
                0f,
                Mathf.Cos(progress * Mathf.PI * 2f) * 0.4f - 0.4f);
            var rotation = Quaternion.Euler(pitch, yaw, 0f);

            m_UpdatePose.Invoke(m_SimCam, new object[] { new Pose(position, rotation) });
        }

        void Update()
        {
            UpdateAutoScan();

            m_Timer += Time.unscaledDeltaTime;
            if (m_Timer < 1f)
                return;
            m_Timer = 0f;
            m_Tick++;

            // 第 4 秒截一张 Game 视图的图，用来直接看黑屏长什么样
            if (m_Tick == 4)
            {
                ScreenCapture.CaptureScreenshot("D:/UnityProjects/gameview.png");
                Debug.Log("[ARDiag] 已截图到 D:/UnityProjects/gameview.png");
            }


            var sb = new StringBuilder();
            sb.AppendLine($"[Diag {m_Tick}] 运行 {Time.time:F1} 秒 --------------------------");

            var gs = XRGeneralSettings.Instance;
            sb.AppendLine(gs == null
                ? "XRGeneralSettings: null（XR 设置没加载）"
                : $"XRGeneralSettings: ok, InitOnStart={gs.InitManagerOnStart}");

            if (gs != null && gs.Manager != null)
            {
                sb.AppendLine($"XRManager: 初始化完成={gs.Manager.isInitializationComplete}, 当前加载器={(gs.Manager.activeLoader != null ? gs.Manager.activeLoader.name : "null")}");
            }

            sb.AppendLine($"ARSession.state={ARSession.state}, NotTrackingReason={ARSession.notTrackingReason}");
            sb.AppendLine($"场景里 ARSession 数量={Object.FindObjectsOfType<ARSession>().Length}, ARCameraManager 数量={Object.FindObjectsOfType<ARCameraManager>().Length}, ARPlaneManager 数量={Object.FindObjectsOfType<ARPlaneManager>().Length}, ARRaycastManager 数量={Object.FindObjectsOfType<ARRaycastManager>().Length}");
            sb.AppendLine($"相机帧数={m_FrameCount}");

            var bg = Object.FindObjectOfType<ARCameraBackground>();
            sb.AppendLine($"ARCameraBackground: {(bg == null ? "没有！" : "有, 启用=" + bg.enabled)}");

            var pm = Object.FindObjectOfType<ARPlaneManager>();
            sb.AppendLine($"已识别平面数量={(pm != null ? pm.trackables.count : -1)}");

            var cam = Camera.main;
            if (cam == null)
            {
                sb.AppendLine("Camera.main = null（主相机没找到！）");
            }
            else
            {
                sb.AppendLine($"主相机: 位置={cam.transform.position}, 朝向={cam.transform.rotation.eulerAngles}, 启用={cam.enabled}, TargetTexture={(cam.targetTexture == null ? "null" : cam.targetTexture.name)}, FOV={cam.fieldOfView}");
            }

            var kb = Keyboard.current;
            var mouseDevice = Mouse.current;
            sb.AppendLine($"输入: 键盘={(kb == null ? "无" : "有")}, 鼠标={(mouseDevice == null ? "无" : "有")}, 右键按住={(mouseDevice != null && mouseDevice.rightButton.isPressed)}, W键={(kb != null && kb.wKey.isPressed)}, 鼠标位置={(mouseDevice != null ? mouseDevice.position.ReadValue().ToString("F0") : "-")}");

            sb.AppendLine($"屏幕尺寸: {Screen.width}x{Screen.height}");

            try
            {
                File.AppendAllText(kLogPath, sb.ToString());
            }
            catch { /* 忽略写文件失败 */ }

            Debug.Log(sb.ToString());
        }
    }
}
