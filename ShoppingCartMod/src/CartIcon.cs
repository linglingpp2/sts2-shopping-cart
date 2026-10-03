using Godot;

namespace ShoppingCart;

/// <summary>A transparent vector icon embedded in the DLL; no resource pack is needed.</summary>
public static class CartIcon
{
    private static Texture2D? _texture;

    public static Texture2D Texture
    {
        get
        {
            if (_texture == null || !GodotObject.IsInstanceValid(_texture))
            {
                using var image = new Image();
                image.LoadSvgFromString("""
                    <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32">
                      <g fill="none" stroke="#91f5d0" stroke-width="2.3" stroke-linecap="round" stroke-linejoin="round">
                        <path d="M3 5h4l3.3 16H25l3-11H8"/>
                        <path d="M11 15h15M16 10v11M22 10v11" opacity=".65"/>
                      </g>
                      <circle cx="13" cy="27" r="2.2" fill="#91f5d0"/>
                      <circle cx="24" cy="27" r="2.2" fill="#91f5d0"/>
                    </svg>
                    """);
                _texture = ImageTexture.CreateFromImage(image);
            }
            return _texture;
        }
    }
}
