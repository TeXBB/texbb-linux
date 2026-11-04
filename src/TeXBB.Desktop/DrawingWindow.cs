using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;

namespace TeXBB.Desktop;

/// <summary>A first Linux freehand board, preserving the Windows V1 stroke format.</summary>
public sealed class DrawingWindow : Window
{
    private const int MaximumFileBytes = 64 * 1024 * 1024;
    private readonly string currentDirectory;
    private readonly DrawingCanvas canvas = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 8) };
    private readonly Button saveButton = new() { Content = "Save" };
    private readonly Button exportButton = new() { Content = "Export PNG…" };
    private readonly Button copyButton = new() { Content = "Save copy" };
    private readonly Button undoButton = new() { Content = "Undo" };
    private readonly Button redoButton = new() { Content = "Redo" };
    private readonly Button clearButton = new() { Content = "Clear" };
    private string targetPath;
    private byte[]? expectedHash;
    private bool dirty;
    private bool closeApproved;
    private bool isClosed;
    private Task<bool>? closeRequest;

    public DrawingWindow(string workspaceRoot, bool dark = true)
    {
        currentDirectory = Path.Combine(Path.GetFullPath(workspaceRoot), "current");
        targetPath = Path.Combine(currentDirectory, "current_board.strokes.json");
        Title = "TeXBB — Blackboard";
        Width = 1120; Height = 760; MinWidth = 760; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        // This window explicitly awaits its unsaved-board dialog before approving close.
        ClosingBehavior = WindowClosingBehavior.OwnerWindowOnly;
        RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Background = Brush.Parse(dark ? "#0A1612" : "#F4F9F6");
        Foreground = Brush.Parse(dark ? "#EEF7F2" : "#183027");
        var surface = Brush.Parse(dark ? "#0F1F19" : "#FFFFFF");
        var subtle = Brush.Parse(dark ? "#152B22" : "#EAF4EF");
        var border = Brush.Parse(dark ? "#2F4C3F" : "#C2D8CD");
        var accent = Brush.Parse(dark ? "#3AAE7A" : "#0F6B4B");
        var accentSoft = Brush.Parse(dark ? "#173D30" : "#DDEFE6");
        FontSize = 13;
        void StyleButton(Button button)
        {
            button.Background = subtle; button.Foreground = Foreground;
            button.Padding = new Thickness(12, 7); button.CornerRadius = new CornerRadius(4);
            button.BorderBrush = border; button.BorderThickness = new Thickness(1);
        }
        var pen = new Button { Content = "Pen" };
        var eraser = new Button { Content = "Eraser" };
        ToolTip.SetTip(eraser, "Erase whole strokes. Undo restores the entire eraser drag.");
        void SelectTool(bool erase)
        {
            canvas.FinishStroke(); canvas.IsEraser = erase;
            canvas.Cursor = new Cursor(erase ? StandardCursorType.Hand : StandardCursorType.Cross);
            pen.Background = erase ? subtle : accentSoft;
            eraser.Background = erase ? accentSoft : subtle;
            pen.BorderBrush = erase ? border : accent; eraser.BorderBrush = erase ? accent : border;
        }
        pen.Click += (_, _) => SelectTool(false); eraser.Click += (_, _) => SelectTool(true);
        var colors = new Dictionary<string, Color>
        {
            ["Chalk"] = Color.Parse("#FFF7EA"), ["Green"] = Color.Parse("#82DFAD"),
            ["Yellow"] = Color.Parse("#F3CD65"), ["Blue"] = Color.Parse("#72B8F3"),
            ["Pink"] = Color.Parse("#F29CA9")
        };
        var colorButtons = new List<Button>();
        var palette = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        foreach (var pair in colors)
        {
            var swatch = new Button { Name = "Pen" + pair.Key, Width = 28, Height = 28, Padding = new Thickness(5),
                CornerRadius = new CornerRadius(14), Background = subtle, BorderThickness = new Thickness(2),
                BorderBrush = pair.Key == "Chalk" ? accent : Brushes.Transparent,
                Content = new Border { Background = new SolidColorBrush(pair.Value), CornerRadius = new CornerRadius(7), Width = 14, Height = 14 } };
            ToolTip.SetTip(swatch, pair.Key + " pen");
            Avalonia.Automation.AutomationProperties.SetName(swatch, pair.Key + " pen");
            swatch.Click += (_, _) =>
            {
                canvas.PenColor = pair.Value; SelectTool(false);
                foreach (var other in colorButtons) other.BorderBrush = other == swatch ? accent : Brushes.Transparent;
            };
            colorButtons.Add(swatch); palette.Children.Add(swatch);
        }
        var widthPicker = new ComboBox { ItemsSource = new double[] { 2, 4, 6, 10, 16 }, SelectedIndex = 2,
            Width = 76, Background = surface, Foreground = Foreground, BorderBrush = border };
        ToolTip.SetTip(widthPicker, "Pen width");
        widthPicker.SelectionChanged += (_, _) =>
        {
            if (widthPicker.SelectedItem is double width) canvas.PenWidth = width;
        };
        var create = new Button { Content = "New board" };
        var open = new Button { Name = "OpenBoard", Content = "Open…" };
        open.Click += async (_, _) => await OpenDrawingAsync();
        ToolTip.SetTip(open, "Open an editable board (Ctrl+O)");
        undoButton.Click += (_, _) => canvas.Undo(); redoButton.Click += (_, _) => canvas.Redo();
        clearButton.Click += (_, _) => canvas.ClearBoard();
        ToolTip.SetTip(undoButton, "Undo (Ctrl+Z)"); ToolTip.SetTip(redoButton, "Redo (Ctrl+Y)");
        create.Click += async (_, _) =>
        {
            if (!await CanDiscardAsync()) return;
            canvas.Reset([], 3200, 1600, false); targetPath = NewCopyPath(); expectedHash = null;
            SetDirty(false); UpdateAvailability();
            status.Text = "New board — " + Path.GetFileName(targetPath);
        };
        saveButton.Click += (_, _) => SaveDrawing(false);
        copyButton.Click += (_, _) => SaveDrawing(true);
        exportButton.Click += async (_, _) => await ExportPngAsync();
        canvas.Changed += () => { SetDirty(true); UpdateAvailability(); };
        canvas.HistoryChanged += UpdateAvailability;

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(16, 10) };
        header.Children.Add(new TextBlock { Text = "Blackboard", FontSize = 18, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var fileActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var button in new[] { create, open, saveButton, copyButton, exportButton })
        {
            StyleButton(button); fileActions.Children.Add(button);
        }
        saveButton.Background = accent; saveButton.Foreground = dark ? Brush.Parse("#08120F") : Brushes.White;
        ToolTip.SetTip(saveButton, "Save editable strokes (Ctrl+S)");
        Grid.SetColumn(fileActions, 1); header.Children.Add(fileActions);
        var toolbar = new WrapPanel { Margin = new Thickness(12, 6), Orientation = Orientation.Horizontal };
        foreach (var button in new[] { pen, eraser, undoButton, redoButton, clearButton }) StyleButton(button);
        foreach (var control in new Control[]
        {
            pen, eraser, palette, new TextBlock { Text = "Width", VerticalAlignment = VerticalAlignment.Center },
            widthPicker, undoButton, redoButton, clearButton
        })
        {
            control.Margin = new Thickness(4); toolbar.Children.Add(control);
        }
        var toolbarBorder = new Border { Background = surface, BorderBrush = border, BorderThickness = new Thickness(0, 1, 0, 1), Child = toolbar };
        var statusBorder = new Border { Background = surface, BorderBrush = border, BorderThickness = new Thickness(0, 1, 0, 0), Child = status };
        status.FontSize = 12; status.Foreground = Brush.Parse(dark ? "#A4BEB1" : "#546E63");
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        layout.Children.Add(header);
        Grid.SetRow(toolbarBorder, 1); layout.Children.Add(toolbarBorder);
        Grid.SetRow(canvas, 2); layout.Children.Add(canvas);
        Grid.SetRow(statusBorder, 3); layout.Children.Add(statusBorder);
        Content = layout; SelectTool(false);
        KeyDown += async (_, e) =>
        {
            if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0) return;
            if (e.Key == Key.O) { e.Handled = true; await OpenDrawingAsync(); return; }
            if (e.Key == Key.S) { SaveDrawing(false); e.Handled = true; }
            if (e.Key == Key.Y || (e.Key == Key.Z && (e.KeyModifiers & KeyModifiers.Shift) != 0))
            { canvas.Redo(); e.Handled = true; }
            else if (e.Key == Key.Z) { canvas.Undo(); e.Handled = true; }
        };
        Closing += async (_, e) =>
        {
            if (!dirty || closeApproved) return;
            e.Cancel = true;
            await RequestCloseAsync();
        };
        Closed += (_, _) => isClosed = true;
        LoadDrawing(); UpdateAvailability();
    }
    /// <summary>Closes after any unsaved-board decision, sharing a pending decision with concurrent callers.</summary>
    public async Task<bool> RequestCloseAsync()
    {
        if (isClosed) return true;
        if (closeRequest is not null) return await closeRequest;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        closeRequest = completion.Task;
        try
        {
            if (!await CanDiscardAsync()) { completion.SetResult(false); return false; }
            closeApproved = true;
            Close();
            completion.SetResult(isClosed);
            return isClosed;
        }
        catch (Exception error)
        {
            if (!isClosed) closeApproved = false;
            status.Text = "Could not close: " + error.Message;
            completion.TrySetResult(isClosed);
            return isClosed;
        }
        finally { closeRequest = null; }
    }
    private void SetDirty(bool value)
    {
        dirty = value;
        Title = "TeXBB — Blackboard" + (dirty ? " *" : string.Empty);
        if (dirty) status.Text = "Unsaved — " + Path.GetFileName(targetPath) + "  ·  " + canvas.Strokes.Count + " strokes";
    }

    private void UpdateAvailability()
    {
        saveButton.IsEnabled = copyButton.IsEnabled = exportButton.IsEnabled = !canvas.IsReadOnly;
        undoButton.IsEnabled = canvas.CanUndo; redoButton.IsEnabled = canvas.CanRedo;
        clearButton.IsEnabled = !canvas.IsReadOnly && canvas.Strokes.Count > 0;
    }

    private void LoadDrawing()
    {
        try
        {
            byte[] bytes;
            try { bytes = ReadBounded(targetPath); }
            catch (FileNotFoundException)
            {
                if (File.Exists(targetPath + ".bak"))
                    throw new InvalidDataException("The original is missing and a Windows backup is present.");
                status.Text = "Draw with the mouse, pen, or touch. Save keeps an editable board in this workspace.";
                return;
            }
            catch (DirectoryNotFoundException)
            {
                status.Text = "Draw with the mouse, pen, or touch. Save keeps an editable board in this workspace.";
                return;
            }
            var loaded = ParseDrawing(bytes);
            canvas.Reset(loaded.Strokes, loaded.Width, loaded.Height, false);
            expectedHash = SHA256.HashData(bytes);
            status.Text = "Loaded " + Path.GetFileName(targetPath) + ".";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException)
        {
            canvas.Reset([], 3200, 1600, true);
            status.Text = error.Message + " Original left unchanged. Choose New board to create a separate Linux drawing.";
        }
        UpdateAvailability();
    }

    private async Task OpenDrawingAsync()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open a TeXBB board", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Editable TeXBB board") { Patterns = ["*.strokes.json"] }]
            });
            string? path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path is null || !await CanDiscardAsync()) return;
            LoadDrawingFile(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            status.Text = "Could not open: " + error.Message;
        }
    }

    private bool LoadDrawingFile(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            byte[] bytes = ReadBounded(fullPath);
            var loaded = ParseDrawing(bytes);
            // Validate the whole file before replacing the current board or its save target.
            // Unsupported images/text/schema leave both the source and the current board intact.
            canvas.Reset(loaded.Strokes, loaded.Width, loaded.Height, false);
            targetPath = fullPath; expectedHash = SHA256.HashData(bytes); SetDirty(false); UpdateAvailability();
            status.Text = "Loaded " + fullPath; return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or
            InvalidOperationException or FormatException or OverflowException or KeyNotFoundException or ArgumentException)
        {
            status.Text = "Could not open: " + error.Message + " Current board kept unchanged.";
            return false;
        }
    }
    private static byte[] ReadBounded(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumFileBytes) throw new InvalidDataException("The drawing exceeds the 64 MiB limit.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static (List<Stroke> Strokes, int Width, int Height) ParseDrawing(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        RequireProperties(root, ["version", "canvasWidth", "canvasHeight", "mode", "images", "strokes", "texts"]);
        if (!root.TryGetProperty("version", out var version) || !version.TryGetInt32(out var number) || number != 1)
            throw new InvalidDataException("This drawing version is not supported yet.");
        if (root.TryGetProperty("mode", out var mode) && mode.GetString() is not (null or "" or "editable"))
            throw new InvalidDataException("Raster-only drawing is not supported yet.");
        foreach (var name in new[] { "images", "texts", "strokes" })
            if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Drawing arrays are missing or invalid.");
        if (root.GetProperty("images").GetArrayLength() != 0 || root.GetProperty("texts").GetArrayLength() != 0)
            throw new InvalidDataException("This board contains images or text, which the Linux drawing editor cannot edit yet.");
        var width = root.TryGetProperty("canvasWidth", out var w) ? w.GetInt32() : 3200;
        var height = root.TryGetProperty("canvasHeight", out var h) ? h.GetInt32() : 1600;
        if (width is < 1 or > 16384 || height is < 1 or > 16384)
            throw new InvalidDataException("Canvas dimensions are outside the supported range.");
        var strokes = new List<Stroke>();
        var totalPoints = 0;
        foreach (var item in root.GetProperty("strokes").EnumerateArray())
        {
            RequireProperties(item, ["color", "width", "points"]);
            var colorText = item.GetProperty("color").GetString();
            if (colorText is null || colorText.Length != 9 || colorText[0] != '#' ||
                !uint.TryParse(colorText.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
                throw new InvalidDataException("A drawing color is invalid.");
            var thickness = Number(item.GetProperty("width"));
            if (thickness is <= 0 or > 16384) throw new InvalidDataException("A stroke width is invalid.");
            var points = new List<Point>();
            foreach (var point in item.GetProperty("points").EnumerateArray())
            {
                if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() != 2 || ++totalPoints > 1000000)
                    throw new InvalidDataException("Drawing points are invalid or exceed the supported limit.");
                points.Add(new Point(Number(point[0]), Number(point[1])));
            }
            if (points.Count == 0) throw new InvalidDataException("A drawing stroke has no points.");
            strokes.Add(new Stroke(Color.FromUInt32(argb), thickness, points));
        }
        return (strokes, width, height);
    }

    private static void RequireProperties(JsonElement element, string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            element.EnumerateObject().Any(property => !allowed.Contains(property.Name, StringComparer.Ordinal)))
            throw new InvalidDataException("The drawing contains unsupported fields.");
    }

    private static double Number(JsonElement value)
    {
        var number = value.GetDouble();
        if (!double.IsFinite(number) || number < -float.MaxValue || number > float.MaxValue)
            throw new InvalidDataException("A drawing coordinate is invalid.");
        return number;
    }

    private string SerializeDrawing() => JsonSerializer.Serialize(new
    {
        version = 1, mode = "editable", canvasWidth = canvas.CanvasWidth, canvasHeight = canvas.CanvasHeight,
        images = Array.Empty<object>(),
        strokes = canvas.Strokes.Select(stroke => new
        {
            color = "#" + stroke.Color.ToUInt32().ToString("X8", CultureInfo.InvariantCulture),
            width = stroke.Width, points = stroke.Points.Select(point => new[] { point.X, point.Y })
        }),
        texts = Array.Empty<object>()
    }, new JsonSerializerOptions { WriteIndented = true });

    private string NewCopyPath() => Path.Combine(currentDirectory,
        "current_board.linux-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" +
        Guid.NewGuid().ToString("N")[..6] + ".strokes.json");

    private bool SaveDrawing(bool copy)
    {
        if (canvas.IsReadOnly) return false;
        canvas.FinishStroke();
        var destination = copy ? NewCopyPath() : targetPath;
        string? temporary = null;
        try
        {
            if (!copy)
            {
                byte[]? currentHash = null;
                try { currentHash = SHA256.HashData(ReadBounded(destination)); }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                if ((expectedHash is null) != (currentHash is null) ||
                    (expectedHash is not null && currentHash is not null && !expectedHash.SequenceEqual(currentHash)))
                    throw new IOException("The drawing changed on disk. Use Save copy to keep both versions.");
            }
            var bytes = Encoding.UTF8.GetBytes(SerializeDrawing());
            if (bytes.Length > MaximumFileBytes) throw new IOException("The drawing exceeds the 64 MiB save limit.");
            _ = ParseDrawing(bytes); // Never save a board which this editor cannot reopen.
            Directory.CreateDirectory(currentDirectory);
            temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllBytes(temporary, bytes);
            if (!copy && expectedHash is not null) File.Copy(destination, destination + ".bak", true);
            File.Move(temporary, destination, !copy && expectedHash is not null);
            temporary = null;
            targetPath = destination;
            expectedHash = SHA256.HashData(bytes);
            SetDirty(false);
            status.Text = "Saved " + destination;
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            status.Text = "Could not save: " + error.Message;
            return false;
        }
        finally
        {
            if (temporary is not null)
                try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private async Task ExportPngAsync()
    {
        if (canvas.IsReadOnly) return;
        canvas.FinishStroke();
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export drawing as PNG", SuggestedFileName = "drawing.png", DefaultExtension = "png",
                ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"], MimeTypes = ["image/png"] }]
            });
            if (file is null) return;
            var scale = Math.Min(1d, 4096d / Math.Max(canvas.CanvasWidth, canvas.CanvasHeight));
            var width = Math.Max(1, (int)Math.Round(canvas.CanvasWidth * scale));
            var height = Math.Max(1, (int)Math.Round(canvas.CanvasHeight * scale));
            using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
            using (var context = bitmap.CreateDrawingContext()) canvas.DrawBoard(context, new Rect(0, 0, width, height));
            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek) stream.SetLength(0);
            bitmap.Save(stream, new PngBitmapEncoderOptions());
            status.Text = "Exported PNG. Use Save to keep the editable strokes.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            status.Text = "Could not export PNG: " + error.Message;
        }
    }

    private async Task<bool> CanDiscardAsync()
    {
        canvas.FinishStroke();
        if (!dirty) return true;
        var dialog = new Window { Title = "Unsaved drawing", Width = 430, Height = 170, CanResize = false,
            RequestedThemeVariant = RequestedThemeVariant, Background = Background, Foreground = Foreground,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        foreach (var text in new[] { "Save", "Discard", "Cancel" })
        {
            var button = new Button { Content = text };
            button.Click += (_, _) => dialog.Close(text);
            buttons.Children.Add(button);
        }
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 24,
            Children = { new TextBlock { Text = "Save changes to this drawing before continuing?", TextWrapping = TextWrapping.Wrap }, buttons }
        };
        var answer = await dialog.ShowDialog<string?>(this);
        return answer == "Discard" || (answer == "Save" && SaveDrawing(false));
    }

    private sealed class Stroke(Color color, double width, List<Point> points)
    {
        public Color Color { get; } = color;
        public double Width { get; } = width;
        public List<Point> Points { get; } = points;
        public StreamGeometry? Geometry { get; set; }
    }

    private sealed class DrawingCanvas : Control
    {
        private readonly List<List<Stroke>> undo = [];
        private readonly List<List<Stroke>> redo = [];
        private Stroke? active;
        private IPointer? pointer;
        private Point? previousEraserPoint;
        private bool eraserChanged;
        public List<Stroke> Strokes { get; private set; } = [];
        public int CanvasWidth { get; private set; } = 3200;
        public int CanvasHeight { get; private set; } = 1600;
        public Color PenColor { get; set; } = Color.Parse("#FFF7EA");
        public double PenWidth { get; set; } = 6;
        public bool IsEraser { get; set; }
        public bool IsReadOnly { get; private set; }
        public bool CanUndo => !IsReadOnly && undo.Count > 0;
        public bool CanRedo => !IsReadOnly && redo.Count > 0;
        public event Action? Changed;
        public event Action? HistoryChanged;

        public DrawingCanvas()
        {
            Focusable = true; ClipToBounds = true;
            PointerPressed += (_, e) =>
            {
                if (IsReadOnly || pointer is not null || !PaperBounds().Contains(e.GetPosition(this)) ||
                    (e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)) return;
                Focus(); pointer = e.Pointer; e.Pointer.Capture(this);
                if (IsEraser)
                {
                    eraserChanged = false; previousEraserPoint = null; EraseAt(e.GetPosition(this));
                }
                else
                {
                    RememberUndo();
                    active = new Stroke(PenColor, PenWidth, [LogicalPoint(e.GetPosition(this))]);
                    Strokes.Add(active); Changed?.Invoke(); InvalidateVisual();
                }
                e.Handled = true;
            };
            PointerMoved += (_, e) =>
            {
                if (pointer != e.Pointer) return;
                if (IsEraser) EraseAt(e.GetPosition(this)); else AddPoint(e.GetPosition(this));
                e.Handled = true;
            };
            PointerReleased += (_, e) =>
            {
                if (pointer != e.Pointer) return;
                if (IsEraser) EraseAt(e.GetPosition(this)); else AddPoint(e.GetPosition(this));
                FinishStroke(); e.Handled = true;
            };
            PointerCaptureLost += (_, _) => FinishStroke();
        }

        public void Reset(List<Stroke> strokes, int width, int height, bool readOnly)
        {
            FinishStroke(); Strokes = strokes; CanvasWidth = width; CanvasHeight = height; IsReadOnly = readOnly;
            undo.Clear(); redo.Clear(); HistoryChanged?.Invoke(); InvalidateVisual();
        }

        private Rect PaperBounds()
        {
            var scale = Math.Min(Bounds.Width / CanvasWidth, Bounds.Height / CanvasHeight);
            var width = CanvasWidth * scale; var height = CanvasHeight * scale;
            return new Rect((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height);
        }

        private Point LogicalPoint(Point position)
        {
            var paper = PaperBounds(); var scale = paper.Width / CanvasWidth;
            if (scale <= 0) return default;
            return new Point(Math.Clamp((position.X - paper.X) / scale, 0, CanvasWidth),
                Math.Clamp((position.Y - paper.Y) / scale, 0, CanvasHeight));
        }

        private void AddPoint(Point position)
        {
            var point = LogicalPoint(position);
            if (active is null || active.Points[^1] == point) return;
            active.Points.Add(point); active.Geometry = null; InvalidateVisual();
        }

        private void EraseAt(Point position)
        {
            if (IsReadOnly) return;
            double scale = PaperBounds().Width / CanvasWidth;
            if (scale <= 0) return;
            Point end = LogicalPoint(position), start = previousEraserPoint ?? end;
            previousEraserPoint = end;
            // Keep a consistent hit area in screen pixels while preserving logical stroke coordinates.
            double radius = 10 / scale;
            var hit = Strokes.Where(stroke => IntersectsStroke(stroke, start, end, radius)).ToArray();
            if (hit.Length == 0) return;
            // A whole drag is one undo action, even when it crosses several strokes.
            if (!eraserChanged) { RememberUndo(); eraserChanged = true; }
            foreach (var stroke in hit) Strokes.Remove(stroke);
            Changed?.Invoke(); InvalidateVisual();
        }

        private static bool IntersectsStroke(Stroke stroke, Point start, Point end, double radius)
        {
            double limit = radius + stroke.Width / 2; limit *= limit;
            if (stroke.Points.Count == 1) return PointSegmentDistanceSquared(stroke.Points[0], start, end) <= limit;
            for (int i = 1; i < stroke.Points.Count; i++)
                if (SegmentDistanceSquared(start, end, stroke.Points[i - 1], stroke.Points[i]) <= limit) return true;
            return false;
        }

        private static double PointSegmentDistanceSquared(Point point, Point start, Point end)
        {
            Vector direction = end - start;
            double length = direction.X * direction.X + direction.Y * direction.Y;
            double parameter = length == 0 ? 0 : Math.Clamp(
                ((point.X - start.X) * direction.X + (point.Y - start.Y) * direction.Y) / length, 0, 1);
            Vector difference = point - (start + direction * parameter);
            return difference.X * difference.X + difference.Y * difference.Y;
        }

        private static double SegmentDistanceSquared(Point a, Point b, Point c, Point d)
        {
            Vector ab = b - a, cd = d - c, ac = c - a;
            double cross = ab.X * cd.Y - ab.Y * cd.X;
            if (cross != 0)
            {
                double first = (ac.X * cd.Y - ac.Y * cd.X) / cross;
                double second = (ac.X * ab.Y - ac.Y * ab.X) / cross;
                if (first >= 0 && first <= 1 && second >= 0 && second <= 1) return 0;
            }
            return Math.Min(Math.Min(PointSegmentDistanceSquared(a, c, d), PointSegmentDistanceSquared(b, c, d)),
                Math.Min(PointSegmentDistanceSquared(c, a, b), PointSegmentDistanceSquared(d, a, b)));
        }

        public void FinishStroke()
        {
            active = null; previousEraserPoint = null; eraserChanged = false;
            var captured = pointer; pointer = null; captured?.Capture(null);
        }

        public void Undo()
        {
            if (IsReadOnly) return;
            FinishStroke(); if (undo.Count == 0) return;
            redo.Add([.. Strokes]); Strokes = undo[^1]; undo.RemoveAt(undo.Count - 1);
            Changed?.Invoke(); InvalidateVisual();
        }

        public void Redo()
        {
            if (IsReadOnly) return;
            FinishStroke(); if (redo.Count == 0) return;
            undo.Add([.. Strokes]); if (undo.Count > 50) undo.RemoveAt(0);
            Strokes = redo[^1]; redo.RemoveAt(redo.Count - 1);
            Changed?.Invoke(); InvalidateVisual();
        }

        public void ClearBoard()
        {
            if (IsReadOnly) return;
            FinishStroke(); if (Strokes.Count == 0) return;
            RememberUndo(); Strokes = []; Changed?.Invoke(); InvalidateVisual();
        }

        private void RememberUndo()
        {
            undo.Add([.. Strokes]); if (undo.Count > 50) undo.RemoveAt(0);
            redo.Clear();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            context.FillRectangle(new SolidColorBrush(Color.Parse("#08120F")), new Rect(Bounds.Size));
            DrawBoard(context, PaperBounds());
        }

        public void DrawBoard(DrawingContext context, Rect paper)
        {
            if (paper.Width <= 0 || paper.Height <= 0) return;
            context.FillRectangle(new SolidColorBrush(Color.Parse("#004731")), paper);
            var scale = paper.Width / CanvasWidth;
            using var clip = context.PushClip(paper);
            using var transform = context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(paper.X, paper.Y));
            foreach (var stroke in Strokes)
            {
                var brush = new SolidColorBrush(stroke.Color);
                if (stroke.Points.Count == 1)
                {
                    context.DrawEllipse(brush, null, stroke.Points[0], stroke.Width / 2, stroke.Width / 2); continue;
                }
                if (stroke.Geometry is null)
                {
                    stroke.Geometry = new StreamGeometry();
                    using var path = stroke.Geometry.Open(); path.BeginFigure(stroke.Points[0], false);
                    foreach (var point in stroke.Points.Skip(1)) path.LineTo(point);
                    path.EndFigure(false);
                }
                context.DrawGeometry(null, new Pen(brush, stroke.Width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), stroke.Geometry);
            }
        }
    }
}
