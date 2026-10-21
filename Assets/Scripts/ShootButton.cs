using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// The Shoot button. It is only there while the shot view is on (the camera is at the shooter and the ring of spread is shown):
/// it fades in with the camera. Pressing it fires one bullet (ShotCamera.Shoot); it can be pressed again once that bullet has
/// hit something. The button cannot be pressed while the camera is still flying in.
/// It lives on the button; the whole panel around it is made invisible and untouchable by a CanvasGroup when there is no shot view.
/// </summary>
[RequireComponent(typeof(Button))]
public class ShootButton : MonoBehaviour
{
    [SerializeField] private ShotCamera shotCamera;
    [Tooltip("Group that fades the whole panel (the button and its frame) in and out.")]
    [SerializeField] private CanvasGroup panelGroup;

    private Button button;
    private TMP_Text label;
    private int shownCost = -1;

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
    }

    /// <summary>Shows or hides the panel and sets whether the button can be pressed (done every frame by itself).</summary>
    public void Refresh(float deltaTime)
    {
        if (button == null)
            button = GetComponent<Button>();
        if (shotCamera == null)
            shotCamera = FindFirstObjectByType<ShotCamera>();

        bool visible = shotCamera != null && shotCamera.IsOn;
        if (panelGroup != null)
        {
            panelGroup.alpha = Mathf.MoveTowards(panelGroup.alpha, visible ? 1f : 0f, 6f * Mathf.Max(0f, deltaTime));
            panelGroup.blocksRaycasts = visible;
            panelGroup.interactable = visible;
        }

        button.interactable = shotCamera != null && shotCamera.CanShoot;

        // the price in action points is on the button
        if (label == null)
            label = GetComponentInChildren<TMP_Text>();
        Unit shooter = shotCamera != null ? shotCamera.Shooter : null;
        if (label != null && shooter != null && shooter.ShotCost != shownCost)
        {
            shownCost = shooter.ShotCost;
            label.text = "СТРЕЛЯТЬ (" + shownCost + " ОД)";
        }
    }

    private void OnClick()
    {
        if (shotCamera != null)
            shotCamera.Shoot();
    }
}
