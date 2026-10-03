using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using ShoppingCart;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Multiplayer.Game;

// Isolated process: the real CartState / MerchantEntry checkout pipeline runs with
// UI methods suppressed. Membership-order cases also replace native relic obtain
// commands, while using the real MembershipCard price hook and purchase wrapper.
// No Godot engine, running game, or save is touched.
var gameDataDir = Environment.GetEnvironmentVariable("STS2_GAME_DATA_DIR")
    ?? Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "GameDataDir").Value;
if (string.IsNullOrWhiteSpace(gameDataDir) || !Directory.Exists(gameDataDir))
    throw new DirectoryNotFoundException("The game DLL directory is unavailable. Run test.ps1/test.sh with your GameDir, or rebuild with the correct GameDataDir.");
gameDataDir = Path.GetFullPath(gameDataDir);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var path = Path.Combine(gameDataDir, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
var harmony = new Harmony("shopping-cart.isolated-validation");
var skip = new HarmonyMethod(typeof(UiStub).GetMethod(nameof(UiStub.Skip))!);
foreach (var method in new[]
{
    AccessTools.Method(typeof(CartState), "SetBadge"),
    AccessTools.Method(typeof(Log), nameof(Log.Info)),
    AccessTools.Method(typeof(Log), nameof(Log.Error))
}) harmony.Patch(method, prefix: skip);
var guardType = typeof(CartState).Assembly.GetType("ShoppingCart.CheckoutInputGuard");
if (guardType != null)
{
    harmony.Patch(AccessTools.Method(typeof(CartState), "RefreshPanel"), prefix: skip);
    harmony.Patch(AccessTools.Method(guardType, "IsUsable"),
        prefix: new HarmonyMethod(typeof(UiStub).GetMethod(nameof(UiStub.IsUsable))!));
    harmony.Patch(AccessTools.Method(guardType, "CaptureInputState"),
        prefix: new HarmonyMethod(typeof(UiStub).GetMethod(nameof(UiStub.Capture))!));
    harmony.Patch(AccessTools.Method(guardType, "IsTraveling"),
        prefix: new HarmonyMethod(typeof(UiStub).GetMethod(nameof(UiStub.IsTraveling))!));
    harmony.Patch(AccessTools.Method(guardType, "HasPendingMapVote"),
        prefix: new HarmonyMethod(typeof(UiStub).GetMethod(nameof(UiStub.HasPendingMapVote))!));
}
harmony.Patch(AccessTools.Method(typeof(NMerchantInventory), nameof(NMerchantInventory.BlockInput)),
    prefix: new HarmonyMethod(typeof(UiStub).GetMethod(nameof(UiStub.Block))!));
harmony.Patch(AccessTools.Method(typeof(NMerchantInventory), nameof(NMerchantInventory.UnblockInput)),
    prefix: new HarmonyMethod(typeof(UiStub).GetMethod(nameof(UiStub.Unblock))!));
harmony.Patch(AccessTools.PropertyGetter(typeof(NullRunState), nameof(NullRunState.CurrentRoom)),
    prefix: new HarmonyMethod(typeof(MerchantStub).GetMethod(nameof(MerchantStub.CurrentRoom))!));
harmony.Patch(AccessTools.Method(typeof(NullRunState), nameof(NullRunState.IterateHookListeners)),
    prefix: new HarmonyMethod(typeof(MerchantStub).GetMethod(nameof(MerchantStub.HookListeners))!));
harmony.Patch(AccessTools.Method(typeof(MerchantRelicEntry), "OnTryPurchase"),
    prefix: new HarmonyMethod(typeof(MerchantStub).GetMethod(nameof(MerchantStub.PurchaseRelic))!));
harmony.Patch(AccessTools.PropertyGetter(typeof(RunManager), nameof(RunManager.IsInProgress)),
    prefix: new HarmonyMethod(typeof(RemovalStub).GetMethod(nameof(RemovalStub.IsRunInProgress))!));
harmony.Patch(AccessTools.Method(typeof(MerchantCardRemovalEntry), nameof(MerchantCardRemovalEntry.OnTryPurchaseWrapper),
        new[] { typeof(MerchantInventory), typeof(bool), typeof(bool) }),
    prefix: new HarmonyMethod(typeof(RemovalStub).GetMethod(nameof(RemovalStub.ObserveTypedWrapper))!));
harmony.Patch(AccessTools.Method(typeof(OneOffSynchronizer), nameof(OneOffSynchronizer.DoLocalMerchantCardRemoval)),
    prefix: new HarmonyMethod(typeof(RemovalStub).GetMethod(nameof(RemovalStub.SelectAndRemove))!));
harmony.Patch(AccessTools.PropertyGetter(typeof(Loc), nameof(Loc.Chinese)),
    prefix: new HarmonyMethod(typeof(UiStub).GetMethod(nameof(UiStub.IsChinese))!));

var results = new List<(string, bool, string)>();
await Check("all selected items are bought", async () =>
{
    var (cart, player) = NewCart(100);
    var a = new TestEntry(player, 30);
    var b = new TestEntry(player, 40);
    Add(cart, a, b);
    await cart.CheckoutAsync();
    return (a.Buys == 1 && b.Buys == 1 && player.Gold == 30 && cart.Entries.Count == 0 && !cart.IsCheckingOut,
        $"buys={a.Buys},{b.Buys}; gold={player.Gold}; remaining={cart.Entries.Count}");
});
await Check("gold failure retains this and remaining items", async () =>
{
    var (cart, player) = NewCart(50);
    var a = new TestEntry(player, 30);
    var b = new TestEntry(player, 40);
    var c = new TestEntry(player, 10);
    Add(cart, a, b, c);
    await cart.CheckoutAsync();
    return (a.Buys == 1 && b.Buys == 0 && c.Buys == 0 && player.Gold == 20 && cart.Entries.SequenceEqual(new[] { b, c }) && !cart.IsCheckingOut,
        $"buys={a.Buys},{b.Buys},{c.Buys}; gold={player.Gold}; remaining={cart.Entries.Count}");
});
await Check("out-of-stock items are skipped", async () =>
{
    var (cart, player) = NewCart(100);
    var a = new TestEntry(player, 30);
    var b = new TestEntry(player, 40);
    Add(cart, a, b);
    a.Stocked = false;
    await cart.CheckoutAsync();
    return (a.Buys == 0 && b.Buys == 1 && player.Gold == 60 && cart.Entries.Count == 0,
        $"buys={a.Buys},{b.Buys}; gold={player.Gold}; remaining={cart.Entries.Count}");
});
await Check("entry removed during earlier purchase is not bought again", async () =>
{
    var (cart, player) = NewCart(100);
    var b = new TestEntry(player, 40);
    var a = new TestEntry(player, 30) { BeforeBuy = () => b.InvokePurchaseCompleted(b) };
    Add(cart, a, b);
    await cart.CheckoutAsync();
    return (a.Buys == 1 && b.Buys == 0 && player.Gold == 70,
        $"buys={a.Buys},{b.Buys}; gold={player.Gold}; remaining={cart.Entries.Count}");
});
await Check("clear during checkout cancels remaining selected purchases", async () =>
{
    var (cart, player) = NewCart(100);
    var a = new TestEntry(player, 30) { BeforeBuy = cart.Clear };
    var b = new TestEntry(player, 40);
    Add(cart, a, b);
    await cart.CheckoutAsync();
    return (a.Buys == 1 && b.Buys == 0 && player.Gold == 70,
        $"buys={a.Buys},{b.Buys}; gold={player.Gold}; remaining={cart.Entries.Count}");
});
await Check("repeat checkout while waiting does not buy twice", async () =>
{
    var (cart, player) = NewCart(100);
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var a = new TestEntry(player, 30) { BeforeBuyAsync = () => gate.Task };
    Add(cart, a);
    var first = cart.CheckoutAsync();
    await cart.CheckoutAsync();
    var pending = cart.IsCheckingOut;
    gate.SetResult();
    await first;
    return (pending && a.Buys == 1 && player.Gold == 70 && UiStub.Blocks == 1 && UiStub.Unblocks == 1 && !cart.IsCheckingOut,
        $"pending={pending}; buys={a.Buys}; gold={player.Gold}; input calls={UiStub.Blocks},{UiStub.Unblocks}");
});
await Check("busy card prefix blocks original purchase", async () =>
{
    var (cart, player) = NewCart(100);
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var a = new TestEntry(player, 30) { BeforeBuyAsync = () => gate.Task };
    Add(cart, a);
    var first = cart.CheckoutAsync();
    var slot = (NMerchantCard)RuntimeHelpers.GetUninitializedObject(typeof(NMerchantCard));
    AccessTools.Field(typeof(NMerchantSlot), "_merchantRug").SetValue(slot, cart.Rug);
    Task result = null!;
    var allowsOriginal = SlotClickPatches.CardPrefix(slot, ref result);
    CartState.CartModeEnabled = false;
    Task directResult = null!;
    var allowsDirectOriginal = SlotClickPatches.CardPrefix(slot, ref directResult);
    CartState.CartModeEnabled = true;
    gate.SetResult();
    await first;
    return (!allowsOriginal && result == Task.CompletedTask && !allowsDirectOriginal && directResult == Task.CompletedTask,
        $"allows cart original={allowsOriginal}; allows direct original={allowsDirectOriginal}; prefix results set={result != null && directResult != null}");
});
await Check("a pending direct purchase blocks checkout and cart additions until success", async () =>
{
    var (cart, player) = NewCart(100);
    var a = new TestEntry(player, 30);
    Add(cart, a);
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var observed = cart.ObserveDirectPurchaseAsync(gate.Task);
    var pending = cart.IsCheckingOut;
    await cart.CheckoutAsync();
    var b = new TestEntry(player, 40);
    Add(cart, b);
    var blocked = a.Buys == 0 && cart.Entries.SequenceEqual(new[] { a }) && UiStub.Blocks == 0;
    gate.SetResult();
    await observed;
    var recovered = !cart.IsCheckingOut;
    await cart.CheckoutAsync();
    return (pending && blocked && recovered && a.Buys == 1 && player.Gold == 70,
        $"pending={pending}; blocked={blocked}; recovered={recovered}; checkout buys={a.Buys}");
});
await Check("a failed direct purchase preserves its exception and releases busy state", async () =>
{
    var (cart, player) = NewCart(100);
    var a = new TestEntry(player, 30);
    Add(cart, a);
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var observed = cart.ObserveDirectPurchaseAsync(gate.Task);
    var expected = new InvalidOperationException("Simulated failed direct purchase");
    gate.SetException(expected);
    Exception? actual = null;
    try { await observed; } catch (Exception ex) { actual = ex; }
    var recovered = !cart.IsCheckingOut;
    await cart.CheckoutAsync();
    return (ReferenceEquals(actual, expected) && recovered && a.Buys == 1,
        $"exception preserved={ReferenceEquals(actual, expected)}; recovered={recovered}; retry buys={a.Buys}");
});
await Check("already completed direct tasks are returned unchanged without blocking", async () =>
{
    var (cart, _) = NewCart(100);
    var complete = Task.CompletedTask;
    var failed = Task.FromException(new InvalidOperationException("Already failed"));
    var sameSuccess = ReferenceEquals(cart.ObserveDirectPurchaseAsync(complete), complete);
    var sameFailure = ReferenceEquals(cart.ObserveDirectPurchaseAsync(failed), failed);
    try { await failed; } catch (InvalidOperationException) { }
    return (sameSuccess && sameFailure && !cart.IsCheckingOut && UiStub.Blocks == 0,
        $"same success={sameSuccess}; same failure={sameFailure}; busy={cart.IsCheckingOut}");
});
await Check("multiple direct purchases remain busy until the last one completes", async () =>
{
    var (cart, _) = NewCart(100);
    var firstGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var secondGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var first = cart.ObserveDirectPurchaseAsync(firstGate.Task);
    var second = cart.ObserveDirectPurchaseAsync(secondGate.Task);
    firstGate.SetResult();
    await first;
    var stillBusy = cart.IsCheckingOut;
    secondGate.SetResult();
    await second;
    return (stillBusy && !cart.IsCheckingOut,
        $"busy after first={stillBusy}; busy after last={cart.IsCheckingOut}");
});
await Check("a queued item remains selected after a pending direct purchase fails", async () =>
{
    var (cart, player) = NewCart(100);
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var entry = new TestEntry(player, 30) { PurchaseSucceeds = false, BeforeBuyAsync = () => gate.Task };
    var slot = NewTestSlot(cart, entry);
    cart.Add(entry, slot);
    int completed = 0;
    int failed = 0;
    entry.PurchaseCompleted += (_, _) => completed++;
    entry.PurchaseFailed += _ => failed++;
    bool allowsOriginal;
    bool queuedAfterClick;
    CartState.CartModeEnabled = false;
    try
    {
        Task prefixResult = null!;
        allowsOriginal = SlotClickPatches.CardPrefix(slot, ref prefixResult);
        queuedAfterClick = cart.Contains(entry);
    }
    finally { CartState.CartModeEnabled = true; }
    var purchase = entry.OnTryPurchaseWrapper(cart.Rug.Inventory);
    Task observed = purchase;
    PendingSelectionPatch.Postfix(slot, ref observed);
    var pending = cart.IsCheckingOut && !observed.IsCompleted && cart.Contains(entry);
    gate.SetResult();
    await observed;
    var success = await purchase;
    return (allowsOriginal && queuedAfterClick && pending && !success && !cart.IsCheckingOut
        && cart.Contains(entry) && entry.Buys == 0 && player.Gold == 100 && completed == 0 && failed == 1,
        $"allowed={allowsOriginal}; queued after click={queuedAfterClick}; pending={pending}; success={success}; queued after failure={cart.Contains(entry)}; events={completed},{failed}");
});
await Check("a queued item is removed only after a direct purchase completes successfully", async () =>
{
    var (cart, player) = NewCart(100);
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var entry = new TestEntry(player, 30) { BeforeBuyAsync = () => gate.Task };
    var slot = NewTestSlot(cart, entry);
    cart.Add(entry, slot);
    int completed = 0;
    entry.PurchaseCompleted += (_, _) => completed++;
    bool allowsOriginal;
    bool queuedAfterClick;
    CartState.CartModeEnabled = false;
    try
    {
        Task prefixResult = null!;
        allowsOriginal = SlotClickPatches.CardPrefix(slot, ref prefixResult);
        queuedAfterClick = cart.Contains(entry);
    }
    finally { CartState.CartModeEnabled = true; }
    var purchase = entry.OnTryPurchaseWrapper(cart.Rug.Inventory);
    Task observed = purchase;
    PendingSelectionPatch.Postfix(slot, ref observed);
    var pending = cart.IsCheckingOut && !observed.IsCompleted && cart.Contains(entry);
    gate.SetResult();
    await observed;
    var success = await purchase;
    return (allowsOriginal && queuedAfterClick && pending && success && !cart.IsCheckingOut
        && !cart.Contains(entry) && entry.Buys == 1 && player.Gold == 70 && completed == 1,
        $"allowed={allowsOriginal}; queued after click={queuedAfterClick}; pending={pending}; success={success}; queued after success={cart.Contains(entry)}; completed events={completed}");
});
await Check("BlockInput failure resets busy state", async () =>
{
    var (cart, player) = NewCart(100);
    var a = new TestEntry(player, 30);
    Add(cart, a);
    UiStub.ThrowOnBlock = true;
    var escaped = false;
    try { await cart.CheckoutAsync(); }
    catch (InvalidOperationException) { escaped = true; }
    UiStub.ThrowOnBlock = false;
    var stuckBusy = cart.IsCheckingOut;
    await cart.CheckoutAsync();
    return (!stuckBusy && a.Buys == 1 && !cart.IsCheckingOut,
        $"escaped={escaped}; stuck busy={stuckBusy}; retry buys={a.Buys}; input calls={UiStub.Blocks},{UiStub.Unblocks}");
});
await Check("partially disabled controls are restored after failure", async () =>
{
    var (cart, player) = NewCart(100);
    var a = new TestEntry(player, 30);
    Add(cart, a);
    UiStub.ThrowOnDisableIndex = 2;
    await cart.CheckoutAsync();
    var restored = !cart.IsCheckingOut && a.Buys == 0 && UiStub.ControlStates.SequenceEqual(new[] { true, false, true });
    UiStub.ThrowOnDisableIndex = -1;
    await cart.CheckoutAsync();
    return (restored && a.Buys == 1 && UiStub.Unblocks == 2 && UiStub.ControlStates.SequenceEqual(new[] { true, false, true }),
        $"restored after failure={restored}; retry buys={a.Buys}; states={string.Join(',', UiStub.ControlStates)}");
});
await Check("restoration continues after one control restore throws", async () =>
{
    var (cart, player) = NewCart(100);
    var a = new TestEntry(player, 30);
    Add(cart, a);
    UiStub.ThrowOnRestoreIndex = 2;
    await cart.CheckoutAsync();
    return (!cart.IsCheckingOut && a.Buys == 1 && UiStub.ControlStates[0] && !UiStub.ControlStates[1] && UiStub.Unblocks == 1,
        $"busy={cart.IsCheckingOut}; buys={a.Buys}; other control restored={UiStub.ControlStates[0]}; unblocks={UiStub.Unblocks}");
});
await Check("native snapshot preserves an existing shop input lock", async () =>
{
    var (cart, player) = NewCart(100);
    UiStub.UseNativeCapture = true;
    UiStub.SetBlocked(cart.Rug, true);
    var a = new TestEntry(player, 30);
    Add(cart, a);
    await cart.CheckoutAsync();
    return (a.Buys == 1 && UiStub.Blocks == 0 && UiStub.Unblocks == 0 && UiStub.GetBlocked(cart.Rug),
        $"buys={a.Buys}; input calls={UiStub.Blocks},{UiStub.Unblocks}; pre-existing lock={UiStub.GetBlocked(cart.Rug)}");
});
await Check("native snapshot releases a newly acquired shop input lock", async () =>
{
    var (cart, player) = NewCart(100);
    UiStub.UseNativeCapture = true;
    var a = new TestEntry(player, 30);
    Add(cart, a);
    await cart.CheckoutAsync();
    return (a.Buys == 1 && UiStub.Blocks == 1 && UiStub.Unblocks == 1 && !UiStub.GetBlocked(cart.Rug),
        $"buys={a.Buys}; input calls={UiStub.Blocks},{UiStub.Unblocks}; lock={UiStub.GetBlocked(cart.Rug)}");
});
await Check("replacing shop inventory cancels remaining purchases", async () =>
{
    var (cart, player) = NewCart(100);
    var a = new TestEntry(player, 30) { BeforeBuy = () =>
        AccessTools.Property(typeof(NMerchantInventory), nameof(NMerchantInventory.Inventory))!.SetValue(cart.Rug, new MerchantInventory(player)) };
    var b = new TestEntry(player, 40);
    Add(cart, a, b);
    await cart.CheckoutAsync();
    return (a.Buys == 1 && b.Buys == 0 && player.Gold == 70 && cart.Entries.SequenceEqual(new[] { b }),
        $"buys={a.Buys},{b.Buys}; gold={player.Gold}; remaining={cart.Entries.Count}");
});
await Check("queued or disposed inventory cancels remaining purchases", async () =>
{
    var (cart, player) = NewCart(100);
    var a = new TestEntry(player, 30) { BeforeBuy = () => UiStub.InvalidNodes.Add(cart.Rug) };
    var b = new TestEntry(player, 40);
    Add(cart, a, b);
    await cart.CheckoutAsync();
    return (a.Buys == 1 && b.Buys == 0 && player.Gold == 70 && !cart.IsCheckingOut,
        $"buys={a.Buys},{b.Buys}; gold={player.Gold}; busy={cart.IsCheckingOut}");
});
await Check("membership discount applies only to subsequent entries before rounding", () =>
{
    var (_, player) = NewCart(200);
    var member = new TestEntry(player, 100);
    var card = new TestEntry(player, 99);
    var estimate = CartEstimate.Calculate(new[]
    {
        new CartEstimateInput(member, 100m, false, 0.5m),
        new CartEstimateInput(card, 99m, true)
    }, 200, 0);
    return Task.FromResult((estimate.TotalCost == 149 && estimate.RemainingGold == 51 && estimate.CanAfford
        && estimate.CostFor(member) == 100 && estimate.CostFor(card) == 49,
        $"costs={string.Join(',', estimate.Entries.Select(e => e.Cost))}; total={estimate.TotalCost}; remaining={estimate.RemainingGold}"));
});
await Check("membership acquired after the first item preserves purchase order", () =>
{
    var (_, player) = NewCart(250);
    var first = new TestEntry(player, 99);
    var member = new TestEntry(player, 100);
    var last = new TestEntry(player, 101);
    var estimate = CartEstimate.Calculate(new[]
    {
        new CartEstimateInput(first, 99m, true),
        new CartEstimateInput(member, 100m, false, 0.5m),
        new CartEstimateInput(last, 101m, true)
    }, 250, 0);
    return Task.FromResult((estimate.TotalCost == 249 && estimate.RemainingGold == 1 && estimate.CanAfford
        && estimate.Entries.Select(e => e.Cost).SequenceEqual(new[] { 99, 100, 50 }),
        $"costs={string.Join(',', estimate.Entries.Select(e => e.Cost))}; total={estimate.TotalCost}; remaining={estimate.RemainingGold}"));
});
await Check("stacked known discounts are multiplied before final integer rounding", () =>
{
    var (_, player) = NewCart(300);
    var member = new TestEntry(player, 100);
    var courier = new TestEntry(player, 199);
    var final = new TestEntry(player, 23);
    var estimate = CartEstimate.Calculate(new[]
    {
        new CartEstimateInput(member, 100m, false, 0.5m),
        new CartEstimateInput(courier, 199m, false, 0.8m),
        new CartEstimateInput(final, 23m, true)
    }, 300, 0);
    return Task.FromResult((estimate.TotalCost == 208 && estimate.RemainingGold == 92 && estimate.CanAfford
        && estimate.Entries.Select(e => e.Cost).SequenceEqual(new[] { 100, 99, 9 }),
        $"costs={string.Join(',', estimate.Entries.Select(e => e.Cost))}; total={estimate.TotalCost}; remaining={estimate.RemainingGold}"));
});
await Check("LuckyFysh refunds can fund a later item", () =>
{
    var (_, player) = NewCart(50);
    var card = new TestEntry(player, 30);
    var relic = new TestEntry(player, 35);
    var estimate = CartEstimate.Calculate(new[]
    {
        new CartEstimateInput(card, 30m, true),
        new CartEstimateInput(relic, 35m, false)
    }, 50, 15);
    return Task.FromResult((estimate.TotalCost == 65 && estimate.RemainingGold == 0 && estimate.CanAfford
        && estimate.Entries.Select(e => e.GoldAfterPurchase).SequenceEqual(new[] { 35, 0 }),
        $"total={estimate.TotalCost}; remaining={estimate.RemainingGold}; affordable={estimate.CanAfford}"));
});
await Check("LuckyFysh cannot fund a card before it is purchased", () =>
{
    var (_, player) = NewCart(50);
    var card = new TestEntry(player, 60);
    var estimate = CartEstimate.Calculate(new[] { new CartEstimateInput(card, 60m, true) }, 50, 15, true);
    return Task.FromResult((!estimate.CanAfford && !estimate.Entries[0].CanAfford && estimate.HasUncertainty,
        $"remaining={estimate.RemainingGold}; affordable={estimate.CanAfford}; uncertainty={estimate.HasUncertainty}"));
});

await Check("an affordable selected membership card is bought first and estimates match real price hooks", async () =>
{
    var (cart, player) = NewCart(250);
    MerchantStub.Enable();
    var first = new TestEntry(player, 99);
    var member = MerchantStub.Membership(player, 100);
    var last = new TestEntry(player, 101);
    Add(cart, first, member, last);
    var selection = cart.Entries.ToArray();
    var estimate = cart.Estimate;
    var selectionPreserved = cart.Entries.SequenceEqual(selection);
    await cart.CheckoutAsync();
    var expectedOrder = new MerchantEntry[] { member, first, last };
    return (selectionPreserved && estimate.MembershipCardFirst && estimate.CanAfford
        && estimate.Entries.Select(row => row.Entry).SequenceEqual(expectedOrder)
        && estimate.Entries.Select(row => row.Cost).SequenceEqual(new[] { 100, 49, 50 })
        && MerchantStub.Purchases.Select(row => row.Entry).SequenceEqual(expectedOrder)
        && MerchantStub.Purchases.Select(row => row.Cost).SequenceEqual(new[] { 100, 49, 50 })
        && estimate.TotalCost == 199 && estimate.RemainingGold == 51 && player.Gold == 51 && cart.Entries.Count == 0,
        $"selection preserved={selectionPreserved}; estimated costs={string.Join(',', estimate.Entries.Select(row => row.Cost))}; paid costs={string.Join(',', MerchantStub.Purchases.Select(row => row.Cost))}; gold={player.Gold}");
});
await Check("having exactly the membership price buys it first but does not pretend the rest is affordable", async () =>
{
    var (cart, player) = NewCart(100);
    MerchantStub.Enable();
    var first = new TestEntry(player, 50);
    var member = MerchantStub.Membership(player, 100);
    var last = new TestEntry(player, 10);
    Add(cart, first, member, last);
    var estimate = cart.Estimate;
    await cart.CheckoutAsync();
    return (estimate.MembershipCardFirst && !estimate.CanAfford && estimate.TotalCost == 130
        && MerchantStub.Purchases.Select(row => row.Entry).SequenceEqual(new MerchantEntry[] { member })
        && player.Gold == 0 && first.Buys == 0 && last.Buys == 0
        && cart.Entries.SequenceEqual(new MerchantEntry[] { first, last }) && !cart.IsCheckingOut,
        $"priority={estimate.MembershipCardFirst}; affordable={estimate.CanAfford}; total={estimate.TotalCost}; purchases={MerchantStub.Purchases.Count}; gold={player.Gold}");
});
await Check("an initially unaffordable membership preserves click order and stops on its gold failure", async () =>
{
    var (cart, player) = NewCart(99);
    MerchantStub.Enable();
    var first = new TestEntry(player, 20);
    var member = MerchantStub.Membership(player, 100);
    var last = new TestEntry(player, 10);
    Add(cart, first, member, last);
    var estimate = cart.Estimate;
    await cart.CheckoutAsync();
    return (!estimate.MembershipCardFirst && !estimate.CanAfford
        && estimate.Entries.Select(row => row.Entry).SequenceEqual(new MerchantEntry[] { first, member, last })
        && MerchantStub.Purchases.Select(row => row.Entry).SequenceEqual(new MerchantEntry[] { first })
        && player.Gold == 79 && last.Buys == 0 && cart.Entries.SequenceEqual(new MerchantEntry[] { member, last }),
        $"priority={estimate.MembershipCardFirst}; bought first={first.Buys}; bought last={last.Buys}; gold={player.Gold}");
});
await Check("a membership offered in the shop but not selected is never added or purchased", async () =>
{
    var (cart, player) = NewCart(250);
    MerchantStub.Enable();
    var first = new TestEntry(player, 99);
    var unselected = MerchantStub.Membership(player, 100);
    cart.Rug.Inventory!.AddRelicEntry(unselected);
    var last = new TestEntry(player, 101);
    Add(cart, first, last);
    var estimate = cart.Estimate;
    await cart.CheckoutAsync();
    return (!estimate.MembershipCardFirst && estimate.TotalCost == 200 && estimate.RemainingGold == 50
        && MerchantStub.Purchases.Select(row => row.Entry).SequenceEqual(new MerchantEntry[] { first, last })
        && unselected.IsStocked && player.Gold == 50 && cart.Entries.Count == 0,
        $"priority={estimate.MembershipCardFirst}; unselected still stocked={unselected.IsStocked}; paid total={MerchantStub.Purchases.Sum(row => row.Cost)}; gold={player.Gold}");
});
await Check("membership priority is stable for all other selected items and leaves the source list unchanged", () =>
{
    var (_, player) = NewCart(500);
    MerchantStub.Enable();
    var first = new TestEntry(player, 10);
    var second = new TestEntry(player, 20);
    var member = MerchantStub.Membership(player, 100);
    var third = new TestEntry(player, 30);
    var selected = new MerchantEntry[] { first, second, member, third };
    var plan = CartPurchasePlan.Create(selected, 500);
    return Task.FromResult((plan.MembershipCardFirst
        && plan.Entries.SequenceEqual(new MerchantEntry[] { member, first, second, third })
        && selected.SequenceEqual(new MerchantEntry[] { first, second, member, third })
        && !ReferenceEquals(plan.Entries, selected),
        $"priority={plan.MembershipCardFirst}; stable={plan.Entries.Skip(1).SequenceEqual(new MerchantEntry[] { first, second, third })}; snapshot owns its list={!ReferenceEquals(plan.Entries, selected)}"));
});
await Check("a sold-out selected membership is not prioritized or bought", async () =>
{
    var (cart, player) = NewCart(250);
    MerchantStub.Enable();
    var first = new TestEntry(player, 99);
    var member = MerchantStub.Membership(player, 100);
    var last = new TestEntry(player, 101);
    Add(cart, first, member, last);
    AccessTools.Property(typeof(MerchantRelicEntry), nameof(MerchantRelicEntry.Model))!.SetValue(member, null);
    var plan = CartPurchasePlan.Create(cart.Entries, player.Gold);
    await cart.CheckoutAsync();
    return (!plan.MembershipCardFirst && plan.Entries.SequenceEqual(new MerchantEntry[] { first, member, last })
        && MerchantStub.Purchases.Select(row => row.Entry).SequenceEqual(new MerchantEntry[] { first, last })
        && player.Gold == 50 && cart.Entries.Count == 0,
        $"priority={plan.MembershipCardFirst}; purchases={MerchantStub.Purchases.Count}; gold={player.Gold}; remaining={cart.Entries.Count}");
});
await Check("clearing during the prioritized membership purchase cancels the remaining checkout snapshot", async () =>
{
    var (cart, player) = NewCart(250);
    MerchantStub.Enable();
    var first = new TestEntry(player, 99);
    var member = MerchantStub.Membership(player, 100, beforeBuy: cart.Clear);
    var last = new TestEntry(player, 101);
    Add(cart, first, member, last);
    await cart.CheckoutAsync();
    return (MerchantStub.Purchases.Select(row => row.Entry).SequenceEqual(new MerchantEntry[] { member })
        && first.Buys == 0 && last.Buys == 0 && player.Gold == 150 && cart.Entries.Count == 0 && !cart.IsCheckingOut,
        $"purchases={MerchantStub.Purchases.Count}; other buys={first.Buys},{last.Buys}; gold={player.Gold}; busy={cart.IsCheckingOut}");
});
await Check("a membership removed automatically during an earlier awaited purchase is never bought from old stock", async () =>
{
    var (cart, player) = NewCart(50);
    MerchantStub.Enable();
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var first = new TestEntry(player, 20) { BeforeBuyAsync = () => gate.Task };
    var member = MerchantStub.Membership(player, 100);
    var last = new TestEntry(player, 10);
    Add(cart, first, member, last);
    var checkout = cart.CheckoutAsync();
    member.InvokePurchaseCompleted(member);
    gate.SetResult();
    await checkout;
    return (MerchantStub.Purchases.Select(row => row.Entry).SequenceEqual(new MerchantEntry[] { first, last })
        && member.IsStocked && player.Gold == 20 && cart.Entries.Count == 0 && !cart.IsCheckingOut,
        $"purchases={MerchantStub.Purchases.Count}; member still stocked={member.IsStocked}; gold={player.Gold}; remaining={cart.Entries.Count}");
});
await Check("gold gained during an awaited purchase keeps its fixed estimate order and restores idle priority after failure", async () =>
{
    var (cart, player) = NewCart(99);
    MerchantStub.Enable();
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var first = new TestEntry(player, 20) { PurchaseSucceeds = false, BeforeBuyAsync = () => gate.Task };
    var member = MerchantStub.Membership(player, 100);
    var last = new TestEntry(player, 10);
    Add(cart, first, member, last);
    var clickOrder = new MerchantEntry[] { first, member, last };
    var initial = cart.Estimate;
    var checkout = cart.CheckoutAsync();
    player.Gold += 15;
    var duringAwait = cart.Estimate;
    bool remainedFixed = cart.IsCheckingOut && !checkout.IsCompleted
        && !duringAwait.MembershipCardFirst && duringAwait.Entries.Select(row => row.Entry).SequenceEqual(clickOrder);
    gate.SetResult();
    await checkout;
    var idle = cart.Estimate;
    return (!initial.MembershipCardFirst && remainedFixed && player.Gold == 114
        && MerchantStub.Purchases.Count == 0 && cart.Entries.SequenceEqual(clickOrder) && !cart.IsCheckingOut
        && idle.MembershipCardFirst && idle.Entries.Select(row => row.Entry).SequenceEqual(new MerchantEntry[] { member, first, last }),
        $"busy order fixed={remainedFixed}; gold={player.Gold}; failed purchase retained={cart.Entries.SequenceEqual(clickOrder)}; idle membership first={idle.MembershipCardFirst}");
});

await Check("removal service clicks add once, include its cost, and toggle it out without purchasing", () =>
{
    var (cart, player) = NewCart(200);
    var removal = RemovalStub.Create(player, 75);
    var slot = NewRemovalSlot(cart, removal);
    Task firstResult = null!;
    bool firstOriginal = SlotClickPatches.RemovalPrefix(slot, ref firstResult);
    bool selectedOnce = cart.Entries.SequenceEqual(new[] { removal }) && cart.TotalCost == 75
        && cart.DisplayNameFor(removal) == Loc.CardRemoval && player.Gold == 200;
    Task secondResult = null!;
    bool secondOriginal = SlotClickPatches.RemovalPrefix(slot, ref secondResult);
    return Task.FromResult((!firstOriginal && !secondOriginal && firstResult == Task.CompletedTask
        && secondResult == Task.CompletedTask && selectedOnce && cart.Entries.Count == 0
        && RemovalStub.SelectionCalls == 0 && !removal.Used,
        $"selected once={selectedOnce}; toggled out={cart.Entries.Count == 0}; original calls allowed={firstOriginal},{secondOriginal}; gold={player.Gold}"));
});
await Check("a used removal service cannot be selected by clicking or adding directly", () =>
{
    var (cart, player) = NewCart(200);
    var removal = RemovalStub.Create(player, 75);
    removal.SetUsed();
    var slot = NewRemovalSlot(cart, removal);
    Task result = null!;
    bool original = SlotClickPatches.RemovalPrefix(slot, ref result);
    cart.Add(removal, slot);
    return Task.FromResult((!original && result == Task.CompletedTask && cart.Entries.Count == 0
        && RemovalStub.SelectionCalls == 0 && player.Gold == 200,
        $"original allowed={original}; selected={cart.Entries.Count}; selection calls={RemovalStub.SelectionCalls}; gold={player.Gold}"));
});
await Check("direct mode preserves original removal and retains the queued service after cancellation", async () =>
{
    var (cart, player) = NewCart(200);
    var removal = RemovalStub.Create(player, 75);
    var slot = NewRemovalSlot(cart, removal);
    cart.Add(removal, slot);
    RemovalStub.Succeeds = false;
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    RemovalStub.WaitForSelection = () => gate.Task;
    bool allowsOriginal;
    CartState.CartModeEnabled = false;
    try
    {
        Task prefixResult = null!;
        allowsOriginal = SlotClickPatches.RemovalPrefix(slot, ref prefixResult);
    }
    finally { CartState.CartModeEnabled = true; }
    var purchase = removal.OnTryPurchaseWrapper(cart.Rug.Inventory, false, true);
    Task observed = purchase;
    PendingSelectionPatch.Postfix(slot, ref observed);
    bool pending = cart.IsCheckingOut && cart.Contains(removal) && !observed.IsCompleted;
    gate.SetResult();
    await observed;
    bool success = await purchase;
    return (allowsOriginal && pending && !success && cart.Contains(removal) && !cart.IsCheckingOut
        && player.Gold == 200 && RemovalStub.TypedWrapperCalls == 1 && RemovalStub.SelectionCalls == 1,
        $"original allowed={allowsOriginal}; pending={pending}; canceled={ !success}; queued after cancellation={cart.Contains(removal)}; gold={player.Gold}");
});
await Check("a busy checkout blocks removal clicks in both shopping modes", async () =>
{
    var (cart, player) = NewCart(200);
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var first = new TestEntry(player, 30) { BeforeBuyAsync = () => gate.Task };
    var removal = RemovalStub.Create(player, 75);
    Add(cart, first, removal);
    var checkout = cart.CheckoutAsync();
    var slot = NewRemovalSlot(cart, removal);
    Task cartResult = null!;
    bool cartOriginal = SlotClickPatches.RemovalPrefix(slot, ref cartResult);
    CartState.CartModeEnabled = false;
    bool directOriginal;
    Task directResult = null!;
    try { directOriginal = SlotClickPatches.RemovalPrefix(slot, ref directResult); }
    finally { CartState.CartModeEnabled = true; }
    bool selectedWhileBusy = cart.Contains(removal) && RemovalStub.SelectionCalls == 0;
    gate.SetResult();
    await checkout;
    return (!cartOriginal && !directOriginal && cartResult == Task.CompletedTask && directResult == Task.CompletedTask
        && selectedWhileBusy && first.Buys == 1 && removal.Used && RemovalStub.SelectionCalls == 1,
        $"blocked={ !cartOriginal},{ !directOriginal}; selected while busy={selectedWhileBusy}; removal calls={RemovalStub.SelectionCalls}");
});
await Check("canceling removal keeps it and later items without charging or completing purchase", async () =>
{
    var (cart, player) = NewCart(200);
    var removal = RemovalStub.Create(player, 75);
    var later = new TestEntry(player, 40);
    Add(cart, removal, later);
    RemovalStub.Succeeds = false;
    int completed = 0;
    removal.PurchaseCompleted += (_, _) => completed++;
    await cart.CheckoutAsync();
    return (RemovalStub.TypedWrapperCalls == 1 && RemovalStub.SelectionCalls == 1 && RemovalStub.LastCancelable
        && !removal.Used && player.Gold == 200 && completed == 0 && later.Buys == 0
        && cart.Entries.SequenceEqual(new MerchantEntry[] { removal, later }) && !cart.IsCheckingOut && UiStub.Unblocks == 1,
        $"typed wrapper={RemovalStub.TypedWrapperCalls}; cancelable={RemovalStub.LastCancelable}; gold={player.Gold}; later buys={later.Buys}; complete events={completed}; remaining={cart.Entries.Count}");
});
await Check("successful removal spends once, exhausts the service, emits success once, and continues checkout", async () =>
{
    var (cart, player) = NewCart(200);
    var removal = RemovalStub.Create(player, 75);
    var later = new TestEntry(player, 40);
    Add(cart, removal, later);
    int completed = 0;
    removal.PurchaseCompleted += (_, _) => completed++;
    await cart.CheckoutAsync();
    await cart.CheckoutAsync();
    cart.Add(removal, NewRemovalSlot(cart, removal));
    return (RemovalStub.TypedWrapperCalls == 1 && RemovalStub.SelectionCalls == 1 && RemovalStub.LastGoldCost == 75
        && removal.Used && !removal.IsStocked && player.Gold == 85 && completed == 1 && later.Buys == 1
        && cart.Entries.Count == 0 && !cart.IsCheckingOut,
        $"typed wrapper={RemovalStub.TypedWrapperCalls}; removal calls={RemovalStub.SelectionCalls}; used={removal.Used}; gold={player.Gold}; completed events={completed}; later buys={later.Buys}");
});
await Check("an unaffordable removal reports gold failure before opening selection and stops later purchases", async () =>
{
    var (cart, player) = NewCart(74);
    var removal = RemovalStub.Create(player, 75);
    var later = new TestEntry(player, 10);
    Add(cart, removal, later);
    int goldFailures = 0;
    removal.PurchaseFailed += status => { if (status == PurchaseStatus.FailureGold) goldFailures++; };
    var estimate = cart.Estimate;
    await cart.CheckoutAsync();
    return (!estimate.CanAfford && estimate.TotalCost == 85 && RemovalStub.TypedWrapperCalls == 1
        && RemovalStub.SelectionCalls == 0 && goldFailures == 1 && player.Gold == 74 && later.Buys == 0
        && !removal.Used && cart.Entries.SequenceEqual(new MerchantEntry[] { removal, later }) && !cart.IsCheckingOut,
        $"estimated affordable={estimate.CanAfford}; selection calls={RemovalStub.SelectionCalls}; gold failures={goldFailures}; gold={player.Gold}; later buys={later.Buys}");
});
await Check("removal waits for player confirmation while cart editing and concurrent checkout stay blocked", async () =>
{
    var (cart, player) = NewCart(200);
    var removal = RemovalStub.Create(player, 75);
    var later = new TestEntry(player, 40);
    Add(cart, removal, later);
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    RemovalStub.WaitForSelection = () => gate.Task;
    var checkout = cart.CheckoutAsync();
    await cart.CheckoutAsync();
    cart.Add(new TestEntry(player, 10), NewTestSlot(cart, later));
    bool waiting = !checkout.IsCompleted && cart.IsCheckingOut && player.Gold == 200 && !removal.Used
        && later.Buys == 0 && cart.Entries.Count == 2 && UiStub.Blocks == 1 && UiStub.Unblocks == 0;
    gate.SetResult();
    await checkout;
    return (waiting && RemovalStub.TypedWrapperCalls == 1 && RemovalStub.SelectionCalls == 1
        && player.Gold == 85 && later.Buys == 1 && !cart.IsCheckingOut && UiStub.Unblocks == 1,
        $"waiting without charge={waiting}; typed wrapper={RemovalStub.TypedWrapperCalls}; gold={player.Gold}; later buys={later.Buys}; unlocks={UiStub.Unblocks}");
});
await Check("membership priority discounts removal before estimate and real checkout with native rounding", async () =>
{
    var (cart, player) = NewCart(250);
    MerchantStub.Enable();
    var removal = RemovalStub.Create(player, 75);
    var member = MerchantStub.Membership(player, 100);
    var later = new TestEntry(player, 40);
    Add(cart, removal, member, later);
    var expected = new MerchantEntry[] { member, removal, later };
    var estimate = cart.Estimate;
    await cart.CheckoutAsync();
    return (estimate.MembershipCardFirst && estimate.CanAfford && estimate.TotalCost == 157
        && estimate.RemainingGold == 93 && estimate.Entries.Select(row => row.Entry).SequenceEqual(expected)
        && estimate.Entries.Select(row => row.Cost).SequenceEqual(new[] { 100, 37, 20 })
        && MerchantStub.Purchases.Select(row => row.Entry).SequenceEqual(expected)
        && MerchantStub.Purchases.Select(row => row.Cost).SequenceEqual(new[] { 100, 37, 20 })
        && RemovalStub.LastGoldCost == 37 && removal.Used && player.Gold == 93 && cart.Entries.Count == 0,
        $"estimated prices={string.Join(',', estimate.Entries.Select(row => row.Cost))}; paid prices={string.Join(',', MerchantStub.Purchases.Select(row => row.Cost))}; gold={player.Gold}");
});
await Check("a removal service never receives the card-purchase LuckyFysh refund", async () =>
{
    var (cart, player) = NewCart(200);
    MerchantStub.Enable();
    var fysh = (LuckyFysh)RuntimeHelpers.GetUninitializedObject(typeof(LuckyFysh));
    AccessTools.Property(typeof(AbstractModel), nameof(AbstractModel.IsMutable))!.SetValue(fysh, true);
    fysh.Owner = player;
    MerchantStub.AddHookListener(fysh);
    var removal = RemovalStub.Create(player, 75);
    Add(cart, removal);
    var estimate = cart.Estimate;
    await cart.CheckoutAsync();
    return (estimate.TotalCost == 75 && estimate.RemainingGold == 125 && estimate.CanAfford
        && player.Gold == 125 && removal.Used && cart.Entries.Count == 0,
        $"estimated total={estimate.TotalCost}; estimated remaining={estimate.RemainingGold}; actual gold={player.Gold}");
});
await Check("removal uses the native in-progress validation before any charge or later purchase", async () =>
{
    var (cart, player) = NewCart(200);
    var removal = RemovalStub.Create(player, 75);
    var later = new TestEntry(player, 40);
    Add(cart, removal, later);
    RemovalStub.RunInProgress = false;
    await cart.CheckoutAsync();
    return (RemovalStub.TypedWrapperCalls == 1 && RemovalStub.SelectionCalls == 0 && player.Gold == 200
        && later.Buys == 0 && !removal.Used && cart.Entries.Count == 2 && !cart.IsCheckingOut,
        $"typed wrapper={RemovalStub.TypedWrapperCalls}; selection calls={RemovalStub.SelectionCalls}; gold={player.Gold}; later buys={later.Buys}");
});

foreach (bool isVote in new[] { false, true })
{
    await Check(isVote ? "a committed map vote prevents checkout from starting" : "an active map travel prevents checkout from starting", async () =>
    {
        var (cart, player) = NewCart(100);
        var item = new TestEntry(player, 30);
        Add(cart, item);
        if (isVote) UiStub.PendingMapVote = true; else UiStub.Traveling = true;
        bool blocked = cart.CheckoutBlockedByTravelOrVote;
        await cart.CheckoutAsync();
        bool preserved = item.Buys == 0 && cart.Contains(item) && player.Gold == 100 && !cart.IsCheckingOut && UiStub.Blocks == 0;
        UiStub.PendingMapVote = UiStub.Traveling = false;
        await cart.CheckoutAsync();
        return (blocked && preserved && item.Buys == 1 && player.Gold == 70,
            $"blocked={blocked}; preservedBeforeTravelEnds={preserved}; buysAfterTravelEnds={item.Buys}");
    });
    await Check(isVote ? "a map vote during an awaited purchase cancels the remaining cart" : "map travel during an awaited purchase cancels the remaining cart", async () =>
    {
        var (cart, player) = NewCart(100);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new TestEntry(player, 30) { BeforeBuyAsync = () => gate.Task };
        var second = new TestEntry(player, 40);
        Add(cart, first, second);
        var checkout = cart.CheckoutAsync();
        if (isVote) UiStub.PendingMapVote = true; else UiStub.Traveling = true;
        gate.SetResult();
        await checkout;
        return (first.Buys == 1 && second.Buys == 0 && cart.Contains(second) && !cart.IsCheckingOut && UiStub.Unblocks == 1,
            $"buys={first.Buys},{second.Buys}; remaining={cart.Entries.Count}; unlocks={UiStub.Unblocks}");
    });
}
await Check("restoring a travel switch preserves an already started journey", () =>
{
    var method = AccessTools.Method(guardType, "CanRestoreTravel");
    bool Restore(bool enabled, bool traveling) => (bool)method.Invoke(null, new object[] { enabled, traveling })!;
    return Task.FromResult((!Restore(true, true) && Restore(true, false) && Restore(false, true),
        "an old enabled state does not overwrite the travel lock; normal restoration remains available"));
});

foreach (var (name, passed, detail) in results)
    Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}: {detail}");
