using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 ISS-042 (#2933): the guard on the closed firm-label table for the values a rejection finding read
/// (<see cref="FirmValueReadLabel.Table"/>). The labels are partner-visible, and the producers (the Billing handlers and the evaluator, under
/// <c>Services/Signals/Billing</c>) name each extracted value with an internal key such as <c>line.rate</c>. This guard scans those producers for every
/// name they give an extracted value (the call sites of <c>LineField</c>, <c>HeaderField</c> and <c>Extracted</c>, and the object initialisers that
/// set <c>ExtractedField</c>), so a producer that adds a name without a table entry fails here, instead of showing a firm the generic fallback. It also
/// fails when a name the scan cannot read (a computed one) appears, and when the table keeps a name no producer emits. Maintain-class (ADR-038): pure
/// logic, no mocks, no network. Enforced by: this guard (CI).
/// </summary>
/// <remarks>
/// Only an extracted value can reach a firm: <c>FirmRejectionFindings</c> drops a value that has no extracted-field reference, so the names given with
/// <c>HandlerContext.Reference</c> (<c>approvedRate</c>, <c>coreRecord.openDate</c>, and so on) never need a label. Two producer files are excluded
/// because their rule types can never reject (<see cref="ExcludedProducerFiles"/>; asserted against the catalogue below).
/// </remarks>
public sealed class FirmValueReadLabelGuardTests
{
    /// <summary>
    /// Producers whose findings can never be a rejection, so their value names never reach a firm: the AI observation handler (I-5: an AI observation never
    /// rejects; its value's field is AI-assigned and filtered anyway) and the system findings of the SubmissionIntegrity type (not reject-eligible).
    /// </summary>
    private static readonly string[] ExcludedProducerFiles = ["AiObservationHandler.cs", "BillingSystemFindings.cs"];

    private const string RequiredDataFile = "RequiredDataHandler.cs";

    private static readonly Regex LineFieldCall = new(@"\bLineField\(\s*(?<first>[A-Za-z_][\w.]*)\s*,\s*(?<name>""[^""]*""|[^,]+?)\s*,", RegexOptions.Compiled);
    private static readonly Regex HeaderOrExtractedCall = new(@"\b(?:HeaderField|Extracted)\(\s*(?<name>""[^""]*""|[^,]+?)\s*,", RegexOptions.Compiled);
    private static readonly Regex Initialiser = new(@"Name\s*=\s*(?<name>""[^""]*""|name)\s*,[^{}]*?ExtractedField\s*=", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex ExtractedFieldAssignment = new(@"ExtractedField\s*=(?!=)", RegexOptions.Compiled);
    private static readonly Regex ConcatenatedName = new(@"^""(?<prefix>(?:invoice|line)\.)""\s*\+\s*field$", RegexOptions.Compiled);
    private static readonly Regex DefinitionStart = new(@"^\(\s*(?:string|BillingLineFacts|FieldRef)\b", RegexOptions.Compiled);

    // ── The scan ──

    /// <summary>The names the producers give an extracted value, and what the scan could not read.</summary>
    private sealed record ScanResult(SortedSet<string> Names, List<string> Problems);

    /// <summary>Scans <paramref name="sources"/> (file name, text) for every name given to an extracted value.</summary>
    private static ScanResult Scan(IReadOnlyList<(string File, string Text)> sources)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        var problems = new List<string>();
        var required = sources.FirstOrDefault(s => s.File == RequiredDataFile).Text;

        foreach (var (file, raw) in sources.Where(s => !ExcludedProducerFiles.Contains(s.File)))
        {
            // Comment lines hold examples ("line.rate") and cross-references to the helpers; they are not producers.
            var text = string.Join('\n', raw.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

            foreach (Match m in LineFieldCall.Matches(text))
            {
                AddName(m.Groups["name"].Value, file, required, names, problems);
            }

            foreach (Match m in HeaderOrExtractedCall.Matches(text))
            {
                // A definition ("HeaderField(string name, ...") is not a call; the name argument then starts with a type.
                var afterOpen = text[(m.Index + m.Value.IndexOf('('))..];
                if (DefinitionStart.IsMatch(afterOpen))
                {
                    continue;
                }

                AddName(m.Groups["name"].Value, file, required, names, problems);
            }

            var literalInitialisers = Initialiser.Matches(text);
            foreach (Match m in literalInitialisers)
            {
                AddName(m.Groups["name"].Value, file, required, names, problems);
            }

            var assignments = ExtractedFieldAssignment.Matches(text).Count;
            if (assignments != literalInitialisers.Count)
            {
                problems.Add($"{file}: {assignments} ExtractedField assignment(s) but {literalInitialisers.Count} initialiser(s) with a name the scan can read");
            }
        }

        return new ScanResult(names, problems);
    }

    private static void AddName(string argument, string file, string requiredDataText, SortedSet<string> names, List<string> problems)
    {
        var arg = argument.Trim();
        if (arg.Length >= 2 && arg[0] == '"' && arg[^1] == '"')
        {
            names.Add(arg[1..^1]);
            return;
        }

        // The helpers pass their own `name` parameter on to the one that builds the value; their callers are scanned.
        if (arg == "name")
        {
            return;
        }

        var concatenated = ConcatenatedName.Match(arg);
        if (concatenated.Success)
        {
            // RequiredData: the field is one of its own ordered lists (HeaderOrder for "invoice.", LineOrder for "line.").
            var list = concatenated.Groups["prefix"].Value == "invoice." ? "HeaderOrder" : "LineOrder";
            var declared = Regex.Match(requiredDataText ?? string.Empty, list + @"\s*=\s*\[(?<items>[^\]]*)\]");
            var items = declared.Success ? Regex.Matches(declared.Groups["items"].Value, @"""([^""]+)""").Select(i => i.Groups[1].Value).ToList() : [];
            if (items.Count == 0)
            {
                problems.Add($"{file}: the name {arg} concatenates a field, but {list} was not found in {RequiredDataFile}");
                return;
            }

            foreach (var item in items)
            {
                names.Add(concatenated.Groups["prefix"].Value + item);
            }

            return;
        }

        problems.Add($"{file}: the extracted value name `{arg}` is not a literal; the scan cannot list it, so give it a literal name or extend the scan");
    }

    /// <summary>What is wrong with <paramref name="table"/> against the names the producers emit (empty when nothing is).</summary>
    private static List<string> Violations(ScanResult scan, IReadOnlyDictionary<string, string> table)
    {
        var problems = new List<string>(scan.Problems);
        problems.AddRange(scan.Names.Where(n => !table.TryGetValue(n, out var label) || string.IsNullOrWhiteSpace(label))
            .Select(n => $"{n}: a producer emits it, but it has no non-blank label"));
        problems.AddRange(table.Keys.Where(k => !scan.Names.Contains(k)).Select(k => $"{k}: has a label but no producer emits it"));
        return problems;
    }

    // ── The real producers ──

    private static string ProducerDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "Services", "Signals", "Billing");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Services/Signals/Billing was not found above " + AppContext.BaseDirectory);
    }

    private static List<(string File, string Text)> ProducerSources() =>
        Directory.EnumerateFiles(ProducerDirectory(), "*.cs", SearchOption.AllDirectories)
            .Select(p => (Path.GetFileName(p), File.ReadAllText(p)))
            .ToList();

    [Fact(DisplayName = "ISS-042 guard: every extracted-value name a Billing producer emits has a firm label, and the table keeps no name no producer emits")]
    public void TheTable_CoversExactlyTheNamesTheProducersEmit()
    {
        var scan = Scan(ProducerSources());

        scan.Names.Should().Contain(["line.rate", "line.hours", "line.amount", "line.date", "line.timekeeperName", "line.expenseCode", "line.narrative",
            "invoice.date", "invoice.currency", "invoice.firmInvoiceNumber", "invoice.billingPeriodStart", "line.taskCode"],
            "the scan must find the producers it was written for, or it proves nothing");
        scan.Names.Should().NotContain(n => n.StartsWith("approvedRate", StringComparison.Ordinal) || n.StartsWith("coreRecord", StringComparison.Ordinal),
            "a reference value has no extracted field and never reaches a firm");
        Violations(scan, FirmValueReadLabel.Table).Should().BeEmpty();
    }

    [Fact(DisplayName = "ISS-042 guard: the excluded producers are of rule types that can never reject, so their names never reach a firm")]
    public void TheExcludedProducers_AreOfTypesThatNeverReject()
    {
        BillingRuleCatalogue.IsRejectEligible(RuleType.AiObservation).Should().BeFalse("I-5: an AI observation never rejects");
        BillingRuleCatalogue.IsRejectEligible(RuleType.SubmissionIntegrity).Should().BeFalse("the system findings are never a rejection");
        ExcludedProducerFiles.Should().BeEquivalentTo("AiObservationHandler.cs", "BillingSystemFindings.cs");
        ProducerSources().Select(s => s.File).Should().Contain(ExcludedProducerFiles, "an excluded file that no longer exists is a stale exclusion");
    }

    [Fact(DisplayName = "ISS-042 guard: every label is short, sentence-case firm English: letters and spaces, no key, no dot, no placeholder")]
    public void EveryLabel_IsFirmSafeText()
    {
        foreach (var (key, label) in FirmValueReadLabel.Table)
        {
            label.Should().Be(label.Trim(), key);
            label.Length.Should().BeInRange(1, FirmValueReadLabel.MaxLabelLength, key);
            label.Should().MatchRegex(@"^[A-Z][a-z]+( [a-z]+)*$", $"{key}: sentence case, letters and spaces only (no digits, dots, underscores, braces or markup)");
            label.Should().NotContain(".", key);
            label.ToLowerInvariant().Should().NotBe(key.ToLowerInvariant(), "a label is not the raw key");
        }

        FirmValueReadLabel.GenericLabel.Should().MatchRegex(@"^[A-Z][a-z]+( [a-z]+)*$");
        FirmValueReadLabel.Table.Values.Should().NotContain(FirmValueReadLabel.GenericLabel, "the generic label is the fallback, not a mapped name's label");
    }

    [Fact(DisplayName = "ISS-042 guard: no label is, or contains, a reason code or a rule-type name")]
    public void NoLabel_ShowsAReasonCodeOrARuleTypeName()
    {
        var codes = Enum.GetValues<RuleType>()
            .Where(t => RuleDomains.IsBilling(t) && !BillingRuleCatalogue.IsSystemType(t))
            .SelectMany(t => BillingRuleCatalogue.RejectCapableReasons(t))
            .ToList();
        var typeNames = Enum.GetNames<RuleType>();

        foreach (var (key, label) in FirmValueReadLabel.Table)
        {
            codes.Should().NotContain(c => label.Contains(c, StringComparison.OrdinalIgnoreCase), key);
            typeNames.Should().NotContain(t => label.Equals(t, StringComparison.OrdinalIgnoreCase), key);
        }
    }

    // ── The guard itself: must-fire and must-not-fire ──

    private static readonly (string File, string Text) RequiredData = (RequiredDataFile, """
        private static readonly string[] HeaderOrder = ["firmInvoiceNumber", "invoiceDate"];
        private static readonly string[] LineOrder = ["timekeeper", "narrative"];
        var a = context.HeaderField("invoice." + field, fieldName, null);
        var b = context.LineField(line, "line." + field, FieldNameOf(field), null);
        """);

    [Fact(DisplayName = "ISS-042 guard must-fire: a producer that emits a name with no label fails")]
    public void MustFire_ANewProducerName_WithNoLabel_Fails()
    {
        var scan = Scan([RequiredData, ("NewHandler.cs", """context.LineField(line, "line.discountPercent", BillingFieldNames.Rate, null);""")]);

        var table = new Dictionary<string, string>
        {
            ["invoice.firmInvoiceNumber"] = "Invoice number",
            ["invoice.invoiceDate"] = "Invoice date",
            ["line.timekeeper"] = "Timekeeper",
            ["line.narrative"] = "Description",
        };

        Violations(scan, table).Should().ContainSingle().Which.Should().StartWith("line.discountPercent").And.Contain("no non-blank label");
    }

    [Fact(DisplayName = "ISS-042 guard must-fire: a blank label, a label with no producer, a computed name and an unreadable initialiser each fail")]
    public void MustFire_BlankDeadComputedAndInitialiser_Fail()
    {
        var scan = Scan(
        [
            RequiredData,
            ("A.cs", """
                var x = context.HeaderField(NameFor(x), BillingFieldNames.Currency, null);
                var y = new BillingValueRead { Name = Compute(), Value = v, ExtractedField = new FieldRef(null, "Rate") };
                """),
        ]);

        var table = new Dictionary<string, string>
        {
            ["invoice.firmInvoiceNumber"] = "   ",
            ["invoice.invoiceDate"] = "Invoice date",
            ["line.timekeeper"] = "Timekeeper",
            ["line.narrative"] = "Description",
            ["line.dead"] = "Dead",
        };

        var problems = Violations(scan, table);
        problems.Should().Contain(p => p.StartsWith("invoice.firmInvoiceNumber") && p.Contains("no non-blank label"));
        problems.Should().Contain(p => p.StartsWith("line.dead") && p.Contains("no producer emits it"));
        problems.Should().Contain(p => p.Contains("NameFor(x)") && p.Contains("not a literal"));
        problems.Should().Contain(p => p.StartsWith("A.cs") && p.Contains("ExtractedField assignment"));
        problems.Should().HaveCount(4);
    }

    [Fact(DisplayName = "ISS-042 guard must-not-fire: literals, the RequiredData concatenation, a forwarded name, definitions, comments and an excluded file report nothing")]
    public void MustNotFire_ReadableProducers_ReportNothing()
    {
        var scan = Scan(
        [
            RequiredData,
            ("B.cs", """
                // context.LineField(line, "line.commented", f, null) is only an example; so is HeaderField("invoice.commented", f, v).
                internal BillingValueRead LineField(BillingLineFacts line, string name, string fieldName, string? value) =>
                    Extracted(name, new FieldRef(line.Index, fieldName), value, line.FieldConfidence);
                internal BillingValueRead HeaderField(string name, string fieldName, string? value) => Extracted(name, new FieldRef(null, fieldName), value, 0.9m);
                private BillingValueRead Extracted(string name, FieldRef field, string? value, decimal? confidence) => new()
                {
                    Name = name,
                    Value = value,
                    ExtractedField = field,
                };
                var rate = context.LineField(line, "line.rate", BillingFieldNames.Rate, null);
                var cur = new BillingValueRead { Name = "invoice.currency", Value = v, ExtractedField = new FieldRef(null, "Currency") };
                var ref1 = HandlerContext.Reference("approvedRate", null);
                """),
            ("AiObservationHandler.cs", """context.Extracted("line.observation", new FieldRef(1, "Observation.X"), null, 0.9m);"""),
        ]);

        scan.Problems.Should().BeEmpty();
        scan.Names.Should().BeEquivalentTo(
            "invoice.firmInvoiceNumber", "invoice.invoiceDate", "line.timekeeper", "line.narrative", "line.rate", "invoice.currency");
    }
}
