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
            Capture(window, Path.Combine(root, $"main-{(dark ? "dark" : "light")}-{width}.png"));
        }

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
        Capture(drawing, Path.Combine(root, "drawing-stroke.png"));
        Check((bool)Invoke(drawing, "SaveDrawing", false)!, "drawing saves without file picker");
        string strokePath = Path.Combine(store.Root, "current", "current_board.strokes.json");
        Check(File.Exists(strokePath) && File.ReadAllText(strokePath).Contains("strokes"), "saved drawing creates editable stroke document");
        drawing.Close();
        Check((bool)Invoke(window, "SaveCurrent")!, "current document saved before closing");
        window.Close(); Pump();
        Check(!window.IsVisible && !drawing.IsVisible, "saved windows close without unsaved prompts");
    }

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
