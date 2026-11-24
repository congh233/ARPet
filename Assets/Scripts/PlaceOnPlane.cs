using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// 点一下屏幕（真机是手指，电脑上是鼠标左键）放置物体。
/// 开启了网格可视化时，优先贴着"看得见的网格表面"放；否则退回 AR 平面放置。
/// </summary>
public class PlaceOnPlane : MonoBehaviour
{
    [Tooltip("要放置的物体。留空的话会自动生成一个红色小球。")]
    public GameObject objectToPlace;

    [Tooltip("自动生成小球的直径（米）。0.15 差不多是一个小苹果的大小。")]
    public float ballSize = 0.15f;

    [Header("网格吸附（网格可视化开启时生效）")]
    [Tooltip("优先用蓝色网格的碰撞体放置，让物体贴合可见表面。")]
    public bool useMeshSnapping = true;

    [Tooltip("网格已就绪但射线没打在网格上时：不放球（避免悬空）。")]
    public bool strictMeshMode = true;

    [Tooltip("网格碰撞体还没生成好时：先按平面放球。")]
    public bool planeFallbackWhileMeshPending = true;

    [Tooltip("网格射线最远距离（米）。")]
    public float meshRayDistance = 20f;

    [Tooltip("网格所在 Layer；0 = 自动使用 ARMesh 层。")]
    public LayerMask meshLayerMask;

    [Tooltip("放置自定义物体时，沿表面法线抬起多少米。")]
    public float objectToPlaceSurfaceOffset;

    [Tooltip("只允许放在法线朝上的表面（0 = 允许任意方向）。墙面/侧面默认不放；按住 Shift 可临时允许。")]
    public float minSurfaceNormalY = 0.6f;

    [Tooltip("在物体与表面的接触点显示一圈深色软阴影，帮助判断有没有贴住。")]
    public bool showContactMarker = true;

    ARRaycastManager m_RaycastManager;
    ARMeshManager m_MeshManager;
    static readonly List<ARRaycastHit> k_Hits = new List<ARRaycastHit>();

    void Awake()
    {
        // 找到负责"从屏幕点向空间发射线"的管理器（挂在 XR Origin 上）
        m_RaycastManager = GetComponent<ARRaycastManager>();
        if (m_RaycastManager == null)
            m_RaycastManager = FindObjectOfType<ARRaycastManager>();
    }