Environment.ExitCode = results.Any(x => !x.Item2) ? 1 : 0;

async Task Check(string name, Func<Task<(bool, string)>> test)
{
    try
    {
        var (passed, detail) = await test();
        results.Add((name, passed, detail));
    }
    catch (Exception ex) { results.Add((name, false, ex.ToString())); }
    finally { MerchantStub.Reset(); RemovalStub.Reset(); }
}

static (CartState, Player) NewCart(int gold)
{
    MerchantStub.Reset();
    RemovalStub.Reset();
    UiStub.Blocks = UiStub.Unblocks = 0;
    UiStub.ThrowOnBlock = false;
    UiStub.UseNativeCapture = false;
    UiStub.Traveling = UiStub.PendingMapVote = false;
    UiStub.ThrowOnDisableIndex = UiStub.ThrowOnRestoreIndex = -1;
    UiStub.ControlStates = new[] { true, false, true };
    UiStub.InvalidNodes.Clear();
    var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
    AccessTools.Field(typeof(Player), "_runState").SetValue(player, NullRunState.Instance);
    player.Gold = gold;
    var rug = (NMerchantInventory)RuntimeHelpers.GetUninitializedObject(typeof(NMerchantInventory));
    AccessTools.Property(typeof(NMerchantInventory), nameof(NMerchantInventory.Inventory))!.SetValue(rug, new MerchantInventory(player));
    return (CartState.For(rug), player);
}

