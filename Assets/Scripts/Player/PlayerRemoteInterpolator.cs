using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class PlayerRemoteInterpolator : NetworkBehaviour
{
    struct Snap
    {
        public double time;
        public Vector3 pos;
        public Quaternion rot;
    }

    readonly Queue<Snap> snaps = new();
    [SerializeField] float backTime = 0.1f;

    public override void OnDeserialize(NetworkReader reader, bool initialState)
    {
        if (isLocalPlayer) return;

        Vector3 p = reader.ReadVector3();
        Quaternion r = reader.ReadQuaternion();

        snaps.Enqueue(new Snap
        {
            time = NetworkTime.time,
            pos = p,
            rot = r
        });

        while (snaps.Count > 20) snaps.Dequeue();
    }

    void Update()
    {
        if (isLocalPlayer || snaps.Count < 2) return;

        double target = NetworkTime.time - backTime;
        Snap a = snaps.Peek();

        foreach (var s in snaps)
        {
            if (s.time <= target) a = s;
            else break;
        }

        transform.position = Vector3.Lerp(transform.position, a.pos, 0.25f);
        transform.rotation = Quaternion.Slerp(transform.rotation, a.rot, 0.25f);
    }
}
