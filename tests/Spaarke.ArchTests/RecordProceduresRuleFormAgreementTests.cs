using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 084c (EB-D-217, D-27): the Record Procedures page edits a rule's body with ONE typed form per billing rule type
/// (<c>src/solutions/RecordProcedures/src/components/wizards/ruleForms/&lt;Type&gt;.form.ts</c>). The server's JSON Schemas
/// (<c>Services/Signals/Schemas/billing-*.schema.json</c>) are the authority; this test pins each form to its schema so a new type, a new
/// property, a changed bound or vocabulary, or a changed requirement fails here instead of surfacing as a server refusal on a user's screen.
/// <para>
/// Read as text (no browser, no network). Enforced by: this test, on every ArchTests run. Conventions the parser relies on (also stated in
/// <c>ruleForms/types.ts</c>): one field per line in a <c>*.form.ts</c>; <c>key</c> and option <c>value</c> in single quotes; every label,
/// help and refusal text in double quotes. A field the parser cannot read in full fails the test (no silent skip). Each pin is shown to
/// fire on a seeded drift (must-fire controls below).
/// </para>
/// </summary>
public sealed class RecordProceduresRuleFormAgreementTests
{
    private static readonly string[] AuthorableTypes = BillingRuleCatalogue.Types.Select(t => t.ToString()).ToArray();

    [Fact(DisplayName = "084c: every authorable billing type has exactly one form file and one entry in the table; no other form file exists")]
    public void EveryAuthorableType_HasExactlyOneForm_InTheTable()
    {
        var files = Directory.GetFiles(FormsDir, "*.form.ts").Select(f => Path.GetFileName(f)[..^".form.ts".Length]).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(AuthorableTypes.OrderBy(x => x, StringComparer.Ordinal).ToArray(), files);

        var index = File.ReadAllText(Path.Combine(FormsDir, "index.ts"));
        foreach (var type in AuthorableTypes)
        {
            Assert.Matches($@"from '\./{type}\.form'", index);
            Assert.Equal(type, ParseForm(ReadForm(type)).RuleType);
        }

        // The table lists each form once.
        var listed = Regex.Match(index, @"RULE_FORMS[^=]*=\s*\[(?<body>[^\]]*)\]").Groups["body"].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToArray();
        Assert.Equal(AuthorableTypes.Length, listed.Length);
        Assert.Equal(listed.Length, listed.Distinct().Count());
    }

    [Fact(DisplayName = "084c: the system type (SubmissionIntegrity) is not authorable and has no form")]
    public void TheSystemType_HasNoForm()
    {
        Assert.True(BillingRuleCatalogue.IsSystemType(RuleType.SubmissionIntegrity));
        Assert.DoesNotContain("SubmissionIntegrity", AuthorableTypes);
        Assert.False(File.Exists(Path.Combine(FormsDir, "SubmissionIntegrity.form.ts")));
        Assert.DoesNotContain("SubmissionIntegrity", Plain(File.ReadAllText(Path.Combine(FormsDir, "index.ts"))));
    }

    public static IEnumerable<object[]> Types() => BillingRuleCatalogue.Types.Select(t => new object[] { t.ToString() });

    [Theory(DisplayName = "084c: each form agrees with its server schema: properties, requirements, bounds, vocabularies, code pattern")]
    [MemberData(nameof(Types))]
    public void EachForm_AgreesWithItsSchema(string type)
    {
        var problems = Compare(ParseForm(ReadForm(type)), ReadSchema(type), CodePatternInForms(), MaxCodesInForms());
        Assert.True(problems.Count == 0, $"{type}: " + string.Join("; ", problems));
    }

