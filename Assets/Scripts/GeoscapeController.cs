using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The strategic scene: the globe with the missions on it, and the interface round it.
///  - The game clock at the top runs by itself (Game Minutes Per Second).
///  - After First Mission Delay seconds a mission shows up on land (a random place that is not sea on the map; Max Missions says how many
///    there can be at once): a mark pops up on the globe, a message tells about it, and the camera flies over to it.
///  - The pointer on a mark highlights it and shows what it is; a click on it (not a drag of the globe) fades the screen out and loads the
///    tactical scene (GameSession.Mission is left for it to read).
/// The interface objects are given in the Inspector; everything is optional, the scene works without any of them.
/// </summary>
public class GeoscapeController : MonoBehaviour
{
    [Header("World")]
    [SerializeField] private Transform globe;
    [SerializeField] private GlobeCamera globeCamera;
    [Tooltip("The map used to tell land from sea when a mission picks its place (it must be readable). Empty = anywhere.")]
    [SerializeField] private Texture2D landMask;

    [Header("Missions")]
    [SerializeField, Min(0f)] private float firstMissionDelay = 4f;
    [SerializeField, Min(0)] private int maxMissions = 1;
    [Tooltip("How long the camera takes to fly to a new mission.")]
    [SerializeField, Min(0f)] private float focusSeconds = 1.8f;
    [Tooltip("Missions are placed between these latitudes (the poles are ice and are left alone).")]
    [SerializeField] private Vector2 latitudeRange = new Vector2(-50f, 68f);
    [SerializeField] private string[] missionTitles =
    {
        "Террористическая активность",
        "Захват заложников",
        "Вторжение в охраняемую зону",
        "Перехват конвоя"
    };

    [Header("Clock")]
    [Tooltip("How many minutes of the game pass in a second of real time.")]
    [SerializeField, Min(0f)] private float gameMinutesPerSecond = 6f;
    [SerializeField, Min(0)] private int startHour = 8;

    [Header("Interface (all optional)")]
    [SerializeField] private TMP_Text clockText;
    [SerializeField] private TMP_Text signalsText;
    [SerializeField] private CanvasGroup signalsGroup;
    [SerializeField] private TMP_Text toastText;
    [SerializeField] private CanvasGroup toastGroup;
    [SerializeField] private RectTransform tooltipRect;
    [SerializeField] private CanvasGroup tooltipGroup;
    [SerializeField] private TMP_Text tooltipText;
    [Tooltip("A black full-screen image whose alpha is raised when a mission is chosen, before the next scene is loaded.")]
    [SerializeField] private CanvasGroup fadeGroup;
    [SerializeField, Min(0f)] private float fadeSeconds = 0.6f;
    [SerializeField, Min(0f)] private float toastSeconds = 7f;

    private readonly List<MissionMarker> markers = new List<MissionMarker>();
    private MissionMarker hovered;
    private float timer;
    private float gameMinutes;
    private float toastLeft;
    private int missionCount;
    private bool loading;

    /// <summary>The missions on the map.</summary>
    public IReadOnlyList<MissionMarker> Markers => markers;

    /// <summary>True once a mission has been chosen and the next scene is on its way.</summary>
    public bool IsLoading => loading;

