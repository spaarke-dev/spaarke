// Spike 6 — reverse round-trip (Word edit -> webhook -> read) + re-anchor band tuning
//
// STANDALONE scratch prototype. Not part of Sprk.Bff.Api. Companion to
// projects/spaarkeai-compose-r2/notes/spikes/spike-6-word-roundtrip.md
//
// Part A: build a "base" docx (what Compose would have produced/loaded), then simulate
//          a Word-for-Web session editing it in place — adding a genuine w:comment,
//          a w:ins, and a w:del with a REAL human author (not "Spaarke AI"), mimicking
//          what the SPE webhook would hand back to the BFF after a user save.
// Part B: a DocxAnnotationReader-shaped parser that opens the "edited" docx and
//          recovers w:comment / w:ins / w:del with correct author + date — this is
//          the FR-25 read path validation.
// Part C: re-anchor confidence-band tuning. AnchoredAnnotation.anchor = { textPattern,
//          paragraphHint, spanId } (design.md line 551-560). This section implements a
//          content+structural scorer and runs it against 7 drift scenarios modeled on
//          realistic Word edits, to empirically test the ≥0.85 / 0.6-0.85 / <0.6 bands.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;

var outDir = AppContext.BaseDirectory;
var basePath = Path.Combine(outDir, "spike6-base.docx");
var editedPath = Path.Combine(outDir, "spike6-word-edited.docx");

Console.WriteLine("=== PART A: build base document + simulate Word-for-Web edit ===\n");
BuildBaseDocument(basePath);
Console.WriteLine($"Base document written: {basePath}");
ValidateDocx(basePath, "base");

SimulateWordEdit(basePath, editedPath);
Console.WriteLine($"Word-edited document written: {editedPath}");
ValidateDocx(editedPath, "word-edited");

Console.WriteLine("\n=== PART B: DocxAnnotationReader-shaped parse of the Word-edited file ===\n");
var recovered = ReadAnnotations(editedPath);
foreach (var a in recovered)
{
    Console.WriteLine($"  [{a.Kind}] id={a.Id} author=\"{a.Author}\" date={a.Date:O} text=\"{Trunc(a.Text)}\"");
}

Console.WriteLine("\n=== PART C: re-anchor confidence-band tuning ===\n");
RunBandTuning();

Console.WriteLine("\nDone.");

// ---------------------------------------------------------------------------
// PART A helpers
// ---------------------------------------------------------------------------

static void BuildBaseDocument(string path)
{
    var paragraphs = new[]
    {
        "This Master Services Agreement (the \"Agreement\") is entered into between the parties identified in the signature block.",
        "Indemnification is capped at fees paid by Client in the twelve months preceding the claim, except for breaches of confidentiality.",
        "Each Party shall perform its obligations under this Agreement in a professional and workmanlike manner consistent with industry standards.",
        "Termination of this Agreement requires thirty (30) days written notice delivered to the other Party's designated contact.",
        "Confidential Information disclosed by either Party under this Agreement shall not be shared with third parties absent written consent."
    };

    using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
    var mainPart = doc.AddMainDocumentPart();
    mainPart.Document = new Document();
    var body = mainPart.Document.AppendChild(new Body());
    foreach (var text in paragraphs)
    {
        body.AppendChild(new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve })));
    }
    body.AppendChild(new SectionProperties());
    mainPart.Document.Save();
}

