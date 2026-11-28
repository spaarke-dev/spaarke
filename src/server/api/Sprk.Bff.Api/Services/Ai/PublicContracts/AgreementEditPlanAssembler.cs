using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using Json.Schema;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>Raised by <see cref="AgreementEditPlanAssembler"/>; carries a reason code and the JSON path that failed, never content.</summary>
internal sealed class AgreementOutputRefusal(string reasonCode, string path) : Exception($"Refused at '{path}'.")
{
    public string ReasonCode { get; } = reasonCode;

    public string Path { get; } = path;
}

/// <summary>
/// Turns the <c>agreement-edit-plan</c> Action's output (topic-keyed edits) into the complete plan the redline engines take, and
/// refuses anything that is not safe to hand them (spaarke-intake-pipeline-r1 task 004, FR-02, NFR-09).
/// </summary>
/// <remarks>
/// <para>
/// <b>Who writes what.</b> The model writes only the edits: where (<c>para</c>, <c>find</c>/<c>after</c>), what
/// (<c>old</c>, <c>replace</c>, <c>insert</c>), why (<c>comment</c>), which topic, and the words of its summary row. This class
/// writes <c>author</c>, <c>initials</c>, <c>date</c>, <c>provenance</c>, <c>agreement</c>, <c>review_basis</c> (the pinned standard's
/// name and version), the bookmarks and the summary numbering and links. A bookmark name or an <c>n</c> from a model could collide or
/// skip a number and the engines refuse the plan, so neither is ever asked of it.
/// </para>
/// <para>
/// <b>Structured Outputs.</b> The Action's output schema is the strict-mode subset: every member required, optional ones nullable,
/// no <c>oneOf</c>, <c>format</c>, <c>pattern</c> or length bound. The rules that subset cannot express (an edit is a replacement or a
/// pure insertion, never both; a pattern on the topic id; the date format) are enforced here by validating the assembled plan
/// against the accelerator's own <c>edit-plan.schema.json</c>, plus the topic check against the pinned standard.
/// </para>
/// <para>
/// <b>Anchoring = a closed para_key list plus an exact-match pre-check; there is no fuzzy matching anywhere.</b> See
/// <see cref="AgreementSegmentation"/>. An edit whose anchors do not fit is listed as refused with a closed-set code and left out of the plan.
/// </para>
/// <para>
/// <b>One edit's fault never costs the plan.</b> A malformed edit (a missing member, a topic outside the pinned standard, a shape the
/// engine's <c>validate_plan</c> rejects) is refused on its own as <see cref="AgreementRefusalCodes.EditInvalid"/>. Only faults of the
/// output as a whole are thrown: not an object, no <c>edits</c> or <c>unplaceable</c> array, or an <c>unplaceable</c> finding that names
/// no section or no pinned topic. The summary row is one nullable object in the model's schema (<c>summaryRow</c>), so the model cannot
/// write half a row (live run 2026-11-26, D1).
/// </para>
/// <para>
/// <b>Refusals carry no content</b> (ADR-015): a reason code and a JSON path only. Schema errors are reduced to the instance location.
/// </para>
/// </remarks>
internal static class AgreementEditPlanAssembler
{
    /// <summary>The skill named on the plan's provenance line: the Action's code.</summary>
    internal const string SkillName = "agreement-edit-plan";

    /// <summary>The Action's <c>$version</c>; a contract test pins it to <c>agreement-edit-plan.action.json</c>.</summary>
    internal const string SkillVersion = "1";

    private static readonly JsonSerializerOptions PlanJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly Lazy<JsonSchema> EditPlanSchema = new(
        () => JsonSchema.FromText(PinnedNdaStandard.ReadResource(PinnedNdaStandard.EditPlanSchemaResource)));

    // JsonSchema.Net 7.3.4's Evaluate is not safe for concurrent calls on one schema instance (measured in
    // RuleBodySchemaValidator, which serialises the same way); a false IsValid here would send a malformed plan to the engines.
    private static readonly object EvaluationGate = new();

