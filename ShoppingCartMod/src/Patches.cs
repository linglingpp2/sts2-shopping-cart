using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace ShoppingCart;

/// <summary>
/// 把商店槽位的点击购买改为“加入/移出购物车”。
/// 卡牌、遗物、药水及删牌服务共用加入/移出逻辑。
/// </summary>
[HarmonyPatch]
public static class SlotClickPatches
{
    private static readonly FieldInfo RugField = AccessTools.Field(typeof(NMerchantSlot), "_merchantRug");

    private static bool HandleClick(NMerchantSlot slot, ref Task __result)
    {
        var rug = (NMerchantInventory?)RugField.GetValue(slot);
        if (rug == null)
        {
            return true;
        }
        var state = CartState.For(rug);
        // A busy checkout must suppress the original purchase in both click modes.
        if (state.IsCheckingOut)
        {
            __result = Task.CompletedTask;
            return false;
        }
        if (!CartState.CartModeEnabled)
        {
            // PurchaseCompleted removes queued entries only after a successful purchase.
            return true;
        }
        var entry = slot.Entry;
        if (entry == null)
        {
            return true;
        }
        if (!entry.IsStocked)
        {
            __result = Task.CompletedTask;
            return false;
        }

        if (state.Contains(entry))
        {
            state.Remove(entry);
        }
        else
        {
            state.Add(entry, slot);
        }

        // 跳过原版购买；外层 OnSelected 会继续刷新悬停提示
        __result = Task.CompletedTask;
        return false;
    }

    [HarmonyPatch(typeof(NMerchantCard), "OnTryPurchase")]
    [HarmonyPrefix]
    public static bool CardPrefix(NMerchantCard __instance, ref Task __result) => HandleClick(__instance, ref __result);

    [HarmonyPatch(typeof(NMerchantRelic), "OnTryPurchase")]
    [HarmonyPrefix]
    public static bool RelicPrefix(NMerchantRelic __instance, ref Task __result) => HandleClick(__instance, ref __result);

    [HarmonyPatch(typeof(NMerchantPotion), "OnTryPurchase")]
    [HarmonyPrefix]
    public static bool PotionPrefix(NMerchantPotion __instance, ref Task __result) => HandleClick(__instance, ref __result);

    [HarmonyPatch(typeof(NMerchantCardRemoval), "OnTryPurchase")]
    [HarmonyPrefix]
    public static bool RemovalPrefix(NMerchantCardRemoval __instance, ref Task __result) => HandleClick(__instance, ref __result);
}

/// <summary>Also prevent a cart checkout from overlapping an in-progress direct purchase or removal.</summary>
[HarmonyPatch(typeof(NMerchantSlot), "OnSelected")]
public static class PendingSelectionPatch
{
    private static readonly FieldInfo RugField = AccessTools.Field(typeof(NMerchantSlot), "_merchantRug");

    [HarmonyPostfix]
    public static void Postfix(NMerchantSlot __instance, ref Task __result)
    {
        var rug = (NMerchantInventory?)RugField.GetValue(__instance);
        if (rug != null && __result != null && !__result.IsCompleted)
            __result = CartState.For(rug).ObserveDirectPurchaseAsync(__result);
    }
}

/// <summary>商店清单打开时挂上/显示购物车面板，关闭时清空购物车。</summary>
[HarmonyPatch]
public static class InventoryLifecyclePatches
{
    [HarmonyPatch(typeof(NMerchantInventory), "Open")]
    [HarmonyPostfix]
    public static void OpenPostfix(NMerchantInventory __instance)
    {
        CartOverlay.For(__instance).Open();
    }

    [HarmonyPatch(typeof(NMerchantInventory), "Close")]
    [HarmonyPostfix]
    public static void ClosePostfix(NMerchantInventory __instance)
    {
        CartOverlay.CloseFor(__instance);
    }
}

/// <summary>Original purchases rebuild slot navigation; reconnect the cart afterward.</summary>
[HarmonyPatch]
public static class CartNavigationPatches
{
    [HarmonyPatch(typeof(NMerchantInventory), "UpdateNavigation")]
    [HarmonyPostfix]
    public static void NormalPostfix(NMerchantInventory __instance) => CartOverlay.UpdateNavigationFor(__instance);

    [HarmonyPatch(typeof(NFakeMerchantInventory), "UpdateNavigation")]
    [HarmonyPostfix]
    public static void FakePostfix(NFakeMerchantInventory __instance) => CartOverlay.UpdateNavigationFor(__instance);
}
