using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The exact shape of a unit for bullets. A unit has one simple capsule, much fatter than the soldier himself; for a shot at
/// a target this builds colliders in the shape the unit has right now (its body in its present pose, and its weapon). A bullet
/// that passes through the gap between the arm and the body is a miss, just like the picture of the ring says.
/// Build() makes them (and replaces older ones), Clear() takes them away. ShotCamera adds and removes this component.
/// </summary>
public class UnitHitbox : MonoBehaviour
{
    private Transform root;
    private readonly List<Mesh> bakedMeshes = new List<Mesh>();

    /// <summary>The object that holds the collider parts (null until Build).</summary>
    public Transform Root => root;

    /// <summary>How many collider parts there are now.</summary>
    public int PartCount => root != null ? root.childCount : 0;

    public void Build()
    {
        Clear();

        GameObject holder = new GameObject("Hitbox");
        holder.hideFlags = HideFlags.DontSave;
        holder.transform.SetParent(transform, false);
        root = holder.transform;

        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r == null || r.transform.IsChildOf(root) || r.GetComponent<UnitVisionDisplay>() != null)
                continue;

            SkinnedMeshRenderer skinned = r as SkinnedMeshRenderer;
            if (skinned != null && skinned.sharedMesh != null)
            {
                // the body as it stands now (the mesh is baked with the scale of the transform, so the part has no scale of its own)
                Mesh baked = new Mesh();
                baked.name = "Hitbox_" + skinned.name;
                baked.hideFlags = HideFlags.HideAndDontSave;
                skinned.BakeMesh(baked, true);
                bakedMeshes.Add(baked);
                AddPart(skinned.name, baked, skinned.transform.position, skinned.transform.rotation, Vector3.one);
                continue;
            }

            MeshRenderer plain = r as MeshRenderer;
            MeshFilter filter = plain != null ? plain.GetComponent<MeshFilter>() : null;
            if (filter != null && filter.sharedMesh != null)
            {
                Vector3 own = root.lossyScale;
                Vector3 scale = filter.transform.lossyScale;
                AddPart(plain.name, filter.sharedMesh, filter.transform.position, filter.transform.rotation, new Vector3(scale.x / own.x, scale.y / own.y, scale.z / own.z));
            }
        }
    }

    public void Clear()
    {
        if (root != null)
        {
            if (Application.isPlaying) Destroy(root.gameObject); else DestroyImmediate(root.gameObject);
            root = null;
        }

        foreach (Mesh m in bakedMeshes)
        {
            if (m == null)
                continue;
            if (Application.isPlaying) Destroy(m); else DestroyImmediate(m);
        }
        bakedMeshes.Clear();
    }

    private void AddPart(string partName, Mesh mesh, Vector3 position, Quaternion rotation, Vector3 localScale)
    {
        GameObject part = new GameObject("Hit_" + partName);
        part.hideFlags = HideFlags.DontSave;
        part.transform.SetParent(root, false);
        part.transform.SetPositionAndRotation(position, rotation);
        part.transform.localScale = localScale;

        MeshCollider collider = part.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        collider.contactOffset = 0.0001f;           // the body is exactly as big as it looks, not a centimetre bigger
    }

    private void OnDestroy()
    {
        Clear();
    }
}
