using UnityEngine;
using Mirror;

/// <summary>
/// Custom NetworkManager giúp bạn hiểu rõ flow spawn player.
/// 
/// FLOW SPAWN:
/// 1. User click "Host" hoặc "Client" trên HUD
/// 2. NetworkManager.StartHost() hoặc StartClient() được gọi
/// 3. Khi client connect thành công, Mirror tự động gọi NetworkClient.Ready() và NetworkClient.AddPlayer()
/// 4. Server nhận request và gọi OnServerAddPlayer()
/// 5. Server tạo player object và spawn cho client đó
/// 6. Mirror tự động đồng bộ object tới tất cả clients
/// 7. Trên local player, OnStartLocalPlayer() được gọi
/// </summary>
public class SimpleNetworkManager : NetworkManager
{
    // [SerializeField] private Transform spawnPoint;
    // public Transform SpawnPoint => spawnPoint; // read-only property if you need it in code

    [Header("Spawn Settings")]
    [Tooltip("Cac vi tri spawn player, neu khong co se spawn tai Vector3.zero")]
    public Transform[] spawnPoints;

    public override void Awake()
    {
        // Tu dong assign transport neu chua co
        if (transport == null)
        {
            transport = GetComponent<Transport>();
        }

        base.Awake();
    }

    // =================================================================
    // SERVER EVENTS
    // =================================================================

    /// <summary>
    /// Gọi khi server bắt đầu (cả Host và Dedicated Server)
    /// Đây là nơi tốt để spawn các object của game như coins, enemies, etc.
    /// </summary>
    public override void OnStartServer()
    {
        base.OnStartServer();
        Debug.Log("[SERVER] Server started!");
    }

    /// <summary>
    /// Gọi khi server dừng
    /// </summary>
    public override void OnStopServer()
    {
        base.OnStopServer();
        Debug.Log("[SERVER] Server stopped!");
    }

    /// <summary>
    /// QUAN TRONG: Đây là nơi SERVER spawn player cho client.
    /// 
    /// Khi client gọi NetworkClient.AddPlayer(), message được gửi tới server,
    /// và server gọi function này để tạo player object.
    /// </summary>
    /// <param name="conn">Connection của client đang request spawn</param>
    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        // Bước 1: Xác định vị trí spawn
        Vector3 spawnPosition = GetSpawnPosition(conn.connectionId);
        Quaternion spawnRotation = Quaternion.identity;

        Debug.Log($"[SERVER] Spawning player for connection {conn.connectionId} at {spawnPosition}");

        // Bước 2: SERVER tạo player object từ prefab
        // playerPrefab là prefab được assign trong Inspector của NetworkManager
        GameObject playerObject = Instantiate(playerPrefab, spawnPosition, spawnRotation);

        // Bước 3: SERVER spawn object và gán ownership cho connection này
        // Sau bước này, Mirror sẽ TỰ ĐỘNG:
        // - Tạo object tương tự trên tất cả connected clients
        // - Đánh dấu object này thuộc về connection 'conn'
        // - Gọi OnStartServer() trên server
        // - Gọi OnStartClient() trên tất cả clients
        // - Gọi OnStartLocalPlayer() CHỈ trên client sở hữu object này
        NetworkServer.AddPlayerForConnection(conn, playerObject);

        NetworkIdentity netId = playerObject.GetComponent<NetworkIdentity>();
        Debug.Log($"[SERVER] Player spawned successfully! NetId: {netId.netId}, ConnectionId: {conn.connectionId}");
        
        // Kiểm tra NetworkTransform component
        var networkTransform = playerObject.GetComponent<NetworkTransformBase>();
        if (networkTransform != null)
        {
            Debug.Log($"[SERVER] NetworkTransform found: syncPosition={networkTransform.syncPosition}, syncRotation={networkTransform.syncRotation}");
        }
        else
        {
            Debug.LogWarning($"[SERVER] WARNING: No NetworkTransform component found on player prefab! Position/Rotation will NOT sync!");
        }
    }

    /// <summary>
    /// Tính toán vị trí spawn dựa trên connectionId
    /// </summary>
    private Vector3 GetSpawnPosition(int connectionId)
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            // Spawn tại vị trí ngẫu nhiên nếu không có spawn points
            return new Vector3(Random.Range(-5f, 5f), 1f, Random.Range(-5f, 5f));
        }

        // Chọn spawn point theo round-robin
        int index = connectionId % spawnPoints.Length;
        return spawnPoints[index].position;
    }

    /// <summary>
    /// Gọi khi một client connect tới server
    /// </summary>
    public override void OnServerConnect(NetworkConnectionToClient conn)
    {
        base.OnServerConnect(conn);
        Debug.Log($"[SERVER] Client connected! ConnectionId: {conn.connectionId}");
    }

    /// <summary>
    /// Gọi khi một client disconnect khỏi server
    /// Player object sẽ tự động bị destroy
    /// </summary>
    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        Debug.Log($"[SERVER] Client disconnected! ConnectionId: {conn.connectionId}");
        base.OnServerDisconnect(conn); // Destroy player object
    }

    // =================================================================
    // CLIENT EVENTS
    // =================================================================

    /// <summary>
    /// Gọi khi client bắt đầu (bao gồm cả Host client)
    /// </summary>
    public override void OnStartClient()
    {
        base.OnStartClient();
        Debug.Log("[CLIENT] Client started!");
    }

    /// <summary>
    /// Gọi khi client connect tới server thành công
    /// Sau bước này, Mirror sẽ tự động gọi NetworkClient.Ready() 
    /// và NetworkClient.AddPlayer() (nếu autoCreatePlayer = true)
    /// </summary>
    public override void OnClientConnect()
    {
        base.OnClientConnect();
        Debug.Log("[CLIENT] Connected to server!");

        // Nếu autoCreatePlayer = true (mặc định), Mirror tự động gọi:
        // NetworkClient.Ready();
        // NetworkClient.AddPlayer();

        // Nếu autoCreatePlayer = false, bạn phải gọi thủ công:
        // if (!NetworkClient.ready) NetworkClient.Ready();
        // NetworkClient.AddPlayer();
    }

    /// <summary>
    /// Gọi khi client disconnect khỏi server
    /// </summary>
    public override void OnClientDisconnect()
    {
        base.OnClientDisconnect();
        Debug.Log("[CLIENT] Disconnected from server!");
    }

    /// <summary>
    /// Gọi khi client dừng
    /// </summary>
    public override void OnStopClient()
    {
        base.OnStopClient();
        Debug.Log("[CLIENT] Client stopped!");
    }
}
