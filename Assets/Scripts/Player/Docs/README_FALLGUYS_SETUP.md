# Fall Guys Style Setup Guide

## Tong quan

Project nay implement Fall Guys style gameplay voi:
- Third-person camera xoay theo chuot
- Di chuyen theo huong camera
- Character xoay theo huong di chuyen
- Jump + Dive mechanics
- Server authoritative physics

---

## Files

| File | Mo ta |
|------|-------|
| FallGuysCamera.cs | Camera controller - attach vao Main Camera |
| FallGuysMovement.cs | Movement controller - thay the movement trong PlayerNetwork |
| PlayerNetwork.cs | Giu nguyen cho coin pickup logic |

---

## Setup trong Unity

### Buoc 1: Setup Player Prefab

1. Mo Player prefab

2. **Rigidbody settings** (quan trong!):
   ```
   Mass: 1
   Drag: 3          <- Tao cam giac "nang ne"
   Angular Drag: 0.5
   Use Gravity: true
   Interpolation: Interpolate
   Collision Detection: Continuous
   Constraints:
     - Freeze Rotation: X, Y, Z (tat ca)  <- De script tu xoay
   ```

3. **Add component** `FallGuysMovement`
   - Ground Check: tao empty child o chan player, assign vao day
   - Ground Mask: chon layers la mat dat (vd: Default, Ground)

4. **PlayerNetwork** - giu nguyen, nhung co the xoa phan movement code cu
   (hoac de rieng de so sanh)

### Buoc 2: Setup Camera

1. Chon **Main Camera** trong scene

2. **Remove** cac camera script cu (neu co)

3. **Add component** `FallGuysCamera`
   - Target: de trong - se tu tim local player
   - Dieu chinh Distance, Rotation Speed theo y thich
   - Collision Mask: ~0 (tat ca) hoac chon layers can check

### Buoc 3: Setup Ground

1. Cac object la mat dat can co:
   - Collider (Box, Mesh, etc.)
   - Thuoc layer duoc chon trong Ground Mask

2. Vi du: tao layer "Ground", assign cho cac floor tiles

### Buoc 4: Setup NetworkManager

1. Dam bao **Player Prefab** trong NetworkManager la prefab da setup

2. Kiem tra **Spawn Prefabs** chua Coin prefab

---

## Controls

| Key | Action |
|-----|--------|
| WASD | Di chuyen |
| Mouse | Xoay camera |
| Space | Nhay |
| Shift/Ctrl | Dive (khi dang trong khong trung) |
| Scroll | Zoom camera |
| Esc | Toggle cursor lock |

---

## Flow Du Lieu

```
CLIENT                          SERVER                      ALL CLIENTS
   |                               |                            |
   | 1. Doc input (WASD, Mouse)    |                            |
   |                               |                            |
   | 2. Tinh camera yaw            |                            |
   |                               |                            |
   | 3. CmdSendInput() ----------->|                            |
   |    (input, jump, dive, yaw)   |                            |
   |                               |                            |
   |                               | 4. Luu input               |
   |                               |                            |
   |                               | 5. FixedUpdate:            |
   |                               |    - Tinh movement theo    |
   |                               |      camera direction      |
   |                               |    - Apply physics         |
   |                               |    - Xoay character        |
   |                               |                            |
   |                               | 6. NetworkTransform ------>|
   |                               |    dong bo position/rotation|
   |                               |                            |
   |                               |                            | 7. Tat ca clients
   |                               |                            |    thay player di chuyen
```

---

## So sanh voi code cu

### Code cu (PlayerNetwork.cs):
```csharp
// Di chuyen theo WORLD SPACE
rb.linearVelocity = new Vector3(
    horizontalInput * moveSpeed,
    rb.linearVelocity.y,
    verticalInput * moveSpeed
);
```

### Code moi (FallGuysMovement.cs):
```csharp
// Di chuyen theo HUONG CAMERA
Vector3 forward = Quaternion.Euler(0f, cameraYaw, 0f) * Vector3.forward;
Vector3 right = Quaternion.Euler(0f, cameraYaw, 0f) * Vector3.right;
Vector3 moveDirection = (forward * input.y + right * input.x).normalized;
```

---

## Tinh chinh gameplay feel

### Nhanh hon / Responsive hon:
- Tang `moveSpeed`: 8 -> 12
- Tang `acceleration`: 50 -> 80
- Giam `Rigidbody.Drag`: 3 -> 1

### Cham hon / "Fall Guys feel" hon:
- Giam `moveSpeed`: 8 -> 6
- Giam `acceleration`: 50 -> 30
- Tang `Rigidbody.Drag`: 3 -> 5

### Nhay cao hon:
- Tang `jumpForce`: 8 -> 12

### Dive manh hon:
- Tang `diveForce`: 12 -> 15

---

## Troubleshooting

### Player khong di chuyen
- Kiem tra `isServer` trong FixedUpdate - chi server xu ly physics
- Kiem tra NetworkTransform da duoc add chua

### Camera khong xoay
- Kiem tra `Cursor.lockState` - phai la Locked
- Kiem tra FallGuysCamera.Instance co null khong

### Player bi xoay loan xa
- Kiem tra Rigidbody: Freeze Rotation X, Y, Z phai bat

### Player rot xuyen dat
- Kiem tra Ground Mask co dung khong
- Kiem tra Collider cua player va ground

### Dive khong hoat dong
- Phai dang trong khong trung (khong cham dat)
- Kiem tra diveCooldown

---

## Next Steps

1. **Animation**: Them Animator + animation clips cho Run, Jump, Dive, Fall
2. **Ragdoll**: Them ragdoll physics khi bi knockout
3. **Grab**: Them mechanic grab objects/players
4. **Obstacles**: Them cac loai obstacle nhu trong Fall Guys
5. **Game Mode**: Them race, survival, team modes
