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
/// set <c>ExtractedField</c>), so a producer that adds a name without a table entry fails here, instead of showing a firm the generic fallback.
/// Maintain-class (ADR-038): pure logic, no mocks, no network. Enforced by: this guard (CI).
/// </summary>
/// <remarks>
/// <para><b>What the guard fails on</b> (it fails closed; a shape it cannot read is a failure, not a pass):</para>
/// <list type="bullet">
/// <item>a name a producer emits with no non-blank table entry, and a table entry no producer emits;</item>
/// <item>a call of <c>LineField</c>, <c>HeaderField</c> or <c>Extracted</c> that the scan did not parse: the calls are counted in each file and the
/// count must equal the calls read (a first argument that is not a plain identifier, a named argument, a space before the parenthesis), and a
/// reference to one of them that is not a call;</item>
/// <item>a name that is not a string literal, except the two expansions below;</item>
/// <item>an <c>ExtractedField</c> assignment the scan did not read as an initialiser with a literal name first, a <c>with { Name = … }</c> expression
/// (it renames a value after the producer named it), and a local named <c>name</c> in <c>HandlerContext.cs</c> (it would look like the forwarded parameter; elsewhere any forwarded <c>name</c> already fails).</item>
/// </list>
/// <para><b>The two allowed expansions, each in one file.</b> <c>HandlerContext.cs</c> may pass its own <c>name</c> parameter on (the helpers hand it to the
/// one method that builds the value; the helpers' callers are scanned). <c>RequiredDataHandler.cs</c> may write <c>"line." + field</c> or
/// <c>"invoice." + field</c>, read as its own <c>LineOrder</c> and <c>HeaderOrder</c> lists. Either shape anywhere else fails.</para>
/// <para><b>What it does not read.</b> A producer that builds the name some other way, in a file the scan does not list under
/// <c>Services/Signals/Billing</c>, is outside it. Only an extracted value can reach a firm: <c>FirmRejectionFindings</c> drops a value that has no
/// extracted-field reference, so the names given with <c>HandlerContext.Reference</c> (<c>approvedRate</c>, <c>coreRecord.openDate</c>, and so on)
/// never need a label. Two producer files are excluded because their rule types can never reject (<see cref="ExcludedProducerFiles"/>; asserted against
/// the catalogue below).</para>
/// </remarks>
public sealed class FirmValueReadLabelGuardTests
{
    /// <summary>
    /// Producers whose findings can never be a rejection, so their value names never reach a firm: the AI observation handler (I-5: an AI observation never
    /// rejects; its value's field is AI-assigned and filtered anyway) and the system findings of the SubmissionIntegrity type (not reject-eligible).
    /// </summary>
    private static readonly string[] ExcludedProducerFiles = ["AiObservationHandler.cs", "BillingSystemFindings.cs"];

    private const string RequiredDataFile = "RequiredDataHandler.cs";
    private const string HandlerContextFile = "HandlerContext.cs";

    private static readonly Regex LineFieldCall = new(@"\bLineField\s*\(\s*(?<first>[A-Za-z_][\w.]*)\s*,\s*(?<name>""[^""]*""|[^,]+?)\s*,", RegexOptions.Compiled);
    private static readonly Regex HeaderOrExtractedCall = new(@"\b(?:HeaderField|Extracted)\s*\(\s*(?<name>""[^""]*""|[^,]+?)\s*,", RegexOptions.Compiled);
    private static readonly Regex Initialiser = new(@"Name\s*=\s*(?<name>""[^""]*""|name)\s*,[^{}]*?ExtractedField\s*=", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex ExtractedFieldAssignment = new(@"ExtractedField\s*=(?!=)", RegexOptions.Compiled);
    private static readonly Regex ConcatenatedName = new(@"^""(?<prefix>(?:invoice|line)\.)""\s*\+\s*field$", RegexOptions.Compiled);

    // Every producer call, however its arguments are written, so the calls the scan parsed can be checked against all of them.
    private static readonly Regex AnyProducerCall = new(@"\b(?:LineField|HeaderField|Extracted)\s*\(", RegexOptions.Compiled);
    private static readonly Regex BareProducerReference = new(@"\b(?:LineField|HeaderField|Extracted)\b(?!\s*\()", RegexOptions.Compiled);

    // A definition starts its argument list with a typed parameter ("string name", "BillingLineFacts line", "FieldRef field"); a call does not.
    private static readonly Regex DefinitionStart = new(@"^\(\s*(?:string|BillingLineFacts|FieldRef)\s+\w+", RegexOptions.Compiled);
    private static readonly Regex NameSetByWith = new(@"\bwith\s*\{[^}]*\bName\s*=", RegexOptions.Compiled);
    private static readonly Regex LocalNameDeclaration = new(@"\b(?:var|string)\s+name\s*=(?!=)", RegexOptions.Compiled);

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

            var parsedCalls = 0;

            foreach (Match m in LineFieldCall.Matches(text))
            {
                parsedCalls++;
                AddName(m.Groups["name"].Value, file, required, names, problems);
            }

            foreach (Match m in HeaderOrExtractedCall.Matches(text))
            {
                // A definition ("HeaderField(string name, ...") is not a call; the name argument then starts with a type.
                if (IsDefinition(text, m))
                {
                    continue;
                }

                parsedCalls++;
                AddName(m.Groups["name"].Value, file, required, names, problems);
            }

            // Fail closed: every non-definition call must be one the scan parsed. A call the regexes above did not match (the first argument is
            // an indexer or a method call, an argument is named, a space splits the name from the parenthesis) would otherwise pass unseen.
            var allCalls = AnyProducerCall.Matches(text).Count(m => !IsDefinition(text, m));
            if (allCalls != parsedCalls)
            {
                problems.Add($"{file}: {allCalls} LineField/HeaderField/Extracted call(s) but {parsedCalls} the scan can read; write each call positionally with an identifier as the first argument of LineField");
            }

            foreach (Match m in BareProducerReference.Matches(text))
            {
                problems.Add($"{file}: `{m.Value}` is used without being called; the scan cannot read the names it would emit");
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

            if (NameSetByWith.IsMatch(text))
            {
                problems.Add($"{file}: a `with {{ Name = ... }}` expression renames a value after its producer named it; the scan cannot read it, so name the value where it is built");
            }

            if (file == HandlerContextFile && LocalNameDeclaration.IsMatch(text))
            {
                problems.Add($"{file}: declares a local `name`; in this file a `name` argument is read as the forwarded parameter, so use another identifier");
            }
        }

        return new ScanResult(names, problems);
    }

