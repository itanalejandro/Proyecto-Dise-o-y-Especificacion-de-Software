using System.Windows;
using System.Windows.Controls;
using Ftracker.Infrastructure;

namespace Ftracker.Desktop;
public sealed class AccessWindow : Window
{
    public AccessWindow(LocalStore store, bool setup)
    {
        Title = setup ? "Ftracker · Configuración inicial" : "Ftracker · Acceso";
        Icon = Brand.Icon;
        Style = (Style)FindResource(typeof(Window));
        Width = 520; SizeToContent = SizeToContent.Height; MaxHeight = SystemParameters.WorkArea.Height - 30; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel();
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Border { Margin = new Thickness(24), Padding = new Thickness(28), Background = ThemeManager.Get("Surface"), CornerRadius = new CornerRadius(12), Child = panel } };
        panel.Children.Add(Brand.Header(32));
        panel.Children.Add(new TextBlock { Text = setup ? "Empecemos con tu cuenta" : "Bienvenido de nuevo", FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 5) });
        panel.Children.Add(new TextBlock { Text = setup ? "Esta cuenta administrará usuarios, rutas y rondines en este equipo." : "Usa tu correo o nombre completo y la contraseña. Si el nombre está repetido, entra con tu correo.", TextWrapping = TextWrapping.Wrap, Foreground = ThemeManager.Get("Muted"), Margin = new Thickness(0, 0, 0, 22) });
        var name = new TextBox(); if (setup) { panel.Children.Add(new TextBlock { Text = "Nombre completo" }); panel.Children.Add(name); }
        panel.Children.Add(new TextBlock { Text = setup ? "Correo" : "Correo o nombre completo" }); var email = new TextBox(); panel.Children.Add(email);
        System.Windows.Automation.AutomationProperties.SetName(email, setup ? "Correo" : "Correo o nombre completo");
        panel.Children.Add(new TextBlock { Text = setup ? "Contraseña (mínimo 12 caracteres)" : "Contraseña" }); var password = new PasswordBox(); panel.Children.Add(password);
        var confirmation = new PasswordBox();
        if (setup) { panel.Children.Add(new TextBlock { Text = "Confirma tu contraseña" }); panel.Children.Add(confirmation); }
        var caps = new TextBlock { Text = "Bloq Mayús está activado", Foreground = System.Windows.Media.Brushes.DarkGoldenrod, Visibility = Visibility.Collapsed };
        panel.Children.Add(caps); password.PreviewKeyUp += (_, _) => caps.Visibility = System.Windows.Input.Keyboard.IsKeyToggled(System.Windows.Input.Key.CapsLock) ? Visibility.Visible : Visibility.Collapsed;
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = ThemeManager.Get("Danger"), Margin = new Thickness(0, 4, 0, 8) }; panel.Children.Add(error);
        var submit = new Button { Content = setup ? "Crear cuenta y continuar" : "Entrar", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Stretch }; panel.Children.Add(submit);
        Loaded += (_, _) => { if (setup) name.Focus(); else email.Focus(); };
        submit.Click += async (_, _) =>
        {
            submit.IsEnabled = false; error.Text = ""; var label = submit.Content; submit.Content = "Un momento…";
            try
            {
                if (string.IsNullOrWhiteSpace(email.Text) || string.IsNullOrEmpty(password.Password)) throw new Exception("Escribe tu correo o nombre completo y contraseña para continuar.");
                if (setup && password.Password != confirmation.Password) throw new Exception("Las contraseñas no coinciden. Vuelve a confirmarlas.");
                if (setup) await store.SetupAsync(name.Text, email.Text, password.Password);
                await store.LoginAsync(email.Text, password.Password); DialogResult = true;
            }
            catch (Exception ex) { error.Text = ex.Message; }
            finally { submit.IsEnabled = true; submit.Content = label; }
        };
    }
}
