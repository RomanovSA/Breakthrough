using UnityEngine;

/// <summary>
/// Draws the sky behind the game.
/// The game camera is orthographic, and an orthographic camera cannot draw a skybox properly (it looks at one single
/// direction, so you only get one flat colour). This second, perspective camera draws ONLY the skybox, behind the
/// game camera, and turns with it, so the sky behind the field moves when the game camera is rotated with Q / E.
/// Set up as a URP camera stack: this camera is the Base, the game camera is an Overlay on top of it.
/// </summary>
[RequireComponent(typeof(Camera))]
public class SkyBackdrop : MonoBehaviour
{
    [SerializeField] private Camera gameCamera;

    [Tooltip("How many degrees above the horizon the sky camera looks. Higher = less of the ground half of the sky, more sky.")]
    [SerializeField, Range(-30f, 60f)] private float pitch = 10f;

    [SerializeField, Range(20f, 120f)] private float fieldOfView = 70f;

    private Camera skyCamera;

    private void LateUpdate()
    {
        if (skyCamera == null)
            skyCamera = GetComponent<Camera>();
        if (gameCamera == null)
            gameCamera = Camera.main;
        if (gameCamera == null)
            return;

        skyCamera.fieldOfView = fieldOfView;

        // Same heading as the game camera, but looking level instead of down at the field.
        float yaw = gameCamera.transform.eulerAngles.y;
        transform.rotation = Quaternion.Euler(-pitch, yaw, 0f);
    }
}
