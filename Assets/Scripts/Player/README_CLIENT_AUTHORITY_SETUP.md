# Hướng dẫn Setup Client Authority Movement

## File mới: `OnlyUpClientAuthority.cs`

File này sử dụng **Client Authority** pattern giống QWorld, cho phép client di chuyển trực tiếp mà không cần Command.

## So sánh 2 patterns:

### Server Authority (OnlyUp.cs - file cũ)
- ✅ An toàn, chống cheat
- ❌ Có delay từ server
- Flow: Client → Command → Server → NetworkTransform sync

### Client Authority (OnlyUpClientAuthority.cs - file mới)
- ✅ Responsive, không delay
- ✅ Đơn giản hơn
- ❌ Dễ bị hack (cần server validation nếu cần)
- Flow: Client di chuyển trực tiếp → NetworkTransform sync lên server → Server broadcast

## Cách setup trong Unity:

### Bước 1: Tạo prefab mới hoặc duplicate prefab hiện tại

### Bước 2: Thay script component
- Xóa `SimpleNetworkJumpWithGround` (OnlyUp.cs)
- Thêm `OnlyUpClientAuthority` component

### Bước 3: Cấu hình NetworkTransform (QUAN TRỌNG!)

Trong Inspector của Player prefab, tìm component **NetworkTransformHybrid**:

1. **Sync Direction**: Đổi từ `ServerToClient` → `ClientToServer`
   - Hoặc trong Inspector: tìm field `Sync Direction` và chọn `ClientToServer`

2. **Sync Position**: ✅ Bật (true)

3. **Sync Rotation**: ✅ Bật (true)

4. **Use Fixed Update**: ✅ Bật (true) - nếu dùng Rigidbody

### Bước 4: Cấu hình NetworkIdentity

Đảm bảo **NetworkIdentity** component có:
- `Server Only`: ❌ Tắt (false)
- `Local Player Authority`: ✅ Bật (true) - QUAN TRỌNG!

### Bước 5: XÓA hoặc DISABLE NetworkRigidbody (QUAN TRỌNG!)

**VẤN ĐỀ**: NetworkRigidbody tự động set `isKinematic = true` cho remote players.
Với Client Authority, chúng ta **KHÔNG CẦN** NetworkRigidbody!

**GIẢI PHÁP**:
1. **Cách 1 (Khuyến nghị)**: Xóa NetworkRigidbody component khỏi prefab
   - Tìm component `Network Rigidbody (Reliable)` hoặc `Network Rigidbody (Unreliable)`
   - Xóa nó đi (chỉ cần NetworkTransform là đủ)

2. **Cách 2**: Code tự động disable (đã implement trong OnlyUpClientAuthority.cs)
   - Script sẽ tự động disable NetworkRigidbody khi local player spawn
   - Nhưng tốt nhất vẫn nên xóa khỏi prefab

### Bước 5: Test

1. Build và chạy game
2. Host một server
3. Kết nối client khác
4. Kiểm tra:
   - Local player có thể nhảy ngay lập tức (không delay)
   - Remote players thấy local player di chuyển
   - Console logs: `[CLIENT] Local player X jumped`

## Lưu ý:

1. **Client Authority chỉ áp dụng cho local player**
   - Remote players vẫn nhận position từ server
   - Chỉ local player mới có quyền di chuyển

2. **NetworkTransform tự động xử lý sync**
   - Local player: gửi position lên server
   - Server: broadcast xuống các clients khác
   - Remote players: nhận và hiển thị position

3. **Nếu vẫn không sync:**
   - Kiểm tra `Local Player Authority` trong NetworkIdentity
   - Kiểm tra `Sync Direction` trong NetworkTransform
   - Kiểm tra Console logs để debug

## Code differences:

### Server Authority (OnlyUp.cs):
```csharp
[Command]
private void CmdRequestJump() {
    PerformJump(); // Chạy trên SERVER
}
```

### Client Authority (OnlyUpClientAuthority.cs):
```csharp
[Client]
private void HandleJump() {
    rb.AddForce(...); // Chạy trên CLIENT
}
```

