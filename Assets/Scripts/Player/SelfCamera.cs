using UnityEngine;
using Mirror;
using TMPro.Examples;
using UnityEngine.UI;

public class SelfCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("Distance")]
    [SerializeField] private float distance = 5f;
    [SerializeField] private float minDistance = 2f;
    [SerializeField] private float maxDistance = 10f;

    [Header("Rotation")]
    [SerializeField] private float rotationSpeed = 3f;
    [SerializeField] private float minVerticalAngle = -20f;
    [SerializeField] private float maxVerticalAngle = 60f;

    [Header("Offset")]
    [SerializeField] private Vector3 targetOffset = new Vector3(0f,1.5f,0f);

    [Header("Smoothing")]
    [SerializeField] private float smoothTime = 0.1f;

    // Khởi tạo góc camera default lúc spawn
    private float horizontalAngle = -40f;
    private float verticalAngle = 20f;
    // Biến gán tạm vận tốc xoay camera
    private Vector3 currentVelocity;

    private bool isActive = false;
    private bool isGameActive = true;
    private void FindLocalPlayer()
    {
        if(NetworkClient.localPlayer != null)
        {   
            // Xét Cliet(máy người chơi) xem có local player nào không, nếu có,
            // lấy transform của local player để làm target cho camera 
            target = NetworkClient.localPlayer.transform;
            Debug.Log("[Camera] Found local player to follow");
            // Kích hoạt trạng thái hoạt động cho camera
            isActive = true;
            // Vì chỉ sử dụng chuột để xoay camera nên ta khóa chuột lại vào giữa màn hình,
            // dùng để làm tâm điều khiển
            Cursor.lockState = CursorLockMode.Locked;
            // Và đồng thời ẩn con trỏ chuột đi
            Cursor.visible = false;
        }
    }

    private void HandleRotationInput()
    {   
        // Lấy input rotate từ chuột, giá trị tăng dần theo vận tốc chuột
        float mouseX = Input.GetAxis("Mouse X"); // trái âm phải dương
        float mouseY = Input.GetAxis("Mouse Y"); // xuống âm lên dương

        // Logic rotate camera (Góc camera cho ta nhìn ngược lại với hướng mà camera di chuyển)
        // Kéo chuột sang phải (input dương), hướng camera xoay sang phải, vị trí camera đi sang trái 
        // Kéo chuột sang trái (input âm), hướng camera xoay sang trái, vị trí camera đi sang phải 
        horizontalAngle += mouseX * rotationSpeed;
        // Đẩy chuột lên (input dương), camera hướng lên, vị trí camera đi xuống 
        // Kéo chuột xuống (input âm), camera hướng xuống, vị trí camera đi lên 
        verticalAngle -= mouseY * rotationSpeed;
        // Giữ góc quay camera trong khoảng cho phép, tránh trường hợp
        // chui xuống đất hoặc lộn vòng lên trời
        verticalAngle = Mathf.Clamp(verticalAngle, minVerticalAngle, maxVerticalAngle);
        //Scroll để zoom in/out
        float scroll = Input.GetAxis("Mouse ScrollWheel"); // scroll xuống âm, scroll lên dương
        distance -= scroll * 2f; // phóng đại input lên
        // Giữ distance trong khoảng cho phép
        distance = Mathf.Clamp(distance, minDistance, maxDistance);
    }
    
    // Thay đổi vị trí camera dựa trên góc quay và khoảng cách
    private void UpdateCameraPosition()
    {   
        // Bước 1: Xác định “điểm camera phải nhìn”
        // target = Object mà camera theo dõi
        // position = transform.position của object đó
        Vector3 targetPosition = target.position + targetOffset;
        // Bước 2: Xác định trạng thái của camera trong không gian
        // Dựa trên 2 góc Euler (verticalAngle, horizontalAngle) được input từ input chuột.
        //
        // Quaternion.Euler KHÔNG thực hiện hành động xoay theo thời gian,
        // cũng KHÔNG quan tâm camera đã xoay từ đâu tới đây.
        // Nó chỉ chuyển bộ góc Euler hiện tại thành một Quaternion hợp lệ,
        // đại diện cho "camera đang quay mặt về hướng nào" trong không gian 3D.
        //
        // Nói cách khác:
        // logic_rotate không phải là chuyển động xoay,
        // mà là một trạng thái hướng (orientation) độc lập, hoàn chỉnh.
        Quaternion logic_rotate = Quaternion.Euler(verticalAngle, horizontalAngle, 0);
        // Bước 3: Tính vị trí camera trong không gian 
        // (Camera nên đứng ở đây để có góc nhìn đúng)
        // Đưa hướng nhìn forward của camera về trạng thái nhìn logic_rotate,
        // từ đó xác định hướng đặt camera quanh target
        // Lùi camera ra sau một khoảng distance để tạo góc nhìn third-person
        // Trả lời cho câu hỏi: Nếu một vật thể có orientation = logic_rotate 
        // thì hướng forward của nó trong thế giới là gì?
        Vector3 desiredPosition = targetPosition - (logic_rotate * Vector3.forward * distance); 
        // Bước 4: Di chuyển camera tới vị trí đó
        // Sau khi đã có logic mục tiêu, trạng thái và khoảng cách, ta tạo logic chuyển động cho camera
        // SmoothDamp giúp camera di chuyển một cách mềm mại từ vị trí hiện tại đến vị trí mong muốn,
        // không teleport
        // Quay từ vị trí hiện tại(transform.position) đến vị trí mong muốn(desiredPosition)
        // với tốc độ được tính toán và ghi lại vào currentVelocity để sử dụng cho lần quay hiện tại, 
        // và trong khoảng thời gian cho phép(smoothTime)
        // Note: Camera đang ở đúng chỗ, nhưng chưa nhìn đúng hướng
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref currentVelocity, smoothTime);
        // Cuối cùng:
        // Sau khi đã đến đúng được vị trí mới với các yếu tố như 
        // điểm neo, logic xoay, vị trí xoay, và logic di chuyển,
        // ta xoay camera để nó nhìn đúng vào target, 
        // hoàn thành 1 lần chuyển động camera 
        // Tránh không đặt LookAt trước các step trên vì vị trí sẽ bị thay đổi, 
        // dẫn đến việc target lúc này sẽ bị lệch đi so với tầm ngắm của camera
        transform.LookAt(targetPosition);
    }

    // Ẩn/hiện chuột
    private void ToggleCursorLock()
    {   
        // Nếu nhấn escape lúc game đang chạy
        if(Input.GetKeyDown(KeyCode.Escape) && isGameActive)
        {
            Debug.Log("Toggling cursor lock: " + Cursor.lockState);
            // Ngừng trạng thái chạy của game
            isGameActive = false;
            // Gỡ bỏ trạng thái lock chuột và cho hiển thị chuột
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // Nếu click chuột vào màn hình lúc game đang bị dừng
        else if (Input.GetMouseButtonDown(0) && !isGameActive)
        {   
            Debug.Log("Toggling cursor lock: " + Cursor.lockState);
            // Cho game hoạt động trở lại
            isGameActive = true;
            // Lock chuột và tắt hiển thị chuột
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    // Đặt sau FixedUpdate 
    // Đợi người dùng thực hiện các thao tác, logic vật lý được update xong trong FixedUpdate
    // rồi mới update đến camera
    private void LateUpdate()
    {
        // Check xem camera đã tìm được object để neo vào chưa
        if (target == null)
        {   
            // nếu chưa có target thì gọi hàm để tìm
            FindLocalPlayer();
            return;
        }
        // Check khóa cursor cho game play
        ToggleCursorLock();
        // Nếu đã player đã connect + camera tìm tháy điểm neo và đã khóa cursor cho game play
        // thì mới xử lý xoay + di chuyển camera
        if (!isActive || !isGameActive) return;
        HandleRotationInput();
        UpdateCameraPosition();
    }
}