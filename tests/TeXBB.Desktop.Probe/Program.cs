using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using TeXBB.Core.Documents;
using TeXBB.Desktop;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 1) { Console.Error.WriteLine("Usage: TeXBB.Desktop.Probe <fresh-directory-under-linux/.codex-tmp>"); return 2; }
        try
        {
            string root = Path.GetFullPath(args[0]);
            string linux = FindLinuxRoot();
            string relative = Path.GetRelativePath(Path.Combine(linux, ".codex-tmp"), root);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar) || relative == ".")
                throw new ArgumentException("Fixture must be a new child of linux/.codex-tmp.");
            if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Use a fresh fixture directory.");
            Directory.CreateDirectory(root);
            App.Headless = true;
            AppBuilder.Configure<App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
            Run(root);
            string report = $"PASS: {checks} headless UI checks. Screenshots: {root}\n" +
                "Skia headless on " + System.Runtime.InteropServices.RuntimeInformation.OSDescription +
                "; not native Linux, WebView, system clipboard, or IME composition coverage.\n";
            File.WriteAllText(Path.Combine(root, "results.txt"), report);
            Console.Write(report);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Run(string root)
    {
        CheckHighlighting();
        var store = new WorkspaceStore(Path.Combine(root, "workspace"));
        var first = store.CreateDocument(DocumentKind.Markdown);
        var recovery = store.CreateDocument(DocumentKind.Latex);
        string recoveryDisk = recovery.Text;
        recovery.Text = "Recovered draft — 복구 초안\n";
        store.SaveRecovery(recovery);
        var window = new MainWindow(store.Root);
        window.Show();
        PumpUntil(() => Field<DocumentBuffer?>(window, "current") != null, "initial document opens");
        var editor = Field<TextEditor>(window, "editor");
        Check(Field<DocumentBuffer>(window, "current").File.Path == first.File.Path, "non-first draft does not interrupt initial document");
        string text = "# Headless input\n한국어 αβ ∑ and emoji 🌱\nExact save.\n";
        editor.SelectAll(); editor.TextArea.Focus();
        window.KeyTextInput(text); Pump();
        Check(editor.Text == text, "Unicode and Korean text input reaches editor exactly");
        Check(Field<DocumentBuffer>(window, "current").IsDirty, "text input marks document dirty");
        window.KeyPress(Key.S, RawInputModifiers.Control, PhysicalKey.S, "s");
        window.KeyRelease(Key.S, RawInputModifiers.Control, PhysicalKey.S, "s"); Pump();
        Check(File.ReadAllText(first.File.Path) == text, "Ctrl+S persists exact typed text");
        Check(!Field<DocumentBuffer>(window, "current").IsDirty, "Ctrl+S clears dirty state");
        CheckDocumentSessions(window, store, first, text);

        Invoke(window, "OpenBuffer", store.CreateDocument(DocumentKind.BibTeX));
        editor.SelectAll(); editor.TextArea.Focus();
        const string bibliography = "@article{draft, title={Unsaved 한국어 bibliography}}\n";
        window.KeyTextInput(bibliography); Pump();
        var cachedBib = Field<DocumentBuffer>(window, "current");
        Invoke(window, "OpenBuffer", store.CreateDocument(DocumentKind.Markdown));
        Invoke(window, "OpenBuffer", store.CreateDocument(DocumentKind.BibTeX)); Pump();
        Check(ReferenceEquals(Field<DocumentBuffer>(window, "current"), cachedBib), "New BibTeX reuses dirty cached buffer");
        Check(editor.Text == bibliography && cachedBib.IsDirty, "dirty bibliography survives New and repeated BibTeX creation");
        Check((bool)Invoke(window, "SaveCurrent")!, "cached bibliography saves without prompt");

        var open = (Task)Invoke(window, "OpenDocumentAsync", recovery.File.Path)!;
        PumpUntil(() => window.OwnedWindows.Any(child => child.Title == "Recover document"), "non-first document recovery dialog appears");
        Check(!open.IsCompleted && ReferenceEquals(Field<DocumentBuffer>(window, "current"), cachedBib), "recovery decision precedes replacing the active editor");
        Check(File.ReadAllText(recovery.File.Path) == recoveryDisk, "recovery prompt preserves the disk source");
        var dialog = window.OwnedWindows.Single(child => child.Title == "Recover document");
        Capture(dialog, Path.Combine(root, "recover-document.png"));
        var loadDraft = dialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Load draft"));
        loadDraft.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => open.IsCompleted, "load draft completes document opening");
        open.GetAwaiter().GetResult();
        Check(editor.Text == recovery.Text && Field<DocumentBuffer>(window, "current").IsDirty, "Load draft displays recovery text as unsaved");
        Check((bool)Invoke(window, "SaveCurrent")!, "recovered draft saves");
        var previewReady = (Task)Invoke(window, "UpdatePreviewAsync")!;
        PumpUntil(() => previewReady.IsCompleted, "document preview placeholder settles before captures");
        previewReady.GetAwaiter().GetResult();

        Field<DispatcherTimer>(window, "previewTimer").Stop();
        string oldPdf = Path.Combine(root, "prior-document.pdf");
        int oldEpoch = Field<int>(window, "previewGeneration");
        SetField(window, "pdfPath", oldPdf); SetField(window, "pdfPage", 2); SetField(window, "pdfPages", 4);
        var oldNavigation = (StackPanel)Invoke(window, "CreatePdfNavigation", oldPdf, oldEpoch, 2, 4)!;
        SetField(window, "pdfNavigation", oldNavigation);
        using (var inspection = new CancellationTokenSource())
        {
            SetField(window, "pdfInfoCancellation", inspection);
            Invoke(window, "InvalidatePreview");
            Check(inspection.IsCancellationRequested, "changing previews cancels the active PDF metadata request");
            Check(!oldNavigation.IsEnabled && Field<string?>(window, "pdfPath") == null,
                "changing previews disables old PDF navigation and clears its active source");
            SetField(window, "pdfInfoCancellation", null);
        }
        int rendersBeforeOldClick = Field<int>(window, "renderGeneration");
        var oldNext = oldNavigation.Children.OfType<Button>().Single(button => Equals(button.Content, "›"));
        oldNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(Field<int>(window, "pdfPage") == 2 && Field<int>(window, "renderGeneration") == rendersBeforeOldClick,
            "an already queued old PDF page click cannot start a render in the new preview");
        SetField(window, "pdfPath", oldPdf);
        oldNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(Field<int>(window, "pdfPage") == 2 && Field<int>(window, "renderGeneration") == rendersBeforeOldClick,
            "old PDF controls remain stale even when the same PDF is opened in a later preview");
        var wrongSource = (StackPanel)Invoke(window, "CreatePdfNavigation", oldPdf + ".other", Field<int>(window, "previewGeneration"), 2, 4)!;
        wrongSource.Children.OfType<Button>().Single(button => Equals(button.Content, "›"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(Field<int>(window, "pdfPage") == 2 && Field<int>(window, "renderGeneration") == rendersBeforeOldClick,
            "PDF page controls are also bound to the exact source document");
        Invoke(window, "InvalidatePreview");

        var debounce = Field<DispatcherTimer>(window, "previewTimer");
        debounce.Start();
        var explicitPdf = (Task)Invoke(window, "OpenPdfAsync", oldPdf, -1)!;
        Check(!debounce.IsEnabled, "opening a build result cancels the earlier edit debounce");
        PumpUntil(() => explicitPdf.IsCompleted, "failed PDF opening also settles without a queued preview");
        explicitPdf.GetAwaiter().GetResult();
        Invoke(window, "UpdateTitle");

        CheckModeNavigation(window, first.File.Path);
        CheckSaveAsCloseAndReload(window, store, first, root);
        object settings = Field<object>(window, "settings");
        foreach (bool dark in new[] { true, false })
        foreach (int width in new[] { 860, 1280 })
        {
            settings.GetType().GetProperty("Dark")!.SetValue(settings, dark);
            Invoke(window, "ApplyTheme");
            window.Width = width; window.Height = 760; Pump();
            Check(window.ActualThemeVariant == (dark ? ThemeVariant.Dark : ThemeVariant.Light), "requested theme is applied");
            Check(editor.Bounds.Width > 120 && editor.Bounds.Height > 200, "editor remains usable after resize");
            var editorLocation = editor.TranslatePoint(default, window)!.Value;
            Check(editorLocation.X >= 0 && editorLocation.X + editor.Bounds.Width <= window.ClientSize.Width + 1,
                "editor remains within window after resize");
            CheckCoreLayout(window);
            Capture(window, Path.Combine(root, $"main-{(dark ? "dark" : "light")}-{width}.png"));
        }

        CheckSettings(window, settings, store.Root, root);
        var drawing = new DrawingWindow(store.Root);
        drawing.Show(window); Pump();
        var canvas = Field<Control>(drawing, "canvas");
        Check(canvas.Bounds.Width > 500 && canvas.Bounds.Height > 300, "drawing canvas receives available space");
        Point origin = canvas.TranslatePoint(default, drawing)!.Value;
        drawing.MouseDown(origin + new Vector(100, 100), MouseButton.Left);
        drawing.MouseMove(origin + new Vector(180, 150));
        drawing.MouseMove(origin + new Vector(260, 100));
        drawing.MouseUp(origin + new Vector(260, 100), MouseButton.Left); Pump();
        Check(Field<bool>(drawing, "dirty"), "pointer drag changes drawing");
        CheckDrawingHistory(drawing, canvas, origin);
        Capture(drawing, Path.Combine(root, "drawing-stroke.png"));
        Check((bool)Invoke(drawing, "SaveDrawing", false)!, "drawing saves without file picker");
        string strokePath = Path.Combine(store.Root, "current", "current_board.strokes.json");
        Check(File.Exists(strokePath) && File.ReadAllText(strokePath).Contains("strokes"), "saved drawing creates editable stroke document");
        CheckDrawingOpen(drawing, canvas, origin, strokePath, root);
        drawing.Close();
        Check((bool)Invoke(window, "SaveCurrent")!, "current document saved before closing");
        window.Close(); Pump();
        Check(!window.IsVisible && !drawing.IsVisible, "saved windows close without unsaved prompts");
        CheckOwnerDrawingClose(root);
    }

    private static void CheckHighlighting()
    {
        Type type = typeof(MainWindow).Assembly.GetType("TeXBB.Desktop.EditorHighlighting")!;
        MethodInfo create = type.GetMethod("For", BindingFlags.Static | BindingFlags.Public)!;
        foreach (bool dark in new[] { true, false })
        foreach (DocumentKind kind in Enum.GetValues<DocumentKind>())
        {
            var definition = (IHighlightingDefinition)create.Invoke(null, [kind, dark])!;
            string sample = kind switch
            {
                DocumentKind.Markdown => "# Highlighted heading\nAn **important** $x^2$ result.",
                DocumentKind.Latex => "\\section{Highlighted title}\n% Comment\n$x^2$",
                DocumentKind.Typst => "#let answer = 42\n= Highlighted title",
                _ => "@article{example,\n title = {Highlighted title},\n year = 2026\n}"
            };
            var document = new TextDocument(sample);
            using var highlighter = new DocumentHighlighter(document, definition);
            Check(highlighter.HighlightLine(1).Sections.Count > 0, $"{kind} {(dark ? "dark" : "light")} syntax colors a meaningful token");
            for (int line = 1; line <= document.LineCount; line++) highlighter.HighlightLine(line);
        }
    }

    private static void CheckDocumentSessions(MainWindow window, WorkspaceStore store, DocumentBuffer first, string savedText)
    {
        var editor = Field<TextEditor>(window, "editor");
        TextDocument firstDocument = editor.Document;
        const string suffix = "A local edit preserved across tabs.\n";
        editor.Document.Insert(editor.Document.TextLength, suffix); Pump();
        var second = store.CreateDocument(DocumentKind.Markdown);
        Invoke(window, "OpenBuffer", second);
        TextDocument secondDocument = editor.Document;
        const string secondSuffix = "An independent document edit.\n";
        editor.Document.Insert(editor.Document.TextLength, secondSuffix); Pump();
        Invoke(window, "OpenBuffer", first); Pump();
        Check(ReferenceEquals(editor.Document, firstDocument) && editor.Text == savedText + suffix,
            "switching tabs restores the same editor document and unsaved text");
        editor.Undo(); Pump();
        Check(editor.Text == savedText && !Field<DocumentBuffer>(window, "current").IsDirty,
            "Undo after a tab switch returns to the saved text and clears dirty state");
        editor.Redo(); Pump();
        Check(editor.Text == savedText + suffix && Field<DocumentBuffer>(window, "current").IsDirty,
            "Redo after a tab switch restores the unsaved edit");
        editor.Undo();
        Invoke(window, "OpenBuffer", second); Pump();
        Check(ReferenceEquals(editor.Document, secondDocument) && editor.Text.EndsWith(secondSuffix, StringComparison.Ordinal),
            "each open document retains its own text and undo history");
        editor.Undo(); Pump();
        Check(!Field<DocumentBuffer>(window, "current").IsDirty, "Undo in the second tab does not change the first tab");
        Invoke(window, "OpenBuffer", first); Pump();
        Check(editor.Text == savedText && File.ReadAllText(first.File.Path) == savedText,
            "tab switching and Undo leave the saved source unchanged");
    }

    private static void CheckModeNavigation(MainWindow window, string returnPath)
    {
        foreach (DocumentKind kind in Enum.GetValues<DocumentKind>())
        {
            var select = (Task)Invoke(window, "SelectModeAsync", kind)!;
            PumpUntil(() => select.IsCompleted, $"{kind} mode selection completes");
            select.GetAwaiter().GetResult();
            Check(Field<DocumentBuffer>(window, "current").File.Kind == kind, $"{kind} mode opens a matching document");
            var list = Field<ListBox>(window, "documentList");
            Check(list.Items.Cast<DocumentFile>().All(file => file.Kind == kind), $"{kind} sidebar filters to the selected mode");
            Check(Field<TextEditor>(window, "editor").SyntaxHighlighting != null, $"{kind} mode applies editor syntax colors");
        }
        var reopen = (Task)Invoke(window, "OpenDocumentAsync", returnPath)!;
        PumpUntil(() => reopen.IsCompleted, "mode test restores the first document");
        reopen.GetAwaiter().GetResult();
    }

    private static void CheckSaveAsCloseAndReload(MainWindow window, WorkspaceStore store, DocumentBuffer first, string root)
    {
        var editor = Field<TextEditor>(window, "editor");
        var source = store.CreateDocument(DocumentKind.Markdown);
        string sourceDisk = source.Text;
        Invoke(window, "OpenBuffer", source);
        editor.Document.Insert(editor.Document.TextLength, "Unsaved source for Save As.\n"); Pump();
        TextDocument sourceDocument = editor.Document;
        string copyText = editor.Text;
        var destination = store.CreateDocument(DocumentKind.Markdown);
        string destinationDisk = destination.Text;
        Invoke(window, "OpenBuffer", destination);
        editor.Document.Insert(editor.Document.TextLength, "Unsaved destination must survive.\n"); Pump();
        string destinationDraft = editor.Text;
        Invoke(window, "OpenBuffer", source);
        bool rejected = false;
        try { Invoke(window, "SaveAsToPath", destination.File.Path); }
        catch (TargetInvocationException error) when (error.InnerException is IOException) { rejected = true; }
        Check(rejected && ReferenceEquals(Field<DocumentBuffer>(window, "current"), source) && source.Text == copyText && destination.Text == destinationDraft &&
                File.ReadAllText(source.File.Path) == sourceDisk && File.ReadAllText(destination.File.Path) == destinationDisk,
            "Save As rejects a dirty open destination without changing either buffer or disk file");
        Invoke(window, "OpenBuffer", destination); editor.Undo(); Pump();
        Check(!destination.IsDirty, "destination cleanup uses its retained Undo history");
        Invoke(window, "OpenBuffer", source);
        string copy = Path.Combine(root, "save-as-copy.md");
        Invoke(window, "SaveAsToPath", copy); Pump();
        Check(File.ReadAllText(source.File.Path) == sourceDisk && File.ReadAllText(copy) == copyText &&
                Field<DocumentBuffer>(window, "current").File.Path == copy && !Field<DocumentBuffer>(window, "current").IsDirty,
            "Save As writes the unsaved source into a new active file while preserving the original");
        Check(store.GetRecovery(source.File)?.Text == copyText, "Save As preserves an unsaved original as a recovery draft");
        editor.Undo(); Pump();
        Check(ReferenceEquals(editor.Document, sourceDocument) && editor.Text == sourceDisk && Field<DocumentBuffer>(window, "current").IsDirty,
            "Save As keeps the editor Undo history under the new file identity");
        editor.Redo(); Pump();
        Check(editor.Text == copyText && !Field<DocumentBuffer>(window, "current").IsDirty,
            "Redo after Save As returns exactly to the new saved file");

        editor.Document.Insert(editor.Document.TextLength, "A closed tab recovery draft.\n"); Pump();
        var closingBuffer = Field<DocumentBuffer>(window, "current");
        string closedDraft = editor.Text;
        var cancelClose = (Task)Invoke(window, "CloseDocumentAsync", copy)!;
        AnswerDialog(window, "Unsaved document", "Cancel", cancelClose);
        Check(ReferenceEquals(Field<DocumentBuffer>(window, "current"), closingBuffer) && editor.Text == closedDraft && closingBuffer.IsDirty,
            "Cancel keeps a dirty tab open with all edits intact");
        var close = (Task)Invoke(window, "CloseDocumentAsync", copy)!;
        AnswerDialog(window, "Unsaved document", "Keep draft", close);
        Check(!Field<Dictionary<string, DocumentBuffer>>(window, "buffers").ContainsKey(copy) &&
                store.GetRecovery(closingBuffer.File)?.Text == closedDraft && File.ReadAllText(copy) == copyText,
            "Keep draft closes the tab without overwriting its file and preserves recoverable edits");
        var reopen = (Task)Invoke(window, "OpenDocumentAsync", copy)!;
        AnswerDialog(window, "Recover document", "Load draft", reopen);
        Check(editor.Text == closedDraft && Field<DocumentBuffer>(window, "current").IsDirty && File.ReadAllText(copy) == copyText,
            "reopening the closed tab offers and restores its unsaved draft");
        Check((bool)Invoke(window, "SaveCurrent")!, "recovered tab saves before Reload verification");

        editor.Document.Insert(editor.Document.TextLength, "Unsaved edits before Reload.\n"); Pump();
        string beforeReload = editor.Text;
        const string externalText = "# An external disk edit\n";
        File.WriteAllText(copy, externalText);
        string history = Path.Combine(store.Root, "history", "md");
        HashSet<string> beforeHistory = Directory.Exists(history) ? Directory.GetFiles(history).ToHashSet(StringComparer.Ordinal) : [];
        var reload = (Task)Invoke(window, "ReloadCurrentAsync")!;
        AnswerDialog(window, "Reload document", "Reload", reload);
        Check(editor.Text == externalText && !Field<DocumentBuffer>(window, "current").IsDirty && File.ReadAllText(copy) == externalText,
            "Reload shows the current disk version without changing that file");
        Check(Directory.GetFiles(history).Except(beforeHistory).Any(path => File.ReadAllText(path) == beforeReload),
            "Reload preserves the displaced unsaved edits in a history snapshot");
        Invoke(window, "OpenBuffer", first); Pump();
    }

    private static void AnswerDialog(MainWindow owner, string title, string answer, Task operation)
    {
        PumpUntil(() => owner.OwnedWindows.Any(window => window.Title == title), title + " decision appears");
        var dialog = owner.OwnedWindows.Single(window => window.Title == title);
        dialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, answer))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => operation.IsCompleted, title + " decision completes");
        operation.GetAwaiter().GetResult();
    }

    private static void CheckCoreLayout(MainWindow window)
    {
        var editor = Field<TextEditor>(window, "editor");
        var preview = Field<Border>(window, "previewBorder");
        var tabs = Field<StackPanel>(window, "openTabStrip");
        Check(preview.Bounds.Width > 120 && preview.Bounds.Height > 200, "preview remains usable at the current window size");
        Check(tabs.Bounds.Height >= 24 && tabs.Children.Count > 0, "open document tabs have visible controls");
        var sidebar = Field<Border>(window, "sidebar");
        double expandedWidth = sidebar.Bounds.Width, editorWidth = editor.Bounds.Width;
        Invoke(window, "ToggleSidebar"); Pump();
        Check(sidebar.Bounds.Width < expandedWidth && editor.Bounds.Width > editorWidth,
            "collapsing the sidebar gives horizontal space back to the editor");
        Invoke(window, "ToggleSidebar"); Pump();
        Check(Math.Abs(sidebar.Bounds.Width - expandedWidth) < 1, "expanding the sidebar restores its width");
        Point location = preview.TranslatePoint(default, window)!.Value;
        Check(location.X >= 0 && location.X + preview.Bounds.Width <= window.ClientSize.Width + 1,
            "preview remains inside the window after sidebar toggling");
    }

    private static void CheckSettings(MainWindow owner, object settings, string workspace, string root)
    {
        Type type = typeof(MainWindow).Assembly.GetType("TeXBB.Desktop.SettingsWindow")!;
        bool originalDark = Property<bool>(settings, "Dark");
        double originalSize = Property<double>(settings, "FontSize");
        string originalMacros = Property<string>(settings, "MathMacros");
        string path = Path.Combine(workspace, "history", "linux-settings.json");
        string? originalFile = File.Exists(path) ? File.ReadAllText(path) : null;
        var window = (Window)Activator.CreateInstance(type, settings, workspace)!;
        window.Show(owner); Pump();
        var navigation = Named<ListBox>(window, "SettingsNavigation");
        foreach (string page in navigation.Items.Cast<string>().ToArray())
        {
            navigation.SelectedItem = page; Pump();
            Check(window.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == page && block.FontSize >= 20 && block.Bounds.Width > 0),
                $"Settings {page} page is reachable and renders a heading");
            Check(Named<Button>(window, "SaveSettings").IsEffectivelyVisible, "settings save action remains visible while navigating");
        }
        navigation.SelectedItem = "General"; Pump();
        Named<CheckBox>(window, "SettingsDarkTheme").IsChecked = !originalDark;
        Named<NumericUpDown>(window, "SettingsFontSize").Value = 20;
        Capture(window, Path.Combine(root, "settings-general.png"));
        navigation.SelectedItem = "Markdown"; Pump();
        Named<TextBox>(window, "SettingsMathMacros").Text = "{ invalid JSON";
        navigation.SelectedItem = "General"; Pump();
        Named<Button>(window, "SaveSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(window.IsVisible && Equals(navigation.SelectedItem, "Markdown") && Named<TextBlock>(window, "SettingsError").IsVisible,
            "invalid macros keep settings open, select Markdown, and explain the error");
        Check(Property<bool>(settings, "Dark") == originalDark && Property<double>(settings, "FontSize") == originalSize &&
                Property<string>(settings, "MathMacros") == originalMacros,
            "a rejected settings save leaves live preferences unchanged");
        Check((File.Exists(path) ? File.ReadAllText(path) : null) == originalFile,
            "a rejected settings save leaves persisted preferences unchanged");
        Capture(window, Path.Combine(root, "settings-invalid-macros.png"));
        const string validMacros = "{\"RR\":\"\\\\mathbb{R}\"}";
        Named<TextBox>(window, "SettingsMathMacros").Text = validMacros;
        Named<Button>(window, "SaveSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(!window.IsVisible && Property<string>(settings, "MathMacros") == validMacros && Property<double>(settings, "FontSize") == 20,
            "valid settings save applies the preferences and closes the dialog");
        Check(File.Exists(path) && File.ReadAllText(path).Contains("MathMacros", StringComparison.Ordinal), "valid settings are persisted");
        object persisted = settings.GetType().GetMethod("Load", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, [workspace])!;
        var reloaded = (Window)Activator.CreateInstance(type, persisted, workspace)!;
        reloaded.Show(owner); Pump();
        Check(Named<NumericUpDown>(reloaded, "SettingsFontSize").Value == 20,
            "reopening settings retains the saved font size");
        reloaded.Close(); Pump();
        settings.GetType().GetProperty("Dark")!.SetValue(settings, originalDark);
        settings.GetType().GetProperty("FontSize")!.SetValue(settings, originalSize);
        settings.GetType().GetProperty("MathMacros")!.SetValue(settings, originalMacros);
        Invoke(owner, "ApplyTheme");
    }

    private static void CheckDrawingHistory(DrawingWindow drawing, Control canvas, Point origin)
    {
        int StrokeCount() => ((System.Collections.ICollection)canvas.GetType().GetProperty("Strokes")!.GetValue(canvas)!).Count;
        void Click(string label)
        {
            drawing.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, label))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        }
        Check(StrokeCount() == 1, "one pointer drag creates exactly one stroke");
        Click("Undo"); Check(StrokeCount() == 0 && Property<bool>(canvas, "CanRedo"), "drawing Undo removes the stroke and enables Redo");
        Click("Redo"); Check(StrokeCount() == 1, "drawing Redo restores the stroke");
        drawing.MouseDown(origin + new Vector(100, 250), MouseButton.Left);
        drawing.MouseMove(origin + new Vector(260, 250));
        drawing.MouseUp(origin + new Vector(260, 250), MouseButton.Left); Pump();
        Check(StrokeCount() == 2, "a second drag creates an independent stroke");
        Click("Eraser");
        drawing.MouseDown(origin + new Vector(180, 150), MouseButton.Left);
        drawing.MouseMove(origin + new Vector(180, 250));
        drawing.MouseUp(origin + new Vector(180, 250), MouseButton.Left); Pump();
        Check(StrokeCount() == 0, "one eraser drag can remove multiple intersected strokes");
        Click("Undo"); Check(StrokeCount() == 2, "one Undo restores the entire eraser drag");
        Click("Redo"); Check(StrokeCount() == 0, "Redo reapplies the entire eraser drag");
        Click("Undo"); Click("Pen");
        drawing.MouseDown(origin + new Vector(400, 200), MouseButton.Left);
        drawing.MouseMove(origin + new Vector(440, 220));
        drawing.MouseUp(origin + new Vector(440, 220), MouseButton.Left); Pump();
        Check(StrokeCount() == 3 && !Property<bool>(canvas, "CanRedo"), "drawing a new stroke after Undo discards the obsolete Redo branch");
    }

    private static void CheckOwnerDrawingClose(string root)
    {
        string workspace = Path.Combine(root, "owner-close-workspace");
        var owner = new MainWindow(workspace);
        owner.Show();
        PumpUntil(() => Field<DocumentBuffer?>(owner, "current") != null, "owner-close fixture opens its clean document");
        Invoke(owner, "ShowDrawing"); Pump();
        var drawing = Field<DrawingWindow>(owner, "drawingWindow");
        var canvas = Field<Control>(drawing, "canvas");
        Point origin = canvas.TranslatePoint(default, drawing)!.Value;
        Rect paper = (Rect)Invoke(canvas, "PaperBounds")!;
        Point start = origin + new Vector(paper.X + paper.Width / 3, paper.Y + paper.Height / 2);
        drawing.MouseDown(start, MouseButton.Left);
        drawing.MouseMove(start + new Vector(80, 20));
        drawing.MouseUp(start + new Vector(80, 20), MouseButton.Left); Pump();
        Check(Field<bool>(drawing, "dirty"), "owner-close fixture contains an unsaved drawing");

        owner.Close();
        PumpUntil(() => drawing.OwnedWindows.Any(window => window.Title == "Unsaved drawing"),
            "closing the owner waits for the drawing save decision");
        owner.Close(); Pump();
        Check(owner.IsVisible && drawing.IsVisible && drawing.OwnedWindows.Count(window => window.Title == "Unsaved drawing") == 1,
            "repeated owner close requests keep one pending drawing decision");
        var dialog = drawing.OwnedWindows.Single(window => window.Title == "Unsaved drawing");
        dialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Cancel"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => !Field<bool>(owner, "closingDecision"), "Cancel completes the pending owner close decision");
        string path = Path.Combine(workspace, "current", "current_board.strokes.json");
        Check(owner.IsVisible && drawing.IsVisible && Field<bool>(drawing, "dirty") && !File.Exists(path),
            "Cancel leaves the owner and unsaved drawing open without writing the board");

        owner.Close();
        PumpUntil(() => drawing.OwnedWindows.Any(window => window.Title == "Unsaved drawing"),
            "a later owner close presents the drawing decision again");
        dialog = drawing.OwnedWindows.Single(window => window.Title == "Unsaved drawing");
        dialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Save"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => !owner.IsVisible && !drawing.IsVisible, "Save closes both drawing and owner after persistence");
        using var saved = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        Check(saved.RootElement.GetProperty("strokes").GetArrayLength() == 1,
            "closing the owner with Save persists the unsaved stroke before closing");
    }

    private static void CheckDrawingOpen(DrawingWindow drawing, Control canvas, Point origin, string originalPath, string root)
    {
        string original = File.ReadAllText(originalPath);
        string copy = Path.Combine(root, "another-board.strokes.json");
        File.WriteAllText(copy, original);
        Check((bool)Invoke(drawing, "LoadDrawingFile", copy)!, "an existing editable board opens successfully");
        Check(Field<string>(drawing, "targetPath") == copy && !Field<bool>(drawing, "dirty") &&
                ((System.Collections.ICollection)Property<object>(canvas, "Strokes")).Count == 3,
            "opening a board restores its strokes and selects its own save target");
        drawing.MouseDown(origin + new Vector(500, 200), MouseButton.Left);
        drawing.MouseMove(origin + new Vector(540, 230));
        drawing.MouseUp(origin + new Vector(540, 230), MouseButton.Left); Pump();
        object strokes = Property<object>(canvas, "Strokes");
        string unsupported = Path.Combine(root, "unsupported-board.strokes.json");
        const string unsupportedJson = "{\"version\":1,\"canvasWidth\":3200,\"canvasHeight\":1600,\"mode\":\"editable\",\"images\":[{}],\"texts\":[],\"strokes\":[]}";
        File.WriteAllText(unsupported, unsupportedJson);
        Check(!(bool)Invoke(drawing, "LoadDrawingFile", unsupported)!, "unsupported board content is rejected before opening");
        Check(ReferenceEquals(Property<object>(canvas, "Strokes"), strokes) && Field<bool>(drawing, "dirty") && Field<string>(drawing, "targetPath") == copy,
            "failed board opening preserves unsaved strokes and the existing save target");
        Check(File.ReadAllText(unsupported) == unsupportedJson && File.ReadAllText(originalPath) == original,
            "failed board opening leaves both source files unchanged");
        Check((bool)Invoke(drawing, "SaveDrawing", false)! && File.ReadAllText(originalPath) == original,
            "saving after a failed board open writes only the selected editable board");
    }

    private static T Named<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
    private static T Property<T>(object target, string name) => (T)target.GetType().GetProperty(name)!.GetValue(target)!;

    private static void Capture(Window window, string path)
    {
        Pump();
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame: " + path);
        Check(frame.PixelSize.Width > 100 && frame.PixelSize.Height > 100, "Skia capture has a real frame");
        frame.Save(path, new PngBitmapEncoderOptions());
        Check(new FileInfo(path).Length > 1000, "PNG capture is nonempty");
    }

    private static void Pump()
    {
        for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(10); }
    }

    private static void PumpUntil(Func<bool> predicate, string name)
    {
        var timer = Stopwatch.StartNew();
        while (!predicate() && timer.Elapsed < TimeSpan.FromSeconds(8)) Pump();
        Check(predicate(), name);
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void SetField(object target, string name, object? value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static object? Invoke(object target, string name, params object[] arguments) => target.GetType().GetMethod(name, Private)!.Invoke(target, arguments);
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        checks++; Console.WriteLine("PASS: " + name);
    }

    private static string FindLinuxRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "global.json")) && Directory.Exists(Path.Combine(directory.FullName, "src", "TeXBB.Desktop"))) return directory.FullName;
        throw new DirectoryNotFoundException("Run the probe built inside the linux subtree.");
    }
}
