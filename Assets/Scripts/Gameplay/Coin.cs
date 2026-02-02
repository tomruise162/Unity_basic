using UnityEngine;
using Mirror;
using System.Collections;

[RequireComponent(typeof(NetworkIdentity))]
[RequireComponent(typeof(Collider))]
public class Coin : NetworkBehaviour
{
    [Header("Animation Settings")]
    [SerializeField] private float moveDistance = 0.5f;
    [SerializeField] private float moveSpeed = 1f;

    private Vector3 startPosition;
    private Coroutine animationCoroutine;

    void Reset()
    {
        // Lấy collider gắn trên coin
        var col = GetComponent<Collider>();
        // Nếu có collider, bật trigger
        if (col != null) col.isTrigger = true;
    }
    
    // Hàm chạy khi coin xuất hiện(được spawn)
    private void OnEnable()
    {   
        // Vị trí ban đầu của coin, dùng để coin chuyển động lên xuống quanh vị trí gốc
        startPosition = transform.position;
        // Bắt đầu animation lên xuống cho coin
        // Sử dụng StartCoroutine để tạo chuyển động mượt cho coin,
        // giàn trải thời gian coin lên xuống, tránh teleport
        animationCoroutine = StartCoroutine(AnimateUpDown());
    }

    // Chạy khi coin bị destroy
    private void OnDisable()
    {   
        // Nếu animation đang chạy
        if (animationCoroutine != null)
        {   
            // Dừng animation lại
            StopCoroutine(animationCoroutine);
            // Gán bằng null để clear biến
            animationCoroutine = null;
        }
    }

    private IEnumerator AnimateUpDown()
    {
        while (true)
        {
            // Move up
            float elapsed = 0f;
            Vector3 start = startPosition;
            Vector3 end = startPosition + Vector3.up * moveDistance;

            while (elapsed < moveSpeed)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / moveSpeed;
                transform.position = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }

            // Move down
            elapsed = 0f;
            start = startPosition + Vector3.up * moveDistance;
            end = startPosition;

            while (elapsed < moveSpeed)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / moveSpeed;
                transform.position = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
        }
    }

    public override void OnStartClient()
    {
        Debug.Log($"[CLIENT][Coin] Spawned netId={netId} at {transform.position}");
    }
}
