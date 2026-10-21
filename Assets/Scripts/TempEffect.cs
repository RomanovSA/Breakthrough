using UnityEngine;

/// <summary>A short-lived thing (a spark, a bit of debris): it can grow while it lives and removes itself when its time is up.</summary>
public class TempEffect : MonoBehaviour
{
    public float life = 1f;

    [Tooltip("Relative growth per second (6 = the object is about 7 times bigger after a second).")]
    public float grow;

    private void Update()
    {
        life -= Time.deltaTime;
        if (grow != 0f)
            transform.localScale *= 1f + grow * Time.deltaTime;

        if (life <= 0f)
            Destroy(gameObject);
    }
}
