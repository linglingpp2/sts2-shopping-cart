using System;
using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.Fonts;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace ShoppingCart;

/// <summary>
/// Native canvas controls keep the cart independent of shop transforms and clipping.
/// Signals and explicit initialization work without Godot's script source generator.
/// </summary>
public sealed class CartOverlay
{
    private static readonly ConditionalWeakTable<NMerchantInventory, CartOverlay> Overlays = new();

    public static CartOverlay For(NMerchantInventory inventory) =>
        Overlays.GetValue(inventory, key => new CartOverlay(CartState.For(key)));

    public static void CloseFor(NMerchantInventory inventory)
    {
        if (Overlays.TryGetValue(inventory, out var overlay))
        {
            overlay.Close();
        }
        else
        {
            CartState.For(inventory).Clear();
        }
    }

    public static void UpdateNavigationFor(NMerchantInventory inventory)
    {
        if (Overlays.TryGetValue(inventory, out var overlay) && !overlay._disposed)
        {
            overlay._panel.UpdateNavigation();
        }
    }

    private readonly CartState _state;
    private readonly CanvasLayer _layer;
    private readonly Control _host;
    private readonly CartPanel _panel;
    private readonly Button _toggle;
    private Viewport? _viewport;
    private bool _disposed;
    private bool _open;
    private bool _selectPressed;

    private CartOverlay(CartState state)
    {
        _state = state;
        _layer = new CanvasLayer { Name = "ShoppingCartOverlay", Layer = 5, Visible = false };
        _host = new Control { Name = "ShoppingCartHost", MouseFilter = Control.MouseFilterEnum.Ignore };
        _layer.AddChild(_host);

        _panel = new CartPanel { Name = "ShoppingCartPanel" };
        _panel.Initialize(state);
        state.Panel = _panel;
        _host.AddChild(_panel);

        _toggle = new Button
        {
            Name = "ShoppingCartToggle",
            Text = Loc.Title,
            Icon = CartIcon.Texture,
            ExpandIcon = false,
            CustomMinimumSize = new Vector2(134, 42),
            TooltipText = Loc.OpenCartHint,
        };
        _toggle.AddThemeFontSizeOverride("font_size", 18);
        _host.AddChild(_toggle);
        _panel.SetToggleButton(_toggle);
        ApplyToggleFont();
        _toggle.Pressed += TogglePanel;
        _toggle.FocusExited += () => _selectPressed = false;
        _toggle.GuiInput += OnToggleInput;
        _panel.Refreshed += RefreshToggle;
        _host.Ready += OnReady;
        state.Rug.TreeExiting += Dispose;
        state.Rug.AddChild(_layer);
        ActiveScreenContext.Instance.Updated += UpdateVisibility;
        LocManager.Instance?.SubscribeToLocaleChange(OnLocaleChanged);
        Log.Info($"[{Mod.Id}] cart UI built explicitly; minimumSize={_panel.GetCombinedMinimumSize()}");
    }

    private void OnReady()
    {
        _viewport = _host.GetViewport();
        _viewport.SizeChanged += OnViewportChanged;
        OnViewportChanged();
    }

    public void Open()
    {
        if (_disposed) return;
        _open = true;
        _panel.SetPanelVisible(true);
        _panel.EnsurePlaced();
        _panel.Refresh();
        UpdateVisibility();
        Callable.From(() =>
        {
            if (_disposed || !GodotObject.IsInstanceValid(_panel) || !_panel.IsInsideTree()) return;
            _panel.EnsurePlaced();
            Log.Info($"[{Mod.Id}] cart shown; pos={_panel.GlobalPosition}, size={_panel.Size}, viewport={_panel.GetViewportRect().Size}");
        }).CallDeferred();
    }

    private void Close()
    {
        _open = false;
        _selectPressed = false;
        _layer.Visible = false;
        _panel.SetPanelVisible(false);
        _state.Clear();
    }

    private void UpdateVisibility()
    {
        if (_disposed) return;
        bool wasVisible = _layer.Visible;
        // CanvasLayer draws above the room; hide it whenever a real game screen covers the shop.
        _layer.Visible = _open && GodotObject.IsInstanceValid(_state.Rug) &&
            _state.Rug.IsInsideTree() && _state.Rug.IsOpen && ActiveScreenContext.Instance.IsCurrent(_state.Rug);
        if (_layer.Visible && !wasVisible) _panel.Refresh();
        _panel.UpdateNavigation();
    }

    private void OnViewportChanged()
    {
        if (_disposed || _viewport == null) return;
        _host.Size = _viewport.GetVisibleRect().Size;
        _toggle.Position = new Vector2(Mathf.Max(8, _host.Size.X - _toggle.GetCombinedMinimumSize().X - 24), 82);
        _panel.EnsurePlaced();
    }

    private void RefreshToggle()
    {
        if (_disposed) return;
        _toggle.Text = _state.Entries.Count == 0 ? Loc.Title : $"{Loc.Title} ({_state.Entries.Count})";
        _toggle.Disabled = _state.IsCheckingOut;
        OnViewportChanged();
    }

    private void ApplyToggleFont()
    {
        var language = LocManager.Instance?.Language;
        var font = language == null ? null : FontManager.GetSubstituteFont(language, FontType.Regular);
        if (font != null) _toggle.AddThemeFontOverride("font", font);
        else _toggle.RemoveThemeFontOverride("font");
    }

    private void OnLocaleChanged()
    {
        if (_disposed) return;
        ApplyToggleFont();
        _toggle.TooltipText = Loc.OpenCartHint;
        RefreshToggle();
    }

    private void TogglePanel()
    {
        if (_disposed || _state.IsCheckingOut) return;
        _panel.SetPanelVisible(!_panel.Visible);
        if (_panel.Visible) _panel.EnsurePlaced();
    }

    private void OnToggleInput(InputEvent input)
    {
        if (_disposed || _toggle.Disabled) return;
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
        {
            _panel.ResetPosition();
            _panel.SetPanelVisible(true);
            _toggle.AcceptEvent();
        }
        else if (input.IsActionPressed(MegaInput.select) && !input.IsEcho())
        {
            _selectPressed = true;
            _toggle.AcceptEvent();
        }
        else if (input.IsActionReleased(MegaInput.select))
        {
            if (_selectPressed) TogglePanel();
            _selectPressed = false;
            _toggle.AcceptEvent();
        }
    }

    private void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ActiveScreenContext.Instance.Updated -= UpdateVisibility;
        LocManager.Instance?.UnsubscribeToLocaleChange(OnLocaleChanged);
        _panel.Refreshed -= RefreshToggle;
        if (_viewport != null && GodotObject.IsInstanceValid(_viewport))
            _viewport.SizeChanged -= OnViewportChanged;
        _state.Clear();
        _state.Panel = null;
        Overlays.Remove(_state.Rug);
    }
}
