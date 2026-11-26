using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        var summaryNumber = 0;
        var index = -1;

        foreach (var item in RequireArray(root, "edits", "$"))
        {
            index++;
            var path = $"$.edits[{index}]";
            var source = RequireObject(item, path);

            var topic = RequireString(source, "topic", path);
            if (!PinnedNdaStandard.TopicIds.Contains(topic))
            {
                // The model named a topic the pinned standard does not have: never forwarded, never guessed at.
                throw new AgreementOutputRefusal(AgreementAiException.PlanInvalid, $"{path}.topic");
            }

            var para = RequireString(source, "para", path);
            var find = OptionalString(source, "find", path);
            var old = OptionalString(source, "old", path);
            var replace = OptionalString(source, "replace", path);
            var after = OptionalString(source, "after", path);
            var insert = OptionalString(source, "insert", path);
            var comment = OptionalString(source, "comment", path);

            var section = OptionalString(source, "section", path);
            var issue = OptionalString(source, "issue", path);
            var revision = OptionalString(source, "revision", path);
            var priority = OptionalString(source, "priority", path);
            var summaryMembers = new[] { section, issue, revision, priority };
            if (summaryMembers.Any(m => m is not null) && summaryMembers.Any(m => m is null))
            {
                // A summary row is all four members or none: half a row would render a table cell with no text.
                throw new AgreementOutputRefusal(AgreementAiException.OutputInvalid, $"{path}.summary");
            }

            // Exact-match anchor pre-check: a closed para_key list and quotes that occur once. No repair, no closest match.
            var code = segmentation.CheckAnchors(para, find, old, after);
            if (code is not null)
            {
                refused.Add(new AgreementRefusedEdit(index, code, topic));
                continue;
            }

            var edit = new JsonObject { ["para"] = para };
            AddIfPresent(edit, "find", find);
            AddIfPresent(edit, "old", old);
            AddIfPresent(edit, "replace", replace);
            AddIfPresent(edit, "after", after);
            AddIfPresent(edit, "insert", insert);
            AddIfPresent(edit, "comment", comment);
            edit["topic"] = topic;

            if (section is not null)
            {
                if (bookmarks[para] is null)
                {
                    bookmarks[para] = $"NDA_Sec{bookmarks.Count + 1}";
                }

                edit["summary"] = new JsonObject
                {
                    ["n"] = ++summaryNumber,
                    ["section"] = section,
                    ["bookmark"] = bookmarks[para]!.GetValue<string>(),
                    ["issue"] = issue,
                    ["revision"] = revision,
                    ["priority"] = priority,
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
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new AgreementOutputRefusal(AgreementAiException.OutputInvalid, $"{path}.{name}");
        }

        return value.GetString()!;
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
            JsonValueKind.String => value.GetString(),
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
