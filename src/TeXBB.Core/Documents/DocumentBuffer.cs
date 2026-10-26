namespace TeXBB.Core.Documents;

public sealed class DocumentBuffer
{
    private string text;
    private string savedText;

    internal DocumentBuffer(DocumentFile file, string text, string diskRevision)
    {
        File = file;
        this.text = text;
        savedText = text;
        DiskRevision = diskRevision;
    }

    public DocumentFile File { get; }

    public string Text
    {
        get => text;
        set => text = value ?? throw new ArgumentNullException(nameof(value));
    }

    public bool IsDirty => !string.Equals(text, savedText, StringComparison.Ordinal);

    internal string DiskRevision { get; private set; }
    internal string SavedText => savedText;

    internal void MarkSaved(string persistedText, string revision)
    {
        savedText = persistedText;
        DiskRevision = revision;
    }
}
