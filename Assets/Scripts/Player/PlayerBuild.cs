using UnityEngine;
using Mirror;

public class PlayerBuild : NetworkBehaviour
{
    public GameObject buildPrefab;
    public float maxDistance = 6f;
    public LayerMask groundMask;

    void Update()
    {
        if (!isLocalPlayer) return;

        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 200f, groundMask))
                CmdPlace(hit.point);
        }
    }

    [Command]
    void CmdPlace(Vector3 pos)
    {
        if (Vector3.Distance(transform.position, pos) > maxDistance) return;

        var go = Instantiate(buildPrefab, pos, Quaternion.identity);
        var bp = go.GetComponent<BuildPiece>();
        if (bp != null) bp.ownerNetId = netId;

        NetworkServer.Spawn(go);
    }
}
