using System.Text;

namespace Nordvik.Shared;

/// <summary>
/// Genera PDF de verdad (PDF 1.4, Helvetica, A4) sin librerías: suficiente para contratos y extractos de prueba.
/// Los textos se escriben en WinAnsi (å, ä, ö, é, ñ funcionan); lo que no cabe se sustituye por '?'.
/// </summary>
public static class PdfWriter
{
    private static readonly Encoding Latin1 = Encoding.Latin1;

    private static string Esc(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            var c = ch > 255 ? '?' : ch;
            if (c is '(' or ')' or '\\') sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static IEnumerable<string> Wrap(string line, int width = 95)
    {
        if (line.Length <= width) { yield return line; yield break; }
        var words = line.Split(' ');
        var current = new StringBuilder();
        foreach (var w in words)
        {
            if (current.Length + w.Length + 1 > width && current.Length > 0) { yield return current.ToString(); current.Clear(); }
            if (current.Length > 0) current.Append(' ');
            current.Append(w);
        }
        if (current.Length > 0) yield return current.ToString();
    }

    public static byte[] Create(string title, IEnumerable<string> lines, string? footer = null)
    {
        var all = lines.SelectMany(l => Wrap(l)).ToList();
        const int perPage = 52;
        var pages = Math.Max(1, (int)Math.Ceiling(all.Count / (double)perPage));
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "", // páginas, se rellena después
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
        };
        var kids = new List<int>();
        for (var p = 0; p < pages; p++)
        {
            var sb = new StringBuilder("BT /F2 14 Tf 50 800 Td (" + Esc(title) + ") Tj ET\n");
            var y = 770;
            foreach (var l in all.Skip(p * perPage).Take(perPage))
            {
                sb.Append($"BT /F1 10 Tf 50 {y} Td (" + Esc(l) + ") Tj ET\n");
                y -= 14;
            }
            sb.Append($"BT /F1 8 Tf 50 30 Td (" + Esc((footer ?? "Nordvik Wealth & Pension · documento de pruebas") + $" · página {p + 1}/{pages}") + ") Tj ET\n");
            var stream = sb.ToString();
            objects.Add($"<< /Length {Latin1.GetByteCount(stream)} >>\nstream\n{stream}endstream");
            var contentId = objects.Count;
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents {contentId} 0 R /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> >>");
            kids.Add(objects.Count);
        }
        objects[1] = $"<< /Type /Pages /Kids [{string.Join(" ", kids.Select(k => k + " 0 R"))}] /Count {pages} >>";
        using var ms = new MemoryStream();
        void W(string s) { var b = Latin1.GetBytes(s); ms.Write(b, 0, b.Length); }
        W("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(ms.Position);
            W($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = ms.Position;
        W($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) W($"{o:D10} 00000 n \n");
        W($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /Info << /Producer (Nordvik render) >> >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    public static bool LooksLikePdf(byte[] data) => data.Length > 8 && data[0] == '%' && data[1] == 'P' && data[2] == 'D' && data[3] == 'F';
    public static int PageCount(byte[] data) => System.Text.RegularExpressions.Regex.Matches(Latin1.GetString(data), @"/Type /Page\b").Count;
}
