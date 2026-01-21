using UnityEngine;
using Mirror;

/// <summary>
/// Fall Guys style Movement Controller with Coin Collection (Mirror, client-authoritative)
///
/// Client-Authoritative Movement pattern:
/// 1. Client đọc input và di chuyển trực tiếp
/// 2. NetworkTransform (Client Authority) sync position từ client lên server
/// 3. Server broadcast xuống các clients khác
///
/// Coin collection vẫn dùng Server Authority để đảm bảo tính hợp lệ.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FallGuysMovement : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float acceleration = 50f;
    [SerializeField] private float deceleration = 40f;
    [SerializeField] private float airControlMultiplier = 0.4f;

    [Header("Rotation")]
    [SerializeField] private float rotationSpeed = 720f; // degrees/sec

    [Header("Jump")]
    [SerializeField] private float jumpForce = 8f;
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.3f;
    [SerializeField] private LayerMask groundMask;
    [Tooltip("Time window to buffer jump input (seconds)")]
    [SerializeField] private float jumpBufferTime = 0.15f;
    [Tooltip("Time window to allow jump after leaving ground (coyote time)")]
    [SerializeField] private float coyoteTime = 0.1f;

    [Header("Dive")]
    [SerializeField] private float diveForce = 12f;
    [SerializeField] private float diveCooldown = 1f;

    [Header("Physics Feel")]
    [Tooltip("Them luc keo xuong khi dang roi de cam giac 'nang' hon")]
    [SerializeField] private float fallMultiplier = 2.5f;

    [Tooltip("Khi nha nut nhay som, roi nhanh hon")]
    [SerializeField] private float lowJumpMultiplier = 2f;

    [Header("Camera Reference (Client Only)")]
    [Tooltip("Camera hoac pivot yaw. Neu de trong, local player se tu lay Camera.main.")]
    [SerializeField] private Transform cameraTransform;

    [Header("Coin System")]
    // SyncVar là biến:
    // Chỉ được thay đổi trên Server
    // Tự động sync giá trị xuống tất cả Client
    // Client không được set trực tiếp

    // Hook là hàm được gọi trên client khi giá trị SyncVar thay đổi:
    // Được gọi sau khi server gửi giá trị mới:
    // Giúp:
    // Update UI
    // Play animation / sound
    // Trigger effect
    [SyncVar(hook = nameof(OnCoinCountChanged))]
    public int coinCount = 0;

    // ===== Client input state =====
    private Vector2 inputDirection;
    private bool jumpPressed;
    private bool divePressed;
    private bool jumpHeld;

    // ===== Client-applied movement state =====
    private Vector3 moveDirection;
    private float jumpBufferTimer;
    private float lastGroundedTime;

    // ===== State =====
    private bool isGrounded;
    private bool wasGrounded;
    private bool isDiving;
    private float lastDiveTime;

    // ===== Components =====
    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        // Setup rigidbody cho Fall Guys feel
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.freezeRotation = true;
    }

    // ================= NETWORK LIFECYCLE =================

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
        
        // Auto-assign camera if not set
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
        
        // Đổi màu cho local player
        ChangePlayerColor(Color.green);
    }

    /// <summary>
    /// Đổi màu player để dễ nhận diện
    /// </summary>
    private void ChangePlayerColor(Color color)
    {
        var renderer = GetComponent<Renderer>();
        if (renderer != null && renderer.material != null)
        {
            renderer.material = new Material(renderer.material);
            renderer.material.color = color;
        }
        else
        {
            var childRenderer = GetComponentInChildren<Renderer>();
            if (childRenderer != null && childRenderer.material != null)
            {
                childRenderer.material = new Material(childRenderer.material);
                childRenderer.material.color = color;
            }
        }
    }

    // ================= COIN SYSTEM =================

    /// <summary>
    /// SYNCVAR HOOK: Runs on CLIENT when coinCount changes.
    /// </summary>
    private void OnCoinCountChanged(int oldValue, int newValue)
    {
        Debug.Log($"[HOOK] coinCount changed: {oldValue} -> {newValue}");

        // Only update UI for local player
        if (isLocalPlayer)
        {
            // Uncomment if you have CoinUI
            // CoinUI.NotifyCoinChanged(newValue);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isLocalPlayer) return;
        if (!other.CompareTag("Coin")) return;

        NetworkIdentity coinNi = other.GetComponent<NetworkIdentity>();
        if (coinNi == null) return;

        CmdPickupCoin(coinNi.netId);
    }

    /// <summary>
    /// COMMAND: Runs on SERVER, called from CLIENT.
    /// </summary>
    [Command]
    private void CmdPickupCoin(uint coinNetId)
    {
        // Server validate: does coin exist?
        if (!NetworkServer.spawned.TryGetValue(coinNetId, out NetworkIdentity coinNi))
        {
            Debug.Log($"[SERVER] Coin {coinNetId} not found - maybe already picked up");
            return;
        }

        // Server: increase coin count (SyncVar will sync to clients)
        coinCount++;
        Debug.Log($"[SERVER] Player {netId} picked up coin. Total: {coinCount}");

        // Server: destroy coin (Mirror syncs to all clients)
        NetworkServer.Destroy(coinNi.gameObject);

        // Notify all clients (visual/audio effects)
        RpcOnCoinPickedUp(coinNi.gameObject.transform.position);

        // Notify specific player (personal feedback)
        TargetOnYouPickedUpCoin(connectionToClient, coinCount);
    }

    [ClientRpc]
    private void RpcOnCoinPickedUp(Vector3 coinPosition)
    {
        Debug.Log($"[RPC-ALL] A coin was picked up at {coinPosition}");
        // Play particle effect, sound, etc.
    }

    [TargetRpc]
    private void TargetOnYouPickedUpCoin(NetworkConnection target, int totalCoins)
    {
        Debug.Log($"[RPC-TARGET] YOU picked up a coin! Total: {totalCoins}");
        // Personal notification, sound, etc.
    }

    // ================= CLIENT-AUTHORITATIVE MOVEMENT =================

    [Client]
    private void Update()
    {
        if (!isLocalPlayer) return;
        if (NetworkClient.connection == null || !NetworkClient.ready) return;

        ReadInput();

        // Get camera orientation vectors
        Vector3 camForward = Vector3.forward;
        Vector3 camRight = Vector3.right;

        if (cameraTransform != null)
        {
            camForward = cameraTransform.forward;
            camRight = cameraTransform.right;

            // Project onto ground plane
            camForward.y = 0f;
            camRight.y = 0f;
            camForward.Normalize();
            camRight.Normalize();
        }

        // Calculate movement direction on CLIENT
        if (inputDirection.sqrMagnitude > 0.001f)
        {
            Vector3 dir = camForward * inputDirection.y + camRight * inputDirection.x;
            moveDirection = dir.sqrMagnitude > 1f ? dir.normalized : dir;
        }
        else
        {
            moveDirection = Vector3.zero;
        }

        // Jump buffering: if jump is pressed, set the buffer timer
        if (jumpPressed)
        {
            jumpBufferTimer = jumpBufferTime;
            jumpPressed = false;
        }

        // Dive input - handle on client
        if (divePressed && CanDive())
        {
            PerformDive();
            divePressed = false;
        }
    }

    private void ReadInput()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        inputDirection = new Vector2(h, v);

        if (inputDirection.sqrMagnitude > 1f)
            inputDirection.Normalize();

        // Use GetKeyDown to capture jump press
        if (Input.GetKeyDown(KeyCode.Space))
        {
            jumpPressed = true;
        }

        jumpHeld = Input.GetKey(KeyCode.Space);

        if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.LeftControl))
            divePressed = true;
        // Lấy camera main làm camera của player
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    // ================= MOVEMENT PHYSICS =================

    [Client]
    private void FixedUpdate()
    {
        if (!isLocalPlayer) return;

        // QUAN TRONG: Đảm bảo Rigidbody không bị NetworkRigidbody set kinematic
        if (rb.isKinematic)
        {
            rb.isKinematic = false;
            Debug.LogWarning($"[LOCAL] Rigidbody was kinematic! Fixed to false. This may be caused by NetworkRigidbody component.");
        }

        CheckGround();

        // Decrease jump buffer timer
        if (jumpBufferTimer > 0f)
        {
            jumpBufferTimer -= Time.fixedDeltaTime;
        }

        // Track last grounded time for coyote time
        if (isGrounded)
        {
            lastGroundedTime = Time.time;
        }

        // Jump with buffer and coyote time
        // Allow jump if:
        // 1. Jump buffer is active (recently pressed jump)
        // 2. AND either grounded OR within coyote time
        bool canJump = jumpBufferTimer > 0f &&
                       (isGrounded || Time.time < lastGroundedTime + coyoteTime);

        if (canJump)
        {
            PerformJump();
            jumpBufferTimer = 0f; // Clear buffer after jumping
        }

        ApplyMovement();
        ApplyRotation();
        ApplyBetterJumpPhysics();

        // Đồng bộ physics để NetworkTransform đọc được transform mới
        Physics.SyncTransforms();

        wasGrounded = isGrounded;
    }

    [Client]
    private void CheckGround()
    {
        Vector3 checkPos = groundCheck != null
            ? groundCheck.position
            : transform.position + Vector3.down * 0.1f;

        isGrounded = Physics.CheckSphere(checkPos, groundCheckRadius, groundMask, QueryTriggerInteraction.Ignore);

        if (isGrounded && isDiving)
            isDiving = false;
    }

    [Client]
    private void ApplyMovement()
    {
        Vector3 targetVelocity = moveDirection * moveSpeed;

        float accelRate = moveDirection.sqrMagnitude > 0.001f ? acceleration : deceleration;
        if (!isGrounded)
            accelRate *= airControlMultiplier;

        // Only change horizontal velocity, the vertical (y) velocity 
        // is controlled by physics of function PerformJump
        // 
        Vector3 horizontalVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        Vector3 newHorizontalVel = Vector3.MoveTowards(
            horizontalVel,
            targetVelocity,
            accelRate * Time.fixedDeltaTime
        );

        // Apply the new horizontal velocity while keeping the current vertical velocity
        rb.linearVelocity = new Vector3(newHorizontalVel.x, rb.linearVelocity.y, newHorizontalVel.z);

        // TODO: Hoi Alvis
        // rb.linearVelocity = Vector3.MoveTowards(
        //     rb.linearVelocity,
        //     targetVelocity,
        //     accelRate * Time.fixedDeltaTime);
    }
    
    [Client]
    private void ApplyRotation()
    {
        if (isDiving) return;

        Vector3 horizontalVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        if (horizontalVel.sqrMagnitude < 0.01f) return;
        
        Quaternion targetRot = Quaternion.LookRotation(horizontalVel.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRot,
            rotationSpeed * Time.fixedDeltaTime
        );
    }

    [Client]
    private void PerformJump()
    {
        // Reset vertical velocity before jumping
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        CmdOnJump();
    }

    [Client]
    private bool CanDive()
    {
        return !isGrounded && !isDiving && Time.time > lastDiveTime + diveCooldown;
    }

    [Client]
    private void PerformDive()
    {
        isDiving = true;
        lastDiveTime = Time.time;

        Vector3 diveDir = transform.forward + Vector3.down * 0.3f;
        diveDir.Normalize();

        rb.linearVelocity = Vector3.zero;
        rb.AddForce(diveDir * diveForce, ForceMode.Impulse);
        rb.AddTorque(Vector3.right * 5f, ForceMode.Impulse);

        CmdOnDive();
    }

    [Client]
    private void ApplyBetterJumpPhysics()
    {
        if (rb.linearVelocity.y < 0f)
        {
            rb.linearVelocity += Vector3.up * Physics.gravity.y * (fallMultiplier - 1f) * Time.fixedDeltaTime;
        }
        else if (rb.linearVelocity.y > 0f && !jumpHeld)
        {
            rb.linearVelocity += Vector3.up * Physics.gravity.y * (lowJumpMultiplier - 1f) * Time.fixedDeltaTime;
        }
    }

    // ================= RPCs (Visual Effects) =================

    [Command]
    private void CmdOnJump()
    {
        RpcOnJump();
    }

    // Remote Procedure Call
    [ClientRpc]
    private void RpcOnJump()
    {
        // Play jump animation, sound, particle effect
    }

    [Command]
    private void CmdOnDive()
    {
        RpcOnDive();
    }

    [ClientRpc]
    private void RpcOnDive()
    {
        // Play dive animation, sound, particle effect
    }

    // ================= DEBUG =================

    private void OnDrawGizmos()
    {
        Vector3 checkPos = groundCheck != null ? groundCheck.position : transform.position + Vector3.down * 0.1f;
        Gizmos.color = Application.isPlaying && isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(checkPos, groundCheckRadius);

        Gizmos.color = Color.blue;
        Gizmos.DrawRay(transform.position + Vector3.up, transform.forward * 2f);
    }
}