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
    private string targetPath;
    private byte[]? expectedHash;
    private bool dirty;
    private bool closeApproved;
    private bool askingToClose;

    public DrawingWindow(string workspaceRoot)
    {
        currentDirectory = Path.Combine(Path.GetFullPath(workspaceRoot), "current");
        targetPath = Path.Combine(currentDirectory, "current_board.strokes.json");
        Title = "TeXBB — Drawing";
        Width = 1120;
        Height = 760;
        MinWidth = 760;
        MinHeight = 480;
        Background = new SolidColorBrush(Color.Parse("#10281E"));

        var colors = new Dictionary<string, Color>
        {
            ["Chalk"] = Color.Parse("#FFF7EA"), ["Green"] = Color.Parse("#82DFAD"),
            ["Yellow"] = Color.Parse("#F3CD65"), ["Blue"] = Color.Parse("#72B8F3"),
            ["Pink"] = Color.Parse("#F29CA9")
        };
        var colorPicker = new ComboBox { ItemsSource = colors.Keys, SelectedIndex = 0, Width = 100 };
        colorPicker.SelectionChanged += (_, _) =>
        {
            if (colorPicker.SelectedItem is string name) canvas.PenColor = colors[name];
        };
        var widthPicker = new ComboBox { ItemsSource = new double[] { 2, 4, 6, 10, 16 }, SelectedIndex = 2, Width = 74 };
        widthPicker.SelectionChanged += (_, _) =>
        {
            if (widthPicker.SelectedItem is double width) canvas.PenWidth = width;
        };
        var undo = new Button { Content = "Undo" };
        var clear = new Button { Content = "Clear" };
        var create = new Button { Content = "New board" };
        undo.Click += (_, _) => canvas.Undo();
        clear.Click += (_, _) => canvas.ClearBoard();
        create.Click += async (_, _) =>
        {
            if (!await CanDiscardAsync()) return;
            canvas.Reset([], 3200, 1600, false);
            targetPath = NewCopyPath();
            expectedHash = null;
            SetDirty(false);
            UpdateAvailability();
            status.Text = "New separate board. Save writes " + Path.GetFileName(targetPath) + ".";
        };
        saveButton.Click += (_, _) => SaveDrawing(false);
        copyButton.Click += (_, _) => SaveDrawing(true);
        exportButton.Click += async (_, _) => await ExportPngAsync();
        canvas.Changed += () => SetDirty(true);

        var toolbar = new WrapPanel { Margin = new Thickness(8), Orientation = Orientation.Horizontal };
        foreach (var control in new Control[]
        {
            new TextBlock { Text = "Pen", VerticalAlignment = VerticalAlignment.Center }, colorPicker,
            new TextBlock { Text = "Width", VerticalAlignment = VerticalAlignment.Center }, widthPicker,
            undo, clear, saveButton, copyButton, exportButton, create
        })
        {
            control.Margin = new Thickness(4);
            toolbar.Children.Add(control);
        }
        var layout = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);
        layout.Children.Add(toolbar);
        layout.Children.Add(status);
        layout.Children.Add(canvas);
        Content = layout;
        KeyDown += (_, e) =>
        {
            if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0) return;
            if (e.Key == Key.S) { SaveDrawing(false); e.Handled = true; }
            if (e.Key == Key.Z) { canvas.Undo(); e.Handled = true; }
        };
        Closing += async (_, e) =>
        {
            if (!dirty || closeApproved) return;
            e.Cancel = true;
            if (askingToClose) return;
            askingToClose = true;
            try
            {
                if (await CanDiscardAsync()) { closeApproved = true; Close(); }
            }
            finally { askingToClose = false; }
        };
        LoadDrawing();
    }

    private void SetDirty(bool value)
    {
        dirty = value;
        Title = "TeXBB — Drawing" + (dirty ? " *" : string.Empty);
        if (dirty) status.Text = "Unsaved drawing — " + Path.GetFileName(targetPath);
    }

    private void UpdateAvailability()
    {
        saveButton.IsEnabled = copyButton.IsEnabled = exportButton.IsEnabled = !canvas.IsReadOnly;
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
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException)
        {
            canvas.Reset([], 3200, 1600, true);
            status.Text = error.Message + " Original left unchanged. Choose New board to create a separate Linux drawing.";
        }
        UpdateAvailability();
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
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
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
        var dialog = new Window { Title = "Unsaved drawing", Width = 430, Height = 170, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
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
        private Stroke? active;
        private IPointer? pointer;
        public List<Stroke> Strokes { get; private set; } = [];
        public int CanvasWidth { get; private set; } = 3200;
        public int CanvasHeight { get; private set; } = 1600;
        public Color PenColor { get; set; } = Color.Parse("#FFF7EA");
        public double PenWidth { get; set; } = 6;
        public bool IsReadOnly { get; private set; }
        public event Action? Changed;

        public DrawingCanvas()
        {
            Focusable = true;
            ClipToBounds = true;
            PointerPressed += (_, e) =>
            {
                if (IsReadOnly || active is not null || !PaperBounds().Contains(e.GetPosition(this)) ||
                    (e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)) return;
                Focus();
                RememberUndo();
                active = new Stroke(PenColor, PenWidth, [LogicalPoint(e.GetPosition(this))]);
                Strokes.Add(active);
                pointer = e.Pointer;
                e.Pointer.Capture(this);
                Changed?.Invoke();
                InvalidateVisual();
                e.Handled = true;
            };
            PointerMoved += (_, e) =>
            {
                if (active is null || pointer != e.Pointer) return;
                AddPoint(e.GetPosition(this));
                e.Handled = true;
            };
            PointerReleased += (_, e) =>
            {
                if (active is null || pointer != e.Pointer) return;
                AddPoint(e.GetPosition(this));
                FinishStroke();
                e.Handled = true;
            };
            PointerCaptureLost += (_, _) => FinishStroke();
        }

        public void Reset(List<Stroke> strokes, int width, int height, bool readOnly)
        {
            FinishStroke();
            Strokes = strokes;
            CanvasWidth = width;
            CanvasHeight = height;
            IsReadOnly = readOnly;
            undo.Clear();
            InvalidateVisual();
        }

        private Rect PaperBounds()
        {
            var scale = Math.Min(Bounds.Width / CanvasWidth, Bounds.Height / CanvasHeight);
            var width = CanvasWidth * scale;
            var height = CanvasHeight * scale;
            return new Rect((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height);
        }

        private Point LogicalPoint(Point position)
        {
            var paper = PaperBounds();
            var scale = paper.Width / CanvasWidth;
            return new Point(Math.Clamp((position.X - paper.X) / scale, 0, CanvasWidth),
                Math.Clamp((position.Y - paper.Y) / scale, 0, CanvasHeight));
        }

        private void AddPoint(Point position)
        {
            var point = LogicalPoint(position);
            if (active is null || active.Points[^1] == point) return;
            active.Points.Add(point);
            active.Geometry = null;
            InvalidateVisual();
        }

        public void FinishStroke()
        {
            active = null;
            var captured = pointer;
            pointer = null;
            captured?.Capture(null);
        }

        public void Undo()
        {
            if (IsReadOnly) return;
            FinishStroke();
            if (undo.Count == 0) return;
            Strokes = undo[^1];
            undo.RemoveAt(undo.Count - 1);
            Changed?.Invoke();
            InvalidateVisual();
        }

        public void ClearBoard()
        {
            if (IsReadOnly || Strokes.Count == 0) return;
            FinishStroke();
            RememberUndo();
            Strokes = [];
            Changed?.Invoke();
            InvalidateVisual();
        }

        private void RememberUndo()
        {
            undo.Add([.. Strokes]);
            if (undo.Count > 50) undo.RemoveAt(0);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            context.FillRectangle(new SolidColorBrush(Color.Parse("#07140E")), new Rect(Bounds.Size));
            DrawBoard(context, PaperBounds());
        }

        public void DrawBoard(DrawingContext context, Rect paper)
        {
            if (paper.Width <= 0 || paper.Height <= 0) return;
            context.FillRectangle(new SolidColorBrush(Color.Parse("#103023")), paper);
            var scale = paper.Width / CanvasWidth;
            using var clip = context.PushClip(paper);
            using var transform = context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(paper.X, paper.Y));
            foreach (var stroke in Strokes)
            {
                var brush = new SolidColorBrush(stroke.Color);
                if (stroke.Points.Count == 1)
                {
                    context.DrawEllipse(brush, null, stroke.Points[0], stroke.Width / 2, stroke.Width / 2);
                    continue;
                }
                if (stroke.Geometry is null)
                {
                    stroke.Geometry = new StreamGeometry();
                    using var path = stroke.Geometry.Open();
                    path.BeginFigure(stroke.Points[0], false);
                    foreach (var point in stroke.Points.Skip(1)) path.LineTo(point);
                    path.EndFigure(false);
                }
                context.DrawGeometry(null, new Pen(brush, stroke.Width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), stroke.Geometry);
            }
        }
    }
}
