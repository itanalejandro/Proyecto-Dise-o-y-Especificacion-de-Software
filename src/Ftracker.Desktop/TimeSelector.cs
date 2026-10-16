using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ftracker.Application;

namespace Ftracker.Desktop;

internal sealed class TimeSelector : UserControl
{
    private readonly TextBox input;
    private readonly TextBlock hint = new() { Foreground = ThemeManager.Get("Muted"), TextWrapping = TextWrapping.Wrap };
    private TimeOnly lastValid;
    private DateTime? selectedDate;
    public DateTime? SelectedDate { get => selectedDate; set { selectedDate = value; RefreshHint(); } }
    public TimeOnly Value => TryRead(out var time) ? time : throw new ReglaNegocioException("Escribe una hora válida en formato de 24 horas, por ejemplo 17:35, o selecciónala en el reloj.");

    public TimeSelector(TimeOnly initial)
    {
        lastValid = initial;
        var panel = new StackPanel();
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        input = new TextBox { Text = initial.ToString("HH:mm", CultureInfo.InvariantCulture), MaxLength = 5, Margin = new Thickness(0, 0, 8, 6), VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(input, "Hora exacta en formato de 24 horas");
        var open = new Button { Content = "Elegir en reloj", Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 0, 6) };
        AutomationProperties.SetName(open, "Abrir reloj para elegir la hora");
        Grid.SetColumn(open, 1); row.Children.Add(input); row.Children.Add(open); panel.Children.Add(row); panel.Children.Add(hint);
        Content = panel; Margin = new Thickness(0, 5, 0, 14);
        input.TextChanged += (_, _) => RefreshHint();
        open.Click += (_, _) =>
        {
            if (TryRead(out var current)) lastValid = current;
            var clock = new ClockTimeWindow(lastValid, SelectedDate) { Owner = Window.GetWindow(this) };
            if (clock.ShowDialog() == true) input.Text = clock.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
        };
        RefreshHint();
    }

    private bool TryRead(out TimeOnly time) => TimeOnly.TryParseExact(input.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    private void RefreshHint()
    {
        if (!TryRead(out var time)) { hint.Text = "Hora no válida. Usa HH:mm (00:00 a 23:59) o abre el reloj."; hint.Foreground = ThemeManager.Get("Danger"); return; }
        lastValid = time; hint.Foreground = ThemeManager.Get("Muted");
        hint.Text = ClockTimeWindow.FormatSelection(SelectedDate, time);
    }
}

internal enum ClockPart { Hours, Minutes }

internal sealed class ClockTimeWindow : Window
{
    private readonly ClockDial dial;
    private readonly Button hours;
    private readonly Button minutes;
    private readonly Button am;
    private readonly Button pm;
    private readonly TextBlock preview = new() { FontSize = 30, FontWeight = FontWeights.SemiBold, Foreground = ThemeManager.Get("Link"), Margin = new Thickness(0, 10, 0, 5) };
    private readonly TextBlock instruction = new() { TextWrapping = TextWrapping.Wrap, Foreground = ThemeManager.Get("Muted"), Margin = new Thickness(0, 6, 0, 12) };
    public TimeOnly Value => dial.Value;