    [Fact(DisplayName = "084c: a form with no fields says it is not changed; AddedFee refuses exactly the allowances the server refuses")]
    public void FormLevelFacts_AreThePinnedOnes()
    {
        foreach (var type in AuthorableTypes)
        {
            var form = ParseForm(ReadForm(type));
            if (form.Fields.Length == 0) Assert.True(form.NoChange, $"{type} has no fields, so it must say it is not changed");
        }

        // A rate card rule's rates come from the Procedure Version's rate card: not changed for one record.
        Assert.True(ParseForm(ReadForm("ApprovedRate")).NoChange);

        var allowance = ParseForm(ReadForm("AddedFee")).Fields.Single(f => f.Key == "allowance");
        Assert.Equal(
            new[] { BillingRuleCatalogue.AllowanceNotAllowed, BillingRuleCatalogue.AllowanceAfterDays }.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            allowance.Refused.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.Contains(BillingRuleCatalogue.AllowanceCap, allowance.Options);
        Assert.DoesNotContain(BillingRuleCatalogue.AllowanceCap, allowance.Refused);

        // No other form refuses an option.
        foreach (var type in AuthorableTypes.Where(t => t != "AddedFee"))
            Assert.All(ParseForm(ReadForm(type)).Fields, f => Assert.Empty(f.Refused));
    }

    [Fact(DisplayName = "084c: the parser reads what it should (a clean control: DailyHoursCap and InvoiceAge)")]
    public void TheParser_ReadsTheFormsItIsGiven()
    {
        var cap = ParseForm(ReadForm("DailyHoursCap"));
        var field = Assert.Single(cap.Fields);
        Assert.Equal(("maxHoursPerDay", "number", true, false), (field.Key, field.Kind, field.Required, field.Integer));
        Assert.Equal((0d, true, 24d, false), (field.Min, field.MinExclusive, field.Max, field.MaxExclusive));

        var age = Assert.Single(ParseForm(ReadForm("InvoiceAge")).Fields);
        Assert.True(age.Integer);
        Assert.Equal((1d, false, 3650d), (age.Min, age.MinExclusive, age.Max));

        var fee = ParseForm(ReadForm("AddedFee"));
        Assert.Equal(new[] { "capAmount", "capPercent" }, fee.ExactlyOneOf.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.True(fee.Fields.Single(f => f.Key == "feeCodes").HasShowWhen);
        Assert.False(fee.Fields.Single(f => f.Key == "feeKind").HasShowWhen);
        Assert.Equal(("allowance", "cap"), (fee.Fields.Single(f => f.Key == "feeCodes").ShowWhenKey, fee.Fields.Single(f => f.Key == "feeCodes").ShowWhenEquals));
        Assert.Equal(("allowance", "afterDays"), (fee.Fields.Single(f => f.Key == "afterDays").ShowWhenKey, fee.Fields.Single(f => f.Key == "afterDays").ShowWhenEquals));
        Assert.Null(fee.Fields.Single(f => f.Key == "feeKind").ShowWhenKey);

        // A showWhen the parser cannot read in full fails (no silent skip).
        var unreadable = ReadForm("AddedFee").Replace("showWhen: { key: 'allowance', equals: 'afterDays' }", "showWhen: { key: 'allowance' }");
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ParseForm(unreadable));
    }

    // ── The server's refusal words the page reads (084c fix round 1, item 5) ──

    /// <summary>One body's single refusal from the server's validator.</summary>
    private static string Validate(RuleType type, string body)
    {
        var result = new RuleBodySchemaValidator().Validate(type, body);
        Assert.False(result.IsValid);
        return Assert.Single(result.Errors);
    }

    [Fact(DisplayName = "084c fix 1: the page's tests use the validator's real refusal words (a member's pointer, and the root's Required properties)")]
    public void ThePagesTests_UseTheValidatorsRealMessages()
    {
        // The validator's real messages: a member's problem is "<pointer>: <message>"; a missing member is reported at the root ("").
        var overMaximum = Validate(RuleType.DailyHoursCap, "{\"type\":\"DailyHoursCap\",\"maxHoursPerDay\":30}");
        var missingMembers = Validate(RuleType.AiObservation, "{\"type\":\"AiObservation\"}");

        // The two shapes serverFieldErrors (ruleForms/body.ts) reads.
        Assert.Matches(@"^/maxHoursPerDay: \S", overMaximum);
        Assert.Matches(@"^: Required properties \[[^\]]*\]", missingMembers);
        Assert.Contains("\"observation\"", missingMembers, StringComparison.Ordinal);
        Assert.Contains("\"minConfidence\"", missingMembers, StringComparison.Ordinal);

        var tests = Path.Combine(RepoRoot(), "src", "solutions", "RecordProcedures", "src", "__tests__");
        var ruleForms = File.ReadAllText(Path.Combine(tests, "ruleForms.test.ts"));
        var wizard = File.ReadAllText(Path.Combine(tests, "WizardsChange.test.tsx"));
        Assert.Contains(JsString(overMaximum), ruleForms, StringComparison.Ordinal);
        Assert.Contains(JsString(missingMembers), ruleForms, StringComparison.Ordinal);
        Assert.Contains(JsString(overMaximum), wizard, StringComparison.Ordinal);
    }

    /// <summary>The message as a single-quoted JavaScript string literal (the page's tests' convention).</summary>
    private static string JsString(string text) => "'" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'";

    // ── Must-fire controls: each pin fails on a seeded drift (the seed is applied to an in-memory copy; nothing on disk changes) ──

    public static IEnumerable<object[]> SchemaDrifts() => new[]
    {
        // type, text to find in the schema, text to put there
        new object[] { "DailyHoursCap", "\"maximum\": 24", "\"maximum\": 25" },
        new object[] { "DailyHoursCap", "\"exclusiveMinimum\": 0", "\"minimum\": 0" },
        new object[] { "DailyHoursCap", "\"maxHoursPerDay\"\n  ]", "\"maxHoursPerDay\", \"extraRequired\"\n  ]" },
        new object[] { "DailyHoursCap", "\"properties\": {", "\"properties\": {\n    \"newServerProperty\": { \"type\": \"number\" }," },
        new object[] { "Duplicate", "\"amount\"", "\"amount\", \"currency\"" },
        new object[] { "InvoiceAge", "\"type\": \"integer\"", "\"type\": \"number\"" },
        new object[] { "ExpenseRule", "\"maxItems\": 500", "\"maxItems\": 400" },
        new object[] { "ExpenseRule", "\"pattern\": \"^[A-Za-z0-9][A-Za-z0-9._-]{0,19}$\"", "\"pattern\": \"^[A-Za-z0-9]{0,19}$\"" },
        new object[] { "AiObservation", "\"vagueDescription\"", "\"vagueDescription\", \"other\"" },
        new object[] { "Discount", "\"exclusiveMaximum\": 100", "\"maximum\": 100" },
        // 084c fix round 1 (item 5): the conditional fields are the schema's allOf branches.
        new object[] { "AddedFee", "\"const\": \"cap\"", "\"const\": \"capped\"" },
        new object[] { "AddedFee", "\"required\": [\"afterDays\"],", "\"required\": [\"afterDays\", \"feeCodes\"]," },
        new object[] { "AddedFee", "\"required\": [\"feeCodes\"],", "\"required\": []," },
    };

    [Theory(DisplayName = "084c: a drift in the server schema fails the agreement (must-fire)")]
    [MemberData(nameof(SchemaDrifts))]
    public void ASeededSchemaDrift_IsCaught(string type, string find, string replacement)
    {
        var schema = ReadSchema(type);
        Assert.Contains(find, schema);
        var form = ParseForm(ReadForm(type));
        Assert.Empty(Compare(form, schema, CodePatternInForms(), MaxCodesInForms()));
        Assert.NotEmpty(Compare(form, schema.Replace(find, replacement), CodePatternInForms(), MaxCodesInForms()));
    }

    public static IEnumerable<object[]> FormDrifts() => new[]
    {
        new object[] { "DailyHoursCap", "max: 24", "max: 25" },
        new object[] { "DailyHoursCap", "required: true, ", "" },
        new object[] { "DailyHoursCap", "minExclusive: true, ", "" },
        new object[] { "InvoiceAge", "integer: true, ", "" },
        new object[] { "InvoiceAge", "min: 1, ", "min: 0, " },
        new object[] { "Duplicate", "kind: 'multi'", "kind: 'codes'" },
        new object[] { "AiObservation", "value: 'blockBilling'", "value: 'blockBillingX'" },
        new object[] { "ExpenseRule", "atLeastOneOf: ['nonReimbursableCodes', 'maxAmountPerItem']", "atLeastOneOf: ['nonReimbursableCodes']" },
        new object[] { "AddedFee", "exactlyOneOf: ['capAmount', 'capPercent']", "exactlyOneOf: ['capAmount']" },
        new object[] { "Discount", "key: 'tolerancePercent'", "key: 'toleranceX'" },
        // 084c fix round 1 (item 5): a conditional field shown on the wrong value, on the wrong key, or always.
        new object[] { "AddedFee", "equals: 'afterDays'", "equals: 'cap'" },
        new object[] { "AddedFee", "showWhen: { key: 'allowance', equals: 'afterDays' }", "showWhen: { key: 'feeKind', equals: 'afterDays' }" },
        new object[] { "AddedFee", "spaces.\", showWhen: { key: 'allowance', equals: 'cap' }", "spaces.\"" },
    };

    [Theory(DisplayName = "084c: a drift in a form fails the agreement (must-fire)")]
    [MemberData(nameof(FormDrifts))]
    public void ASeededFormDrift_IsCaught(string type, string find, string replacement)
    {
        var text = ReadForm(type);
        Assert.Contains(find, text);
        Assert.Empty(Compare(ParseForm(text), ReadSchema(type), CodePatternInForms(), MaxCodesInForms()));
        Assert.NotEmpty(Compare(ParseForm(text.Replace(find, replacement)), ReadSchema(type), CodePatternInForms(), MaxCodesInForms()));
    }

    [Fact(DisplayName = "084c: an extra field line, a missing field line and a field split over two lines are caught (must-fire)")]
    public void FormShapeDrifts_AreCaught()
    {
        var text = ReadForm("DailyHoursCap");
        var form = ParseForm(text);
        var extra = text.Replace("  ],\n};", "    { key: 'extraField', kind: 'number', label: \"x\", help: \"x\" },\n  ],\n};").Replace("  ],\r\n};", "    { key: 'extraField', kind: 'number', label: \"x\", help: \"x\" },\r\n  ],\r\n};");
        Assert.NotEqual(text, extra);
        Assert.NotEmpty(Compare(ParseForm(extra), ReadSchema("DailyHoursCap"), CodePatternInForms(), MaxCodesInForms()));
        Assert.NotEmpty(Compare(new FormDef(form.RuleType, Array.Empty<FormFieldDef>(), false, Array.Empty<string>(), Array.Empty<string>()), ReadSchema("DailyHoursCap"), CodePatternInForms(), MaxCodesInForms()));

        var split = text.Replace("kind: 'number', label:", "kind: 'number',\n      label:");
        Assert.NotEqual(text, split);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ParseForm(split));
    }