    /// <summary>
    /// Builds the plan from the model's <c>edits</c>. <paramref name="segmentation"/> is the closed list of anchors: an edit whose
    /// <c>para</c> or quotes do not fit it exactly is refused (<see cref="AgreementEditPlan.RefusedEdits"/>), never repaired.
    /// </summary>
    public static AgreementEditPlan Assemble(
        JsonElement output, AgreementSegmentation segmentation, AgreementEditPlanRequest request, DateTimeOffset reviewedAt)
    {
        var root = RequireObject(output, "$");

        var edits = new JsonArray();
        var bookmarks = new JsonObject();
        var topicIds = new List<string>();
        var refused = new List<AgreementRefusedEdit>();
        var anchors = segmentation.BeginPlan();
        var summaryNumber = 0;
        var index = -1;

        foreach (var item in RequireArray(root, "edits", "$"))
        {
            index++;

            // A fault in one edit refuses that edit only; the rest of the plan is kept (live run 2026-11-26, D1).
            var e = ReadEdit(item, $"$.edits[{index}]", out var pinnedTopic);
            if (e is null)
            {
                refused.Add(new AgreementRefusedEdit(index, AgreementRefusalCodes.EditInvalid, pinnedTopic ?? string.Empty));
                continue;
            }

            // Exact-match anchor pre-check, in plan order against the clause as the kept edits leave it. No repair, no closest match.
            var code = anchors.Check(e.Para, e.Find, e.Old, e.Replace, e.After, e.Insert);
            if (code is not null)
            {
                refused.Add(new AgreementRefusedEdit(index, code, e.Topic));
                continue;
            }

            var para = e.Para;
            var topic = e.Topic;
            var edit = new JsonObject { ["para"] = para };
            AddIfPresent(edit, "find", e.Find);
            AddIfPresent(edit, "old", e.Old);
            AddIfPresent(edit, "replace", e.Replace);
            AddIfPresent(edit, "after", e.After);
            AddIfPresent(edit, "insert", e.Insert);
            AddIfPresent(edit, "comment", e.Comment);
            edit["topic"] = topic;

            if (e.Summary is { } row)
            {
                if (bookmarks[para] is null)
                {
                    bookmarks[para] = $"NDA_Sec{bookmarks.Count + 1}";
                }

                edit["summary"] = new JsonObject
                {
                    ["n"] = ++summaryNumber,
                    ["section"] = row.Section,
                    ["bookmark"] = bookmarks[para]!.GetValue<string>(),
                    ["issue"] = row.Issue,
                    ["revision"] = row.Revision,
                    ["priority"] = row.Priority,
                };
            }

            edits.Add(edit);
            if (!topicIds.Contains(topic, StringComparer.Ordinal))
            {
                topicIds.Add(topic);
            }
        }

        var unplaceable = ReadUnplaceable(root, segmentation);

        var plan = new JsonObject
        {
            ["author"] = string.IsNullOrWhiteSpace(request.Author) ? AgreementEditPlanRequest.DefaultAuthor : request.Author.Trim(),
            ["initials"] = string.IsNullOrWhiteSpace(request.Initials) ? AgreementEditPlanRequest.DefaultInitials : request.Initials.Trim(),
            ["date"] = reviewedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["provenance"] = $"Standards: {PinnedNdaStandard.Name} v{PinnedNdaStandard.Version} (customer playbook) · Skill: {SkillName} v{SkillVersion}",
        };

        if (!string.IsNullOrWhiteSpace(request.FileName))
        {
            plan["agreement"] = new JsonObject { ["file_name"] = request.FileName.Trim() };
        }

        plan["review_basis"] = new JsonObject
        {
            ["playbook"] = PinnedNdaStandard.Name,
            ["version"] = PinnedNdaStandard.Version,
            ["source"] = "customer",
            ["side"] = "mutual",
            ["posture"] = "balanced",
        };

        if (bookmarks.Count > 0)
        {
            plan["bookmarks"] = bookmarks;
        }

        plan["edits"] = edits;

        ValidateAgainstSchema(plan);

        return new AgreementEditPlan
        {
            PlanJson = plan.ToJsonString(PlanJson),
            StandardName = PinnedNdaStandard.Name,
            StandardVersion = PinnedNdaStandard.Version,
            TopicIds = topicIds,
            EditCount = edits.Count,
            RefusedEdits = refused,
            UnplaceableFindings = unplaceable,
        };
    }

    /// <summary>One edit as the model wrote it, after its shape was checked. Null members were null in the output.</summary>
    private sealed record ModelEdit(
        string Topic, string Para, string? Find, string? Old, string? Replace, string? After, string? Insert, string? Comment, SummaryRow? Summary);

    /// <summary>The four words of an edit's summary-table row: one nullable object in the model's schema, so all four or none.</summary>
    private sealed record SummaryRow(string Section, string Issue, string Revision, string Priority);

