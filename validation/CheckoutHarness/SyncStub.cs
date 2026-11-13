#if MERCHANT_SYNC_V2
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync;
using MegaCrit.Sts2.Core.Runs;

// The checkout regression suite replaces the transport boundary. Dedicated
// synchronizer integration cases can allow the original method to execute.
public static class SyncStub
{
    public static MerchantInventory? Inventory;
    public static int Calls;
    public static bool UseOriginal;
    public static MerchantSynchronizer? Original;
    public static Player? Player;
    public static List<uint> Messages = new();
    private static readonly MerchantSynchronizer Instance =
        (MerchantSynchronizer)RuntimeHelpers.GetUninitializedObject(typeof(MerchantSynchronizer));

    public static bool GetSynchronizer(ref MerchantSynchronizer __result)
    {
        __result = UseOriginal ? Original! : Instance;
        return false;
    }

    public static bool Purchase(MerchantEntry entry, ref Task<bool> __result)
    {
        Calls++;
        if (UseOriginal) return true;
        __result = entry.OnTryPurchaseUnsynchronized(Inventory);
        return false;
    }

    public static void PatchBoundaries(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(NullRunState), nameof(NullRunState.GetPlayerSlotIndex)),
            prefix: new HarmonyMethod(typeof(SyncStub), nameof(GetSlot)));
        harmony.Patch(AccessTools.Method(typeof(NullRunState), nameof(NullRunState.GetPlayer)),
            prefix: new HarmonyMethod(typeof(SyncStub), nameof(GetPlayer)));
        var send = typeof(NetSingleplayerGameService).GetMethods()
            .Single(method => method.Name == "SendMessage" && method.GetParameters().Length == 1)
            .MakeGenericMethod(typeof(MerchantEntryPurchasedMessage));
        harmony.Patch(send, prefix: new HarmonyMethod(typeof(SyncStub), nameof(CaptureMessage)));
    }

    public static void UseNative(Player player, MerchantInventory inventory, params MerchantEntry[] entries)
    {
        Player = player;
        LocalContext.NetId = player.NetId;
        UseOriginal = true;
        inventory.testEntries.AddRange(entries);
        inventory.Setup();
        var transport = (NetSingleplayerGameService)RuntimeHelpers.GetUninitializedObject(typeof(NetSingleplayerGameService));
        // The buffer constructor creates a Godot-backed logger. Only its current
        // location is needed here; the real purchase method and inventory lock run.
        var buffer = RuntimeHelpers.GetUninitializedObject(typeof(RunLocationTargetedMessageBuffer));
        Original = (MerchantSynchronizer)RuntimeHelpers.GetUninitializedObject(typeof(MerchantSynchronizer));
        AccessTools.Field(typeof(MerchantSynchronizer), "_runState").SetValue(Original, NullRunState.Instance);
        AccessTools.Field(typeof(MerchantSynchronizer), "_netService").SetValue(Original, transport);
        AccessTools.Field(typeof(MerchantSynchronizer), "_locationBuffer").SetValue(Original, buffer);
        AccessTools.Field(typeof(MerchantSynchronizer), "_inventories").SetValue(Original, new List<MerchantInventory> { inventory });
    }

    public static bool GetSlot(ref int __result)
    {
        if (!UseOriginal) return true;
        __result = 0;
        return false;
    }

    public static bool GetPlayer(ref Player? __result)
    {
        if (!UseOriginal) return true;
        __result = Player;
        return false;
    }

    public static void CaptureMessage(MerchantEntryPurchasedMessage message) => Messages.Add(message.id);

    public static void Reset()
    {
        UseOriginal = false;
        Original = null;
        Player = null;
        Messages.Clear();
    }
}
#endif
