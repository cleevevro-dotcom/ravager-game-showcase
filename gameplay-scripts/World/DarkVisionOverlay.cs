using UnityEngine;

/// <summary>
/// Three-zone Dark Vision overlay. One MeshRenderer + one material + one shader pass —
/// no per-object lights, no per-object scripts. Mobile / WebGL safe.
///
/// Visibility zones (world units, measured from the player):
///   1. Inner (dist <= visionRadius)                                        — fully visible
///   2. Middle (visionRadius .. visionRadius + midFadeWidth)                — smooth fade to dim
///   3. Outer (midEnd .. midEnd + outerFadeWidth) and beyond                — heavy dim, NEVER full black
///
/// Outer darkness is clamped by <see cref="_outerDarkness"/> (default ~0.78) so terrain,
/// roads, and large obstacles stay readable even in the dimmest zone — the player can still
/// understand where they can MOVE without getting blinded to the map. Enemies/projectiles
/// are smaller and naturally harder to spot once dimmed at this level, which delivers the
/// design goal of "enemies hidden more than terrain" perceptually.
///
/// The Vision skill drives PlayerStats.visionRadius, which is read every LateUpdate and
/// sized into the shader's inner clear radius. Optionally the skill can also raise the
/// middle ring by tweaking PlayerStats.visionMidBrightnessBonus (added on top of the
/// component's _midDarkness floor).
/// </summary>
[DefaultExecutionOrder(50)]
public class DarkVisionOverlay : MonoBehaviour
{
    public static DarkVisionOverlay Instance { get; private set; }

    [Header("Falloff zones (world units)")]
    [Tooltip("Smooth fade width from the inner clear radius to the middle dim zone.")]
    [SerializeField] float _midFadeWidth   = 1.5f;
    [Tooltip("Smooth fade width from the middle dim zone to the outer heavy-dim zone.")]
    [SerializeField] float _outerFadeWidth = 2.5f;

    [Header("Darkness levels (alpha 0..1)")]
    [Tooltip("Alpha at the middle ring. 0 = no dimming; ~0.30-0.40 reads as 'dim but legible'.")]
    [Range(0f, 1f)]
    [SerializeField] float _midDarkness   = 0.35f;
    [Tooltip("Alpha at the outer zone. KEEP BELOW 1 so terrain stays faintly visible. Default ~0.75-0.80.")]
    [Range(0f, 1f)]
    [SerializeField] float _outerDarkness = 0.78f;

    [Header("Quad placement")]
    [Tooltip("Extra padding (world units) added to the camera rect when sizing the overlay quad. Avoids any seam at the screen edge if the camera shakes.")]
    [SerializeField] float _quadPadding   = 2.0f;
    [Tooltip("Distance in front of the camera to place the overlay. Must be > nearClipPlane and < anything else you want to see through it (we draw with ZTest Always anyway).")]
    [SerializeField] float _quadDistance  = 1.0f;

    Camera        _cam;
    Transform     _camTr;
    PlayerStats   _playerStats;
    Transform     _playerTransform;

    Material      _material;
    Mesh          _quadMesh;
    GameObject    _quadGO;
    MeshRenderer  _quadMR;