static void SimulateWordEdit(string basePath, string editedPath)
{
    File.Copy(basePath, editedPath, overwrite: true);

    var wordUser = "Jordan Ellis";               // a real human editor, not "Spaarke AI"
    var whenUtc = new DateTime(2026, 7, 9, 15, 42, 0, DateTimeKind.Utc);

    using var doc = WordprocessingDocument.Open(editedPath, isEditable: true);
    var mainPart = doc.MainDocumentPart!;
    var body = mainPart.Document!.Body!;
    var paras = body.Elements<Paragraph>().ToList();

    // --- Edit 1: tracked INSERTION appended to the termination clause (paragraph index 3) ---
    var terminationPara = paras[3];
    var run = terminationPara.Elements<Run>().First();
    var originalText = run.GetFirstChild<Text>()!.Text;
    run.RemoveAllChildren<Text>();
    run.AppendChild(new Text(originalText) { Space = SpaceProcessingModeValues.Preserve });

    var insertion = new InsertedRun { Id = "500", Author = wordUser, Date = whenUtc };
    insertion.AppendChild(new Run(new Text(" This provision may also be invoked immediately upon a material breach that remains uncured for ten (10) days.")
        { Space = SpaceProcessingModeValues.Preserve }));
    terminationPara.AppendChild(insertion);

    // --- Edit 2: tracked DELETION inside the professional-manner clause (paragraph index 2) ---
    var professionalPara = paras[2];
    var profRun = professionalPara.Elements<Run>().First();
    var profText = profRun.GetFirstChild<Text>()!.Text;
    const string deletedPhrase = " consistent with industry standards";
    var keepText = profText.Replace(deletedPhrase, string.Empty);
    profRun.RemoveAllChildren<Text>();
    profRun.AppendChild(new Text(keepText) { Space = SpaceProcessingModeValues.Preserve });

    var deletion = new DeletedRun { Id = "501", Author = wordUser, Date = whenUtc };
    deletion.AppendChild(new Run(new DeletedText(deletedPhrase) { Space = SpaceProcessingModeValues.Preserve }));
    professionalPara.InsertAfter(deletion, profRun);

    // --- Edit 3: a native comment anchored to the indemnification clause (paragraph index 1) ---
    var indemPara = paras[1];
    var indemRun = indemPara.Elements<Run>().First();
    var indemText = indemRun.GetFirstChild<Text>()!.Text;
    indemPara.RemoveAllChildren<Run>();
    indemPara.AppendChild(new CommentRangeStart { Id = "0" });
    indemPara.AppendChild(new Run(new Text(indemText) { Space = SpaceProcessingModeValues.Preserve }));
    indemPara.AppendChild(new CommentRangeEnd { Id = "0" });
    indemPara.AppendChild(new Run(new CommentReference { Id = "0" }));

    var commentsPart = mainPart.AddNewPart<WordprocessingCommentsPart>();
    var comment = new Comment { Id = "0", Author = wordUser, Initials = "JE", Date = whenUtc };
    comment.AppendChild(new Paragraph(new Run(new Text(
        "Can we confirm the 12-month cap survives against gross negligence carve-outs too, or only ordinary breach?"))));
    commentsPart.Comments = new Comments(comment);
    commentsPart.Comments.Save();

    mainPart.Document.Save();
}

static void ValidateDocx(string path, string label)
{
    using var doc = WordprocessingDocument.Open(path, isEditable: false);
    var validator = new OpenXmlValidator(FileFormatVersions.Office2019);
    var errors = validator.Validate(doc).ToList();
    Console.WriteLine($"  OpenXmlValidator({label}): {errors.Count} error(s)");
    foreach (var e in errors.Take(10))
    {
        Console.WriteLine($"    - {e.Description} ({e.Path?.XPath})");
    }
}

static string Trunc(string s) => s.Length <= 70 ? s : s[..67] + "...";

// ---------------------------------------------------------------------------
// PART B: DocxAnnotationReader-shaped parser
// ---------------------------------------------------------------------------

