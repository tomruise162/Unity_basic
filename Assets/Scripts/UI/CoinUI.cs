using TMPro;
using UnityEngine;
using Mirror;
using System;

/// <summary>
/// CoinUI - Shows coin count for the local player.
/// Refactored to work with FallGuysMovement script.
/// </summary>
public class CoinUI : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI coinText;

    [Header("Settings")]
    [Tooltip("True = use polling (Update), False = use event (recommended)")]
    public bool usePolling = false; // Changed default to false as we now have robust events

    private FallGuysMovement localPlayer;

    // Event: FallGuysMovement will call this
    public static event Action<int> OnLocalPlayerCoinChanged;

    void Start()
    {
        OnLocalPlayerCoinChanged += UpdateCoinDisplay;
    }

    void OnDestroy()
    {
        OnLocalPlayerCoinChanged -= UpdateCoinDisplay;
    }

    void Update()
    {
        if (!usePolling) return;

        // Find local player if needed
        if (localPlayer == null)
        {
            localPlayer = NetworkClient.localPlayer?.GetComponent<FallGuysMovement>();
            if (localPlayer == null) return;
        }

        // Poll UI every frame
        UpdateCoinDisplay(localPlayer.coinCount);
    }

    private void UpdateCoinDisplay(int coinCount)
    {
        if (coinText != null)
        {
            coinText.text = $"Coins: {coinCount}";
        }
    }

    // =====================================================================
    // STATIC METHOD: Called from FallGuysMovement.OnCoinCountChanged (hook)
    // =====================================================================

    public static void NotifyCoinChanged(int newValue)
    {
        OnLocalPlayerCoinChanged?.Invoke(newValue);
    }
}
