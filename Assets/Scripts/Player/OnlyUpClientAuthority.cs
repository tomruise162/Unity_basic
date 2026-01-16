using UnityEngine;
using Mirror;

/// <summary>
/// Client-Authoritative Movement với Rigidbody
/// Pattern giống QWorld nhưng dùng Rigidbody thay vì CharacterController
/// 
/// FLOW:
/// 1. Client đọc input và di chuyển trực tiếp
/// 2. NetworkTransform (Client Authority) sync position từ client lên server
/// 3. Server broadcast xuống các clients khác
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class OnlyUpClientAuthority : NetworkBehaviour
{
    [Header("Jump")]
    [SerializeField] private float jumpForce = 8f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.3f;
    [SerializeField] private LayerMask groundMask;

    private Rigidbody rb;

    // ===== Client state =====
    private bool isGrounded;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.freezeRotation = true;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        
        // Đổi màu cho REMOTE players (máy người khác)
        if (!isLocalPlayer)
        {
            ChangePlayerColor(Color.blue);
        }
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        
        // Verify NetworkTransform config
        var networkTransform = GetComponent<Mirror.NetworkTransformHybrid>();
        if (networkTransform == null)
        {
            Debug.LogError($"[LOCAL] NetworkTransformHybrid component not found!");
        }
        else if (networkTransform.syncDirection != Mirror.SyncDirection.ClientToServer)
        {
            Debug.LogError($"[LOCAL] NetworkTransform syncDirection must be ClientToServer! Current: {networkTransform.syncDirection}");
        }
        
        // Disable NetworkRigidbody nếu có (không cần với Client Authority)
        var networkRigidbodyReliable = GetComponent<Mirror.NetworkRigidbodyReliable>();
        var networkRigidbodyUnreliable = GetComponent<Mirror.NetworkRigidbodyUnreliable>();
        
        if (networkRigidbodyReliable != null)
        {
            networkRigidbodyReliable.enabled = false;
            Debug.LogWarning($"[LOCAL] Disabled NetworkRigidbodyReliable - not needed with Client Authority");
        }
        
        if (networkRigidbodyUnreliable != null)
        {
            networkRigidbodyUnreliable.enabled = false;
            Debug.LogWarning($"[LOCAL] Disabled NetworkRigidbodyUnreliable - not needed with Client Authority");
        }
        
        // Đảm bảo Rigidbody không bị kinematic
        rb.isKinematic = false;
        
        // Đổi màu cho local player
        ChangePlayerColor(Color.green);
    }

    /// <summary>
    /// Đổi màu player để dễ nhận diện
    /// </summary>
    private void ChangePlayerColor(Color color)
    {
        // Tìm Renderer component để đổi màu
        var renderer = GetComponent<Renderer>();
        if (renderer != null && renderer.material != null)
        {
            // Tạo material instance để không ảnh hưởng đến prefab
            renderer.material = new Material(renderer.material);
            renderer.material.color = color;
        }
        else
        {
            // Nếu không có Renderer, thử tìm trong children
            var childRenderer = GetComponentInChildren<Renderer>();
            if (childRenderer != null && childRenderer.material != null)
            {
                childRenderer.material = new Material(childRenderer.material);
                childRenderer.material.color = color;
            }
        }
    }

    // ================= CLIENT-AUTHORITATIVE MOVEMENT =================
    
    /// <summary>
    /// CLIENT: Đọc input và xử lý movement
    /// Chỉ local player mới chạy logic này
    /// </summary>
    [Client]
    private void Update()
    {
        // Chỉ local player mới xử lý input
        if (!isLocalPlayer) return;

        // Đọc input
        if (Input.GetKeyDown(KeyCode.Space))
        {
            HandleJump();
        }
    }

    /// <summary>
    /// CLIENT: FixedUpdate cho physics
    /// Chỉ local player mới chạy physics
    /// </summary>
    [Client]
    private void FixedUpdate()
    {
        // Chỉ local player mới chạy physics
        if (!isLocalPlayer) return;

        // QUAN TRONG: Đảm bảo Rigidbody không bị NetworkRigidbody set kinematic
        // NetworkRigidbody có thể override isKinematic trong FixedUpdate
        // Với Client Authority, local player CẦN Rigidbody động
        if (rb.isKinematic)
        {
            rb.isKinematic = false;
            Debug.LogWarning($"[LOCAL] Rigidbody was kinematic! Fixed to false. This may be caused by NetworkRigidbody component.");
        }

        // Check ground
        CheckGround();
    }

    /// <summary>
    /// CLIENT: Xử lý jump trực tiếp trên client
    /// NetworkTransform sẽ tự động sync position lên server
    /// </summary>
    [Client]
    private void HandleJump()
    {
        if (!isGrounded) return;

        // Reset Y velocity
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        // Apply jump impulse trên CLIENT
        rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);

        // Đồng bộ physics để NetworkTransform đọc được transform mới
        Physics.SyncTransforms();

        // Gửi RPC để các clients khác biết (visual effects, sound, etc.)
        CmdOnJump();
    }

    /// <summary>
    /// CLIENT: Check ground trên local player
    /// </summary>
    [Client]
    private void CheckGround()
    {
        Vector3 checkPos = groundCheck != null
            ? groundCheck.position
            : transform.position + Vector3.down * 0.1f;

        isGrounded = Physics.CheckSphere(
            checkPos,
            groundCheckRadius,
            groundMask,
            QueryTriggerInteraction.Ignore
        );
    }

    // ================= RPC (Optional - cho visual effects) =================

    /// <summary>
    /// COMMAND: Gửi từ client lên server để notify jump
    /// Dùng cho visual effects, sound, etc.
    /// </summary>
    [Command]
    private void CmdOnJump()
    {
        // Server broadcast xuống tất cả clients
        RpcOnJump();
    }

    /// <summary>
    /// CLIENT RPC: Tất cả clients nhận được để play effects
    /// </summary>
    [ClientRpc]
    private void RpcOnJump()
    {
        // animation / sound / particle effects
    }

    // ================= DEBUG =================
    private void OnDrawGizmos()
    {
        Vector3 checkPos = groundCheck != null
            ? groundCheck.position
            : transform.position + Vector3.down * 0.1f;

        Gizmos.color = isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(checkPos, groundCheckRadius);
    }
}