static List<RecoveredAnnotation> ReadAnnotations(string path)
{
    var results = new List<RecoveredAnnotation>();
    using var doc = WordprocessingDocument.Open(path, isEditable: false);
    var mainPart = doc.MainDocumentPart!;
    var body = mainPart.Document!.Body!;

    // w:ins -> InsertedRun
    foreach (var ins in body.Descendants<InsertedRun>())
    {
        var text = string.Concat(ins.Descendants<Text>().Select(t => t.Text));
        results.Add(new RecoveredAnnotation(
            "w:ins", ins.Id?.Value ?? "?", ins.Author?.Value ?? "(none)",
            ParseDate(ins.Date), text));
    }

    // w:del -> DeletedRun (text lives in DeletedText, NOT Text — spike-5 gotcha confirmed on read side too)
    foreach (var del in body.Descendants<DeletedRun>())
    {
        var text = string.Concat(del.Descendants<DeletedText>().Select(t => t.Text));
        results.Add(new RecoveredAnnotation(
            "w:del", del.Id?.Value ?? "?", del.Author?.Value ?? "(none)",
            ParseDate(del.Date), text));
    }

    // w:comment -> Comment (in WordprocessingCommentsPart), cross-referenced to its
    // anchor range in the body via matching w:id on CommentRangeStart/End + CommentReference.
    var commentsPart = mainPart.WordprocessingCommentsPart;
    if (commentsPart?.Comments != null)
    {
        foreach (var comment in commentsPart.Comments.Elements<Comment>())
        {
            var commentText = string.Concat(comment.Descendants<Text>().Select(t => t.Text));

            var rangeStart = body.Descendants<CommentRangeStart>().FirstOrDefault(r => r.Id == comment.Id);
            var rangeEnd = body.Descendants<CommentRangeEnd>().FirstOrDefault(r => r.Id == comment.Id);
            var anchoredText = rangeStart != null && rangeEnd != null
                ? ExtractRangeText(rangeStart, rangeEnd)
                : "(anchor range not found)";

            results.Add(new RecoveredAnnotation(
                "w:comment", comment.Id?.Value ?? "?", comment.Author?.Value ?? "(none)",
                ParseDate(comment.Date), $"comment=\"{commentText}\" anchoredTo=\"{anchoredText}\""));
        }
    }

    return results;
}

static DateTime ParseDate(DateTimeValue? dtv)
{
    if (dtv?.Value is DateTime dt)
    {
        return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    }
    return default;
}

static string ExtractRangeText(CommentRangeStart start, CommentRangeEnd end)
{
    var parent = start.Parent;
    if (parent == null) return "(no parent)";
    var collecting = false;
    var sb = new System.Text.StringBuilder();
    foreach (var child in parent.ChildElements)
    {
        if (ReferenceEquals(child, start)) { collecting = true; continue; }
        if (ReferenceEquals(child, end)) { break; }
        if (collecting)
        {
            sb.Append(string.Concat(child.Descendants<Text>().Select(t => t.Text)));
        }
    }
    return sb.ToString();
}

// ---------------------------------------------------------------------------
// PART C: re-anchor confidence-band tuning
// ---------------------------------------------------------------------------