    [Fact(DisplayName = "084c: the code pattern and code-list limit in body.ts are the schemas' (must-fire on a changed pattern)")]
    public void TheCodePattern_IsTheSchemas()
    {
        var body = File.ReadAllText(Path.Combine(FormsDir, "body.ts"));
        Assert.Matches(@"CODE_PATTERN\s*=\s*/\^\[A-Za-z0-9\]\[A-Za-z0-9\._-\]\{0,19\}\$/", body);
        foreach (var type in new[] { "ExpenseRule", "NonBillableActivity", "AddedFee" })
        {
            using var doc = JsonDocument.Parse(ReadSchema(type));
            Assert.Equal(CodePatternInForms(), doc.RootElement.GetProperty("$defs").GetProperty("code").GetProperty("pattern").GetString());
        }

        Assert.NotEmpty(Compare(ParseForm(ReadForm("NonBillableActivity")), ReadSchema("NonBillableActivity"), "^[A-Z]+$", MaxCodesInForms()));
        Assert.NotEmpty(Compare(ParseForm(ReadForm("NonBillableActivity")), ReadSchema("NonBillableActivity"), CodePatternInForms(), 499));
    }

    // ── The parser ──────────────────────────────────────────────────────────

    internal sealed record FormFieldDef(
        string Key,
        string Kind,
        bool Required,
        bool Integer,
        double? Min,
        bool MinExclusive,
        double? Max,
        bool MaxExclusive,
        bool HasShowWhen,
        string[] Options,
        string[] Refused,
        string? ShowWhenKey = null,
        string? ShowWhenEquals = null);

