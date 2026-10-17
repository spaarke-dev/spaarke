using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>Who a rule set applies to (<c>sprk_ruleset.sprk_scope</c>, task 019). Values are the option values.</summary>
public enum RuleSetScope
{
    ClientDefault = 100000000,
    Firm = 100000001,
}

/// <summary>What the uploaded source document is. Sets the initial "visible to firms" flag (D-66); not stored.</summary>
public enum RuleSetSourceKind
{
    /// <summary>An outside counsel guideline: firms see its clause quotes (flag on).</summary>
    Guideline,

    /// <summary>A rate letter or rate card: firms see its clause quotes (flag on).</summary>
    RateCard,

    /// <summary>An internal procedure: firms never see it (flag off).</summary>
    InternalProcedure,
}

/// <summary>The review state of a drafted rule version (<c>sprk_policyversion.sprk_proposalstatus</c>, task 019).</summary>
public enum ProposalState
{
    Proposed = 100000000,
    Accepted = 100000001,
    Rejected = 100000002,
}

/// <summary>What happens when a billing rule fails (<c>sprk_policyversion.sprk_consequence</c>, task 019).</summary>
public enum BillingConsequence
{
    Reject = 100000000,
    Flag = 100000001,
    SuggestAdjustment = 100000002,
}

/// <summary>What the caller asked to create. The caller's object id is server-derived, never from the body.</summary>
public sealed record RuleSetCreateRequest(
    string Name, RuleSetSourceKind SourceKind, RuleSetScope Scope, Guid? FirmId, string FileName, Guid? CallerObjectId);

/// <summary>A rule set row as the admin sees it.</summary>
public sealed record RuleSetSummary(
    Guid Id, string Name, string Status, string Scope, Guid? FirmId, bool VisibleToFirms,
    DateOnly? EffectiveFrom, DateOnly? EffectiveTo, int RuleCount);

/// <summary>A created rule set: Draft, with its source document stored in the Procedure Library.</summary>
public sealed record RuleSetCreated(
    Guid RuleSetId, Guid SourceDocumentId, string Status, string Scope, Guid? FirmId, string SourceKind, bool VisibleToFirms);

/// <summary>One drafted or published rule (a billing policy) as its WORKING version shows it.</summary>
/// <param name="InForce">The working version is the policy's current version.</param>
/// <param name="HasPublishedVersion">The policy has (or had) a version in force, so an edit creates a new version.</param>
public sealed record ProposalView(
    Guid PolicyId, string PolicyCode, string Name, string State, bool InForce, bool HasPublishedVersion,
    Guid VersionId, int VersionNumber, string? RuleType, JsonNode? RuleBody, string? MessageTemplate, string? ShortHeadline,
    string? Consequence, string? DescriptionText, string? DescriptionFormal, string? DescriptionRefusal,
    string? ClauseQuote, string? ClauseLocation, IReadOnlyList<string> ValidationErrors);

/// <summary>A rule set with its rules (the Rule Review page's read).</summary>
public sealed record RuleSetDetail(
    RuleSetSummary RuleSet, Guid? SourceDocumentId, JsonNode? NotConverted, IReadOnlyList<ProposalView> Proposals);

/// <summary>An edit to a rule's working version. A <c>null</c> member is left as it is.</summary>
public sealed record ProposalEdit(
    string? RuleType, string? RuleBody, string? MessageTemplate, string? ShortHeadline, string? Consequence,
    string? ClauseReference, string? ClauseLocation, Guid? CallerObjectId);

/// <summary>The result of an edit: the working version after it, and whether a new version was created.</summary>
public sealed record ProposalEdited(ProposalView Proposal, bool CreatedNewVersion);

/// <summary>A version a publish put in force and the version it superseded.</summary>
public sealed record PublishedRuleVersion(Guid PolicyId, Guid VersionId, int VersionNumber, Guid? SupersededVersionId);

/// <summary>A successful publish. <see cref="ClauseQuotesVisibleToFirms"/> mirrors the rule set's flag at publish (FR-28, D-66).</summary>
public sealed record RuleSetPublished(
    Guid RuleSetId, string Status, DateOnly EffectiveDate, IReadOnlyList<PublishedRuleVersion> Published,
    int NotPublishedProposed, int NotPublishedRejected, bool ClauseQuotesVisibleToFirms, string ClauseVisibilityNotice);

/// <summary>One problem with one field of one rule (or of the request when <see cref="PolicyId"/> is null).</summary>
public sealed record RuleSetError(Guid? PolicyId, string? RuleName, string Field, string Message, string? Reason);

/// <summary>What a firm may be shown of a rule's clause. Both members are null when the rule set's flag is off (D-66, D-79).</summary>
/// <remarks>There is deliberately no member for the source document or any link to it (D-63): a firm never gets one.</remarks>
public sealed record FirmClauseText(string? Quote, string? SectionReference)
{
    public static FirmClauseText None { get; } = new(null, null);
}

/// <summary>
/// THE single decision whether a rule's clause text may be shown to a firm (ebilling-r1 task 076, D-66 / D-79): the findings (130), the SPA
/// invoice detail (134) and the Billing Guidelines view (135) all call it and nothing else decides.
/// </summary>
public static class RuleSetClauseVisibility
{
    /// <summary>
    /// With the rule set's "visible to firms" flag on, the quote and the section reference; with it off, nothing at all (the firm sees the
    /// rule name and reason only). Fails closed: a missing flag is off, and blank text is returned as null.
    /// </summary>
    public static FirmClauseText ForFirm(bool? ruleSetVisibleToFirms, string? clauseQuote, string? clauseLocation) =>
        ruleSetVisibleToFirms == true
            ? new FirmClauseText(NullIfBlank(clauseQuote), NullIfBlank(clauseLocation))
            : FirmClauseText.None;

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}

public enum RuleSetOutcomeKind
{
    Done,

    /// <summary>The input has problems (<see cref="RuleSetOutcome{T}.Errors"/>): HTTP 400, nothing written.</summary>
    Invalid,

    /// <summary>Refused for another stable reason (<see cref="RuleSetOutcome{T}.ErrorCode"/> with its status).</summary>
    Refused,

    /// <summary>No owner team could be resolved for the new rows: HTTP 409, nothing written.</summary>
    OwnerRefused,
}

/// <summary>The result of a rule set operation, mapped to HTTP by <c>RuleSetEndpoints</c>.</summary>
public sealed class RuleSetOutcome<T> where T : class
{
    private RuleSetOutcome() { }

    public RuleSetOutcomeKind Kind { get; private init; }
    public T? Value { get; private init; }
    public IReadOnlyList<RuleSetError> Errors { get; private init; } = Array.Empty<RuleSetError>();
    public string? ErrorCode { get; private init; }
    public int StatusCode { get; private init; }
    public string? Detail { get; private init; }
    public RecordOwnerResolution? OwnerRefusal { get; private init; }

    internal static RuleSetOutcome<T> Done(T value) => new() { Kind = RuleSetOutcomeKind.Done, Value = value };

    internal static RuleSetOutcome<T> Invalid(string errorCode, IReadOnlyList<RuleSetError> errors) =>
        new() { Kind = RuleSetOutcomeKind.Invalid, ErrorCode = errorCode, Errors = errors };

    internal static RuleSetOutcome<T> Refused(int statusCode, string errorCode, string detail) =>
        new() { Kind = RuleSetOutcomeKind.Refused, StatusCode = statusCode, ErrorCode = errorCode, Detail = detail };

