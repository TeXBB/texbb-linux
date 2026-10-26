using TeXBB.Core.Tests;

if (ToolTests.TryRunFixture(args, out int fixtureExitCode)) return fixtureExitCode;
if (args.Length == 2 && args[0] == "--pdf-smoke") return await PdfSmoke.RunAsync(args[1]);
if (args.Length != 1) { Console.Error.WriteLine("Usage: TeXBB.Core.Tests <fresh-fixture-directory>"); return 2; }
try
{
    string root = Path.GetFullPath(args[0]);
    if (Directory.Exists(root)) throw new IOException("Use a fresh fixture directory.");
    Directory.CreateDirectory(root);
    int checks = DocumentTests.Run(Path.Combine(root, "documents"));
    checks += PreviewTests.Run(Path.Combine(root, "preview"));
    checks += ToolTests.Run(Path.Combine(root, "tools"));
    Console.WriteLine($"PASS: {checks} portable core checks on {System.Runtime.InteropServices.RuntimeInformation.OSDescription}.");
    return 0;
}
catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