    internal sealed record FormDef(string RuleType, FormFieldDef[] Fields, bool NoChange, string[] AtLeastOneOf, string[] ExactlyOneOf);

    /// <summary>Block comments and whole-line comments out, then every double-quoted string emptied (labels may hold any character).</summary>
    private static string Plain(string text)
    {
        var t = text.Replace("\r\n", "\n");
        t = Regex.Replace(t, @"/\*[\s\S]*?\*/", "");
        t = Regex.Replace(t, @"^[ \t]*//.*$", "", RegexOptions.Multiline);
        return Regex.Replace(t, "\"(?:[^\"\\\\\\n]|\\\\.)*\"", "\"\"");
    }

    internal static FormDef ParseForm(string source)
    {
        var text = Plain(source);
        var ruleType = Regex.Match(text, @"\bruleType:\s*'(\w+)'");
        Assert.True(ruleType.Success, "ruleType: '<Type>' not found");

        var fields = new List<FormFieldDef>();
        foreach (var raw in text.Split('\n'))
        {
            if (!Regex.IsMatch(raw, @"^\s*\{\s*key:\s*'")) continue;
            Assert.True(
                raw.Count(c => c == '{') == raw.Count(c => c == '}') && raw.Count(c => c == '[') == raw.Count(c => c == ']'),
                $"a field must be on one line (balanced braces and brackets): {raw.Trim()}");
            var hasShowWhen = raw.Contains("showWhen:", StringComparison.Ordinal);
            var showWhen = Regex.Match(raw, @"showWhen:\s*\{\s*key:\s*'(\w+)',\s*equals:\s*'([^']+)'\s*\}");
            var line = Regex.Replace(raw, @"showWhen:\s*\{[^{}]*\}", "");
            var key = Regex.Match(line, @"^\s*\{\s*key:\s*'(\w+)'").Groups[1].Value;
            Assert.True(!hasShowWhen || showWhen.Success, $"{key}: showWhen must read {{ key: '<field>', equals: '<value>' }}");
            var kind = Regex.Match(line, @"\bkind:\s*'(\w+)'");
            Assert.True(kind.Success, $"{key}: kind not found");

            var options = new List<string>();
            var refused = new List<string>();
            var at = line.IndexOf("options:", StringComparison.Ordinal);
            if (at >= 0)
            {
                var open = line.IndexOf('[', at);
                var depth = 0;
                var close = -1;
                for (var i = open; i < line.Length && close < 0; i++)
                {
                    if (line[i] == '[') depth++;
                    else if (line[i] == ']' && --depth == 0) close = i;
                }

                Assert.True(open >= 0 && close > open, $"{key}: options list not closed on its line");
                foreach (Match option in Regex.Matches(line[open..close], @"\{[^{}]*\}"))
                {
                    var value = Regex.Match(option.Value, @"\bvalue:\s*'([^']+)'");
                    Assert.True(value.Success, $"{key}: an option has no value: {option.Value}");
                    options.Add(value.Groups[1].Value);
                    if (Regex.IsMatch(option.Value, @"\brefused:")) refused.Add(value.Groups[1].Value);
                }

                line = line[..at] + line[(close + 1)..];
            }

            double? Number(string name) => Regex.Match(line, $@"\b{name}:\s*(-?\d+(?:\.\d+)?)") is { Success: true } m ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
            bool Flag(string name) => Regex.IsMatch(line, $@"\b{name}:\s*true");
            fields.Add(new FormFieldDef(
                key,
                kind.Groups[1].Value,
                Flag("required"),
                Flag("integer"),
                Number("min"),
                Flag("minExclusive"),
                Number("max"),
                Flag("maxExclusive"),
                hasShowWhen,
                options.ToArray(),
                refused.ToArray(),
                showWhen.Success ? showWhen.Groups[1].Value : null,
                showWhen.Success ? showWhen.Groups[2].Value : null));
        }

        string[] KeysOf(string name) =>
            Regex.Match(text, $@"\b{name}:\s*\[([^\]]*)\]") is { Success: true } m ? Regex.Matches(m.Groups[1].Value, @"'(\w+)'").Select(x => x.Groups[1].Value).ToArray() : Array.Empty<string>();

        return new FormDef(ruleType.Groups[1].Value, fields.ToArray(), Regex.IsMatch(text, @"\bnoChange:"), KeysOf("atLeastOneOf"), KeysOf("exactlyOneOf"));
    }

