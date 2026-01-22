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
        // Coin nên là trigger
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnEnable()
    {
        startPosition = transform.position;
        animationCoroutine = StartCoroutine(AnimateUpDown());
    }

    private void OnDisable()
    {
        if (animationCoroutine != null)
        {
            StopCoroutine(animationCoroutine);
            animationCoroutine = null;
        }
    }

    private IEnumerator AnimateUpDown()
    {
        // Start at random position in animation cycle (no initial wait)
        float randomOffset = Random.Range(0f, 1f);
        
        while (true)
        {
            // Randomize speed for each cycle
            float currentSpeed = moveSpeed * Random.Range(0.8f, 1.2f);
            
            // Move up
            float elapsed = randomOffset * currentSpeed;
            Vector3 start = startPosition;
            Vector3 end = startPosition + Vector3.up * moveDistance;
            
            while (elapsed < currentSpeed)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / currentSpeed;
                transform.position = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }

            // Randomize speed for down movement
            currentSpeed = moveSpeed * Random.Range(0.8f, 1.2f);
            
            // Move down
            elapsed = 0f;
            start = startPosition + Vector3.up * moveDistance;
            end = startPosition;
            
            while (elapsed < currentSpeed)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / currentSpeed;
                transform.position = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
            
            // Reset random offset after first cycle
            randomOffset = 0f;
        }
    }

    public override void OnStartClient()
    {
        Debug.Log($"[CLIENT][Coin] Spawned netId={netId} at {transform.position}");
    }
}