static void Add(CartState cart, params MerchantEntry[] entries)
{
    foreach (var entry in entries)
        cart.Add(entry, (NMerchantCard)RuntimeHelpers.GetUninitializedObject(typeof(NMerchantCard)));
}

static TestSlot NewTestSlot(CartState cart, MerchantEntry entry)
{
    // Only the slot's view of its model is faked; prefixes, async observation,
    // purchase validation, and completion/failure events run from the real DLLs.
    var slot = (TestSlot)RuntimeHelpers.GetUninitializedObject(typeof(TestSlot));
    slot.EntryModel = entry;
    AccessTools.Field(typeof(NMerchantSlot), "_merchantRug").SetValue(slot, cart.Rug);
    return slot;
}

static NMerchantCardRemoval NewRemovalSlot(CartState cart, MerchantCardRemovalEntry entry)
{
    var slot = (NMerchantCardRemoval)RuntimeHelpers.GetUninitializedObject(typeof(NMerchantCardRemoval));
    AccessTools.Field(typeof(NMerchantCardRemoval), "_removalEntry").SetValue(slot, entry);
    AccessTools.Field(typeof(NMerchantSlot), "_merchantRug").SetValue(slot, cart.Rug);
    return slot;
}

public sealed class TestSlot : NMerchantCard
{
    public MerchantEntry EntryModel = null!;
    public override MerchantEntry Entry => EntryModel;
}

