using UnityEngine;
using Mirror;

[RequireComponent(typeof(NetworkIdentity))]
public class CoinSpawner : NetworkBehaviour
{
    public GameObject coinPrefab;

    public override void OnStartServer()
    {
        if (coinPrefab == null)
        {
            Debug.LogError("[SERVER][CoinSpawner] coinPrefab is NOT assigned. No coins will spawn.");
            return;
        }

        var tiles = Object.FindObjectsByType<FloorTile>(FindObjectsSortMode.None);
        Debug.Log($"[SERVER][CoinSpawner] Found {tiles.Length} FloorTiles. Spawning coins...");

        foreach (var tile in tiles)
        {
            if (tile.coinSpawnPoint == null) continue;

            var coin = Instantiate(coinPrefab, tile.coinSpawnPoint.position, Quaternion.identity);
            NetworkServer.Spawn(coin);
            if (coin.TryGetComponent<NetworkIdentity>(out var ni))
                Debug.Log($"[SERVER][CoinSpawner] Spawned coin netId={ni.netId} at {coin.transform.position}");
        }
    }
}
