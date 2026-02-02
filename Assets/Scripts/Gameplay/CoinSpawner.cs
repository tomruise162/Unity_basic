using UnityEngine;
using Mirror;
using System.Collections.Generic;

[RequireComponent(typeof(NetworkIdentity))]
public class CoinSpawner : NetworkBehaviour
{
    public GameObject coinPrefab;

    [Header("Spawn Settings")]
    [Tooltip("Drag the parent object containing spawn points here (Script will automatically get children)")]
    public Transform spawnRoot;

    [Tooltip("Or manually drag each position into this list")]
    // Tạo list chứa các points cho spawning coin
    public List<Transform> spawnPoints = new List<Transform>();

    private void Awake()
    {
        if (coinPrefab == null)
            Debug.LogWarning("[CoinSpawner] coinPrefab is missing!");

        if (spawnRoot == null && (spawnPoints == null || spawnPoints.Count == 0))
            Debug.LogWarning("[CoinSpawner] No spawnRoot or spawnPoints assigned!");
    }

    // FIXED: Changed from OnStartClient to OnStartServer
    public override void OnStartServer()
    {
        base.OnStartServer();

        if (coinPrefab == null)
        {
            Debug.LogError("[SERVER][CoinSpawner] coinPrefab is NOT assigned!");
            return;
        }

        // Nếu như transformation được tạo sẵn trong spawnRoot
        if (spawnPoints.Count == 0 && spawnRoot != null)
        {   
            // Duyệt qua từng child trong spawnRoot
            foreach (Transform child in spawnRoot)
            {   
                // Add các transformation vào spawnPoints
                spawnPoints.Add(child);
            }
        }

        // Nếu transformation không được tạo sẵn trong spawnRoot lẫn spawnPoints
        if (spawnPoints.Count == 0)
        {   
            Debug.LogWarning("[SERVER][CoinSpawner] No spawn points found!");
            return;
        }

        // Nếu chuẩn bị transformation thành công thì thông báo tổng số vị trí spawn
        Debug.Log($"[SERVER][CoinSpawner] Spawning {spawnPoints.Count} coins...");

        // Với mỗi transformation được add vào spawnPoints
        foreach (var point in spawnPoints)
        {   
            if (point == null) continue;
            // Khởi tạo game object coin với coinPrefab, transformation, với góc mặc định
            GameObject coin = Instantiate(coinPrefab, point.position, Quaternion.identity);
            // Spawn game object đó ra scene
            NetworkServer.Spawn(coin);
        }
    }
}