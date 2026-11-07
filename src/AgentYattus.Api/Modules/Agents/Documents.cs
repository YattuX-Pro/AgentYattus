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
    private const int MaxSheets = 50;

    /// <summary>Total décompressé d'une archive Office : au-delà, on ne l'ouvre pas (bombe de décompression).</summary>
    private const long MaxUnzippedBytes = 100 * 1024 * 1024;

    /// <summary>Le temps qu'un document peut prendre ; vérifié entre deux pages ou deux rangées.</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(15);

    public static bool IsDocument(string path) => Path.GetExtension(path).ToLowerInvariant() is ".pdf" or ".docx" or ".xlsx";

    /// <summary>Le texte du document, coupé à <paramref name="maxChars"/> caractères avec une note qui le dit.</summary>
    public static string ExtractText(string path, int maxChars, CancellationToken cancellationToken = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(Budget);
        var text = new Bounded(maxChars, budget.Token);
        if (Path.GetExtension(path).ToLowerInvariant() is ".docx" or ".xlsx")
            CheckUnzippedSize(path, budget.Token);
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

    /// <summary>
    /// Avant d'ouvrir l'archive : on la décompresse pour de bon, sans rien garder, en comptant les octets réellement
    /// produits. Les tailles annoncées dans l'archive peuvent mentir ; ce compte-là, non. Au-delà, on arrête tout de suite.
    /// </summary>
    private static void CheckUnzippedSize(string path, CancellationToken budget)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(path);
        if (zip.Entries.Count > 2000)
            throw new InvalidDataException("Archive avec trop d'entrées.");
        var total = 0L;
        var buffer = new byte[81920];
        foreach (var entry in zip.Entries)
        {
            using var stream = entry.Open();
            for (int read; (read = stream.Read(buffer, 0, buffer.Length)) > 0;)
            {
                total += read;
                if (total > MaxUnzippedBytes)
                    throw new InvalidDataException("Archive trop volumineuse une fois décompressée.");
                budget.ThrowIfCancellationRequested();
            }
        }
    }

    // Une archive Office dont une partie dépasse cette taille une fois décompressée est refusée (bombe de décompression).
    // 5 millions de caractères par partie : avec le compte total ci-dessus, la mémoire reste bornée avant la première vérification.
    private static readonly OpenSettings Office = new() { MaxCharactersInPart = 5_000_000 };

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
        foreach (var sheet in (workbook.Workbook?.Sheets?.Elements<Sheet>() ?? []).Take(MaxSheets))
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

    /// <summary>
    /// Écrit un .docx à partir d'un texte simple : « # » titre, « ## » sous-titre, « - » puce, une ligne vide sépare.
    /// Le texte est mis tel quel dans des runs : aucun XML ni champ Word ne peut être injecté par le contenu.
    /// </summary>
    public static void WriteDocx(string path, string content)
    {
        using var doc = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        var body = new Word.Body();
        foreach (var raw in content.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            var (text, size, bold) = line switch
            {
                _ when line.StartsWith("## ", StringComparison.Ordinal) => (line[3..], "28", true),
                _ when line.StartsWith("# ", StringComparison.Ordinal) => (line[2..], "36", true),
                _ when line.StartsWith("- ", StringComparison.Ordinal) => ("• " + line[2..], "22", false),
                _ => (line, "22", false),
            };
            var properties = new Word.RunProperties(new Word.FontSize { Val = size });
            if (bold)
                properties.Append(new Word.Bold());
            body.Append(new Word.Paragraph(new Word.Run(properties, new Word.Text(text) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve })));
        }
        main.Document = new Word.Document(body);
    }

    /// <summary>Accumule des lignes jusqu'à la limite ; ensuite, une seule note dit que le texte est coupé.</summary>
    private sealed class Bounded(int max, CancellationToken budget)
    {
        private readonly StringBuilder _text = new();
        private bool _full;

        public bool Line(string line)
        {
            // Le temps est écoulé (ou la mission arrêtée) : on rend ce qui a été lu, en le disant.
            if (!_full && budget.IsCancellationRequested)
            {
                _text.Append("[… lecture arrêtée : document trop long à lire]");
                _full = true;
            }
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
