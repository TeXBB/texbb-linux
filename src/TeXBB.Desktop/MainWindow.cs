using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Search;
using TeXBB.Core.Documents;
using TeXBB.Core.Preview;
using TeXBB.Core.Tools;

namespace TeXBB.Desktop;

public sealed class MainWindow : Window
{
    private WorkspaceStore workspace;
    private DesktopSettings settings;
    private readonly Dictionary<string, DocumentBuffer> buffers = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly ObservableCollection<DocumentFile> documents = [];
    private readonly ListBox documentList = new() { Margin = new Thickness(8) };
    private readonly TextBox filter = new() { PlaceholderText = "Find a document…", Margin = new Thickness(12, 10) };
    private readonly TextEditor editor = new() { Name = "SourceEditor", ShowLineNumbers = true, FontSize = 15, WordWrap = false,
        FontFamily = new FontFamily("Cascadia Code, DejaVu Sans Mono, monospace"), Padding = new Thickness(10) };
    private readonly TextBlock status = new() { Margin = new Thickness(16, 6), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock documentTitle = new() { Margin = new Thickness(14, 10), FontWeight = FontWeight.SemiBold };
    private readonly ContentControl preview = new();
    private readonly TextBox log = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        FontFamily = FontFamily.Default, MinHeight = 100, MaxHeight = 220 };
    private readonly DispatcherTimer previewTimer = new() { Interval = TimeSpan.FromMilliseconds(550) };
    private readonly DispatcherTimer recoveryTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Grid shell = new() { RowDefinitions = new RowDefinitions("54,*,32") };
    private readonly Border sidebar = new();
    private readonly Border editorBorder = new();
    private readonly Border previewBorder = new();
    private readonly Button buildButton;
    private readonly Button cancelButton;
    private DocumentBuffer? current;
    private NativeWebView? browser;
    private Uri? previewUri;
    private string? mathJaxPath;
    private string? pdfPath;
    private int pdfPage = 1, pdfPages = 1, renderGeneration, previewGeneration;
    private Bitmap? pdfBitmap;
    private StackPanel? pdfNavigation;
    private CancellationTokenSource? buildCancellation, renderCancellation, pdfInfoCancellation;
    private bool loading, closingAllowed, closingDecision, closed;
    private string lastLog = "Ready.";

