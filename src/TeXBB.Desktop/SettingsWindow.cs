using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using TeXBB.Core.Tools;

namespace TeXBB.Desktop;

internal sealed class SettingsWindow : Window
{
    public SettingsWindow(DesktopSettings settings, string workspace)
    {
        Title = "Settings"; Width = 920; Height = 700; MinWidth = 760; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        RequestedThemeVariant = settings.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        IBrush background = Brush.Parse(settings.Dark ? "#0A1612" : "#F4F9F6");
        IBrush surface = Brush.Parse(settings.Dark ? "#0F1F19" : "#FFFFFF");
        IBrush subtle = Brush.Parse(settings.Dark ? "#152B22" : "#EAF4EF");
        IBrush border = Brush.Parse(settings.Dark ? "#2F4C3F" : "#C2D8CD");
        IBrush text = Brush.Parse(settings.Dark ? "#EEF7F2" : "#183027");
        IBrush muted = Brush.Parse(settings.Dark ? "#A4BEB1" : "#546E63");
        IBrush accent = Brush.Parse(settings.Dark ? "#3AAE7A" : "#0F6B4B");
        IBrush accentSoft = Brush.Parse(settings.Dark ? "#173D30" : "#DDEFE6");
        Background = background; Foreground = text; FontSize = 13;
        Resources["SystemAccentColor"] = settings.Dark ? Color.Parse("#3AAE7A") : Color.Parse("#0F6B4B");
        foreach (string name in new[] { "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3", "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3" })
            Resources[name] = Resources["SystemAccentColor"];

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("196,*"), RowDefinitions = new RowDefinitions("*,Auto") };
        var pages = new Dictionary<string, Control>();
        var content = new ContentControl { Background = surface, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch };
        Grid.SetColumn(content, 1); root.Children.Add(content);
        var navigation = new ListBox { Name = "SettingsNavigation", Background = Brushes.Transparent, Foreground = text,
            BorderThickness = new Thickness(0), Padding = new Thickness(0) };
        navigation.ItemTemplate = new FuncDataTemplate<string>((name, _) => new TextBlock
        {
            Text = name, Margin = new Thickness(12, 10), FontSize = 13, VerticalAlignment = VerticalAlignment.Center
        });
        navigation.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
        {
            Setters = { new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(5)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                new Setter(Layoutable.MarginProperty, new Thickness(0, 2)) }
        });
        navigation.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>().Class(":selected"))
        {
            Setters = { new Setter(TemplatedControl.BackgroundProperty, accentSoft),
                new Setter(TemplatedControl.ForegroundProperty, text) }
        });
        navigation.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>().Class(":selected").Template().OfType<ContentPresenter>())
        {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, accentSoft),
                new Setter(ContentPresenter.ForegroundProperty, text) }
        });
        var navigationBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(14, 24, 14, 18) };
        navigationBody.Children.Add(new TextBlock { Text = "Settings", FontSize = 21, FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(12, 0, 0, 22) });
        Grid.SetRow(navigation, 1); navigationBody.Children.Add(navigation);
        var workspaceNote = new TextBlock { Text = "TeXBB for Linux", Foreground = muted, FontSize = 12,
            Margin = new Thickness(12, 18, 0, 0) };
        Grid.SetRow(workspaceNote, 2); navigationBody.Children.Add(workspaceNote);
        root.Children.Add(new Border { Background = background, BorderBrush = border,
            BorderThickness = new Thickness(0, 0, 1, 0), Child = navigationBody });

        var error = new TextBlock { Name = "SettingsError", TextWrapping = TextWrapping.Wrap,
            Foreground = Brush.Parse(settings.Dark ? "#FFB7AA" : "#A52A21"), IsVisible = false };
        void ReportError(Exception exception)
        {
            error.Text = exception.Message; error.IsVisible = true;
        }
        Button ActionButton(string label, Action action)
        {
            var button = new Button { Content = label, Padding = new Thickness(14, 8), CornerRadius = new CornerRadius(5),
                Background = subtle, Foreground = text, BorderBrush = border, BorderThickness = new Thickness(1) };
            button.Click += (_, _) => { try { action(); } catch (Exception exception) { ReportError(exception); } };
            return button;
        }
        TextBlock Description(string value) => new() { Text = value, Foreground = muted, TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
        Border Card(Control child) => new() { Child = child, Background = subtle, BorderBrush = border,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(18) };
        StackPanel Page(string name, string description)
        {
            var body = new StackPanel { Spacing = 18, Margin = new Thickness(28, 24, 28, 28) };
            body.Children.Add(new TextBlock { Text = name, FontSize = 24, FontWeight = FontWeight.SemiBold });
            body.Children.Add(Description(description));
            pages.Add(name, new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            return body;
        }
        TextBlock SectionTitle(string title) => new() { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold };

        var general = Page("General", "Appearance and editor preferences for this workspace.");
        var dark = new CheckBox { Name = "SettingsDarkTheme", Content = "Dark theme", IsChecked = settings.Dark, Foreground = text };
        var appearance = new StackPanel { Spacing = 12 };
        appearance.Children.Add(SectionTitle("Appearance")); appearance.Children.Add(dark);
        appearance.Children.Add(Description("Use TeXBB's dark green workspace. Changes take effect when you save."));
        general.Children.Add(Card(appearance));
        var size = new NumericUpDown { Name = "SettingsFontSize", Minimum = 10, Maximum = 32, Value = (decimal)settings.FontSize,
            Width = 112, Background = surface, Foreground = text, BorderBrush = border,
            HorizontalAlignment = HorizontalAlignment.Right };
        var fontRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        fontRow.Children.Add(new TextBlock { Text = "Editor font size", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(size, 1); fontRow.Children.Add(size); general.Children.Add(Card(fontRow));
        var workspaceInfo = new StackPanel { Spacing = 10 };
        workspaceInfo.Children.Add(SectionTitle("Workspace")); workspaceInfo.Children.Add(Description(workspace));
        general.Children.Add(Card(workspaceInfo));

        var markdown = Page("Markdown", "Configure mathematical notation in the Markdown preview.");
        var macroBody = new StackPanel { Spacing = 12 };
        macroBody.Children.Add(SectionTitle("Math macros"));
        macroBody.Children.Add(Description("Enter a JSON object. These macros apply to Markdown equations."));
        macroBody.Children.Add(new TextBlock { Text = "Example: {\"RR\":\"\\\\mathbb{R}\"}", Foreground = muted,
            FontFamily = new FontFamily("DejaVu Sans Mono, monospace"), TextWrapping = TextWrapping.Wrap });
        var macros = new TextBox { Name = "SettingsMathMacros", Text = settings.MathMacros, AcceptsReturn = true,
            MinHeight = 220, TextWrapping = TextWrapping.NoWrap, FontSize = 13,
            FontFamily = new FontFamily("DejaVu Sans Mono, monospace"), Background = surface, Foreground = text,
            BorderBrush = border, Padding = new Thickness(12) };
        ScrollViewer.SetHorizontalScrollBarVisibility(macros, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(macros, ScrollBarVisibility.Auto);
        macroBody.Children.Add(macros); markdown.Children.Add(Card(macroBody));

        void ToolPage(string name, string description, string[] toolNames, string help, string? homepageUrl = null)
        {
            var page = Page(name, description);
            var cardBody = new StackPanel { Spacing = 16 };
            var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var title = SectionTitle("Setup"); title.VerticalAlignment = VerticalAlignment.Center; heading.Children.Add(title);
            var tools = new StackPanel { Spacing = 16 };
            void Scan()
            {
                var found = ToolDiscovery.Scan().ToDictionary(tool => tool.Name);
                tools.Children.Clear();
                foreach (string toolName in toolNames)
                {
                    ToolDescriptor tool = found[toolName];
                    var row = new StackPanel { Spacing = 5 };
                    var statusRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                    statusRow.Children.Add(new TextBlock { Text = toolName, FontWeight = FontWeight.SemiBold });
                    var availability = new TextBlock { Text = tool.IsAvailable ? "Ready" : "Not installed",
                        Foreground = tool.IsAvailable ? accent : muted, FontSize = 12 };
                    Grid.SetColumn(availability, 1); statusRow.Children.Add(availability); row.Children.Add(statusRow);
                    row.Children.Add(Description(tool.ExecutablePath ?? "Install this tool, then refresh to check again."));
                    tools.Children.Add(row);
                }
            }
            var refresh = ActionButton("⟳", Scan); ToolTip.SetTip(refresh, "Scan again");
            refresh.Name = "Refresh" + name; Grid.SetColumn(refresh, 1); heading.Children.Add(refresh);
            cardBody.Children.Add(heading); cardBody.Children.Add(tools);
            if (homepageUrl != null)
            {
                var homepage = ActionButton("Homepage", () => DesktopServices.OpenUri(new Uri(homepageUrl)));
                homepage.HorizontalAlignment = HorizontalAlignment.Left; cardBody.Children.Add(homepage);
            }
            page.Children.Add(Card(cardBody)); page.Children.Add(Description(help)); Scan();
        }
        ToolPage("LaTeX", "Build LaTeX documents and preview the generated PDF.", ["pdflatex", "latexmk", "tectonic"],
            "Install TeX Live or Tectonic using your Linux distribution's package tools. You only need the compiler used by your documents.",
            "https://tug.org/texlive/");
        ToolPage("Typst", "Build Typst documents and preview the generated PDF.", ["typst"],
            "Install Typst, then refresh to make it available to TeXBB.", "https://typst.app/");
        ToolPage("PDF", "Tools used to display PDF pages and read PDF information.", ["pdfinfo", "pdftoppm", "pdftotext"],
            "These tools are included in Poppler. On Ubuntu and Debian, install the poppler-utils package, then refresh.");

        navigation.ItemsSource = pages.Keys.ToArray();
        navigation.SelectionChanged += (_, _) =>
        {
            if (navigation.SelectedItem is string name) content.Content = pages[name];
        };
        navigation.SelectedIndex = 0;

        var footerBody = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(24, 14) };
        error.VerticalAlignment = VerticalAlignment.Center; error.Margin = new Thickness(0, 0, 20, 0);
        footerBody.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var cancel = ActionButton("Cancel", () => Close()); cancel.MinWidth = 86; actions.Children.Add(cancel);
        var save = ActionButton("Save", () =>
        {
            var proposed = new DesktopSettings { Dark = dark.IsChecked == true, FontSize = (double)(size.Value ?? 15), MathMacros = macros.Text ?? "{}" };
            try { proposed.Save(workspace); }
            catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidDataException)
            {
                navigation.SelectedItem = "Markdown"; macros.Focus(); throw;
            }
            settings.Dark = proposed.Dark; settings.FontSize = proposed.FontSize; settings.MathMacros = proposed.MathMacros;
            Close();
        });
        save.Name = "SaveSettings"; save.MinWidth = 86; save.Background = accent;
        save.Foreground = settings.Dark ? Brush.Parse("#08120F") : Brushes.White; save.BorderThickness = new Thickness(0);
        actions.Children.Add(save); Grid.SetColumn(actions, 1); footerBody.Children.Add(actions);
        var footer = new Border { Background = background, BorderBrush = border, BorderThickness = new Thickness(0, 1, 0, 0), Child = footerBody };
        Grid.SetRow(footer, 1); Grid.SetColumnSpan(footer, 2); root.Children.Add(footer);
        Content = root;
    }
}
