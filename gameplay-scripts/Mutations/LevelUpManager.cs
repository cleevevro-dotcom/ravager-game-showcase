using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class LevelUpManager : MonoBehaviour
{
    public static LevelUpManager Instance { get; private set; }

    [SerializeField] GameObject _panel;
    [SerializeField] Button[]            _mutationButtons;
    [SerializeField] TextMeshProUGUI[]   _mutationNames;
    [SerializeField] TextMeshProUGUI[]   _mutationDescs;
    // Per-card icon Image refs. Wired by LevelUpPanelBuilder.SetMutationIcons so the
    // PresentChoices hot path can stamp the IconSkills sprite without doing a child Find.
    [SerializeField] Image[]             _mutationIcons;
    // Per-card "Context" label (category + tier badge). Wired by LevelUpPanelBuilder so the
    // PresentChoices hot path doesn't have to walk the hierarchy for it either.
    [SerializeField] TextMeshProUGUI[]   _mutationContexts;
    // Cached reference to the panel builder so re-builds don't have to FindObject.
    LevelUpPanelBuilder _builder;

    PlayerStats _playerStats;
    List<IPlayerMutation> _currentChoices;
    int  _pendingLevelUps;
    bool _awaitingPick;

    [SerializeField] int _cardsPerLevelUp = 3;

    static readonly KeyCode[] HotKeys = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4 };

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        if (_panel != null) _panel.SetActive(false);
    }

    public void Init(PlayerStats stats)
    {
        _playerStats = stats;
        stats.OnLevelUp += HandleLevelUp;
    }

    // (Hotkey + arm-window handling moved to the Update method further down in this file.)

    void HandleLevelUp(int level)
    {
        if (GameManager.Instance != null && GameManager.Instance.State == GameState.Dead) return;

        if (_awaitingPick)
        {
            _pendingLevelUps++;
            return;
        }

        PresentChoices();
    }

    void PresentChoices()
    {
        // Runtime auto-build: if the scene didn't pre-author the LevelUp overlay (or the
        // designer hasn't run Tools > Ravager > Build Game UI yet), build it now under the
        // shared GameScreen hierarchy so the level-up flow never silently no-ops.
        if (_panel == null || _mutationButtons == null || _mutationButtons.Length == 0)
            TryAutoBuildPanel();

        if (_panel == null || _mutationButtons == null || _mutationButtons.Length == 0)
        {
            Debug.LogError("[LevelUpManager] Panel or mutation buttons not assigned (auto-build failed; check that [GameUI] exists in the scene).");
            return;
        }

        GameManager.Instance.SetState(GameState.LevelUp);
        var inv = SkillManager.Instance != null ? SkillManager.Instance.Inventory : null;

        // Cap to the number of buttons actually wired in the scene so the UI stays sane even
        // if the prefab still has 3 slots.
        int target = Mathf.Min(_cardsPerLevelUp, _mutationButtons != null ? _mutationButtons.Length : 0);
        _currentChoices = MutationLibrary.GetRandom(target, inv);

        ForceInteractable(_panel);

        // Hot-path local references — touch the serialized arrays exactly once each so the
        // per-card loop reads array slots, not field reads or hierarchy walks.
        var art      = RavagerArt.Load();
        var buttons  = _mutationButtons;
        var titles   = _mutationNames;
        var contexts = _mutationContexts;
        var descs    = _mutationDescs;
        var icons    = _mutationIcons;
        int btnLen   = buttons  != null ? buttons.Length  : 0;
        int titLen   = titles   != null ? titles.Length   : 0;
        int ctxLen   = contexts != null ? contexts.Length : 0;
        int descLen  = descs    != null ? descs.Length    : 0;
        int iconLen  = icons    != null ? icons.Length    : 0;

        for (int i = 0; i < btnLen; i++)
        {
            var button = buttons[i];
            if (button == null) continue;

            bool hasChoice = i < _currentChoices.Count;
            button.gameObject.SetActive(hasChoice);
            if (!hasChoice) continue;

            var choice = _currentChoices[i];
            string hex     = CategoryHex(choice.Category);
            string tierTag = choice.Tier switch { 1 => "T1", 2 => "T2", 3 => "T3", _ => "T?" };
            string catName = choice.Category.ToString(); // Elemental / Weapon / Stat / Bonus

            // Title — the skill name itself (no tier prefix; that lives on Context).
            if (i < titLen && titles[i] != null) titles[i].text = choice.Name;

            // Context — category badge in category color (e.g. "Elemental T1").
            if (i < ctxLen && contexts[i] != null)
                contexts[i].text = $"<color=#{hex}>{catName} {tierTag}</color>";

            // Description — flavor + stack count.
            if (i < descLen && descs[i] != null)
                descs[i].text = $"{choice.Description}\n<size=80%>Stack ×{choice.StacksAfter}</size>";

            // Icon — cached Image, sprite from the IconSkills set.
            if (art != null && choice is SkillTierCard stc
                && i < iconLen && icons[i] != null)
            {
                Sprite spr = art.GetSkillIcon(stc.Id);
                var img = icons[i];
                if (img.sprite != spr) img.sprite = spr;
                if (!img.preserveAspect) img.preserveAspect = true;
                img.enabled = spr != null;
                img.color   = Color.white;
            }

            int idx = i;
            button.interactable = true;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => OnCardTapped(idx));
            ForceInteractable(button.gameObject);
        }

        // Reset arm state every level-up so spam from the previous level can't carry over.
        _armedIdx          = -1;
        _armedAtUnscaled   = 0f;

        _panel.SetActive(true);
        _awaitingPick = true;

        if (EventSystem.current != null && _mutationButtons[0] != null)
            EventSystem.current.SetSelectedGameObject(_mutationButtons[0].gameObject);
    }

    // 2-tap-to-confirm state. First tap on a card ARMS it (visual scale-up + label
    // changes to "TAP AGAIN"); a second tap on the SAME card within _confirmWindow
    // confirms the pick. A tap on a different card re-arms that one. Any tap outside
    // the window disarms — prevents the player's attack-spam from accidentally picking
    // a card the moment the panel pops up.
    int   _armedIdx        = -1;
    float _armedAtUnscaled = 0f;
    const float _confirmWindow = 1.5f;
    static readonly Vector3 ArmedScale   = new Vector3(1.06f, 1.06f, 1f);
    static readonly Vector3 RestingScale = Vector3.one;

    void OnCardTapped(int idx)
    {
        if (!_awaitingPick) return;
        if (_currentChoices == null || idx < 0 || idx >= _currentChoices.Count) return;

        float now = Time.unscaledTime;
        bool sameCard = idx == _armedIdx;
        bool inWindow = (now - _armedAtUnscaled) <= _confirmWindow;

        if (sameCard && inWindow)
        {
            PickMutation(idx);
            return;
        }

        // First tap (or expired window, or different card) — arm this card.
        DisarmAllCards();
        _armedIdx        = idx;
        _armedAtUnscaled = now;
        ArmCardVisual(idx, true);
    }

    void DisarmAllCards()
    {
        if (_mutationButtons == null) return;
        for (int i = 0; i < _mutationButtons.Length; i++)
            ArmCardVisual(i, false);
        _armedIdx = -1;
    }

    void ArmCardVisual(int idx, bool armed)
    {
        if (_mutationButtons == null || idx < 0 || idx >= _mutationButtons.Length) return;
        var btn = _mutationButtons[idx];
        if (btn == null) return;
        btn.transform.localScale = armed ? ArmedScale : RestingScale;
        // Re-tint the card background to hint the armed state.
        var img = btn.targetGraphic as Image;
        if (img != null) img.color = armed ? new Color(1f, 0.95f, 0.7f, 1f) : Color.white;
        // Swap title to "TAP AGAIN" while armed; restore real name on disarm.
        if (_mutationNames != null && idx < _mutationNames.Length && _mutationNames[idx] != null
            && _currentChoices != null && idx < _currentChoices.Count)
        {
            _mutationNames[idx].text = armed ? "TAP AGAIN" : _currentChoices[idx].Name;
        }
    }

    // Disarm when the confirm window expires so the visual hint doesn't linger forever.
    void Update()
    {
        if (_armedIdx >= 0 && Time.unscaledTime - _armedAtUnscaled > _confirmWindow)
            DisarmAllCards();

        if (!_awaitingPick || _currentChoices == null) return;
        for (int i = 0; i < HotKeys.Length && i < _currentChoices.Count; i++)
        {
            if (Input.GetKeyDown(HotKeys[i]))
            {
                // Keyboard hotkey: behave as if the player tapped the card normally
                // (still needs the 2-tap to confirm, for parity with touch).
                OnCardTapped(i);
                return;
            }
        }
    }

    void PickMutation(int idx)
    {
        if (!_awaitingPick) return;
        if (_currentChoices == null || idx < 0 || idx >= _currentChoices.Count) return;

        DisarmAllCards();
        _currentChoices[idx].Apply(_playerStats);
        _playerStats.RefreshStatsUI();
        _awaitingPick = false;

        if (_pendingLevelUps > 0)
        {
            _pendingLevelUps--;
            PresentChoices();
            return;
        }

        _panel.SetActive(false);
        GameManager.Instance.SetState(GameState.Playing);
    }

    static void ForceInteractable(GameObject go)
    {
        var cg = go.GetComponent<CanvasGroup>();
        if (cg == null) return;
        cg.interactable   = true;
        cg.blocksRaycasts = true;
        if (cg.alpha < 0.01f) cg.alpha = 1f;
    }

    static string CategoryHex(SkillCategory cat) => cat switch
    {
        SkillCategory.Elemental => "F08A4B",
        SkillCategory.Weapon    => "B0C4DE",
        SkillCategory.Stat      => "9DCB66",
        SkillCategory.Bonus     => "E0BBE4",
        _                       => "FFFFFF",
    };

    /// <summary>Kept for backwards compatibility — the LevelUpPanelBuilder now sets the
    /// field via reflection directly, but external callers (Editor tools, tests) can still
    /// use this entry point.</summary>
    public void SetMutationIcons(Image[] icons) => _mutationIcons = icons;
    public void SetMutationContexts(TextMeshProUGUI[] contexts) => _mutationContexts = contexts;

    /// <summary>
    /// One-shot auto-build of the mutation overlay. Caches the builder reference so a
    /// second call (e.g. on the next level-up) does NOT walk the hierarchy again — even
    /// if the designer adds more cards, the cached builder reference still wires into
    /// the existing manager fields.
    ///
    /// Looks for the panel under either of the user-visible names: "MutationPanel"
    /// (the authored hierarchy in the screenshot) or "LevelUpPanel" (the old default).
    /// Never calls FindObjectOfType at the per-frame layer — only once, here, to locate
    /// the hub before [GameUI]/GameScreenHub Awake has fired.
    /// </summary>
    void TryAutoBuildPanel()
    {
        if (_builder != null)
        {
            _builder.BuildIfMissing();
            return;
        }
        // GameScreenHub sets a static singleton inside its Awake; the editor builder
        // also runs that Awake at edit time, so this almost always returns non-null
        // without doing a scene-wide search.
        var hub = GameScreenHub.Instance;
        if (hub == null) hub = FindObjectOfType<GameScreenHub>(includeInactive: true);
        if (hub == null || hub.GameScreen == null) return;

        // The user's authored panel is named "MutationPanel". Old code defaulted to
        // "LevelUpPanel" — accept either, prefer the authored name.
        Transform t = hub.GameScreen.Find("MutationPanel");
        if (t == null) t = hub.GameScreen.Find("LevelUpPanel");

        GameObject panelGo;
        if (t != null) panelGo = t.gameObject;
        else
        {
            panelGo = new GameObject("MutationPanel", typeof(RectTransform));
            panelGo.transform.SetParent(hub.GameScreen, false);
            var rt = (RectTransform)panelGo.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        _builder = panelGo.GetComponent<LevelUpPanelBuilder>();
        if (_builder == null) _builder = panelGo.AddComponent<LevelUpPanelBuilder>();
        _builder.SetLevelUpManager(this);
        _builder.BuildIfMissing();
    }
}
