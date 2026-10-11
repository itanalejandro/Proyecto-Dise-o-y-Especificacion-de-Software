using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ftracker.Desktop;

internal static class Brand
{
    public static ImageSource Icon => new BitmapImage(new Uri("pack://application:,,,/Ftracker;component/Assets/ftracker.ico"));

    public static FrameworkElement Header(double fontSize)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var logo = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/Ftracker;component/Assets/ftracker-logo.png")),
            Width = 48, Height = 48, Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 12, 0)
        };
        System.Windows.Automation.AutomationProperties.SetName(logo, "Logo de Ftracker");
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        panel.Children.Add(logo);
        panel.Children.Add(new TextBlock
        {
            Text = "Ftracker", FontSize = fontSize, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(18, 54, 90)),
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }
}
