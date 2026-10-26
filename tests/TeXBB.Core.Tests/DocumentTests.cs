using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TeXBB.Core.Documents;

namespace TeXBB.Core.Tests;

public static class DocumentTests
{
    public static int Run(string fixtureRoot)
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("Documents: " + message);
        }
        void Throws<T>(Action action, string message) where T : Exception
        {
            bool threw = false;
            try { action(); }
            catch (T) { threw = true; }
            Check(threw, message);
        }

        string fixtures = Path.Combine(Path.GetFullPath(fixtureRoot), "documents-" + Guid.NewGuid().ToString("N"));
        string workspace = Path.Combine(fixtures, "workspace");
        var store = new WorkspaceStore(workspace);
        Check(store.Root == workspace && Directory.Exists(Path.Combine(workspace, "history", "recovery")),
            "A workspace has its own current, reference and recovery locations.");
        Check(store.ListDocuments().Count == 0, "Opening an empty workspace does not manufacture source files.");

        DocumentBuffer markdown = store.CreateDocument(DocumentKind.Markdown);
        string initialMarkdown = markdown.Text;
        Check(markdown.File.Path == Path.Combine(workspace, "current", "current_board_1.md") &&
            markdown.File.Kind == DocumentKind.Markdown && !markdown.IsDirty, "New Markdown uses the canonical name and starts clean.");
        DocumentBuffer second = store.CreateDocument(DocumentKind.Markdown);
        Check(second.File.Name == "current_board_2.md" && File.ReadAllText(markdown.File.Path) == initialMarkdown,
            "Creating another document never overwrites the first source.");
        DocumentBuffer latex = store.CreateDocument(DocumentKind.Latex);
        Check(latex.File.Name == "current_board_1.tex" && latex.Text.Contains("\\documentclass{article}") &&
            latex.Text.Contains("\\begin{document}") && latex.Text.Contains("\\end{document}"), "LaTeX starts as a complete article.");
        DocumentBuffer typst = store.CreateDocument(DocumentKind.Typst);
        Check(typst.File.Name == "current_board_1.typ" && typst.Text.StartsWith("= ", StringComparison.Ordinal), "Typst starts with a valid heading.");
        DocumentBuffer bibliography = store.CreateDocument(DocumentKind.BibTeX);
        bibliography.Text = "@article{KeepMe, title={An existing reference}}\n";
        store.SaveDocument(bibliography);
        Check(store.CreateDocument(DocumentKind.BibTeX).Text == bibliography.Text &&
            bibliography.File.Path == Path.Combine(workspace, "reference", "reference.bib"),
            "The canonical bibliography is reopened without resetting existing entries.");
        Check(store.ListDocuments().Count == 5, "Only source documents are listed; histories do not appear as editable documents.");

        markdown.Text = "# 한글\r\n\r\nα + β\n  trailing spaces  \n";
        Check(markdown.IsDirty, "Editing marks the buffer dirty.");
        store.SaveDocument(markdown);
        Check(!markdown.IsDirty && store.OpenDocument(markdown.File.Path).Text == markdown.Text,
            "Saving preserves Unicode, mixed line endings and whitespace and clears the dirty state.");
        Check(Directory.EnumerateFiles(Path.Combine(workspace, "history", "md")).Any(path => File.ReadAllText(path) == initialMarkdown),
            "An explicit save preserves the previous disk source in history.");
        string savedMarkdown = markdown.Text;
        markdown.Text += "draft only";
        string draftHistory = store.SaveHistory(markdown);
        Check(File.ReadAllText(draftHistory) == markdown.Text && File.ReadAllText(markdown.File.Path) == savedMarkdown && markdown.IsDirty,
            "A history snapshot stores the draft without modifying its source or dirty state.");
        markdown.Text = savedMarkdown;
        Check(!markdown.IsDirty, "Returning to the saved text clears the dirty state without another save.");

        DocumentBuffer writerA = store.OpenDocument(markdown.File.Path);
        DocumentBuffer writerB = store.OpenDocument(markdown.File.Path);
        writerA.Text = "Saved by the first editor\n";
        store.SaveDocument(writerA);
        writerB.Text = "Draft from the second editor\n";
        Throws<IOException>(() => store.SaveDocument(writerB), "A second editor cannot silently overwrite a newer disk revision.");
        Check(File.ReadAllText(markdown.File.Path) == writerA.Text && writerB.IsDirty,
            "A conflict leaves both the external version and the unsaved buffer intact.");
        store.SaveDocument(writerB, overwriteExternalChanges: true);
        Check(File.ReadAllText(markdown.File.Path) == writerB.Text && !writerB.IsDirty &&
            Directory.EnumerateFiles(Path.Combine(workspace, "history", "md")).Any(path => File.ReadAllText(path) == writerA.Text),
            "A confirmed overwrite preserves the newer external version in history before saving.");

        DocumentBuffer recovered = store.OpenDocument(second.File.Path);
        string original = recovered.Text;
        recovered.Text = "Unsaved recovery α\n";
        store.SaveRecovery(recovered);
        RecoveryDraft? recovery = store.GetRecovery(recovered.File);
        Check(recovery != null && recovery.Text == recovered.Text && recovery.MatchesCurrentFile && recovery.SavedAt != default,
            "A verified recovery draft carries its text, timestamp and baseline match.");
        Check(File.ReadAllText(recovered.File.Path) == original && store.OpenDocument(recovered.File.Path).Text == original,
            "Neither capturing nor reading a recovery draft automatically changes the source.");
        File.WriteAllText(recovered.File.Path, "Changed externally\n", new UTF8Encoding(false));
        Check(store.GetRecovery(recovered.File) is { MatchesCurrentFile: false },
            "A recovery draft remains available but identifies an externally changed source.");
        Throws<IOException>(() => store.SaveDocument(recovered), "Recovery does not bypass normal save conflict protection.");
        store.SaveDocument(recovered, overwriteExternalChanges: true);
        Check(store.GetRecovery(recovered.File) == null && File.ReadAllText(recovered.File.Path) == recovered.Text,
            "An explicit save satisfies and removes its matching recovery draft.");

        recovered.Text = "A later unsaved draft\n";
        store.SaveRecovery(recovered);
        string recoveryPath = FindRecovery(workspace, recovered.File.Path);
        JsonObject record = JsonNode.Parse(File.ReadAllText(recoveryPath))!.AsObject();
        record["DraftText"] = "Tampered text";
        string damagedRecord = record.ToJsonString();
        File.WriteAllText(recoveryPath, damagedRecord, new UTF8Encoding(false));
        Throws<InvalidDataException>(() => store.GetRecovery(recovered.File), "A recovery draft with a mismatched digest is rejected.");
        Check(File.ReadAllText(recoveryPath) == damagedRecord, "A damaged recovery record is retained for manual inspection.");

        string externalDirectory = Path.Combine(fixtures, "external project");
        Directory.CreateDirectory(externalDirectory);
        string externalPath = Path.Combine(externalDirectory, "paper.tex");
        File.WriteAllText(externalPath, "\\input{sections/introduction}\n", new UTF8Encoding(true));
        DocumentBuffer external = store.OpenDocument(externalPath);
        Check(external.File.Path == externalPath && external.Text == "\\input{sections/introduction}\n",
            "Explicit external selection retains its path and relative TeX input text and accepts UTF-8 BOM.");
        external.Text += "% unsaved external draft\n";
        store.SaveRecovery(external);
        string externalRecovery = FindRecovery(workspace, externalPath);
        Check(Path.GetDirectoryName(externalRecovery) == Path.Combine(workspace, "history", "recovery") &&
            !store.ListDocuments().Any(file => file.Path == externalPath),
            "External recovery remains inside this workspace and external files do not enter its document list.");
        Check(!File.ReadAllText(externalPath).Contains("unsaved external draft"), "External selection and recovery never auto-save the original.");
        store.SaveDocument(external);
        Check(File.ReadAllText(externalPath) == external.Text && !external.IsDirty,
            "An explicit external save writes the selected file and clears its dirty state.");

        string anotherDirectory = Path.Combine(fixtures, "another external project");
        Directory.CreateDirectory(anotherDirectory);
        string anotherPath = Path.Combine(anotherDirectory, "paper.tex");
        File.WriteAllText(anotherPath, "another original\n");
        DocumentBuffer another = store.OpenDocument(anotherPath);
        external.Text = "first paper draft";
        another.Text = "second paper draft";
        store.SaveRecovery(external);
        store.SaveRecovery(another);
        Check(store.GetRecovery(external.File)?.Text == "first paper draft" && store.GetRecovery(another.File)?.Text == "second paper draft" &&
            FindRecovery(workspace, externalPath) != FindRecovery(workspace, anotherPath),
            "External documents with identical filenames have distinct recovery identities.");

        File.Delete(anotherPath);
        Check(store.GetRecovery(another.File) is { MatchesCurrentFile: false }, "A deleted source does not hide its surviving recovery draft.");
        Throws<IOException>(() => store.SaveDocument(another), "A deleted source requires explicit confirmation before recreation.");
        store.SaveDocument(another, overwriteExternalChanges: true);
        Check(File.ReadAllText(anotherPath) == another.Text, "Explicit confirmation can recreate a deleted source from its buffer.");

        string invalidPath = Path.Combine(externalDirectory, "invalid.md");
        byte[] invalidUtf8 = [0xC3, 0x28];
        File.WriteAllBytes(invalidPath, invalidUtf8);
        Throws<InvalidDataException>(() => store.OpenDocument(invalidPath), "Invalid UTF-8 is rejected instead of silently replacing characters.");
        Check(File.ReadAllBytes(invalidPath).SequenceEqual(invalidUtf8), "Opening invalid input never modifies its bytes.");
        Throws<ArgumentException>(() => store.OpenDocument(Path.Combine(externalDirectory, "image.png")), "Unsupported file kinds are rejected.");
        Throws<ArgumentOutOfRangeException>(() => store.CreateDocument((DocumentKind)999), "Invalid document kinds do not create files.");
        Check(!Directory.EnumerateFiles(fixtures, ".texbb-write-*.tmp", SearchOption.AllDirectories).Any(),
            "Successful and rejected writes leave no temporary publication files.");

        if (!OperatingSystem.IsWindows())
        {
            string caseUpper = Path.Combine(externalDirectory, "Case.md");
            string caseLower = Path.Combine(externalDirectory, "case.md");
            File.WriteAllText(caseUpper, "upper");
            File.WriteAllText(caseLower, "lower");
            File.SetUnixFileMode(caseUpper, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            DocumentBuffer upper = store.OpenDocument(caseUpper), lower = store.OpenDocument(caseLower);
            upper.Text = "upper draft";
            lower.Text = "lower draft";
            store.SaveRecovery(upper);
            store.SaveRecovery(lower);
            Check(store.GetRecovery(upper.File)?.Text == "upper draft" && store.GetRecovery(lower.File)?.Text == "lower draft",
                "Case-distinct Linux files retain distinct source and recovery identities.");
            store.SaveDocument(upper);
            Check(File.GetUnixFileMode(caseUpper) == (UnixFileMode.UserRead | UnixFileMode.UserWrite),
                "Atomic saves preserve the existing Unix source permissions.");
            string link = Path.Combine(workspace, "current", "linked.md");
            File.CreateSymbolicLink(link, caseUpper);
            Throws<IOException>(() => store.OpenDocument(link), "A symbolic-link document cannot redirect an atomic source save.");
            string linkedRoot = Path.Combine(fixtures, "linked workspace");
            Directory.CreateSymbolicLink(linkedRoot, workspace);
            Throws<IOException>(() => new WorkspaceStore(linkedRoot), "A linked workspace cannot redirect recovery or history writes.");
            Check(File.ReadAllText(caseUpper) == "upper draft", "Rejected links leave their external target unchanged.");
        }
        else Console.WriteLine("Documents: Linux case-sensitive and symbolic-link cases require the Linux test run.");

        return checks;
    }

    private static string FindRecovery(string workspace, string target)
    {
        return Directory.EnumerateFiles(Path.Combine(workspace, "history", "recovery"), "*.v1.json")
            .Single(path =>
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                return document.RootElement.GetProperty("Target").GetString() == target;
            });
    }
}
