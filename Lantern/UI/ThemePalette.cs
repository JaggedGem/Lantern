using System.Drawing;

namespace Lantern.UI;

/// <summary>Paired surfaces and foregrounds shared by every desktop renderer.</summary>
internal sealed record ThemePalette(
    bool IsDark, Color Canvas, Color Surface, Color Raised, Color Input, Color Text,
    Color Muted, Color Border, Color Primary, Color PrimaryHover, Color PrimaryPressed,
    Color OnPrimary, Color Selection, Color OnSelection, Color DisabledSurface,
    Color DisabledText, Color Success, Color Danger)
{
    public static ThemePalette Light { get; } = new(false,
        Hex(0xF3F5F9), Hex(0xFFFFFF), Hex(0xF7F9FC), Hex(0xFFFFFF), Hex(0x182336),
        Hex(0x526176), Hex(0x7D8B9E), Hex(0x244AC4), Hex(0x1D3DA6), Hex(0x17328C),
        Hex(0xFFFFFF), Hex(0xDCE7FF), Hex(0x142849), Hex(0xE8EDF4), Hex(0x526176),
        Hex(0x176747), Hex(0xAD283D));

    public static ThemePalette Dark { get; } = new(true,
        Hex(0x0D131C), Hex(0x151E2A), Hex(0x1D2A3A), Hex(0x101923), Hex(0xE7EDF6),
        Hex(0xABB9CC), Hex(0x64758C), Hex(0x6E96FF), Hex(0x8CABFF), Hex(0x5D85F2),
        Hex(0x0C1731), Hex(0x29476F), Hex(0xF3F6FF), Hex(0x243246), Hex(0xA3B2C7),
        Hex(0x78DCAF), Hex(0xFF9DA7));

    private static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);

    internal static double Contrast(Color first, Color second)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color color) => 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        var a = Luminance(first); var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