public static class UiStub
{
    public static int Blocks;
    public static int Unblocks;
    public static bool ThrowOnBlock;
    public static bool UseNativeCapture;
    public static bool Traveling;
    public static bool PendingMapVote;
    public static bool IsTraveling(ref bool __result) { __result = Traveling; return false; }
    public static bool HasPendingMapVote(ref bool __result) { __result = PendingMapVote; return false; }
    public static int ThrowOnDisableIndex = -1;
    public static int ThrowOnRestoreIndex = -1;
    public static bool[] ControlStates = new[] { true, false, true };
    public static readonly HashSet<object> InvalidNodes = new(ReferenceEqualityComparer.Instance);
    private static readonly FieldInfo InputBlockedField = AccessTools.Field(typeof(NMerchantInventory), "_isInputBlocked");
    public static bool Skip() => false;
    public static bool IsChinese(ref bool __result) { __result = false; return false; }
    public static bool IsUsable(object? node, ref bool __result)
    {
        __result = node != null && !InvalidNodes.Contains(node);
        return false;
    }
    public static bool Capture(NMerchantInventory rug, ref object __result)
    {
        if (UseNativeCapture) return true;
        var guard = typeof(CartState).Assembly.GetType("ShoppingCart.CheckoutInputGuard")!;
        var switchType = guard.GetNestedType("InputSwitch", BindingFlags.NonPublic)!;
        var snapshotType = guard.GetNestedType("InputSnapshot", BindingFlags.NonPublic)!;
        var switches = Array.CreateInstance(switchType, ControlStates.Length);
        for (var i = 0; i < ControlStates.Length; i++)
        {
            var index = i;
            Action<bool> setter = enabled =>
            {
                ControlStates[index] = enabled;
                if ((!enabled && ThrowOnDisableIndex == index) || (enabled && ThrowOnRestoreIndex == index))
                    throw new InvalidOperationException("Simulated partial input control failure");
            };
            switches.SetValue(Activator.CreateInstance(switchType, ControlStates[i], setter), i);
        }
        Action block = rug.BlockInput;
        Action restore = () => { if (!InvalidNodes.Contains(rug)) rug.UnblockInput(); };
        __result = Activator.CreateInstance(snapshotType, block, restore, switches)!;
        return false;
    }
    public static void SetBlocked(NMerchantInventory rug, bool value) => InputBlockedField.SetValue(rug, value);
    public static bool GetBlocked(NMerchantInventory rug) => (bool)InputBlockedField.GetValue(rug)!;
    public static bool Block(NMerchantInventory __instance)
    {
        Blocks++;
        var previouslyBlocked = GetBlocked(__instance);
        SetBlocked(__instance, true);
        if (previouslyBlocked) throw new InvalidOperationException("Simulated duplicate blocking screen");
        if (ThrowOnBlock) throw new InvalidOperationException("Simulated duplicate blocking screen");
        return false;
    }
    public static bool Unblock(NMerchantInventory __instance) { Unblocks++; SetBlocked(__instance, false); return false; }
}