    private static readonly string[] Priorities = ["High", "Medium", "Low"];

    /// <summary>
    /// Reads and checks one edit. Returns null when the edit is malformed (a missing member, a wrong JSON kind, a topic outside the pinned
    /// standard, or a shape the engine's <c>validate_plan</c> or <c>edit-plan.schema.json</c> rejects): such an edit is refused on its
    /// own, because the engine would reject the whole plan for it. <paramref name="pinnedTopic"/> is the edit's topic when it is one of
    /// the pinned standard's, else null (model text is never carried into a refusal).
    /// </summary>
    private static ModelEdit? ReadEdit(JsonElement item, string path, out string? pinnedTopic)
    {
        pinnedTopic = null;
        try
        {
            var source = RequireObject(item, path);
            var topic = RequireString(source, "topic", path);
            if (!PinnedNdaStandard.TopicIds.Contains(topic))
            {
                return null; // never forwarded, never guessed at
            }

            pinnedTopic = topic;
            var edit = new ModelEdit(
                topic,
                RequireString(source, "para", path),
                OptionalString(source, "find", path),
                OptionalString(source, "old", path),
                OptionalString(source, "replace", path),
                OptionalString(source, "after", path),
                OptionalString(source, "insert", path),
                OptionalString(source, "comment", path),
                ReadSummaryRow(source, path));
            return IsWellFormed(edit) ? edit : null;
        }
        catch (AgreementOutputRefusal)
        {
            return null;
        }
    }