    // ── The comparison ──────────────────────────────────────────────────────

    private static bool SameSet(IEnumerable<string> a, IEnumerable<string> b) => new HashSet<string>(a).SetEquals(b);

    internal static List<string> Compare(FormDef form, string schemaText, string codePattern, int maxCodes)
    {
        var problems = new List<string>();
        using var doc = JsonDocument.Parse(schemaText);
        var root = doc.RootElement;

        if (!(root.GetProperty("properties").TryGetProperty("type", out var typeProp) && typeProp.TryGetProperty("const", out var constant) && constant.GetString() == form.RuleType))
            problems.Add($"the schema's type const is not {form.RuleType}");

        var props = root.GetProperty("properties").EnumerateObject().Where(p => p.Name != "type").ToDictionary(p => p.Name, p => p.Value);
        var formKeys = form.Fields.Select(f => f.Key).ToList();
        if (formKeys.Count != formKeys.Distinct().Count()) problems.Add("a field key is listed twice");
        foreach (var key in props.Keys.Except(formKeys)) problems.Add($"the schema has property '{key}' that the form has no field for");
        foreach (var key in formKeys.Except(props.Keys)) problems.Add($"the form has field '{key}' that the schema does not have");

        var required = root.TryGetProperty("required", out var req) ? req.EnumerateArray().Select(x => x.GetString()!).Where(x => x != "type").ToHashSet() : new HashSet<string>();
        foreach (var key in required.Except(props.Keys)) problems.Add($"the schema requires '{key}' which is not one of its properties");
        foreach (var field in form.Fields.Where(f => props.ContainsKey(f.Key)))
        {
            var p = props[field.Key];
            var name = field.Key;
            if (field.Required != required.Contains(name)) problems.Add($"{name}: required is {field.Required} in the form and {required.Contains(name)} in the schema");
            var type = p.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;

            switch (field.Kind)
            {
                case "number":
                    if (type is not ("number" or "integer")) { problems.Add($"{name}: a number field, but the schema type is {type}"); break; }
                    if (field.Integer != (type == "integer")) problems.Add($"{name}: integer is {field.Integer} in the form and the schema type is {type}");
                    Bound(problems, name, "min", field.Min, field.MinExclusive, p, "minimum", "exclusiveMinimum");
                    Bound(problems, name, "max", field.Max, field.MaxExclusive, p, "maximum", "exclusiveMaximum");
                    break;
                case "choice":
                    if (!p.TryGetProperty("enum", out var e)) { problems.Add($"{name}: a choice field, but the schema has no enum"); break; }
                    if (!SameSet(field.Options, e.EnumerateArray().Select(x => x.GetString()!))) problems.Add($"{name}: the options are not the schema's enum");
                    break;
                case "multi":
                    if (type != "array" || !p.TryGetProperty("items", out var mi) || !mi.TryGetProperty("enum", out var me)) { problems.Add($"{name}: a multi field, but the schema is not an array of an enum"); break; }
                    if (!SameSet(field.Options, me.EnumerateArray().Select(x => x.GetString()!))) problems.Add($"{name}: the options are not the schema's items enum");
                    break;
                case "codes":
                    if (type != "array" || !p.TryGetProperty("items", out var ci) || !ci.TryGetProperty("$ref", out var rf) || rf.GetString() != "#/$defs/code") { problems.Add($"{name}: a codes field, but the schema is not an array of $defs/code"); break; }
                    if (!(p.TryGetProperty("maxItems", out var mx) && mx.GetInt32() == maxCodes)) problems.Add($"{name}: the schema's maxItems is not the form's limit {maxCodes}");
                    if (!(root.TryGetProperty("$defs", out var defs) && defs.TryGetProperty("code", out var code) && code.TryGetProperty("pattern", out var pat) && pat.GetString() == codePattern))
                        problems.Add($"{name}: the schema's code pattern is not the form's");
                    break;
                default:
                    problems.Add($"{name}: unknown field kind {field.Kind}");
                    break;
            }
        }

        // "at least one of" is the root anyOf of single required members; "exactly one of" is a oneOf of them under an allOf branch.
        var anyOf = root.TryGetProperty("anyOf", out var any) ? SingleRequired(any) : new List<string>();
        if (!SameSet(form.AtLeastOneOf, anyOf)) problems.Add($"atLeastOneOf is [{string.Join(",", form.AtLeastOneOf)}] in the form and [{string.Join(",", anyOf)}] in the schema");
        var oneOf = new List<string>();
        if (root.TryGetProperty("allOf", out var all))
            foreach (var branch in all.EnumerateArray())
                if (branch.TryGetProperty("then", out var then) && then.TryGetProperty("oneOf", out var one))
                    oneOf.AddRange(SingleRequired(one));
        if (!SameSet(form.ExactlyOneOf, oneOf)) problems.Add($"exactlyOneOf is [{string.Join(",", form.ExactlyOneOf)}] in the form and [{string.Join(",", oneOf)}] in the schema");

        ConditionalFields(problems, form, root);
        return problems;
    }

