using UnityEngine;
using Mirror;

[RequireComponent(typeof(NetworkIdentity))]
public class CoinSpawner : NetworkBehaviour
{
    public GameObject coinPrefab;

    public override void OnStartServer()
    {
        var tiles = Object.FindObjectsByType<FloorTile>(FindObjectsSortMode.None);

        foreach (var tile in tiles)
        {
            if (tile.coinSpawnPoint == null) continue;

            var coin = Instantiate(coinPrefab, tile.coinSpawnPoint.position, Quaternion.identity);
            NetworkServer.Spawn(coin);
        }
    }
}
