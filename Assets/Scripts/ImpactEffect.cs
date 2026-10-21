using UnityEngine;

/// <summary>Little effects for the shooting: a flash where a bullet hits, and the pieces that fly when an obstacle breaks.</summary>
public static class ImpactEffect
{
    /// <summary>A bright flash that swells and goes out in a moment.</summary>
    public static void Spark(Vector3 point, Color colour)
    {
        // made by hand, not from a primitive: a primitive has a collider, which cannot be removed inside a physics callback
        // (this is called from one) and which would stop later bullets and catch the mouse
        GameObject flash = new GameObject("Spark");
        flash.layer = 2;
        flash.transform.position = point;
        flash.transform.localScale = Vector3.one * 0.1f;
        flash.AddComponent<MeshFilter>().sharedMesh = SphereMesh();

        Renderer r = flash.AddComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.sharedMaterial = FlatMaterial(colour);

        TempEffect t = flash.AddComponent<TempEffect>();
        t.life = 0.12f;
        t.grow = 7f;
    }

    /// <summary>Pieces of a broken thing: small cubes in its colour that fly apart, fall and disappear after a while.</summary>
    public static void Debris(Bounds bounds, Color colour, int count)
    {
        Material material = FlatMaterial(colour);
        for (int i = 0; i < count; i++)
        {
            GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            piece.name = "Debris";
            piece.layer = 2;                                          // not something the mouse can hit
            piece.transform.position = bounds.center + new Vector3(Random.Range(-1f, 1f) * bounds.extents.x, Random.Range(-0.3f, 1f) * bounds.extents.y, Random.Range(-1f, 1f) * bounds.extents.z);
            piece.transform.rotation = Random.rotation;
            piece.transform.localScale = Vector3.one * Random.Range(0.06f, 0.16f);

            Renderer r = piece.GetComponent<Renderer>();
            r.sharedMaterial = material;

            Rigidbody body = piece.AddComponent<Rigidbody>();
            body.mass = 0.3f;
            body.linearVelocity = (piece.transform.position - bounds.center).normalized * Random.Range(1.5f, 4f) + Vector3.up * Random.Range(1f, 3f);
            body.angularVelocity = Random.insideUnitSphere * 8f;

            piece.AddComponent<TempEffect>().life = Random.Range(1.2f, 2.2f);
        }
    }

    private static Mesh sphere;

    private static Mesh SphereMesh()
    {
        if (sphere == null)
        {
            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere = temp.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying) Object.Destroy(temp); else Object.DestroyImmediate(temp);
        }
        return sphere;
    }

    private static Material FlatMaterial(Color colour)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        Material m = new Material(shader);
        m.SetColor("_BaseColor", colour);
        m.SetFloat("_Smoothness", 0.1f);
        return m;
    }
}
