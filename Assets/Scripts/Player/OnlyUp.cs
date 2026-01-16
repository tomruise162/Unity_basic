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

    // ================= CLIENT =================
    private void Update()
    {
        if (!isLocalPlayer) return;

        if (Input.GetKeyDown(KeyCode.Space))
        {
            CmdRequestJump();
        }
    }

    // ================= SERVER =================
    private void FixedUpdate()
    {
        if (!isServer) return;

        CheckGround();
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

        // QUAN TRONG: Đồng bộ physics ngay lập tức để NetworkTransform đọc được
        // transform.position mới nhất từ Rigidbody
        // Điều này đảm bảo NetworkTransform đọc transform SAU khi Rigidbody di chuyển
        Physics.SyncTransforms();

        Debug.Log($"[SERVER] Player {netId} jumped at position: {transform.position}");

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
