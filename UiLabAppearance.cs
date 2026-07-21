using System.Text.Json;

namespace SmartShellCleanerAvalonia;

internal sealed record UiPresetOption(
    string Key,
    string Title,
    string Library,
    string Accent,
    string BrightAccent,
    string DeepAccent,
    string SoftAccent,
    string AccentBorder,
    IReadOnlyDictionary<string, string> Colors)
{
    public override string ToString() => Title;
}

internal sealed record UiCustomPalette(
    string Accent,
    string Background,
    string Surface,
    string Border,
    string Text,
    string DashboardPanel = "#151c24",
    string ShopPanel = "#121a24",
    string PsTimerPanel = "#101f1a",
    string MapControlPanel = "#121a24",
    string PcFree = "#2f8cff",
    string PcLoggedIn = "#17c964",
    string PcSession = "#8b5cf6",
    string PcCleaned = "#17c964",
    string PcService = "#f5a524",
    string PcShellOff = "#ff4d4f",
    string PcConnectionLost = "#ff3b4d",
    string PcOffline = "#667085",
    string Motif = "",
    string BackgroundImage = "",
    double BackgroundOpacity = 0,
    double PanelOpacity = 1,
    double PcCardOpacity = -1);

internal sealed record UiSavedPalette(string Name, UiCustomPalette Palette);