    public ClockTimeWindow(TimeOnly initial, DateTime? date)
    {
        Title = "Elegir hora · Ftracker"; Icon = Brand.Icon; Style = (Style)FindResource(typeof(Window));
        Width = 700; Height = 550; MinWidth = 660; MinHeight = 530; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        var header = new TextBlock { Text = "Hora del recorrido", FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Cancelar", IsCancel = true, Style = (Style)FindResource("SecondaryButton") };
        cancel.Click += (_, _) => { DialogResult = false; };
        var accept = new Button { Content = "Usar esta hora", IsDefault = true };
        accept.Click += (_, _) => { DialogResult = true; };
        footer.Children.Add(cancel); footer.Children.Add(accept); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(body);
        dial = new ClockDial(initial) { Width = 300, Height = 300, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(dial);
        var side = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(side, 1); body.Children.Add(side);
        side.Children.Add(new TextBlock { Text = date?.ToString("dddd d 'de' MMMM 'de' yyyy", CultureInfo.GetCultureInfo("es-MX")) ?? "Selecciona la fecha en el formulario", TextWrapping = TextWrapping.Wrap, FontSize = 15 });
        side.Children.Add(preview);
        var periods = new WrapPanel(); am = Tool("a. m.", () => dial.SetValue(new TimeOnly(Value.Hour % 12, Value.Minute))); pm = Tool("p. m.", () => dial.SetValue(new TimeOnly(Value.Hour % 12 + 12, Value.Minute)));
        periods.Children.Add(am); periods.Children.Add(pm); side.Children.Add(periods);
        var parts = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) }; hours = Tool("Horas", () => dial.Part = ClockPart.Hours); minutes = Tool("Minutos", () => dial.Part = ClockPart.Minutes);
        parts.Children.Add(hours); parts.Children.Add(minutes); side.Children.Add(parts); side.Children.Add(instruction);
        var precise = new WrapPanel(); precise.Children.Add(Tool("−1 min", () => dial.SetValue(Value.AddMinutes(-1)))); precise.Children.Add(Tool("+1 min", () => dial.SetValue(Value.AddMinutes(1)))); side.Children.Add(precise);
        side.Children.Add(new TextBlock { Text = "También puedes usar las flechas del teclado sobre el reloj.", Foreground = ThemeManager.Get("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        dial.ValueChanged += Refresh; dial.PartChanged += Refresh; Loaded += (_, _) => dial.Focus(); Refresh();
    }

    internal static string FormatSelection(DateTime? date, TimeOnly time) => (date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha") + " · " + FormatTime(time);
    private static string FormatTime(TimeOnly time) => $"{(time.Hour % 12 == 0 ? 12 : time.Hour % 12)}:{time.Minute:00} {(time.Hour >= 12 ? "p. m." : "a. m.")}";
    private Button Tool(string title, Action action)
    {
        var button = new Button { Content = title, Style = (Style)FindResource("SecondaryButton"), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 6, 6) };
        button.Click += (_, _) => { action(); dial.Focus(); }; return button;
    }
    private void Refresh()
    {
        preview.Text = FormatTime(Value);
        hours.Background = dial.Part == ClockPart.Hours ? ThemeManager.Get("Selected") : ThemeManager.Get("Surface");
        minutes.Background = dial.Part == ClockPart.Minutes ? ThemeManager.Get("Selected") : ThemeManager.Get("Surface");
        am.Background = Value.Hour < 12 ? ThemeManager.Get("Selected") : ThemeManager.Get("Surface"); pm.Background = Value.Hour >= 12 ? ThemeManager.Get("Selected") : ThemeManager.Get("Surface");
        instruction.Text = dial.Part == ClockPart.Hours ? "Elige la hora o arrastra su manecilla. Después selecciona los minutos." : "Elige los minutos de 5 en 5 o arrastra su manecilla. Usa ±1 min para afinar.";
        AutomationProperties.SetHelpText(dial, instruction.Text + " Hora seleccionada: " + preview.Text);
    }
}

internal sealed class ClockDial : FrameworkElement
{
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x24, 0x5F, 0x9D));
    private ClockPart part;
    private bool dragging;
    public TimeOnly Value { get; private set; }
    public ClockPart Part { get => part; set { part = value; InvalidateVisual(); PartChanged?.Invoke(); } }
    public event Action? ValueChanged;
    public event Action? PartChanged;
    private Point Center => new(ActualWidth / 2, ActualHeight / 2);
    private double Radius => Math.Min(ActualWidth, ActualHeight) / 2 - 8;
    public ClockDial(TimeOnly initial)
    {
        Value = initial; Focusable = true; Cursor = Cursors.Hand;
        AutomationProperties.SetName(this, "Reloj interactivo de horas y minutos");
        AutomationProperties.SetHelpText(this, "Selecciona con el ratón o usa flechas; H elige horas y M minutos.");
    }
    public void SetValue(TimeOnly value) { Value = value; InvalidateVisual(); ValueChanged?.Invoke(); }
    private Point Position(double units, double length) { var angle = units * Math.PI * 2 - Math.PI / 2; return new Point(Center.X + Math.Cos(angle) * length, Center.Y + Math.Sin(angle) * length); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); var r = Radius; if (r <= 0) return;
        dc.DrawEllipse(ThemeManager.Get("Surface"), new Pen(IsKeyboardFocused ? Accent : Brushes.LightSlateGray, IsKeyboardFocused ? 3 : 1), Center, r, r);
        for (var tick = 0; tick < 60; tick++)
            dc.DrawLine(new Pen(tick % 5 == 0 ? ThemeManager.Get("Muted") : Brushes.LightGray, tick % 5 == 0 ? 2 : 1), Position(tick / 60d, r - 8), Position(tick / 60d, r - (tick % 5 == 0 ? 17 : 12)));
        var selected = Part == ClockPart.Hours ? Value.Hour % 12 : Value.Minute / 5d;
        for (var n = 0; n < 12; n++)
        {
            var pos = Position(n / 12d, r - 34); var active = Math.Abs(n - selected) < .01;
            if (active) dc.DrawEllipse(Accent, null, pos, 18, 18);
            var label = Part == ClockPart.Hours ? (n == 0 ? 12 : n).ToString() : (n * 5).ToString("00");
            var text = new FormattedText(label, CultureInfo.GetCultureInfo("es-MX"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), 17, active ? Brushes.White : ThemeManager.Get("Text"), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(pos.X - text.Width / 2, pos.Y - text.Height / 2));
        }
        DrawHand(dc, (Value.Hour % 12 + Value.Minute / 60d) / 12, r * .49, Part == ClockPart.Hours);
        DrawHand(dc, Value.Minute / 60d, r * .70, Part == ClockPart.Minutes);
        dc.DrawEllipse(Accent, null, Center, 7, 7);
    }
    private void DrawHand(DrawingContext dc, double units, double length, bool active)
    {
        var brush = active ? Accent : Brushes.LightSlateGray;
        dc.DrawLine(new Pen(brush, active ? 5 : 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Center, Position(units, length));
        dc.DrawEllipse(brush, null, Position(units, length), active ? 8 : 5, active ? 8 : 5);
    }
    // Shared by pointer events and deterministic tests; minutes snap to five-minute marks.
    internal bool SelectAt(Point point)
    {
        var delta = point - Center; if (delta.Length < 15 || delta.Length > Radius + 10) return false;
        var turns = (Math.Atan2(delta.Y, delta.X) + Math.PI / 2 + Math.PI * 2) / (Math.PI * 2) % 1;
        var index = (int)Math.Round(turns * 12, MidpointRounding.AwayFromZero) % 12;
        SetValue(Part == ClockPart.Hours ? new TimeOnly(index + (Value.Hour >= 12 ? 12 : 0), Value.Minute) : new TimeOnly(Value.Hour, index * 5));
        return true;
    }
    internal void FinishSelection() { if (Part == ClockPart.Hours) Part = ClockPart.Minutes; }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); if (!IsEnabled) return;
        Focus(); var point = e.GetPosition(this);
        var hourTip = Position((Value.Hour % 12 + Value.Minute / 60d) / 12, Radius * .49);
        var minuteTip = Position(Value.Minute / 60d, Radius * .70);
        if ((point - hourTip).Length < 15 && (point - hourTip).Length < (point - minuteTip).Length) Part = ClockPart.Hours;
        else if ((point - minuteTip).Length < 15) Part = ClockPart.Minutes;
        if (SelectAt(point)) { dragging = CaptureMouse(); e.Handled = true; }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); if (dragging && e.LeftButton == MouseButtonState.Pressed) { SelectAt(e.GetPosition(this)); e.Handled = true; }
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e); if (!dragging) return;
        SelectAt(e.GetPosition(this)); dragging = false; ReleaseMouseCapture(); FinishSelection(); e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { dragging = false; base.OnLostMouseCapture(e); }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.H) Part = ClockPart.Hours;
        else if (e.Key == Key.M) Part = ClockPart.Minutes;
        else if (e.Key is Key.Left or Key.Down or Key.Right or Key.Up)
        {
            var amount = e.Key is Key.Left or Key.Down ? -1 : 1;
            SetValue(Part == ClockPart.Hours ? Value.AddHours(amount) : Value.AddMinutes(amount));
        }
        else return;
        e.Handled = true;
    }
}
