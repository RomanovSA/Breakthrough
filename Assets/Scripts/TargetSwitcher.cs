using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The little panel in the shot view with two arrows and 'TARGET 2 / 3': it switches the shot from one enemy the shooter can
/// see to the next. It is only there while the shot view is on and there is more than one enemy to choose from.
/// (The same can be done with Tab / Shift+Tab or the Left and Right arrow keys, by clicking an enemy in the picture or by
/// clicking its icon at the bottom of the screen.) The whole panel fades with a CanvasGroup so that it can stay active.
/// </summary>
public class TargetSwitcher : MonoBehaviour
{
    [SerializeField] private ShotCamera shotCamera;
    [SerializeField] private CanvasGroup group;
    [SerializeField] private Button previousButton;
    [SerializeField] private Button nextButton;
    [SerializeField] private TMP_Text label;

    private int shownIndex = -1;
    private int shownCount = -1;

    private void OnEnable()
    {
        if (previousButton != null) previousButton.onClick.AddListener(OnPrevious);
        if (nextButton != null) nextButton.onClick.AddListener(OnNext);
    }

    private void OnDisable()
    {
        if (previousButton != null) previousButton.onClick.RemoveListener(OnPrevious);
        if (nextButton != null) nextButton.onClick.RemoveListener(OnNext);
    }

    private void Update()
    {
        Refresh(Time.unscaledDeltaTime);
    }

    /// <summary>Shows or hides the panel and updates the text and the buttons (done every frame by itself).</summary>
    public void Refresh(float deltaTime)
    {
        if (shotCamera == null)
            shotCamera = FindFirstObjectByType<ShotCamera>();

        bool visible = shotCamera != null && shotCamera.IsOn && shotCamera.Targets.Count > 1;
        if (group != null)
        {
            group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, 6f * Mathf.Max(0f, deltaTime));
            group.blocksRaycasts = visible;
            group.interactable = visible;
        }

        if (!visible)
            return;

        bool free = shotCamera.IsHolding && !shotCamera.IsSwitching && !Bullet.AnyInFlight;
        if (previousButton != null) previousButton.interactable = free;
        if (nextButton != null) nextButton.interactable = free;

        int index = shotCamera.TargetIndex + 1;
        int count = shotCamera.Targets.Count;
        if (label != null && (index != shownIndex || count != shownCount))
        {
            shownIndex = index;
            shownCount = count;
            label.text = "ЦЕЛЬ " + index + " / " + count;
        }
    }

    private void OnPrevious()
    {
        if (shotCamera != null)
            shotCamera.CycleTarget(-1);
    }

    private void OnNext()
    {
        if (shotCamera != null)
            shotCamera.CycleTarget(1);
    }
}
