using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using Sprk.Bff.Api.Services.Compose.Operations;

namespace ComposeApplierSpike;

// THROWAWAY spike driver (task 005). Applies a small task-003 op set to the CIPO corpus doc via the
// build-on-OpenXML-SDK candidate (Candidate B) and verifies placement by paraId+run position with
// ZERO text-search. Candidate A (Docxodus) is evaluated by API/footprint analysis (see
// notes/patch-engine-ab-decision.md) — it is a whole-document WmlComparer, not an offset applier,
// so there is nothing offset-addressed to drive here.

internal static class Program
{
    private const string W14 = "http://schemas.microsoft.com/office/word/2010/wordml";

    private static int Main(string[] args)
    {
        var inPath = args.Length > 0
            ? args[0]
            : @"c:\code_files\spaarke-wt-spaarkeai-compose-r4\tests\fixtures\compose-corpus\PAT 109270W-1 - CLAIMS track changes vs US12470413 claims(206092900.1).docx";
        var outPath = args.Length > 1
            ? args[1]
            : Path.Combine(Path.GetTempPath(), "cipo-spike-openxml-out.docx");

        var source = File.ReadAllBytes(inPath);
        Console.WriteLine($"Loaded CIPO doc: {source.Length} bytes");

        // -- The op set (built directly against the REAL task-003 schema; no AI, no text-search) --
        // Anchors verified against the CIPO document.xml (interior paraIds + run-local offsets):
        //   712269E5 run[0]="5. The method of claim 1, wherein"  run[2]=" comprises one or more members."
        //   5D98777E run[0]="1. A computer implemented method comprising:"
        var log = new ComposeOperationLog
        {
            Operations = new ComposeOperation[]
            {
                // 1) INTERIOR insertText: split run[0] at run-local offset 16 (after "5. The method of")
                new InsertTextOperation
                {
                    ParaId = "712269E5",
                    At = new ComposeRunPoint(RunIndex: 0, Offset: 16),
                    Text = " AMENDED",
                },
                // 2) INTERIOR deleteRange in a DISTINCT paragraph (410C9E5F) so no intra-paragraph
                //    index drift from op 1. 410C9E5F run[2] run-local [1,19) == "asset information ".
                //    (Multiple ops on ONE paragraph need anchor rebasing — see decision-doc finding.)
                new DeleteRangeOperation
                {
                    ParaId = "410C9E5F",
                    Range = new ComposeRunRange(
                        new ComposeRunPoint(2, 1),
                        new ComposeRunPoint(2, 19)),
                },
                // 3) Para-mark deletion probe (hardest edge): content w:del + w:pPr/w:rPr/w:del
                new DeleteParagraphOperation { ParaId = "5D98777E" },
            },
        };

        byte[] outBytes;
        try
        {
            outBytes = SpikeOpenXmlApplier.Apply(source, log);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL: applier threw: {ex}");
            return 2;
        }

        File.WriteAllBytes(outPath, outBytes);
        Console.WriteLine($"Wrote output: {outPath} ({outBytes.Length} bytes)");

        return Verify(outBytes, outPath) ? 0 : 3;
    }

    private static bool Verify(byte[] outBytes, string outPath)
    {
        using var ms = new MemoryStream(outBytes);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        var body = doc.MainDocumentPart!.Document!.Body!;

        // Resolve the targeted paragraphs by paraId (ZERO text-search).
        var p712 = FindByParaId(body, "712269E5");
        var p410 = FindByParaId(body, "410C9E5F");
        var p5d9 = FindByParaId(body, "5D98777E");

        var ok = true;

        // (a) insertText landed as native w:ins carrying " AMENDED" INSIDE paraId 712269E5, and the
        //     run immediately BEFORE the w:ins ends with "of" (i.e. it split run[0] at offset 16).
        var ins = p712?.Descendants<InsertedRun>()
            .FirstOrDefault(i => i.InnerText == " AMENDED");
        var insOk = ins is not null;
        var splitOk = ins?.PreviousSibling<Run>()?.InnerText.EndsWith("of", StringComparison.Ordinal) == true;
        Report("insertText -> w:ins \" AMENDED\" inside paraId 712269E5", insOk);
        Report("  split boundary: preceding run ends at offset 16 (\"...of\")", splitOk);
        ok &= insOk && splitOk;

        // (b) deleteRange landed as native w:del carrying "asset information " (w:delText) in paraId 410C9E5F.
        var del = p410?.Descendants<DeletedRun>()
            .FirstOrDefault(d => d.InnerText == "asset information ");
        var delOk = del is not null && del.Descendants<DeletedText>().Any();
        Report("deleteRange -> w:del \"asset information \" (w:delText) inside paraId 410C9E5F", delOk);
        ok &= delOk;

        // (c) para-mark deletion probe: w:pPr/w:rPr/w:del present on paraId 5D98777E.
        var markDel = p5d9?.ParagraphProperties?.ParagraphMarkRunProperties?.GetFirstChild<Deleted>();
        var markOk = markDel is not null;
        Report("para-mark deletion -> w:pPr/w:rPr/w:del on paraId 5D98777E", markOk);
        ok &= markOk;

        // (d) OpenXmlValidator: 0 errors (opens-in-Word gate).
        var errs = new OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2019)
            .Validate(doc).ToList();
        var validOk = errs.Count == 0;
        Report($"OpenXmlValidator: {errs.Count} error(s)", validOk);
        foreach (var e in errs.Take(10))
        {
            Console.WriteLine($"    - {e.Description} @ {e.Path?.XPath}");
        }

        ok &= validOk;

        Console.WriteLine();
        Console.WriteLine(ok
            ? $"RESULT: PASS — interior edits landed at paraId+offset with zero text-search; output valid. ({outPath})"
            : "RESULT: FAIL — see checks above.");
        return ok;
    }

    private static Paragraph? FindByParaId(Body body, string paraId) =>
        body.Descendants<Paragraph>().FirstOrDefault(p =>
            p.GetAttributes().Any(a => a.LocalName == "paraId" && a.NamespaceUri == W14 && a.Value == paraId));

    private static void Report(string label, bool ok) =>
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {label}");
}