    /// <summary><c>summaryRow</c>: null, or an object whose four members are non-blank strings and a known priority.</summary>
    private static SummaryRow? ReadSummaryRow(JsonElement source, string path)
    {
        if (!source.TryGetProperty("summaryRow", out var row))
        {
            throw new AgreementOutputRefusal(AgreementAiException.OutputInvalid, $"{path}.summaryRow");
        }

        if (row.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var rowPath = $"{path}.summaryRow";
        var priority = RequireString(RequireObject(row, rowPath), "priority", rowPath);
        if (!Priorities.Contains(priority, StringComparer.Ordinal))
        {
            throw new AgreementOutputRefusal(AgreementAiException.OutputInvalid, $"{rowPath}.priority");
        }

        return new SummaryRow(
            RequireString(row, "section", rowPath), RequireString(row, "issue", rowPath), RequireString(row, "revision", rowPath), priority);
    }

    /// <summary>
    /// The per-edit rules of the engine's <c>validate_plan</c> and of <c>edit-plan.schema.json</c> that the strict output schema cannot
    /// state: exactly one of <c>find</c> and <c>after</c>; no blank quote; an insertion has text and no <c>replace</c>/<c>old</c>; a
    /// replacement has no <c>insert</c>; a comment-only edit and a summary row each need a comment; a comment is never empty. Every
    /// string the engine writes into the document is XML 1.0 text: the engine stops the WHOLE plan (not a skippable refusal) on a
    /// control character, a lone surrogate or U+FFFE/U+FFFF (live run 2026-11-26: the model wrote U+0000 into four replacements).
    /// </summary>
    private static bool IsWellFormed(ModelEdit e)
    {
        static bool Blank(string? s) => s is not null && string.IsNullOrWhiteSpace(s);

        if ((e.Find is null) == (e.After is null) || Blank(e.Find) || Blank(e.Old) || Blank(e.After) || e.Comment is { Length: 0 })
        {
            return false;
        }

        // Refused, never cleaned: removing the character would change what the model wrote (the live U+0000 stood in for an escape).
        if (!new[] { e.Para, e.Find, e.Old, e.Replace, e.After, e.Insert, e.Comment, e.Summary?.Section, e.Summary?.Issue, e.Summary?.Revision }
                .All(IsXmlText))
        {
            return false;
        }

        var hasComment = !string.IsNullOrEmpty(e.Comment);
        if (e.After is not null
            ? string.IsNullOrEmpty(e.Insert) || e.Replace is not null || e.Old is not null
            : e.Insert is not null || (e.Replace is null && !hasComment))
        {
            return false;
        }

        return e.Summary is null || hasComment;
    }

    /// <summary>True when every character is an XML 1.0 <c>Char</c> (a surrogate only as part of a pair), as lxml requires; null is true.</summary>
    private static bool IsXmlText(string? s)
    {
        if (s is null)
        {
            return true;
        }

        for (var i = 0; i < s.Length; i++)
        {
            if (XmlConvert.IsXmlChar(s[i]))
            {
                continue;
            }

            if (i + 1 < s.Length && XmlConvert.IsXmlSurrogatePair(s[i + 1], s[i]))
            {
                i++;
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// The model's findings on sections that cannot be edited. A finding names a pinned topic and a section of the supplied
    /// segmentation; it is reported, never anchored.
    /// </summary>
    private static List<AgreementUnplaceableFinding> ReadUnplaceable(JsonElement root, AgreementSegmentation segmentation)
    {
        var findings = new List<AgreementUnplaceableFinding>();
        var index = -1;
        foreach (var item in RequireArray(root, "unplaceable", "$"))
        {
            index++;
            var path = $"$.unplaceable[{index}]";
            var source = RequireObject(item, path);

            var topic = RequireString(source, "topic", path);
            if (!PinnedNdaStandard.TopicIds.Contains(topic))
            {
                throw new AgreementOutputRefusal(AgreementAiException.PlanInvalid, $"{path}.topic");
            }

            if (!source.TryGetProperty("section_index", out var sectionIndex)
                || sectionIndex.ValueKind != JsonValueKind.Number
                || !sectionIndex.TryGetInt32(out var sectionNumber)
                || !segmentation.HasSection(sectionNumber))
            {
                throw new AgreementOutputRefusal(AgreementAiException.OutputInvalid, $"{path}.section_index");
            }

            findings.Add(new AgreementUnplaceableFinding(topic, sectionNumber, RequireString(source, "comment", path)));
        }

        return findings;
    }

    private static void ValidateAgainstSchema(JsonObject plan)
    {
        EvaluationResults results;
        try
        {
            lock (EvaluationGate)
            {
                results = EditPlanSchema.Value.Evaluate(
                    plan, new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });
            }
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException or IndexOutOfRangeException)
        {
            throw new AgreementOutputRefusal(AgreementAiException.PlanInvalid, "$");
        }

        if (results.IsValid)
        {
            return;
        }

        // Instance locations only (JSON pointers such as /edits/2/find): the library's error text can quote values.
        var location = results.Details
            .Where(d => d.HasErrors)
            .Select(d => d.InstanceLocation.ToString())
            .FirstOrDefault(l => !string.IsNullOrEmpty(l)) ?? "$";
        throw new AgreementOutputRefusal(AgreementAiException.PlanInvalid, location);
    }

    // ── Strict readers for the Action's output (a missing member or a wrong JSON kind is OutputInvalid, never a default) ──

    private static JsonElement RequireObject(JsonElement element, string path) =>
        element.ValueKind == JsonValueKind.Object
            ? element
            : throw new AgreementOutputRefusal(AgreementAiException.OutputInvalid, path);

    private static IEnumerable<JsonElement> RequireArray(JsonElement parent, string name, string path)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new AgreementOutputRefusal(AgreementAiException.OutputInvalid, $"{path}.{name}");
        }

        return value.EnumerateArray();
    }

    private static string RequireString(JsonElement parent, string name, string path)
    {
        if (!parent.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.String
            || ReadString(value, $"{path}.{name}") is not { } text
            || string.IsNullOrWhiteSpace(text))
        {
            throw new AgreementOutputRefusal(AgreementAiException.OutputInvalid, $"{path}.{name}");
        }

        return text;
    }

    /// <summary>
    /// A JSON string's value. An escaped lone surrogate (<c>\uD800</c>) is valid JSON that <see cref="JsonElement.GetString"/> cannot
    /// decode (it throws <see cref="InvalidOperationException"/>): that is a refusal at this path, never an unhandled error.
    /// </summary>
    private static string ReadString(JsonElement value, string path)
    {
        try
        {
            return value.GetString()!;
        }
        catch (InvalidOperationException)
        {
            throw new AgreementOutputRefusal(AgreementAiException.OutputInvalid, path);
        }
    }

    /// <summary>A member that must be present (strict mode lists every member) and is a string or null.</summary>
    private static string? OptionalString(JsonElement parent, string name, string path)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            throw new AgreementOutputRefusal(AgreementAiException.OutputInvalid, $"{path}.{name}");
        }

        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => ReadString(value, $"{path}.{name}"),
            _ => throw new AgreementOutputRefusal(AgreementAiException.OutputInvalid, $"{path}.{name}"),
        };
    }

    private static void AddIfPresent(JsonObject target, string name, string? value)
    {
        if (value is not null)
        {
            target[name] = value;
        }
    }
}
