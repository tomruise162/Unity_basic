# Mirror Phase 2 - Summary

## Cac khai niem da implement trong project

### 1) Mirror Fundamentals

| Khai niem | File | Mo ta |
|-----------|------|-------|
| NetworkManager | SimpleNetworkManager.cs | Quan ly ket noi, spawn player |
| NetworkIdentity | PlayerNetwork.cs | Dinh danh object tren network |
| NetworkBehaviour | PlayerNetwork.cs | Base class cho network scripts |
| isServer | FixedUpdate() | Kiem tra dang chay tren server |
| isClient | - | Kiem tra dang chay tren client |
| isLocalPlayer | Update(), OnTriggerEnter() | Kiem tra player cua minh |
| OnStartServer | PlayerNetwork.cs:40 | Goi khi spawn tren server |
| OnStartClient | PlayerNetwork.cs:51 | Goi khi spawn tren client |
| OnStartLocalPlayer | PlayerNetwork.cs:65 | Goi chi cho local player |

---

### 2) Dong bo du lieu

#### SyncVar + Hook

```csharp
// Khai bao
[SyncVar(hook = nameof(OnCoinCountChanged))]
public int coinCount = 0;

// Hook - chay tren CLIENT khi gia tri thay doi
private void OnCoinCountChanged(int oldValue, int newValue)
{
    if (isLocalPlayer)
        CoinUI.NotifyCoinChanged(newValue);
}
```

**Flow:**
```
SERVER: coinCount++ 
    |
    v
Mirror dong bo gia tri moi
    |
    v
CLIENT: OnCoinCountChanged(0, 1) duoc goi
    |
    v
UI update
```

---

#### Command (Client -> Server)

```csharp
// Client goi
CmdPickupCoin(coinNetId);

// Server thuc thi
[Command]
private void CmdPickupCoin(uint coinNetId)
{
    // Validate
    if (!NetworkServer.spawned.TryGetValue(coinNetId, out var coin))
        return;
    
    // Xu ly
    coinCount++;
    NetworkServer.Destroy(coin.gameObject);
}
```

**Flow:**
```
CLIENT: Input detected (OnTriggerEnter)
    |
    v
CLIENT: CmdPickupCoin(42) - gui request
    |
    v (Network)
    v
SERVER: CmdPickupCoin(42) duoc thuc thi
    |
    v
SERVER: Validate + Process
```

---

#### ClientRpc (Server -> All Clients)

```csharp
// Server goi
RpcOnCoinPickedUp(coinPosition);

// Tat ca clients thuc thi
[ClientRpc]
private void RpcOnCoinPickedUp(Vector3 coinPosition)
{
    // Spawn effect, play sound
    Debug.Log($"Coin picked at {coinPosition}");
}
```

**Flow:**
```
SERVER: RpcOnCoinPickedUp(position)
    |
    v (Broadcast)
    v
CLIENT A: RpcOnCoinPickedUp() chay
CLIENT B: RpcOnCoinPickedUp() chay
CLIENT C: RpcOnCoinPickedUp() chay
```

---

#### TargetRpc (Server -> 1 Client)

```csharp
// Server goi cho 1 client cu the
TargetOnYouPickedUpCoin(connectionToClient, coinCount);

// Chi client do thuc thi
[TargetRpc]
private void TargetOnYouPickedUpCoin(NetworkConnection target, int total)
{
    Debug.Log($"YOU picked coin! Total: {total}");
}
```

**Flow:**
```
SERVER: TargetOnYouPickedUpCoin(conn, 5)
    |
    v (Chi gui cho conn)
    v
CLIENT A: Khong nhan gi
CLIENT B: TargetOnYouPickedUpCoin() chay  <-- Chi client nay
CLIENT C: Khong nhan gi
```

---

### 3) Spawn/Despawn & Ownership

#### NetworkServer.Spawn

```csharp
// Tao object tren server
GameObject coin = Instantiate(coinPrefab, position, Quaternion.identity);

// Spawn cho tat ca clients
NetworkServer.Spawn(coin);
```

#### NetworkServer.Destroy

```csharp
// Destroy tren server -> Mirror tu destroy tren clients
NetworkServer.Destroy(coin.gameObject);
```

#### Authority Check

```csharp
// Chi local player moi gui command
if (!isLocalPlayer) return;
CmdPickupCoin(coinNetId);
```

#### Security

```csharp
[Command]
void CmdPickupCoin(uint coinNetId)
{
    // Server VALIDATE truoc khi xu ly
    if (!NetworkServer.spawned.TryGetValue(coinNetId, out var coin))
        return; // Coin khong ton tai hoac da bi nhat
    
    // Chi server moi thay doi SyncVar
    coinCount++;
    
    // Chi server moi destroy
    NetworkServer.Destroy(coin.gameObject);
}
```

---

## Bang so sanh

| Attribute | Huong | Chay o dau | Goi tu dau |
|-----------|-------|------------|------------|
| `[Command]` | Client -> Server | Server | Client |
| `[ClientRpc]` | Server -> All Clients | All Clients | Server |
| `[TargetRpc]` | Server -> 1 Client | 1 Client | Server |
| `[SyncVar]` | Server -> All Clients | Auto sync | - |

---

## Test checklist

- [ ] 2 clients ket noi (Host + Client build)
- [ ] Player di chuyen chi voi local input
- [ ] Nhat coin: Command -> Server validate -> Destroy
- [ ] SyncVar hook update UI
- [ ] ClientRpc thong bao tat ca
- [ ] TargetRpc thong bao rieng

---

## Log output khi nhat coin

```
[SERVER] Player 1 picked up coin. Total: 1
[HOOK] coinCount changed: 0 -> 1
[RPC-ALL] A coin was picked up at (1.5, 0.5, 2.3)
[RPC-TARGET] YOU picked up a coin! Total: 1
```