internal static class UiLabAppearance
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SmartShellCleaner",
        "appearance.json");
    private static readonly string LegacyUiLabSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SmartShellCleaner.UiLab",
        "appearance.json");

    public static IReadOnlyList<UiPresetOption> Options { get; } =
    [
        Preset("smartshell", "SmartShell Blue", "Fluent", "#2f8cff", "#4ea1ff", "#102a4d", "#16243a", "#24466f",
            "#080d12", "#0d1217", "#111820", "#151d28", "#182231", "#263244", "#2b3550", "#334052", "#f5f7ff", "#a9b2c5", "#8f9aad", "#778399", "#c9d3e6"),
        Preset("violet", "Violet Night", "Fluent", "#8b5cf6", "#a78bfa", "#2c1f4d", "#251b3d", "#5b3f8f",
            "#0b0912", "#100d18", "#15111f", "#1a1526", "#211a31", "#362a4e", "#4b376b", "#403354", "#faf7ff", "#d0c7e2", "#a49ab6", "#7d748c", "#ded6ec"),
        Preset("semi", "Semi Midnight", "Semi", "#7c6cf2", "#9b8cff", "#292453", "#23203d", "#4e4784",
            "#0f1115", "#13161b", "#171a20", "#1c2027", "#222730", "#303640", "#3a424d", "#343b46", "#f4f5f7", "#c9cdd3", "#9ca2ab", "#777e88", "#d7dbe1"),
        Preset("suki", "Suki Neon", "SukiUI", "#00a8e8", "#46c7ff", "#07344a", "#0d2b3b", "#176382",
            "#080d16", "#0c1320", "#101827", "#141e30", "#19263a", "#263a52", "#31506d", "#2b4058", "#f3f8ff", "#c3d2e5", "#8fa5bd", "#6e8298", "#d1e2f4"),
        Preset("material", "Material Graphite", "Material", "#bb86fc", "#d0a8ff", "#3b2855", "#2a2038", "#65488a",
            "#101114", "#131419", "#17181d", "#1c1d23", "#22232b", "#343640", "#424550", "#3a3c46", "#f7f5fa", "#cbc7d0", "#9d98a5", "#77727f", "#ddd8e2"),
        Preset("oled", "OLED Purple", "Fluent", "#a855f7", "#c084fc", "#321348", "#230e32", "#5c247c",
            "#000000", "#050506", "#08090b", "#0d0f12", "#11141a", "#252933", "#303642", "#2a2f39", "#faf8ff", "#cbc6d2", "#9993a3", "#716c79", "#dcd6e3")
    ];

    private static readonly AppearanceSettings Settings = LoadSettings();

    public static string CurrentKey { get; private set; } = ForcedKey() ?? Settings.Theme;
    public static UiPresetOption Current => Options.FirstOrDefault(x => x.Key == CurrentKey) ?? Options[0];
    public static UiCustomPalette? CustomPalette => Settings.Palette;
    public static IReadOnlyList<UiSavedPalette> SavedPalettes => Settings.SavedPalettes;
    public static string EffectiveAccent => CustomPalette?.Accent ?? Current.Accent;
    public static string EffectiveBrightAccent => CustomPalette is { } palette ? Mix(palette.Accent, "#ffffff", 0.18) : Current.BrightAccent;
    public static string EffectiveDeepAccent => CustomPalette is { } palette ? Mix(palette.Accent, palette.Background, 0.30) : Current.DeepAccent;
    public static string EffectiveSoftAccent => CustomPalette is { } palette ? Mix(palette.Accent, palette.Surface, 0.18) : Current.SoftAccent;
    public static string EffectiveAccentBorder => CustomPalette is { } palette ? Mix(palette.Accent, palette.Surface, 0.55) : Current.AccentBorder;
    public static string DashboardPanel => CustomPalette?.DashboardPanel ?? "#151c24";
    public static string ShopPanel => CustomPalette?.ShopPanel ?? "#121a24";
    public static string PsTimerPanel => CustomPalette?.PsTimerPanel ?? "#101f1a";
    public static string MapControlPanel => CustomPalette?.MapControlPanel ?? "#121a24";
    public static string PcFree => CustomPalette?.PcFree ?? "#2f8cff";
    public static string PcLoggedIn => CustomPalette?.PcLoggedIn ?? "#17c964";
    public static string PcSession => CustomPalette?.PcSession ?? "#8b5cf6";
    public static string PcCleaned => CustomPalette?.PcCleaned ?? "#17c964";
    public static string PcService => CustomPalette?.PcService ?? "#f5a524";
    public static string PcShellOff => CustomPalette?.PcShellOff ?? "#ff4d4f";
    public static string PcConnectionLost => CustomPalette?.PcConnectionLost ?? "#ff3b4d";
    public static string PcOffline => CustomPalette?.PcOffline ?? "#667085";
    public static string BackgroundImage => CustomPalette?.BackgroundImage ?? "";
    public static double BackgroundOpacity => Math.Clamp(CustomPalette?.BackgroundOpacity ?? 0, 0, 1);
    public static double PanelOpacity => Math.Clamp(CustomPalette?.PanelOpacity ?? 1, 0.08, 1);
    public static double PcCardOpacity => CustomPalette is { PcCardOpacity: >= 0 and <= 1 } palette
        ? palette.PcCardOpacity
        : HasDragonMotif ? 224d / 255 : 46d / 255;
    public static bool HasBackdrop => !string.IsNullOrWhiteSpace(BackgroundImage);
    public static string ActivePaletteName => Settings.ActivePaletteName;
    public static string ActiveMotif => CustomPalette?.Motif
                                        ?? Settings.SavedPalettes.FirstOrDefault(x => x.Name.Equals(Settings.ActivePaletteName, StringComparison.OrdinalIgnoreCase))?.Palette.Motif
                                        ?? "";
    public static bool HasDragonMotif =>
        string.Equals(ActivePaletteName, "Родион", StringComparison.OrdinalIgnoreCase)
        && string.Equals(ActiveMotif, "dragon", StringComparison.OrdinalIgnoreCase);
    public static bool HasSpiderMotif => string.Equals(ActiveMotif, "spider", StringComparison.OrdinalIgnoreCase);
    public static bool HasCatMotif => string.Equals(ActiveMotif, "cat", StringComparison.OrdinalIgnoreCase);

    public static void Select(string key)
    {
        if (Options.All(x => x.Key != key)) return;
        CurrentKey = key;
        Settings.Theme = key;
        Save();
    }

    public static void SetCustomPalette(UiCustomPalette palette)
    {
        Settings.Palette = palette;
        Save();
    }

    public static void ActivateNamedPalette(string name)
    {
        var saved = Settings.SavedPalettes.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (saved == null) return;
        Settings.ActivePaletteName = saved.Name;
        Settings.Palette = saved.Palette;
        Save();
    }

    public static void PreviewCustomPalette(UiCustomPalette? palette)
    {
        Settings.Palette = palette;
    }

    public static void ResetCustomPalette()
    {
        Settings.Palette = null;
        Settings.ActivePaletteName = "";
        Save();
    }

    public static void SaveNamedPalette(string name, UiCustomPalette palette)
    {
        name = name.Trim();
        if (name.Length == 0) return;
        var index = Settings.SavedPalettes.FindIndex(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var motif = index >= 0 && !string.IsNullOrWhiteSpace(Settings.SavedPalettes[index].Palette.Motif)
            ? Settings.SavedPalettes[index].Palette.Motif
            : DefaultMotif(name);
        palette = palette with { Motif = motif };
        var saved = new UiSavedPalette(name, palette);
        if (index >= 0)
            Settings.SavedPalettes[index] = saved;
        else
            Settings.SavedPalettes.Add(saved);
        Settings.ActivePaletteName = name;
        Settings.Palette = palette;
        Save();
    }

    public static void DeleteNamedPalette(string name)
    {
        Settings.SavedPalettes.RemoveAll(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (Settings.ActivePaletteName.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            Settings.ActivePaletteName = "";
            Settings.Palette = null;
        }
        Save();
    }

    public static string Resolve(string hex)
    {
        var key = hex.ToLowerInvariant();
        var semanticPanel = key switch
        {
            "#151c25" => DashboardPanel,
            "#121a25" => ShopPanel,
            "#101f1b" => PsTimerPanel,
            "#121b25" => MapControlPanel,
            _ => null
        };
        if (semanticPanel != null) return semanticPanel;

        if (CustomPalette is { } palette)
        {
            var custom = key switch
            {
                "#080d12" => palette.Background,
                "#0d1217" => Mix(palette.Surface, palette.Background, 0.45),
                "#111820" => palette.Surface,
                "#151d28" or "#161c23" => Mix(palette.Text, palette.Surface, 0.06),
                "#182231" => Mix(palette.Text, palette.Surface, 0.10),
                "#263244" => palette.Border,
                "#2b3550" => Mix(palette.Text, palette.Border, 0.12),
                "#334052" => Mix(palette.Text, palette.Border, 0.20),
                "#f5f7ff" => palette.Text,
                "#a9b2c5" => Mix(palette.Text, palette.Surface, 0.76),
                "#8f9aad" => Mix(palette.Text, palette.Surface, 0.62),
                "#778399" => Mix(palette.Text, palette.Surface, 0.48),
                "#c9d3e6" => Mix(palette.Text, palette.Surface, 0.86),
                _ => null
            };
            if (custom != null) return custom;
            if (key.Equals(palette.PcFree, StringComparison.OrdinalIgnoreCase) ||
                key.Equals(palette.PcLoggedIn, StringComparison.OrdinalIgnoreCase) ||
                key.Equals(palette.PcSession, StringComparison.OrdinalIgnoreCase) ||
                key.Equals(palette.PcCleaned, StringComparison.OrdinalIgnoreCase) ||
                key.Equals(palette.PcService, StringComparison.OrdinalIgnoreCase) ||
                key.Equals(palette.PcShellOff, StringComparison.OrdinalIgnoreCase) ||
                key.Equals(palette.PcConnectionLost, StringComparison.OrdinalIgnoreCase) ||
                key.Equals(palette.PcOffline, StringComparison.OrdinalIgnoreCase))
                return key;
            if (HasDragonMotif) return Dragonize(key, palette);
        }
        return Current.Colors.TryGetValue(key, out var replacement) ? replacement : key;
    }

    private static string Dragonize(string key, UiCustomPalette palette)
    {
        if (key.Length != 7 || key[0] != '#') return key;
        try
        {
            var r = Convert.ToInt32(key.Substring(1, 2), 16);
            var g = Convert.ToInt32(key.Substring(3, 2), 16);
            var b = Convert.ToInt32(key.Substring(5, 2), 16);
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            if (max - min < 38) return key;
            if (b >= g + 20 && r >= g + 15) return key;
            var brightness = (r + g + b) / 765d;
            return brightness switch
            {
                > 0.72 => Mix("#ffffff", palette.Accent, 0.48),
                < 0.28 => Mix(palette.Accent, palette.Background, 0.42),
                _ => palette.Accent
            };
        }
        catch
        {
            return key;
        }
    }

    private static UiPresetOption Preset(
        string key, string title, string library,
        string accent, string brightAccent, string deepAccent, string softAccent, string accentBorder,
        string window, string overlay, string surface, string surfaceRaised, string input,
        string border, string borderStrong, string divider,
        string text, string textSecondary, string textMuted, string textSubtle, string textSoft)
    {
        return new UiPresetOption(key, title, library, accent, brightAccent, deepAccent, softAccent, accentBorder,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["#080d12"] = window,
                ["#0d1217"] = overlay,
                ["#111820"] = surface,
                ["#151d28"] = surfaceRaised,
                ["#161c23"] = surfaceRaised,
                ["#182231"] = input,
                ["#263244"] = border,
                ["#2b3550"] = borderStrong,
                ["#334052"] = divider,
                ["#f5f7ff"] = text,
                ["#a9b2c5"] = textSecondary,
                ["#8f9aad"] = textMuted,
                ["#778399"] = textSubtle,
                ["#c9d3e6"] = textSoft
            });
    }

    private static AppearanceSettings LoadSettings()
    {
        try
        {
            var path = File.Exists(SettingsPath) ? SettingsPath : LegacyUiLabSettingsPath;
            var settings = File.Exists(path)
                ? JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(path)) ?? new AppearanceSettings()
                : new AppearanceSettings();
            var palettesChanged = EnsurePersonalPalettes(settings);
            if (string.IsNullOrWhiteSpace(settings.ActivePaletteName))
            {
                var matchingPalette = settings.Palette == null
                    ? null
                    : settings.SavedPalettes.FirstOrDefault(x => x.Palette == settings.Palette);
                matchingPalette ??= settings.SavedPalettes.FirstOrDefault(x =>
                    !string.IsNullOrWhiteSpace(settings.Motif) &&
                    x.Palette.Motif.Equals(settings.Motif, StringComparison.OrdinalIgnoreCase));
                settings.ActivePaletteName = matchingPalette?.Name ?? "";
            }
            if (palettesChanged)
                SaveSettings(settings);
            return settings;
        }
        catch
        {
            var settings = new AppearanceSettings();
            EnsurePersonalPalettes(settings);
            return settings;
        }
    }

    private static string DefaultMotif(string name)
    {
        if (name.Equals("Родион", StringComparison.OrdinalIgnoreCase)) return "dragon";
        if (name.Equals("Соня", StringComparison.OrdinalIgnoreCase)) return "spider";
        if (name.Equals("Артём", StringComparison.OrdinalIgnoreCase)) return "cat";
        if (name.Equals("Кирилл", StringComparison.OrdinalIgnoreCase)) return "cat";
        return "";
    }

    private static bool EnsurePersonalPalettes(AppearanceSettings settings)
    {
        var changed = false;
        var rodionPalette = new UiCustomPalette(
            Accent: "#e11d8d",
            Background: "#070208",
            Surface: "#150918",
            Border: "#6f1d58",
            Text: "#fff4fa",
            DashboardPanel: "#1b0a20",
            ShopPanel: "#18081c",
            PsTimerPanel: "#200a24",
            MapControlPanel: "#1c091f",
            PcFree: "#2f8cff",
            PcLoggedIn: "#17c978",
            PcSession: "#a855f7",
            PcCleaned: "#22c55e",
            PcService: "#f5a524",
            PcShellOff: "#ff4d4f",
            PcConnectionLost: "#ff1744",
            PcOffline: "#667085",
            Motif: "dragon",
            BackgroundImage: "avares://SmartShellCleaner.Avalonia/Assets/rodion-dragon-night-art.png",
            BackgroundOpacity: 0.22,
            PanelOpacity: 0.74);
        var rodionIndex = settings.SavedPalettes.FindIndex(x => x.Name.Equals("Родион", StringComparison.OrdinalIgnoreCase));
        var legacyRodionPalette = rodionIndex >= 0
                                  && settings.SavedPalettes[rodionIndex].Palette.Accent.Equals("#9D4EDD", StringComparison.OrdinalIgnoreCase)
                                  && settings.SavedPalettes[rodionIndex].Palette.Background.Equals("#07030D", StringComparison.OrdinalIgnoreCase);
        if (rodionIndex < 0 || legacyRodionPalette)
        {
            if (rodionIndex < 0)
                settings.SavedPalettes.Add(new UiSavedPalette("Родион", rodionPalette));
            else
                settings.SavedPalettes[rodionIndex] = new UiSavedPalette("Родион", rodionPalette);
            changed = true;
        }

        if (settings.AppearanceVersion < 1)
        {
            var migrationIndex = settings.SavedPalettes.FindIndex(x => x.Name.Equals("Родион", StringComparison.OrdinalIgnoreCase));
            if (migrationIndex >= 0)
            {
                var saved = settings.SavedPalettes[migrationIndex];
                var migrated = saved.Palette with
                {
                    BackgroundImage = rodionPalette.BackgroundImage,
                    BackgroundOpacity = rodionPalette.BackgroundOpacity,
                    PanelOpacity = rodionPalette.PanelOpacity
                };
                settings.SavedPalettes[migrationIndex] = saved with { Palette = migrated };
                if (settings.ActivePaletteName.Equals("Родион", StringComparison.OrdinalIgnoreCase))
                    settings.Palette = migrated;
            }
            settings.AppearanceVersion = 1;
            changed = true;
        }

        if (settings.ActivePaletteName.Equals("Родион", StringComparison.OrdinalIgnoreCase)
            && (settings.Palette is null
                || legacyRodionPalette
                || !settings.Palette.Motif.Equals("dragon", StringComparison.OrdinalIgnoreCase)))
        {
            settings.Palette = rodionPalette;
            changed = true;
        }

        var sonyaPalette = new UiCustomPalette(
            Accent: "#ff3d8d",
            Background: "#100a10",
            Surface: "#1c1219",
            Border: "#673149",
            Text: "#fff7fa",
            DashboardPanel: "#24151e",
            ShopPanel: "#20131b",
            PsTimerPanel: "#25151e",
            MapControlPanel: "#21141c",
            PcFree: "#3b9eff",
            PcLoggedIn: "#27c77c",
            PcSession: "#ff3d8d",
            PcCleaned: "#20c776",
            PcService: "#ff963f",
            PcShellOff: "#ff5368",
            PcConnectionLost: "#ff2f59",
            PcOffline: "#717486",
            Motif: "spider");
        var index = settings.SavedPalettes.FindIndex(x => x.Name.Equals("Соня", StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            settings.SavedPalettes.Add(new UiSavedPalette("Соня", sonyaPalette));
            changed = true;
        }
        else if (!settings.SavedPalettes[index].Palette.Motif.Equals("spider", StringComparison.OrdinalIgnoreCase))
        {
            settings.SavedPalettes[index] = new UiSavedPalette("Соня", sonyaPalette);
            changed = true;
        }

        if (settings.ActivePaletteName.Equals("Соня", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(settings.Palette?.Motif, "spider", StringComparison.OrdinalIgnoreCase))
        {
            settings.Palette = sonyaPalette;
            changed = true;
        }

        var artemPalette = new UiCustomPalette(
            Accent: "#ff1f5a",
            Background: "#0e0024",
            Surface: "#1a0b30",
            Border: "#68224d",
            Text: "#fff7fb",
            DashboardPanel: "#211038",
            ShopPanel: "#1c0b31",
            PsTimerPanel: "#24103b",
            MapControlPanel: "#200d36",
            PcFree: "#369cff",
            PcLoggedIn: "#25c77b",
            PcSession: "#ff1f5a",
            PcCleaned: "#1fc879",
            PcService: "#ffad3d",
            PcShellOff: "#ff4b5f",
            PcConnectionLost: "#ff174d",
            PcOffline: "#747080",
            Motif: "cat");
        var artemIndex = settings.SavedPalettes.FindIndex(x => x.Name.Equals("Артём", StringComparison.OrdinalIgnoreCase));
        if (artemIndex < 0)
        {
            settings.SavedPalettes.Add(new UiSavedPalette("Артём", artemPalette));
            changed = true;
        }
        else if (!settings.SavedPalettes[artemIndex].Palette.Motif.Equals("cat", StringComparison.OrdinalIgnoreCase))
        {
            settings.SavedPalettes[artemIndex] = new UiSavedPalette("Артём", artemPalette);
            changed = true;
        }

        if (settings.ActivePaletteName.Equals("Артём", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(settings.Palette?.Motif, "cat", StringComparison.OrdinalIgnoreCase))
        {
            settings.Palette = artemPalette;
            changed = true;
        }

        var kirillPalette = new UiCustomPalette(
            Accent: "#2f8cff",
            Background: "#06101f",
            Surface: "#0d1b2e",
            Border: "#22558a",
            Text: "#f4f9ff",
            DashboardPanel: "#10243d",
            ShopPanel: "#0d2037",
            PsTimerPanel: "#112a46",
            MapControlPanel: "#0e233c",
            PcFree: "#36a3ff",
            PcLoggedIn: "#22c77a",
            PcSession: "#7c6cf2",
            PcCleaned: "#20c777",
            PcService: "#f5a524",
            PcShellOff: "#ff5368",
            PcConnectionLost: "#ff3155",
            PcOffline: "#66778f",
            Motif: "cat");
        var kirillIndex = settings.SavedPalettes.FindIndex(x => x.Name.Equals("Кирилл", StringComparison.OrdinalIgnoreCase));
        if (kirillIndex < 0)
        {
            settings.SavedPalettes.Add(new UiSavedPalette("Кирилл", kirillPalette));
            changed = true;
        }
        else if (!settings.SavedPalettes[kirillIndex].Palette.Motif.Equals("cat", StringComparison.OrdinalIgnoreCase))
        {
            settings.SavedPalettes[kirillIndex] = new UiSavedPalette("Кирилл", kirillPalette);
            changed = true;
        }

        if (settings.ActivePaletteName.Equals("Кирилл", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(settings.Palette?.Motif, "cat", StringComparison.OrdinalIgnoreCase))
        {
            settings.Palette = kirillPalette;
            changed = true;
        }
        return changed;
    }

    private static string? ForcedKey()
    {
        var forced = Environment.GetEnvironmentVariable("SMARTSHELL_UI_THEME");
        return string.IsNullOrWhiteSpace(forced) ? null : forced;
    }

    private static void Save()
    {
        SaveSettings(Settings);
    }

    private static void SaveSettings(AppearanceSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings));
    }

    private static string Mix(string foreground, string background, double amount)
    {
        static (int R, int G, int B) Parse(string value)
        {
            var hex = value.TrimStart('#');
            return (Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex.Substring(2, 2), 16), Convert.ToInt32(hex.Substring(4, 2), 16));
        }

        var fg = Parse(foreground);
        var bg = Parse(background);
        var r = (int)Math.Round(bg.R + (fg.R - bg.R) * amount);
        var g = (int)Math.Round(bg.G + (fg.G - bg.G) * amount);
        var b = (int)Math.Round(bg.B + (fg.B - bg.B) * amount);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private sealed class AppearanceSettings
    {
        public string Theme { get; set; } = "violet";
        public UiCustomPalette? Palette { get; set; }
        public string ActivePaletteName { get; set; } = "";
        public string Motif { get; set; } = "";
        public List<UiSavedPalette> SavedPalettes { get; set; } = [];
        public int AppearanceVersion { get; set; }
    }
}
