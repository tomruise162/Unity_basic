using UnityEngine;
using Mirror;

/// <summary>
/// Fall Guys style Movement Controller with Coin Collection (Mirror, server-authoritative)
/// FIXED: Added proper velocity syncing for client movement visibility
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
    [SerializeField] private float rotationSpeed = 720f;

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
    [SyncVar(hook = nameof(OnCoinCountChanged))]
    public int coinCount = 0;

    // ===== SYNC VARIABLES FOR CLIENT PREDICTION =====
    // These ensure clients see smooth movement even though server calculates physics
    [SyncVar]
    private Vector3 syncedVelocity;

    [SyncVar]
    private bool syncedIsGrounded;

    // ===== Client input state =====
    private Vector2 inputDirection;
    private bool jumpPressed;
    private bool divePressed;
    private bool jumpHeld;

    // ===== Server-applied movement state =====
    private Vector3 serverMoveDirection;
    private bool serverJumpHeld;

    // Jump buffering on server
    private float serverJumpBufferTimer;
    private float serverLastGroundedTime;

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

    public override void OnStartServer()
    {
        base.OnStartServer();
        Debug.Log($"[SERVER] Player spawned on server. NetId: {netId}");
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        Debug.Log($"[CLIENT] Player appeared on client. NetId: {netId}, IsLocalPlayer: {isLocalPlayer}");
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        Debug.Log($"[LOCAL] This is MY player! NetId: {netId}");

        // Auto-assign camera if not set
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        // Change color for local player
        var renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = Color.green;
        }
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        Debug.Log($"[SERVER] Player destroyed on server. NetId: {netId}");
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        Debug.Log($"[CLIENT] Player destroyed on client. NetId: {netId}");
    }

    // ================= COIN SYSTEM =================

    private void OnCoinCountChanged(int oldValue, int newValue)
    {
        Debug.Log($"[HOOK] coinCount changed: {oldValue} -> {newValue}");

        if (isLocalPlayer)
        {
            CoinUI.NotifyCoinChanged(newValue);
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

    [Command]
    private void CmdPickupCoin(uint coinNetId)
    {
        if (!NetworkServer.spawned.TryGetValue(coinNetId, out NetworkIdentity coinNi))
        {
            Debug.Log($"[SERVER] Coin {coinNetId} not found - maybe already picked up");
            return;
        }

        coinCount++;
        Debug.Log($"[SERVER] Player {netId} picked up coin. Total: {coinCount}");

        NetworkServer.Destroy(coinNi.gameObject);
        RpcOnCoinPickedUp(coinNi.gameObject.transform.position);
        TargetOnYouPickedUpCoin(connectionToClient, coinCount);
    }

    [ClientRpc]
    private void RpcOnCoinPickedUp(Vector3 coinPosition)
    {
        Debug.Log($"[RPC-ALL] A coin was picked up at {coinPosition}");
    }

    [TargetRpc]
    private void TargetOnYouPickedUpCoin(NetworkConnection target, int totalCoins)
    {
        Debug.Log($"[RPC-TARGET] YOU picked up a coin! Total: {totalCoins}");
    }

    // ================= MOVEMENT INPUT =================

    private void Update()
    {
        if (!isLocalPlayer) return;
        if (NetworkClient.connection == null || !NetworkClient.ready) return;

        ReadInput();

        Vector3 camForward = Vector3.forward;
        Vector3 camRight = Vector3.right;

        if (cameraTransform != null)
        {
            camForward = cameraTransform.forward;
            camRight = cameraTransform.right;
            camForward.y = 0f;
            camRight.y = 0f;
            camForward.Normalize();
            camRight.Normalize();
        }

        CmdSendInput(inputDirection, camForward, camRight, jumpPressed, divePressed, jumpHeld);

        if (jumpPressed)
        {
            jumpPressed = false;
        }

        divePressed = false;
    }

    private void ReadInput()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        inputDirection = new Vector2(h, v);

        if (inputDirection.sqrMagnitude > 1f)
            inputDirection.Normalize();

        if (Input.GetKeyDown(KeyCode.Space))
        {
            jumpPressed = true;
            Debug.Log("[CLIENT] Jump button pressed!");
        }

        jumpHeld = Input.GetKey(KeyCode.Space);

        if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.LeftControl))
            divePressed = true;

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    [Command]
    private void CmdSendInput(Vector2 rawInput, Vector3 camForward, Vector3 camRight, bool jump, bool dive, bool heldJump)
    {
        if (rawInput.sqrMagnitude > 0.001f)
        {
            Vector3 dir = camForward * rawInput.y + camRight * rawInput.x;
            serverMoveDirection = dir.sqrMagnitude > 1f ? dir.normalized : dir;
        }
        else
        {
            serverMoveDirection = Vector3.zero;
        }

        if (jump)
        {
            serverJumpBufferTimer = jumpBufferTime;
            Debug.Log("[SERVER] Jump input received, buffer set!");
        }

        if (dive && CanDive())
        {
            PerformDive();
        }

        serverJumpHeld = heldJump;
    }

    // ================= MOVEMENT PHYSICS =================

    private void FixedUpdate()
    {
        // SERVER: Calculate all physics
        if (isServer)
        {
            CheckGround();

            if (serverJumpBufferTimer > 0f)
            {
                serverJumpBufferTimer -= Time.fixedDeltaTime;
            }

            if (isGrounded)
            {
                serverLastGroundedTime = Time.time;
            }

            bool canJump = serverJumpBufferTimer > 0f &&
                           (isGrounded || Time.time < serverLastGroundedTime + coyoteTime);

            if (canJump)
            {
                PerformJump();
                serverJumpBufferTimer = 0f;
            }

            ApplyMovement();
            ApplyRotation();
            ApplyBetterJumpPhysics();

            // Sync velocity to clients
            syncedVelocity = rb.linearVelocity;
            syncedIsGrounded = isGrounded;

            wasGrounded = isGrounded;
        }
        // CLIENT: Apply synced velocity for smooth interpolation
        else if (isClient)
        {
            // Optional: Apply synced velocity to rigidbody for smoother client-side prediction
            // This helps if NetworkRigidbody isn't syncing velocity properly
            if (rb.linearVelocity != syncedVelocity)
            {
                rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, syncedVelocity, Time.fixedDeltaTime * 10f);
            }

            isGrounded = syncedIsGrounded;
        }
    }

    private void CheckGround()
    {
        Vector3 checkPos = groundCheck != null
            ? groundCheck.position
            : transform.position + Vector3.down * 0.1f;

        isGrounded = Physics.CheckSphere(checkPos, groundCheckRadius, groundMask, QueryTriggerInteraction.Ignore);

        if (isGrounded && isDiving)
            isDiving = false;
    }

    private void ApplyMovement()
    {
        if (isDiving) return;

        Vector3 targetVelocity = serverMoveDirection * moveSpeed;

        float accelRate = serverMoveDirection.sqrMagnitude > 0.001f ? acceleration : deceleration;
        if (!isGrounded)
            accelRate *= airControlMultiplier;

        Vector3 horizontalVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        Vector3 newHorizontalVel = Vector3.MoveTowards(
            horizontalVel,
            targetVelocity,
            accelRate * Time.fixedDeltaTime
        );

        rb.linearVelocity = new Vector3(newHorizontalVel.x, rb.linearVelocity.y, newHorizontalVel.z);
    }

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

    private void PerformJump()
    {
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        Debug.Log("[SERVER] Player jumped!");
        RpcOnJump();
    }

    private bool CanDive()
    {
        return !isGrounded && !isDiving && Time.time > lastDiveTime + diveCooldown;
    }

    private void PerformDive()
    {
        isDiving = true;
        lastDiveTime = Time.time;

        Vector3 diveDir = transform.forward + Vector3.down * 0.3f;
        diveDir.Normalize();

        rb.linearVelocity = Vector3.zero;
        rb.AddForce(diveDir * diveForce, ForceMode.Impulse);
        rb.AddTorque(Vector3.right * 5f, ForceMode.Impulse);

        RpcOnDive();
    }

    private void ApplyBetterJumpPhysics()
    {
        if (rb.linearVelocity.y < 0f)
        {
            rb.linearVelocity += Vector3.up * Physics.gravity.y * (fallMultiplier - 1f) * Time.fixedDeltaTime;
        }
        else if (rb.linearVelocity.y > 0f && !serverJumpHeld)
        {
            rb.linearVelocity += Vector3.up * Physics.gravity.y * (lowJumpMultiplier - 1f) * Time.fixedDeltaTime;
        }
    }

    // ================= RPCs =================

    [ClientRpc]
    private void RpcOnJump()
    {
        Debug.Log($"[RPC] Player {netId} jumped!");
    }

    [ClientRpc]
    private void RpcOnDive()
    {
        Debug.Log($"[RPC] Player {netId} dived!");
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