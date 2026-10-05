using UnityEngine;

/// <summary>
/// Makes the cat glow: on enable (music starts — the node is activated by
/// UVPentagramSequence) all renderers under this object get an emissive
/// material so the silhouette reads against the dark room; on disable the
/// emission is removed again. Pair with the dark first preset
/// (crushed exposure + bloom) so only the cat's outline is visible.
/// </summary>
public class GlowingCat : MonoBehaviour
{
    [Tooltip("Emission color of the cat's glow.")]
    [SerializeField] private Color _glowColor = new Color(0.35f, 1f, 0.6f);

    [Tooltip("HDR multiplier on the emission color. 1–2 = soft glow with bloom.")]
    [SerializeField, Range(0f, 5f)] private float _glowIntensity = 1.5f;

    private void OnEnable()
    {
        SetEmission(true);
    }

    private void OnDisable()
    {
        SetEmission(false);
    }

    private void SetEmission(bool glowing)
    {
        foreach (Renderer rend in GetComponentsInChildren<Renderer>(true))
        {
            if (rend == null) continue;

            // Instance materials — prefab asset is never modified
            Material[] materials = rend.materials;
            foreach (Material mat in materials)
            {
                if (mat == null || !mat.HasProperty("_EmissionColor")) continue;

                if (glowing)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", _glowColor * _glowIntensity);
                }
                else
                {
                    mat.SetColor("_EmissionColor", Color.black);
                }
            }
        }
    }
}
