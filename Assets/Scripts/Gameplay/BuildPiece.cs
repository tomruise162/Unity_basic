using Mirror;
using UnityEngine;

public class BuildPiece : NetworkBehaviour
{
    [SyncVar] public uint ownerNetId;
}
