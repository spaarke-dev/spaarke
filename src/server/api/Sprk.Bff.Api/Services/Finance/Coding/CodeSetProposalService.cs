using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Resolution;

namespace Sprk.Bff.Api.Services.Finance.Coding;

/// <summary>The two reference tables that carry <c>sprk_defaultbillingcodeset</c> (ebilling-r1 task 018, FR-22).</summary>
public enum CodeSetReferenceKind
{
    MatterType,
    PracticeArea,
}

/// <summary>
/// Names, route segments and Dataverse privileges of the two <see cref="CodeSetReferenceKind"/> tables. One place, so the endpoint's
/// authorization declaration and the service's reads and writes cannot disagree about which table a route segment means.
/// </summary>
public static class CodeSetReferenceTables
{
    public const string MatterTypeSegment = "mattertype";
    public const string PracticeAreaSegment = "practicearea";

    /// <summary>
    /// Write privileges by their LIVE names (spaarkedev1 <c>privilege</c>, 2026-10-16). The schema names are mixed case
    /// (<c>sprk_MatterType_Ref</c>, <c>sprk_PracticeArea_Ref</c>); a lower-case spelling would be "not held" for every caller and
    /// deny every request, which is why a test pins them.
    /// </summary>
    public const string WriteMatterTypePrivilege = "prvWritesprk_MatterType_Ref";
    public const string WritePracticeAreaPrivilege = "prvWritesprk_PracticeArea_Ref";