    /// <summary>True when the parenthesis that ends <paramref name="call"/> opens a parameter list (a typed first parameter), not an argument list.</summary>
    private static bool IsDefinition(string text, Match call) =>
        DefinitionStart.IsMatch(text[(call.Index + call.Value.LastIndexOf('('))..]);

    private static void AddName(string argument, string file, string requiredDataText, SortedSet<string> names, List<string> problems)
    {
        var arg = argument.Trim();
        if (arg.Length >= 2 && arg[0] == '"' && arg[^1] == '"')
        {
            names.Add(arg[1..^1]);
            return;
        }

        // HandlerContext's helpers pass their own `name` parameter on to the one that builds the value; their callers are scanned. Anywhere else
        // a `name` argument is a wrapper or a local the scan cannot follow.
        if (arg == "name")
        {
            if (file != HandlerContextFile)
            {
                problems.Add($"{file}: forwards a `name` the scan cannot read; only {HandlerContextFile} may pass its own name parameter on, so give the value a literal name");
            }

            return;
        }

        var concatenated = ConcatenatedName.Match(arg);
        if (concatenated.Success)
        {
            if (file != RequiredDataFile)
            {
                problems.Add($"{file}: the name {arg} concatenates a field; only {RequiredDataFile} may, because the scan reads its own ordered lists");
                return;
            }

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

    /// <summary>A table that labels exactly the names <see cref="RequiredData"/> emits, so the only problem a case can raise is its own.</summary>
    private static readonly Dictionary<string, string> RequiredDataTable = new()
    {
        ["invoice.firmInvoiceNumber"] = "Invoice number",
        ["invoice.invoiceDate"] = "Invoice date",
        ["line.timekeeper"] = "Timekeeper",
        ["line.narrative"] = "Description",
    };

    private static List<string> ViolationsOf(string file, string text) => Violations(Scan([RequiredData, (file, text)]), RequiredDataTable);

    [Fact(DisplayName = "ISS-042 guard must-fire: a producer that emits a name with no label fails")]
    public void MustFire_ANewProducerName_WithNoLabel_Fails()
    {
        var scan = Scan([RequiredData, ("NewHandler.cs", """context.LineField(line, "line.discountPercent", BillingFieldNames.Rate, null);""")]);

        Violations(scan, RequiredDataTable).Should().ContainSingle().Which.Should().StartWith("line.discountPercent").And.Contain("no non-blank label");
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

    // A call the regexes would not match is counted by the any-call pattern, so the file fails instead of passing unseen.
    [Theory(DisplayName = "ISS-042 guard must-fire: a call shape the scan cannot parse fails (counted, not skipped)")]
    [InlineData("""var v = context.LineField(members[0], "line.newKey", BillingFieldNames.Rate, null);""")]
    [InlineData("""var v = context.LineField(fees.First(), "line.newKey", BillingFieldNames.Rate, null);""")]
    [InlineData("""var v = context.LineField(line: line, name: "line.newKey", fieldName: BillingFieldNames.Rate, value: null);""")]
    [InlineData("""var v = context.LineField (line, "line.newKey", BillingFieldNames.Rate, null);""")]
    [InlineData("""var v = context.HeaderField ("invoice.newKey", BillingFieldNames.Currency, null);""")]
    [InlineData("""var pick = context.LineField;""")]
    public void MustFire_AnUnreadableCallShape_Fails(string code)
    {
        var problems = ViolationsOf("C.cs", code);

        ViolationsOf("Clean.cs", string.Empty).Should().BeEmpty("the baseline raises nothing, so any problem below is the case's own");
        problems.Should().NotBeEmpty();
    }

    [Fact(DisplayName = "ISS-042 guard must-fire: a name changed after the producer set it (`with { Name = ... }`) fails")]
    public void MustFire_AWithExpressionThatSetsName_Fails()
    {
        var problems = ViolationsOf("C.cs", """
            var v = context.LineHours(line) with { Name = "line.newKey" };
            """);

        problems.Should().ContainSingle().Which.Should().StartWith("C.cs").And.Contain("with");
    }

    [Fact(DisplayName = "ISS-042 guard must-fire: a wrapper that forwards a `name` parameter, outside HandlerContext.cs, fails")]
    public void MustFire_AForwardingWrapper_OutsideHandlerContext_Fails()
    {
        var problems = ViolationsOf("Wrapper.cs", """
            internal BillingValueRead LineAmountOf(HandlerContext c, BillingLineFacts line, string name) => c.LineField(line, name, BillingFieldNames.Amount, null);
            """);

        problems.Should().ContainSingle().Which.Should().StartWith("Wrapper.cs").And.Contain("forwards a `name`");
    }

    [Fact(DisplayName = "ISS-042 guard must-fire: a local `name` with a computed value, passed to a producer call, fails")]
    public void MustFire_ALocalNameWithAComputedValue_Fails()
    {
        var problems = ViolationsOf("C.cs", """
            var name = Compute(line);
            var v = context.LineField(line, name, BillingFieldNames.Amount, null);
            """);

        problems.Should().Contain(p => p.StartsWith("C.cs") && p.Contains("forwards a `name`"));
    }

    [Fact(DisplayName = "ISS-042 guard must-fire: a local `name` with a computed value fails even inside HandlerContext.cs")]
    public void MustFire_ALocalNameWithAComputedValue_InHandlerContext_Fails()
    {
        var problems = ViolationsOf("HandlerContext.cs", """
            var name = Compute(line);
            var v = LineField(line, name, BillingFieldNames.Amount, null);
            """);

        problems.Should().ContainSingle().Which.Should().StartWith("HandlerContext.cs").And.Contain("local `name`");
    }

    [Theory(DisplayName = "ISS-042 guard must-fire: a concatenated name outside RequiredDataHandler.cs fails")]
    [InlineData("""var v = context.LineField(line, "line." + field, FieldNameOf(field), null);""")]
    [InlineData("""var v = context.HeaderField("invoice." + field, fieldName, null);""")]
    public void MustFire_AConcatenatedName_OutsideRequiredData_Fails(string code)
    {
        var problems = ViolationsOf("Other.cs", code);

        problems.Should().ContainSingle().Which.Should().StartWith("Other.cs").And.Contain("concatenates a field");
    }

    [Fact(DisplayName = "ISS-042 guard must-not-fire: literals, the RequiredData concatenation, HandlerContext's forwarded name, definitions, comments and an excluded file report nothing")]
    public void MustNotFire_ReadableProducers_ReportNothing()
    {
        var scan = Scan(
        [
            RequiredData,
            ("B.cs", """
                // context.LineField(line, "line.commented", f, null) is only an example; so is HeaderField("invoice.commented", f, v).
                var rate = context.LineField(line, "line.rate", BillingFieldNames.Rate, null);
                var cur = new BillingValueRead { Name = "invoice.currency", Value = v, ExtractedField = new FieldRef(null, "Currency") };
                var ref1 = HandlerContext.Reference("approvedRate", null);
                var kept = row with { Value = "x" };
                """),
            (HandlerContextFile, """
                internal BillingValueRead LineField(BillingLineFacts line, string name, string fieldName, string? value) =>
                    Extracted(name, new FieldRef(line.Index, fieldName), value, line.FieldConfidence);
                internal BillingValueRead HeaderField(string name, string fieldName, string? value) => Extracted(name, new FieldRef(null, fieldName), value, 0.9m);
                private BillingValueRead Extracted(string name, FieldRef field, string? value, decimal? confidence) => new()
                {
                    Name = name,
                    Value = value,
                    ExtractedField = field,
                };
                internal BillingValueRead LineHours(BillingLineFacts line) => LineField(line, "line.hours", BillingFieldNames.Hours, null);
                """),
            ("AiObservationHandler.cs", """context.Extracted("line.observation", new FieldRef(1, "Observation.X"), null, 0.9m);"""),
        ]);

        scan.Problems.Should().BeEmpty();
        scan.Names.Should().BeEquivalentTo(
            "invoice.firmInvoiceNumber", "invoice.invoiceDate", "line.timekeeper", "line.narrative", "line.rate", "invoice.currency", "line.hours");
    }
}
