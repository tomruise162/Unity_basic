using UnityEngine;
using Mirror;
using Mirror.Examples.Billiards;
using Mirror.Examples.RigidbodyBenchmark;
using NUnit.Framework;

public class SelfMovement: NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float acceleration = 50f;
    [SerializeField] private float deceleration = 40f;
    [SerializeField] private float airControlMultiplier = 0.4f;

    [Header("Rotation")]
    [SerializeField] private float rotationSpeed = 720f;

    [Header("Jump")]
    [SerializeField] private float jumpForce = 6f;
    [SerializeField] private float jumpBufferTime = 0.2f;
    [SerializeField] private float coyoteTime = 0.15f;
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.3f;
    [SerializeField] private LayerMask groundMask;

    [Header("Physic Feel")]
    [SerializeField] private float jumpFallMultiplier = 2.5f;
    [SerializeField] private float lowJumpMultiplier = 2f;

    [Header("Camera Refference")]
    [SerializeField] private Transform cameraTransform;

    [Header("Coin System")]
    [SyncVar(hook = nameof(OnCoinCountChanged))]
    public int coinCount = 0;

    // Var for input 
    private Vector2 inputDirection;
    private bool jumpHeld;
    private bool jumpPressed;

    // Var for movement
    private Vector3 moveDirection;
    private float jumpBufferTimer;
    private float lastGroundTimer;

    // Var for state
    private bool isGrounded;

    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        // Nội suy các thông tin cho frame ở giữa các khoảng thời gian Update và FixedUpdate
        // để movement và scene không bị giật do chưa kịp nhận thông tin từ FixedUpdate
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        // Sử dụng check collision descrete để máy không phải xử lý liên tục.
        // Tuy nhiên có những rủi ro trong các game yêu cầu collision chính xác.
        // Ví dụ: game bắn súng, các game có skill dash/teleport, game đua xe,...
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        rb.freezeRotation = true;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!isLocalPlayer)
        {
            ChangePlayerColor(Color.blue);
        }
    }

    // Hàm chỉ chạy trên local player, để check synchronization
    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        // Lấy component NetworkTransformHybrid đã được gắn lên object này để sử dụng
        var networkTransform = GetComponent<NetworkTransformHybrid>();
        // Nếu không có component NetworkTransformHybrid
        if (networkTransform == null)
        {
            Debug.LogError("[Local] NetworkTransformHybrid is not found");
        }
        // Nếu syncDirection không đúng
        else if (networkTransform.syncDirection != Mirror.SyncDirection.ClientToServer)
        {
            Debug.LogError($"[Local] NetworkTransform must be ClientToServer. Current: {networkTransform.syncDirection}");
        }

        // Option cho phép điều khiển object dựa trên các tác động vật lý
        // Nếu set isKinematic = true thì object chỉ di chuyển theo scipt viết sẵn
        rb.isKinematic = false;
        // Nếu chưa gán cameraTransform thì gán mặc định là Camera.main
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        ChangePlayerColor(Color.green);
    }

    private void ChangePlayerColor(Color color)
    {
        // Lấy Renderer đã được gắn lên object này
        var renderer = GetComponent<Renderer>();
        // Check xem có Renderer và Material hay không
        if (renderer != null && renderer.material != null)
        {
            // Nếu có, tạo một bản sao Material riêng cho player này
            renderer.material = new Material(renderer.material);
            // Đổi màu
            renderer.material.color = color;
        }

        // Trường hợp Object gốc chỉ là container và Model nằm trong 1 object con
        else
        {
            // Check tương tự đối với object con
            var childRenderer = GetComponentInChildren<Renderer>();
            if (childRenderer != null && childRenderer.material != null)
            {
                childRenderer.material = new Material(childRenderer.material);
                childRenderer.material.color = color;
            }
        }
    }

    private void OnCoinCountChanged(int oldValue, int newValue)
    {
        Debug.Log($"[HOOK] Coin: {oldValue} -> {newValue}");
    }

    [Command]
    // Hàm nhận argument netId từ NetworkIdentity của object coin,
    private void CmdPickupCoin(uint coinID)
    {   
        // coinID chính là coinNi.netId được gửi từ hàm OnTriggerEnter
        // còn netId ở đây là netId của local player
        Debug.Log($"[Server] CmdPickupCoin is called by {netId} for coin {coinID}");
        // Kiểm tra danh sách các NetworkObject đang được server quản lý
        // NetworkServer.spawned là Dictionary<netId, NetworkIdentity>
        //
        // TryGetValue(coinID, out coinNi):
        // - Tìm object có netId = coinID trong dictionary
        // - Nếu tồn tại: trả về true và gán NetworkIdentity vào biến coinNi
        // - Nếu không tồn tại: trả về false, coinNi không hợp lệ
        if (!NetworkServer.spawned.TryGetValue(coinID, out NetworkIdentity coinNi))
        {
            Debug.LogError($"[Server] Not found coin {coinID}");
            return;
        }

        // Nếu có netId = coinID, thực hiện cộng coin cho player
        coinCount++;
        Debug.Log($"[Server] Player {netId} collected coin {coinNi.netId}. Total count: {coinCount}");
        NetworkServer.Destroy(coinNi.gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isLocalPlayer) return;
        if (other.gameObject.layer != LayerMask.NameToLayer("Coin")) return;

        // Tạo biến coinNi với kiểu dữ NetworkIdentity
        // Lấy component NetworkIdentity từ các object mà cho phép collide với player
        NetworkIdentity coinNi = other.GetComponent<NetworkIdentity>();
        if (coinNi == null)
        {
            Debug.LogError("[CLIENT] Coin has no NetworkIdentity!");
            return;
        }

        // Nếu object coin đó có NetworkIdentity thì tạo request pickup coin
        // với property là netId
        Debug.Log($"Request pickup coin {coinNi.netId}");
        CmdPickupCoin(coinNi.netId);
    }

    [Client]
    private void Update()
    {
        if (!isLocalPlayer) return;
        if (NetworkClient.connection == null || !NetworkClient.ready) return;

        ReadInput();
        UpdateMoveDirection();

        if (jumpPressed)
        {
            jumpBufferTimer = jumpBufferTime;
            jumpPressed = false;
        }
    }

    [Client]

    private void FixedUpdate()
    {
        if(!isLocalPlayer) return;
        if (rb.isKinematic)
        {
            rb.isKinematic = false;
            Debug.LogWarning("[LOCAL] Rigidbody was kinematic! Fixed.");
        }

        CheckGround();
        
        if(jumpBufferTimer > 0f)
        {
            jumpBufferTimer -= Time.fixedDeltaTime;
        }

        if (isGrounded)
        {
            lastGroundTimer = Time.time;
        }

        bool canJump = jumpBufferTimer > 0f && (isGrounded || (Time.time <= lastGroundTimer + coyoteTime));

        if (canJump)
        {
            PerformJump();
            jumpBufferTimer = 0f;
        }

        ApplyMovement();
        ApplyRotation();
        ApplyBetterJumpPhysics();
        Physics.SyncTransforms();
    }

    private void ReadInput()
    {
        // GetAxisRaw nhận giá trị thô: giả sử nhấn W thì giá trị sẽ thay đổi 0->1
        // chứ không tăng dần lên. Chỉ được dùng với mục đích lấy hướng
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        inputDirection = new Vector2(h,v);

        if(inputDirection.sqrMagnitude > 1f)
        {
            inputDirection.Normalize();
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            jumpPressed = true;
        }

        jumpHeld = Input.GetKey(KeyCode.Space);

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }


    private void UpdateMoveDirection()
    {   
        // Lấy hướng phía trước và bên phải của camera (trục Oz và Ox)
        Vector3 camForward = cameraTransform.forward; // Oz
        Vector3 camRight = cameraTransform.right; // Ox
        // Set cố định trục Oy = 0 để khi hướng camera lên xuống thì không bị Oy ảnh hưởng tới độ lớn
        // Ví dụ: 
        // Khi cam nhìn thẳng: forward ≈ (0, 0, 1)
        // Khi cam cúi xuống: forward ≈ (0, -0.9, 0.4)
        // Object đi chậm hơn cho bị chi phối bởi trục Oy
        camForward.y = 0f;
        camRight.y = 0f;

        // Chuẩn hóa vector khi ta không xét tới trục Oy nữa:
        // Ví dụ: camera cúi xuống, camForward ≈ (0, -0.91, 0.41)
        // Nhưng vì ta đã apply camForward.y = 0f; Vector trở thành: (0, 0, 0.41)
        // Điều này dẫn đến việc độ lớn của vector sẽ bị giảm đi do ta không còn độ lớn từ Oy nữa
        // Cần normalize lại để độ lớn của vector trở về 1
        camForward.Normalize();
        camRight.Normalize();   

        if (inputDirection.sqrMagnitude > 0.001f)
        {   
            // Chuyển input 2D từ bàn phím thành input 3D theo camera
            Vector3 dir = camForward * inputDirection.y + camRight * inputDirection.x;
            moveDirection = dir.sqrMagnitude > 1f ? dir.normalized : dir;
        }
        else
        {   
            // Nếu không có input gì thì đứng yên
            moveDirection = Vector3.zero;
        }
    }

    [Client]
    // Xử lý việc di chuyển cho object: 
    // Di chuyển nhanh chậm thế nào dựa trên các thành phần và logic gì
    private void ApplyMovement()
    {   
        // Tạo một vận tốc mà ta muốn object player đạt được 
        // (tăng vận tốc cơ bản bằng moveSpeed)
        Vector3 targetVelocity = moveDirection * moveSpeed;
        // Apply gia tốc/giảm tốc để làm mượt movement
        float accelerate = moveDirection.sqrMagnitude > 0.001f ? acceleration : deceleration;

        if (!isGrounded)
        {
            accelerate *= airControlMultiplier;
        }
        // Lấy vận tốc hiện tại theo phương ngang (loại bỏ thành phần y)
        Vector3 horizontalVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        // Sử dụng MoveTowards để đưa vận tốc hiện tại tới vận tốc mà ta muốn đạt tới
        // Thay đổi dần theo mỗi đợt update vật lý (tick của FixedUpdate)
        // Không thay đổi ngay lập tức (teleport)
        Vector3 newHorizontalVel = Vector3.MoveTowards(
            horizontalVel, 
            targetVelocity, 
            accelerate * Time.fixedDeltaTime); // Chỉ được tăng/giảm vận tốc bao nhiêu trong một tick
        // Áp dụng vận tốc mới cho Rigidbody, giữ nguyên vận tốc của trục Oy
        rb.linearVelocity = new Vector3(newHorizontalVel.x, rb.linearVelocity.y, newHorizontalVel.z);

    }

    [Client]
    private void ApplyRotation()
    {   
        // Lấy vận tốc theo phương ngang (Vì di chuyển chỉ tác động tới trục Ox và Oz)
        Vector3 horizontalVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        if (horizontalVel.sqrMagnitude < 0.001f) return;

        // Tạo ra một rotation (góc quay) để object player quay mặt về một hướng nào đó
        // “Hãy quay object sao cho mặt trước của nó nhìn về hướng này(horizontalVel.normalized)”
        // Thực hiện quay bằng trục Vector3.up(Oy)
        Quaternion targetRotation = Quaternion.LookRotation(horizontalVel.normalized, Vector3.up);
        // Phép xoay: Thực hiện xoay từ dần từ hướng hiện tại tới hướng object muốn di chuyển
        // theo tick update vật lý của game
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, 
            targetRotation, 
            rotationSpeed * Time.fixedDeltaTime); // Chỉ được xoay bao nhiêu độ trong một tick 
    }

    private void CheckGround()
    {
        Vector3 checkPos = groundCheck != null ? groundCheck.position : transform.position + Vector3.down * 0.1f;
        // QueryTriggerInteraction.Ignore:
        // Khi kiểm tra collider, đừng tính những collider nào đang là Trigger
        // Tránh việc nhân vật có thể nhảy vô hạn do collide với trigger collider
        isGrounded = Physics.CheckSphere(checkPos, groundCheckRadius, groundMask, QueryTriggerInteraction.Ignore);
    }
    private void PerformJump()
    {   
        // Reset vận tốc theo trục Y về 0 trước khi nhảy
        // để đảm bảo lực nhảy luôn nhất quán.
        //
        // Nếu không reset:
        // - Khi object đang bay lên (velocity Y > 0) mà nhảy tiếp
        //   → vận tốc cũ + lực mới → nhảy cao bất thường
        // - Khi object đang rơi xuống (velocity Y < 0) mà nhảy
        //   → lực nhảy bị triệt tiêu một phần → nhảy thấp hơn
        //
        // Reset Y giúp mỗi lần nhảy đều bắt đầu từ cùng một trạng thái.
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
    }

    [Client]
    // Làm hành động nhảy mượt hơn bằng cách add thêm lực cho gravity
    // Logic chung là lấy Trục Oy hướng lên hợp với Vector trọng lực hướng xuống (add thêm lực cho gravity)
    // Giúp nhân vật rơi nhanh hơn, không bị lơ lửng trên không 
    private void ApplyBetterJumpPhysics()       
    {   
        if (rb.linearVelocity.y < 0f)
        {
            rb.linearVelocity += Vector3.up * Physics.gravity.y * (jumpFallMultiplier - 1f) * Time.fixedDeltaTime;
        }

        if (rb.linearVelocity.y > 0 && !jumpHeld)
        {
            rb.linearVelocity += Vector3.up * Physics.gravity.y * (lowJumpMultiplier - 1f) * Time.fixedDeltaTime;
        }
    }

    private void OnDrawGizmos()
    {
        Vector3 checkPos = groundCheck != null ? groundCheck.position: transform.position + Vector3.down * 0.1f;
        Gizmos.color = Application.isPlaying && isGrounded ? Color.green : Color.red;
        // Vẽ hình cầu rỗng ra (tâm, bán kính) để check collision
        Gizmos.DrawWireSphere(checkPos, groundCheckRadius);
        // Set lại màu vẽ cho các gizmos khác 
        Gizmos.color = Color.blue;
        // Vẽ tia cho hướng nhìn của object
        // Bắt đầu từ vị trí nào, vẽ dài ra bao nhiêu
        Gizmos.DrawRay(transform.position + Vector3.up, transform.forward * 2f);
    }
}