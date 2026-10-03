using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace ShoppingCart;

/// <summary>
/// 每个商店（NMerchantInventory）一份的购物车状态：已选条目、槽位角标、面板、结算流程。
/// </summary>
public class CartState
{
    /// <summary>点击模式：true = 点击加入购物车；false = 恢复原版即点即买。</summary>
    public static bool CartModeEnabled = true;

    private static readonly ConditionalWeakTable<NMerchantInventory, CartState> States = new();

    public static CartState For(NMerchantInventory rug) =>
        States.GetValue(rug, _ => new CartState { Rug = rug });

    public NMerchantInventory Rug = null!;
    public CartPanel? Panel;

    private readonly List<MerchantEntry> _entries = new();
    private readonly Dictionary<MerchantEntry, NMerchantSlot> _slots = new();
    private readonly Dictionary<MerchantEntry, Action<PurchaseStatus, MerchantEntry>> _purchaseHandlers = new();
    private readonly Dictionary<MerchantEntry, Action> _updatedHandlers = new();
    private bool _isCheckingOut;
    private int _directPurchaseCount;
    private int _clearVersion;
    private CartPurchasePlan? _checkoutPlan;

    public IReadOnlyList<MerchantEntry> Entries => _entries;
    public bool IsCheckingOut => _isCheckingOut || _directPurchaseCount > 0;
    public bool CheckoutBlockedByTravelOrVote => CheckoutInputGuard.IsTraveling()
        || CheckoutInputGuard.HasPendingMapVote(CheckoutInputGuard.IsUsable(Rug) ? Rug.Inventory?.Player : null);

    /// <summary>Keep cart editing/checkout disabled while an ordinary purchase or removal is awaiting its effects.</summary>
    public Task ObserveDirectPurchaseAsync(Task purchase)
    {
        if (purchase.IsCompleted) return purchase;
        _directPurchaseCount++;
        RefreshPanel();
        return AwaitDirectPurchaseAsync(purchase);
    }

    private async Task AwaitDirectPurchaseAsync(Task purchase)
    {
        try { await purchase; }
        finally
        {
            _directPurchaseCount--;
            RefreshPanel();
        }
    }

    public bool Contains(MerchantEntry entry) => _entries.Contains(entry);

    public void Add(MerchantEntry entry, NMerchantSlot slot)
    {
        if (IsCheckingOut || !entry.IsStocked || _entries.Contains(entry))
        {
            return;
        }
        _entries.Add(entry);
        _slots[entry] = slot;
        SetBadge(slot, true);

        // 包括 LordsParasol 自动购买在内，所有成功购买都移出购物车。
        Action<PurchaseStatus, MerchantEntry> onPurchase = (status, e) =>
        {
            if (status == PurchaseStatus.Success)
            {
                Remove(entry);
            }
        };
        Action onUpdated = RefreshPanel;
        _purchaseHandlers[entry] = onPurchase;
        _updatedHandlers[entry] = onUpdated;
        entry.PurchaseCompleted += onPurchase;
        entry.EntryUpdated += onUpdated;

        RefreshPanel();
    }

    public void Remove(MerchantEntry entry)
    {
        if (!_entries.Remove(entry))
        {
            return;
        }
        if (_purchaseHandlers.TryGetValue(entry, out var purchase))
        {
            entry.PurchaseCompleted -= purchase;
        }
        if (_updatedHandlers.TryGetValue(entry, out var updated))
        {
            entry.EntryUpdated -= updated;
        }
        _purchaseHandlers.Remove(entry);
        _updatedHandlers.Remove(entry);
        if (_slots.TryGetValue(entry, out var slot))
        {
            SetBadge(slot, false);
            _slots.Remove(entry);
        }
        RefreshPanel();
    }

    /// <summary>关闭商店清单时清空购物车（角标隐藏，条目全部移除）。</summary>
    public void Clear()
    {
        // Clearing/closing while awaiting an item cancels the remaining snapshot, even if it is later repopulated.
        _clearVersion++;
        foreach (var entry in _entries.ToList())
        {
            Remove(entry);
        }
        RefreshPanel();
    }

