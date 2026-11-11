using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The look the game ships with (art/ART_BIBLE.md): hard shadows from the main light and a low dusk sun.
/// Lives outside the debug panel on purpose: that file is left out of release builds, which then had no shadows at all.
/// </summary>
public static class ShippedLook
{
    public static void Apply()
    {
        Light sun = Object.FindFirstObjectByType<Light>();
        if (sun != null)
        {
            sun.shadows = LightShadows.Hard;
            sun.color = new Color(1f, 0.8f, 0.6f);
            sun.intensity = 1.25f;
            sun.transform.rotation = Quaternion.Euler(34f, -55f, 0f);
        }
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp) urp.mainLightShadowmapResolution = 1024;
        RenderSettings.ambientLight = new Color(0.27f, 0.3f, 0.38f);
    }
}
