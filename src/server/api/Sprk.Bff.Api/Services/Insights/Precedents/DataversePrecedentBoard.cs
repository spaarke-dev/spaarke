using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Insights.Precedents;

/// <summary>
/// Dataverse-backed implementation of <see cref="IPrecedentBoard"/> over the
/// <c>sprk_precedent</c> entity provisioned in task 011 (D-P3 schema).
/// </summary>
/// <remarks>
/// <para>
/// <b>Boundary placement</b>: Zone B per SPEC §3.5 — this service consumes
/// <see cref="IGenericEntityService"/> only (no AI internals, no LLM client,
/// no playbook engine). The §3.5.4 forbidden-imports grep is asserted against
/// this folder before merge.
/// </para>
/// <para>
/// <b>Schema reference (from task 011 commit <c>ae309bfe</c>)</b>:
/// <list type="bullet">
///   <item>Entity: <c>sprk_precedent</c></item>
///   <item>Primary name: <c>sprk_name</c> (string 200)</item>
///   <item><c>sprk_patternstatement</c> (Memo 4000, required)</item>
///   <item><c>sprk_status</c> (Picklist → <c>sprk_precedentstatus</c>; values per <see cref="PrecedentStatus"/>)</item>
///   <item><c>sprk_reviewerby</c> (Lookup → <c>systemuser</c>)</item>
///   <item><c>sprk_reviewdate</c> (DateOnly)</item>
///   <item><c>sprk_clusterdefinition</c> (Memo 2000) — used for the scope tag in Phase 1</item>
///   <item><c>sprk_producedby</c> (string 200) — set to <c>manual-sme-author</c> for D-P3 Phase 1 mode</item>
///   <item>N:N <c>sprk_precedent_matter</c> → <c>sprk_matter</c> (supporting matters)</item>
/// </list>
/// </para>
/// </remarks>
public sealed class DataversePrecedentBoard : IPrecedentBoard
{
    internal const string EntityName = "sprk_precedent";
    internal const string SupportingMatterRelationship = "sprk_precedent_matter";
    internal const string MatterEntityName = "sprk_matter";
    internal const string SystemUserEntityName = "systemuser";

    // Producer tag distinguishes Phase 1 manual SME authoring from the Phase 1.5+
    // nightly cluster job (which would emit "cluster-summarization@v1") so the
    // D-P11 review surface can filter and prioritise human-authored Precedents.
    internal const string ManualProducer = "manual-sme-author";

    // Fields the GetAsync read path returns (kept minimal — the admin endpoint
    // only needs the verification set for the 201 response, and the integration
    // test only asserts status/producedby/reviewerby).
    private static readonly string[] ReadColumns =
    [
        "sprk_precedentid",
        "sprk_name",
        "sprk_patternstatement",
        "sprk_status",
        "sprk_reviewerby",
        "sprk_producedby"
    ];

    private readonly IGenericEntityService _entityService;
    private readonly ILogger<DataversePrecedentBoard> _logger;

    public DataversePrecedentBoard(
        IGenericEntityService entityService,
        ILogger<DataversePrecedentBoard> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Guid> CreateTentativeAsync(CreatePrecedentRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.PatternStatement))
        {
            throw new ArgumentException("PatternStatement is required.", nameof(request));
        }

        // Derive primary name from the pattern statement (first 200 chars, single line).
        // The model-driven view shows sprk_name as the row title; keeping it short
        // means the list view is scannable even when patternstatement is 4000 chars.
        var displayName = DerivePrimaryName(request.PatternStatement);

        var entity = new Entity(EntityName)
        {
            ["sprk_name"] = displayName,
            ["sprk_patternstatement"] = request.PatternStatement,
            ["sprk_status"] = new OptionSetValue(PrecedentStatus.Tentative),
            ["sprk_reviewdate"] = DateTime.UtcNow.Date,
            ["sprk_producedby"] = ManualProducer
        };

        if (request.ReviewerByUserId is { } reviewerId && reviewerId != Guid.Empty)
        {
            entity["sprk_reviewerby"] = new EntityReference(SystemUserEntityName, reviewerId);
        }