    /// <summary>
    /// 084c fix round 1 (item 5): a field shown only when another field has a value (<c>showWhen: { key, equals }</c>) is the schema's
    /// <c>allOf[]</c> branch <c>if.properties.&lt;key&gt;.const = equals</c>. The fields a branch shows are exactly the members its
    /// <c>then</c> needs (<c>then.required</c> plus the single-member <c>then.oneOf</c> entries); a branch that needs none shows none, and
    /// a field the schema needs only in a branch is never shown always.
    /// </summary>
    private static void ConditionalFields(List<string> problems, FormDef form, JsonElement root)
    {
        var branches = new List<(string Key, string Value, HashSet<string> Needs)>();
        if (root.TryGetProperty("allOf", out var all))
        {
            foreach (var branch in all.EnumerateArray())
            {
                var tests = branch.TryGetProperty("if", out var cond) && cond.TryGetProperty("properties", out var cp)
                    ? cp.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("const", out _)).ToList()
                    : new List<JsonProperty>();
                if (tests.Count != 1)
                {
                    problems.Add("an allOf branch does not test exactly one property's const; the form's showWhen cannot say it");
                    continue;
                }

                var needs = new HashSet<string>(StringComparer.Ordinal);
                if (branch.TryGetProperty("then", out var then))
                {
                    if (then.TryGetProperty("required", out var r)) needs.UnionWith(r.EnumerateArray().Select(x => x.GetString()!));
                    if (then.TryGetProperty("oneOf", out var one)) needs.UnionWith(SingleRequired(one));
                }

                branches.Add((tests[0].Name, tests[0].Value.GetProperty("const").ToString(), needs));
            }
        }