public static class RemovalStub
{
    public static bool Enabled { get; private set; }
    public static bool RunInProgress = true;
    public static bool Succeeds = true;
    public static Func<Task>? WaitForSelection;
    public static int TypedWrapperCalls;
    public static int SelectionCalls;
    public static int LastGoldCost;
    public static bool LastCancelable;
    private static Player? _player;
    private static MerchantCardRemovalEntry? _entry;
    private static OneOffSynchronizer? _previousSynchronizer;

    public static MerchantCardRemovalEntry Create(Player player, int cost)
    {
        Enabled = true;
        _player = player;
        _entry = (MerchantCardRemovalEntry)RuntimeHelpers.GetUninitializedObject(typeof(MerchantCardRemovalEntry));
        AccessTools.Field(typeof(MerchantEntry), "_player").SetValue(_entry, player);
        AccessTools.Field(typeof(MerchantEntry), "_cost").SetValue(_entry, cost);
        _previousSynchronizer = RunManager.Instance.OneOffSynchronizer;
        AccessTools.Property(typeof(RunManager), nameof(RunManager.OneOffSynchronizer))!.SetValue(
            RunManager.Instance, RuntimeHelpers.GetUninitializedObject(typeof(OneOffSynchronizer)));
        return _entry;
    }

    public static void Reset()
    {
        if (Enabled)
            AccessTools.Property(typeof(RunManager), nameof(RunManager.OneOffSynchronizer))!.SetValue(
                RunManager.Instance, _previousSynchronizer);
        Enabled = false;
        RunInProgress = Succeeds = true;
        TypedWrapperCalls = SelectionCalls = LastGoldCost = 0;
        LastCancelable = false;
        WaitForSelection = null;
        _player = null;
        _entry = null;
        _previousSynchronizer = null;
    }