        if (!string.IsNullOrWhiteSpace(request.Scope))
        {
            // Phase 1: scope rides as a small JSON tag inside sprk_clusterdefinition.
            // Phase 1.5+ may introduce a dedicated scope entity per SPEC §3.3 deferrals;
            // for now this preserves the value without needing a new column.
            entity["sprk_clusterdefinition"] = $"{{\"scope\":\"{EscapeJsonString(request.Scope)}\"}}";
        }

        var precedentId = await _entityService.CreateAsync(entity, ct);

        _logger.LogInformation(
            "[PRECEDENT] Created tentative Precedent {PrecedentId} (producer={Producer}, supportingMatters={SupportingMatterCount})",
            precedentId, ManualProducer, request.SupportingMatterIds.Count);

        // Attach supporting matters via the N:N relationship. Empty supportingMatterIds
        // is allowed in Phase 1 (Tentative Precedents may be authored before the
        // matter pool exists or while it's being curated).
        var matterRefs = request.SupportingMatterIds
            .Where(id => id != Guid.Empty)
            .Select(id => new EntityReference(MatterEntityName, id))
            .ToList();

        if (matterRefs.Count > 0)
        {
            await _entityService.AssociateAsync(
                EntityName,
                precedentId,
                SupportingMatterRelationship,
                matterRefs,
                ct);
        }

        return precedentId;
    }

    public async Task<PrecedentRecord?> GetAsync(Guid precedentId, CancellationToken ct)
    {
        if (precedentId == Guid.Empty)
        {
            return null;
        }

        try
        {
            var entity = await _entityService.RetrieveAsync(EntityName, precedentId, ReadColumns, ct);

            var statusValue = entity.GetAttributeValue<OptionSetValue>("sprk_status")?.Value
                ?? PrecedentStatus.Tentative;

            var reviewerRef = entity.GetAttributeValue<EntityReference>("sprk_reviewerby");

            return new PrecedentRecord(
                Id: entity.Id,
                Name: entity.GetAttributeValue<string>("sprk_name") ?? string.Empty,
                PatternStatement: entity.GetAttributeValue<string>("sprk_patternstatement") ?? string.Empty,
                StatusValue: statusValue,
                ReviewerByUserId: reviewerRef?.Id,
                ProducedBy: entity.GetAttributeValue<string>("sprk_producedby"));
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
            && (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }
    }

    public async Task ConfirmAsync(Guid precedentId, Guid confirmedByUserId, CancellationToken ct)
    {
        if (precedentId == Guid.Empty)
        {
            throw new ArgumentException("PrecedentId is required.", nameof(precedentId));
        }

        var fields = new Dictionary<string, object>
        {
            ["sprk_status"] = new OptionSetValue(PrecedentStatus.Confirmed),
            ["sprk_reviewdate"] = DateTime.UtcNow.Date
        };

        if (confirmedByUserId != Guid.Empty)
        {
            fields["sprk_reviewerby"] = new EntityReference(SystemUserEntityName, confirmedByUserId);
        }

        await _entityService.UpdateAsync(EntityName, precedentId, fields, ct);

        _logger.LogInformation(
            "[PRECEDENT] Confirmed Precedent {PrecedentId} by user {UserId}",
            precedentId, confirmedByUserId);
    }

    public async Task DeprecateAsync(Guid precedentId, CancellationToken ct)
    {
        if (precedentId == Guid.Empty)
        {
            throw new ArgumentException("PrecedentId is required.", nameof(precedentId));
        }

        var fields = new Dictionary<string, object>
        {
            ["sprk_status"] = new OptionSetValue(PrecedentStatus.Deprecated)
        };

        await _entityService.UpdateAsync(EntityName, precedentId, fields, ct);

        _logger.LogInformation("[PRECEDENT] Deprecated Precedent {PrecedentId}", precedentId);
    }

    /// <summary>
    /// First 200 chars of the pattern statement, with newlines/tabs replaced by spaces
    /// and double spaces collapsed — gives the model-driven list view a scannable title.
    /// </summary>
    internal static string DerivePrimaryName(string patternStatement)
    {
        var cleaned = patternStatement.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        while (cleaned.Contains("  ", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);
        }
        cleaned = cleaned.Trim();
        return cleaned.Length <= 200 ? cleaned : cleaned[..200];
    }

    /// <summary>
    /// Minimal JSON string escaping for the scope tag stored in sprk_clusterdefinition.
    /// </summary>
    private static string EscapeJsonString(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
}
