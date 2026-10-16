using Bezi;
using UnityEditor;
using UnityEngine;

public static class SpiderWebBaker
{
    /// <summary>
    /// Bakes the SpiderWeb.jpg luminance into a clean RGBA PNG: white strands
    /// on a fully transparent background. Produces /Assets/Materials/SpiderInfestation/Textures/SpiderWebOverlay.png
    /// </summary>
    [BeziAction(
        "Bakes the spider web texture luminance into a clean RGBA PNG with transparent background (white strands only). Returns the new asset path.",
        IsReadOnly = true
    )]
    public static string BakeOverlayTexture()
    {
        const string sourcePath = "Assets/Materials/SpiderInfestation/Textures/SpiderWeb.jpg";
        const string outputPath = "Assets/Materials/SpiderInfestation/Textures/SpiderWebOverlay.png";

        var source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
        if (source == null)
            throw new System.Exception($"Source texture not found: {sourcePath}");

        var sourceImporter = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
        if (sourceImporter == null)
            throw new System.Exception($"Texture importer not found for {sourcePath}");

        // Temporarily make the source readable so pixels can be read on CPU
        bool wasReadable = sourceImporter.isReadable;
        if (!wasReadable)
        {
            sourceImporter.isReadable = true;
            sourceImporter.SaveAndReimport();
            source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
        }

        int width;
        int height;
        try
        {
            width = source.width;
            height = source.height;
            var pixels = source.GetPixels();

            var baked = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var output = new Color[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                float lum = pixels[i].r * 0.299f + pixels[i].g * 0.587f + pixels[i].b * 0.114f;
                // The JPEG decodes as white background with dark strands, so
                // strand opacity comes from inverted luminance. Cut the faint
                // JPEG haze and keep the line cores at full alpha.
                float darkness = 1f - lum;
                float a = Mathf.Clamp01((darkness - 0.3f) / 0.3f);
                output[i] = new Color(1f, 1f, 1f, a);
            }
            baked.SetPixels(output);
            baked.Apply();

            System.IO.File.WriteAllBytes(outputPath, baked.EncodeToPNG());
            Object.DestroyImmediate(baked);
        }
        finally
        {
            if (!wasReadable)
            {
                sourceImporter.isReadable = false;
                sourceImporter.SaveAndReimport();
            }
        }

        AssetDatabase.Refresh();

        // Import as a clean uncompressed sprite
        var importer = AssetImporter.GetAtPath(outputPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }

        Debug.Log($"SpiderWebBaker: baked {outputPath} from {sourcePath} ({width}x{height})");
        return outputPath;
    }
}
