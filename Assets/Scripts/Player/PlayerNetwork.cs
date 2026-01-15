using UnityEngine;
using Mirror;

[RequireComponent(typeof(NetworkIdentity))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class PlayerNetwork : NetworkBehaviour
{
    [Header("Move")]
    [SerializeField] private float moveSpeed = 6f;

    [Header("Jump")]
    [SerializeField] private Transform groundCheckTransform;
    [SerializeField] private LayerMask groundMask;
    [SerializeField] private float groundCheckRadius = 0.5f;
    [SerializeField] private float jumpPower = 10f;

    [Header("Coin")]
    // =====================================================================
    // SYNCVAR + HOOK
    // - SyncVar: bien tu dong dong bo tu Server -> tat ca Clients
    // - hook: function duoc goi tren CLIENT khi gia tri thay doi
    // - Format: [SyncVar(hook = nameof(TenFunction))]
    // =====================================================================
    [SyncVar(hook = nameof(OnCoinCountChanged))]
    public int coinCount = 0;

    /// <summary>
    /// SYNCVAR HOOK: Chay tren CLIENT khi coinCount thay doi.
    /// Server thay doi coinCount -> Mirror dong bo -> Client nhan va goi hook nay.
    /// 
    /// QUAN TRONG:
    /// - Hook KHONG chay tren Server
    /// - Hook nhan 2 tham so: oldValue va newValue
    /// - Dung de update UI, play sound, hieu ung, etc.
    /// </summary>
    private void OnCoinCountChanged(int oldValue, int newValue)
    {
        Debug.Log($"[HOOK] coinCount changed: {oldValue} -> {newValue}");

        // Chi update UI cua local player
        // (Vi SyncVar hook chay cho TAT CA player objects tren client,
        // nhung ta chi muon update UI cua player minh)
        if (isLocalPlayer)
        {
            CoinUI.NotifyCoinChanged(newValue);
        }
    }

    private float horizontalInput;
    private float verticalInput;

    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    // =================================================================
    // NETWORK LIFECYCLE CALLBACKS (theo thu tu goi)
    // =================================================================

    /// <summary>
    /// [1] Chay tren SERVER khi object duoc spawn.
    /// Goi truoc OnStartClient.
    /// Su dung: khoi tao server-side logic, AI, physics authority.
    /// </summary>
    public override void OnStartServer()
    {
        base.OnStartServer();
        Debug.Log($"[SERVER] Player spawned on server. NetId: {netId}");
    }

    /// <summary>
    /// [2] Chay tren TAT CA CLIENTS khi object duoc spawn (bao gom ca Host).
    /// Goi cho MOI player object, khong phai chi local player.
    /// Su dung: khoi tao visual, animation, audio.
    /// </summary>
    public override void OnStartClient()
    {
        base.OnStartClient();
        Debug.Log($"[CLIENT] Player appeared on client. NetId: {netId}, IsLocalPlayer: {isLocalPlayer}");
    }

    /// <summary>
    /// [3] CHI chay tren LOCAL PLAYER - player ma ban dang dieu khien.
    /// Day la noi ban setup camera, input, UI cho player cua minh.
    /// 
    /// QUAN TRONG: 
    /// - isLocalPlayer = true chi o day
    /// - Cac player khac (cua nguoi choi khac) se KHONG goi ham nay
    /// </summary>
    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        Debug.Log($"[LOCAL] This is MY player! NetId: {netId}");

        // Day la noi tot de:
        // - Setup camera follow
        // - Doi mau de phan biet player cua minh
        // - Enable input controls
        // - Show UI rieng cho player

        // Vi du: doi mau player cua minh
        var renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = Color.green;
        }
    }

    /// <summary>
    /// Goi khi object bi destroy tren server.
    /// </summary>
    public override void OnStopServer()
    {
        base.OnStopServer();
        Debug.Log($"[SERVER] Player destroyed on server. NetId: {netId}");
    }

    /// <summary>
    /// Goi khi object bi destroy tren client.
    /// </summary>
    public override void OnStopClient()
    {
        base.OnStopClient();
        Debug.Log($"[CLIENT] Player destroyed on client. NetId: {netId}");
    }

    /* ===================== CLIENT ===================== */

    private void Update()
    {
        if (!isLocalPlayer) return;

        // Client: read input
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        // Server store them
        CmdMove(h, v);

        // This action run on client, and then, command is sent to server
        if (Input.GetKeyDown(KeyCode.Space))
        {
            CmdJump();
        }
    }

    /* ===================== SERVER ===================== */

    [Command]
    private void CmdMove(float h, float v)
    {
        horizontalInput = h;
        verticalInput = v;
    }

    [Command]
    // Server applies the jump force
    // Server updates physics
    // Server syncs result to all clients
    private void CmdJump()
    {
        if (groundCheckTransform == null) return;

        bool grounded =
            Physics.OverlapSphere(
                groundCheckTransform.position,
                groundCheckRadius,
                groundMask
            ).Length > 0;

        if (!grounded) return;

        rb.AddForce(Vector3.up * jumpPower, ForceMode.VelocityChange);
    }

    private void FixedUpdate()
    {
        if (!isServer) return; // server authoritative physics

        // Server: apply movement based on input received from client
        rb.linearVelocity = new Vector3(
            horizontalInput * moveSpeed,
            rb.linearVelocity.y,
            verticalInput * moveSpeed
        );
    }

    /* ===================== COIN ===================== */

    private void OnTriggerEnter(Collider other)
    {
        if (!isLocalPlayer) return;
        if (!other.CompareTag("Coin")) return;

        NetworkIdentity coinNi = other.GetComponent<NetworkIdentity>();
        if (coinNi == null) return;

        CmdPickupCoin(coinNi.netId);
    }

    /// <summary>
    /// COMMAND: Chay tren SERVER, goi tu CLIENT.
    /// Client gui request nhat coin -> Server validate va xu ly.
    /// </summary>
    [Command]
    private void CmdPickupCoin(uint coinNetId)
    {
        // Server validate: coin co ton tai khong?
        if (!NetworkServer.spawned.TryGetValue(coinNetId, out NetworkIdentity coinNi))
        {
            Debug.Log($"[SERVER] Coin {coinNetId} not found - maybe already picked up");
            return;
        }

        // Server xu ly: tang coin (SyncVar se dong bo ve clients)
        coinCount++;
        Debug.Log($"[SERVER] Player {netId} picked up coin. Total: {coinCount}");

        // Server destroy coin (Mirror dong bo toi tat ca clients)
        NetworkServer.Destroy(coinNi.gameObject);

        // =====================================================================
        // CLIENTRPC: Server -> TAT CA Clients
        // Goi ham nay tren tat ca clients dang connect
        // Dung de: thong bao, hieu ung, animation chung
        // =====================================================================
        RpcOnCoinPickedUp(coinNi.gameObject.transform.position);

        // =====================================================================
        // TARGETRPC: Server -> 1 Client cu the
        // Chi gui cho client so huu object nay (connectionToClient)
        // Dung de: thong bao rieng, UI rieng, reward rieng
        // =====================================================================
        TargetOnYouPickedUpCoin(connectionToClient, coinCount);
    }

    // =====================================================================
    // RPC METHODS
    // =====================================================================

    /// <summary>
    /// CLIENTRPC: Chay tren TAT CA CLIENTS, goi tu SERVER.
    /// Tat ca clients (ke ca nguoi khong nhat coin) deu thay hieu ung.
    /// 
    /// Vi du su dung:
    /// - Play sound effect tai vi tri coin
    /// - Spawn particle effect
    /// - Update leaderboard UI
    /// </summary>
    [ClientRpc]
    private void RpcOnCoinPickedUp(Vector3 coinPosition)
    {
        Debug.Log($"[RPC-ALL] A coin was picked up at {coinPosition}");

        // Vi du: spawn particle effect tai vi tri coin
        // Instantiate(coinPickupEffect, coinPosition, Quaternion.identity);

        // Vi du: play sound
        // AudioSource.PlayClipAtPoint(coinPickupSound, coinPosition);
    }

    /// <summary>
    /// TARGETRPC: Chay tren 1 CLIENT CU THE, goi tu SERVER.
    /// Chi client duoc chi dinh moi nhan duoc message nay.
    /// 
    /// Vi du su dung:
    /// - Thong bao rieng: "Ban da nhat coin!"
    /// - Update UI rieng cua player do
    /// - Trigger tutorial/achievement
    /// </summary>
    [TargetRpc]
    private void TargetOnYouPickedUpCoin(NetworkConnection target, int totalCoins)
    {
        Debug.Log($"[RPC-TARGET] YOU picked up a coin! Total: {totalCoins}");

        // Vi du: hien thi thong bao rieng
        // NotificationUI.Show($"You collected a coin! Total: {totalCoins}");

        // Vi du: play sound rieng cho player nay
        // AudioSource.PlayOneShot(personalCoinSound);
    }

    /* ===================== DEBUG ===================== */

    private void OnDrawGizmos()
    {
        if (groundCheckTransform == null) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(groundCheckTransform.position, groundCheckRadius);
    }

}
