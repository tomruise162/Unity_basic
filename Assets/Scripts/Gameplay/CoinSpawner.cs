using UnityEngine;
using Mirror;
using System.Collections.Generic;

[RequireComponent(typeof(NetworkIdentity))]
public class CoinSpawner : NetworkBehaviour
{
    public GameObject coinPrefab;

    [Header("Spawn Settings")]
    [Tooltip("Keo object cha chua cac spawn points vao day (Script se tu dong lay children)")]
    public Transform spawnRoot;

    [Tooltip("Hoac keo thu cong tung vi tri vao list nay")]
    public List<Transform> spawnPoints = new List<Transform>();

    public override void OnStartServer()
    {
        if (coinPrefab == null)
        {
            Debug.LogError("[SERVER][CoinSpawner] coinPrefab is NOT assigned. No coins will spawn.");
            return;
        }

        // Logic moi: Lay vi tri tu spawnRoot hoac list spawnPoints
        List<Transform> finalPoints = new List<Transform>(spawnPoints);

        // 1. Neu co spawnRoot, them tat ca children cua no vao list
        if (spawnRoot != null)
        {
            foreach (Transform child in spawnRoot)
            {
                if (child != null) finalPoints.Add(child);
            }
        }

        if (finalPoints.Count == 0)
        {
            Debug.LogWarning("[SERVER][CoinSpawner] No spawn points found! Assign 'Spawn Root' or populate 'Spawn Points'.");
            return;
        }

        Debug.Log($"[SERVER][CoinSpawner] Found {finalPoints.Count} spawn points. Spawning coins...");

        // 2. Spawn coin tai tung vi tri
        foreach (var point in finalPoints)
        {
            if (point == null) continue;

            GameObject coin = Instantiate(coinPrefab, point.position, Quaternion.identity);
            NetworkServer.Spawn(coin);

            // Debug check
            // if (coin.TryGetComponent<NetworkIdentity>(out var ni))
            //    Debug.Log($"[SERVER] Spawned coin netId={ni.netId}");
        }
    }
}
