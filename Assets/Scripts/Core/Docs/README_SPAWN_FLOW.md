# Mirror Spawn Flow - Giai thich chi tiet

## Tong quan

Trong Mirror, quy trinh spawn player dien ra theo mo hinh **Client-Server**:
- **Client** gui request spawn
- **Server** tao object va dong bo toi tat ca clients
- Mirror tu dong xu ly viec dong bo

---

## Flow Chi Tiet

```
+------------------------------------------------------------------+
|                         SPAWN FLOW                                |
+------------------------------------------------------------------+

[1] User click "Host" hoac "Client"
         |
         v
[2] NetworkManager.StartHost() hoac StartClient()
         |
         v
[3] Client connect thanh cong toi server
         |
         v
[4] Mirror tu dong goi NetworkClient.Ready()
         |
         v
[5] Mirror tu dong goi NetworkClient.AddPlayer()
    (gui message toi server yeu cau spawn player)
         |
         v
+------------------------------------------------------------------+
|                          SERVER                                   |
+------------------------------------------------------------------+
         |
         v
[6] Server nhan AddPlayerMessage
         |
         v
[7] NetworkManager.OnServerAddPlayer(conn) duoc goi
         |
         v
[8] Server: Instantiate(playerPrefab)
    - Tao player object tren server
         |
         v
[9] Server: NetworkServer.AddPlayerForConnection(conn, player)
    - Gan ownership cho connection
    - Mirror bat dau dong bo object
         |
         v
[10] Server: PlayerNetwork.OnStartServer() duoc goi
     - Khoi tao server-side logic
         |
         v
+------------------------------------------------------------------+
|                      TAT CA CLIENTS                               |
+------------------------------------------------------------------+
         |
         v
[11] Mirror tu dong tao object tuong tu tren moi client
         |
         v
[12] PlayerNetwork.OnStartClient() duoc goi tren MOI client
     - Cho moi player object (ca cua minh va nguoi khac)
         |
         v
+------------------------------------------------------------------+
|                      CHI LOCAL PLAYER                             |
+------------------------------------------------------------------+
         |
         v
[13] PlayerNetwork.OnStartLocalPlayer() duoc goi
     - CHI tren player cua chinh ban
     - Day la noi setup camera, input, UI
         |
         v
[14] Player san sang choi!
```

---

## Thu tu goi cac Callback

| Thu tu | Callback              | Chay o dau?      | Muc dich                           |
|--------|----------------------|------------------|------------------------------------|
| 1      | OnStartServer()      | Server           | Khoi tao server logic              |
| 2      | OnStartClient()      | Tat ca Clients   | Khoi tao visual, audio             |
| 3      | OnStartLocalPlayer() | Chi Local Player | Setup camera, input, UI            |
| 4      | OnStartAuthority()   | Co authority     | Khi nhan quyen dieu khien          |

---

## Vi du trong code

### SimpleNetworkManager.cs (Server-side spawn)

```csharp
public override void OnServerAddPlayer(NetworkConnectionToClient conn)
{
    // Buoc 1: Chon vi tri spawn
    Vector3 spawnPos = GetSpawnPosition(conn.connectionId);
    
    // Buoc 2: SERVER tao player object
    GameObject player = Instantiate(playerPrefab, spawnPos, Quaternion.identity);
    
    // Buoc 3: Spawn va gan ownership
    // Mirror se TU DONG dong bo object toi tat ca clients
    NetworkServer.AddPlayerForConnection(conn, player);
}
```

### PlayerNetwork.cs (Player callbacks)

```csharp
// Chay tren SERVER khi spawn
public override void OnStartServer()
{
    Debug.Log("[SERVER] Player spawned");
}

// Chay tren TAT CA CLIENTS  
public override void OnStartClient()
{
    Debug.Log("[CLIENT] Player appeared");
}

// CHI chay tren LOCAL PLAYER
public override void OnStartLocalPlayer()
{
    Debug.Log("[LOCAL] This is MY player!");
    // Setup camera, input, UI o day
}
```

---

## Cac khai niem quan trong

| Khai niem          | Giai thich                                              |
|--------------------|---------------------------------------------------------|
| `isLocalPlayer`    | True neu day la player cua ban                          |
| `isServer`         | True neu code dang chay tren server                     |
| `isClient`         | True neu code dang chay tren client                     |
| `isOwned`          | True neu ban co authority tren object nay               |
| `[Command]`        | Function chay tren SERVER, goi tu CLIENT                |
| `[ClientRpc]`      | Function chay tren TAT CA CLIENTS, goi tu SERVER        |
| `[TargetRpc]`      | Function chay tren MOT CLIENT cu the, goi tu SERVER     |
| `[SyncVar]`        | Bien tu dong dong bo tu SERVER -> CLIENTS               |

---

## Luu y khi code

1. **Kiem tra isLocalPlayer truoc khi xu ly input:**
```csharp
void Update()
{
    if (!isLocalPlayer) return; // Chi xu ly input cho player cua minh
    
    float h = Input.GetAxis("Horizontal");
    // ...
}
```

2. **Chi goi [Command] tu isLocalPlayer:**
```csharp
void OnTriggerEnter(Collider other)
{
    if (!isLocalPlayer) return; // Tranh spam commands tu cac client khac
    
    CmdPickupCoin(coinNetId);
}
```

3. **[SyncVar] chi thay doi tren Server:**
```csharp
[SyncVar] public int coinCount = 0;

[Command]
void CmdPickupCoin(uint coinNetId)
{
    // Chi server moi thay doi duoc SyncVar
    coinCount++; // Tu dong dong bo toi clients
}
```

---

## Setup trong Unity

1. **Tao NetworkManager object:**
   - Add component `SimpleNetworkManager` (hoac `NetworkManager`)
   - Add component `NetworkManagerHUD` (de test)
   - Add component `KcpTransport`

2. **Tao Player Prefab:**
   - Add component `NetworkIdentity`
   - Add component `NetworkTransform` hoac `NetworkTransformReliable`
   - Add component `PlayerNetwork`
   - Assign vao `Player Prefab` cua NetworkManager

3. **Tao Coin Prefab:**
   - Add component `NetworkIdentity`
   - Add component `Coin`
   - Tag = "Coin"
   - Collider.isTrigger = true
   - Assign vao `Spawn Prefabs` list cua NetworkManager

---

## Test

1. Build game thanh 2 instance
2. Instance 1: Click "Host"
3. Instance 2: Click "Client"
4. Xem Console de thay flow spawn
