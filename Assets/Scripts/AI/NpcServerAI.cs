using UnityEngine;
using Mirror;

[RequireComponent(typeof(Rigidbody))]
public class NpcServerAI : NetworkBehaviour
{
    public float speed = 2f;
    public float wanderRadius = 5f;

    Rigidbody rb;
    Vector3 origin;
    Vector3 target;

    void Awake() => rb = GetComponent<Rigidbody>();

    public override void OnStartServer()
    {
        origin = transform.position;
        PickTarget();
        InvokeRepeating(nameof(PickTarget), 1f, 3f);
    }

    [Server]
    void PickTarget()
    {
        Vector2 r = Random.insideUnitCircle * wanderRadius;
        target = origin + new Vector3(r.x, 0, r.y);
    }

    void FixedUpdate()
    {
        if (!isServer) return;

        Vector3 dir = (target - transform.position);
        if (dir.magnitude < 0.2f) return;

        rb.linearVelocity = new Vector3(dir.normalized.x * speed, rb.linearVelocity.y, dir.normalized.z * speed);
    }
}