        foreach (var field in form.Fields.Where(f => f.ShowWhenKey is not null))
        {
            if (!branches.Any(b => b.Key == field.ShowWhenKey && b.Value == field.ShowWhenEquals))
                problems.Add($"{field.Key}: shown when {field.ShowWhenKey} is '{field.ShowWhenEquals}', but the schema has no allOf branch on {field.ShowWhenKey} = '{field.ShowWhenEquals}'");
            var on = form.Fields.FirstOrDefault(f => f.Key == field.ShowWhenKey);
            if (on is null || !on.Options.Contains(field.ShowWhenEquals))
                problems.Add($"{field.Key}: shown when {field.ShowWhenKey} is '{field.ShowWhenEquals}', which is not an option of a field of the form");
        }

        foreach (var (key, value, needs) in branches)
        {
            var shown = form.Fields.Where(f => f.ShowWhenKey == key && f.ShowWhenEquals == value).Select(f => f.Key).ToList();
            if (!SameSet(shown, needs))
                problems.Add($"when {key} is '{value}' the form shows [{string.Join(",", shown.Order(StringComparer.Ordinal))}] and the schema's branch needs [{string.Join(",", needs.Order(StringComparer.Ordinal))}]");
        }
    }

    private static List<string> SingleRequired(JsonElement array) =>
        array.EnumerateArray()
            .Where(x => x.TryGetProperty("required", out var r) && r.GetArrayLength() == 1)
            .Select(x => x.GetProperty("required")[0].GetString()!)
            .ToList();

    private static void Bound(List<string> problems, string name, string which, double? value, bool exclusive, JsonElement prop, string inclusiveKey, string exclusiveKey)
    {
        double? schemaValue = null;
        var schemaExclusive = false;
        if (prop.TryGetProperty(inclusiveKey, out var i)) schemaValue = i.GetDouble();
        if (prop.TryGetProperty(exclusiveKey, out var x))
        {
            if (schemaValue is not null) problems.Add($"{name}: the schema has both {inclusiveKey} and {exclusiveKey}");
            schemaValue = x.GetDouble();
            schemaExclusive = true;
        }

        if (value != schemaValue) problems.Add($"{name}: {which} is {value?.ToString(CultureInfo.InvariantCulture) ?? "absent"} in the form and {schemaValue?.ToString(CultureInfo.InvariantCulture) ?? "absent"} in the schema");
        else if (value is not null && exclusive != schemaExclusive) problems.Add($"{name}: {which} is {(exclusive ? "exclusive" : "inclusive")} in the form and {(schemaExclusive ? "exclusive" : "inclusive")} in the schema");
    }

    // ── Files ───────────────────────────────────────────────────────────────

    private static string FormsDir => Path.Combine(RepoRoot(), "src", "solutions", "RecordProcedures", "src", "components", "wizards", "ruleForms");

    private static string ReadForm(string type) => File.ReadAllText(Path.Combine(FormsDir, $"{type}.form.ts")).Replace("\r\n", "\n");

    private static string ReadSchema(string type) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "server", "api", "Sprk.Bff.Api", "Services", "Signals", "Schemas", $"billing-{Kebab(type)}.schema.json")).Replace("\r\n", "\n");

    private static string Kebab(string type) => Regex.Replace(type, "(?<!^)([A-Z])", "-$1").ToLowerInvariant();

    private static string CodePatternInForms()
    {
        var m = Regex.Match(File.ReadAllText(Path.Combine(FormsDir, "body.ts")), @"CODE_PATTERN\s*=\s*/(?<p>.+)/;");
        Assert.True(m.Success, "CODE_PATTERN not found in ruleForms/body.ts");
        return m.Groups["p"].Value;
    }

    private static int MaxCodesInForms()
    {
        var m = Regex.Match(File.ReadAllText(Path.Combine(FormsDir, "body.ts")), @"MAX_CODES\s*=\s*(\d+)");
        Assert.True(m.Success, "MAX_CODES not found in ruleForms/body.ts");
        return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Spaarke.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