    internal static RuleSetOutcome<T> OwnerRefused(RecordOwnerResolution resolution) =>
        new() { Kind = RuleSetOutcomeKind.OwnerRefused, OwnerRefusal = resolution };
}

/// <summary>
/// The rule set API's server-side owner (ebilling-r1 task 076, FR-28): create a rule set from an uploaded source document, read its rules
/// beside their clause quotes, edit / reject / accept each, publish, assign and set visibility.
/// </summary>
/// <remarks>
/// <para><b>Model.</b> A rule set (<c>sprk_ruleset</c>) groups billing policies (<c>sprk_policy</c>, <c>sprk_domain</c> = Billing, task 019); a
/// policy's versions (<c>sprk_policyversion</c>) hold the rule. A version is IN FORCE when it is the policy's <c>sprk_currentversion</c>;
/// nothing else is. A version's review state is <c>sprk_proposalstatus</c>. A policy's predecessor version is its sibling with the next
/// lower number (019: no link column). The WORKING version of a rule is its highest-numbered version.</para>
/// <para><b>Immutability.</b> A version that is current, or has ever been (it carries an <c>sprk_inforceto</c>), is never updated: an edit
/// creates a new version (next number, Proposed). A version that was never in force is a draft and is edited in place. Publish stamps the
/// predecessor's <c>sprk_inforceto</c> and moves the pointer; the only writes to an existing version are a draft's own columns (in place),
/// its <c>sprk_inforcefrom</c> at publish (the effective date) and a predecessor's <c>sprk_inforceto</c>.</para>
/// <para><b>Publish is all or nothing.</b> Every accepted rule that is not yet in force is validated
/// (<see cref="PolicyVersionValidator.ValidateForSave"/> with <see cref="RuleDomain.Billing"/>, consequence set, headline and template
/// set) before the first write; one failure refuses the whole publish with per-rule errors and writes nothing. The writes are three
/// transactions in an order that a retry completes: (1) the versions (the new versions' in-force-from, the predecessors' in-force-to), (2) the
/// policies (pointer and enabled), (3) the rule set (Published, effective-from). Nothing is in force before step 2. A failure
/// between steps leaves the old versions in force; a second publish re-derives the same pending set and finishes. Each step is atomic per
/// 1,000 rows (the Dataverse transaction cap), the whole is not.</para>
/// <para><b>Domain.</b> Every read of <c>sprk_policy</c> and <c>sprk_ruleset</c> here carries <c>sprk_domain = Billing</c> in the query
/// (<see cref="RuleDomains.AddBillingFilter"/>) AND is re-checked per row (<see cref="RuleDomains.IsBillingRow"/>), so an Ontology policy
/// (empty or Ontology domain) is the uniform "not found" for every operation and is never read into a response or written. This service
/// never writes <c>sprk_domain</c>. Versions are read only through a billing policy's id.</para>
/// <para><b>Identity.</b> The route filter has already established, as the caller, that they hold the Dataverse table privileges of a billing
/// admin (D-76). The writes run as the application (like the rate card import); the rows are owned by the owner team resolved for the caller.</para>
/// <para>§11: <i>Existing</i> — RateCardImportService (the Draft-to-Published shape, SPE upload, ownership) and the Ontology authoring publish
/// order (stamp then move). <i>Extension</i> — neither can be extended: the Ontology service is caller-identity and Ontology-domain by
/// design, and the rate card service owns other tables. <i>Cost of doing nothing</i> — no billing rule can be reviewed or put in force.</para>
/// </remarks>
public class RuleSetService
{
    internal const string RuleSetEntity = "sprk_ruleset";
    internal const string PolicyEntity = "sprk_policy";
    internal const string VersionEntity = "sprk_policyversion";
    internal const string DocumentEntity = "sprk_document";
    internal const string FirmEntity = "sprk_organization";

    internal const int RuleSetDraft = 1;
    internal const int RuleSetPublished = 659490000;
    internal const int RuleSetRetired = 2;

    // Stable error codes (ADR-019).
    internal const string CodeInvalid = "ruleset.invalid";
    internal const string CodePublishInvalid = "ruleset.publish_invalid";
    internal const string CodeNotFound = "ruleset.not_found";
    internal const string CodeProposalNotFound = "ruleset.proposal_not_found";
    internal const string CodeNothingToPublish = "ruleset.nothing_to_publish";
    internal const string CodeRetired = "ruleset.retired";
    internal const string CodeInForce = "ruleset.proposal_in_force";
    internal const string CodeContainerNotConfigured = "ruleset.container_not_configured";
    internal const string CodeResultTruncated = "ruleset.result_truncated";
    internal const string CodeWriteFailed = "ruleset.write_failed";

    /// <summary>The notice a publish carries when the flag is on (FR-28, D-66).</summary>
    internal const string VisibleNotice = "Clause quotes will be visible to firms.";

    /// <summary>The notice a publish carries when the flag is off.</summary>
    internal const string HiddenNotice = "Clause quotes will not be visible to firms.";

    // <= Dataverse's 1,000-request ExecuteTransaction cap; also keeps ConditionOperator.In under SQL Server's parameter limit.
    internal const int MaxPerTransaction = 1000;
    internal const int MaxInValues = 500;

    // sprk_policy columns billing needs on a policy that is NOT created here (075 creates them): see the notes for the row shape.
    private static readonly string[] PolicyColumns =
        { "sprk_name", "sprk_policycode", "sprk_currentversion", "sprk_enabled", "sprk_ruleset", RuleDomains.PolicyDomainColumn };

    private static readonly string[] VersionColumns =
    {
        "sprk_policy", "sprk_versionnumber", "sprk_ruletype", "sprk_rulebody", "sprk_messagetemplate", "sprk_shortheadline",
        "sprk_consequence", "sprk_proposalstatus", "sprk_clausereference", "sprk_clauselocation", "sprk_inforcefrom", "sprk_inforceto",
    };

    private static readonly string[] RuleSetColumns =
    {
        "sprk_name", "sprk_scope", "sprk_firm", "sprk_visibletofirms", "sprk_effectivefrom", "sprk_effectiveto", "sprk_sourcedocument",
        "sprk_notconverted", "statuscode", RuleDomains.PolicyDomainColumn,
    };

    private readonly IGenericEntityService _entities;
    private readonly IDocumentDataverseService _documents;
    private readonly SpeFileStore _spe;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly PolicyVersionValidator _validator;
    private readonly RuleBodyDescriber _describer;
    private readonly IOptionsMonitor<FinanceOptions> _finance;
    private readonly ILogger<RuleSetService> _logger;