static void RunBandTuning()
{
    var scenarios = new List<BandScenario>
    {
        new("A. No change (identical paragraph, same position)",
            AnchorText: "Indemnification is capped at fees paid by Client in the twelve months preceding the claim, except for breaches of confidentiality.",
            AnchorParagraphHint: 1,
            CurrentParagraphs: BaseParas()),

        new("B. Trivial unrelated edit elsewhere; anchor paragraph untouched, same position",
            AnchorText: "Indemnification is capped at fees paid by Client in the twelve months preceding the claim, except for breaches of confidentiality.",
            AnchorParagraphHint: 1,
            CurrentParagraphs: WithEditAt(BaseParas(), 3, "Termination requires thirty (30) days written notice delivered to the other Party's contact (revised).")),

        new("C. Paragraph reflowed 1 position down (new paragraph inserted above), text identical",
            AnchorText: "Each Party shall perform its obligations under this Agreement in a professional and workmanlike manner consistent with industry standards.",
            AnchorParagraphHint: 2,
            CurrentParagraphs: InsertParagraphBefore(BaseParas(), 0, "Preamble: this excerpt is provided for illustration only and creates no binding obligation.")),

        new("D. Partial rewrite - ~1/3 of anchor sentence's words changed (mirrors Part A's real Word deletion edit)",
            AnchorText: "Each Party shall perform its obligations under this Agreement in a professional and workmanlike manner consistent with industry standards.",
            AnchorParagraphHint: 2,
            CurrentParagraphs: WithEditAt(BaseParas(), 2, "Each Party shall perform its obligations under this Agreement in a professional and workmanlike manner.")),

        new("E. Heavy rewrite - most words changed, same topic/paragraph position",
            AnchorText: "Each Party shall perform its obligations under this Agreement in a professional and workmanlike manner consistent with industry standards.",
            AnchorParagraphHint: 2,
            CurrentParagraphs: WithEditAt(BaseParas(), 2, "Both parties agree to act in good faith and to promptly escalate any performance concerns to the designated account manager.")),

        new("F. Anchor paragraph deleted entirely (text no longer present anywhere)",
            AnchorText: "Each Party shall perform its obligations under this Agreement in a professional and workmanlike manner consistent with industry standards.",
            AnchorParagraphHint: 2,
            CurrentParagraphs: RemoveParagraph(BaseParas(), 2)),

        new("G. Ambiguous duplicate - BYTE-IDENTICAL boilerplate sentence now appears at two positions",
            AnchorText: "Termination of this Agreement requires thirty (30) days written notice delivered to the other Party's designated contact.",
            AnchorParagraphHint: 3,
            CurrentParagraphs: DuplicateParagraphExact(BaseParas(), 3, insertAt: 5)),

        new("H. Near-duplicate (restated clause) inserted elsewhere; TRUE original also edited slightly",
            AnchorText: "Termination of this Agreement requires thirty (30) days written notice delivered to the other Party's designated contact.",
            AnchorParagraphHint: 3,
            CurrentParagraphs: DuplicateParagraph(BaseParas(), 3, insertAt: 5)),
    };

    Console.WriteLine("Scoring model: combinedScore = 0.75*contentSimilarity + 0.25*structuralProximity");
    Console.WriteLine("contentSimilarity = 1 - (Levenshtein(anchorText, bestCandidateParaText) / max(len(anchorText), len(candidate)))");
    Console.WriteLine("structuralProximity = 1.0 at exact hint, 0.85 within +-1, 0.6 within +-3, 0.3 beyond, 0 if best match not found at all\n");

    var results = new List<(string Scenario, double Score, string Band, string Note)>();

    foreach (var s in scenarios)
    {
        var (bestIdx, bestContent, secondBestContent) = FindBestParagraphMatch(s.AnchorText, s.CurrentParagraphs);
        double structural = StructuralProximity(bestIdx, s.AnchorParagraphHint);
        double combined = bestIdx < 0 ? 0.0 : (0.75 * bestContent + 0.25 * structural);

        string band = BandOf(combined);
        string note = "";

        if (bestIdx >= 0 && secondBestContent >= bestContent - 0.05 && secondBestContent > 0.5)
        {
            note = $"AMBIGUITY: second-best content-sim={secondBestContent:F2} within 0.05 of best={bestContent:F2} -> naive band would auto-anchor to a possibly-wrong candidate";
        }

        Console.WriteLine($"{s.Name}");
        Console.WriteLine($"  bestParagraphIndex={bestIdx} contentSim={bestContent:F3} structuralProx={structural:F2} combined={combined:F3} -> BAND: {band}");
        if (note.Length > 0) Console.WriteLine($"  ** {note}");
        Console.WriteLine();

        results.Add((s.Name, combined, band, note));
    }

    Console.WriteLine("--- Summary table ---");
    foreach (var r in results)
    {
        Console.WriteLine($"  {r.Band,-6} score={r.Score:F3}  {r.Scenario}{(r.Note.Length > 0 ? "  [AMBIGUOUS]" : "")}");
    }
}

