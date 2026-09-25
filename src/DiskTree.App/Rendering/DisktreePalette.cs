using System.Numerics;
using Windows.UI;
using DiskTree.Core.Classify;

namespace DiskTree.App.Rendering;

/// <summary>
/// Color system derived from disktree's signature palette:
/// Muted category hues, depth-based lightness lift, amber highlight, and danger red.
/// </summary>
public static class DisktreePalette
{
    public static readonly Color HighlightAmber = Color.FromArgb(255, 245, 158, 11);
    public static readonly Color DangerRed = Color.FromArgb(255, 239, 68, 68);
    public static readonly Color ReclaimHatch = Color.FromArgb(120, 245, 158, 11);

    public static (float Hue, float Chroma) CategoryHue(Category category) => category switch
    {
        Category.Code => (0.605f, 1.0f),          // Blue (Source code, scripts, configs)
        Category.AgentScratch => (0.065f, 1.0f),  // Coral (AI agent worktrees, sandboxes)
        Category.Toolchain => (0.415f, 1.0f),     // Teal/Green (Binaries, toolchains, build tools)
        Category.Synced => (0.535f, 1.0f),        // Cyan (Cloud sync)
        Category.Git => (0.955f, 1.0f),           // Crimson/Rose (.git objects)
        Category.Media => (0.745f, 1.0f),         // Purple/Violet (Images, Video, 3D, Audio)
        Category.Cache => (0.125f, 0.95f),        // Gold/Amber (Caches, build outputs, temp)
        Category.Documents => (0.600f, 0.18f),    // Muted slate-blue (PDF, Docs, Sheets)
        Category.Other => (0.600f, 0.08f),        // Near-neutral grey (Unknown extensions)
        _ => (0.600f, 0.08f)
    };

    public static Color CategoryFill(Category category, int depth, bool isDark = true)
    {
        var (h, chroma) = CategoryHue(category);
        float step = Math.Min(depth, 4);

        float s, l;
        if (isDark)
        {
            s = 0.26f * chroma;
            l = 0.215f + (step * 0.028f);
        }
        else
        {
            s = 0.30f * chroma;
            l = Math.Max(0.25f, 0.84f - (step * 0.03f));
        }

        return HslToRgb(h, s, l);
    }


    public static Color CategoryAccent(Category category, bool isDark = true)
    {
        var (h, chroma) = CategoryHue(category);
        float s = isDark ? 0.42f * chroma : 0.45f * chroma;
        float l = isDark ? 0.52f : 0.46f;
        return HslToRgb(h, s, l);
    }

    public static Color AgeFill(int bucket, int depth, bool isDark = true)
    {
        float fade = Math.Min(bucket, 4) / 4.0f;
        float step = Math.Min(depth, 4);

        float s = (1.0f - fade) * 0.34f + 0.03f;
        float l = isDark ? (0.29f - fade * 0.09f) + (step * 0.028f) : (0.74f + fade * 0.1f) - (step * 0.03f);
        return HslToRgb(0.55f, s, l); // Cyan/slate age hue
    }

    public static Color HslToRgb(float h, float s, float l, byte a = 255)
    {
        h = (h % 1.0f + 1.0f) % 1.0f;
        s = Math.Clamp(s, 0.0f, 1.0f);
        l = Math.Clamp(l, 0.0f, 1.0f);

        float r, g, b;

        if (s == 0f)
        {
            r = g = b = l;
        }
        else
        {
            float q = l < 0.5f ? l * (1.0f + s) : (l + s) - (l * s);
            float p = (2.0f * l) - q;
            r = HueToRgb(p, q, h + (1.0f / 3.0f));
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - (1.0f / 3.0f));
        }

        return Color.FromArgb(a, (byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    private static float HueToRgb(float p, float q, float t)
    {
        if (t < 0f) t += 1f;
        if (t > 1f) t -= 1f;
        if (t < 1f / 6f) return p + ((q - p) * 6f * t);
        if (t < 1f / 2f) return q;
        if (t < 2f / 3f) return p + ((q - p) * ((2f / 3f) - t) * 6f);
        return p;
    }

    public static string FormatBytes(ulong bytes)
    {
        if (bytes >= 1024UL * 1024 * 1024 * 1024)
            return $"{(double)bytes / (1024UL * 1024 * 1024 * 1024):F1} TB";
        if (bytes >= 1024UL * 1024 * 1024)
            return $"{(double)bytes / (1024UL * 1024 * 1024):F1} GB";
        if (bytes >= 1024UL * 1024)
            return $"{(double)bytes / (1024UL * 1024):F1} MB";
        if (bytes >= 1024UL)
            return $"{(double)bytes / 1024UL:F1} KB";
        return $"{bytes} B";
    }
}
