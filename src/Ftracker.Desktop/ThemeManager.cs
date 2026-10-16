using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;
using System.ComponentModel;

namespace Ftracker.Desktop;

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
    private static string PreferencePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ftracker", "tema.txt");
    public static bool IsDark { get; private set; }
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
    public static void Initialize()
    {
        var dark = false;
        try { dark = File.ReadAllText(PreferencePath).Trim() == "oscuro"; }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        Apply(dark);
    }
    public static void Apply(bool dark)
    {
        IsDark = dark;
        var palette = new Dictionary<string, string[]>
        {
            ["Page"] = ["#F3F6FA", "#151C25"], ["Surface"] = ["#FFFFFF", "#202B38"],
            ["Text"] = ["#20354A", "#E5EDF5"], ["Muted"] = ["#52677D", "#AFBDCC"],
            ["Accent"] = ["#245F9D", "#315F92"], ["Link"] = ["#245F9D", "#9ACBFF"],
            ["Border"] = ["#C8D6E5", "#45566A"], ["Header"] = ["#EDF3F9", "#2B394A"],
            ["Alternate"] = ["#F8FAFD", "#253141"], ["Selected"] = ["#E7F0FB", "#304D6D"],
            ["Danger"] = ["#B3261E", "#FFAAA2"], ["DangerBorder"] = ["#E7B4B0", "#975E5A"]
        };
        foreach (var (key, colors) in palette)
        {
            var brush = (SolidColorBrush)Get(key);
            values[key].Color = (Color)ColorConverter.ConvertFromString(colors[dark ? 1 : 0]);
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
            try { Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!); File.WriteAllText(PreferencePath, IsDark ? "oscuro" : "claro"); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
        return button;
    }
}
