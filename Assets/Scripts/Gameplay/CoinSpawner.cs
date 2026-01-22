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
    public List<Transform> spawnPoints = new List<Transform>();

    private void Awake()
    {
        // Populate spawn points from children
        if (spawnPoints.Count == 0 && spawnRoot != null)
        {
            foreach (Transform child in spawnRoot)
            {
                spawnPoints.Add(child);
            }
            Debug.Log($"[CoinSpawner] Found {spawnPoints.Count} spawn points under {spawnRoot.name}");
        }
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

        // Auto-populate from spawnRoot if needed
        if (spawnPoints.Count == 0 && spawnRoot != null)
        {
            foreach (Transform child in spawnRoot)
            {
                spawnPoints.Add(child);
            }
        }

        if (spawnPoints.Count == 0)
        {
            Debug.LogWarning("[SERVER][CoinSpawner] No spawn points found!");
            return;
        }

        Debug.Log($"[SERVER][CoinSpawner] Spawning {spawnPoints.Count} coins...");

        foreach (var point in spawnPoints)
        {
            if (point == null) continue;

            GameObject coin = Instantiate(coinPrefab, point.position, Quaternion.identity);
            NetworkServer.Spawn(coin);

            // Debug.Log($"[SERVER] Spawned coin at {point.position}");
        }
    }
}