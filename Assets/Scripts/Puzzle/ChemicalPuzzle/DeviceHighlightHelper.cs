using UnityEngine;

namespace ChemicalPuzzle
{
    /// <summary>
    /// Handles hover highlight for chemical devices via MaterialPropertyBlock.
    ///
    /// Previous approach created a material instance with new Material(sharedMaterial)
    /// and enabled the _EMISSION keyword. This broke in builds when the source material
    /// was an FBX-embedded sub-asset — the copied material had an invalid shader reference
    /// and the mesh stopped rendering.
    ///
    /// This helper enables the _EMISSION keyword on the shared material once (with
    /// _EmissionColor set to black so other renderers are unaffected), then uses
    /// MaterialPropertyBlock to override _EmissionColor per-renderer. This avoids
    /// creating material instances entirely and works with any material source type
    /// (standalone .mat, FBX sub-asset) in both editor and builds.
    /// </summary>
    public static class DeviceHighlightHelper
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        /// <summary>
        /// Enables emission highlight on the renderer via MaterialPropertyBlock.
        /// The _EMISSION keyword is enabled on the shared material (once) with
        /// _EmissionColor set to black as default, then _EmissionColor is overridden
        /// per-renderer through the PropertyBlock.
        /// </summary>
        /// <param name="renderer">Target renderer to highlight.</param>
        /// <param name="highlightColor">Emission color applied to the renderer.</param>
        public static void ShowHighlight(Renderer renderer, Color highlightColor)
        {
            if (renderer == null) return;

            Material mat = renderer.sharedMaterial;
            if (mat != null)
            {
                // Enable the _EMISSION keyword on the shared material once.
                // Set _EmissionColor to black so renderers without a PropertyBlock
                // are unaffected (black emission = no visible emission).
                if (!mat.IsKeywordEnabled("_EMISSION"))
                {
                    mat.EnableKeyword("_EMISSION");
                    if (mat.HasProperty(EmissionColorId))
                        mat.SetColor(EmissionColorId, Color.black);
                }
            }

            // Override _EmissionColor per-renderer via MaterialPropertyBlock.
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(EmissionColorId, highlightColor);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>
        /// Removes the highlight by clearing the MaterialPropertyBlock.
        /// Emission reverts to the shared material's _EmissionColor (black = none).
        /// </summary>
        /// <param name="renderer">Target renderer to restore.</param>
        public static void HideHighlight(Renderer renderer)
        {
            if (renderer == null) return;
            renderer.SetPropertyBlock(null);
        }
    }
}
