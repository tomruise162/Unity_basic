using UnityEngine;
using Mirror;

[RequireComponent(typeof(NetworkIdentity))]
[RequireComponent(typeof(Collider))]
public class Coin : NetworkBehaviour
{
    void Reset()
    {
        // Coin nên là trigger
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }
}
