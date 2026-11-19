using UnityEngine;

[CreateAssetMenu(fileName = "WorldSetLibrary", menuName = "Ravager/World Set Library")]
public class WorldSetLibrary : ScriptableObject
{
    [System.Serializable]
    public class HouseVariant
    {
        public Sprite  sprite;
        [Tooltip("Visual scale applied to the SpriteRenderer transform (sprites here use a bottom-left pivot).")]
        public Vector2 scale = new Vector2(10f, 10f);
        [Tooltip("Width and height of the obstacle box, in world units. Player/enemies cannot enter this box.")]
        public Vector2 obstacleSize = new Vector2(5f, 5f);
        [Tooltip("Offset from the sprite anchor (= spawn point) to the obstacle box CENTER, in world units.")]
        public Vector2 obstacleOffset = new Vector2(0f, 0f);
    }

    [Header("Ground")]
    public Sprite grass;
    public Sprite water;

    [Header("Road")]
    [Tooltip("Sliced path sub-sprites used as a procedural variation pool.")]
    public Sprite[] pathTiles;
    [Tooltip("Optional fallback path sprite when pathTiles is empty.")]
    public Sprite fullPath;

    [Header("Houses — variant A")]
    public HouseVariant houseAVertical   = new HouseVariant();
    public HouseVariant houseAHorizontal = new HouseVariant();

    [Header("Houses — variant B")]
    public HouseVariant houseBVertical   = new HouseVariant();
    public HouseVariant houseBHorizontal = new HouseVariant();

    [Header("Houses — destroyed (shared between variants)")]
    public Sprite houseDestroyed;

    [Header("Tower")]
    public HouseVariant tower = new HouseVariant
    {
        scale          = new Vector2(10f, 10f),
        obstacleSize   = new Vector2(5f, 5f),
        obstacleOffset = Vector2.zero,
    };

    [Header("Decoration — outside the village")]
    [Tooltip("Fallback single tree sprite. Used when 'trees' is empty.")]
    public Sprite tree;
    [Tooltip("Sliced tree variants. WorldManager picks one per spawn via a deterministic hash so the same tile always shows the same tree.")]
    public Sprite[] trees;
    [Tooltip("Sprite variants used for bushes outside the village. Placeholder small circle is used if empty.")]
    public Sprite[] bushes;
}
