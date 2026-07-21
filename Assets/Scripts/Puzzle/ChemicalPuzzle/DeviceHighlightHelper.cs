using UnityEngine;

namespace ChemicalPuzzle
{
    /// <summary>
    /// Handles hover highlight for chemical devices via MaterialPropertyBlock.
    ///
    /// Uses _BaseColor (URP) or _Color (Standard) brightening instead of emission
    /// keywords. This avoids creating material instances and does NOT call
    /// EnableKeyword, which could break meshes in builds due to shader variant
    /// stripping — especially when the source material is an FBX-embedded sub-asset
    /// shared by multiple renderers (centrifuge body, analyzer, buttons).
    /// </summary>
    public static class DeviceHighlightHelper
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId     = Shader.PropertyToID("_Color");

        /// <summary>
        /// Brightens the renderer's base color by adding <paramref name="highlightColor"/>
        /// via MaterialPropertyBlock. Does not modify the shared material or any shader
        /// keywords, making it safe in builds with shader variant stripping.
        /// </summary>
        /// <param name="renderer">Target renderer to highlight.</param>
        /// <param name="highlightColor">Color added to the base color (clamped to 1).</param>
        public static void ShowHighlight(Renderer renderer, Color highlightColor)
        {
            if (renderer == null) return;

            Material mat = renderer.sharedMaterial;
            if (mat == null) return;

            // Determine the base color property (URP: _BaseColor, Standard: _Color).
            int colorId = mat.HasProperty(BaseColorId) ? BaseColorId : ColorId;
            if (!mat.HasProperty(colorId)) return;

            Color original = mat.GetColor(colorId);
            Color brightened = original + highlightColor;
            brightened.r = Mathf.Min(brightened.r, 1f);
            brightened.g = Mathf.Min(brightened.g, 1f);
            brightened.b = Mathf.Min(brightened.b, 1f);
            brightened.a = original.a;

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(colorId, brightened);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>
        /// Removes the highlight by clearing the MaterialPropertyBlock.
        /// The renderer reverts to the shared material's original base color.
        /// </summary>
        /// <param name="renderer">Target renderer to restore.</param>
        public static void HideHighlight(Renderer renderer)
        {
            if (renderer == null) return;
            renderer.SetPropertyBlock(null);
        }
    }
}
