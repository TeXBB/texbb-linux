namespace TeXBB.Core.Documents;

public enum DocumentKind { Markdown, Latex, Typst, BibTeX }

public sealed record DocumentFile(string Path, DocumentKind Kind, string Name);

/// <summary>A recovery draft is offered to the editor; reading it never changes its source.</summary>
public sealed record RecoveryDraft(
    DocumentFile File,
    string Text,
    DateTimeOffset SavedAt,
    bool MatchesCurrentFile);
