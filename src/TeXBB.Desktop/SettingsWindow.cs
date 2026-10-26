using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TeXBB.Core.Tools;

namespace TeXBB.Desktop;

internal sealed class SettingsWindow : Window
{
    public SettingsWindow(DesktopSettings settings, string workspace)
    {
        Title = "Settings"; Width = 700; Height = 680; MinWidth = 520; MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(24) };
        var body = new StackPanel { Spacing = 18 };
        var dark = new CheckBox { Content = "Dark theme", IsChecked = settings.Dark };
        body.Children.Add(new TextBlock { Text = "Appearance", FontSize = 20, FontWeight = FontWeight.SemiBold });
        body.Children.Add(dark);
        var size = new NumericUpDown { Minimum = 10, Maximum = 32, Value = (decimal)settings.FontSize, Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
        body.Children.Add(new TextBlock { Text = "Editor font size" }); body.Children.Add(size);
        body.Children.Add(new TextBlock { Text = "Markdown · Math macros", FontSize = 20, FontWeight = FontWeight.SemiBold });
        var macros = new TextBox { Text = settings.MathMacros, AcceptsReturn = true, Height = 130,
            TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("DejaVu Sans Mono, monospace") };
        body.Children.Add(new TextBlock { Text = "JSON object, for example: {\"RR\":\"\\\\mathbb{R}\"}", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(macros);
        body.Children.Add(new TextBlock { Text = "Installed tools", FontSize = 20, FontWeight = FontWeight.SemiBold });
        var tools = new StackPanel { Spacing = 10 };
        void Scan()
        {
            tools.Children.Clear();
            foreach (var tool in ToolDiscovery.Scan())
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("100,*,Auto") };
                row.Children.Add(new TextBlock { Text = tool.Name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                var value = new TextBlock { Text = tool.ExecutablePath ?? "Setup needed", TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8) };
                Grid.SetColumn(value, 1); row.Children.Add(value);
                string? url = tool.Name switch { "typst" => "https://typst.app/", "pandoc" => "https://pandoc.org/index.html", _ => null };
                if (url != null)
                {
                    var homepage = new Button { Content = "Homepage", Padding = new Thickness(10, 6) };
                    homepage.Click += (_, _) => DesktopServices.OpenUri(new Uri(url));
                    Grid.SetColumn(homepage, 2); row.Children.Add(homepage);
                }
                tools.Children.Add(row);
            }
        }
        var refresh = new Button { Content = "⟳", HorizontalAlignment = HorizontalAlignment.Right };
        ToolTip.SetTip(refresh, "Scan again"); refresh.Click += (_, _) => Scan();
        body.Children.Add(refresh); body.Children.Add(tools); Scan();
        body.Children.Add(new TextBlock { Text = "Install TeX Live, Typst, Pandoc and Poppler with your Linux distribution's package tools. Then scan again.", TextWrapping = TextWrapping.Wrap, Opacity = .75 });
        root.Children.Add(new ScrollViewer { Content = body });
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 20, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 350 };
        footer.Children.Add(error);
        var cancel = new Button { Content = "Cancel" }; cancel.Click += (_, _) => Close(); footer.Children.Add(cancel);
        var save = new Button { Content = "Save", Background = Brush.Parse("#28745d"), Foreground = Brushes.White };
        save.Click += (_, _) =>
        {
            try
            {
                var proposed = new DesktopSettings { Dark = dark.IsChecked == true, FontSize = (double)(size.Value ?? 15), MathMacros = macros.Text ?? "{}" };
                proposed.Save(workspace);
                settings.Dark = proposed.Dark; settings.FontSize = proposed.FontSize; settings.MathMacros = proposed.MathMacros;
                Close();
            }
            catch (Exception exception) { error.Text = exception.Message; }
        };
        footer.Children.Add(save); Grid.SetRow(footer, 1); root.Children.Add(footer); Content = root;
    }
}