    public static bool TryParse(string? segment, out CodeSetReferenceKind kind)
    {
        switch (segment?.Trim().ToLowerInvariant())
        {
            case MatterTypeSegment:
                kind = CodeSetReferenceKind.MatterType;
                return true;
            case PracticeAreaSegment:
                kind = CodeSetReferenceKind.PracticeArea;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    public static string Segment(CodeSetReferenceKind kind) =>
        kind == CodeSetReferenceKind.MatterType ? MatterTypeSegment : PracticeAreaSegment;

    public static string Entity(CodeSetReferenceKind kind) =>
        kind == CodeSetReferenceKind.MatterType ? CodeSetResolver.MatterTypeEntity : CodeSetResolver.PracticeAreaEntity;

    /// <summary>The entity SET name, for the authorization deny log only.</summary>
    public static string EntitySet(CodeSetReferenceKind kind) =>
        kind == CodeSetReferenceKind.MatterType ? "sprk_mattertype_refs" : "sprk_practicearea_refs";

    public static string WritePrivilege(CodeSetReferenceKind kind) =>
        kind == CodeSetReferenceKind.MatterType ? WriteMatterTypePrivilege : WritePracticeAreaPrivilege;

    internal static string NameColumn(CodeSetReferenceKind kind) =>
        kind == CodeSetReferenceKind.MatterType ? "sprk_mattertypename" : "sprk_practiceareaname";

    internal static string IdColumn(CodeSetReferenceKind kind) => Entity(kind) + "id";
}

/// <summary>One code set that can be proposed, with the similarity of the reference row to it.</summary>
/// <param name="Score">Cosine similarity of the name embeddings, 0 to 1, three decimals.</param>
public sealed record CodeSetCandidate(Guid CodeSetId, string Name, decimal Score);

/// <summary>The AI's proposal for one unmapped reference row. Nothing here has been applied.</summary>
/// <param name="Table"><c>mattertype</c> or <c>practicearea</c> (the confirm route's <c>{table}</c> segment).</param>
/// <param name="Proposed">The best-scoring usable code set, or <c>null</c> when no usable code set exists.</param>
/// <param name="RunnerUp">The second-best, or <c>null</c> when there is only one usable set.</param>
/// <param name="Rationale">One sentence saying what was compared and how close the runner-up was.</param>
public sealed record CodeSetProposal(
    string Table,
    Guid ReferenceId,
    string ReferenceName,
    CodeSetCandidate? Proposed,
    CodeSetCandidate? RunnerUp,
    string Rationale);

/// <summary>What <see cref="CodeSetProposalService.ConfirmAsync"/> did.</summary>
public enum CodeSetConfirmOutcome
{
    /// <summary>The mapping was written on exactly that row.</summary>
    Confirmed,

    /// <summary>No active reference row has that id.</summary>
    ReferenceNotFound,

    /// <summary>No active code set with at least one active Task code has that id.</summary>
    CodeSetNotFound,

    /// <summary>The row gained a mapping since the proposal; nothing was written.</summary>
    AlreadyMapped,
}

/// <summary>
/// AI code-set mapping proposals (ebilling-r1 task 048, FR-22, D-52): for matter types and practice areas with no
/// <c>sprk_defaultbillingcodeset</c>, ranks the usable code sets by embedding similarity; an admin confirms each proposal; only
/// <see cref="ConfirmAsync"/> ever writes a mapping. Also supplies FR-22 step 4 (<see cref="ICodeSetSuggestionProvider"/>), which
/// proposes a set for one line's record WITHOUT persisting anything.
/// </summary>
/// <remarks>
/// <para>
/// <b>No Action, no prompt (D-52, ADR-013).</b> The only AI call is <see cref="IInvoiceAi.GenerateEmbeddingAsync"/>. The code set table has
/// no description column (task 018), so a set is embedded as its name and standards body ("UTBMS Bankruptcy (ABA)") and a reference row as
/// its name. The reference tables carry no description either. When the AI is off the facade throws its feature-disabled exception, which
/// propagates (the route answers 503); it never falls back to a guess.
/// </para>
/// <para>
/// <b>Usable sets.</b> A set can be proposed or confirmed only if it is active and has at least one active Task code: the same test
/// <see cref="CodeSetResolver"/> applies when it loads a set, so a mapping this service writes is never one the resolver would skip. That
/// excludes the shared "UTBMS Activity" and "UTBMS Expense" sets.
/// </para>
/// <para>
/// <b>Concurrency.</b> Confirm re-reads the row and refuses with <see cref="CodeSetConfirmOutcome.AlreadyMapped"/> when it is mapped,
/// but the write itself is not conditional (the generic Dataverse seam has no row-version update): two admins confirming the same row in
/// the same instant can both pass the check, and the later write wins. Both writes are admin-confirmed mappings of a usable set, so the
/// outcome is a valid mapping either way.
/// </para>
/// <para>
/// <b>Lifetime.</b> Scoped. The usable code sets and their embeddings are loaded once per instance (one request or one job scope) and
/// reused for every row; nothing is cached across scopes, so an admin's edit of a set is seen by the next request.
/// </para>
/// </remarks>
public sealed class CodeSetProposalService : ICodeSetSuggestionProvider
{
    private const int ActiveState = 0;
    private const int PageSize = 500;
    private const string CodeSetEntity = CodeSetResolver.CodeSetEntity;
    private const string CodeEntity = CodeSetResolver.CodeEntity;
    private const string DefaultSetColumn = CodeSetResolver.DefaultCodeSetColumn;

    private readonly IGenericEntityService _entities;
    private readonly IInvoiceAi _ai;
    private readonly ILogger<CodeSetProposalService> _logger;

    private List<EmbeddedCodeSet>? _codeSets;

    public CodeSetProposalService(IGenericEntityService entities, IInvoiceAi ai, ILogger<CodeSetProposalService> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private sealed record EmbeddedCodeSet(Guid Id, string Name, float[] Vector);

    private sealed record ReferenceRow(Guid Id, string Name, bool Active, bool Mapped);

    // ── Proposals (GET) ─────────────────────────────────────────────────────────

    /// <summary>
    /// A proposal for every ACTIVE matter type and practice area that has no default code set, matter types first, each group by
    /// name. Mapped rows are excluded. Writes nothing.
    /// </summary>
    public async Task<IReadOnlyList<CodeSetProposal>> ProposeAsync(CancellationToken cancellationToken = default)
    {
        var proposals = new List<CodeSetProposal>();
        foreach (var kind in new[] { CodeSetReferenceKind.MatterType, CodeSetReferenceKind.PracticeArea })
        {
            foreach (var row in await ListUnmappedAsync(kind, cancellationToken).ConfigureAwait(false))
            {
                var ranked = await RankAsync(row.Name, cancellationToken).ConfigureAwait(false);
                proposals.Add(ToProposal(kind, row, ranked));
            }
        }

        _logger.LogInformation("Code-set proposals: {Count} unmapped reference rows", proposals.Count);
        return proposals;
    }

    // ── FR-22 step 4 (ICodeSetSuggestionProvider) ───────────────────────────────

    /// <summary>
    /// Proposes the best code set for the record's matter type, else its practice area, but only when NEITHER has a mapping: a record
    /// with a mapped matter type or practice area was resolved (or its stale mapping skipped) by the resolver's own steps 2 and 3, and
    /// a suggestion must not second-guess an admin's choice. A lookup that is not set counts as unmapped; with both unset there is
    /// nothing to base a suggestion on. The result is used for that invoice's coding only (the resolver flags it
    /// <see cref="CodeSetSource.AiSuggestion"/> for review); it is never written as a mapping.
    /// </summary>
    public async Task<Guid?> SuggestCodeSetAsync(CodeSetSuggestionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var rows = new List<ReferenceRow>();
        foreach (var (kind, id) in new[]
        {
            (CodeSetReferenceKind.MatterType, context.MatterTypeId),
            (CodeSetReferenceKind.PracticeArea, context.PracticeAreaId),
        })
        {
            if (id is not { } referenceId)
            {
                continue;
            }

            var row = await ReadAsync(kind, referenceId, cancellationToken).ConfigureAwait(false);
            if (row is { Mapped: true })
            {
                return null;
            }

            if (row is not null)
            {
                rows.Add(row);
            }
        }

        if (rows.Count == 0)
        {
            return null;
        }

        var ranked = await RankAsync(rows[0].Name, cancellationToken).ConfigureAwait(false);
        return ranked.Count == 0 ? null : ranked[0].CodeSetId;
    }

    // ── Confirm (POST) ──────────────────────────────────────────────────────────

    /// <summary>
    /// The ONLY writer of <c>sprk_defaultbillingcodeset</c>. Sets the lookup on exactly the row named, after re-reading it: a row that is
    /// already mapped is refused and nothing is written. The caller's Write privilege on the table was checked by the route's filter;
    /// the write is made as the app (check as the caller, write as the app, owner decision G5).
    /// </summary>
    /// <param name="confirmedBy">The admin's object id, for the audit log.</param>
    public async Task<CodeSetConfirmOutcome> ConfirmAsync(
        CodeSetReferenceKind kind, Guid referenceId, Guid codeSetId, Guid? confirmedBy, CancellationToken cancellationToken = default)
    {
        var row = await ReadAsync(kind, referenceId, cancellationToken).ConfigureAwait(false);
        if (row is not { Active: true })
        {
            return CodeSetConfirmOutcome.ReferenceNotFound;
        }

        if (row.Mapped)
        {
            return CodeSetConfirmOutcome.AlreadyMapped;
        }

        if (!await IsUsableCodeSetAsync(codeSetId, cancellationToken).ConfigureAwait(false))
        {
            return CodeSetConfirmOutcome.CodeSetNotFound;
        }

        await _entities.UpdateAsync(
            CodeSetReferenceTables.Entity(kind),
            referenceId,
            new Dictionary<string, object> { [DefaultSetColumn] = new EntityReference(CodeSetEntity, codeSetId) },
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Code-set mapping confirmed: {Table} {ReferenceId} -> code set {CodeSetId} by {ConfirmedBy}",
            CodeSetReferenceTables.Segment(kind),
            referenceId,
            codeSetId,
            confirmedBy);
        return CodeSetConfirmOutcome.Confirmed;
    }

    // ── Reference rows ──────────────────────────────────────────────────────────

    private async Task<List<ReferenceRow>> ListUnmappedAsync(CodeSetReferenceKind kind, CancellationToken ct)
    {
        var nameColumn = CodeSetReferenceTables.NameColumn(kind);
        var query = new QueryExpression(CodeSetReferenceTables.Entity(kind))
        {
            ColumnSet = new ColumnSet(nameColumn, DefaultSetColumn, "statecode"),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, ActiveState);
        query.Criteria.AddCondition(DefaultSetColumn, ConditionOperator.Null);
        query.AddOrder(nameColumn, OrderType.Ascending);

        var rows = await RetrieveAllAsync(query, ct).ConfigureAwait(false);
        return rows.Select(r => ToRow(kind, r)).Where(r => !r.Mapped && r.Name.Length > 0).ToList();
    }

    private async Task<ReferenceRow?> ReadAsync(CodeSetReferenceKind kind, Guid id, CancellationToken ct)
    {
        var query = new QueryExpression(CodeSetReferenceTables.Entity(kind))
        {
            ColumnSet = new ColumnSet(CodeSetReferenceTables.NameColumn(kind), DefaultSetColumn, "statecode"),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 1,
        };
        query.Criteria.AddCondition(CodeSetReferenceTables.IdColumn(kind), ConditionOperator.Equal, id);

        var rows = (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities;
        return rows.Count == 0 ? null : ToRow(kind, rows[0]);
    }

    private static ReferenceRow ToRow(CodeSetReferenceKind kind, Entity row) => new(
        row.Id,
        row.GetAttributeValue<string>(CodeSetReferenceTables.NameColumn(kind))?.Trim() ?? string.Empty,
        (row.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? ActiveState) == ActiveState,
        row.GetAttributeValue<EntityReference>(DefaultSetColumn) is not null);

    // ── Usable code sets and ranking ────────────────────────────────────────────

    private async Task<bool> IsUsableCodeSetAsync(Guid codeSetId, CancellationToken ct)
    {
        if (codeSetId == Guid.Empty)
        {
            return false;
        }

        var setQuery = new QueryExpression(CodeSetEntity)
        {
            ColumnSet = new ColumnSet("sprk_name"),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 1,
        };
        setQuery.Criteria.AddCondition("sprk_billingcodesetid", ConditionOperator.Equal, codeSetId);
        setQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, ActiveState);
        if ((await _entities.RetrieveMultipleAsync(setQuery, ct).ConfigureAwait(false)).Entities.Count == 0)
        {
            return false;
        }

        var codeQuery = new QueryExpression(CodeEntity)
        {
            ColumnSet = new ColumnSet("sprk_code"),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 1,
        };
        codeQuery.Criteria.AddCondition("sprk_codeset", ConditionOperator.Equal, codeSetId);
        codeQuery.Criteria.AddCondition("sprk_codetype", ConditionOperator.Equal, (int)BillingCodeKind.Task);
        codeQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, ActiveState);
        return (await _entities.RetrieveMultipleAsync(codeQuery, ct).ConfigureAwait(false)).Entities.Count > 0;
    }

    private async Task<List<EmbeddedCodeSet>> LoadCodeSetsAsync(CancellationToken ct)
    {
        if (_codeSets is not null)
        {
            return _codeSets;
        }

        var setQuery = new QueryExpression(CodeSetEntity)
        {
            ColumnSet = new ColumnSet("sprk_name", "sprk_body"),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        setQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, ActiveState);
        setQuery.AddOrder("sprk_name", OrderType.Ascending);

        var codeQuery = new QueryExpression(CodeEntity)
        {
            ColumnSet = new ColumnSet("sprk_codeset"),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        codeQuery.Criteria.AddCondition("sprk_codetype", ConditionOperator.Equal, (int)BillingCodeKind.Task);
        codeQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, ActiveState);
        codeQuery.AddOrder("sprk_codeset", OrderType.Ascending);

        var sets = await RetrieveAllAsync(setQuery, ct).ConfigureAwait(false);
        var withTaskCodes = (await RetrieveAllAsync(codeQuery, ct).ConfigureAwait(false))
            .Select(c => c.GetAttributeValue<EntityReference>("sprk_codeset")?.Id)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToHashSet();

        var loaded = new List<EmbeddedCodeSet>();
        foreach (var set in sets.Where(s => withTaskCodes.Contains(s.Id)))
        {
            var name = set.GetAttributeValue<string>("sprk_name")?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var body = set.GetAttributeValue<string>("sprk_body")?.Trim();
            var text = string.IsNullOrEmpty(body) ? name : $"{name} ({body})";
            loaded.Add(new EmbeddedCodeSet(set.Id, name, await EmbedAsync(text, ct).ConfigureAwait(false)));
        }

        _codeSets = loaded;
        return loaded;
    }

    /// <summary>The usable sets for <paramref name="referenceName"/>, best first (ties by name, then id, so the order is stable).</summary>
    private async Task<List<CodeSetCandidate>> RankAsync(string referenceName, CancellationToken ct)
    {
        var sets = await LoadCodeSetsAsync(ct).ConfigureAwait(false);
        if (sets.Count == 0)
        {
            return [];
        }

        var vector = await EmbedAsync(referenceName, ct).ConfigureAwait(false);
        return sets
            .Select(s => new CodeSetCandidate(s.Id, s.Name, decimal.Round(TimekeeperMatcher.Cosine(vector, s.Vector), 3)))
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.CodeSetId)
            .ToList();
    }

    private async Task<float[]> EmbedAsync(string text, CancellationToken ct) =>
        (await _ai.GenerateEmbeddingAsync(text, cancellationToken: ct).ConfigureAwait(false)).ToArray();

    private static CodeSetProposal ToProposal(CodeSetReferenceKind kind, ReferenceRow row, List<CodeSetCandidate> ranked)
    {
        var segment = CodeSetReferenceTables.Segment(kind);
        if (ranked.Count == 0)
        {
            return new CodeSetProposal(
                segment, row.Id, row.Name, null, null,
                "No active code set with Task codes exists to propose.");
        }

        var best = ranked[0];
        var runnerUp = ranked.Count > 1 ? ranked[1] : null;
        var rationale = string.Create(CultureInfo.InvariantCulture, $"'{row.Name}' is closest by name to '{best.Name}' (similarity {best.Score:0.000})");
        rationale += runnerUp is null
            ? "; it is the only usable code set."
            : string.Create(CultureInfo.InvariantCulture, $"; runner-up '{runnerUp.Name}' ({runnerUp.Score:0.000}).");
        return new CodeSetProposal(segment, row.Id, row.Name, best, runnerUp, rationale);
    }

    private async Task<List<Entity>> RetrieveAllAsync(QueryExpression query, CancellationToken ct)
    {
        query.PageInfo = new PagingInfo { PageNumber = 1, Count = PageSize };
        var rows = new List<Entity>();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            rows.AddRange(page.Entities);

            if (!page.MoreRecords)
            {
                return rows;
            }

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }
    }
}
