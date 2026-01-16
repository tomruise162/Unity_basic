using UnityEngine;
using Mirror;

[RequireComponent(typeof(Rigidbody))]
public class SimpleNetworkJumpWithGround : NetworkBehaviour
{
    [Header("Jump")]
    [SerializeField] private float jumpForce = 8f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.3f;
    [SerializeField] private LayerMask groundMask;

    private Rigidbody rb;

    // ===== Server state =====
    private bool isGrounded;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.freezeRotation = true;
    }

    // ================= NETWORK LIFECYCLE =================

    public override void OnStartServer()
    {
        base.OnStartServer();
        Debug.Log($"[SERVER] Player {netId} started on server");
        
        // Server: Rigidbody chạy physics bình thường
        rb.isKinematic = false;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        Debug.Log($"[CLIENT] Player {netId} appeared on client, isLocalPlayer: {isLocalPlayer}");
        
        // Client: Chỉ local player mới có Rigidbody động, remote players phải kinematic
        // để tránh conflict với NetworkTransform sync
        if (!isLocalPlayer)
        {
            rb.isKinematic = true;
            Debug.Log($"[CLIENT] Set remote player {netId} Rigidbody to kinematic");
        }
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        Debug.Log($"[LOCAL] This is MY player! NetId: {netId}");
        
        // Local player trên client: Rigidbody động (nhưng server vẫn là authority)
        // Trong host mode, server sẽ điều khiển, client chỉ hiển thị
        rb.isKinematic = false;
    }

    // ================= CLIENT =================
    private void Update()
    {
        if (!isLocalPlayer) return;

        if (Input.GetKeyDown(KeyCode.Space))
        {
            CmdRequestJump();
        }
    }
    
    // Debug: Log position của remote players (chạy trên tất cả clients)
    private void LateUpdate()
    {
        if (isLocalPlayer) return; // Bỏ qua local player
        
        if (Time.frameCount % 50 == 0) // ~1 lần/giây
        {
            Debug.Log($"[CLIENT] Remote player {netId} position: {transform.position}");
        }
    }

    // ================= SERVER =================
    private void FixedUpdate()
    {
        if (!isServer) return;

        CheckGround();
        
        // Debug: Log position mỗi giây để kiểm tra sync
        if (Time.frameCount % 50 == 0) // ~1 lần/giây với 50 FPS
        {
            Debug.Log($"[SERVER] Player {netId} position: {transform.position}");
        }
    }

    [Command]
    private void CmdRequestJump()
    {
        if (!isGrounded) return;

        PerformJump();
    }

    private void PerformJump()
    {
        // Reset Y velocity
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        // Apply jump impulse
        rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);

        Debug.Log($"[SERVER] Player {netId} jumped");

        RpcOnJump();
    }

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

    // ================= RPC =================
    [ClientRpc]
    private void RpcOnJump()
    {
        Debug.Log($"[RPC] Jump visual for player {netId}");
        // animation / sound sau này
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
