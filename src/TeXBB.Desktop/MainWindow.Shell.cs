using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using AvaloniaEdit.Document;
using AvaloniaEdit.Search;
using TeXBB.Core.Documents;

namespace TeXBB.Desktop;

public sealed partial class MainWindow
{
    private static readonly StringComparer FilePaths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly Dictionary<string, EditorState> editorStates = new(FilePaths);
    private readonly Dictionary<string, (string PdfPath, string SourceText)> lastBuilds = new(FilePaths);
    private readonly Dictionary<DocumentKind, string> lastSelectedByKind = [];
    private readonly Dictionary<DocumentKind, Button> modeButtons = [];
    private readonly StackPanel openTabStrip = new() { Orientation = Orientation.Horizontal, Spacing = 3 };
    private readonly Grid bodyGrid = new() { ColumnDefinitions = new ColumnDefinitions("236,*,6,*"), Margin = new Thickness(12, 8, 12, 8) };
    private readonly Grid logPanel = new() { RowDefinitions = new RowDefinitions("30,*"), IsVisible = false, Margin = new Thickness(12, 0, 12, 6), Height = 160 };
    private readonly TextBlock modeStatus = new() { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 10, 0) };
    private readonly TextBlock caretStatus = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) };
    private readonly TextBlock stateStatus = new() { Text = "Ready", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 12, 0), MaxWidth = 240, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock sidebarHeading = new() { Text = "Documents", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0) };
    private readonly Styles shellStyles = new();
    private readonly List<Border> shellSurfaces = [];
    private Button sidebarToggle = null!;
    private Button workspaceButton = null!;
    private Button addDocumentButton = null!;
    private SearchPanel searchPanel = null!;
    private DrawingWindow? drawingWindow;
    private DocumentKind selectedKind = DocumentKind.Markdown;
    private bool sidebarExpanded = true;

    private sealed class EditorState(TextDocument document)
    {
        public TextDocument Document { get; } = document;
        public int CaretOffset, SelectionStart, SelectionLength;
        public double HorizontalOffset, VerticalOffset;
    }

    private static string KindName(DocumentKind kind) => kind switch
    {
        DocumentKind.Latex => "LaTeX", DocumentKind.BibTeX => "Reference", _ => kind.ToString()
    };

    private IBrush ColorFor(string dark, string light) => Brush.Parse(settings.Dark ? dark : light);

    private Border ShellSurface(Control child, int row)
    {
        var border = new Border { Child = child, BorderThickness = new Thickness(0, 0, 0, 1) };
        shellSurfaces.Add(border); Grid.SetRow(border, row); shell.Children.Add(border);
        return border;
    }

    private void InitializeShell()
    {
        FontFamily = new FontFamily("Inter, Noto Sans, DejaVu Sans, sans-serif");
        FontSize = 13;
        Styles.Add(shellStyles);
        ShellSurface(CreateMainMenu(), 0);

        var modeRow = new Grid { Margin = new Thickness(8, 6) };
        var modes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var boardButton = Button("▧  Blackboard", ShowDrawing);
        boardButton.MinWidth = 122; modes.Children.Add(boardButton);
        foreach (var kind in new[] { DocumentKind.Markdown, DocumentKind.Latex, DocumentKind.Typst, DocumentKind.BibTeX })
        {
            var mode = Button(KindName(kind), () => SelectModeAsync(kind));
            mode.MinWidth = kind == DocumentKind.Markdown ? 116 : 94;
            mode.FontSize = 13; modeButtons.Add(kind, mode); modes.Children.Add(mode);
        }
        modeRow.Children.Add(modes);
        ShellSurface(modeRow, 1);

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 4) };
        toolbar.Children.Add(Button("↻  Reload", ReloadCurrentAsync));
        toolbar.Children.Add(Button("Save", () => SaveCurrent()));
        toolbar.Children.Add(Button("Save As…", SaveAsAsync));
        toolbar.Children.Add(Button("Open…", OpenFileAsync));
        toolbar.Children.Add(Button("Clear All", ClearDocumentAsync));
        buildButton = Button("↻  Preview", BuildOrRefreshAsync); toolbar.Children.Add(buildButton);
        cancelButton = Button("Cancel", () => buildCancellation?.Cancel()); cancelButton.IsVisible = false; toolbar.Children.Add(cancelButton);
        toolbar.Children.Add(new Border { Width = 1, Margin = new Thickness(3, 4), Background = ColorFor("#2F4C3F", "#C2D8CD") });
        var add = Button("+", NewDocumentAsync); add.Width = 32; ToolTip.SetTip(add, "New document (Ctrl+N)"); toolbar.Children.Add(add);
        toolbar.Children.Add(Button("History", ShowHistoryAsync));
        ShellSurface(new ScrollViewer { Content = toolbar, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }, 2);

        var side = new Grid { RowDefinitions = new RowDefinitions("38,Auto,*,Auto,Auto") };
        var sideHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,34") };
        sideHeader.Children.Add(sidebarHeading);
        sidebarToggle = Button("‹", ToggleSidebar); sidebarToggle.Width = 28; sidebarToggle.Height = 28;
        sidebarToggle.Padding = new Thickness(0); sidebarToggle.Margin = new Thickness(2, 4);
        ToolTip.SetTip(sidebarToggle, "Collapse documents");
        Grid.SetColumn(sidebarToggle, 1); sideHeader.Children.Add(sidebarToggle); side.Children.Add(sideHeader);
        filter.Margin = new Thickness(8, 4, 8, 8); filter.MinWidth = 0; filter.FontSize = 12;
        Grid.SetRow(filter, 1); side.Children.Add(filter);
        documentList.Margin = new Thickness(4, 0); documentList.ItemsSource = documents;
        documentList.ItemTemplate = new FuncDataTemplate<DocumentFile>((file, _) =>
        {
            var label = new TextBlock { Text = file?.Name ?? "", Margin = new Thickness(6, 8), TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 };
            if (file != null) ToolTip.SetTip(label, file.Path);
            return label;
        });
        Grid.SetRow(documentList, 2); side.Children.Add(documentList);
        addDocumentButton = Button("+  New " + KindName(selectedKind), NewDocumentAsync);
        addDocumentButton.Margin = new Thickness(8); Grid.SetRow(addDocumentButton, 3); side.Children.Add(addDocumentButton);
        workspaceButton = Button("Open workspace…", ChooseWorkspaceAsync);
        workspaceButton.Margin = new Thickness(8, 0, 8, 8); Grid.SetRow(workspaceButton, 4); side.Children.Add(workspaceButton);
        sidebar.Child = side; sidebar.BorderThickness = new Thickness(0, 0, 1, 0); bodyGrid.Children.Add(sidebar);

        var editing = new Grid { RowDefinitions = new RowDefinitions("40,*") };
        var tabRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,32") };
        var tabScroll = new ScrollViewer { Content = openTabStrip, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        tabRow.Children.Add(tabScroll);
        var newTab = Button("+", NewDocumentAsync); newTab.Width = 28; newTab.Padding = new Thickness(0); newTab.Margin = new Thickness(2, 3); newTab.MinHeight = 28;
        ToolTip.SetTip(newTab, "New document (Ctrl+N)"); Grid.SetColumn(newTab, 1); tabRow.Children.Add(newTab); editing.Children.Add(tabRow);
        editor.Margin = new Thickness(0); editor.Padding = new Thickness(10, 8); Grid.SetRow(editor, 1); editing.Children.Add(editor);
        editorBorder.Child = editing; editorBorder.Margin = new Thickness(8, 0, 0, 0); editorBorder.BorderThickness = new Thickness(1); editorBorder.CornerRadius = new CornerRadius(4);
        Grid.SetColumn(editorBorder, 1); bodyGrid.Children.Add(editorBorder);
        var splitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, ResizeDirection = GridResizeDirection.Columns, Background = Brushes.Transparent };
        Grid.SetColumn(splitter, 2); bodyGrid.Children.Add(splitter);
        var viewing = new Grid { RowDefinitions = new RowDefinitions("40,*") };
        documentTitle.Margin = new Thickness(12, 0); documentTitle.VerticalAlignment = VerticalAlignment.Center; documentTitle.FontSize = 12;
        documentTitle.TextTrimming = TextTrimming.CharacterEllipsis; viewing.Children.Add(documentTitle);
        Grid.SetRow(preview, 1); viewing.Children.Add(preview);
        preview.HorizontalContentAlignment = HorizontalAlignment.Stretch; preview.VerticalContentAlignment = VerticalAlignment.Stretch;
        previewBorder.Child = viewing; previewBorder.BorderThickness = new Thickness(1); previewBorder.CornerRadius = new CornerRadius(4);
        Grid.SetColumn(previewBorder, 3); bodyGrid.Children.Add(previewBorder);
        Grid.SetRow(bodyGrid, 3); shell.Children.Add(bodyGrid);

        var logHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,30") };
        logHeader.Children.Add(new TextBlock { Text = "Build log", Margin = new Thickness(8, 0), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var closeLog = Button("×", () => SetLogVisible(false)); closeLog.Width = 26; closeLog.Padding = new Thickness(0); closeLog.MinHeight = 24;
        Grid.SetColumn(closeLog, 1); logHeader.Children.Add(closeLog); logPanel.Children.Add(logHeader);
        log.MinHeight = 0; log.MaxHeight = double.PositiveInfinity; Grid.SetRow(log, 1); logPanel.Children.Add(log);
        Grid.SetRow(logPanel, 4); shell.Children.Add(logPanel);

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        footer.Children.Add(modeStatus);
        status.Margin = new Thickness(0); status.FontSize = 12; status.VerticalAlignment = VerticalAlignment.Center;
        status.Cursor = new Cursor(StandardCursorType.Hand);
        status.PointerPressed += (_, _) => Guard(() => DesktopServices.OpenUri(new Uri(Path.GetDirectoryName(current?.File.Path) ?? workspace.Root)));
        Grid.SetColumn(status, 1); footer.Children.Add(status);
        Grid.SetColumn(caretStatus, 2); footer.Children.Add(caretStatus);
        Grid.SetColumn(stateStatus, 3); footer.Children.Add(stateStatus); ShellSurface(footer, 5);
        editor.TextArea.Caret.PositionChanged += (_, _) => UpdateCaretStatus();
    }

    private MenuItem MenuAction(string text, Action action, string? gesture = null)
    {
        var item = new MenuItem { Header = text };
        if (gesture != null) item.InputGesture = KeyGesture.Parse(gesture);
        item.Click += (_, _) => Guard(action); return item;
    }

    private MenuItem MenuAction(string text, Func<Task> action, string? gesture = null)
    {
        var item = new MenuItem { Header = text };
        if (gesture != null) item.InputGesture = KeyGesture.Parse(gesture);
        item.Click += async (_, _) => { try { await action(); } catch (Exception error) { ShowError(error); } }; return item;
    }

    private Menu CreateMainMenu()
    {
        var menu = new Menu { Padding = new Thickness(6, 0), FontSize = 12 };
        var file = new MenuItem { Header = "_File" };
        var create = new MenuItem { Header = "New" };
        foreach (var kind in Enum.GetValues<DocumentKind>())
            create.Items.Add(MenuAction(KindName(kind), () => NewDocumentOfKindAsync(kind)));
        file.Items.Add(create);
        file.Items.Add(MenuAction("Open…", OpenFileAsync, "Ctrl+O"));
        file.Items.Add(MenuAction("Save", () => SaveCurrent(), "Ctrl+S"));
        file.Items.Add(MenuAction("Save As…", SaveAsAsync, "Ctrl+Shift+S"));
        file.Items.Add(MenuAction("Reload from disk", ReloadCurrentAsync));
        file.Items.Add(MenuAction("Close tab", async () => { if (current != null) await CloseDocumentAsync(current.File.Path); }, "Ctrl+W"));
        file.Items.Add(new Separator());
        file.Items.Add(MenuAction("Open workspace…", ChooseWorkspaceAsync));
        file.Items.Add(MenuAction("Save history snapshot", ShowHistoryAsync));
        file.Items.Add(MenuAction("Open history folder", () => DesktopServices.OpenUri(new Uri(Path.Combine(workspace.Root, "history")))));
        file.Items.Add(new Separator()); file.Items.Add(MenuAction("Exit", Close));
        menu.Items.Add(file);
        var edit = new MenuItem { Header = "_Edit" };
        edit.Items.Add(MenuAction("Undo", () => editor.Undo(), "Ctrl+Z"));
        edit.Items.Add(MenuAction("Redo", () => editor.Redo(), "Ctrl+Y"));
        edit.Items.Add(new Separator());
        edit.Items.Add(MenuAction("Cut", () => editor.Cut(), "Ctrl+X"));
        edit.Items.Add(MenuAction("Copy", () => editor.Copy(), "Ctrl+C"));
        edit.Items.Add(MenuAction("Paste", () => editor.Paste(), "Ctrl+V"));
        edit.Items.Add(MenuAction("Select All", () => editor.SelectAll(), "Ctrl+A"));
        edit.Items.Add(new Separator()); edit.Items.Add(MenuAction("Find…", ShowFind, "Ctrl+F")); menu.Items.Add(edit);
        var view = new MenuItem { Header = "_View" };
        view.Items.Add(MenuAction("Show / hide documents", ToggleSidebar));
        view.Items.Add(MenuAction("Show / hide build log", () => SetLogVisible(!logPanel.IsVisible)));
        view.Items.Add(MenuAction("Word wrap", () => editor.WordWrap = !editor.WordWrap));
        view.Items.Add(MenuAction("Line numbers", () => editor.ShowLineNumbers = !editor.ShowLineNumbers));
        view.Items.Add(MenuAction("Dark / light theme", async () => { settings.Dark = !settings.Dark; settings.Save(workspace.Root); ApplyTheme(); await UpdatePreviewAsync(); }));
        view.Items.Add(new Separator());
        view.Items.Add(MenuAction("Increase editor font", () => ChangeFontSize(1)));
        view.Items.Add(MenuAction("Decrease editor font", () => ChangeFontSize(-1))); menu.Items.Add(view);
        menu.Items.Add(MenuAction("_Settings", ShowSettingsAsync));
        var help = new MenuItem { Header = "_Help" };
        help.Items.Add(MenuAction("Keyboard shortcuts", async () => await ChooseAsync("Keyboard shortcuts", "Ctrl+N  New document\nCtrl+O  Open file\nCtrl+S  Save\nCtrl+Shift+S  Save As\nCtrl+W  Close tab\nCtrl+Tab / Ctrl+Shift+Tab  Switch tabs\nCtrl+F  Find\nCtrl+Enter  Preview / Build PDF\nCtrl+Z / Ctrl+Y  Undo / Redo", "OK")));
        help.Items.Add(MenuAction("About TeXBB", async () => await ChooseAsync("About TeXBB", "TeXBB for Linux\nFree desktop edition · 0.1.0 alpha\n\nBlackboard, Markdown, LaTeX, Typst and BibTeX editing.", "OK"))); menu.Items.Add(help);
        return menu;
    }

    private void ApplyTheme()
    {
        RequestedThemeVariant = settings.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Background = ColorFor("#0A1612", "#F4F9F6"); Foreground = ColorFor("#EEF7F2", "#183027");
        var surface = ColorFor("#0F1F19", "#FFFFFF"); var deep = ColorFor("#08120F", "#FFFFFF");
        var subtle = ColorFor("#152B22", "#EAF4EF"); var border = ColorFor("#2F4C3F", "#C2D8CD");
        var accent = ColorFor("#3AAE7A", "#0F6B4B"); var soft = ColorFor("#173D30", "#DDEFE6");
        Resources["SystemAccentColor"] = settings.Dark ? Color.Parse("#3AAE7A") : Color.Parse("#0F6B4B");
        foreach (string name in new[] { "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3", "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3" })
            Resources[name] = Resources["SystemAccentColor"];
        Resources["ButtonBackground"] = soft; Resources["ButtonBackgroundPointerOver"] = subtle;
        Resources["ButtonForeground"] = Foreground; Resources["ButtonBorderBrush"] = Brushes.Transparent;
        Resources["ListBoxItemSelectedBackground"] = soft; Resources["ListBoxItemSelectedPointerOverBackground"] = soft;
        shellStyles.Clear();
        shellStyles.Add(new Style(x => x.OfType<Button>().Class("shell-action"))
        { Setters = { new Setter(TemplatedControl.BackgroundProperty, soft), new Setter(TemplatedControl.ForegroundProperty, Foreground), new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)) } });
        shellStyles.Add(new Style(x => x.OfType<Button>().Class("shell-action").Class(":pointerover"))
        { Setters = { new Setter(TemplatedControl.BackgroundProperty, subtle) } });
        shellStyles.Add(new Style(x => x.OfType<ListBoxItem>().Class(":selected"))
        { Setters = { new Setter(TemplatedControl.BackgroundProperty, soft), new Setter(TemplatedControl.ForegroundProperty, Foreground) } });
        shellStyles.Add(new Style(x => x.OfType<ListBoxItem>().Class(":selected").Template().OfType<ContentPresenter>())
        { Setters = { new Setter(ContentPresenter.BackgroundProperty, soft) } });
        foreach (var outer in shellSurfaces) { outer.Background = surface; outer.BorderBrush = border; }
        sidebar.Background = deep; sidebar.BorderBrush = border;
        editorBorder.Background = previewBorder.Background = surface;
        editorBorder.BorderBrush = previewBorder.BorderBrush = border;
        editor.Background = settings.Dark ? Background : Brushes.White;
        editor.Foreground = Foreground; editor.FontSize = settings.FontSize;
        editor.LineNumbersForeground = ColorFor("#A4BEB1", "#546E63");
        if (current != null) editor.SyntaxHighlighting = EditorHighlighting.For(current.File.Kind, settings.Dark);
        documentList.Background = deep; documentList.Foreground = Foreground;
        filter.Background = subtle; filter.Foreground = Foreground; filter.BorderBrush = border;
        log.Background = deep; log.Foreground = Foreground; log.BorderBrush = border;
        status.Foreground = caretStatus.Foreground = ColorFor("#A4BEB1", "#546E63");
        UpdateModeButtons(); RefreshOpenTabs();
    }

    private void UpdateModeButtons()
    {
        foreach (var (kind, button) in modeButtons)
        {
            bool active = kind == selectedKind;
            button.Background = active ? ColorFor("#3AAE7A", "#0F6B4B") : ColorFor("#173D30", "#DDEFE6");
            button.Foreground = active ? (settings.Dark ? Brush.Parse("#08120F") : Brushes.White) : Foreground;
            button.FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal;
        }
        addDocumentButton.Content = "+  New " + KindName(selectedKind);
    }

    private void RefreshOpenTabs()
    {
        openTabStrip.Children.Clear();
        foreach (var buffer in buffers.Values.Where(value => value.File.Kind == selectedKind))
        {
            string path = buffer.File.Path;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
            var button = Button(buffer.File.Name + (buffer.IsDirty ? " •" : ""), () => OpenDocumentAsync(path));
            button.Padding = new Thickness(10, 5); button.MaxWidth = 210; button.MinHeight = 32;
            button.Content = new TextBlock { Text = buffer.File.Name + (buffer.IsDirty ? " •" : ""), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 180 };
            button.Background = ReferenceEquals(buffer, current) ? ColorFor("#173D30", "#DDEFE6") : ColorFor("#0F1F19", "#FFFFFF");
            button.Foreground = Foreground; ToolTip.SetTip(button, path); row.Children.Add(button);
            var close = Button("×", () => CloseDocumentAsync(path)); close.Padding = new Thickness(4, 0); close.Width = 24; close.MinHeight = 32;
            close.Background = button.Background; close.Foreground = Foreground; ToolTip.SetTip(close, "Close tab"); row.Children.Add(close);
            openTabStrip.Children.Add(new Border { Child = row, Margin = new Thickness(2, 3, 0, 2), BorderThickness = new Thickness(0, 0, 0, ReferenceEquals(buffer, current) ? 2 : 0), BorderBrush = ColorFor("#3AAE7A", "#0F6B4B") });
        }
    }

    private void ToggleSidebar()
    {
        sidebarExpanded = !sidebarExpanded;
        bodyGrid.ColumnDefinitions[0].Width = new GridLength(sidebarExpanded ? 236 : 36);
        sidebarHeading.IsVisible = filter.IsVisible = documentList.IsVisible = addDocumentButton.IsVisible = workspaceButton.IsVisible = sidebarExpanded;
        sidebarToggle.Content = sidebarExpanded ? "‹" : "›";
        ToolTip.SetTip(sidebarToggle, sidebarExpanded ? "Collapse documents" : "Show documents");
    }

    private void SetLogVisible(bool visible) => logPanel.IsVisible = visible;
    private void ShowFind() { editor.Focus(); searchPanel.Open(); }
    private void ChangeFontSize(int change) { settings.FontSize = Math.Clamp(settings.FontSize + change, 10, 32); settings.Save(workspace.Root); editor.FontSize = settings.FontSize; }
    private void UpdateCaretStatus() => caretStatus.Text = current == null ? "" : $"Ln {editor.TextArea.Caret.Line}, Col {editor.TextArea.Caret.Column}";

    private void RememberEditorState()
    {
        if (current == null || !editorStates.TryGetValue(current.File.Path, out var state)) return;
        state.CaretOffset = editor.CaretOffset; state.SelectionStart = editor.SelectionStart; state.SelectionLength = editor.SelectionLength;
        state.HorizontalOffset = editor.HorizontalOffset; state.VerticalOffset = editor.VerticalOffset;
    }

    private void ShowDrawing()
    {
        if (drawingWindow != null) { drawingWindow.Activate(); return; }
        var window = new DrawingWindow(workspace.Root, settings.Dark);
        drawingWindow = window;
        window.Closed += (_, _) => { if (ReferenceEquals(drawingWindow, window)) drawingWindow = null; };
        window.Show(this);
    }

    private async Task SelectModeAsync(DocumentKind kind)
    {
        if (lastSelectedByKind.TryGetValue(kind, out var last) && buffers.TryGetValue(last, out var remembered)) { OpenBuffer(remembered); return; }
        var opened = buffers.Values.LastOrDefault(value => value.File.Kind == kind);
        if (opened != null) { OpenBuffer(opened); return; }
        var file = workspace.ListDocuments().FirstOrDefault(value => value.Kind == kind);
        await OpenDocumentAsync(file?.Path ?? workspace.CreateDocument(kind).File.Path);
    }

    private Task NewDocumentAsync() => NewDocumentOfKindAsync(selectedKind);
    private Task NewDocumentOfKindAsync(DocumentKind kind) => OpenDocumentAsync(workspace.CreateDocument(kind).File.Path);
    private async Task BuildOrRefreshAsync()
    {
        if (current?.File.Kind == DocumentKind.Markdown) { previewTimer.Stop(); await UpdatePreviewAsync(); }
        else await BuildAsync();
    }

    private void CycleDocument(int direction)
    {
        var list = buffers.Values.Where(value => value.File.Kind == selectedKind).ToArray();
        if (list.Length == 0) return;
        int index = Array.FindIndex(list, value => ReferenceEquals(value, current));
        OpenBuffer(list[(index + direction + list.Length) % list.Length]);
    }

    private async Task ClearDocumentAsync()
    {
        if (current == null || editor.Document.TextLength == 0) return;
        if (await ChooseAsync("Clear document", "Clear the current document? You can restore the text with Undo.", "Cancel", "Clear") != "Clear") return;
        editor.Document.Remove(0, editor.Document.TextLength);
    }

    private async Task CloseDocumentAsync(string path)
    {
        if (!buffers.TryGetValue(path, out var target)) return;
        if (target.IsDirty)
        {
            string choice = await ChooseAsync("Unsaved document", "Save changes to " + target.File.Name + " before closing this tab?", "Cancel", "Keep draft", "Save");
            if (choice == "Cancel") return;
            if (choice == "Save") workspace.SaveDocument(target); else workspace.SaveRecovery(target);
        }
        bool wasCurrent = ReferenceEquals(current, target);
        if (wasCurrent) RememberEditorState();
        buffers.Remove(path); editorStates.Remove(path); lastBuilds.Remove(path);
        if (lastSelectedByKind.TryGetValue(target.File.Kind, out var last) && FilePaths.Equals(last, path)) lastSelectedByKind.Remove(target.File.Kind);
        if (wasCurrent)
        {
            current = null;
            var next = buffers.Values.LastOrDefault(value => value.File.Kind == selectedKind);
            if (next != null) { OpenBuffer(next); return; }
            InvalidatePreview(); previewTimer.Stop(); loading = true;
            try { editor.Document = new TextDocument(); editor.IsReadOnly = true; } finally { loading = false; }
            preview.Content = Message(KindName(selectedKind), "Choose a document on the left, or create one with +.");
        }
        RefreshDocumentList(); UpdateTitle();
    }

    private async Task ReloadCurrentAsync()
    {
        if (current == null) return;
        var old = current;
        if (old.IsDirty && await ChooseAsync("Reload document", "Reload " + old.File.Name + " from disk? Your current edits will be kept in a history snapshot.", "Cancel", "Reload") != "Reload") return;
        var replacement = workspace.OpenDocument(old.File.Path);
        if (old.IsDirty) workspace.SaveHistory(old);
        buffers.Remove(old.File.Path); editorStates.Remove(old.File.Path); lastBuilds.Remove(old.File.Path);
        OpenBuffer(replacement);
    }

    private async Task SaveAsAsync()
    {
        if (current == null) return;
        string extension = Path.GetExtension(current.File.Path);
        var file = await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Save document as", SuggestedFileName = current.File.Name, DefaultExtension = extension.TrimStart('.'), ShowOverwritePrompt = true,
            FileTypeChoices = [new Avalonia.Platform.Storage.FilePickerFileType(KindName(current.File.Kind)) { Patterns = ["*" + extension] }]
        });
        string? path = file?.TryGetLocalPath();
        if (path != null) SaveAsToPath(path);
    }

    private void SaveAsToPath(string destination)
    {
        if (current == null) return;
        var source = current;
        string path = Path.GetFullPath(destination);
        if (FilePaths.Equals(path, source.File.Path)) { SaveCurrent(); return; }
        if (!string.Equals(Path.GetExtension(path), Path.GetExtension(source.File.Path), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Keep the same file extension when saving a copy of this document.");
        if (buffers.TryGetValue(path, out var destinationBuffer) && destinationBuffer.IsDirty)
            throw new IOException("The destination is open with unsaved changes. Save or close that tab first.");
        DocumentBuffer saved;
        if (File.Exists(path))
        {
            saved = workspace.OpenDocument(path);
            saved.Text = source.Text;
            workspace.SaveDocument(saved);
        }
        else
        {
            // Exclusive creation cannot silently overwrite a file created after the picker closed.
            // Validate parent links before creating a new external file, as WorkspaceStore does on normal saves.
            for (DirectoryInfo? directory = new(Path.GetDirectoryName(path)!); directory != null; directory = directory.Parent)
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Choose a destination without symbolic links.");
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(false, true).GetBytes(source.Text);
                stream.Write(bytes); stream.Flush(true);
            }
            saved = workspace.OpenDocument(path);
        }
        RememberEditorState();
        if (source.IsDirty) workspace.SaveRecovery(source);
        editorStates.TryGetValue(source.File.Path, out var state);
        buffers.Remove(source.File.Path); buffers.Remove(path); editorStates.Remove(source.File.Path); editorStates.Remove(path);
        lastBuilds.Remove(source.File.Path); lastBuilds.Remove(path);
        if (state != null) editorStates[path] = state;
        current = null; OpenBuffer(saved);
        lastLog = "Saved " + saved.File.Path; log.Text = lastLog;
    }
}
