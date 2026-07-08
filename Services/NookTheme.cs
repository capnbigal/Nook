using MudBlazor;

namespace Nook.Services;

/// <summary>Static MudTheme factory mirroring the Nook token palette (nook-tokens.css).</summary>
public static class NookTheme
{
    private static readonly string[] Body = { "Figtree", "system-ui", "sans-serif" };
    private static readonly string[] Display = { "Bricolage Grotesque", "system-ui", "sans-serif" };
    private static readonly string[] Mono = { "JetBrains Mono", "ui-monospace", "monospace" };

    public static MudTheme Build() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#5B54E8",
            Secondary = "#F98A3C",
            Background = "#F7F5F0",
            Surface = "#FFFFFF",
            AppbarBackground = "#FFFFFF",
            DrawerBackground = "#F7F5F0",
            TextPrimary = "#262119",
            LinesDefault = "#E9E4D9",
            TableLines = "#E9E4D9",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#5B54E8",
            Secondary = "#F98A3C",
            Background = "#16130E",
            Surface = "#201C15",
            AppbarBackground = "#201C15",
            DrawerBackground = "#16130E",
            TextPrimary = "#F3EEE4",
            LinesDefault = "#2C271E",
        },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "10px" },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = Body },
            H1 = new H1Typography { FontFamily = Display },
            H2 = new H2Typography { FontFamily = Display },
            H3 = new H3Typography { FontFamily = Display },
            H4 = new H4Typography { FontFamily = Display },
            H5 = new H5Typography { FontFamily = Display },
            H6 = new H6Typography { FontFamily = Display },
            Body1 = new Body1Typography { FontFamily = Body },
            Body2 = new Body2Typography { FontFamily = Body },
            Button = new ButtonTypography { FontFamily = Body },
            Caption = new CaptionTypography { FontFamily = Body },
        },
    };
}