    private static void SetBadge(NMerchantSlot slot, bool visible)
    {
        if (!CheckoutInputGuard.IsUsable(slot)) return;
        try
        {
            // 槽位节点可能被布局容器拉伸，改用真正可点击的 %Hitbox 作为角标宿主。
            var host = slot.GetNodeOrNull<Control>("%Hitbox") ?? slot;
            var badge = host.GetChildren().OfType<CartBadge>().FirstOrDefault(CheckoutInputGuard.IsUsable);
            if (badge == null)
            {
                if (!visible) return;
                badge = new CartBadge();
                host.AddChild(badge);
                badge.PlaceAtTopRight();
            }
            badge.Visible = visible;
        }
        catch (Exception e)
        {
            Log.Error($"[{Mod.Id}] badge update failed: {e}");
        }
    }

    private void RefreshPanel()
    {
        if (!CheckoutInputGuard.IsUsable(Panel)) return;
        try { Panel!.Refresh(); }
        catch (Exception e) { Log.Error($"[{Mod.Id}] cart refresh failed: {e}"); }
    }

    /// <summary>一键结算：逐件走原版购买管线，失败即停（原版的失败提示/抖动会自动触发）。</summary>
    public async Task CheckoutAsync()
    {
        if (IsCheckingOut || _entries.Count == 0 || !CheckoutInputGuard.IsUsable(Rug) || CheckoutBlockedByTravelOrVote)
        {
            return;
        }
        _isCheckingOut = true;
        int clearVersion = _clearVersion;
        CheckoutInputGuard? inputGuard = null;
        try
        {
            var inventory = Rug.Inventory;
            // Fix the order once, using gold available before any purchase or card refund.
            var plan = CartPurchasePlan.Create(_entries, inventory?.Player.Gold ?? 0);
            _checkoutPlan = plan;
            inputGuard = CheckoutInputGuard.Acquire(Rug);
            RefreshPanel();
            foreach (var entry in plan.Entries)
            {
                if (_clearVersion != clearVersion || !CheckoutInputGuard.IsUsable(Rug) || CheckoutBlockedByTravelOrVote
                    || !ReferenceEquals(Rug.Inventory, inventory))
                {
                    break;
                }
                // A successful automatic purchase can remove an entry and refill its slot during an earlier await.
                // That new stock was never selected by the player.
                if (!_entries.Contains(entry)) continue;
                if (!entry.IsStocked)
                {
                    // 已被其它途径买走/清空，直接移出
                    Remove(entry);
                    continue;
                }
                // Removal has its own wrapper: it opens the original cancelable selection/sync flow.
                bool ok = entry is MerchantCardRemovalEntry removal
                    ? await removal.OnTryPurchaseWrapper(inventory, ignoreCost: false, cancelable: true)
                    : await entry.OnTryPurchaseWrapper(inventory);
                if (!ok)
                {
                    Log.Info($"[{Mod.Id}] checkout stopped at {entry.GetType().Name}");
                    break;
                }
                if (_entries.Contains(entry))
                {
                    Remove(entry);
                }
            }
        }
        catch (Exception e)
        {
            Log.Error($"[{Mod.Id}] checkout failed: {e}");
        }
        finally
        {
            _checkoutPlan = null;
            _isCheckingOut = false;
            inputGuard?.Dispose();
            RefreshPanel();
        }
    }

    public CartEstimate Estimate
    {
        get
        {
            var player = CheckoutInputGuard.IsUsable(Rug) ? Rug.Inventory?.Player : null;
            if (_checkoutPlan == null) return CartEstimate.For(player, _entries);

            // Gold gains during an awaited purchase must not reorder a checkout already in progress.
            var remaining = _checkoutPlan.Entries.Where(_entries.Contains).ToList();
            bool memberFirst = _checkoutPlan.MembershipCardFirst && remaining.Count > 0
                && ReferenceEquals(remaining[0], _checkoutPlan.Entries[0]);
            return CartEstimate.For(player, remaining, new CartPurchasePlan(remaining, memberFirst));
        }
    }

    public int TotalCost => Estimate.TotalCost;

    public string DisplayNameFor(MerchantEntry entry)
    {
        try
        {
            switch (entry)
            {
                case MerchantCardRemovalEntry:
                    return Loc.CardRemoval;
                case MerchantCardEntry card when card.CreationResult?.Card != null:
                    return card.CreationResult.Card.Title;
                case MerchantRelicEntry relic when relic.Model != null:
                    return relic.Model.Title.GetFormattedText();
                case MerchantPotionEntry potion when potion.Model != null:
                    return potion.Model.Title.GetFormattedText();
            }
        }
        catch (Exception e)
        {
            Log.Error($"[{Mod.Id}] display name failed: {e}");
        }
        return entry.GetType().Name;
    }
}
