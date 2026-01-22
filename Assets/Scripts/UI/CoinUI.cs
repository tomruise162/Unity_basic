using TMPro;
using UnityEngine;
using Mirror;
using System;

/// <summary>
/// CoinUI - Hien thi so coin cua local player.
/// 
/// Co 2 cach de update UI:
/// 
/// CACH 1 (hien tai): Polling trong Update()
/// - Don gian, de hieu
/// - Chay moi frame, khong toi uu
/// 
/// CACH 2 (tot hon): Event-based
/// - Chi update khi gia tri thay doi
/// - Ket hop voi SyncVar hook trong PlayerNetwork
/// 
/// Code nay giu ca 2 cach de ban hoc.
/// </summary>
public class CoinUI : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI coinText;

    [Header("Settings")]
    [Tooltip("True = use polling (Update), False = use event")]
    public bool usePolling = true;

    private PlayerNetwork localPlayer;

    // Event cho cach 2: PlayerNetwork se goi event nay
    public static event Action<int> OnLocalPlayerCoinChanged;

    void Start()
    {
        // Dang ky lang nghe event (cach 2)
        OnLocalPlayerCoinChanged += UpdateCoinDisplay;
    }

    void OnDestroy()
    {
        // Huy dang ky khi destroy
        OnLocalPlayerCoinChanged -= UpdateCoinDisplay;
    }

    void Update()
    {
        // Chi chay neu dung polling mode
        if (!usePolling) return;

        // Tim local player neu chua co
        if (localPlayer == null)
        {
            localPlayer = NetworkClient.localPlayer?.GetComponent<PlayerNetwork>();
            if (localPlayer == null) return;
        }

        // Update UI moi frame
        UpdateCoinDisplay(localPlayer.coinCount);
    }

    /// <summary>
    /// Update hien thi coin.
    /// Duoc goi tu Update() (polling) hoac tu event (event-based).
    /// </summary>
    private void UpdateCoinDisplay(int coinCount)
    {
        if (coinText != null)
        {
            coinText.text = $"Coins: {coinCount}";
        }
    }

    // =====================================================================
    // STATIC METHOD: Goi tu PlayerNetwork.OnCoinCountChanged (hook)
    // =====================================================================

    /// <summary>
    /// Goi method nay tu SyncVar hook de thong bao UI update.
    /// </summary>
    public static void NotifyCoinChanged(int newValue)
    {
        OnLocalPlayerCoinChanged?.Invoke(newValue);
    }
}
