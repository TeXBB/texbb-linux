using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TeXBB.Core.Documents;

/// <summary>Document persistence shared by desktop front ends, with optimistic conflict checks.</summary>
public sealed class WorkspaceStore
{
    private const int MaximumDocumentBytes = 128 * 1024 * 1024;
    private const int MaximumRecoveryBytes = 256 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private readonly object gate = new();

    public WorkspaceStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        EnsureNoLinks(Root);
        Directory.CreateDirectory(Root);
        EnsureDirectory("current");
        EnsureDirectory("reference");
        EnsureDirectory("history", "recovery");
    }

    public string Root { get; }

    public IReadOnlyList<DocumentFile> ListDocuments()
    {
        lock (gate)
        {
            var files = new List<DocumentFile>();
            string current = WorkspacePath("current");
            foreach (string path in Directory.EnumerateFiles(current))
            {
                if (TryKind(path, out var kind))
                {
                    EnsureNoLinks(path);
                    files.Add(new DocumentFile(path, kind, Path.GetFileName(path)));
                }
            }
            string bibliography = WorkspacePath("reference", "reference.bib");
            if (File.Exists(bibliography))
                files.Add(new DocumentFile(bibliography, DocumentKind.BibTeX, "reference.bib"));
            return files.OrderBy(file => file.Kind)
                .ThenBy(file => DocumentNumber(file.Path))
                .ThenBy(file => file.Name, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public DocumentBuffer CreateDocument(DocumentKind kind)
    {
        lock (gate)
        {
            string extension = Extension(kind);
            if (kind == DocumentKind.BibTeX)
            {
                string bibliography = WorkspacePath("reference", "reference.bib");
                TryWriteNew(bibliography, "% Bibliography\n");
                return OpenDocument(bibliography);
            }

            string directory = EnsureDirectory("current");
            long number = Directory.EnumerateFiles(directory)
                .Where(path => TryKind(path, out var existingKind) && existingKind == kind)
                .Select(DocumentNumber).Where(value => value < long.MaxValue).DefaultIfEmpty(0).Max();
            while (number < long.MaxValue)
            {
                number++;
                string path = WorkspacePath("current", "current_board_" +
                    number.ToString(CultureInfo.InvariantCulture) + extension);
                if (TryWriteNew(path, Template(kind))) return OpenDocument(path);
            }
            throw new IOException("No document number is available.");
        }
    }

    /// <summary>Explicitly selected external files retain their original paths and relative resources.</summary>
    public DocumentBuffer OpenDocument(string path)
    {
        lock (gate)
        {
            DocumentFile file = Describe(path);
            DiskSnapshot disk = ReadDisk(file.Path) ?? throw new FileNotFoundException("The document was not found.", file.Path);
            return new DocumentBuffer(file, disk.Text, disk.Revision);
        }
    }

    public void SaveDocument(DocumentBuffer buffer, bool overwriteExternalChanges = false)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        lock (gate)
        {
            DocumentFile file = ValidateFile(buffer.File);
            string text = buffer.Text;
            DiskSnapshot? current = ReadDisk(file.Path);
            if (!overwriteExternalChanges && current?.Revision != buffer.DiskRevision)
                throw new IOException("The document changed on disk. Reload it or explicitly confirm overwriting the external changes.");

            if (current?.Text == text)
            {
                buffer.MarkSaved(text, current.Revision);
                DeleteSatisfiedRecovery(file, text);
                return;
            }

            // Preserve the disk content, including an externally edited version, before an explicit replacement.
            if (current != null) SaveSnapshot(file, current.Text);
            string? expectedRevision = current?.Revision;
            byte[] bytes = Encode(text);
            WriteAtomic(file.Path, bytes, overwrite: true, beforePublish: () =>
            {
                EnsureNoLinks(file.Path);
                if (ReadDisk(file.Path)?.Revision != expectedRevision)
                    throw new IOException("The document changed again while saving. The source was kept.");
            });
            buffer.MarkSaved(text, Revision(bytes));
            DeleteSatisfiedRecovery(file, text);
        }
    }

    public void SaveRecovery(DocumentBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        lock (gate)
        {
            DocumentFile file = ValidateFile(buffer.File);
            if (!buffer.IsDirty) return;
            string text = buffer.Text;
            Encode(text); // Enforce the same source-size and Unicode constraints as an explicit save.
            var record = new RecoveryRecord
            {
                Version = 1,
                Target = file.Path,
                BaseRevision = TextRevision(buffer.SavedText),
                DraftRevision = TextRevision(text),
                DraftText = text,
                SavedAt = DateTimeOffset.UtcNow
            };
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(record);
            if (bytes.Length > MaximumRecoveryBytes) throw new IOException("The recovery draft exceeds its size limit.");
            WriteAtomic(RecoveryPath(file), bytes, overwrite: true);
        }
    }

    public RecoveryDraft? GetRecovery(DocumentFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        lock (gate)
        {
            file = ValidateFile(file);
            RecoveryRecord? record = ReadRecovery(file);
            if (record == null) return null;
            DiskSnapshot? disk = ReadDisk(file.Path);
            if (disk?.Text == record.DraftText) return null;
            return new RecoveryDraft(file, record.DraftText, record.SavedAt,
                disk != null && TextRevision(disk.Text) == record.BaseRevision);
        }
    }

    /// <summary>Save an immutable snapshot of the buffer without modifying its source.</summary>
    public string SaveHistory(DocumentBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        lock (gate) return SaveSnapshot(ValidateFile(buffer.File), buffer.Text);
    }

    private string SaveSnapshot(DocumentFile file, string text)
    {
        string directory = EnsureDirectory("history", Extension(file.Kind)[1..]);
        string stem = Path.GetFileNameWithoutExtension(file.Path);
        stem = new string(stem.Take(48).Select(character => char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_').ToArray());
        string name = stem + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff", CultureInfo.InvariantCulture) +
            "_" + Identity(file)[..12] + "_" + Guid.NewGuid().ToString("N")[..8] + Extension(file.Kind);
        string path = Path.Combine(directory, name);
        WriteAtomic(path, Encode(text), overwrite: false);
        return path;
    }

    private RecoveryRecord? ReadRecovery(DocumentFile file)
    {
        string path = RecoveryPath(file);
        byte[]? bytes = ReadBytes(path, MaximumRecoveryBytes);
        if (bytes == null) return null;
        RecoveryRecord? record;
        try { record = JsonSerializer.Deserialize<RecoveryRecord>(bytes); }
        catch (JsonException error) { throw new InvalidDataException("The recovery draft is damaged; it has been preserved.", error); }
        if (record == null || record.Version != 1 || record.DraftText == null ||
            string.IsNullOrEmpty(record.BaseRevision) || !string.Equals(record.Target, file.Path, PathComparison) ||
            TextRevision(record.DraftText) != record.DraftRevision)
            throw new InvalidDataException("The recovery draft could not be verified; it has been preserved.");
        return record;
    }

    private void DeleteSatisfiedRecovery(DocumentFile file, string savedText)
    {
        // Saving a source must not fail because cleanup of an old recovery record failed.
        try
        {
            RecoveryRecord? record = ReadRecovery(file);
            if (record?.DraftText == savedText) File.Delete(RecoveryPath(file));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string RecoveryPath(DocumentFile file) => WorkspacePath("history", "recovery", Identity(file) + ".v1.json");

    private static string Identity(DocumentFile file)
    {
        string identity = OperatingSystem.IsWindows() ? file.Path.ToUpperInvariant() : file.Path;
        return Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(identity))).ToLowerInvariant();
    }

    private DocumentFile ValidateFile(DocumentFile file)
    {
        DocumentFile described = Describe(file.Path);
        if (described.Kind != file.Kind) throw new ArgumentException("The document kind does not match its extension.", nameof(file));
        return described;
    }

    private static DocumentFile Describe(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!TryKind(fullPath, out var kind)) throw new ArgumentException("Choose a Markdown, LaTeX, Typst, or BibTeX document.", nameof(path));
        EnsureNoLinks(fullPath);
        return new DocumentFile(fullPath, kind, Path.GetFileName(fullPath));
    }

    private string WorkspacePath(params string[] parts)
    {
        string path = Path.GetFullPath(Path.Combine([Root, .. parts]));
        string relative = Path.GetRelativePath(Root, path);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException("The path escapes its workspace.");
        EnsureNoLinks(path);
        return path;
    }

    private string EnsureDirectory(params string[] parts)
    {
        string path = WorkspacePath(parts);
        Directory.CreateDirectory(path);
        EnsureNoLinks(path);
        return path;
    }

    private static void EnsureNoLinks(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current != null)
        {
            FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (entry.LinkTarget != null || (entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) != 0))
                throw new IOException("Document storage cannot use symbolic links or linked directories: " + current);
            current = Path.GetDirectoryName(current);
        }
    }

    private static DiskSnapshot? ReadDisk(string path)
    {
        byte[]? bytes = ReadBytes(path, MaximumDocumentBytes);
        if (bytes == null) return null;
        int offset = bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0;
        try { return new DiskSnapshot(Utf8.GetString(bytes, offset, bytes.Length - offset), Revision(bytes)); }
        catch (DecoderFallbackException error) { throw new InvalidDataException("The document is not valid UTF-8.", error); }
    }

    private static byte[]? ReadBytes(string path, int maximumBytes)
    {
        EnsureNoLinks(path);
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length > maximumBytes) throw new IOException("The file exceeds its size limit.");
            byte[] bytes = new byte[checked((int)input.Length)];
            input.ReadExactly(bytes);
            if (input.ReadByte() != -1) throw new IOException("The file changed while being read.");
            return bytes;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static bool TryWriteNew(string path, string text)
    {
        if (File.Exists(path)) return false;
        try { WriteAtomic(path, Encode(text), overwrite: false); return true; }
        catch (IOException) when (File.Exists(path)) { return false; }
    }

    private static void WriteAtomic(string path, byte[] bytes, bool overwrite, Action? beforePublish = null)
    {
        EnsureNoLinks(path);
        string directory = Path.GetDirectoryName(path) ?? throw new IOException("The destination has no parent directory.");
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("The destination directory no longer exists.");
        string temporary = Path.Combine(directory, ".texbb-write-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            if (!OperatingSystem.IsWindows() && overwrite && File.Exists(path))
                File.SetUnixFileMode(temporary, File.GetUnixFileMode(path));
            beforePublish?.Invoke();
            EnsureNoLinks(path);
            File.Move(temporary, path, overwrite);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static byte[] Encode(string text)
    {
        if (Utf8.GetByteCount(text) > MaximumDocumentBytes) throw new IOException("The document exceeds its size limit.");
        return Utf8.GetBytes(text);
    }

    private static string Revision(byte[] bytes) => Convert.ToBase64String(SHA256.HashData(bytes));
    private static string TextRevision(string text) => Revision(Utf8.GetBytes(text));

    private static long DocumentNumber(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        const string prefix = "current_board_";
        return name.StartsWith(prefix, StringComparison.Ordinal) &&
            long.TryParse(name.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out long number) && number > 0
            ? number : long.MaxValue;
    }

    private static bool TryKind(string path, out DocumentKind kind)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".md": case ".markdown": kind = DocumentKind.Markdown; return true;
            case ".tex": kind = DocumentKind.Latex; return true;
            case ".typ": kind = DocumentKind.Typst; return true;
            case ".bib": kind = DocumentKind.BibTeX; return true;
            default: kind = default; return false;
        }
    }

    private static string Extension(DocumentKind kind) => kind switch
    {
        DocumentKind.Markdown => ".md", DocumentKind.Latex => ".tex", DocumentKind.Typst => ".typ", DocumentKind.BibTeX => ".bib",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string Template(DocumentKind kind) => kind switch
    {
        DocumentKind.Markdown => "# Title\n\n",
        DocumentKind.Latex => "\\documentclass{article}\n\\usepackage{amsmath,amssymb}\n\n\\begin{document}\n\\section{Title}\n\n\\end{document}\n",
        DocumentKind.Typst => "= Title\n\n",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private sealed record DiskSnapshot(string Text, string Revision);

    private sealed class RecoveryRecord
    {
        public int Version { get; set; }
        public string Target { get; set; } = "";
        public string BaseRevision { get; set; } = "";
        public string DraftRevision { get; set; } = "";
        public string DraftText { get; set; } = "";
        public DateTimeOffset SavedAt { get; set; }
    }
}
