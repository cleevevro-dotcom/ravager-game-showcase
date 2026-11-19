using UnityEngine;

/// <summary>Small cache of UI-friendly placeholder sprites (reuses PlaceholderSprites textures).</summary>
public static class UISprites
{
    static Sprite _circle;
    static Sprite _ring;
    static Sprite _square;

    public static Sprite Circle { get { if (_circle == null) _circle = PlaceholderSprites.GetCircle(128);                  return _circle; } }
    public static Sprite Ring   { get { if (_ring   == null) _ring   = PlaceholderSprites.GetRing  (128, 0.12f);           return _ring;   } }
    public static Sprite Square { get { if (_square == null) _square = PlaceholderSprites.GetSquare(64);                   return _square; } }
}