    public RuleSetService(
        IGenericEntityService entities,
        IDocumentDataverseService documents,
        SpeFileStore spe,
        IRecordOwnershipResolver ownership,
        PolicyVersionValidator validator,
        RuleBodyDescriber describer,
        IOptionsMonitor<FinanceOptions> finance,
        ILogger<RuleSetService> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _spe = spe ?? throw new ArgumentNullException(nameof(spe));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _describer = describer ?? throw new ArgumentNullException(nameof(describer));
        _finance = finance ?? throw new ArgumentNullException(nameof(finance));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Create
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The initial "visible to firms" flag for a source kind (D-66): on for guidelines and rate cards, off for internal procedures.</summary>
    internal static bool InitialVisibleToFirms(RuleSetSourceKind kind) => kind != RuleSetSourceKind.InternalProcedure;

    /// <summary>
    /// Stores <paramref name="file"/> in the Procedure Library (through <see cref="SpeFileStore"/>, internal only, D-63), links it as an
    /// unlinked document, and creates the Draft rule set. Writes nothing when the input is invalid.
    /// </summary>
    public virtual async Task<RuleSetOutcome<RuleSetCreated>> CreateAsync(RuleSetCreateRequest request, byte[] file, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(file);

        var driveId = _finance.CurrentValue.ProcedureLibraryContainerId;
        if (string.IsNullOrWhiteSpace(driveId))
        {
            return RuleSetOutcome<RuleSetCreated>.Refused(
                StatusCodes.Status503ServiceUnavailable, CodeContainerNotConfigured,
                "The Procedure Library container is not configured in this environment, so a rule set cannot be created.");
        }

        var errors = new List<RuleSetError>();
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > 200)
        {
            errors.Add(new RuleSetError(null, null, "name", "name is required and may be at most 200 characters.", null));
        }

        if (request.Scope == RuleSetScope.Firm && (request.FirmId is null || request.FirmId == Guid.Empty))
        {
            errors.Add(new RuleSetError(null, null, "firmId", "A rule set for a firm needs firmId.", null));
        }
        else if (request.Scope == RuleSetScope.ClientDefault && request.FirmId is not null)
        {
            errors.Add(new RuleSetError(null, null, "firmId", "A client default rule set takes no firmId.", null));
        }
        else if (request.FirmId is { } firm && !await ExistsAsync(FirmEntity, "sprk_organizationid", firm, ct))
        {
            errors.Add(new RuleSetError(null, null, "firmId", "The firm was not found.", null));
        }

        if (errors.Count > 0)
        {
            return RuleSetOutcome<RuleSetCreated>.Invalid(CodeInvalid, errors);
        }

        var owner = await _ownership.ResolveOwnerAsync(OwnerContext(request.CallerObjectId), ct);
        if (!owner.IsOwned)
        {
            return RuleSetOutcome<RuleSetCreated>.OwnerRefused(owner);
        }

        var created = new List<(string Entity, Guid Id)>();
        string? uploadedItemId = null;
        try
        {
            var uploadPath = SpeUploadPath.SanitizeFileName(SourceFileName(request.FileName, name));
            var uploaded = await _spe.UploadSmallAsync(driveId, uploadPath, new MemoryStream(file, writable: false), ConflictBehavior.Rename, ct)
                ?? throw new InvalidOperationException("The Procedure Library upload returned no file.");
            uploadedItemId = uploaded.Id;

            var documentId = Guid.Parse(await _documents.CreateDocumentAsync(
                new CreateDocumentRequest
                {
                    Name = uploaded.Name,
                    ContainerId = driveId,
                    OwningTeamId = owner.OwningTeamId,
                    CreatedByPersonId = owner.CreatedByPerson,
                },
                ct));
            created.Add((DocumentEntity, documentId));

            await _documents.UpdateDocumentAsync(
                documentId.ToString(),
                new UpdateDocumentRequest
                {
                    GraphDriveId = driveId,
                    GraphItemId = uploaded.Id,
                    FileName = uploaded.Name,
                    FileSize = uploaded.Size ?? file.LongLength,
                    MimeType = MimeTypeOf(uploaded.Name),
                    HasFile = true,
                    FilePath = uploaded.WebUrl,
                },
                ct);

            var visible = InitialVisibleToFirms(request.SourceKind);
            var row = new Entity(RuleSetEntity)
            {
                ["sprk_name"] = name,
                [RuleDomains.PolicyDomainColumn] = new OptionSetValue(RuleDomains.PolicyDomainBilling),
                ["sprk_scope"] = new OptionSetValue((int)request.Scope),
                ["sprk_sourcedocument"] = new EntityReference(DocumentEntity, documentId),
                ["sprk_visibletofirms"] = visible,
                ["statecode"] = new OptionSetValue(0),
                ["statuscode"] = new OptionSetValue(RuleSetDraft),
            };
            if (request.FirmId is { } firmId)
            {
                row["sprk_firm"] = new EntityReference(FirmEntity, firmId);
            }

            owner.ApplyTo(row);
            var ruleSetId = await _entities.CreateAsync(row, ct);
            created.Add((RuleSetEntity, ruleSetId));

            _logger.LogInformation("Rule set {RuleSetId} created as Draft ({SourceKind}, visibleToFirms={Visible})", ruleSetId, request.SourceKind, visible);
            return RuleSetOutcome<RuleSetCreated>.Done(new RuleSetCreated(
                ruleSetId, documentId, "Draft", request.Scope.ToString(), request.FirmId, request.SourceKind.ToString(), visible));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await CompensateAsync(created, driveId, uploadedItemId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rule set create failed after {Written} writes; removing what it created", created.Count);
            await CompensateAsync(created, driveId, uploadedItemId);
            return RuleSetOutcome<RuleSetCreated>.Refused(
                StatusCodes.Status500InternalServerError, CodeWriteFailed,
                "The rule set could not be created and nothing was saved. Try again; if it keeps failing, contact support.");
        }
    }

    private async Task CompensateAsync(List<(string Entity, Guid Id)> created, string driveId, string? itemId)
    {
        for (var i = created.Count - 1; i >= 0; i--)
        {
            try
            {
                await _entities.DeleteAsync(created[i].Entity, created[i].Id, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rule set cleanup could not delete {Entity} {Id}; it is a Draft row and in force nowhere", created[i].Entity, created[i].Id);
            }
        }

        if (itemId is not null)
        {
            try
            {
                await _spe.DeleteFileAsync(driveId, itemId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rule set cleanup could not delete the uploaded source document {ItemId}", itemId);
            }
        }
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Reads
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The billing rule sets, newest first, each with its rule count.</summary>
    public virtual async Task<RuleSetOutcome<IReadOnlyList<RuleSetSummary>>> ListAsync(CancellationToken ct)
    {
        var query = new QueryExpression(RuleSetEntity) { ColumnSet = new ColumnSet(RuleSetColumns) };
        RuleDomains.AddBillingFilter(query);
        query.Orders.Add(new OrderExpression("createdon", OrderType.Descending));
        var sets = await _entities.RetrieveMultipleAsync(query, ct);
        if (sets.MoreRecords)
        {
            return RuleSetOutcome<IReadOnlyList<RuleSetSummary>>.Refused(
                StatusCodes.Status422UnprocessableEntity, CodeResultTruncated, "There are more rule sets than can be listed in one pass.");
        }

        var rows = sets.Entities.Where(RuleDomains.IsBillingRow).ToList();
        var counts = new Dictionary<Guid, int>();
        foreach (var batch in rows.Select(r => (object)r.Id).Chunk(MaxInValues))
        {
            var policies = await _entities.RetrieveMultipleAsync(PolicyQuery("sprk_ruleset", ConditionOperator.In, batch), ct);
            if (policies.MoreRecords)
            {
                return RuleSetOutcome<IReadOnlyList<RuleSetSummary>>.Refused(
                    StatusCodes.Status422UnprocessableEntity, CodeResultTruncated, "There are more rules than can be counted in one pass.");
            }

            foreach (var policy in policies.Entities.Where(RuleDomains.IsBillingRow))
            {
                if (policy.GetAttributeValue<EntityReference>("sprk_ruleset")?.Id is { } setId)
                {
                    counts[setId] = counts.GetValueOrDefault(setId) + 1;
                }
            }
        }

        return RuleSetOutcome<IReadOnlyList<RuleSetSummary>>.Done(
            rows.Select(r => Summarize(r, counts.GetValueOrDefault(r.Id))).ToList());
    }

    /// <summary>One rule set with every rule as its working version shows it, beside its clause quote.</summary>
    public virtual async Task<RuleSetOutcome<RuleSetDetail>> GetAsync(Guid ruleSetId, CancellationToken ct)
    {
        var set = await LoadRuleSetAsync(ruleSetId, ct);
        if (set is null)
        {
            return RuleSetOutcome<RuleSetDetail>.Refused(StatusCodes.Status404NotFound, CodeNotFound, "The rule set was not found.");
        }

        var loaded = await LoadRulesAsync(ruleSetId, ct);
        if (loaded.Truncated)
        {
            return RuleSetOutcome<RuleSetDetail>.Refused(
                StatusCodes.Status422UnprocessableEntity, CodeResultTruncated, "The rule set has more rules than can be read in one pass.");
        }

        var views = new List<ProposalView>();
        foreach (var rule in loaded.Rules.OrderBy(r => r.Policy.Code, StringComparer.Ordinal))
        {
            views.Add(await ViewAsync(rule, ct));
        }

        JsonNode? notConverted = null;
        if (set.GetAttributeValue<string>("sprk_notconverted") is { Length: > 0 } text)
        {
            try
            {
                notConverted = JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                notConverted = JsonValue.Create(text);
            }
        }

        return RuleSetOutcome<RuleSetDetail>.Done(new RuleSetDetail(
            Summarize(set, loaded.Rules.Count), set.GetAttributeValue<EntityReference>("sprk_sourcedocument")?.Id, notConverted, views));
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Edit / accept / reject
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Edits a rule's working version. A draft (never in force) is edited in place; a version that is, or has been, in force is left
    /// untouched and the edit becomes a NEW Proposed version. The result is validated first: an invalid result writes nothing.
    /// </summary>
    public virtual async Task<RuleSetOutcome<ProposalEdited>> EditAsync(Guid ruleSetId, Guid policyId, ProposalEdit edit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(edit);

        var found = await FindRuleAsync<ProposalEdited>(ruleSetId, policyId, ct);
        if (found.Refusal is not null)
        {
            return found.Refusal;
        }

        var rule = found.Rule!;
        var work = rule.Working;
        var errors = new List<RuleSetError>();

        if (edit is { RuleType: null, RuleBody: null, MessageTemplate: null, ShortHeadline: null, Consequence: null, ClauseReference: null, ClauseLocation: null })
        {
            errors.Add(new RuleSetError(policyId, rule.Policy.Name, "edit", "Nothing to change: name at least one member.", null));
        }

        int? consequence = work.Consequence;
        if (edit.Consequence is not null)
        {
            if (TryParseConsequence(edit.Consequence, out var parsed))
            {
                consequence = (int)parsed;
            }
            else
            {
                errors.Add(new RuleSetError(policyId, rule.Policy.Name, "consequence", "consequence must be Reject, Flag or SuggestAdjustment.", null));
            }
        }

        var ruleTypeRaw = edit.RuleType ?? work.RuleType?.ToString(CultureInfo.InvariantCulture);
        var merged = work with
        {
            RuleBody = edit.RuleBody ?? work.RuleBody,
            Template = edit.MessageTemplate ?? work.Template,
            Headline = edit.ShortHeadline ?? work.Headline,
            Consequence = consequence,
            ClauseReference = edit.ClauseReference ?? work.ClauseReference,
            ClauseLocation = edit.ClauseLocation ?? work.ClauseLocation,
        };
        errors.AddRange(ValidateRule(rule.Policy, ruleTypeRaw, merged));
        if (errors.Count > 0)
        {
            return RuleSetOutcome<ProposalEdited>.Invalid(CodeInvalid, errors);
        }

        // Valid: the type parses and is a billing type (ValidateForSave with the Billing domain), so this cannot fail.
        RuleBodySchemaValidator.TryParseRuleType(ruleTypeRaw, out var ruleType);
        merged = merged with { RuleType = (int)ruleType };

        try
        {
            if (work.IsDraft(rule.Policy))
            {
                var fields = new Dictionary<string, object>
                {
                    ["sprk_ruletype"] = new OptionSetValue(merged.RuleType!.Value),
                    ["sprk_rulebody"] = merged.RuleBody!,
                    ["sprk_messagetemplate"] = merged.Template!,
                    ["sprk_shortheadline"] = merged.Headline!,
                    ["sprk_consequence"] = new OptionSetValue(merged.Consequence!.Value),
                    ["sprk_proposedaction"] = ProposedActionText(merged.Consequence),
                };
                if (merged.ClauseReference is not null)
                {
                    fields["sprk_clausereference"] = merged.ClauseReference;
                }

                if (merged.ClauseLocation is not null)
                {
                    fields["sprk_clauselocation"] = merged.ClauseLocation;
                }

                await _entities.UpdateAsync(VersionEntity, work.Id, fields, ct);
                rule = rule with { Working = merged };
                return RuleSetOutcome<ProposalEdited>.Done(new ProposalEdited(await ViewAsync(rule, ct), CreatedNewVersion: false));
            }

            // The working version is, or has been, in force: it is never updated. The edit is a new version.
            var owner = await _ownership.ResolveOwnerAsync(OwnerContext(edit.CallerObjectId), ct);
            if (!owner.IsOwned)
            {
                return RuleSetOutcome<ProposalEdited>.OwnerRefused(owner);
            }

            var number = rule.Versions.Max(v => v.Number) + 1;
            var created = new Entity(VersionEntity)
            {
                ["sprk_name"] = $"{rule.Policy.Code} v{number.ToString(CultureInfo.InvariantCulture)}",
                ["sprk_policy"] = new EntityReference(PolicyEntity, policyId),
                ["sprk_versionnumber"] = number,
                ["sprk_ruletype"] = new OptionSetValue(merged.RuleType!.Value),
                ["sprk_rulebody"] = merged.RuleBody!,
                ["sprk_messagetemplate"] = merged.Template!,
                ["sprk_shortheadline"] = merged.Headline!,
                ["sprk_consequence"] = new OptionSetValue(merged.Consequence!.Value),
                ["sprk_proposedaction"] = ProposedActionText(merged.Consequence),
                ["sprk_proposalstatus"] = new OptionSetValue((int)ProposalState.Proposed),
                // Required column; provisional until publish sets the effective date. No reader treats a non-current version as in force.
                ["sprk_inforcefrom"] = DateTime.UtcNow,
            };
            if (merged.ClauseReference is not null)
            {
                created["sprk_clausereference"] = merged.ClauseReference;
            }

            if (merged.ClauseLocation is not null)
            {
                created["sprk_clauselocation"] = merged.ClauseLocation;
            }

            owner.ApplyTo(created);
            var newId = await _entities.CreateAsync(created, ct);
            var newWorking = merged with
            {
                Id = newId, Number = number, State = (int)ProposalState.Proposed, InForceFrom = null, InForceTo = null,
            };
            var versions = rule.Versions.Append(newWorking).ToList();
            _logger.LogInformation("Rule {PolicyId} of rule set {RuleSetId}: new draft version {Number} {VersionId} (predecessor {Predecessor} untouched)",
                policyId, ruleSetId, number, newId, work.Id);
            return RuleSetOutcome<ProposalEdited>.Done(new ProposalEdited(
                await ViewAsync(rule with { Working = newWorking, Versions = versions }, ct), CreatedNewVersion: true));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rule {PolicyId} edit failed; the working version {VersionId} is unchanged or a never-in-force draft", policyId, work.Id);
            return RuleSetOutcome<ProposalEdited>.Refused(
                StatusCodes.Status500InternalServerError, CodeWriteFailed, "The rule could not be saved. Try again; if it keeps failing, contact support.");
        }
    }

    /// <summary>Accept (validated) or reject a rule's working version. A version in force or ever in force cannot be decided (409).</summary>
    public virtual async Task<RuleSetOutcome<ProposalView>> DecideAsync(Guid ruleSetId, Guid policyId, bool accept, CancellationToken ct)
    {
        var found = await FindRuleAsync<ProposalView>(ruleSetId, policyId, ct);
        if (found.Refusal is not null)
        {
            return found.Refusal;
        }

        var rule = found.Rule!;
        var work = rule.Working;
        if (!work.IsDraft(rule.Policy))
        {
            return RuleSetOutcome<ProposalView>.Refused(
                StatusCodes.Status409Conflict, CodeInForce,
                "This version is, or has been, in force and cannot be accepted or rejected. Edit the rule to draft a new version.");
        }

        if (accept)
        {
            var errors = ValidateRule(rule.Policy, work.RuleType?.ToString(CultureInfo.InvariantCulture), work);
            if (errors.Count > 0)
            {
                return RuleSetOutcome<ProposalView>.Invalid(CodeInvalid, errors);
            }
        }

        var state = accept ? ProposalState.Accepted : ProposalState.Rejected;
        try
        {
            await _entities.UpdateAsync(VersionEntity, work.Id, new Dictionary<string, object>
            {
                ["sprk_proposalstatus"] = new OptionSetValue((int)state),
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rule {PolicyId} could not be set {State}", policyId, state);
            return RuleSetOutcome<ProposalView>.Refused(
                StatusCodes.Status500InternalServerError, CodeWriteFailed, "The decision could not be saved. Try again; if it keeps failing, contact support.");
        }

        return RuleSetOutcome<ProposalView>.Done(await ViewAsync(rule with { Working = work with { State = (int)state } }, ct));
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Publish
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Puts every Accepted version that is not yet in force into force on <paramref name="effectiveDate"/>, all or nothing. Proposed and
    /// Rejected versions are left as they are and counted in the result.
    /// </summary>
    public virtual async Task<RuleSetOutcome<RuleSetPublished>> PublishAsync(Guid ruleSetId, DateOnly effectiveDate, CancellationToken ct)
    {
        var set = await LoadRuleSetAsync(ruleSetId, ct);
        if (set is null)
        {
            return RuleSetOutcome<RuleSetPublished>.Refused(StatusCodes.Status404NotFound, CodeNotFound, "The rule set was not found.");
        }

        var status = set.GetAttributeValue<OptionSetValue>("statuscode")?.Value;
        if (status == RuleSetRetired)
        {
            return RuleSetOutcome<RuleSetPublished>.Refused(StatusCodes.Status409Conflict, CodeRetired, "A retired rule set cannot be published.");
        }

        var setErrors = new List<RuleSetError>();
        var scope = set.GetAttributeValue<OptionSetValue>("sprk_scope")?.Value;
        var hasFirm = set.GetAttributeValue<EntityReference>("sprk_firm") is not null;
        if (scope == (int)RuleSetScope.Firm && !hasFirm)
        {
            setErrors.Add(new RuleSetError(null, null, "scope", "A rule set assigned to a firm needs its firm before it is published.", null));
        }
        else if (scope != (int)RuleSetScope.Firm && scope != (int)RuleSetScope.ClientDefault)
        {
            setErrors.Add(new RuleSetError(null, null, "scope", "The rule set has no scope: assign it to the client default or a firm before it is published.", null));
        }
        else if (scope == (int)RuleSetScope.ClientDefault && hasFirm)
        {
            setErrors.Add(new RuleSetError(null, null, "scope", "A client default rule set takes no firm.", null));
        }

        var loaded = await LoadRulesAsync(ruleSetId, ct);
        if (loaded.Truncated)
        {
            return RuleSetOutcome<RuleSetPublished>.Refused(
                StatusCodes.Status422UnprocessableEntity, CodeResultTruncated, "The rule set has more rules than can be published in one pass.");
        }

        var candidates = new List<RuleSetRule>();
        int proposed = 0, rejected = 0;
        foreach (var rule in loaded.Rules)
        {
            var work = rule.Working;
            if (!work.IsDraft(rule.Policy))
            {
                continue; // already in force (or its end is stamped): nothing to publish
            }

            switch (work.StateOrProposed)
            {
                case ProposalState.Accepted:
                    candidates.Add(rule);
                    break;
                case ProposalState.Rejected:
                    rejected++;
                    break;
                default:
                    proposed++;
                    break;
            }
        }

        if (candidates.Count == 0 && setErrors.Count == 0)
        {
            return RuleSetOutcome<RuleSetPublished>.Refused(
                StatusCodes.Status400BadRequest, CodeNothingToPublish, "No accepted rule is waiting to be published. Accept at least one rule first.");
        }

        // Every candidate is validated BEFORE the first write; one failure refuses the whole publish.
        var errors = new List<RuleSetError>(setErrors);
        var effectiveStart = ToUtcDate(effectiveDate);
        foreach (var rule in candidates)
        {
            var work = rule.Working;
            errors.AddRange(ValidateRule(rule.Policy, work.RuleType?.ToString(CultureInfo.InvariantCulture), work));
            if (rule.Predecessor is { } predecessor && predecessor.InForceFrom is { } from && effectiveStart < from.Date)
            {
                errors.Add(new RuleSetError(rule.Policy.Id, rule.Policy.Name, "effectiveDate",
                    $"The effective date is before the date the version it replaces came into force ({DateOnly.FromDateTime(from):yyyy-MM-dd}).", null));
            }
        }

        if (errors.Count > 0)
        {
            return RuleSetOutcome<RuleSetPublished>.Invalid(CodePublishInvalid, errors);
        }

        try
        {
            // (1) the versions: the new versions' in-force-from, the predecessors' in-force-to (one table, one transaction per chunk).
            var versionUpdates = new List<(Guid id, Dictionary<string, object> fields)>();
            foreach (var rule in candidates)
            {
                versionUpdates.Add((rule.Working.Id, new Dictionary<string, object> { ["sprk_inforcefrom"] = effectiveStart }));
                if (rule.Predecessor is { InForceTo: null } predecessor)
                {
                    versionUpdates.Add((predecessor.Id, new Dictionary<string, object> { ["sprk_inforceto"] = effectiveStart }));
                }
            }

            foreach (var chunk in versionUpdates.Chunk(MaxPerTransaction))
            {
                await _entities.BulkUpdateAsync(VersionEntity, chunk.ToList(), ct);
            }

            // (2) the policies: the pointer move is what puts a version in force.
            var policyUpdates = candidates.Select(rule => (rule.Policy.Id, new Dictionary<string, object>
            {
                ["sprk_currentversion"] = new EntityReference(VersionEntity, rule.Working.Id),
                ["sprk_enabled"] = true,
            })).ToList();
            foreach (var chunk in policyUpdates.Chunk(MaxPerTransaction))
            {
                await _entities.BulkUpdateAsync(PolicyEntity, chunk.ToList(), ct);
            }

            // (3) the rule set. Last, so a failure before it leaves a state a second publish finishes.
            var setFields = new Dictionary<string, object>
            {
                ["statecode"] = new OptionSetValue(0),
                ["statuscode"] = new OptionSetValue(RuleSetPublished),
            };
            if (set.GetAttributeValue<DateTime?>("sprk_effectivefrom") is null || status != RuleSetPublished)
            {
                setFields["sprk_effectivefrom"] = effectiveStart;
            }

            await _entities.UpdateAsync(RuleSetEntity, ruleSetId, setFields, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rule set {RuleSetId} publish failed part-way; publishing again completes it (the set is not Published until the last write)", ruleSetId);
            return RuleSetOutcome<RuleSetPublished>.Refused(
                StatusCodes.Status500InternalServerError, CodeWriteFailed,
                "The rule set could not be published. Publish again; if it keeps failing, contact support.");
        }

        var visible = set.GetAttributeValue<bool>("sprk_visibletofirms");
        _logger.LogInformation("Rule set {RuleSetId} published: {Count} rule version(s) in force from {Effective}", ruleSetId, candidates.Count, effectiveDate);
        return RuleSetOutcome<RuleSetPublished>.Done(new RuleSetPublished(
            ruleSetId, "Published", effectiveDate,
            candidates.Select(c => new PublishedRuleVersion(c.Policy.Id, c.Working.Id, c.Working.Number, c.Predecessor?.Id)).ToList(),
            proposed, rejected, visible, visible ? VisibleNotice : HiddenNotice));
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Assignment and visibility
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Assigns the rule set to the client default or to one firm.</summary>
    public virtual async Task<RuleSetOutcome<RuleSetSummary>> AssignAsync(Guid ruleSetId, RuleSetScope scope, Guid? firmId, CancellationToken ct)
    {
        var set = await LoadRuleSetAsync(ruleSetId, ct);
        if (set is null)
        {
            return RuleSetOutcome<RuleSetSummary>.Refused(StatusCodes.Status404NotFound, CodeNotFound, "The rule set was not found.");
        }

        if (set.GetAttributeValue<OptionSetValue>("statuscode")?.Value == RuleSetRetired)
        {
            return RuleSetOutcome<RuleSetSummary>.Refused(StatusCodes.Status409Conflict, CodeRetired, "A retired rule set cannot be changed.");
        }

        var errors = new List<RuleSetError>();
        if (scope == RuleSetScope.Firm && (firmId is null || firmId == Guid.Empty))
        {
            errors.Add(new RuleSetError(null, null, "firmId", "Assigning to a firm needs firmId.", null));
        }
        else if (scope == RuleSetScope.ClientDefault && firmId is not null)
        {
            errors.Add(new RuleSetError(null, null, "firmId", "The client default takes no firmId.", null));
        }
        else if (firmId is { } firm && !await ExistsAsync(FirmEntity, "sprk_organizationid", firm, ct))
        {
            errors.Add(new RuleSetError(null, null, "firmId", "The firm was not found.", null));
        }

        if (errors.Count > 0)
        {
            return RuleSetOutcome<RuleSetSummary>.Invalid(CodeInvalid, errors);
        }

        var fields = new Dictionary<string, object>
        {
            ["sprk_scope"] = new OptionSetValue((int)scope),
            // DBNull clears the lookup (the generic seam's convention), so moving a firm set to the client default drops the firm.
            ["sprk_firm"] = firmId is { } id ? new EntityReference(FirmEntity, id) : DBNull.Value,
        };
        return await UpdateSetAsync(ruleSetId, set, fields, ct);
    }

    /// <summary>Turns "visible to firms" on or off. Takes effect for every firm-facing projection at once (it is read when the finding is shown).</summary>
    public virtual async Task<RuleSetOutcome<RuleSetSummary>> SetVisibilityAsync(Guid ruleSetId, bool visibleToFirms, CancellationToken ct)
    {
        var set = await LoadRuleSetAsync(ruleSetId, ct);
        if (set is null)
        {
            return RuleSetOutcome<RuleSetSummary>.Refused(StatusCodes.Status404NotFound, CodeNotFound, "The rule set was not found.");
        }

        if (set.GetAttributeValue<OptionSetValue>("statuscode")?.Value == RuleSetRetired)
        {
            return RuleSetOutcome<RuleSetSummary>.Refused(StatusCodes.Status409Conflict, CodeRetired, "A retired rule set cannot be changed.");
        }

        return await UpdateSetAsync(ruleSetId, set, new Dictionary<string, object> { ["sprk_visibletofirms"] = visibleToFirms }, ct);
    }

    private async Task<RuleSetOutcome<RuleSetSummary>> UpdateSetAsync(Guid ruleSetId, Entity set, Dictionary<string, object> fields, CancellationToken ct)
    {
        try
        {
            await _entities.UpdateAsync(RuleSetEntity, ruleSetId, fields, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rule set {RuleSetId} could not be updated", ruleSetId);
            return RuleSetOutcome<RuleSetSummary>.Refused(
                StatusCodes.Status500InternalServerError, CodeWriteFailed, "The rule set could not be saved. Try again; if it keeps failing, contact support.");
        }

        foreach (var (key, value) in fields)
        {
            set[key] = value is DBNull ? null : value;
        }

        var loaded = await LoadRulesAsync(ruleSetId, ct);
        return RuleSetOutcome<RuleSetSummary>.Done(Summarize(set, loaded.Rules.Count));
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Firm-facing clause text (130 / 134 / 135)
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// What a firm may be shown of one rule version's clause: resolves the version's billing policy and rule set and applies
    /// <see cref="RuleSetClauseVisibility.ForFirm"/>. Fails closed: no text when the version, its billing policy or its rule set cannot
    /// be read, when the rule set is not Published, or when the version was never in force (a draft's clause is never shown).
    /// </summary>
    public virtual async Task<FirmClauseText> GetFirmClauseAsync(Guid policyVersionId, CancellationToken ct)
    {
        var versionQuery = new QueryExpression(VersionEntity)
        {
            ColumnSet = new ColumnSet("sprk_policy", "sprk_clausereference", "sprk_clauselocation", "sprk_inforceto"),
            Criteria = { Conditions = { new ConditionExpression("sprk_policyversionid", ConditionOperator.Equal, policyVersionId) } },
            TopCount = 1,
        };
        var version = (await _entities.RetrieveMultipleAsync(versionQuery, ct)).Entities.FirstOrDefault();
        if (version?.GetAttributeValue<EntityReference>("sprk_policy")?.Id is not { } policyId)
        {
            return FirmClauseText.None;
        }

        var policyQuery = PolicyQuery("sprk_policyid", ConditionOperator.Equal, new object[] { policyId });
        var policy = (await _entities.RetrieveMultipleAsync(policyQuery, ct)).Entities.FirstOrDefault(RuleDomains.IsBillingRow);
        if (policy?.GetAttributeValue<EntityReference>("sprk_ruleset")?.Id is not { } ruleSetId)
        {
            return FirmClauseText.None;
        }

        var wasInForce = policy.GetAttributeValue<EntityReference>("sprk_currentversion")?.Id == policyVersionId
            || version.GetAttributeValue<DateTime?>("sprk_inforceto") is not null;
        if (!wasInForce)
        {
            return FirmClauseText.None;
        }

        var set = await LoadRuleSetAsync(ruleSetId, ct);
        if (set is null || set.GetAttributeValue<OptionSetValue>("statuscode")?.Value != RuleSetPublished)
        {
            return FirmClauseText.None;
        }

        return RuleSetClauseVisibility.ForFirm(
            set.GetAttributeValue<bool>("sprk_visibletofirms"),
            version.GetAttributeValue<string>("sprk_clausereference"),
            version.GetAttributeValue<string>("sprk_clauselocation"));
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Reading the rules of one rule set
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    internal sealed record PolicyRow(Guid Id, string Code, string Name, Guid? CurrentVersionId);

    internal sealed record VersionRow(
        Guid Id, int Number, int? RuleType, string? RuleBody, string? Template, string? Headline, int? Consequence, int? State,
        string? ClauseReference, string? ClauseLocation, DateTime? InForceFrom, DateTime? InForceTo)
    {
        public ProposalState StateOrProposed =>
            State is { } s && Enum.IsDefined(typeof(ProposalState), s) ? (ProposalState)s : ProposalState.Proposed;

        /// <summary>A draft: not current and never ended. Only a draft is ever updated in place.</summary>
        public bool IsDraft(PolicyRow policy) => policy.CurrentVersionId != Id && InForceTo is null;
    }

    /// <summary>One rule: its policy, every version, the working (highest-numbered) version, and the version it would replace.</summary>
    internal sealed record RuleSetRule(PolicyRow Policy, IReadOnlyList<VersionRow> Versions, VersionRow Working)
    {
        /// <summary>The policy's current version when it is not the working one: what a publish of the working version stamps ended.</summary>
        public VersionRow? Predecessor =>
            Policy.CurrentVersionId is { } current && current != Working.Id ? Versions.FirstOrDefault(v => v.Id == current) : null;
    }

    private async Task<Entity?> LoadRuleSetAsync(Guid ruleSetId, CancellationToken ct)
    {
        if (ruleSetId == Guid.Empty)
        {
            return null;
        }

        var query = new QueryExpression(RuleSetEntity)
        {
            ColumnSet = new ColumnSet(RuleSetColumns),
            Criteria = { Conditions = { new ConditionExpression("sprk_rulesetid", ConditionOperator.Equal, ruleSetId) } },
            TopCount = 1,
        };
        RuleDomains.AddBillingFilter(query);
        var rows = await _entities.RetrieveMultipleAsync(query, ct);
        return rows.Entities.FirstOrDefault(r => r.Id == ruleSetId && RuleDomains.IsBillingRow(r));
    }

    private static QueryExpression PolicyQuery(string column, ConditionOperator op, object[] values)
    {
        var query = new QueryExpression(PolicyEntity) { ColumnSet = new ColumnSet(PolicyColumns) };
        query.Criteria.AddCondition(column, op, values);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        RuleDomains.AddBillingFilter(query);
        return query;
    }

    private async Task<(List<RuleSetRule> Rules, bool Truncated)> LoadRulesAsync(Guid ruleSetId, CancellationToken ct)
    {
        var policies = await _entities.RetrieveMultipleAsync(PolicyQuery("sprk_ruleset", ConditionOperator.Equal, new object[] { ruleSetId }), ct);
        if (policies.MoreRecords)
        {
            return (new List<RuleSetRule>(), true);
        }

        var policyRows = policies.Entities
            .Where(p => RuleDomains.IsBillingRow(p) && p.GetAttributeValue<EntityReference>("sprk_ruleset")?.Id == ruleSetId)
            .Select(p => new PolicyRow(
                p.Id,
                p.GetAttributeValue<string>("sprk_policycode") ?? p.Id.ToString(),
                p.GetAttributeValue<string>("sprk_name") ?? p.GetAttributeValue<string>("sprk_policycode") ?? p.Id.ToString(),
                p.GetAttributeValue<EntityReference>("sprk_currentversion")?.Id))
            .ToList();

        var versionsByPolicy = new Dictionary<Guid, List<VersionRow>>();
        foreach (var batch in policyRows.Select(p => (object)p.Id).Chunk(MaxInValues))
        {
            var query = new QueryExpression(VersionEntity) { ColumnSet = new ColumnSet(VersionColumns) };
            query.Criteria.AddCondition("sprk_policy", ConditionOperator.In, batch);
            var versions = await _entities.RetrieveMultipleAsync(query, ct);
            if (versions.MoreRecords)
            {
                return (new List<RuleSetRule>(), true);
            }

            foreach (var v in versions.Entities)
            {
                if (v.GetAttributeValue<EntityReference>("sprk_policy")?.Id is { } pid)
                {
                    if (!versionsByPolicy.TryGetValue(pid, out var list))
                    {
                        versionsByPolicy[pid] = list = new List<VersionRow>();
                    }

                    list.Add(ToVersion(v));
                }
            }
        }

        var rules = new List<RuleSetRule>();
        foreach (var policy in policyRows)
        {
            if (versionsByPolicy.TryGetValue(policy.Id, out var versions) && versions.Count > 0)
            {
                rules.Add(new RuleSetRule(policy, versions, versions.MaxBy(v => v.Number)!));
            }
        }

        return (rules, false);
    }

    private async Task<(RuleSetRule? Rule, RuleSetOutcome<T>? Refusal)> FindRuleAsync<T>(Guid ruleSetId, Guid policyId, CancellationToken ct)
        where T : class
    {
        var set = await LoadRuleSetAsync(ruleSetId, ct);
        if (set is null)
        {
            return (null, RuleSetOutcome<T>.Refused(StatusCodes.Status404NotFound, CodeNotFound, "The rule set was not found."));
        }

        if (set.GetAttributeValue<OptionSetValue>("statuscode")?.Value == RuleSetRetired)
        {
            return (null, RuleSetOutcome<T>.Refused(StatusCodes.Status409Conflict, CodeRetired, "A retired rule set cannot be changed."));
        }

        var loaded = await LoadRulesAsync(ruleSetId, ct);
        if (loaded.Truncated)
        {
            return (null, RuleSetOutcome<T>.Refused(
                StatusCodes.Status422UnprocessableEntity, CodeResultTruncated, "The rule set has more rules than can be read in one pass."));
        }

        var rule = loaded.Rules.FirstOrDefault(r => r.Policy.Id == policyId);
        return rule is null
            ? (null, RuleSetOutcome<T>.Refused(StatusCodes.Status404NotFound, CodeProposalNotFound, "The rule was not found in this rule set."))
            : (rule, null);
    }

    private static VersionRow ToVersion(Entity v) => new(
        v.Id,
        v.GetAttributeValue<int?>("sprk_versionnumber") ?? 0,
        v.GetAttributeValue<OptionSetValue>("sprk_ruletype")?.Value,
        v.GetAttributeValue<string>("sprk_rulebody"),
        v.GetAttributeValue<string>("sprk_messagetemplate"),
        v.GetAttributeValue<string>("sprk_shortheadline"),
        v.GetAttributeValue<OptionSetValue>("sprk_consequence")?.Value,
        v.GetAttributeValue<OptionSetValue>("sprk_proposalstatus")?.Value,
        v.GetAttributeValue<string>("sprk_clausereference"),
        v.GetAttributeValue<string>("sprk_clauselocation"),
        v.GetAttributeValue<DateTime?>("sprk_inforcefrom"),
        v.GetAttributeValue<DateTime?>("sprk_inforceto"));

    private static RuleSetSummary Summarize(Entity set, int ruleCount)
    {
        var status = set.GetAttributeValue<OptionSetValue>("statuscode")?.Value switch
        {
            RuleSetPublished => "Published",
            RuleSetRetired => "Retired",
            _ => "Draft",
        };
        var scope = set.GetAttributeValue<OptionSetValue>("sprk_scope")?.Value;
        return new RuleSetSummary(
            set.Id,
            set.GetAttributeValue<string>("sprk_name") ?? string.Empty,
            status,
            scope == (int)RuleSetScope.Firm ? nameof(RuleSetScope.Firm) : scope == (int)RuleSetScope.ClientDefault ? nameof(RuleSetScope.ClientDefault) : "Unassigned",
            set.GetAttributeValue<EntityReference>("sprk_firm")?.Id,
            set.GetAttributeValue<bool>("sprk_visibletofirms"),
            set.GetAttributeValue<DateTime?>("sprk_effectivefrom") is { } from ? DateOnly.FromDateTime(from) : null,
            set.GetAttributeValue<DateTime?>("sprk_effectiveto") is { } to ? DateOnly.FromDateTime(to) : null,
            ruleCount);
    }

    private async Task<ProposalView> ViewAsync(RuleSetRule rule, CancellationToken ct)
    {
        var work = rule.Working;
        var validation = ValidateRule(rule.Policy, work.RuleType?.ToString(CultureInfo.InvariantCulture), work);

        string? text = null, formal = null, refusal = null;
        var description = await _describer.DescribeAsync(work.RuleBody, ct);
        if (description.Description is { Clauses.Count: > 0 } d)
        {
            text = d.Clauses[0].Text;
            formal = d.Clauses[0].Formal;
        }
        else
        {
            refusal = description.Refusal;
        }

        JsonNode? body = null;
        if (work.RuleBody is { Length: > 0 } json)
        {
            try
            {
                body = JsonNode.Parse(json);
            }
            catch (JsonException)
            {
                body = null;
            }
        }

        var ruleType = work.RuleType is { } rt && Enum.IsDefined(typeof(RuleType), rt) ? ((RuleType)rt).ToString() : null;
        return new ProposalView(
            rule.Policy.Id, rule.Policy.Code, rule.Policy.Name, work.StateOrProposed.ToString(), rule.Policy.CurrentVersionId == work.Id,
            rule.Policy.CurrentVersionId is not null || rule.Versions.Any(v => v.InForceTo is not null),
            work.Id, work.Number, ruleType, body, work.Template, work.Headline,
            work.Consequence is { } c && Enum.IsDefined(typeof(BillingConsequence), c) ? ((BillingConsequence)c).ToString() : null,
            text, formal, refusal, work.ClauseReference, work.ClauseLocation,
            validation.Select(e => $"{e.Field}: {e.Message}").ToList());
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Validation
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The one validation of a billing rule version, for accept, edit and publish: <see cref="PolicyVersionValidator.ValidateForSave"/>
    /// with <see cref="RuleDomain.Billing"/> (never <c>ValidateForAuthoring</c>, which is the Ontology admin's), then the required
    /// columns (consequence, headline, template).
    /// </summary>
    private List<RuleSetError> ValidateRule(PolicyRow policy, string? ruleTypeRaw, VersionRow version)
    {
        var errors = new List<RuleSetError>();
        void Add(string field, string message, string? reason = null) =>
            errors.Add(new RuleSetError(policy.Id, policy.Name, field, message, reason));

        if (string.IsNullOrWhiteSpace(version.RuleBody))
        {
            Add("ruleBody", "The rule body is required.");
        }
        else
        {
            var result = _validator.ValidateForSave(ruleTypeRaw, version.RuleBody, version.Template, RuleDomain.Billing);
            if (!result.IsValid)
            {
                var field = result.Reason switch
                {
                    OntologyWriterFailureReason.RuleTypeUnsupported or OntologyWriterFailureReason.RuleTypeWrongDomain => "ruleType",
                    OntologyWriterFailureReason.TemplateMalformed or OntologyWriterFailureReason.TemplateTokenOutsideReadSet => "messageTemplate",
                    _ => "ruleBody",
                };
                foreach (var message in result.Errors)
                {
                    Add(field, message, result.Reason);
                }
            }
        }

        if (version.Consequence is not { } c || !Enum.IsDefined(typeof(BillingConsequence), c))
        {
            Add("consequence", "The rule needs a consequence: Reject, Flag or SuggestAdjustment.");
        }

        if (string.IsNullOrWhiteSpace(version.Headline))
        {
            Add("shortHeadline", "The rule needs a short headline.");
        }

        if (string.IsNullOrWhiteSpace(version.Template))
        {
            Add("messageTemplate", "The rule needs a message template.");
        }

        return errors;
    }

    internal static bool TryParseConsequence(string? text, out BillingConsequence consequence)
    {
        consequence = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var candidate in Enum.GetValues<BillingConsequence>())
        {
            if (string.Equals(text, candidate.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                consequence = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>The required free-text <c>sprk_proposedaction</c> of a billing version (019): a short code of the consequence. Not what the consequence is.</summary>
    internal static string ProposedActionText(int? consequence) => consequence switch
    {
        (int)BillingConsequence.Reject => "billing-reject",
        (int)BillingConsequence.Flag => "billing-flag",
        (int)BillingConsequence.SuggestAdjustment => "billing-suggest-adjustment",
        _ => "billing-rule",
    };

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    private static RecordOwnershipContext OwnerContext(Guid? callerObjectId) =>
        RecordOwnershipContext.ForParents(Array.Empty<RecordOwnershipParent>(), callerObjectId) with
        {
            RequestedBy = RecordRequester.Of(objectId: callerObjectId),
        };

    private async Task<bool> ExistsAsync(string entity, string idColumn, Guid id, CancellationToken ct)
    {
        var result = await _entities.RetrieveMultipleAsync(new QueryExpression(entity)
        {
            ColumnSet = new ColumnSet(idColumn),
            Criteria = { Conditions = { new ConditionExpression(idColumn, ConditionOperator.Equal, id) } },
            TopCount = 1,
        }, ct);
        return result.Entities.Count > 0;
    }

    private static DateTime ToUtcDate(DateOnly d) => new(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);

    private static string SourceFileName(string uploaded, string ruleSetName)
    {
        var extension = Path.GetExtension(uploaded ?? string.Empty);
        extension = extension.Length is > 0 and <= 8 && extension.All(ch => char.IsLetterOrDigit(ch) || ch == '.') ? extension : string.Empty;
        var safe = new string(ruleSetName.Select(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_' ? ch : '_').ToArray()).Trim();
        if (safe.Length > 80)
        {
            safe = safe[..80].TrimEnd();
        }

        return $"Rule set - {(safe.Length == 0 ? "source" : safe)} - {DateTime.UtcNow:yyyyMMddHHmmss}{extension}";
    }

    private static string MimeTypeOf(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".txt" => "text/plain",
        _ => "application/octet-stream",
    };
}
