using UnityEngine;
using Mirror;

/// <summary>
/// Custom NetworkManager with enhanced debugging to help diagnose spawn issues
/// </summary>
public class SimpleNetworkManager : NetworkManager
{
    [Header("Spawn Settings")]
    [Tooltip("Cac vi tri spawn player, neu khong co se spawn tai Vector3.zero")]
    public Transform[] spawnPoints;

    [Header("Debug Settings")]
    [SerializeField] private bool showDebugLogs = true;
    [SerializeField] private bool showDebugGizmos = true;

    public override void Awake()
    {
        // Tu dong assign transport neu chua co
        if (transport == null)
        {
            transport = GetComponent<Transport>();
        }

        base.Awake();

        // CRITICAL CHECKS
        if (playerPrefab == null)
        {
            Debug.LogError("[SETUP ERROR] Player Prefab is not assigned in NetworkManager!");
        }
        else
        {
            DebugLog($"[SETUP] Player Prefab: {playerPrefab.name}");

            // Verify prefab has required components
            var netId = playerPrefab.GetComponent<NetworkIdentity>();
            var rb = playerPrefab.GetComponent<Rigidbody>();
            var renderer = playerPrefab.GetComponent<Renderer>();

            if (netId == null) Debug.LogError("[SETUP ERROR] Player prefab missing NetworkIdentity!");
            if (rb == null) Debug.LogError("[SETUP ERROR] Player prefab missing Rigidbody!");
            if (renderer == null) Debug.LogWarning("[SETUP WARNING] Player prefab missing Renderer - you won't see it!");
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning("[SETUP WARNING] No spawn points assigned! Players will spawn randomly.");
        }
        else
        {
            DebugLog($"[SETUP] {spawnPoints.Length} spawn points configured");
        }
    }

    // =================================================================
    // SERVER EVENTS
    // =================================================================

    public override void OnStartServer()
    {
        base.OnStartServer();
        DebugLog("[SERVER] Server started!");
        DebugLog($"[SERVER] Listening on: {networkAddress}");
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        DebugLog("[SERVER] Server stopped!");
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        // Bước 1: Xác định vị trí spawn
        Vector3 spawnPosition = GetSpawnPosition(conn.connectionId);
        Quaternion spawnRotation = Quaternion.identity;

        DebugLog($"[SERVER] ========================================");
        DebugLog($"[SERVER] Spawning player for ConnectionId: {conn.connectionId}");
        DebugLog($"[SERVER] Spawn Position: {spawnPosition}");
        DebugLog($"[SERVER] Player Prefab: {playerPrefab.name}");

        // Bước 2: SERVER tạo player object từ prefab
        GameObject playerObject = Instantiate(playerPrefab, spawnPosition, spawnRotation);

        // DEBUGGING: Make player more visible
        var renderer = playerObject.GetComponent<Renderer>();
        if (renderer != null)
        {
            // Random color for each player
            renderer.material.color = new Color(
                Random.Range(0.3f, 1f),
                Random.Range(0.3f, 1f),
                Random.Range(0.3f, 1f)
            );
            DebugLog($"[SERVER] Player renderer found and colored");
        }
        else
        {
            Debug.LogWarning($"[SERVER] Player has no renderer! You won't see it!");
        }

        // Verify components
        var netId = playerObject.GetComponent<NetworkIdentity>();
        var rb = playerObject.GetComponent<Rigidbody>();
        DebugLog($"[SERVER] Components check:");
        DebugLog($"  - NetworkIdentity: {(netId != null ? "✓" : "✗ MISSING")}");
        DebugLog($"  - Rigidbody: {(rb != null ? "✓" : "✗ MISSING")}");
        DebugLog($"  - Renderer: {(renderer != null ? "✓" : "✗ MISSING")}");

        // Bước 3: SERVER spawn object và gán ownership
        NetworkServer.AddPlayerForConnection(conn, playerObject);

        DebugLog($"[SERVER] Player spawned successfully! NetId: {netId?.netId}");
        DebugLog($"[SERVER] Active players on server: {NetworkServer.connections.Count}");
        DebugLog($"[SERVER] ========================================");
    }

