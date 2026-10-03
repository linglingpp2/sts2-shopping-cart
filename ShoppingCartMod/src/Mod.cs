using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace ShoppingCart;

[ModInitializer("Load")]
public static class Mod
{
    public const string Id = "ShoppingCart";

    public static void Load()
    {
        var cfg = new Godot.ConfigFile();
        if (cfg.Load("user://shopping_cart.cfg") == Godot.Error.Ok)
        {
            CartState.CartModeEnabled = cfg.GetValue("prefs", "cart_mode", true).AsBool();
        }

        var harmony = new Harmony(Id);
        harmony.PatchAll(Assembly.GetExecutingAssembly());
        Log.Info($"[{Id}] loaded, patched methods: {harmony.GetPatchedMethods().Count()}");
    }
}
