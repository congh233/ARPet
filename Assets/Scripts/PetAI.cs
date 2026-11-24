using UnityEngine;

/// <summary>
/// 桌宠行为核心：
/// - 有目标且前方没障碍：朝目标走，播放 Walk
/// - 前方有障碍：停下，播放 Avoid
/// - 没目标：播放 Idle
/// 移动用 CharacterController，重力贴地，避免穿墙和卡缝。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PetAI : MonoBehaviour
{
    [Header("移动")]
    [Tooltip("走路速度（米/秒）")]
    public float moveSpeed = 0.5f;

    [Tooltip("转身速度")]
    public float turnSpeed = 10f;

    [Tooltip("离目标多近算到达（米）")]
    public float arriveDistance = 0.12f;

    [Header("避障")]
    [Tooltip("向前探障碍的距离（米）")]
    public float obstacleDistance = 0.3f;

    [Tooltip("探障碍射线的高度（米），大概在宠物胸口")]
    public float obstacleRayHeight = 0.12f;

    [Tooltip("障碍物所在的层，默认包含 ARMesh（扫描出来的墙和家具）")]
    public LayerMask obstacleMask;

    [Header("动画")]
    public Animator animator;

    CharacterController m_Controller;
    Vector3? m_Target;
    Vector3 m_SpawnPosition;
    float m_VerticalVelocity;

    public bool HasTarget => m_Target.HasValue;
    public Vector3 Target => m_Target ?? transform.position;

    void Awake()
    {
        m_Controller = GetComponent<CharacterController>();
        m_SpawnPosition = transform.position;

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    /// <summary>点屏幕时由 PetSpawner 调用：设置新的目标点。</summary>
    public void SetTarget(Vector3 worldPosition)
    {
        m_Target = worldPosition;
    }

    public void ClearTarget()
    {
        m_Target = null;
    }

    void Update()
    {
        var dt = Time.deltaTime;

        var hasTarget = m_Target.HasValue;
        var toTarget = hasTarget ? m_Target.Value - transform.position : Vector3.zero;
        toTarget.y = 0f;

        var distance = toTarget.magnitude;
        var arrived = hasTarget && distance <= arriveDistance;

        // 前方探障碍：只探水平方向，不探脚下地面
        var blocked = false;
        if (hasTarget && !arrived && distance > 0.0001f)
        {
            var direction = toTarget / distance;
            var origin = transform.position + Vector3.up * obstacleRayHeight;
            blocked = Physics.Raycast(
                origin,
                direction,
                obstacleDistance,
                obstacleMask,
                QueryTriggerInteraction.Ignore);
        }

        var walking = hasTarget && !arrived && !blocked;
        SetAnimator(walking, blocked);

        var motion = Vector3.zero;

        if (walking)
        {
            var direction = toTarget / distance;
            var look = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, turnSpeed * dt);

            motion = transform.forward * moveSpeed;

            if (distance <= arriveDistance + moveSpeed * dt)
                m_Target = null;
        }
        else if (arrived)
        {
            m_Target = null;
        }

        // 用重力贴地：CharacterController 会踩在扫描出来的网格上
        if (m_Controller.isGrounded && m_VerticalVelocity < 0f)
            m_VerticalVelocity = -1f;

        m_VerticalVelocity += Physics.gravity.y * dt;
        motion.y = m_VerticalVelocity;

        m_Controller.Move(motion * dt);

        // 掉出地图保护：万一网格没生成，回到出生点
        if (transform.position.y < -2f)
            Respawn();
    }

    void SetAnimator(bool walking, bool avoiding)
    {
        if (animator == null)
            return;

        animator.SetBool("IsWalk", walking);
        animator.SetBool("IsAvoid", avoiding);
    }

    void Respawn()
    {
        m_Controller.enabled = false;
        transform.position = m_SpawnPosition + Vector3.up * 0.05f;
        m_Controller.enabled = true;
        m_VerticalVelocity = 0f;
        m_Target = null;
        Debug.LogWarning("[PetAI] 桌宠掉出地图，已回到出生点");
    }
}