    private Vector3 GetSpawnPosition(int connectionId)
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            // Spawn at random position with spacing
            float angle = connectionId * 90f; // 90 degrees apart
            float radius = 5f;
            Vector3 offset = new Vector3(
                Mathf.Cos(angle * Mathf.Deg2Rad) * radius,
                1f,
                Mathf.Sin(angle * Mathf.Deg2Rad) * radius
            );

            DebugLog($"[SERVER] No spawn points, using calculated position: {offset}");
            return offset;
        }

        // Chọn spawn point theo round-robin
        int index = connectionId % spawnPoints.Length;
        Vector3 position = spawnPoints[index].position;

        DebugLog($"[SERVER] Using spawn point [{index}]: {position}");
        return position;
    }

    public override void OnServerConnect(NetworkConnectionToClient conn)
    {
        base.OnServerConnect(conn);
        DebugLog($"[SERVER] ✓ Client connected! ConnectionId: {conn.connectionId}");
        DebugLog($"[SERVER] Total connections: {NetworkServer.connections.Count}");
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        DebugLog($"[SERVER] ✗ Client disconnected! ConnectionId: {conn.connectionId}");
        base.OnServerDisconnect(conn);
    }

    // =================================================================
    // CLIENT EVENTS
    // =================================================================

    public override void OnStartClient()
    {
        base.OnStartClient();
        DebugLog("[CLIENT] Client started!");
    }

    public override void OnClientConnect()
    {
        base.OnClientConnect();
        DebugLog("[CLIENT] ✓ Connected to server!");
        DebugLog($"[CLIENT] Server address: {networkAddress}");

        // Check if client is ready
        if (NetworkClient.ready)
        {
            DebugLog("[CLIENT] Client is READY");
        }
        else
        {
            DebugLog("[CLIENT] Client is NOT ready yet (waiting...)");
        }
    }

    public override void OnClientDisconnect()
    {
        base.OnClientDisconnect();
        DebugLog("[CLIENT] ✗ Disconnected from server!");
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        DebugLog("[CLIENT] Client stopped!");
    }

    // =================================================================
    // HELPER METHODS
    // =================================================================

    private void DebugLog(string message)
    {
        if (showDebugLogs)
        {
            Debug.Log(message);
        }
    }

    // =================================================================
    // SCENE DEBUG VISUALIZATION
    // =================================================================

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;
        if (spawnPoints == null) return;

        // Draw spawn points
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            if (spawnPoints[i] == null) continue;

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(spawnPoints[i].position, 0.5f);
            Gizmos.DrawLine(spawnPoints[i].position, spawnPoints[i].position + Vector3.up * 2f);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(spawnPoints[i].position + Vector3.up * 2.5f, $"Spawn {i}");
#endif
        }

        // Draw connections between spawn points
        Gizmos.color = Color.yellow;
        for (int i = 0; i < spawnPoints.Length - 1; i++)
        {
            if (spawnPoints[i] == null || spawnPoints[i + 1] == null) continue;
            Gizmos.DrawLine(spawnPoints[i].position, spawnPoints[i + 1].position);
        }
    }

    // =================================================================
    // RUNTIME DEBUG INFO
    // =================================================================

    private void OnGUI()
    {
        if (!showDebugLogs) return;

        GUILayout.BeginArea(new Rect(10, 10, 300, 500));
        GUILayout.BeginVertical("box");

        GUILayout.Label($"<b>Network Status</b>", new GUIStyle(GUI.skin.label) { richText = true });
        GUILayout.Label($"Mode: {mode}");

        if (NetworkServer.active)
        {
            GUILayout.Label($"<color=green>SERVER ACTIVE</color>", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.Label($"Connections: {NetworkServer.connections.Count}");
        }

        if (NetworkClient.isConnected)
        {
            GUILayout.Label($"<color=green>CLIENT CONNECTED</color>", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.Label($"Ready: {NetworkClient.ready}");
        }

        // Count players
        var players = FindObjectsByType<NetworkIdentity>(FindObjectsSortMode.None);
        GUILayout.Label($"Network Objects: {players.Length}");

        int localPlayerCount = 0;
        foreach (var p in players)
        {
            if (p.isLocalPlayer) localPlayerCount++;
        }
        GUILayout.Label($"Local Players: {localPlayerCount}");

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
}