using System;
using UnityEngine;
using UnityEngine.UI;

namespace MorePlayers;

internal sealed class LobbyWaterView : MonoBehaviour
{
    private const string GradientShaderName = "Unlit/GradientColor";
    private static readonly int[] WaveSequence = { 2, 3, 4, 5, 6, 6, 8, 8, 9, 10 };
    private RenderTexture surface;
    private Material waterGradient;
    private RawImage image;
    private Sprite[] waveFrames;
    private Image leftWave;
    private Image rightWave;
    private int currentWaveFrame = -1;

    internal static LobbyWaterView Build(RectTransform viewport, Vector2 center, Vector2 size,
        float shorelineHalfWidth = 0, float unitsPerWorldUnit = 32)
    {
        var rect = OnlineInviteMenu.Rect("PracticeWater", viewport, center, size);
        var view = rect.gameObject.AddComponent<LobbyWaterView>();
        view.image = rect.gameObject.AddComponent<RawImage>();
        view.image.raycastTarget = false;
        view.BuildSurface();
        if (shorelineHalfWidth > 0) view.BuildShoreline(shorelineHalfWidth, unitsPerWorldUnit);
        return view;
    }

    private void BuildShoreline(float halfWidth, float unitsPerWorldUnit)
    {
        const string prefix = "WaterWave_";
        var frames = new Sprite[11];
        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (sprite.name.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(sprite.name.Substring(prefix.Length), out int frame) &&
                frame >= 0 && frame < frames.Length)
            {
                frames[frame] = sprite;
            }
        }
        var availableFrames = new System.Collections.Generic.List<Sprite>(WaveSequence.Length);
        foreach (int index in WaveSequence)
            if (frames[index] != null) availableFrames.Add(frames[index]);
        waveFrames = availableFrames.ToArray();
        if (waveFrames.Length == 0) return;

        var strip = image.rectTransform;
        var spriteSize = waveFrames[0].rect.size * (unitsPerWorldUnit / waveFrames[0].pixelsPerUnit);
        // The match puts the mirrored wave particles just outside the land edge and below the waterline.
        float x = halfWidth + 0.2f * unitsPerWorldUnit;
        float y = strip.rect.yMax - 0.2f * unitsPerWorldUnit;
        leftWave = CreateWave(strip, "ShoreWaveLeft", new Vector2(-x, y), spriteSize);
        rightWave = CreateWave(strip, "ShoreWaveRight", new Vector2(x, y), spriteSize);
        leftWave.rectTransform.localScale = new Vector3(-1, 1, 1);
    }

    private Image CreateWave(RectTransform parent, string name, Vector2 center, Vector2 size)
    {
        var wave = OnlineInviteMenu.Rect(name, parent, center, size).gameObject.AddComponent<Image>();
        wave.sprite = waveFrames[0];
        wave.preserveAspect = true;
        wave.raycastTarget = false;
        wave.color = new Color(0.88f, 0.97f, 1f);
        return wave;
    }

    private void Update()
    {
        if (leftWave == null) return;
        int frame = (int)(Time.unscaledTime * 12) % waveFrames.Length;
        if (frame == currentWaveFrame) return;
        currentWaveFrame = frame;
        leftWave.sprite = waveFrames[frame];
        rightWave.sprite = waveFrames[(frame + waveFrames.Length / 2) % waveFrames.Length];
    }

    private void BuildSurface()
    {
        // WaterGradient in the match and GradientColor2 in the menu use
        // the same shader. Reapply the level's water material settings here.
        var shader = Shader.Find(GradientShaderName);
        if (shader == null || !shader.isSupported)
        {
            image.color = new Color(0.004f, 0.435f, 0.812f, 0.85f);
            Main.Log.LogWarning("Bopl's water gradient shader is unavailable in the lobby.");
            return;
        }

        waterGradient = new Material(shader) { name = "Lobby WaterGradient" };
        waterGradient.SetColor("_Gradient", new Color(0.00392157f, 0.43529412f, 0.81176471f));
        waterGradient.SetColor("_Gradient2", new Color(0, 0.31017f, 0.81132f));
        waterGradient.SetFloat("_Strength", 0);
        waterGradient.SetFloat("_AlphaTop", 0.635f);
        waterGradient.SetFloat("_AlphaBot", 1);
        waterGradient.SetFloat("_GradientHeight", -0.3f);

        // The game shader is built for SpriteRenderer and lacks the UI
        // stencil properties. Render it once, then let the lobby mask clip it.
        surface = new RenderTexture(16, 128, 0, RenderTextureFormat.ARGB32)
        {
            name = "Bopl Lobby WaterGradient",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        RenderSurface();
        image.texture = surface;
    }

    private void OnEnable()
    {
        if (surface != null && !surface.IsCreated()) RenderSurface();
    }

    private void RenderSurface()
    {
        var previous = RenderTexture.active;
        try
        {
            RenderTexture.active = surface;
            GL.Clear(false, true, new Color(0, 0.38f, 1f, 1f));
            Graphics.Blit(Texture2D.whiteTexture, surface, waterGradient);
        }
        finally
        {
            RenderTexture.active = previous;
        }
    }

    private void OnDestroy()
    {
        if (surface != null) Destroy(surface);
        if (waterGradient != null) Destroy(waterGradient);
    }
}