    void Update()
    {
        // 按 Backspace / Delete 清空所有球
        if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete))
        {
            ClearAll();
            return;
        }

        if (!TryGetTap(out var screenPos))
            return;

        // 按住 Ctrl：只处理删除，不做放置（避免点到空处又放球）
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        {
            TryDeleteAt(screenPos);
            return;
        }

        TryPlaceAt(screenPos);
    }

    /// <summary>在屏幕某个点尝试放置物体，返回是否放置成功（诊断也用它）。</summary>
    public bool TryPlaceAt(Vector2 screenPos)
    {
        // AR 还没开始追踪时，射线管理器内部是空的，先不理点击
        if (ARSession.state != ARSessionState.SessionTracking)
            return false;

        if (useMeshSnapping)
        {
            var placed = TryPlaceOnMesh(screenPos, out var blockedByStrict);
            if (placed)
                return true;
            if (blockedByStrict)
                return false;
        }

        return TryPlaceOnPlane(screenPos);
    }

    /// <summary>优先用网格碰撞体放置：物体贴着可见表面。</summary>
    bool TryPlaceOnMesh(Vector2 screenPos, out bool blockedByStrict)
    {
        blockedByStrict = false;

        if (m_MeshManager == null)
            m_MeshManager = FindObjectOfType<ARMeshManager>();

        if (m_MeshManager == null || !m_MeshManager.isActiveAndEnabled || m_MeshManager.subsystem == null)
        {
            Debug.Log("[Place] 网格功能不可用（安卓真机常见），走平面放置");
            return false;
        }

        if (!HasReadyMeshCollider(m_MeshManager))
        {
            if (planeFallbackWhileMeshPending)
            {
                Debug.Log("[Place] 网格碰撞体还没就绪，本次先按平面放");
                return false;
            }

            Debug.Log("[Place] 网格碰撞体还没就绪，本次不放球");
            blockedByStrict = true;
            return false;
        }

        var cam = Camera.main;
        if (cam == null)
            return false;

        var ray = cam.ScreenPointToRay(screenPos);
        var distance = Mathf.Min(meshRayDistance, cam.farClipPlane);
        var mask = meshLayerMask.value != 0 ? meshLayerMask.value : PlacementLayers.ResolveMeshLayerMask();

        if (Physics.Raycast(ray, out var hit, distance, mask, QueryTriggerInteraction.Ignore))
        {
            var normal = hit.normal;
            var allowVertical = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (normal.y < minSurfaceNormalY && !allowVertical)
            {
                Debug.Log($"[Place] 命中面太陡（法线y={normal.y:F2} < {minSurfaceNormalY:F2}），默认不放；按住 Shift 可放墙面/侧面");
                blockedByStrict = true;
                return false;
            }

            var pose = new Pose(hit.point, Quaternion.FromToRotation(Vector3.up, normal));
            Spawn(pose, "Mesh", hit.collider.name, hit.distance);
            return true;
        }

        Debug.Log($"[Place] 网格已就绪但射线没打中网格 (strictMeshMode={strictMeshMode})，本次不放球");
        blockedByStrict = strictMeshMode;
        return false;
    }

    bool HasReadyMeshCollider(ARMeshManager manager)
    {
        var meshes = manager.meshes;
        for (var i = 0; i < meshes.Count; i++)
        {
            var meshFilter = meshes[i];
            if (meshFilter == null || !meshFilter.gameObject.activeInHierarchy)
                continue;

            var collider = meshFilter.GetComponent<MeshCollider>();
            if (collider != null && collider.enabled && collider.sharedMesh != null &&
                collider.sharedMesh.vertexCount > 0)
                return true;
        }

        return false;
    }

    /// <summary>退回原逻辑：用 ARRaycastManager 打 AR 平面。</summary>
    bool TryPlaceOnPlane(Vector2 screenPos)
    {
        if (m_RaycastManager == null)
            m_RaycastManager = FindObjectOfType<ARRaycastManager>();

        if (m_RaycastManager == null)
        {
            Debug.LogError("[Place] 场景里没有 ARRaycastManager");
            return false;
        }

        // 从屏幕点发射一条射线，看有没有打中"平面"
        var hit = m_RaycastManager.Raycast(screenPos, k_Hits, TrackableType.PlaneWithinPolygon);
        if (!hit)
            hit = m_RaycastManager.Raycast(screenPos, k_Hits, TrackableType.PlaneWithinBounds); // 兜底：命中范围更宽

        Debug.Log($"[Place] 来源=Plane 屏幕点={screenPos}, 命中={hit}, 命中数={k_Hits.Count}, 屏幕={Screen.width}x{Screen.height}");

        if (!hit)
            return false;

        Spawn(k_Hits[0].pose, "Plane", "ARPlane", 0f);
        return true;
    }

    void Spawn(Pose pose, string source, string surfaceName, float distance)
    {
        var offset = objectToPlace != null ? objectToPlaceSurfaceOffset : ballSize * 0.5f;
        var position = pose.position + pose.up * offset;

        GameObject placedGo;
        if (objectToPlace != null)
        {
            placedGo = Instantiate(objectToPlace, position, pose.rotation);
            placedGo.AddComponent<PlacedObject>();
        }
        else
        {
            placedGo = CreateDefaultBall(pose);
        }

        if (showContactMarker)
            AddContactMarker(placedGo, pose);

        Debug.Log($"[Place] 来源={source}, 表面={surfaceName}, 表面点={pose.position}, 法线={pose.up}, 距离={distance:F2}, 生成点={placedGo.transform.position}");
    }

    /// <summary>在表面接触点贴一个深色薄圆盘，肉眼确认物体是否真的贴住表面。</summary>
    void AddContactMarker(GameObject placedGo, Pose surfacePose)
    {
        var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "Contact Marker";

        var collider = marker.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            Destroy(collider);
        }

        marker.transform.SetParent(placedGo.transform, true);
        marker.transform.position = surfacePose.position + surfacePose.up * 0.001f;
        marker.transform.rotation = Quaternion.FromToRotation(Vector3.up, surfacePose.up);

        var size = Mathf.Max(ballSize * 1.1f, 0.12f);
        var parentScale = placedGo.transform.lossyScale;
        marker.transform.localScale = new Vector3(
            size / Mathf.Max(Mathf.Abs(parentScale.x), 0.0001f),
            0.002f / Mathf.Max(Mathf.Abs(parentScale.y), 0.0001f),
            size / Mathf.Max(Mathf.Abs(parentScale.z), 0.0001f));

        var renderer = marker.GetComponent<Renderer>();
        renderer.sharedMaterial = ContactMarkerMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    static Material s_ContactMarkerMaterial;

    static Material ContactMarkerMaterial
    {
        get
        {
            if (s_ContactMarkerMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default");
                s_ContactMarkerMaterial = new Material(shader);
                s_ContactMarkerMaterial.color = new Color(0f, 0f, 0f, 0.35f);
            }

            return s_ContactMarkerMaterial;
        }
    }

    /// <summary>按住 Ctrl 点击时，如果点到的是我们放下的球，就删掉它。</summary>
    bool TryDeleteAt(Vector2 screenPos)
    {
        var cam = Camera.main;
        if (cam == null)
            return false;

        var ray = cam.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out var hit, 20f))
            return false;

        var placed = hit.collider.GetComponentInParent<PlacedObject>();
        if (placed == null)
            return false;

        Debug.Log($"[Place] 删除单个球：{placed.name}");
        Destroy(placed.gameObject);
        return true;
    }

    void ClearAll()
    {
        var all = FindObjectsOfType<PlacedObject>();
        foreach (var item in all)
            Destroy(item.gameObject);

        Debug.Log($"[Place] 清空了 {all.Length} 个物体");
    }

    /// <summary>真机用手指点，编辑器里用鼠标左键模拟。</summary>
    bool TryGetTap(out Vector2 screenPos)
    {
        if (Input.touchCount > 0)
        {
            var touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
            {
                screenPos = touch.position;
                return true;
            }
        }

        if (Input.GetMouseButtonDown(0))
        {
            screenPos = Input.mousePosition;
            return true;
        }

        // 双保险：万一老输入系统没收到，用新输入系统再兜一次
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            screenPos = mouse.position.ReadValue();
            return true;
        }

        screenPos = default;
        return false;
    }

    GameObject CreateDefaultBall(Pose pose)
    {
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Placed Ball";
        var radius = ballSize * 0.5f;
        // 把球心抬高半个半径，让球"坐"在表面/平面上，而不是半个埋进去
        ball.transform.SetPositionAndRotation(pose.position + pose.up * radius, pose.rotation);
        ball.transform.localScale = Vector3.one * ballSize;
        ball.GetComponent<Renderer>().material.color = new Color(0.9f, 0.25f, 0.15f);
        ball.AddComponent<PlacedObject>();
        return ball;
    }
}
