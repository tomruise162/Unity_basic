using UnityEngine;
using Mirror;

/// <summary>
/// Fall Guys style Third-Person Camera
/// 
/// Dac diem:
/// - Camera orbit xung quanh player
/// - Xoay theo chuot (hoac right stick tren controller)
/// - Co collision de khong xuyen tuong
/// - Smooth follow player
/// 
/// Setup:
/// - Attach script nay vao Main Camera
/// - Script se tu tim local player de follow
/// </summary>
public class FallGuysCamera : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Se tu dong tim local player neu de trong")]
    public Transform target;

    [Header("Distance")]
    [SerializeField] private float distance = 5f;
    [SerializeField] private float minDistance = 2f;
    [SerializeField] private float maxDistance = 10f;

    [Header("Rotation")]
    [SerializeField] private float rotationSpeed = 3f;
    [SerializeField] private float minVerticalAngle = -20f;
    [SerializeField] private float maxVerticalAngle = 60f;

    [Header("Offset")]
    [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1.5f, 0f);

    [Header("Smoothing")]
    [SerializeField] private float positionSmoothTime = 0.1f;
    //[SerializeField] private float rotationSmoothTime = 0.05f;

    [Header("Collision")]
    [SerializeField] private LayerMask collisionMask = ~0;
    [SerializeField] private float collisionRadius = 0.3f;

    // Current angles
    private float horizontalAngle = 0f;
    private float verticalAngle = 20f;

    // Smoothing
    private Vector3 currentVelocity;

    // Singleton for easy access
    public static FallGuysCamera Instance { get; private set; }

    // Public property de lay huong camera (dung cho movement)
    public Vector3 Forward => new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
    public Vector3 Right => new Vector3(transform.right.x, 0f, transform.right.z).normalized;

    // Trang thai hoat dong - chi hoat dong sau khi co player
    private bool isActive = false;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        // KHONG lock cursor o day - de nguoi dung click HUD truoc
        // Cursor se duoc lock sau khi tim thay local player
    }

    private void LateUpdate()
    {
        // Kiem tra Escape de unlock cursor (mo menu)
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            ToggleCursorLock();
        }

        // Tu dong tim target neu chua co
        if (target == null)
        {
            FindLocalPlayer();
            return;
        }

        // Chi xu ly camera khi da active
        if (!isActive) return;

        HandleRotationInput();
        UpdateCameraPosition();
    }

    /// <summary>
    /// Tim local player de follow
    /// </summary>
    private void FindLocalPlayer()
    {
        if (NetworkClient.localPlayer != null)
        {
            target = NetworkClient.localPlayer.transform;
            Debug.Log("[Camera] Found local player to follow");

            // BAT DAU HOAT DONG - lock cursor
            isActive = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    /// <summary>
    /// Doc input chuot de xoay camera
    /// </summary>
    private void HandleRotationInput()
    {
        // Doc mouse input
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        // Update angles
        horizontalAngle += mouseX * rotationSpeed;
        verticalAngle -= mouseY * rotationSpeed;

        // Clamp vertical angle
        verticalAngle = Mathf.Clamp(verticalAngle, minVerticalAngle, maxVerticalAngle);

        // Scroll wheel de zoom
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        distance -= scroll * 2f;
        distance = Mathf.Clamp(distance, minDistance, maxDistance);
    }

    /// <summary>
    /// Update vi tri camera dua tren angles
    /// </summary>
    private void UpdateCameraPosition()
    {
        // Tinh toan target position (player + offset)
        Vector3 targetPosition = target.position + targetOffset;

        // Tinh toan rotation tu angles
        Quaternion rotation = Quaternion.Euler(verticalAngle, horizontalAngle, 0f);

        // Tinh toan desired camera position
        Vector3 desiredPosition = targetPosition - (rotation * Vector3.forward * distance);

        // Collision check - dung raycast de dam bao camera khong xuyen tuong
        float actualDistance = distance;
        if (Physics.SphereCast(targetPosition, collisionRadius, (desiredPosition - targetPosition).normalized,
            out RaycastHit hit, distance, collisionMask))
        {
            actualDistance = hit.distance;
        }

        // Tinh lai vi tri voi actual distance
        Vector3 finalPosition = targetPosition - (rotation * Vector3.forward * actualDistance);

        // Smooth position
        transform.position = Vector3.SmoothDamp(transform.position, finalPosition, ref currentVelocity, positionSmoothTime);

        // Look at target
        transform.LookAt(targetPosition);
    }

    /// <summary>
    /// Toggle cursor lock (goi khi can mo menu)
    /// </summary>
    public void ToggleCursorLock()
    {
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void OnDestroy()
    {
        // Unlock cursor khi destroy
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