static List<string> BaseParas() => new()
{
    "This Master Services Agreement (the \"Agreement\") is entered into between the parties identified in the signature block.",
    "Indemnification is capped at fees paid by Client in the twelve months preceding the claim, except for breaches of confidentiality.",
    "Each Party shall perform its obligations under this Agreement in a professional and workmanlike manner consistent with industry standards.",
    "Termination of this Agreement requires thirty (30) days written notice delivered to the other Party's designated contact.",
    "Confidential Information disclosed by either Party under this Agreement shall not be shared with third parties absent written consent."
};

static List<string> WithEditAt(List<string> paras, int index, string replacement)
{
    var copy = new List<string>(paras);
    copy[index] = replacement;
    return copy;
}

static List<string> InsertParagraphBefore(List<string> paras, int atIndex, string text)
{
    var copy = new List<string>(paras);
    copy.Insert(atIndex, text);
    return copy;
}

static List<string> RemoveParagraph(List<string> paras, int index)
{
    var copy = new List<string>(paras);
    copy.RemoveAt(index);
    return copy;
}

static List<string> DuplicateParagraph(List<string> paras, int sourceIndex, int insertAt)
{
    var copy = new List<string>(paras);
    var near = copy[sourceIndex] + " (restated for the renewal term)";
    copy.Insert(insertAt, near);
    return copy;
}

static List<string> DuplicateParagraphExact(List<string> paras, int sourceIndex, int insertAt)
{
    var copy = new List<string>(paras);
    copy.Insert(insertAt, copy[sourceIndex]);
    return copy;
}

static (int bestIdx, double bestContentSim, double secondBestContentSim) FindBestParagraphMatch(string anchorText, List<string> currentParagraphs)
{
    var scored = currentParagraphs
        .Select((p, i) => (index: i, sim: ContentSimilarity(anchorText, p)))
        .OrderByDescending(x => x.sim)
        .ToList();

    if (scored.Count == 0) return (-1, 0.0, 0.0);
    var best = scored[0];
    var second = scored.Count > 1 ? scored[1].sim : 0.0;

    // Below this floor, don't even nominate a candidate index — matches what a production
    // re-anchorer would do (treat as orphan rather than pin to a near-random paragraph).
    if (best.sim < 0.15) return (-1, best.sim, second);

    return (best.index, best.sim, second);
}

static double StructuralProximity(int bestIdx, int hint)
{
    if (bestIdx < 0) return 0.0;
    var distance = Math.Abs(bestIdx - hint);
    return distance switch
    {
        0 => 1.0,
        1 => 0.85,
        <= 3 => 0.6,
        _ => 0.3
    };
}

static string BandOf(double score) =>
    score >= 0.85 ? "AUTO" : score >= 0.6 ? "REVIEW" : "ORPHAN";

static double ContentSimilarity(string a, string b)
{
    if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return 1.0;
    if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0.0;
    var dist = Levenshtein(a, b);
    var maxLen = Math.Max(a.Length, b.Length);
    return maxLen == 0 ? 1.0 : 1.0 - (double)dist / maxLen;
}

static int Levenshtein(string a, string b)
{
    var dp = new int[a.Length + 1, b.Length + 1];
    for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
    for (int j = 0; j <= b.Length; j++) dp[0, j] = j;
    for (int i = 1; i <= a.Length; i++)
    {
        for (int j = 1; j <= b.Length; j++)
        {
            var cost = a[i - 1] == b[j - 1] ? 0 : 1;
            dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + cost);
        }
    }
    return dp[a.Length, b.Length];
}

record RecoveredAnnotation(string Kind, string Id, string Author, DateTime Date, string Text);
record BandScenario(string Name, string AnchorText, int AnchorParagraphHint, List<string> CurrentParagraphs);
