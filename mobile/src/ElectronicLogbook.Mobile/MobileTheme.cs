using MudBlazor;

namespace ElectronicLogbook.Mobile;

public static class MobileTheme
{
    public static MudTheme Create() =>
        new()
        {
            PaletteLight = new PaletteLight
            {
                Background = "#f7faf7",
                Surface = "#ffffff",
                AppbarBackground = "#ffffff",
                AppbarText = "#102018",
                Primary = "#187a46",
                PrimaryContrastText = "#ffffff",
                TextPrimary = "#102018",
                TextSecondary = "#5f7168",
                Divider = "#d7e3dc",
                Success = "#168a4a",
                Warning = "#b85c00",
                Error = "#b42318",
                ActionDisabled = "#a9b8b0",
                DrawerBackground = "#ffffff",
                DrawerText = "#102018"
            },
            PaletteDark = new PaletteDark
            {
                Background = "#07130f",
                Surface = "#0f211b",
                AppbarBackground = "#0f211b",
                AppbarText = "#f3f7f4",
                Primary = "#37d47d",
                PrimaryContrastText = "#04110b",
                TextPrimary = "#f3f7f4",
                TextSecondary = "#91a69b",
                Divider = "#24443a",
                Success = "#56d68b",
                Warning = "#f59e42",
                Error = "#f87171",
                ActionDisabled = "#486158",
                DrawerBackground = "#0f211b",
                DrawerText = "#f3f7f4"
            },
            Typography = new Typography
            {
                Default = new DefaultTypography
                {
                    FontFamily = ["Inter", "Segoe UI", "Arial", "sans-serif"],
                    FontSize = "14px",
                    FontWeight = "400",
                    LetterSpacing = "0"
                },
                H1 = new H1Typography { FontSize = "28px", FontWeight = "700", LineHeight = "1.14", LetterSpacing = "0" },
                H2 = new H2Typography { FontSize = "22px", FontWeight = "700", LineHeight = "1.18", LetterSpacing = "0" },
                H3 = new H3Typography { FontSize = "18px", FontWeight = "600", LineHeight = "1.22", LetterSpacing = "0" },
                Body1 = new Body1Typography { FontSize = "16px", LineHeight = "1.45", LetterSpacing = "0" },
                Body2 = new Body2Typography { FontSize = "14px", LineHeight = "1.45", LetterSpacing = "0" },
                Caption = new CaptionTypography { FontSize = "12px", LineHeight = "1.35", LetterSpacing = "0" }
            },
            LayoutProperties = new LayoutProperties
            {
                DefaultBorderRadius = "8px"
            }
        };
}
