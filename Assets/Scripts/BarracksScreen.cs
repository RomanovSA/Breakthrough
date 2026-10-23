using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The list of the player's soldiers on the strategic map. A button on the map ("Отряд") opens it over the globe; Esc or its own button
/// closes it. For each soldier it shows the name, what he is (by his weapon), the weapon, health, action points and speed.
/// On start, if the roster (PlayerRoster) is empty, it makes the soldiers: Riflemen with the rifle and Shotgunners with the shotgun, with the
/// health, points and speed of the Unit Template. While it is open the globe does not react to the mouse and keys (GameSession.ModalOpen).
/// The rows are made by the script; the panel, the title and the buttons are given in the Inspector.
/// </summary>
public class BarracksScreen : MonoBehaviour
{
    [Header("Soldiers (made if the roster is empty)")]
    [SerializeField] private WeaponData rifle;
    [SerializeField] private WeaponData shotgun;
    [SerializeField, Min(0)] private int riflemen = 4;
    [SerializeField, Min(0)] private int shotgunners = 4;
    [Tooltip("The unit the soldiers become in the tactical scene: their health, action points and speed come from it.")]
    [SerializeField] private Unit unitTemplate;

    [Header("Interface")]
    [SerializeField] private CanvasGroup screen;
    [SerializeField] private RectTransform rowsRoot;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private Button openButton;
    [SerializeField] private TMP_Text openButtonLabel;
    [SerializeField] private Button closeButton;
    [SerializeField, Min(0.01f)] private float fadeSeconds = 0.15f;

    [Header("Rows")]
    [SerializeField, Min(20f)] private float rowHeight = 62f;
    [SerializeField, Min(0f)] private float rowGap = 6f;

    private bool isOpen;

    /// <summary>True while the list is shown.</summary>
    public bool IsOpen => isOpen;

    private void Awake()
    {
        if (PlayerRoster.Count == 0)
            PlayerRoster.Generate(riflemen, rifle, shotgunners, shotgun, unitTemplate);

        BuildRows();
        RefreshTexts();
        SetOpen(false, true);
    }

    private void OnEnable()
    {
        if (openButton != null)
            openButton.onClick.AddListener(Open);
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);
    }

    private void OnDisable()
    {
        if (openButton != null)
            openButton.onClick.RemoveListener(Open);
        if (closeButton != null)
            closeButton.onClick.RemoveListener(Close);
        GameSession.ModalOpen = false;
    }

    private void Update()
    {
        if (screen != null)
        {
            float target = isOpen ? 1f : 0f;
            screen.alpha = Mathf.MoveTowards(screen.alpha, target, Time.unscaledDeltaTime / fadeSeconds);
        }

        if (isOpen && EscapePressed())
            Close();
    }

    /// <summary>Shows the list.</summary>
    public void Open()
    {
        RefreshTexts();
        SetOpen(true, false);
    }

    /// <summary>Hides the list.</summary>
    public void Close()
    {
        SetOpen(false, false);
    }

    private void SetOpen(bool open, bool instant)
    {
        isOpen = open;
        GameSession.ModalOpen = open;
        if (screen != null)
        {
            screen.blocksRaycasts = open;
            screen.interactable = open;
            if (instant)
                screen.alpha = open ? 1f : 0f;
        }
    }

    private void RefreshTexts()
    {
        int n = PlayerRoster.Count;
        if (titleText != null)
            titleText.text = "<b>ОТРЯД</b>   <size=70%>бойцов: " + n + "</size>";
        if (openButtonLabel != null)
            openButtonLabel.text = "ОТРЯД (" + n + ")";
    }

    // ---- the rows ----

    private static readonly float[] columnX = { 14f, 84f, 510f, 730f, 1030f, 1220f, 1305f };
    private static readonly float[] columnW = { 46f, 420f, 210f, 290f, 175f, 75f, 95f };

    private void BuildRows()
    {
        if (rowsRoot == null || titleText == null)
            return;

        for (int i = rowsRoot.childCount - 1; i >= 0; i--)
        {
            GameObject child = rowsRoot.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
        }

        TMP_FontAsset font = titleText.font;
        Color head = new Color(0.55f, 0.85f, 1f, 0.9f);

        // the line of column names
        RectTransform header = NewRow("Header", 0, new Color(0f, 0f, 0f, 0f), 44f);
        string[] titles = { "№", "БОЕЦ", "РОЛЬ", "ОРУЖИЕ", "ЗДОРОВЬЕ", "ОД", "СКОР." };
        for (int c = 0; c < titles.Length; c++)
            NewText(header, titles[c], font, 24f, head, c == 0 ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft, columnX[c], columnW[c]);

        float y = -(44f + rowGap);
        for (int i = 0; i < PlayerRoster.Count; i++)
        {
            SoldierData s = PlayerRoster.Soldiers[i];
            bool odd = i % 2 == 1;
            RectTransform row = NewRow("Soldier " + s.id, y, odd ? new Color(0.5f, 0.7f, 1f, 0.07f) : new Color(0.5f, 0.7f, 1f, 0.12f), rowHeight);
            y -= rowHeight + rowGap;

            WeaponData w = s.PrimaryWeapon;
            bool isShotgun = w != null && w.FireModes.Count > 0 && w.FireModes[0].pellets > 1;

            // a coloured bar by the kind of weapon
            GameObject chip = new GameObject("Kind", typeof(RectTransform), typeof(Image));
            chip.transform.SetParent(row, false);
            RectTransform cr = chip.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(0, 0); cr.anchorMax = new Vector2(0, 1); cr.pivot = new Vector2(0, 0.5f);
            cr.sizeDelta = new Vector2(8f, 0f); cr.anchoredPosition = Vector2.zero;
            Image ci = chip.GetComponent<Image>();
            ci.color = isShotgun ? new Color(1f, 0.42f, 0.3f) : new Color(1f, 0.78f, 0.28f);
            ci.raycastTarget = false;

            string[] cells =
            {
                s.id.ToString(),
                s.name,
                s.role,
                w != null ? w.DisplayName : "-",
                s.maxHealth.ToString(),
                s.actionPoints.ToString(),
                s.speed.ToString()
            };
            for (int c = 0; c < cells.Length; c++)
                NewText(row, cells[c], font, c == 1 ? 30f : 28f, Color.white, c == 0 ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft, columnX[c], columnW[c]);
        }
    }

    private RectTransform NewRow(string name, float y, Color color, float height)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(rowsRoot, false);
        RectTransform r = go.GetComponent<RectTransform>();
        r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 1f);
        r.sizeDelta = new Vector2(0f, height);
        r.anchoredPosition = new Vector2(0f, y);
        Image img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return r;
    }

    private static void NewText(RectTransform parent, string content, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align, float x, float width)
    {
        GameObject go = new GameObject("Cell", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.GetComponent<TextMeshProUGUI>();
        t.font = font;
        t.text = content;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;
        RectTransform r = t.rectTransform;
        r.anchorMin = new Vector2(0, 0); r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 0.5f);
        r.anchoredPosition = new Vector2(x, 0f);
        r.sizeDelta = new Vector2(width, 0f);
    }

    private static bool EscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Escape))
            return true;
#endif
        return false;
    }
}
