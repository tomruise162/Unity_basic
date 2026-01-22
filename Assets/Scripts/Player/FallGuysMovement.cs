using UnityEngine;
using Mirror;

/// <summary>
/// Fall Guys style Movement Controller with Server Validation
/// 
/// Architecture:
/// 1. Client reads input and moves locally (for responsive feel)
/// 2. NetworkTransform syncs position to server and other clients
/// 3. Server validates important actions (jump, dive) via Commands
/// 4. Server broadcasts effects via RPCs
/// 5. Coin collection uses full server authority
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
    [SerializeField] private float jumpBufferTime = 0.15f;
    [SerializeField] private float coyoteTime = 0.1f;

    [Header("Dive")]
    [SerializeField] private float diveForce = 12f;
    [SerializeField] private float diveCooldown = 1f;

    [Header("Physics Feel")]
    [SerializeField] private float fallMultiplier = 2.5f;
    [SerializeField] private float lowJumpMultiplier = 2f;

    [Header("Camera Reference")]
    [SerializeField] private Transform cameraTransform;

    [Header("Coin System")]
    [SyncVar(hook = nameof(OnCoinCountChanged))]
    public int coinCount = 0;

    [Header("Visual Effects")]
    [SerializeField] private ParticleSystem jumpParticles;
    [SerializeField] private ParticleSystem diveParticles;
    [SerializeField] private AudioClip jumpSound;
    [SerializeField] private AudioClip diveSound;
    [SerializeField] private AudioClip coinSound;

    // ===== Client input state =====
    private Vector2 inputDirection;
    private bool jumpPressed;
    private bool divePressed;
    private bool jumpHeld;

    // ===== Movement state =====
    private Vector3 moveDirection;
    private float jumpBufferTimer;
    private float lastGroundedTime;

    // ===== State (synced with server for validation) =====
    [SyncVar] // Server tracks if player is grounded for validation
    private bool isGrounded;
    private bool wasGrounded;

    [SyncVar] // Server tracks dive state to prevent spam
    private bool isDiving;

    [SyncVar] // Server tracks last dive time for cooldown validation
    private float lastDiveTime;

    // ===== Components =====
    private Rigidbody rb;
    private AudioSource audioSource;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        rb.freezeRotation = true;
    }

    // ================= NETWORK LIFECYCLE =================

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!isLocalPlayer)
        {
            ChangePlayerColor(Color.blue);
        }
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        var networkTransform = GetComponent<Mirror.NetworkTransformHybrid>();
        if (networkTransform == null)
        {
            Debug.LogError("[LOCAL] NetworkTransformHybrid not found!");
        }
        else if (networkTransform.syncDirection != Mirror.SyncDirection.ClientToServer)
        {
            Debug.LogError($"[LOCAL] NetworkTransform must be ClientToServer! Current: {networkTransform.syncDirection}");
        }

        rb.isKinematic = false;

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        ChangePlayerColor(Color.green);
    }

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

    // ================= COIN SYSTEM (Server Authority) =================

    private void OnCoinCountChanged(int oldValue, int newValue)
    {
        Debug.Log($"[HOOK] Coins: {oldValue} -> {newValue}");

        if (isLocalPlayer)
        {
            // Update UI here
            // CoinUI.UpdateCount(newValue);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Only local player detects collision
        if (!isLocalPlayer) return;
        if (!other.CompareTag("Coin")) return;

        NetworkIdentity coinNi = other.GetComponent<NetworkIdentity>();
        if (coinNi == null)
        {
            Debug.LogError("[CLIENT] Coin has no NetworkIdentity!");
            return;
        }

        Debug.Log($"[CLIENT] Requesting coin pickup: netId={coinNi.netId}");

        // COMMAND: Send pickup request to server for validation
        CmdPickupCoin(coinNi.netId);
    }

    /// <summary>
    /// COMMAND: Client requests coin pickup, server validates and processes
    /// This prevents cheating - only server can actually give coins
    /// </summary>
    [Command]
    private void CmdPickupCoin(uint coinNetId)
    {
        Debug.Log($"[SERVER] CmdPickupCoin called by player {netId} for coin {coinNetId}");

        // SERVER VALIDATION 1: Does coin still exist?
        if (!NetworkServer.spawned.TryGetValue(coinNetId, out NetworkIdentity coinNi))
        {
            Debug.Log($"[SERVER] Coin {coinNetId} not found (already collected)");
            return;
        }

        // SERVER VALIDATION 2: Is player close enough to coin?
        float distance = Vector3.Distance(transform.position, coinNi.transform.position);
        if (distance > 3f) // Max pickup range
        {
            Debug.LogWarning($"[SERVER] Player {netId} too far from coin! Distance: {distance}");
            return;
        }

        // SERVER: Grant coin (SyncVar automatically syncs to all clients)
        coinCount++;
        Debug.Log($"[SERVER] Player {netId} collected coin. Total: {coinCount}");

        Vector3 coinPos = coinNi.transform.position;

        // SERVER: Destroy coin (Mirror syncs destruction to all clients)
        NetworkServer.Destroy(coinNi.gameObject);

        // RPC: Notify all clients to play pickup effects
        RpcOnCoinPickedUp(coinPos);

        // TARGET RPC: Send personal feedback to the player who collected it
        TargetOnYouPickedUpCoin(connectionToClient, coinCount);
    }

    /// <summary>
    /// CLIENT RPC: Plays visual/audio effects for all players
    /// Everyone sees the coin collection happen
    /// </summary>
    [ClientRpc]
    private void RpcOnCoinPickedUp(Vector3 coinPosition)
    {
        Debug.Log($"[RPC-ALL] Coin collected at {coinPosition}");

        // Play particle effect at coin position
        if (jumpParticles != null)
        {
            Instantiate(jumpParticles, coinPosition, Quaternion.identity);
        }

        // Play sound (quieter for remote players)
        if (coinSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(coinSound, isLocalPlayer ? 1f : 0.3f);
        }
    }

    /// <summary>
    /// TARGET RPC: Personal feedback only for the player who collected the coin
    /// Only this specific client receives this message
    /// </summary>
    [TargetRpc]
    private void TargetOnYouPickedUpCoin(NetworkConnection target, int totalCoins)
    {
        Debug.Log($"[RPC-TARGET] YOU collected a coin! Total: {totalCoins}");

        // Play louder sound for personal feedback
        if (coinSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(coinSound, 1.5f);
        }

        // Show "+1 COIN" popup UI here
        // CoinUI.ShowPopup("+1");
    }

    // ================= MOVEMENT (Client Prediction + Server Validation) =================

    [Client]
    private void Update()
    {
        if (!isLocalPlayer) return;
        if (NetworkClient.connection == null || !NetworkClient.ready) return;

        ReadInput();

        // Calculate movement direction relative to camera
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

        if (inputDirection.sqrMagnitude > 0.001f)
        {
            Vector3 dir = camForward * inputDirection.y + camRight * inputDirection.x;
            moveDirection = dir.sqrMagnitude > 1f ? dir.normalized : dir;
        }
        else
        {
            moveDirection = Vector3.zero;
        }

        // Jump buffering
        if (jumpPressed)
        {
            jumpBufferTimer = jumpBufferTime;
            jumpPressed = false;
        }

        // Dive input
        if (divePressed && CanDive())
        {
            // Send dive request to server for validation
            CmdRequestDive();
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

        if (Input.GetKeyDown(KeyCode.Space))
            jumpPressed = true;

        jumpHeld = Input.GetKey(KeyCode.Space);

        if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.LeftControl))
            divePressed = true;

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    [Client]
    private void FixedUpdate()
    {
        if (!isLocalPlayer) return;

        if (rb.isKinematic)
        {
            rb.isKinematic = false;
            Debug.LogWarning("[LOCAL] Rigidbody was kinematic! Fixed.");
        }

        CheckGround();

        if (jumpBufferTimer > 0f)
        {
            jumpBufferTimer -= Time.fixedDeltaTime;
        }

        if (isGrounded)
        {
            lastGroundedTime = Time.time;
        }

        // Jump with buffer and coyote time
        bool canJump = jumpBufferTimer > 0f &&
                       (isGrounded || Time.time < lastGroundedTime + coyoteTime);

        if (canJump)
        {
            // Client performs jump immediately for responsiveness
            PerformJump();
            jumpBufferTimer = 0f;

            // Send jump command to server for validation & effects
            CmdRequestJump();
        }

        ApplyMovement();
        ApplyRotation();
        ApplyBetterJumpPhysics();
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

        Vector3 horizontalVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        Vector3 newHorizontalVel = Vector3.MoveTowards(
            horizontalVel,
            targetVelocity,
            accelRate * Time.fixedDeltaTime
        );

        rb.linearVelocity = new Vector3(newHorizontalVel.x, rb.linearVelocity.y, newHorizontalVel.z);
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
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
    }

    [Client]
    private bool CanDive()
    {
        return !isGrounded && !isDiving && Time.time > lastDiveTime + diveCooldown;
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

    // ================= SERVER VALIDATION & RPCs =================

    /// <summary>
    /// COMMAND: Client requests to jump, server validates and broadcasts effect
    /// Server can reject if player is not actually grounded
    /// </summary>
    [Command]
    private void CmdRequestJump()
    {
        Debug.Log($"[SERVER] Player {netId} requested jump. Grounded: {isGrounded}");

        // SERVER VALIDATION: Is player allowed to jump?
        // Note: Due to latency, client might think it's grounded but server doesn't
        // For better feel, we can be lenient here (trust client for jump)
        // But for competitive games, you'd enforce strict server-side checks

        if (!isGrounded)
        {
            // Optional: Reject jump if server thinks player is in air
            // Debug.LogWarning($"[SERVER] Rejected jump - player {netId} not grounded on server");
            // return;
        }

        // Broadcast jump effect to all clients
        RpcOnJump();
    }

    /// <summary>
    /// CLIENT RPC: Play jump effects for all players
    /// Everyone sees/hears the jump happen
    /// </summary>
    [ClientRpc]
    private void RpcOnJump()
    {
        Debug.Log("[RPC-ALL] Playing jump effect");

        // Play particle effect
        if (jumpParticles != null)
        {
            jumpParticles.Play();
        }

        // Play jump sound
        if (jumpSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(jumpSound);
        }

        // Trigger jump animation
        // GetComponent<Animator>()?.SetTrigger("Jump");
    }

    /// <summary>
    /// COMMAND: Client requests to dive, server validates cooldown and broadcasts
    /// Prevents spam and ensures fair gameplay
    /// </summary>
    [Command]
    private void CmdRequestDive()
    {
        Debug.Log($"[SERVER] Player {netId} requested dive");

        // SERVER VALIDATION 1: Is player in air?
        if (isGrounded)
        {
            Debug.LogWarning($"[SERVER] Rejected dive - player {netId} is grounded");
            return;
        }

        // SERVER VALIDATION 2: Is cooldown ready?
        if (isDiving || Time.time < lastDiveTime + diveCooldown)
        {
            Debug.LogWarning($"[SERVER] Rejected dive - player {netId} on cooldown");
            return;
        }

        // SERVER: Update state (SyncVars will sync to all clients)
        isDiving = true;
        lastDiveTime = Time.time;

        // SERVER: Apply dive physics
        Vector3 diveDir = transform.forward + Vector3.down * 0.3f;
        diveDir.Normalize();

        Rigidbody serverRb = GetComponent<Rigidbody>();
        serverRb.linearVelocity = Vector3.zero;
        serverRb.AddForce(diveDir * diveForce, ForceMode.Impulse);
        serverRb.AddTorque(Vector3.right * 5f, ForceMode.Impulse);

        Debug.Log($"[SERVER] Dive approved for player {netId}");

        // Broadcast dive effect to all clients
        RpcOnDive();
    }

    /// <summary>
    /// CLIENT RPC: Play dive effects for all players
    /// Everyone sees the dive animation and effects
    /// </summary>
    [ClientRpc]
    private void RpcOnDive()
    {
        Debug.Log("[RPC-ALL] Playing dive effect");

        // Play particle effect (dive trail)
        if (diveParticles != null)
        {
            diveParticles.Play();
        }

        // Play dive sound
        if (diveSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(diveSound);
        }

        // Trigger dive animation
        // GetComponent<Animator>()?.SetTrigger("Dive");
    }

    // ================= DEBUG =================

    private void OnDrawGizmos()
    {
        Vector3 checkPos = groundCheck != null ? groundCheck.position : transform.position + Vector3.down * 0.1f;
        Gizmos.color = Application.isPlaying && isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(checkPos, groundCheckRadius);

        Gizmos.color = Color.blue;
        Gizmos.DrawRay(transform.position + Vector3.up, transform.forward * 2f);

        // Draw pickup range
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 3f);
    }
}