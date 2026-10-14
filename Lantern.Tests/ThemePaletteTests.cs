using System.Drawing;
using Lantern.UI;
using Xunit;

namespace Lantern.Tests;

public sealed class ThemePaletteTests
{
    public static IEnumerable<object[]> TextPairs()
    {
        foreach (var palette in new[] { ThemePalette.Light, ThemePalette.Dark })
        {
            var name = palette.IsDark ? "dark" : "light";
            var surfaces = new[] { ("canvas", palette.Canvas), ("content", palette.Surface), ("raised", palette.Raised), ("input", palette.Input) };
            var text = new[] { ("body", palette.Text), ("secondary", palette.Muted), ("error", palette.Danger), ("success", palette.Success) };
            foreach (var (surfaceName, background) in surfaces)
                foreach (var (textName, foreground) in text)
                    yield return [$"{name}/{textName}/{surfaceName}", foreground, background];
            yield return [$"{name}/primary", palette.OnPrimary, palette.Primary];
            yield return [$"{name}/primary-hover", palette.OnPrimary, palette.PrimaryHover];
            yield return [$"{name}/primary-pressed", palette.OnPrimary, palette.PrimaryPressed];
            yield return [$"{name}/selection", palette.OnSelection, palette.Selection];
            yield return [$"{name}/secondary-pressed", palette.Text, palette.Selection];
            yield return [$"{name}/disabled", palette.DisabledText, palette.DisabledSurface];
        }
    }
    [Theory]
    [MemberData(nameof(TextPairs))]
    public void AllTextStates_MeetNormalTextContrast(string state, Color foreground, Color background) =>
        Assert.True(ThemePalette.Contrast(foreground, background) >= 4.5, $"{state} contrast is {ThemePalette.Contrast(foreground, background):F2}:1.");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InputAndFocusBoundaries_RemainVisible(bool dark)
    {
        var palette = dark ? ThemePalette.Dark : ThemePalette.Light;
        foreach (var surface in new[] { palette.Canvas, palette.Surface, palette.Input, palette.Raised })
            Assert.True(ThemePalette.Contrast(palette.Border, surface) >= 3);
    }

    [Fact]
    public void DarkSurfaces_AreDarkAndPreserveVisualHierarchy()
    {
        var palette = ThemePalette.Dark;
        Assert.True(palette.Canvas.GetBrightness() < 0.1);
        Assert.True(palette.Surface.GetBrightness() < 0.15);
        Assert.True(palette.Raised.GetBrightness() < 0.2);
        Assert.True(palette.Input.GetBrightness() < 0.15);
        Assert.NotEqual(palette.Canvas, palette.Surface);
        Assert.NotEqual(palette.Surface, palette.Raised);
    }
}
