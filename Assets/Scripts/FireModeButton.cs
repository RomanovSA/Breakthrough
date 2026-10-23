using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The fire mode button of the shot view (and the F key): it switches the shooter's weapon to the next way of firing that he has the action
/// points for (single shot, burst...). The label shows the present mode, how many bullets it fires and what it costs. It is only there while
/// the shot view is on and the weapon has more than one mode; it cannot be pressed while a shot is on its way.
/// It lives on the button; the panel around it fades with a CanvasGroup so that this object can stay active.
/// </summary>
[RequireComponent(typeof(Button))]
public class FireModeButton : MonoBehaviour
{
    [SerializeField] private ShotCamera shotCamera;
    [SerializeField] private CanvasGroup panelGroup;
    [SerializeField] private string prefix = "РЕЖИМ: ";

    private Button button;
    private TMP_Text label;
    private string shownText;

    private void OnEnable()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(OnClick);
    }

    private void OnDisable()
    {
        if (button != null)
            button.onClick.RemoveListener(OnClick);
    }

    private void Update()
    {
        Refresh(Time.unscaledDeltaTime);
        if (KeyPressed() && button.interactable)
            OnClick();
    }

    /// <summary>Shows or hides the panel, sets the label and whether the button can be pressed (done every frame by itself).</summary>
    public void Refresh(float deltaTime)
    {
        if (button == null)
            button = GetComponent<Button>();
        if (shotCamera == null)
            shotCamera = FindFirstObjectByType<ShotCamera>();
        if (label == null)
            label = GetComponentInChildren<TMP_Text>();

        Unit shooter = shotCamera != null ? shotCamera.Shooter : null;
        bool visible = shotCamera != null && shotCamera.IsOn && shooter != null && shooter.FireModeCount > 1;
        if (panelGroup != null)
        {
            panelGroup.alpha = Mathf.MoveTowards(panelGroup.alpha, visible ? 1f : 0f, 6f * Mathf.Max(0f, deltaTime));
            panelGroup.blocksRaycasts = visible;
            panelGroup.interactable = visible;
        }

        button.interactable = visible && shotCamera.CanChangeFireMode;

        if (visible && label != null && shooter.FireMode != null)
        {
            FireModeData mode = shooter.FireMode;
            string text = prefix + mode.displayName + (mode.shots * mode.pellets > 1 ? " x" + mode.shots * mode.pellets : "");
            if (text != shownText)
            {
                shownText = text;
                label.text = text;
            }
        }
    }

    private void OnClick()
    {
        if (shotCamera != null)
            shotCamera.CycleFireMode();
    }

    private static bool KeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.F))
            return true;
#endif
        return false;
    }
}
