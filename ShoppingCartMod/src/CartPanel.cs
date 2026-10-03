using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.Fonts;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace ShoppingCart;

/// <summary>Built explicitly; the mod assembly has no Godot-generated callback dispatch.</summary>
public class CartPanel : PanelContainer
{
    private const string CfgPath = "user://shopping_cart.cfg";
    private const float PanelWidth = 360;
    private const float Margin = 12;
    private CartState _state = null!;
    private Player? _player;
    private Viewport? _viewport;
    private VBoxContainer _rows = null!;
    private ScrollContainer _scroll = null!;
    private HBoxContainer _header = null!;
    private Label _title = null!, _dragHint = null!, _emptyHint = null!, _totalLabel = null!, _goldLabel = null!, _estimateHint = null!;
    private Button _checkoutButton = null!, _clearButton = null!, _modeButton = null!;
    private Button? _toggleButton;
    private readonly Dictionary<MerchantEntry, Button> _removeButtons = new();
    private readonly List<Control> _fontControls = new();
    private readonly Dictionary<NMerchantSlot, (NodePath Right, NodePath Bottom, NodePath Target)> _navOverrides = new();
    private bool _initialized, _subscribed, _placed, _dragging, _layoutQueued;
    private Vector2 _dragOffset;

    public event Action? Refreshed;

    public void Initialize(CartState state)
    {
        if (_initialized) return;
        _initialized = true;
        _state = state;
        _player = state.Rug.Inventory?.Player;
        BuildUi();
        // Native signals work without a Godot SDK/source generator.
        Ready += OnNativeReady;
        TreeEntered += Subscribe;
        TreeExiting += OnTreeExiting;
        Resized += QueueLayout;
        MinimumSizeChanged += QueueLayout;
        VisibilityChanged += OnVisibilityChanged;
        Subscribe();
        Refresh();
    }