    static readonly int PropPlayerPos      = Shader.PropertyToID("_PlayerPos");
    static readonly int PropVisionRadius   = Shader.PropertyToID("_VisionRadius");
    static readonly int PropMidFadeWidth   = Shader.PropertyToID("_MidFadeWidth");
    static readonly int PropOuterFadeWidth = Shader.PropertyToID("_OuterFadeWidth");
    static readonly int PropMidDarkness    = Shader.PropertyToID("_MidDarkness");
    static readonly int PropOuterDarkness  = Shader.PropertyToID("_OuterDarkness");

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_quadMesh != null) Destroy(_quadMesh);
        if (_material != null) Destroy(_material);
    }

    /// <summary>Wire the overlay. Called from GameBootstrap after the player + camera exist.</summary>
    public void Init(Transform player, PlayerStats stats, Camera cam)
    {
        _playerTransform = player;
        _playerStats     = stats;
        _cam             = cam != null ? cam : Camera.main;
        _camTr           = _cam != null ? _cam.transform : null;

        EnsureQuad();
    }

    void EnsureQuad()
    {
        if (_quadGO != null) return;

        // Load shader from Resources/ so it's guaranteed to ship in builds (referenced shaders
        // are otherwise stripped). Asset path: Assets/Resources/DarkVisionOverlay.shader.
        Shader shader = Resources.Load<Shader>("DarkVisionOverlay");
        if (shader == null)
        {
            Debug.LogWarning("[DarkVisionOverlay] Resources/DarkVisionOverlay shader not found — overlay disabled.");
            enabled = false;
            return;
        }

        _material = new Material(shader) { name = "DarkVisionOverlay (runtime)" };

        // Procedural 1-unit quad — we scale via transform.localScale.
        _quadMesh = new Mesh { name = "DarkVisionOverlay Quad" };
        _quadMesh.vertices = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f),
        };
        _quadMesh.triangles = new int[] { 0, 2, 1, 1, 2, 3 };
        _quadMesh.uv        = new Vector2[]
        {
            new Vector2(0,0), new Vector2(1,0), new Vector2(0,1), new Vector2(1,1),
        };
        _quadMesh.RecalculateBounds();
        // Bounds set high so frustum culling never drops the overlay even if its tiny local AABB
        // ends up outside the view due to scale changes between frames.
        _quadMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);

        _quadGO = new GameObject("DarkVisionOverlayQuad");
        _quadGO.transform.SetParent(_camTr != null ? _camTr : transform, false);
        _quadGO.transform.localPosition = new Vector3(0f, 0f, _quadDistance);
        _quadGO.transform.localRotation = Quaternion.identity;

        var mf = _quadGO.AddComponent<MeshFilter>();
        mf.sharedMesh = _quadMesh;

        _quadMR = _quadGO.AddComponent<MeshRenderer>();
        _quadMR.sharedMaterial   = _material;
        _quadMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _quadMR.receiveShadows    = false;
        _quadMR.lightProbeUsage   = UnityEngine.Rendering.LightProbeUsage.Off;
        _quadMR.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    void LateUpdate()
    {
        if (_quadGO == null) return;
        if (_cam == null || _camTr == null)
        {
            _cam   = Camera.main;
            _camTr = _cam != null ? _cam.transform : null;
            if (_camTr == null) return;
            _quadGO.transform.SetParent(_camTr, false);
        }

        // Size the quad so it always covers the viewport with a small padding margin.
        float halfHeight, halfWidth;
        if (_cam.orthographic)
        {
            halfHeight = _cam.orthographicSize + _quadPadding;
            halfWidth  = halfHeight * _cam.aspect;
        }
        else
        {
            halfHeight = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Abs(_quadDistance) + _quadPadding;
            halfWidth  = halfHeight * _cam.aspect;
        }

        _quadGO.transform.localPosition = new Vector3(0f, 0f, _quadDistance);
        _quadGO.transform.localRotation = Quaternion.identity;
        _quadGO.transform.localScale    = new Vector3(halfWidth * 2f, halfHeight * 2f, 1f);

        // Push current player position + vision radius into the material. visionRadius is updated
        // by VisionSkill stacks on PlayerStats; the shader handles the smooth 3-zone falloff.
        Vector2 px;
        if (_playerTransform != null) px = _playerTransform.position;
        else                          px = _camTr.position;

        float radius = _playerStats != null ? _playerStats.visionRadius : 6f;

        // Optional skill-driven middle-ring brightness bonus. Each unit subtracts from
        // _midDarkness, never going below 0. Default 0 (no change). Lets a future Vision-tier
        // bonus brighten the middle ring without touching the inspector.
        float midBonus  = _playerStats != null ? _playerStats.visionMidBrightnessBonus : 0f;
        float midAlpha  = Mathf.Clamp01(_midDarkness   - midBonus);
        float outerAlpha = Mathf.Clamp01(_outerDarkness - midBonus * 0.5f);

        _material.SetVector(PropPlayerPos,      new Vector4(px.x, px.y, 0f, 0f));
        _material.SetFloat (PropVisionRadius,   radius);
        _material.SetFloat (PropMidFadeWidth,   _midFadeWidth);
        _material.SetFloat (PropOuterFadeWidth, _outerFadeWidth);
        _material.SetFloat (PropMidDarkness,    midAlpha);
        _material.SetFloat (PropOuterDarkness,  outerAlpha);
    }
}
