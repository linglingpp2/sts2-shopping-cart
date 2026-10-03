using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Runs;

namespace ShoppingCart;

/// <summary>Owns only the input changes made by one checkout, and restores them after partial failures.</summary>
internal sealed class CheckoutInputGuard : IDisposable
{
    internal sealed record InputSwitch(bool WasEnabled, Action<bool> SetEnabled);
    internal sealed record InputSnapshot(Action BlockShop, Action RestoreShop, IReadOnlyList<InputSwitch> Switches);

    private static readonly FieldInfo? InputBlockedField = typeof(NMerchantInventory).GetField("_isInputBlocked", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? BackButtonField = typeof(NMerchantInventory).GetField("_backButton", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly PropertyInfo? RunStateProperty = typeof(RunManager).GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic);
    private readonly InputSnapshot _snapshot;
    private readonly List<Action> _restoreControls = new();
    private bool _restoreShop;
    private bool _disposed;

    private CheckoutInputGuard(InputSnapshot snapshot) => _snapshot = snapshot;

    internal static bool IsUsable(GodotObject? node) =>
        node != null && GodotObject.IsInstanceValid(node) && (node is not Node treeNode || !treeNode.IsQueuedForDeletion());

    internal static bool IsTraveling()
    {
        var map = NMapScreen.Instance;
        return IsUsable(map) && map!.IsTraveling;
    }

    /// <summary>A committed local vote can cause a network move even after the travel button is disabled.</summary>
    internal static bool HasPendingMapVote(Player? player)
    {
        bool matchingNetworkRun = false;
        try
        {
            var run = RunManager.Instance;
            if (player == null || run == null || !run.IsInProgress || run.NetService == null
                || run.IsSingleplayerOrFakeMultiplayer)
            {
                return false;
            }
            var activeState = RunStateProperty?.GetValue(run);
            if (activeState == null || !ReferenceEquals(activeState, player.RunState)) return false;
            matchingNetworkRun = true;
            // This only reads the vote. Never cancel, restore, or enqueue a vote on the player's behalf.
            return run.MapSelectionSynchronizer.GetVote(player).HasValue;
        }
        catch
        {
            // A missing/changed synchronizer must not let checkout overlap a move in a known network run.
            // Singleplayer and unrelated runs are unaffected.
            return matchingNetworkRun;
        }
    }

    internal static bool CanRestoreTravel(bool enabled, bool isTraveling) => !enabled || !isTraveling;

    internal static CheckoutInputGuard Acquire(NMerchantInventory rug)
    {
        var guard = new CheckoutInputGuard(CaptureInputState(rug));
        try
        {
            // BlockInput can change part of the UI before throwing, so restore even when it fails.
            guard._restoreShop = true;
            guard._snapshot.BlockShop();
            foreach (var input in guard._snapshot.Switches)
            {
                if (!input.WasEnabled)
                {
                    continue;
                }
                guard._restoreControls.Add(() => input.SetEnabled(true));
                input.SetEnabled(false);
            }
            return guard;
        }
        catch
        {
            guard.Dispose();
            throw;
        }
    }

    // Keep native node reads at this boundary so checkout/partial-failure recovery can be tested separately.
    internal static InputSnapshot CaptureInputState(NMerchantInventory rug)
    {
        if (!IsUsable(rug))
        {
            throw new ObjectDisposedException(nameof(NMerchantInventory));
        }
        bool wasBlocked = (bool)(InputBlockedField?.GetValue(rug)
            ?? throw new MissingFieldException(typeof(NMerchantInventory).FullName, "_isInputBlocked"));
        var back = BackButtonField?.GetValue(rug) as NClickableControl;
        bool backWasEnabled = IsUsable(back) && back!.IsEnabled;
        var switches = new List<InputSwitch>();
        var run = NRun.Instance;
        if (IsUsable(run))
        {
            var mapButton = run!.GlobalUi.TopBar.Map;
            var deckButton = run.GlobalUi.TopBar.Deck;
            AddButton(switches, mapButton);
            AddButton(switches, deckButton);
            var map = NMapScreen.Instance;
            if (IsUsable(map))
            {
                switches.Add(new InputSwitch(map!.IsTravelEnabled, enabled =>
                {
                    // A network move can start while a purchase is awaiting an effect.
                    // Preserve its new travel lock instead of restoring the old enabled state over it.
                    if (IsUsable(map) && CanRestoreTravel(enabled, map.IsTraveling)) map.SetTravelEnabled(enabled);
                }));
            }
        }
        return new InputSnapshot(() =>
        {
            // Native blocking is not reference-counted; re-blocking duplicates hotkey registrations.
            if (!wasBlocked) rug.BlockInput();
        }, () =>
        {
            if (wasBlocked || !IsUsable(rug)) return;
            try { rug.UnblockInput(); }
            finally
            {
                if (IsUsable(back))
                {
                    if (backWasEnabled) back!.Enable();
                    else back!.Disable();
                }
            }
        }, switches);
    }

    private static void AddButton(List<InputSwitch> switches, NClickableControl? button)
    {
        if (!IsUsable(button)) return;
        switches.Add(new InputSwitch(button!.IsEnabled, enabled =>
        {
            if (!IsUsable(button)) return;
            if (enabled) button.Enable();
            else button.Disable();
        }));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        for (int i = _restoreControls.Count - 1; i >= 0; i--)
        {
            RestoreSafely(_restoreControls[i]);
        }
        if (_restoreShop) RestoreSafely(_snapshot.RestoreShop);
    }

    private static void RestoreSafely(Action restore)
    {
        try { restore(); }
        catch (Exception e) { Log.Error($"[{Mod.Id}] input restoration failed: {e}"); }
    }
}