    public MainWindow(string workspacePath)
    {
        workspace = new WorkspaceStore(workspacePath);
        settings = DesktopSettings.Load(workspace.Root);
        Title = "TeXBB — Linux preview";
        Width = 1280; Height = 820; MinWidth = 860; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16, 9) };
        toolbar.Children.Add(new TextBlock { Text = "TeXBB", FontWeight = FontWeight.Bold, FontSize = 22,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) });
        var newButton = Button("+ New", () => { });
        var newMenu = new ContextMenu();
        foreach (DocumentKind kind in Enum.GetValues<DocumentKind>())
        {
            var item = new MenuItem { Header = kind == DocumentKind.Latex ? "LaTeX" : kind.ToString() };
            item.Click += async (_, _) => { try { await OpenDocumentAsync(workspace.CreateDocument(kind).File.Path); } catch (Exception error) { ShowError(error); } };
            newMenu.Items.Add(item);
        }
        newButton.Click += (_, _) => newMenu.Open(newButton);
        toolbar.Children.Add(newButton);
        toolbar.Children.Add(Button("Open", async () => await OpenFileAsync()));
        toolbar.Children.Add(Button("Save", () => SaveCurrent()));
        buildButton = Button("Build PDF", async () => await BuildAsync());
        toolbar.Children.Add(buildButton);
        cancelButton = Button("Cancel", () => buildCancellation?.Cancel());
        cancelButton.IsVisible = false;
        toolbar.Children.Add(cancelButton);
        toolbar.Children.Add(Button("Drawing", () => new DrawingWindow(workspace.Root).Show(this)));
        toolbar.Children.Add(Button("History", async () => await ShowHistoryAsync()));
        toolbar.Children.Add(Button("Settings", async () => await ShowSettingsAsync()));
        shell.Children.Add(toolbar);

        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*,5,*"), Margin = new Thickness(10, 0, 10, 0) };
        var side = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        side.Children.Add(filter);
        documentList.ItemsSource = documents;
        documentList.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>().Class(":selected"))
        {
            Setters = { new Setter(TemplatedControl.BackgroundProperty, Brush.Parse("#28745d")),
                new Setter(TemplatedControl.ForegroundProperty, Brushes.White) }
        });
        documentList.ItemTemplate = new FuncDataTemplate<DocumentFile>((file, _) => new TextBlock
        {
            Text = file == null ? "" : file.Name, Margin = new Thickness(6, 8), TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetRow(documentList, 1); side.Children.Add(documentList);
        var workspaceButton = Button("Open workspace…", async () => await ChooseWorkspaceAsync());
        workspaceButton.Margin = new Thickness(12); Grid.SetRow(workspaceButton, 2); side.Children.Add(workspaceButton);
        sidebar.Child = side; sidebar.CornerRadius = new CornerRadius(10); content.Children.Add(sidebar);
        var editing = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        editing.Children.Add(documentTitle);
        Grid.SetRow(editor, 1); editing.Children.Add(editor);
        var logExpander = new Expander { Header = "Build log", Content = log, Margin = new Thickness(8, 0, 8, 8) };
        Grid.SetRow(logExpander, 2); editing.Children.Add(logExpander);
        editorBorder.Child = editing; editorBorder.Margin = new Thickness(10, 0, 0, 0); editorBorder.CornerRadius = new CornerRadius(10);
        Grid.SetColumn(editorBorder, 1); content.Children.Add(editorBorder);
        var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch,
            ResizeDirection = GridResizeDirection.Columns, Background = Brushes.Transparent };
        Grid.SetColumn(splitter, 2); content.Children.Add(splitter);
        previewBorder.Child = preview; previewBorder.CornerRadius = new CornerRadius(10);
        Grid.SetColumn(previewBorder, 3); content.Children.Add(previewBorder);
        Grid.SetRow(content, 1); shell.Children.Add(content);
        status.PointerPressed += (_, _) => Guard(() => DesktopServices.OpenUri(new Uri(
            Path.GetDirectoryName(current?.File.Path) ?? workspace.Root)));
        status.Cursor = new Cursor(StandardCursorType.Hand);
        Grid.SetRow(status, 2); shell.Children.Add(status);
        Content = shell;
        SearchPanel.Install(editor);
        ApplyTheme();
        editor.TextChanged += (_, _) =>
        {
            if (loading || current == null) return;
            current.Text = editor.Text;
            InvalidatePreview();
            UpdateTitle(); previewTimer.Stop(); previewTimer.Start();
        };
        documentList.SelectionChanged += async (_, _) =>
        {
            if (!loading && documentList.SelectedItem is DocumentFile file) await OpenDocumentAsync(file.Path);
        };
        filter.TextChanged += (_, _) => RefreshDocumentList();
        previewTimer.Tick += async (_, _) => { previewTimer.Stop(); await UpdatePreviewAsync(); };
        recoveryTimer.Tick += (_, _) => Guard(SaveDrafts);
        AddHandler(KeyDownEvent, async (_, args) =>
        {
            if ((args.KeyModifiers & KeyModifiers.Control) == 0) return;
            if (args.Key == Key.S) { args.Handled = true; SaveCurrent(); }
            if (args.Key == Key.O) { args.Handled = true; await OpenFileAsync(); }
            if (args.Key == Key.Enter) { args.Handled = true; await BuildAsync(); }
        }, RoutingStrategies.Tunnel);
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            closed = true; InvalidatePreview();
            previewTimer.Stop(); recoveryTimer.Stop(); buildCancellation?.Cancel(); renderCancellation?.Cancel();
            preview.Content = null; pdfBitmap?.Dispose();
        };
        Opened += async (_, _) =>
        {
            try
            {
                RefreshDocumentList();
                await OpenDocumentAsync(documents.Count == 0 ? workspace.CreateDocument(DocumentKind.Markdown).File.Path : documents[0].Path);
                recoveryTimer.Start();
            }
            catch (Exception exception) { ShowError(exception); }
        };
    }

    private Button Button(string text, Action action)
    {
        var button = CreateButton(text);
        button.Click += (_, _) => Guard(action);
        return button;
    }

    private Button Button(string text, Func<Task> action)
    {
        var button = CreateButton(text);
        button.Click += async (_, _) => { try { await action(); } catch (Exception error) { ShowError(error); } };
        return button;
    }

    private Button CreateButton(string text) => new()
    {
        Content = text, Padding = new Thickness(12, 7), CornerRadius = new CornerRadius(7),
        Background = Brush.Parse(settings.Dark ? "#204d3a" : "#ddefe6"),
        Foreground = Brush.Parse(settings.Dark ? "#eaf5ee" : "#183b2b")
    };

    private void ApplyTheme()
    {
        RequestedThemeVariant = settings.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Background = Brush.Parse(settings.Dark ? "#081912" : "#edf5f0");
        Foreground = Brush.Parse(settings.Dark ? "#eaf5ee" : "#183b2b");
        foreach (var border in new[] { sidebar, editorBorder, previewBorder })
            border.Background = Brush.Parse(settings.Dark ? "#10261c" : "#ffffff");
        editor.Background = editorBorder.Background;
        documentList.Background = sidebar.Background;
        editor.Foreground = Foreground; editor.FontSize = settings.FontSize;
    }

    private void RefreshDocumentList()
    {
        string selectedPath = current?.File.Path ?? "";
        var files = workspace.ListDocuments().Concat(buffers.Values.Select(buffer => buffer.File))
            .DistinctBy(file => file.Path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Where(file => string.IsNullOrWhiteSpace(filter.Text) || file.Name.Contains(filter.Text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.Kind).ThenBy(file => file.Name).ToArray();
        loading = true;
        try
        {
            documents.Clear(); foreach (var file in files) documents.Add(file);
            documentList.SelectedItem = files.FirstOrDefault(file => file.Path == selectedPath);
        }
        finally { loading = false; }
    }

    private async Task OpenDocumentAsync(string path)
    {
        try
        {
            path = Path.GetFullPath(path);
            if (buffers.TryGetValue(path, out var cached)) { OpenBuffer(cached); return; }
            var buffer = workspace.OpenDocument(path);
            await OfferRecoveryAsync(buffer);
            if (!closed) OpenBuffer(buffer);
        }
        catch (Exception error) { ShowError(error); }
    }

    private void OpenBuffer(DocumentBuffer buffer)
    {
        SaveDrafts();
        if (buffers.TryGetValue(buffer.File.Path, out var cached)) buffer = cached;
        InvalidatePreview();
        current = buffer; buffers[buffer.File.Path] = buffer;
        loading = true;
        try { editor.Text = buffer.Text; editor.Document.UndoStack.ClearAll(); }
        finally { loading = false; }
        RefreshDocumentList(); UpdateTitle();
        previewTimer.Stop(); previewTimer.Start(); editor.Focus();
    }

    private void UpdateTitle()
    {
        documentTitle.Text = current == null ? "Open a document" : current.File.Name + (current.IsDirty ? "  • Unsaved" : "");
        Title = current == null ? "TeXBB — Linux preview" : $"{current.File.Name}{(current.IsDirty ? " *" : "")} — TeXBB";
        buildButton.IsEnabled = buildCancellation == null && current?.File.Kind is DocumentKind.Latex or DocumentKind.Typst;
        status.Text = current == null ? workspace.Root : $"{current.File.Kind}  |  {current.File.Path}";
    }

    private bool SaveCurrent()
    {
        if (current == null) return false;
        try { workspace.SaveDocument(current); UpdateTitle(); lastLog = "Saved " + current.File.Name; log.Text = lastLog; return true; }
        catch (Exception exception) { ShowError(exception); return false; }
    }

    private void SaveDrafts()
    {
        foreach (var buffer in buffers.Values.Where(buffer => buffer.IsDirty)) workspace.SaveRecovery(buffer);
    }

    private async Task OpenFileAsync()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open a TeXBB document", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Documents") { Patterns = ["*.md", "*.tex", "*.typ", "*.bib", "*.pdf"] }]
            });
            string? path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path == null) return;
            if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) await OpenPdfAsync(path);
            else await OpenDocumentAsync(path);
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private async Task ChooseWorkspaceAsync()
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a TeXBB workspace", AllowMultiple = false });
            string? path = folders.FirstOrDefault()?.TryGetLocalPath(); if (path == null) return;
            if (!await ResolveUnsavedAsync()) return;
            var nextWorkspace = new WorkspaceStore(path);
            var nextSettings = DesktopSettings.Load(nextWorkspace.Root);
            var nextFiles = nextWorkspace.ListDocuments();
            var first = nextFiles.Count == 0 ? nextWorkspace.CreateDocument(DocumentKind.Markdown) : nextWorkspace.OpenDocument(nextFiles[0].Path);
            buildCancellation?.Cancel(); renderCancellation?.Cancel();
            InvalidatePreview();
            workspace = nextWorkspace; settings = nextSettings;
            current = null; buffers.Clear(); mathJaxPath = null; pdfPath = null; filter.Text = "";
            ApplyTheme(); RefreshDocumentList();
            await OfferRecoveryAsync(first); OpenBuffer(first);
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private async Task OfferRecoveryAsync(DocumentBuffer target)
    {
        var draft = workspace.GetRecovery(target.File);
        if (draft == null || draft.Text == target.Text) return;
        string note = draft.MatchesCurrentFile ? "A newer unsaved draft is available." : "An unsaved draft is available. The file on disk has also changed; compare before saving.";
        string choice = await ChooseAsync("Recover document", note, "Keep file", "Load draft");
        if (choice == "Load draft") target.Text = draft.Text;
    }

    private void InvalidatePreview()
    {
        previewGeneration++; renderGeneration++;
        pdfInfoCancellation?.Cancel(); renderCancellation?.Cancel();
        pdfPath = null;
        if (pdfNavigation != null) { pdfNavigation.IsEnabled = false; pdfNavigation = null; }
    }

    private async Task UpdatePreviewAsync()
    {
        if (current == null) return;
        int generation = ++previewGeneration;
        DocumentBuffer target = current;
        string sourceText = target.Text, workspaceRoot = workspace.Root;
        bool dark = settings.Dark;
        string macros = settings.MathMacros;
        bool IsCurrent() => !closed && generation == previewGeneration && current == target && current.Text == sourceText && workspace.Root == workspaceRoot;
        try
        {
            if (target.File.Kind == DocumentKind.Markdown)
            {
                string cache = Path.Combine(workspaceRoot, "history", "linux-cache");
                string runtime = mathJaxPath ?? await Task.Run(() => DesktopServices.EnsureMathJax(cache));
                if (!IsCurrent()) return;
                mathJaxPath = runtime;
                string html = MarkdownRenderer.Render(sourceText, target.File.Path, dark, runtime, macros);
                if (App.Headless) { preview.Content = Message("Markdown preview", "MathJax and local images render in the desktop WebView."); return; }
                if (browser == null)
                {
                    browser = new NativeWebView();
                    browser.NavigationStarted += (_, args) =>
                    {
                        if (args.Request is not Uri request || request.AbsoluteUri == "about:blank") return;
                        if (previewUri != null && request.GetLeftPart(UriPartial.Path) == previewUri.GetLeftPart(UriPartial.Path)) return;
                        args.Cancel = true;
                        if (request.Scheme is "http" or "https" or "mailto") Guard(() => DesktopServices.OpenUri(request));
                        else if (request.IsFile && new[] { ".md", ".tex", ".typ", ".bib" }.Contains(Path.GetExtension(request.LocalPath).ToLowerInvariant()))
                            _ = OpenDocumentAsync(request.LocalPath);
                    };
                }
                preview.Content = browser;
                string path = Path.Combine(cache, "preview-" + Guid.NewGuid().ToString("N") + ".html");
                await File.WriteAllTextAsync(path, html, Encoding.UTF8);
                if (!IsCurrent()) { File.Delete(path); return; }
                Uri? previousPreview = previewUri;
                previewUri = new Uri(path);
                browser.Source = previewUri;
                if (previousPreview?.IsFile == true)
                {
                    try { File.Delete(previousPreview.LocalPath); } catch (IOException) { }
                }
            }
            else if (target.File.Kind == DocumentKind.BibTeX)
                preview.Content = Message("Reference library", "Edit and save your BibTeX library here. Entries remain compatible with the Windows version.");
            else
            {
                string existing = Path.ChangeExtension(target.File.Path, ".pdf");
                if (File.Exists(existing)) await OpenPdfAsync(existing, generation);
                else preview.Content = Message("PDF preview", "Build this document to preview it. Ctrl+Enter builds; Ctrl+S saves.");
            }
        }
        catch (Exception exception)
        {
            if (!IsCurrent()) return;
            preview.Content = Message("Preview unavailable", exception.Message);
            ShowError(exception);
        }
    }

    private async Task BuildAsync()
    {
        if (current == null || buildCancellation != null || current.File.Kind is not (DocumentKind.Latex or DocumentKind.Typst)) return;
        if (!SaveCurrent()) return;
        DocumentBuffer target = current;
        string expectedText = target.Text;
        string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(target.File.Path)))[..16];
        var cancellation = new CancellationTokenSource(); buildCancellation = cancellation;
        cancelButton.IsVisible = true; UpdateTitle(); log.Text = "Building " + target.File.Name + "…";
        try
        {
            var result = await DocumentBuildService.BuildAsync(target.File.Path,
                Path.Combine(workspace.Root, "history", "linux-cache", "build", identity), cancellation.Token);
            lastLog = result.ToolName + Environment.NewLine + result.Stdout + Environment.NewLine + result.Stderr;
            log.Text = lastLog;
            if (current == target && target.Text == expectedText) await OpenPdfAsync(result.PdfPath);
            else log.Text += "\nThe source changed during the build. Build again to update the preview.";
        }
        catch (OperationCanceledException) { log.Text = "Build canceled."; }
        catch (Exception exception)
        {
            if (exception is ToolExecutionException toolError) lastLog = toolError.Result.Stdout + "\n" + toolError.Result.Stderr;
            ShowError(exception);
        }
        finally
        {
            buildCancellation = null; cancellation.Dispose(); cancelButton.IsVisible = false; UpdateTitle();
        }
    }

    private async Task OpenPdfAsync(string path, int generation = -1)
    {
        if (closed) return;
        if (generation < 0)
        {
            // An explicit build result takes precedence over an earlier edit's debounce.
            previewTimer.Stop();
            InvalidatePreview(); generation = previewGeneration;
        }
        if (generation != previewGeneration) return;
        pdfInfoCancellation?.Cancel();
        var cancellation = new CancellationTokenSource(); pdfInfoCancellation = cancellation;
        try
        {
            int pages = await PdfService.GetPageCountAsync(path, cancellation.Token);
            if (closed || generation != previewGeneration || cancellation.IsCancellationRequested) return;
            pdfPath = path; pdfPage = 1; pdfPages = pages;
            await RenderPdfAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!closed && generation == previewGeneration) ShowError(error); }
        finally { if (pdfInfoCancellation == cancellation) pdfInfoCancellation = null; cancellation.Dispose(); }
    }

    private async Task RenderPdfAsync()
    {
        if (pdfPath == null) return;
        int generation = ++renderGeneration;
        int previewEpoch = previewGeneration;
        string source = pdfPath;
        renderCancellation?.Cancel();
        var cancellation = new CancellationTokenSource(); renderCancellation = cancellation;
        try
        {
            string png = await PdfService.RenderPageAsync(source, pdfPage,
                Path.Combine(workspace.Root, "history", "linux-cache", "pdf"), 1.5, cancellation.Token);
            if (closed || generation != renderGeneration || previewEpoch != previewGeneration || cancellation.IsCancellationRequested) { File.Delete(png); return; }
            var image = new Bitmap(png); pdfBitmap?.Dispose(); pdfBitmap = image;
            File.Delete(png);
            var panel = new DockPanel();
            var controls = CreatePdfNavigation(source, previewEpoch, pdfPage, pdfPages);
            pdfNavigation = controls;
            DockPanel.SetDock(controls, Dock.Top); panel.Children.Add(controls);
            panel.Children.Add(new ScrollViewer { Content = new Image { Source = image, Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Top }, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
            preview.Content = panel;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { if (!closed && generation == renderGeneration && previewEpoch == previewGeneration) { preview.Content = Message("PDF preview unavailable", exception.Message); ShowError(exception); } }
        finally { if (renderCancellation == cancellation) renderCancellation = null; cancellation.Dispose(); }
    }

    private StackPanel CreatePdfNavigation(string source, int previewEpoch, int page, int pages)
    {
        bool IsCurrent() => !closed && previewEpoch == previewGeneration && pdfPath == source;
        async Task GoToPageAsync(int requestedPage)
        {
            if (!IsCurrent()) return;
            pdfPage = Math.Clamp(requestedPage, 1, pdfPages);
            await RenderPdfAsync();
        }
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(10) };
        var previous = Button("‹", () => GoToPageAsync(page - 1));
        previous.IsEnabled = page > 1;
        controls.Children.Add(previous);
        controls.Children.Add(new TextBlock { Text = $"{page} / {pages}", VerticalAlignment = VerticalAlignment.Center });
        var next = Button("›", () => GoToPageAsync(page + 1));
        next.IsEnabled = page < pages;
        controls.Children.Add(next);
        controls.Children.Add(Button("Open PDF", () => { if (IsCurrent()) DesktopServices.OpenUri(new Uri(source)); }));
        return controls;
    }

    private async Task ShowHistoryAsync()
    {
        if (current == null) return;
        try
        {
            string result = workspace.SaveHistory(current);
            await ChooseAsync("History snapshot saved", result, "OK");
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private async Task ShowSettingsAsync()
    {
        var window = new SettingsWindow(settings, workspace.Root);
        await window.ShowDialog(this);
        InvalidatePreview();
        ApplyTheme(); await UpdatePreviewAsync();
    }

    private Control Message(string heading, string text) => new StackPanel
    {
        Margin = new Thickness(30), Spacing = 12, VerticalAlignment = VerticalAlignment.Center,
        Children = { new TextBlock { Text = heading, FontSize = 23, FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = .8 } }
    };

    private async void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (closingAllowed) return;
        if (!buffers.Values.Any(buffer => buffer.IsDirty)) return;
        args.Cancel = true;
        if (closingDecision) return;
        closingDecision = true;
        try { if (await ResolveUnsavedAsync()) { closingAllowed = true; Close(); } }
        finally { closingDecision = false; }
    }

    private async Task<bool> ResolveUnsavedAsync()
    {
        if (!buffers.Values.Any(buffer => buffer.IsDirty)) return true;
        string choice = await ChooseAsync("Unsaved documents", "Save your changes or keep recovery drafts to continue later.", "Cancel", "Keep drafts", "Save all");
        try
        {
            if (choice == "Cancel") return false;
            if (choice == "Save all") foreach (var buffer in buffers.Values.Where(buffer => buffer.IsDirty)) workspace.SaveDocument(buffer);
            else SaveDrafts();
            return true;
        }
        catch (Exception exception) { ShowError(exception); return false; }
    }

    private async Task<string> ChooseAsync(string title, string message, params string[] choices)
    {
        var window = new Window { Title = title, Width = 480, SizeToContent = SizeToContent.Height,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 20 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        foreach (string choice in choices) buttons.Children.Add(Button(choice, () => window.Close(choice)));
        panel.Children.Add(buttons); window.Content = panel;
        return await window.ShowDialog<string>(this) ?? choices[0];
    }

    private void Guard(Action action) { try { action(); } catch (Exception exception) { ShowError(exception); } }
    private void ShowError(Exception exception) { log.Text = exception.Message + "\n" + lastLog; status.Text = exception.Message; }
}
