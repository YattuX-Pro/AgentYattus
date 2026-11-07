using AgentYattus.Api.Modules.Agents;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AgentYattus.Api.Tests;

/// <summary>read_file lit aussi les PDF, les Word et les Excel, convertis en texte et bornés (leçon 114).</summary>
public sealed class DocumentsTests
{
    private readonly string _workspace = Directory.CreateTempSubdirectory("yattus-docs-").FullName;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_pdf_is_read_page_by_page()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4).AddText("Devis numero 42", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 750), font);
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4).AddText("Total 1200 euros", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 750), font);
        await File.WriteAllBytesAsync(Path.Combine(_workspace, "devis.pdf"), builder.Build(), Ct);

        var text = await new WorkspaceTools(_workspace).ReadFile("devis.pdf", Ct);

        Assert.Contains("--- Page 1 ---", text);
        Assert.Contains("Devis numero 42", text);
        Assert.Contains("Total 1200 euros", text);
    }

    [Fact]
    public async Task A_word_document_keeps_its_paragraphs_and_tables()
    {
        using (var doc = WordprocessingDocument.Create(Path.Combine(_workspace, "contrat.docx"), WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new W.Document(new W.Body(
                new W.Paragraph(new W.Run(new W.Text("Article 1 : objet du contrat."))),
                new W.Table(new W.TableRow(
                    new W.TableCell(new W.Paragraph(new W.Run(new W.Text("Prix")))),
                    new W.TableCell(new W.Paragraph(new W.Run(new W.Text("300 €"))))))));
        }

        var text = await new WorkspaceTools(_workspace).ReadFile("contrat.docx", Ct);

        Assert.Equal("Article 1 : objet du contrat.\nPrix\t300 €\n", text);
    }

    [Fact]
    public async Task An_excel_workbook_becomes_one_line_per_row()
    {
        using (var doc = SpreadsheetDocument.Create(Path.Combine(_workspace, "ventes.xlsx"), SpreadsheetDocumentType.Workbook))
        {
            var workbook = doc.AddWorkbookPart();
            workbook.Workbook = new Workbook();
            var sheet = workbook.AddNewPart<WorksheetPart>();
            sheet.Worksheet = new Worksheet(new SheetData(
                new Row(new Cell { CellValue = new CellValue("Mois"), DataType = CellValues.String }, new Cell { CellValue = new CellValue("Ventes"), DataType = CellValues.String }),
                new Row(new Cell { CellValue = new CellValue("Janvier"), DataType = CellValues.String }, new Cell { CellValue = new CellValue(1200) })));
            workbook.Workbook.AppendChild(new Sheets(new Sheet { Id = workbook.GetIdOfPart(sheet), SheetId = 1, Name = "2026" }));
        }

        var text = await new WorkspaceTools(_workspace).ReadFile("ventes.xlsx", Ct);

        Assert.Equal("--- Feuille « 2026 » ---\nMois\tVentes\nJanvier\t1200\n", text);
    }

    [Fact]
    public async Task A_long_document_is_cut_and_says_so_and_a_broken_one_is_refused_cleanly()
    {
        using (var doc = WordprocessingDocument.Create(Path.Combine(_workspace, "long.docx"), WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new W.Document(new W.Body(Enumerable.Range(0, 200).Select(i => new W.Paragraph(new W.Run(new W.Text($"Paragraphe {i} assez long pour remplir.")))).ToArray<OpenXmlElement>()));
        }
        await File.WriteAllTextAsync(Path.Combine(_workspace, "faux.pdf"), "pas un pdf", Ct);

        var text = await new WorkspaceTools(_workspace, maxFileBytes: 500).ReadFile("long.docx", Ct);

        Assert.True(text.Length < 600);
        Assert.EndsWith("[… texte coupé : document trop long]", text);
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkspaceTools(_workspace).ReadFile("faux.pdf", Ct));
        Assert.StartsWith("Document illisible : faux.pdf", refused.Message);
    }

    [Fact]
    public async Task An_office_archive_announcing_too_much_once_unzipped_is_refused()
    {
        // Un faux .docx : une seule entrée de 150 Mo de zéros, qui se compresse en quelques centaines de Ko.
        var path = Path.Combine(_workspace, "bombe.docx");
        using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
        await using (var entry = zip.CreateEntry("word/document.xml", System.IO.Compression.CompressionLevel.SmallestSize).Open())
        {
            var zeros = new byte[1024 * 1024];
            for (var i = 0; i < 150; i++)
                await entry.WriteAsync(zeros, Ct);
        }

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkspaceTools(_workspace).ReadFile("bombe.docx", Ct));
        Assert.StartsWith("Document illisible : bombe.docx", refused.Message);
    }
}
