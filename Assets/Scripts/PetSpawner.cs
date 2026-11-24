using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// 点屏幕：
/// - 第一次点在水平面（地面/桌面）上：生成桌宠
/// - 之后再点：给桌宠设置新的目标点
/// 只认平面，不认墙面，避免把桌宠放到半空或墙上。
/// </summary>
public class PetSpawner : MonoBehaviour
{
    [Tooltip("桌宠预制体。留空会报错，请用 Tools > 搭建桌宠 MVP 自动生成。")]
    public GameObject petPrefab;

    [Tooltip("从屏幕发射线用的管理器，留空会自动找。")]
    public ARRaycastManager raycastManager;

    [Tooltip("只允许放在法线朝上的表面（0.5 = 地面/桌面，墙面会被过滤）")]
    public float minSurfaceNormalY = 0.5f;

    [Tooltip("目标点标记停留时间（秒）")]
    public float markerDuration = 0.5f;

    PetAI m_Pet;
    static readonly List<ARRaycastHit> k_Hits = new List<ARRaycastHit>();

    void Awake()
    {
        if (raycastManager == null)
            raycastManager = FindObjectOfType<ARRaycastManager>();

        m_Pet = FindObjectOfType<PetAI>();
    }

    void Update()
    {
        if (!TryGetTap(out var screenPos))
            return;

        if (ARSession.state != ARSessionState.SessionTracking)
            return;

        if (raycastManager == null)
            return;

        if (!raycastManager.Raycast(screenPos, k_Hits, TrackableType.PlaneWithinPolygon))
            return;

        // 从命中的平面里挑第一个"朝上"的，墙面直接跳过
        foreach (var hit in k_Hits)
        {
            if (hit.pose.up.y < minSurfaceNormalY)
                continue;

            HandleTap(hit.pose.position);
            return;
        }
    }

    void HandleTap(Vector3 point)
    {
        if (m_Pet == null)
        {
            if (petPrefab == null)
            {
                Debug.LogError("[PetSpawner] 没有设置桌宠预制体，请执行 Tools > 搭建桌宠 MVP");
                return;
            }

            var go = Instantiate(petPrefab, point + Vector3.up * 0.01f, Quaternion.identity);
            m_Pet = go.GetComponent<PetAI>();
            if (m_Pet == null)
                m_Pet = go.GetComponentInChildren<PetAI>();

            Debug.Log($"[PetSpawner] 桌宠已生成：{point}");
            return;
        }

        m_Pet.SetTarget(point);
        ShowMarker(point);
        Debug.Log($"[PetSpawner] 新目标：{point}");
    }

    void ShowMarker(Vector3 point)
    {
        var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "Pet Target Marker";

        var markerCollider = marker.GetComponent<Collider>();
        if (markerCollider != null)
            Destroy(markerCollider);

        marker.transform.position = point + Vector3.up * 0.002f;
        marker.transform.localScale = new Vector3(0.08f, 0.002f, 0.08f);

        var markerRenderer = marker.GetComponent<Renderer>();
        if (markerRenderer != null)
            markerRenderer.material.color = new Color(1f, 0.75f, 0.1f, 0.85f);

        Destroy(marker, markerDuration);
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

        screenPos = default;
        return false;
    }
}