    private void Awake()
    {
        GameSession.Mission = null;
        gameMinutes = startHour * 60f;
        Time.timeScale = 1f;
        if (fadeGroup != null)
        {
            fadeGroup.alpha = 1f;                       // the scene fades in from black
            fadeGroup.blocksRaycasts = false;
        }
        SetGroup(toastGroup, 0f);
        SetGroup(tooltipGroup, 0f);
        RefreshSignals();
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // fade in at the start
        if (fadeGroup != null && !loading && fadeGroup.alpha > 0f)
            fadeGroup.alpha = Mathf.MoveTowards(fadeGroup.alpha, 0f, dt / Mathf.Max(0.01f, fadeSeconds));

        // the clock
        gameMinutes += gameMinutesPerSecond * dt;
        if (clockText != null)
        {
            int total = Mathf.FloorToInt(gameMinutes);
            int day = total / (24 * 60) + 1;
            int hour = total / 60 % 24;
            int minute = total % 60;
            clockText.text = "ДЕНЬ " + day + "   " + hour.ToString("00") + ":" + minute.ToString("00");
        }

        // a new mission
        timer += dt;
        if (!loading && markers.Count < maxMissions && timer >= firstMissionDelay)
        {
            SpawnMission();
            timer = 0f;
        }

        // the message fades
        if (toastGroup != null && toastLeft > 0f)
        {
            toastLeft -= dt;
            toastGroup.alpha = Mathf.Clamp01(Mathf.Min(toastLeft, 1f) / 1f) * Mathf.Clamp01((toastSeconds - toastLeft) / 0.3f);
        }

        if (loading)
        {
            SetHovered(null);
            return;
        }

        // the pointer and the missions
        Vector2 pointer = PointerPosition();
        bool overUi = GameSession.ModalOpen || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
        MissionMarker under = null;
        if (!overUi && globeCamera != null && !globeCamera.IsDragging)
            under = MarkerAt(pointer);
        SetHovered(under);
        UpdateTooltip(pointer);

        // a click on a mission
        Vector2 clickAt;
        if (globeCamera != null && globeCamera.TryGetClick(out clickAt))
        {
            MissionMarker clicked = MarkerAt(clickAt);
            if (clicked != null)
                StartMission(clicked);
        }
    }

    // ---- missions ----

    /// <summary>Makes a mission at a random place on land and shows it (does nothing if there are as many as there can be).</summary>
    public MissionMarker SpawnMission()
    {
        if (globe == null || globeCamera == null)
            return null;

        float lat, lon;
        PickLandPlace(out lat, out lon);

        StrategicMission mission = new StrategicMission
        {
            number = ++missionCount,
            title = missionTitles != null && missionTitles.Length > 0 ? missionTitles[Random.Range(0, missionTitles.Length)] : "Миссия",
            latitude = lat,
            longitude = lon
        };
        mission.description = "Сигнал из района " + mission.PlaceText + ".";

        MissionMarker marker = MissionMarker.Create(mission, globe, globeCamera.Radius, globeCamera);
        markers.Add(marker);

        globeCamera.FocusOn(lat, lon, focusSeconds);
        ShowToast("<b>ОБНАРУЖЕН СИГНАЛ</b>\n<size=70%>" + mission.title + "  ·  " + mission.PlaceText + "</size>");
        RefreshSignals();
        return marker;
    }

    /// <summary>Chooses the mission: the screen fades out and the tactical scene is loaded.</summary>
    public void StartMission(MissionMarker marker)
    {
        if (loading || marker == null)
            return;

        loading = true;
        GameSession.Mission = marker.Mission;
        SetGroup(tooltipGroup, 0f);
        StartCoroutine(LoadTactical());
    }

    private IEnumerator LoadTactical()
    {
        if (fadeGroup != null)
        {
            fadeGroup.blocksRaycasts = true;
            float t = 0f;
            while (t < fadeSeconds)
            {
                t += Time.unscaledDeltaTime;
                fadeGroup.alpha = Mathf.Clamp01(t / Mathf.Max(0.01f, fadeSeconds));
                yield return null;
            }
            fadeGroup.alpha = 1f;
        }
        SceneManager.LoadScene(GameSession.TacticalSceneName);
    }

