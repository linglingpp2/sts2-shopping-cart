using System;
using System.Reflection;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Runs;

namespace ShoppingCart;

/// <summary>Use each game version's synchronized purchase entry point.</summary>
public static class MerchantPurchase
{
    private static readonly PropertyInfo? SynchronizerProperty = typeof(RunManager).GetProperty("MerchantSynchronizer");
    private static readonly MethodInfo? SynchronizedPurchase = SynchronizerProperty?.PropertyType.GetMethod(
        "PurchaseLocalEntry", new[] { typeof(MerchantEntry) });
    private static readonly MethodInfo? LegacyPurchase = typeof(MerchantEntry).GetMethod(
        "OnTryPurchaseWrapper", new[] { typeof(MerchantInventory), typeof(bool) });
    private static readonly MethodInfo? LegacyRemoval = typeof(MerchantCardRemovalEntry).GetMethod(
        "OnTryPurchaseWrapper", new[] { typeof(MerchantInventory), typeof(bool), typeof(bool) });

    public static bool UsesMerchantSynchronizer => SynchronizedPurchase != null;

    public static Task<bool> PurchaseAsync(MerchantEntry entry, MerchantInventory? inventory)
    {
        try
        {
            if (SynchronizedPurchase != null)
            {
                // Never fall back to the unsynchronized entry API: that skips network
                // messages and the game's inventory purchase lock, even in a solo run.
                var synchronizer = SynchronizerProperty!.GetValue(RunManager.Instance)
                    ?? throw new InvalidOperationException("The merchant synchronizer is unavailable.");
                return Invoke(SynchronizedPurchase, synchronizer, new object?[] { entry });
            }
            if (entry is MerchantCardRemovalEntry && LegacyRemoval != null)
                return Invoke(LegacyRemoval, entry, new object?[] { inventory, false, true });
            if (LegacyPurchase != null)
                return Invoke(LegacyPurchase, entry, new object?[] { inventory, false });
            throw new NotSupportedException("This game version has no supported synchronized merchant purchase API.");
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            return Task.FromException<bool>(exception.InnerException);
        }
        catch (Exception exception)
        {
            return Task.FromException<bool>(exception);
        }
    }

    private static Task<bool> Invoke(MethodInfo method, object target, object?[] arguments) =>
        method.Invoke(target, arguments) as Task<bool>
        ?? throw new InvalidOperationException($"Unexpected return type from {method.Name}.");
}
