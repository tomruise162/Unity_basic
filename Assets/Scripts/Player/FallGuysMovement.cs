using UnityEngine;
using Mirror;

/// <summary>
/// Fall Guys style Movement Controller with Server Validation
/// 
/// Architecture:
/// 1. Client reads input and moves locally (for responsive feel)
/// 2. NetworkTransform syncs position to server and other clients
/// 3. Server validates important actions (jump) via Commands
/// 4. Coin collection uses full server authority
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

    [Header("Physics Feel")]
    [SerializeField] private float fallMultiplier = 2.5f;
    [SerializeField] private float lowJumpMultiplier = 2f;

    [Header("Camera Reference")]
    [SerializeField] private Transform cameraTransform;

    [Header("Coin System")]
    [SyncVar(hook = nameof(OnCoinCountChanged))]
    public int coinCount = 0;

    // ===== Client input state =====
    private Vector2 inputDirection;
    private bool jumpPressed;
    private bool jumpHeld;

    // ===== Movement state =====
    private Vector3 moveDirection;
    private float jumpBufferTimer;
    private float lastGroundedTime;

    // ===== State =====
    private bool isGrounded;
    private bool wasGrounded;

    // ===== Components =====
    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        rb.interpolation = RigidbodyInterpolation.Interpolate;
        // Sử dụng check collision descrete để máy không phải xử lý liên tục.
        // Tuy nhiên có những rủi ro trong các game yêu cầu collision chính xác.
        // Ví dụ: game bắn súng, các game có skill dash/teleport, game đua xe,...
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

        // Sử dụng Client auhtority với NetworkTransformHybrid
        // Client gửi position, rotation, lên server
        // Server sync với các clients khác 
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

        // SERVER: Destroy coin (Mirror syncs destruction to all clients)
        NetworkServer.Destroy(coinNi.gameObject);
    }

    // ================= MOVEMENT (Client Prediction) =================

    [Client]
    private void Update()
    {
        if (!isLocalPlayer) return;
        if (NetworkClient.connection == null || !NetworkClient.ready) return;

        ReadInput();
        UpdateMoveDirection();

        // Jump buffering
        if (jumpPressed)
        {
            jumpBufferTimer = jumpBufferTime;
            jumpPressed = false;
        }
    }

    // Đọc input từ bàn phím và lưu lại
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

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    // Tính hướng di chuyển theo hướng camera đang nhìn.
    // Nếu không player sẽ luôn di chuyển theo hướng cố định của scene
    // Ví dụ:
    // Camera đang nhìn về phía Đông
    // Bạn bấm W
    // Nhân vật sẽ chạy về phía Đông (theo camera)
    // NOTE: Trả lời cho câu hỏi đi đâu, đi theo hướng nào
    private void UpdateMoveDirection()
    {
        Vector3 camForward = Vector3.forward;
        Vector3 camRight = Vector3.right;

        if (cameraTransform != null)
        {
            camForward = cameraTransform.forward;
            camRight = cameraTransform.right;
            camForward.y = 0f;
            camRight.y = 0f;
            // camForward.Normalize();
            // camRight.Normalize();
            // Debug.Log("Camera forward: " + camForward);
            // Debug.Log("Camera right: " + camRight);
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
            PerformJump();
            jumpBufferTimer = 0f;
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
    }

    // Tạo logic di chuyển vật lý cho player
    // NOTE: Trả lời cho câu hỏi di chuyển bằng logic gì
    [Client]
    private void ApplyMovement()
    {
        // Vận tốc mà ta muốn đạt tới
        Vector3 targetVelocity = moveDirection * moveSpeed;
        // Apply gia tốc/giảm tốc để làm mượt movement
        float accelRate = moveDirection.sqrMagnitude > 0.001f ? acceleration : deceleration;

        if (!isGrounded)
            accelRate *= airControlMultiplier;

        // Lấy vận tốc hiện tại
        Vector3 horizontalVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        // Sử dụng MoveTowards để đưa vận tốc hiện tại tới vận tốc mà ta muốn đạt tới
        // Thay đổi dần theo mỗi đợt update vật lý (tick của FixedUpdate)
        // Không thay đổi ngay lập tức (teleport)
        Vector3 newHorizontalVel = Vector3.MoveTowards(
            horizontalVel,
            targetVelocity,
            accelRate * Time.fixedDeltaTime
        );

        // Update lại vận tốc hiện tại
        rb.linearVelocity = new Vector3(newHorizontalVel.x, rb.linearVelocity.y, newHorizontalVel.z);
    }

    // Tạo logic quay cho player
    // TODO: Tại sao lại luôn set trục Oy là 0f
    [Client]
    private void ApplyRotation()
    {
        Vector3 horizontalVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        if (horizontalVel.sqrMagnitude < 0.01f) return;

        // Giữ nhân vật đứng thẳng theo trục Oy (Vector3.up)
        // Nhân vật sẽ quay được trái phải, góc quay không bị nghiêng hay  
        // Quaternion giải quyết vấn đề gimbal lock (bị mất một trục quay khi rotate 2 trục trùng nhau),
        // không tạo ra các góc máy quay ngoài quỹ đạo của 3 trục, thay đổi thất thường, ...
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