    public static bool IsRunInProgress(ref bool __result)
    {
        if (!Enabled) return true;
        __result = RunInProgress;
        return false;
    }

    public static void ObserveTypedWrapper() { if (Enabled) TypedWrapperCalls++; }

    public static bool SelectAndRemove(int goldCost, bool cancelable, ref Task<bool> __result)
    {
        if (!Enabled) return true;
        SelectionCalls++;
        LastGoldCost = goldCost;
        LastCancelable = cancelable;
        __result = SimulateNativeSelectionAsync(goldCost);
        return false;
    }

    private static async Task<bool> SimulateNativeSelectionAsync(int goldCost)
    {
        // This isolated boundary replaces the Godot card-selector and net message
        // transport. Native typed wrapper, price hooks, affordability check, and
        // success dispatch remain intact. SetUsed mirrors the shop UI notification
        // which needs a real NRun; production must leave that to the game.
        if (WaitForSelection != null) await WaitForSelection();
        if (!Succeeds) return false;
        _player!.Gold -= goldCost;
        _entry!.SetUsed();
        if (MerchantStub.Enabled) MerchantStub.Purchases.Add((_entry, goldCost));
        return true;
    }
}

public sealed class TestEntry : MerchantEntry
{
    private readonly int _baseCost;
    public TestEntry(Player player, int cost) : base(player)
    {
        _baseCost = cost;
        CalcCost();
    }
    public int Buys { get; private set; }
    public bool Stocked { get; set; } = true;
    public bool PurchaseSucceeds { get; init; } = true;
    public Action? BeforeBuy { get; init; }
    public Func<Task>? BeforeBuyAsync { get; init; }
    public override bool IsStocked => Stocked;
    public override void CalcCost() => _cost = _baseCost;
    protected override async Task<(bool, int)> OnTryPurchase(MerchantInventory? inventory, bool ignoreCost)
    {
        BeforeBuy?.Invoke();
        if (BeforeBuyAsync != null) await BeforeBuyAsync();
        if (!PurchaseSucceeds)
        {
            InvokePurchaseFailed(PurchaseStatus.FailureSpace);
            return (false, 0);
        }
        Buys++;
        if (MerchantStub.Enabled) MerchantStub.Purchases.Add((this, ignoreCost ? 0 : Cost));
        if (!ignoreCost) _player.Gold -= Cost;
        return (true, ignoreCost ? 0 : Cost);
    }
    protected override void ClearAfterPurchase() => Stocked = false;
    protected override void RestockAfterPurchase(MerchantInventory? inventory) => Stocked = true;
}

