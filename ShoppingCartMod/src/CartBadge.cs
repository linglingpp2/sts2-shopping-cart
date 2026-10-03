using Godot;

namespace ShoppingCart;

/// <summary>贴在商店槽位右上角的“已加入购物车”角标。</summary>
public class CartBadge : PanelContainer
{
    private static readonly StyleBoxFlat BgStyle = new()
    {
        BgColor = new Color(0.08f, 0.35f, 0.2f, 0.92f),
        CornerRadiusBottomLeft = 12,
        CornerRadiusBottomRight = 12,
        CornerRadiusTopLeft = 12,
        CornerRadiusTopRight = 12,
        BorderColor = new Color(0.16f, 0.92f, 0.75f),
        BorderWidthBottom = 1,
        BorderWidthLeft = 1,
        BorderWidthRight = 1,
        BorderWidthTop = 1,
    };

    public CartBadge()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", BgStyle);
        CustomMinimumSize = new Vector2(26, 26);

        var mark = new Label
        {
            Text = "✓",
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        mark.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        mark.AddThemeFontSizeOverride("font_size", 16);
        mark.AddThemeColorOverride("font_color", new Color(0.55f, 1f, 0.75f));
        AddChild(mark);
        Size = CustomMinimumSize;
    }

    /// <summary>定位到槽位右上角（不依赖槽位尺寸）。</summary>
    public void PlaceAtTopRight()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
        OffsetLeft = -32;
        OffsetTop = 2;
        OffsetRight = -6;
        OffsetBottom = 28;
    }
}
