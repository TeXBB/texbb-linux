using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using TeXBB.Core.Tools;

namespace TeXBB.Core.Tests;

// Opt-in integration check. It uses real pdfinfo/pdftoppm on PATH, not fixtures.
internal static class PdfSmoke
{
    public static async Task<int> RunAsync(string directory)
    {
        try
        {
            string root = Path.GetFullPath(directory);
            if (Directory.Exists(root)) throw new IOException("Use a fresh PDF fixture directory.");
            Directory.CreateDirectory(root);
            string pdf = Path.Combine(root, "two pages 한글.pdf");
            WriteFixture(pdf);
            int pages = await PdfService.GetPageCountAsync(pdf);
            if (pages != 2) throw new InvalidDataException("Expected two PDF pages.");
            string first = await PdfService.RenderPageAsync(pdf, 1, root, 1);
            string second = await PdfService.RenderPageAsync(pdf, 2, root, 1);
            foreach (string image in new[] { first, second })
            {
                byte[] bytes = await File.ReadAllBytesAsync(image);
                int width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
                int height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
                if (width != 1024 || height != 768)
                    throw new InvalidDataException($"Expected a 1024x768 rendered page; got {width}x{height}.");
            }
            byte[] firstHash = SHA256.HashData(await File.ReadAllBytesAsync(first));
            byte[] secondHash = SHA256.HashData(await File.ReadAllBytesAsync(second));
            if (firstHash.SequenceEqual(secondHash))
                throw new InvalidDataException("Different pages rendered identically.");
            string report = $"PASS: real Poppler read 2 pages, rendered both at 1024x768, and produced distinct images.\n" +
                $"pdfinfo: {ToolDiscovery.Find("pdfinfo")}\npdftoppm: {ToolDiscovery.Find("pdftoppm")}\n" +
                $"First: {first}\nSecond: {second}\n";
            await File.WriteAllTextAsync(Path.Combine(root, "results.txt"), report);
            Console.WriteLine(report);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void WriteFixture(string path)
    {
        const string red = "1 0 0 rg 40 40 320 220 re f\n";
        const string green = "0 0.6 0.3 rg 80 80 240 140 re f\n";
        string Stream(string text) => $"<< /Length {Encoding.ASCII.GetByteCount(text)} >>\nstream\n{text}endstream";
        string[] objects = [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 300] /Resources << >> /Contents 5 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 300] /Resources << >> /Contents 6 0 R >>",
            Stream(red), Stream(green)
        ];
        var text = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int index = 0; index < objects.Length; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(text.ToString()));
            text.Append($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }
        int xref = Encoding.ASCII.GetByteCount(text.ToString());
        text.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets) text.Append($"{offset:D10} 00000 n \n");
        text.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllText(path, text.ToString(), Encoding.ASCII);
    }
}
