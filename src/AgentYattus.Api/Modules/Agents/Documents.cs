using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using UglyToad.PdfPig;
using Word = DocumentFormat.OpenXml.Wordprocessing;

namespace AgentYattus.Api.Modules.Agents;

/// <summary>
/// PDF, Word et Excel en texte, pour read_file (leçon 114). Un document est une donnée comme un autre fichier :
/// son texte n'est jamais une consigne. Tout est borné : pages, cellules, caractères, taille des parties décompressées.
/// </summary>
internal static class Documents
{
    /// <summary>Au-delà, un document n'est pas ouvert : on ne décompresse pas des centaines de Mo pour un agent.</summary>
    public const long MaxDocumentBytes = 20 * 1024 * 1024;

    private const int MaxPages = 300;
    private const int MaxRows = 5000;

    public static bool IsDocument(string path) => Path.GetExtension(path).ToLowerInvariant() is ".pdf" or ".docx" or ".xlsx";

    /// <summary>Le texte du document, coupé à <paramref name="maxChars"/> caractères avec une note qui le dit.</summary>
    public static string ExtractText(string path, int maxChars)
    {
        var text = new Bounded(maxChars);
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".pdf":
                Pdf(path, text);
                break;
            case ".docx":
                Docx(path, text);
                break;
            case ".xlsx":
                Xlsx(path, text);
                break;
        }
        return text.ToString();
    }

    // Une archive Office dont une partie dépasse cette taille une fois décompressée est refusée (bombe de décompression).
    private static readonly OpenSettings Office = new() { MaxCharactersInPart = 20_000_000 };

    private static void Pdf(string path, Bounded text)
    {
        using var pdf = PdfDocument.Open(path);
        foreach (var page in pdf.GetPages().Take(MaxPages))
        {
            if (!text.Line($"--- Page {page.Number} ---") || !text.Line(page.Text))
                return;
        }
        if (pdf.NumberOfPages > MaxPages)
            text.Line($"[… {pdf.NumberOfPages - MaxPages} pages de plus non lues]");
    }

    private static void Docx(string path, Bounded text)
    {
        using var doc = WordprocessingDocument.Open(path, false, Office);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null)
            return;
        foreach (var block in body.ChildElements)
        {
            var line = block switch
            {
                Word.Paragraph p => p.InnerText,
                // Un tableau : une ligne par rangée, les cellules séparées par des tabulations.
                Word.Table t => string.Join('\n', t.Elements<Word.TableRow>().Select(r => string.Join('\t', r.Elements<Word.TableCell>().Select(c => c.InnerText)))),
                _ => null,
            };
            if (line is not null && !text.Line(line))
                return;
        }
    }

    private static void Xlsx(string path, Bounded text)
    {
        using var doc = SpreadsheetDocument.Open(path, false, Office);
        if (doc.WorkbookPart is not { } workbook)
            return;
        var shared = workbook.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(s => s.InnerText).ToList() ?? [];
        foreach (var sheet in workbook.Workbook?.Sheets?.Elements<Sheet>() ?? [])
        {
            if (sheet.Id?.Value is not { } id || workbook.GetPartById(id) is not WorksheetPart part)
                continue;
            if (!text.Line($"--- Feuille « {sheet.Name} » ---"))
                return;
            var rows = 0;
            foreach (var row in part.Worksheet?.Descendants<Row>() ?? [])
            {
                if (++rows > MaxRows)
                {
                    text.Line($"[… lignes suivantes non lues, {MaxRows} au plus par feuille]");
                    break;
                }
                var cells = row.Elements<Cell>().Select(c => Value(c, shared));
                if (!text.Line(string.Join('\t', cells)))
                    return;
            }
        }
    }

    // La valeur affichable d'une cellule : texte partagé, texte en ligne, ou la valeur brute (nombre, date en nombre de jours).
    private static string Value(Cell cell, List<string> shared) =>
        cell.DataType?.Value == CellValues.SharedString && int.TryParse(cell.CellValue?.Text, out var i) && i >= 0 && i < shared.Count
            ? shared[i]
            : cell.DataType?.Value == CellValues.InlineString ? cell.InlineString?.InnerText ?? ""
            : cell.CellValue?.Text ?? "";

    /// <summary>Accumule des lignes jusqu'à la limite ; ensuite, une seule note dit que le texte est coupé.</summary>
    private sealed class Bounded(int max)
    {
        private readonly StringBuilder _text = new();
        private bool _full;

        public bool Line(string line)
        {
            if (_full)
                return false;
            if (_text.Length + line.Length + 1 > max)
            {
                _text.Append(line.AsSpan(0, Math.Max(0, max - _text.Length - 1))).Append("\n[… texte coupé : document trop long]");
                _full = true;
                return false;
            }
            _text.Append(line).Append('\n');
            return true;
        }

        public override string ToString() => _text.ToString();
    }
}
