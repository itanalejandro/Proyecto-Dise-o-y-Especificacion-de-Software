using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Data;
using System.Windows.Input;
using System.ComponentModel;

namespace Ftracker.Desktop;

internal enum ThemePalette { Azul, Fime }

internal static class ThemeManager
{
    private static readonly Dictionary<string, SolidColorBrush> brushes = [];
    private static readonly Dictionary<string, ThemeColor> values = [];
    private sealed class ThemeColor : INotifyPropertyChanged
    {
        private Color color;
        public Color Color { get => color; set { color = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Color))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    private static readonly Dictionary<string, string[]> Common = new()
    {
        ["Danger"] = ["#B3261E", "#FFAAA2"], ["DangerBorder"] = ["#E7B4B0", "#975E5A"]
    };
    private static readonly Dictionary<string, string[]> Azul = new()
    {
        ["Page"] = ["#F3F6FA", "#151C25"], ["Surface"] = ["#FFFFFF", "#202B38"],
        ["Text"] = ["#20354A", "#E5EDF5"], ["Muted"] = ["#52677D", "#AFBDCC"],
        ["Accent"] = ["#245F9D", "#315F92"], ["AccentText"] = ["#FFFFFF", "#FFFFFF"],
        ["AccentBorder"] = ["#245F9D", "#9ACBFF"], ["Link"] = ["#245F9D", "#9ACBFF"],
        ["Border"] = ["#C8D6E5", "#45566A"], ["Header"] = ["#EDF3F9", "#2B394A"],
        ["Alternate"] = ["#F8FAFD", "#253141"], ["Selected"] = ["#E7F0FB", "#304D6D"]
    };
    // Base supplied for FIME: #53AD35, #34A32A, #006633, #313B48, #28313D.
    // Pale surfaces/links are derived supporting colors. Bright green needs dark text.
    private static readonly Dictionary<string, string[]> Fime = new()
    {
        ["Page"] = ["#F3F7F2", "#28313D"], ["Surface"] = ["#FFFFFF", "#313B48"],
        ["Text"] = ["#28313D", "#F5F8FA"], ["Muted"] = ["#313B48", "#CAD4DE"],
        ["Accent"] = ["#006633", "#53AD35"], ["AccentText"] = ["#FFFFFF", "#172019"],
        ["AccentBorder"] = ["#34A32A", "#34A32A"], ["Link"] = ["#006633", "#BFE6AF"],
        ["Border"] = ["#C7D6C0", "#617264"], ["Header"] = ["#E7F0E2", "#28313D"],
        ["Alternate"] = ["#F5F9F2", "#2C3642"], ["Selected"] = ["#DFEFD8", "#006633"]
    };
    private static string PreferencePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ftracker", "tema.txt");
    public static bool IsDark { get; private set; }
    public static ThemePalette Palette { get; private set; }
    public static Brush Get(string key)
    {
        if (!brushes.TryGetValue(key, out var brush))
        {
            values[key] = new ThemeColor();
            brushes[key] = brush = new SolidColorBrush();
            BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding(nameof(ThemeColor.Color)) { Source = values[key] });
        }
        return brush;
    }
    public static void Initialize() => LoadPreference(PreferencePath);
    internal static void LoadPreference(string path)
    {
        var dark = false; var palette = ThemePalette.Azul;
        try
        {
            // Accept old "claro"/"oscuro" files and the new "oscuro;fime" format.
            var parts = File.ReadAllText(path).Trim().Split(';');
            dark = parts[0] == "oscuro";
            if (parts.Length > 1 && parts[1] == "fime") palette = ThemePalette.Fime;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        Apply(dark, palette);
    }
    public static void Apply(bool dark) => Apply(dark, Palette);
    public static void Apply(bool dark, ThemePalette palette)
    {
        IsDark = dark; Palette = palette;
        foreach (var colors in new[] { Common, palette == ThemePalette.Fime ? Fime : Azul })
            foreach (var (key, pair) in colors)
            {
                var brush = (SolidColorBrush)Get(key);
                values[key].Color = (Color)ColorConverter.ConvertFromString(pair[dark ? 1 : 0]);
                System.Windows.Application.Current.Resources[key] = brush;
            }
        var resources = System.Windows.Application.Current.Resources;
        resources[SystemColors.WindowBrushKey] = Get("Surface");
        resources[SystemColors.WindowTextBrushKey] = Get("Text");
        resources[SystemColors.ControlBrushKey] = Get("Header");
        resources[SystemColors.ControlTextBrushKey] = Get("Text");
        resources[SystemColors.GrayTextBrushKey] = Get("Muted");
        resources[SystemColors.InactiveSelectionHighlightBrushKey] = Get("Selected");
        resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Get("Text");
        if (palette == ThemePalette.Fime)
        {
            resources[SystemColors.HighlightBrushKey] = Get("Accent");
            resources[SystemColors.HighlightTextBrushKey] = Get("AccentText");
        }
        else
        {
            resources.Remove(SystemColors.HighlightBrushKey);
            resources.Remove(SystemColors.HighlightTextBrushKey);
        }
    }
    internal static void SavePreference(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, (IsDark ? "oscuro" : "claro") + ";" + (Palette == ThemePalette.Fime ? "fime" : "azul"));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public static Button ToggleButton(bool persist = true)
    {
        var button = new Button { Style = (Style)System.Windows.Application.Current.FindResource("ThemeSwitch") };
        void Describe()
        {
            button.Content = IsDark ? "Oscuro" : "Claro";
            button.Tag = IsDark ? "Oscuro" : "Claro";
            button.ToolTip = IsDark ? "Cambiar a modo claro" : "Cambiar a modo oscuro";
            System.Windows.Automation.AutomationProperties.SetName(button, (string)button.ToolTip);
        }
        Describe();
        button.Click += (_, _) =>
        {
            Apply(!IsDark); Describe();
            if (!persist) return;
            SavePreference(PreferencePath);
        };
        return button;
    }

    public static FrameworkElement ThemeMenu(bool persist = true)
    {
        var host = new Grid { VerticalAlignment = VerticalAlignment.Center };
        var button = new Button { Content = "Tema ▾", Style = (Style)System.Windows.Application.Current.FindResource("SecondaryButton"), ToolTip = "Elegir modo y colores" };
        System.Windows.Automation.AutomationProperties.SetName(button, "Tema");
        var popup = new Popup { PlacementTarget = button, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, VerticalOffset = 2 };
        var panel = new StackPanel { Width = 244 };
        panel.Children.Add(SectionLabel("Modo"));
        var modeSwitch = ToggleButton(persist);
        modeSwitch.HorizontalAlignment = HorizontalAlignment.Left; modeSwitch.Margin = new Thickness(0, 4, 0, 14);
        panel.Children.Add(modeSwitch); panel.Children.Add(SectionLabel("Colores de la interfaz"));
        var options = new List<(Button Button, ThemePalette Palette)>();
        foreach (var (palette, label, colors) in new[] {
            (ThemePalette.Azul, "Azul", new[] { "#245F9D", "#9ACBFF" }),
            (ThemePalette.Fime, "FIME", new[] { "#53AD35", "#34A32A", "#006633", "#313B48", "#28313D" }) })
        {
            var option = PaletteOption(label, colors);
            option.Click += (_, _) => { Apply(IsDark, palette); Describe(); if (persist) SavePreference(PreferencePath); };
            options.Add((option, palette)); panel.Children.Add(option);
        }
        void Describe()
        {
            modeSwitch.Content = IsDark ? "Oscuro" : "Claro"; modeSwitch.Tag = modeSwitch.Content;
            modeSwitch.ToolTip = IsDark ? "Cambiar a modo claro" : "Cambiar a modo oscuro";
            System.Windows.Automation.AutomationProperties.SetName(modeSwitch, (string)modeSwitch.ToolTip);
            foreach (var (option, palette) in options)
            {
                var selected = palette == Palette;
                option.Background = selected ? Get("Selected") : Get("Surface");
                option.BorderBrush = selected ? Get("Link") : Get("Border"); option.BorderThickness = new Thickness(selected ? 2 : 1);
                System.Windows.Automation.AutomationProperties.SetName(option, (palette == ThemePalette.Fime ? "Paleta FIME" : "Paleta azul") + (selected ? ", seleccionada" : ""));
                if (option.Content is StackPanel row && row.Children[^1] is TextBlock check) check.Visibility = selected ? Visibility.Visible : Visibility.Hidden;
            }
        }
        modeSwitch.Click += (_, _) => Describe(); Describe();
        var frame = new Border { Background = Get("Surface"), BorderBrush = Get("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14), Margin = new Thickness(10), Child = panel,
            Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.28, Color = Colors.Black } };
        frame.KeyDown += (_, e) => { if (e.Key == Key.Escape) { popup.IsOpen = false; button.Focus(); e.Handled = true; } };
        popup.Child = frame;
        var closedAt = DateTime.MinValue;
        popup.Closed += (_, _) => closedAt = DateTime.UtcNow;
        popup.Opened += (_, _) => { Describe(); modeSwitch.Focus(); };
        button.Click += (_, _) => { if (!popup.IsOpen && DateTime.UtcNow - closedAt > TimeSpan.FromMilliseconds(250)) popup.IsOpen = true; };
        host.Children.Add(button); host.Children.Add(popup);
        return host;
    }
    private static TextBlock SectionLabel(string text) => new() { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Get("Muted") };
    private static Button PaletteOption(string label, string[] colors)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var color in colors)
            row.Children.Add(new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Fill = (Brush)new BrushConverter().ConvertFromString(color)!,
                Stroke = Get("Border"), StrokeThickness = 1, Margin = new Thickness(row.Children.Count == 0 ? 0 : -4, 0, 0, 0) });
        row.Children.Add(new TextBlock { Text = label, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Get("Text") });
        row.Children.Add(new TextBlock { Text = "✓", Foreground = Get("Link"), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        return new Button { Content = row, Style = (Style)System.Windows.Application.Current.FindResource("SecondaryButton"), Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(12, 8, 12, 8) };
    }
}