    // A random place that is land on the map (the map is a picture of the Earth: the sea is the blue part of it).
    private void PickLandPlace(out float lat, out float lon)
    {
        float sinMin = Mathf.Sin(Mathf.Min(latitudeRange.x, latitudeRange.y) * Mathf.Deg2Rad);
        float sinMax = Mathf.Sin(Mathf.Max(latitudeRange.x, latitudeRange.y) * Mathf.Deg2Rad);
        lat = 0f;
        lon = 0f;
        for (int attempt = 0; attempt < 300; attempt++)
        {
            lat = Mathf.Asin(Random.Range(sinMin, sinMax)) * Mathf.Rad2Deg;           // even over the area, not crowded at the poles
            lon = Random.Range(-180f, 180f);

            bool farFromOthers = true;
            foreach (MissionMarker m in markers)
            {
                if (Vector3.Angle(Geo.Direction(lat, lon), Geo.Direction(m.Mission.latitude, m.Mission.longitude)) < 25f)
                    farFromOthers = false;
            }

            if (farFromOthers && IsLand(lat, lon))
                return;
        }
    }

    private bool IsLand(float lat, float lon)
    {
        if (landMask == null)
            return true;

        Color c = landMask.GetPixelBilinear((lon + 180f) / 360f, (lat + 90f) / 180f);
        bool water = c.b > c.r * 1.25f && c.b >= c.g * 0.95f;
        return !water;
    }

    // ---- pointer ----

    private MissionMarker MarkerAt(Vector2 screenPosition)
    {
        if (globeCamera == null || globeCamera.Cam == null)
            return null;

        Ray ray = globeCamera.Cam.ScreenPointToRay(screenPosition);
        RaycastHit hit;
        if (!Physics.Raycast(ray, out hit, 500f, ~0, QueryTriggerInteraction.Collide))
            return null;
        return hit.collider.GetComponentInParent<MissionMarker>();
    }

    private void SetHovered(MissionMarker marker)
    {
        if (hovered == marker)
            return;
        if (hovered != null)
            hovered.SetHovered(false);
        hovered = marker;
        if (hovered != null)
            hovered.SetHovered(true);
    }

    private void UpdateTooltip(Vector2 pointer)
    {
        if (tooltipGroup == null)
            return;

        if (hovered == null)
        {
            tooltipGroup.alpha = Mathf.MoveTowards(tooltipGroup.alpha, 0f, 8f * Time.unscaledDeltaTime);
            return;
        }

        if (tooltipText != null)
            tooltipText.text = "<b>" + hovered.Mission.title + "</b>\n<size=75%>" + hovered.Mission.PlaceText + "\nНажмите, чтобы начать миссию</size>";
        tooltipGroup.alpha = Mathf.MoveTowards(tooltipGroup.alpha, 1f, 8f * Time.unscaledDeltaTime);

        if (tooltipRect != null)
        {
            Vector2 size = tooltipRect.sizeDelta * tooltipRect.lossyScale.x;
            Vector2 p = pointer + new Vector2(28f, -22f);
            p.x = Mathf.Clamp(p.x, 4f, Screen.width - size.x - 4f);
            p.y = Mathf.Clamp(p.y, size.y + 4f, Screen.height - 4f);
            tooltipRect.position = p;
        }
    }

    private static Vector2 PointerPosition()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            return Mouse.current.position.ReadValue();
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.mousePosition;
#else
        return Vector2.zero;
#endif
    }

    // ---- interface ----

    private void ShowToast(string text)
    {
        if (toastText != null)
            toastText.text = text;
        toastLeft = toastSeconds;
        if (toastGroup != null)
            toastGroup.alpha = 0f;
    }

    private void RefreshSignals()
    {
        if (signalsText == null)
            return;

        StringBuilder sb = new StringBuilder();
        sb.Append("<b>СИГНАЛЫ</b>  <size=75%>" + markers.Count + "</size>\n");
        foreach (MissionMarker m in markers)
            sb.Append("<size=80%>" + m.Mission.number + ". " + m.Mission.title + "\n    " + m.Mission.PlaceText + "</size>\n");
        if (markers.Count == 0)
            sb.Append("<size=80%>Активности не обнаружено</size>");
        signalsText.text = sb.ToString().TrimEnd('\n');
        SetGroup(signalsGroup, 1f);
    }

    private static void SetGroup(CanvasGroup group, float alpha)
    {
        if (group != null)
            group.alpha = alpha;
    }
}
