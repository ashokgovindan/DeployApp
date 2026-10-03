using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DeployApp.Themes
{
    /// <summary>
    /// Describes one selectable theme.
    /// </summary>
    public sealed class ThemeInfo
    {
        public ThemeInfo(string key, string displayName, bool isDark, string swatchHex)
        {
            Key = key;
            DisplayName = displayName;
            IsDark = isDark;
            SwatchBrush = (SolidColorBrush)new BrushConverter().ConvertFromString(swatchHex)!;
            SwatchBrush.Freeze();
        }

        public string Key { get; }
        public string DisplayName { get; }
        public bool IsDark { get; }

        /// <summary>Accent color shown next to the theme name in the picker.</summary>
        public SolidColorBrush SwatchBrush { get; }

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// Swaps the active color palette at runtime, remembers the choice between sessions,
    /// and colors the Windows title bar to match.
    /// </summary>
    public static class ThemeManager
    {
        private const string PaletteMarker = "Themes/Colors.";

        public static IReadOnlyList<ThemeInfo> Themes { get; } = new[]
        {
            new ThemeInfo("Light",    "Light",    false, "#0B63CE"),
            new ThemeInfo("Dark",     "Dark",     true,  "#4C9AFF"),
            new ThemeInfo("Midnight", "Midnight", true,  "#38BDF8"),
            new ThemeInfo("Sand",     "Sand",     false, "#9A5B2E"),
        };

        public static ThemeInfo Current { get; private set; } = Themes[0];

        public static event EventHandler? ThemeChanged;

        private static string SettingsPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "DeployApp", "theme.txt");

        /// <summary>
        /// Call once at startup, before any window is shown.
        /// </summary>
        public static void Initialize()
        {
            // Color each window's title bar as soon as it has a handle.
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((s, _) => { if (s is Window w) ApplyTitleBar(w); }));

            Apply(LoadSavedKey(), save: false);
        }

        /// <summary>
        /// Applies the theme with the given key. Unknown keys fall back to Light.
        /// </summary>
        public static void Apply(string? key, bool save = true)
        {
            var theme = Themes.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase))
                        ?? Themes[0];

            var app = Application.Current;
            if (app == null) return;

            var dictionaries = app.Resources.MergedDictionaries;
            var palette = new ResourceDictionary
            {
                Source = new Uri($"/DeployApp;component/Themes/Colors.{theme.Key}.xaml", UriKind.Relative)
            };

            var existing = dictionaries.FirstOrDefault(d =>
                d.Source != null && d.Source.OriginalString.Contains(PaletteMarker, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                dictionaries[dictionaries.IndexOf(existing)] = palette;
            else
                dictionaries.Insert(0, palette);

            Current = theme;

            foreach (Window window in app.Windows)
                ApplyTitleBar(window);

            if (save) SaveKey(theme.Key);

            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        #region Persistence

        private static string? LoadSavedKey()
        {
            try
            {
                return File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath).Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        private static void SaveKey(string key)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, key);
            }
            catch
            {
                // Saving the theme is a convenience. Ignore failures.
            }
        }

        #endregion

        #region Title bar (DWM)

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private const int DwmwaUseImmersiveDarkModeOld = 19; // Windows 10 1809 - 1909
        private const int DwmwaUseImmersiveDarkMode = 20;    // Windows 10 2004+ and Windows 11
        private const int DwmwaCaptionColor = 35;            // Windows 11
        private const int DwmwaTextColor = 36;               // Windows 11

        private static void ApplyTitleBar(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;

                int dark = Current.IsDark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeOld, ref dark, sizeof(int));

                // Windows 11 lets us match the caption to the window background exactly.
                if (TryGetColorRef("TitleBarBrush", out int caption))
                    DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref caption, sizeof(int));
                if (TryGetColorRef("TextPrimaryBrush", out int text))
                    DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref text, sizeof(int));
            }
            catch
            {
                // Older Windows versions: keep the default title bar.
            }
        }

        private static bool TryGetColorRef(string brushKey, out int colorRef)
        {
            colorRef = 0;
            if (Application.Current?.TryFindResource(brushKey) is not SolidColorBrush brush)
                return false;

            var c = brush.Color;
            colorRef = c.R | (c.G << 8) | (c.B << 16); // COLORREF is 0x00BBGGRR
            return true;
        }

        #endregion
    }
}