    private void BuildUi()
    {
        Name = "ShoppingCartPanel";
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(PanelWidth, 0);
        Size = new Vector2(PanelWidth, 220);
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.07f, 0.09f, 0.97f),
            BorderColor = new Color(0.16f, 0.92f, 0.75f, 0.9f),
            BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1,
            CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10,
            ContentMarginLeft = 12, ContentMarginRight = 12,
            ContentMarginTop = 10, ContentMarginBottom = 12,
        });
        var root = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);

        _header = new HBoxContainer { MouseFilter = MouseFilterEnum.Stop };
        _header.GuiInput += OnHeaderGuiInput;
        root.AddChild(_header);
        _header.AddChild(new TextureRect
        {
            Texture = CartIcon.Texture, CustomMinimumSize = new Vector2(26, 26),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        _title = MakeLabel(Loc.Title, 20, StsColors.aqua);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _header.AddChild(_title);
        _dragHint = MakeLabel(Loc.DragHint, 12, StsColors.halfTransparentCream);
        _header.AddChild(_dragHint);

        _scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 34),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        root.AddChild(_scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 3);
        _scroll.AddChild(_rows);
        _emptyHint = MakeLabel(Loc.EmptyHint, 14, StsColors.halfTransparentCream);
        _emptyHint.MouseFilter = MouseFilterEnum.Pass;
        _emptyHint.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _rows.AddChild(_emptyHint);

        root.AddChild(MakeSeparator());
        _totalLabel = MakeLabel(string.Empty, 17, StsColors.gold);
        _goldLabel = MakeLabel(string.Empty, 14, StsColors.cream);
        _goldLabel.MouseFilter = MouseFilterEnum.Pass;
        root.AddChild(_totalLabel);
        root.AddChild(_goldLabel);
        _estimateHint = MakeLabel(Loc.EstimateHint, 12, StsColors.halfTransparentCream);
        _estimateHint.MouseFilter = MouseFilterEnum.Pass;
        _estimateHint.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        root.AddChild(_estimateHint);
        root.AddChild(MakeSeparator());

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 6);
        root.AddChild(buttons);
        _checkoutButton = MakeButton(Loc.Checkout, OnCheckoutPressed);
        _checkoutButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        buttons.AddChild(_checkoutButton);
        _clearButton = MakeButton(Loc.Clear, OnClearPressed);
        buttons.AddChild(_clearButton);
        _modeButton = MakeButton(ModeText(), OnModePressed);
        _modeButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        root.AddChild(_modeButton);
    }

    private void Subscribe()
    {
        if (!_subscribed)
        {
            _subscribed = true;
            if (_player != null) _player.GoldChanged += Refresh;
            LocManager.Instance?.SubscribeToLocaleChange(OnLocaleChanged);
        }
        if (IsInsideTree() && _viewport == null)
        {
            _viewport = GetViewport();
            _viewport.SizeChanged += QueueLayout;
        }
    }

    private void OnNativeReady()
    {
        Subscribe();
        EnsurePlaced();
        QueueLayout();
    }

    private void OnTreeExiting()
    {
        _dragging = false;
        RestoreNavigation();
        SaveCfg();
        if (_subscribed)
        {
            if (_player != null) _player.GoldChanged -= Refresh;
            LocManager.Instance?.UnsubscribeToLocaleChange(OnLocaleChanged);
            _subscribed = false;
        }
        if (_viewport != null && GodotObject.IsInstanceValid(_viewport))
            _viewport.SizeChanged -= QueueLayout;
        _viewport = null;
    }

    private Label MakeLabel(string text, int size, Color color)
    {
        var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        RegisterFont(label);
        return label;
    }

    private static Control MakeSeparator() => new ColorRect
    {
        Color = new Color(1, 1, 1, 0.12f), CustomMinimumSize = new Vector2(0, 1),
        MouseFilter = MouseFilterEnum.Ignore,
    };

    private Button MakeButton(string text, Action pressed)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.All };
        StyleButton(button);
        RegisterFont(button);
        button.Pressed += pressed;
        bool selectPressed = false;
        button.FocusExited += () => selectPressed = false;
        button.GuiInput += input =>
        {
            if (!input.IsActionPressed(MegaInput.select) && !input.IsActionReleased(MegaInput.select)) return;
            button.AcceptEvent();
            if (input.IsActionPressed(MegaInput.select) && !input.IsEcho()) selectPressed = !button.Disabled;
            if (input.IsActionReleased(MegaInput.select))
            {
                bool activate = selectPressed && !button.Disabled;
                selectPressed = false;
                if (activate) pressed();
            }
        };
        return button;
    }

    private void RegisterFont(Control control)
    {
        ApplyLocaleFont(control);
        _fontControls.Add(control);
    }

    public static void ApplyLocaleFont(Control control)
    {
        var language = LocManager.Instance?.Language;
        var font = language == null ? null : FontManager.GetSubstituteFont(language, FontType.Regular);
        if (font != null) control.AddThemeFontOverride("font", font);
        else control.RemoveThemeFontOverride("font");
    }

    private static void StyleButton(Button button)
    {
        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.18f, 0.2f, 0.95f),
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            ContentMarginLeft = 10, ContentMarginRight = 10,
            ContentMarginTop = 5, ContentMarginBottom = 5,
        };
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = new Color(0.18f, 0.28f, 0.3f, 0.95f);
        var pressed = (StyleBoxFlat)normal.Duplicate();
        pressed.BgColor = new Color(0.08f, 0.12f, 0.14f, 0.95f);
        var disabled = (StyleBoxFlat)normal.Duplicate();
        disabled.BgColor = new Color(0.1f, 0.12f, 0.13f, 0.6f);
        var focus = (StyleBoxFlat)normal.Duplicate();
        focus.BgColor = Colors.Transparent;
        focus.BorderColor = StsColors.aqua;
        focus.BorderWidthLeft = focus.BorderWidthRight = focus.BorderWidthTop = focus.BorderWidthBottom = 2;
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", pressed);
        button.AddThemeStyleboxOverride("disabled", disabled);
        button.AddThemeStyleboxOverride("focus", focus);
        button.AddThemeColorOverride("font_color", StsColors.cream);
        button.AddThemeColorOverride("font_hover_color", StsColors.aqua);
        button.AddThemeColorOverride("font_pressed_color", StsColors.cream);
        button.AddThemeColorOverride("font_disabled_color", new Color(0.6f, 0.6f, 0.6f, 0.5f));
        button.AddThemeFontSizeOverride("font_size", 16);
    }

    private void OnLocaleChanged()
    {
        _title.Text = Loc.Title;
        _dragHint.Text = Loc.DragHint;
        _emptyHint.Text = Loc.EmptyHint;
        _clearButton.Text = Loc.Clear;
        foreach (var control in _fontControls.Where(GodotObject.IsInstanceValid)) ApplyLocaleFont(control);
        Refresh();
    }

    private static string ModeText() => CartState.CartModeEnabled ? Loc.ModeCart : Loc.ModeDirect;

    public void Refresh()
    {
        if (!_initialized || !GodotObject.IsInstanceValid(this)) return;
        var focus = IsInsideTree() ? GetViewport().GuiGetFocusOwner() : null;
        var focusedEntry = _removeButtons.FirstOrDefault(pair => pair.Value == focus).Key;
        _removeButtons.Clear();
        foreach (var row in _rows.GetChildren().Where(c => c != _emptyHint).ToList())
        {
            _rows.RemoveChild(row);
            row.QueueFree();
        }
        _fontControls.RemoveAll(c => !GodotObject.IsInstanceValid(c) || c.IsQueuedForDeletion() ||
            (c.GetParent() != null && c.GetParent().IsQueuedForDeletion()));

        var estimate = _state.Estimate;
        bool empty = _state.Entries.Count == 0;
        _emptyHint.Visible = empty;
        _emptyHint.Text = CartState.CartModeEnabled ? Loc.EmptyHint : Loc.DirectHint;
        _emptyHint.TooltipText = _emptyHint.Text;
        foreach (var item in estimate.Entries)
        {
            var entry = item.Entry;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var name = MakeLabel(_state.DisplayNameFor(entry), 15, StsColors.cream);
            name.MouseFilter = MouseFilterEnum.Pass;
            name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.TooltipText = entry is MerchantCardRemovalEntry ? Loc.CardRemovalHint : name.Text;
            row.AddChild(name);
            var cost = MakeLabel(item.Cost.ToString(), 15, item.CanAfford ? StsColors.gold : StsColors.red);
            cost.MouseFilter = MouseFilterEnum.Pass;
            cost.TooltipText = Loc.EstimatedPrice;
            row.AddChild(cost);
            var remove = MakeButton("×", () => _state.Remove(entry));
            remove.TooltipText = Loc.Remove;
            remove.Disabled = _state.IsCheckingOut;
            remove.CustomMinimumSize = new Vector2(30, 28);
            row.AddChild(remove);
            _removeButtons[entry] = remove;
            _rows.AddChild(row);
        }
        _totalLabel.Text = $"{Loc.EstimatedTotal}: {estimate.TotalCost}";
        _totalLabel.AddThemeColorOverride("font_color", estimate.CanAfford ? StsColors.gold : StsColors.red);
        _goldLabel.Text = $"{Loc.Gold}: {_player?.Gold ?? 0} → {Loc.EstimatedRemaining}: {estimate.RemainingGold}";
        _goldLabel.AddThemeColorOverride("font_color", estimate.CanAfford ? StsColors.cream : StsColors.red);
        _goldLabel.TooltipText = estimate.CanAfford ? string.Empty : Loc.AffordWarning;
        _estimateHint.Text = estimate.MembershipCardFirst
            ? (estimate.HasUncertainty ? Loc.MembershipCardUncertainHint : Loc.MembershipCardHint)
            : (estimate.HasUncertainty ? Loc.UncertainEstimateHint : Loc.EstimateHint);
        _estimateHint.TooltipText = _estimateHint.Text;
        bool blockedByTravel = _state.CheckoutBlockedByTravelOrVote;
        _checkoutButton.Disabled = empty || _state.IsCheckingOut || blockedByTravel;
        _checkoutButton.TooltipText = blockedByTravel ? Loc.BlockedByTravelHint : string.Empty;
        _checkoutButton.Text = _state.IsCheckingOut ? Loc.CheckingOut : Loc.Checkout;
        _clearButton.Disabled = empty || _state.IsCheckingOut;
        _modeButton.Disabled = _state.IsCheckingOut;
        _modeButton.Text = ModeText();
        QueueLayout();
        UpdateNavigation();
        if (focusedEntry != null && IsVisibleInTree())
        {
            var next = _removeButtons.GetValueOrDefault(focusedEntry) ?? (!_checkoutButton.Disabled ? _checkoutButton : _modeButton);
            if (!next.Disabled) next.GrabFocus();
        }
        Refreshed?.Invoke();
    }

    private void OnCheckoutPressed() => _ = RunCheckoutAsync();

    private async System.Threading.Tasks.Task RunCheckoutAsync()
    {
        try { await _state.CheckoutAsync(); }
        catch (Exception e) { GD.PushError($"[ShoppingCart] checkout error: {e}"); }
    }

    private void OnClearPressed()
    {
        if (!_state.IsCheckingOut) _state.Clear();
    }

    private void OnModePressed()
    {
        if (_state.IsCheckingOut) return;
        CartState.CartModeEnabled = !CartState.CartModeEnabled;
        Refresh();
        SaveCfg();
    }

    private void OnHeaderGuiInput(InputEvent input)
    {
        if (_state.IsCheckingOut) { _dragging = false; return; }
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse)
        {
            _dragging = mouse.Pressed;
            if (_dragging) _dragOffset = GetGlobalMousePosition() - GlobalPosition;
            else SaveCfg();
            _header.AcceptEvent();
        }
        else if (_dragging && input is InputEventMouseMotion)
        {
            GlobalPosition = ClampIntoViewport(GetGlobalMousePosition() - _dragOffset);
            _placed = true;
            _header.AcceptEvent();
        }
    }

    public void SetPanelVisible(bool visible)
    {
        Visible = visible;
        if (visible) { Refresh(); EnsurePlaced(); }
    }

    public void SetToggleButton(Button toggle)
    {
        _toggleButton = toggle;
        RegisterFont(toggle);
        UpdateNavigation();
    }

    private void OnVisibilityChanged()
    {
        if (!Visible) _dragging = false;
        UpdateNavigation();
        QueueLayout();
    }

    public void EnsurePlaced()
    {
        if (!IsInsideTree()) return;
        if (!_placed)
        {
            _placed = true;
            var cfg = new ConfigFile();
            var viewport = GetViewport().GetVisibleRect().Size;
            var position = new Vector2(viewport.X - PanelWidth - 24, 138);
            if (cfg.Load(CfgPath) == Error.Ok)
                position = cfg.GetValue("display", "pos", position).AsVector2();
            GlobalPosition = position;
        }
        GlobalPosition = ClampIntoViewport(GlobalPosition);
        QueueLayout();
    }

    public void ResetPosition()
    {
        if (!IsInsideTree()) return;
        var viewport = GetViewport().GetVisibleRect().Size;
        GlobalPosition = ClampIntoViewport(new Vector2(viewport.X - PanelWidth - 24, 138));
        _placed = true;
        SaveCfg();
    }

    private Vector2 ClampIntoViewport(Vector2 position)
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        var actualSize = Size.Max(GetCombinedMinimumSize());
        float maxX = Math.Max(Margin, viewport.X - actualSize.X - Margin);
        float maxY = Math.Max(Margin, viewport.Y - actualSize.Y - Margin);
        return new Vector2(Mathf.Clamp(position.X, Margin, maxX), Mathf.Clamp(position.Y, Margin, maxY));
    }

    private void QueueLayout()
    {
        if (_layoutQueued || !IsInsideTree()) return;
        _layoutQueued = true;
        Callable.From(UpdateLayout).CallDeferred();
    }

    private void UpdateLayout()
    {
        _layoutQueued = false;
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return;
        // Measure the native font/button minima instead of assuming a fixed row height.
        var children = _rows.GetChildren().OfType<Control>().Where(c => c.Visible).ToList();
        float rowHeight = children.Sum(c => c.GetCombinedMinimumSize().Y);
        rowHeight += Math.Max(0, children.Count - 1) * _rows.GetThemeConstant("separation");
        _scroll.CustomMinimumSize = new Vector2(0, Math.Min(rowHeight, 264));
        UpdateMinimumSize();
        ResetSize();
        GlobalPosition = ClampIntoViewport(GlobalPosition);
        UpdateNavigation();
    }

    public void UpdateNavigation()
    {
        RestoreNavigation();
        if (!_initialized || !IsInsideTree() || !GodotObject.IsInstanceValid(_state.Rug) ||
            !_state.Rug.IsOpen || !ActiveScreenContext.Instance.IsCurrent(_state.Rug) || _state.IsCheckingOut) return;
        var slots = _state.Rug.GetAllSlots().Where(s => GodotObject.IsInstanceValid(s) &&
            s.Visible && s.Entry?.IsStocked == true).ToList();
        var buttons = Visible ? _removeButtons.Values.Concat(new[] { _checkoutButton, _clearButton, _modeButton })
            .Where(b => !b.Disabled).ToList() : new List<Button>();
        if (_toggleButton != null && GodotObject.IsInstanceValid(_toggleButton) && !_toggleButton.Disabled)
            buttons.Add(_toggleButton);
        if (buttons.Count == 0) return;
        var target = (Visible ? (!_checkoutButton.Disabled ? _checkoutButton : _modeButton) : buttons[0]).GetPath();
        foreach (var slot in slots)
        {
            var own = slot.GetPath();
            var original = (slot.FocusNeighborRight, slot.FocusNeighborBottom, target);
            bool changed = false;
            if (slot.FocusNeighborRight == own) { slot.FocusNeighborRight = target; changed = true; }
            if (slot.FocusNeighborBottom == own) { slot.FocusNeighborBottom = target; changed = true; }
            if (changed) _navOverrides[slot] = original;
        }
        for (int i = 0; i < buttons.Count; i++)
        {
            var button = buttons[i];
            button.FocusNeighborTop = buttons[(i + buttons.Count - 1) % buttons.Count].GetPath();
            button.FocusNeighborBottom = buttons[(i + 1) % buttons.Count].GetPath();
            button.FocusNeighborRight = buttons[(i + 1) % buttons.Count].GetPath();
            button.FocusNeighborLeft = slots.Count > 0 ?
                slots.MinBy(s => s.GlobalPosition.DistanceSquaredTo(button.GlobalPosition))!.GetPath() : button.GetPath();
        }
    }

    private void RestoreNavigation()
    {
        foreach (var pair in _navOverrides)
        {
            if (!GodotObject.IsInstanceValid(pair.Key)) continue;
            if (pair.Key.FocusNeighborRight == pair.Value.Target) pair.Key.FocusNeighborRight = pair.Value.Right;
            if (pair.Key.FocusNeighborBottom == pair.Value.Target) pair.Key.FocusNeighborBottom = pair.Value.Bottom;
        }
        _navOverrides.Clear();
    }

    private void SaveCfg()
    {
        var cfg = new ConfigFile();
        cfg.Load(CfgPath);
        cfg.SetValue("prefs", "cart_mode", CartState.CartModeEnabled);
        if (_placed) cfg.SetValue("display", "pos", GlobalPosition);
        cfg.Save(CfgPath);
    }
}