public static class MerchantStub
{
    private sealed record RelicPurchase(Player Player, MembershipCard Model, Action? BeforeBuy);
    private static readonly Dictionary<MerchantRelicEntry, RelicPurchase> Relics = new();
    private static readonly List<AbstractModel> Listeners = new();
    private static MerchantRoom? _room;
    public static bool Enabled { get; private set; }
    public static readonly List<(MerchantEntry Entry, int Cost)> Purchases = new();
    public static void AddHookListener(AbstractModel model) => Listeners.Add(model);

    public static void Enable()
    {
        Enabled = true;
        LocalContext.NetId = 0;
        _room = (MerchantRoom)RuntimeHelpers.GetUninitializedObject(typeof(MerchantRoom));
    }

    public static void Reset()
    {
        Enabled = false;
        LocalContext.NetId = null;
        Relics.Clear();
        Listeners.Clear();
        Purchases.Clear();
        _room = null;
    }

    public static MerchantRelicEntry Membership(Player player, int cost, Action? beforeBuy = null)
    {
        // Avoid constructors which generate inventory, initialize save services,
        // or create Godot assets. The membership's native DynamicVars and price
        // hook run unchanged once the simulated obtain command sets its owner.
        var model = (MembershipCard)RuntimeHelpers.GetUninitializedObject(typeof(MembershipCard));
        AccessTools.Property(typeof(AbstractModel), nameof(AbstractModel.IsMutable))!.SetValue(model, true);
        var entry = (MerchantRelicEntry)RuntimeHelpers.GetUninitializedObject(typeof(MerchantRelicEntry));
        AccessTools.Field(typeof(MerchantEntry), "_player").SetValue(entry, player);
        AccessTools.Field(typeof(MerchantEntry), "_cost").SetValue(entry, cost);
        AccessTools.Property(typeof(MerchantRelicEntry), nameof(MerchantRelicEntry.Model))!.SetValue(entry, model);
        Relics.Add(entry, new RelicPurchase(player, model, beforeBuy));
        return entry;
    }

    public static bool CurrentRoom(ref AbstractRoom? __result)
    {
        if (!Enabled) return true;
        __result = _room;
        return false;
    }

    public static bool HookListeners(ref IEnumerable<AbstractModel> __result)
    {
        if (!Enabled) return true;
        __result = Listeners.ToArray();
        return false;
    }

    public static bool PurchaseRelic(MerchantRelicEntry __instance, bool ignoreCost, ref Task<(bool, int)> __result)
    {
        if (!Enabled || !Relics.TryGetValue(__instance, out var record)) return true;
        record.BeforeBuy?.Invoke();
        int cost = ignoreCost ? 0 : __instance.Cost;
        record.Player.Gold -= cost;
        record.Model.Owner = record.Player;
        Listeners.Add(record.Model);
        Purchases.Add((__instance, cost));
        __result = Task.FromResult((true, cost));
        return false;
    }
}
