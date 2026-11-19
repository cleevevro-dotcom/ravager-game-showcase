using System.Collections.Generic;
using UnityEngine;

public class Chunk
{
    public Vector2Int Coord;
    public bool       Generating;

    public readonly List<SpriteRenderer> Grass         = new List<SpriteRenderer>(4);
    public readonly List<SpriteRenderer> Tiles         = new List<SpriteRenderer>(64);
    public readonly List<SpriteRenderer> Bushes        = new List<SpriteRenderer>(16);
    public readonly List<Destructible>   Destructibles = new List<Destructible>(8);
}
