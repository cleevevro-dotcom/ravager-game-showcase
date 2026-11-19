using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime-generated placeholder sprites (circle / ring / square / line) cached by size.
/// User will replace with real art later.
/// </summary>
public static class PlaceholderSprites
{
    const float PixelsPerUnit = 32f;

    static readonly Dictionary<int, Sprite> _circles = new Dictionary<int, Sprite>();
    static readonly Dictionary<int, Sprite> _rings   = new Dictionary<int, Sprite>();
    static readonly Dictionary<int, Sprite> _squares = new Dictionary<int, Sprite>();

    public static Sprite GetCircle(int size = 64)
    {
        if (_circles.TryGetValue(size, out var s)) return s;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        float cx = size * 0.5f - 0.5f;
        float r  = size * 0.5f - 1f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - cx, dy = y - cx;
            tex.SetPixel(x, y, (dx*dx + dy*dy) <= r*r ? Color.white : Color.clear);
        }
        tex.Apply();
        s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), PixelsPerUnit);
        _circles[size] = s;
        return s;
    }

    public static Sprite GetRing(int size = 64, float thicknessFrac = 0.15f)
    {
        int key = size * 100 + Mathf.RoundToInt(thicknessFrac * 100f);
        if (_rings.TryGetValue(key, out var s)) return s;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        float cx = size * 0.5f - 0.5f;
        float rOuter = size * 0.5f - 1f;
        float rInner = rOuter * (1f - thicknessFrac);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - cx, dy = y - cx;
            float d2 = dx*dx + dy*dy;
            tex.SetPixel(x, y, (d2 <= rOuter*rOuter && d2 >= rInner*rInner) ? Color.white : Color.clear);
        }
        tex.Apply();
        s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), PixelsPerUnit);
        _rings[key] = s;
        return s;
    }

    /// <summary>
    /// Returns the visible diameter (in world units) of a ring/circle of the given pixel size,
    /// accounting for the 1-pixel margin used in the generator so that scaling by a world radius
    /// matches the actual painted pixels.
    /// </summary>
    public static float GetVisibleDiameterUnits(int size) => (size - 2f) / PixelsPerUnit;

    public static Sprite GetSquare(int size = 32)
    {
        if (_squares.TryGetValue(size, out var s)) return s;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var cols = new Color[size * size];
        for (int i = 0; i < cols.Length; i++) cols[i] = Color.white;
        tex.SetPixels(cols);
        tex.Apply();
        s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), PixelsPerUnit);
        _squares[size] = s;
        return s;
    }
}
