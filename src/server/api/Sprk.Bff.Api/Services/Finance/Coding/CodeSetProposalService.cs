using System.Data;
using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
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

    internal static InvoiceCodeSetReferenceType AiType(CodeSetReferenceKind kind) =>
        kind == CodeSetReferenceKind.MatterType ? InvoiceCodeSetReferenceType.MatterType : InvoiceCodeSetReferenceType.PracticeArea;
}

/// <summary>One code set, with the embedding similarity of the reference row to its profile.</summary>
/// <param name="Score">Cosine similarity of the embeddings (row name against the set's profile), 0 to 1, three decimals.</param>
public sealed record CodeSetCandidate(Guid CodeSetId, string Name, decimal Score);

/// <summary>How an unmapped reference row ended (D-93). A mapped row is counted in <see cref="CodeSetCoverage"/>, never listed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CodeSetMatchOutcome>))]
public enum CodeSetMatchOutcome
{
    /// <summary>A code set is proposed; the admin confirms it (or maps another) through the confirm route.</summary>
    Proposed,

    /// <summary>No code set fits. Nothing is proposed: the admin maps the row by hand or adds a code set.</summary>
    NoneFits,
}

/// <summary>The outcome for one unmapped reference row. Nothing here has been applied.</summary>
/// <param name="Table"><c>mattertype</c> or <c>practicearea</c> (the confirm route's <c>{table}</c> segment).</param>
/// <param name="Outcome"><see cref="CodeSetMatchOutcome.Proposed"/> or <see cref="CodeSetMatchOutcome.NoneFits"/>.</param>
/// <param name="Proposed">The proposed set (with its similarity); always <c>null</c> for <see cref="CodeSetMatchOutcome.NoneFits"/>, so a
/// none-fits row never carries a proposal that could be confirmed by mistake.</param>
/// <param name="RunnerUp">For a proposal, the most similar other set; otherwise <c>null</c>.</param>
/// <param name="Closest">For none-fits, the most similar set, as context only (not a proposal); otherwise <c>null</c>.</param>
/// <param name="Confidence">The AI review step's confidence (0-1) in its answer; <c>null</c> when the step did not decide.</param>
/// <param name="Reviewed"><c>true</c> when the AI review step (the <c>invoice-codeset-match</c> Action) decided the outcome; <c>false</c>
/// for an embedding-only fallback proposal (the step failed, was off, or answered outside the candidates: low confidence, check before
/// confirming) and for rows that could not be matched at all.</param>
/// <param name="Rationale">One or two sentences for the admin: why this set, or why none, and what to do.</param>
public sealed record CodeSetProposal(
    string Table,
    Guid ReferenceId,
    string ReferenceName,
    CodeSetMatchOutcome Outcome,
    CodeSetCandidate? Proposed,
    CodeSetCandidate? RunnerUp,
    CodeSetCandidate? Closest,
    decimal? Confidence,
    bool Reviewed,
    string Rationale);

/// <summary>
/// Coverage of one reference table: every ACTIVE row is exactly one of mapped, proposed or none-fits
/// (<c>Active = Mapped + Proposed + NoneFits</c>). <see cref="Unreviewed"/> counts the proposals the AI review step did not decide.
/// </summary>
public sealed record CodeSetCoverageCount(int Active, int Mapped, int Proposed, int NoneFits, int Unreviewed);

/// <summary>Coverage of both reference tables (D-93).</summary>
public sealed record CodeSetCoverage(CodeSetCoverageCount MatterTypes, CodeSetCoverageCount PracticeAreas);

/// <summary>What <see cref="CodeSetProposalService.ProposeAsync"/> returns: one entry per unmapped active row, plus coverage.</summary>
public sealed record CodeSetProposalList(IReadOnlyList<CodeSetProposal> Proposals, CodeSetCoverage Coverage);

/// <summary>What <see cref="CodeSetProposalService.ConfirmAsync"/> did.</summary>
public enum CodeSetConfirmOutcome
{
    /// <summary>The mapping was written on exactly that row.</summary>
    Confirmed,

    /// <summary>No active reference row has that id.</summary>
    ReferenceNotFound,

    /// <summary>No active code set with at least one active Task code has that id.</summary>
    CodeSetNotFound,

    /// <summary>The row gained a mapping since the proposal (or kept changing under the write); nothing was written.</summary>
    AlreadyMapped,
}

/// <summary>
/// Code-set mapping proposals (ebilling-r1 task 048, FR-22; matching redesigned by task 054, D-93): for every active matter type and
/// practice area with no <c>sprk_defaultbillingcodeset</c>, either proposes a code set or says that none fits; an admin confirms each
/// proposal; only <see cref="ConfirmAsync"/> ever writes a mapping. Also supplies FR-22 step 4 (<see cref="ICodeSetSuggestionProvider"/>),
/// which uses the same matcher for one invoice's record WITHOUT persisting anything.
/// </summary>
/// <remarks>
/// <para>
/// <b>Matching (D-93).</b> (1) Every usable set gets a profile: its name and body, the admin-editable tuning text
/// (<c>sprk_description</c>, <c>sprk_matchhints</c>, seeded for the six UTBMS task sets) and its active Task codes; the profiles are
/// embedded once per instance. (2) The row's name is embedded and the sets are ranked by cosine similarity (pre-rank only). (3) The
/// candidates (every usable set when there are at most <see cref="AllCandidatesUpTo"/>, else the top <see cref="TopCandidates"/>) go to
/// the <c>invoice-codeset-match</c> Action through <see cref="IInvoiceAi.MatchCodeSetAsync"/> with up to <see cref="MaxExamples"/>
/// confirmed mappings of this environment as examples; it picks one or answers "none". (4) The answer is guarded: an id that is not a
/// candidate is refused. When the Action fails, is switched off or is refused, the row falls back to the embedding ranking marked
/// <see cref="CodeSetProposal.Reviewed"/> = false: the list never fails because of the review step, and a "none fits" is never
/// fabricated (only the Action, or the absence of any usable set, produces one). Prompt, tier and schema live in the Action row (D-52).
/// </para>
/// <para>
/// <b>AI off.</b> When the whole AI stack is off the embedding call throws the facade's feature-disabled exception, which propagates
/// (the route answers 503); proposals are never invented without it.
/// </para>
/// <para>
/// <b>Usable sets.</b> A set can be a candidate or be confirmed only if it is active and has at least one active Task code: the same
/// test <see cref="CodeSetResolver"/> applies when it loads a set, so the shared "UTBMS Activity" and "UTBMS Expense" sets never appear.
/// </para>
/// <para>
/// <b>Concurrency.</b> Confirm re-reads the row with its <c>versionnumber</c> and writes through
/// <see cref="IFieldMappingDataverseService.UpdateRecordFieldsIfUnchangedAsync"/> (<c>If-Match</c> on that version). Two admins
/// confirming the same row at once: the second write gets HTTP 412, the row is re-read, and it is refused as
/// <see cref="CodeSetConfirmOutcome.AlreadyMapped"/>, so the first admin's mapping is never silently overwritten.
/// </para>
/// <para>
/// <b>Lifetime.</b> Scoped. The usable sets, their embeddings and the examples are loaded once per instance (one request or one job
/// scope); nothing is cached across scopes, so an admin's edit of a set or a new confirmation is seen by the next request.
/// </para>
/// </remarks>
public sealed class CodeSetProposalService : ICodeSetSuggestionProvider
{
    /// <summary>Send every usable set to the Action when there are at most this many (dev and the seed have six).</summary>
    internal const int AllCandidatesUpTo = 6;

    /// <summary>Otherwise send only this many top-ranked sets: enough for a close second choice, short enough for a Fast-tier prompt.</summary>
    internal const int TopCandidates = 4;

    /// <summary>
    /// At most this many confirmed mappings go to the Action as examples (a constant, not a setting: it bounds prompt size only, and
    /// the reference tables hold tens of rows). They come from this environment's own reference tables, so never from another tenant.
    /// </summary>
    internal const int MaxExamples = 10;

    /// <summary>Re-read and retry a confirm this many times when the row keeps changing under the conditional write.</summary>
    internal const int MaxConfirmAttempts = 3;

    internal const string VersionColumn = "versionnumber";

    /// <summary>The tuning columns on <c>sprk_billingcodeset</c> (task 054): the work the set covers, and where it fits / does not fit.</summary>
    internal const string DescriptionColumn = "sprk_description";

    internal const string MatchHintsColumn = "sprk_matchhints";

    /// <summary>The Web API bind of the lookup (navigation property read live on spaarkedev1, 2026-10-16) and the set's entity set.</summary>
    internal const string DefaultSetBind = "sprk_DefaultBillingCodeSet@odata.bind";

    internal const string CodeSetEntitySet = "sprk_billingcodesets";

    private const int ActiveState = 0;
    private const int PageSize = 500;
    private const string CodeSetEntity = CodeSetResolver.CodeSetEntity;
    private const string CodeEntity = CodeSetResolver.CodeEntity;
    private const string DefaultSetColumn = CodeSetResolver.DefaultCodeSetColumn;

    private readonly IGenericEntityService _entities;
    private readonly IFieldMappingDataverseService _records;
    private readonly IInvoiceAi _ai;
    private readonly ILogger<CodeSetProposalService> _logger;

    private List<ProfiledCodeSet>? _codeSets;
    private List<InvoiceCodeSetMatchExample>? _examples;

    public CodeSetProposalService(
        IGenericEntityService entities,
        IFieldMappingDataverseService records,
        IInvoiceAi ai,
        ILogger<CodeSetProposalService> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private sealed record ProfiledCodeSet(
        Guid Id, string Name, string? Body, string? Description, string? MatchHints, IReadOnlyList<string> TaskCodes, float[] Vector);

    private sealed record ReferenceRow(Guid Id, string Name, bool Active, Guid? MappedTo, long? Version)
    {
        public bool Mapped => MappedTo is not null;
    }

    private sealed record Ranked(ProfiledCodeSet Set, decimal Score)
    {
        public CodeSetCandidate ToCandidate() => new(Set.Id, Set.Name, Score);
    }

    // ── Proposals (GET) ─────────────────────────────────────────────────────────

    /// <summary>
    /// One entry for every ACTIVE matter type and practice area that has no default code set (matter types first, each group by name):
    /// proposed or none-fits. Mapped rows are counted in the coverage, not listed. Writes nothing.
    /// </summary>
    public async Task<CodeSetProposalList> ProposeAsync(CancellationToken cancellationToken = default)
    {
        var proposals = new List<CodeSetProposal>();
        var counts = new Dictionary<CodeSetReferenceKind, CodeSetCoverageCount>();
        foreach (var kind in new[] { CodeSetReferenceKind.MatterType, CodeSetReferenceKind.PracticeArea })
        {
            var rows = await ListActiveAsync(kind, cancellationToken).ConfigureAwait(false);
            var forKind = new List<CodeSetProposal>();
            foreach (var row in rows.Where(r => !r.Mapped))
            {
                forKind.Add(await MatchAsync(kind, row, cancellationToken).ConfigureAwait(false));
            }

            proposals.AddRange(forKind);
            counts[kind] = new CodeSetCoverageCount(
                Active: rows.Count,
                Mapped: rows.Count(r => r.Mapped),
                Proposed: forKind.Count(p => p.Outcome == CodeSetMatchOutcome.Proposed),
                NoneFits: forKind.Count(p => p.Outcome == CodeSetMatchOutcome.NoneFits),
                Unreviewed: forKind.Count(p => p.Outcome == CodeSetMatchOutcome.Proposed && !p.Reviewed));
        }

        var coverage = new CodeSetCoverage(counts[CodeSetReferenceKind.MatterType], counts[CodeSetReferenceKind.PracticeArea]);
        _logger.LogInformation(
            "Code-set proposals: {Count} unmapped reference rows ({Proposed} proposed, {NoneFits} none fits, {Unreviewed} unreviewed)",
            proposals.Count,
            proposals.Count(p => p.Outcome == CodeSetMatchOutcome.Proposed),
            proposals.Count(p => p.Outcome == CodeSetMatchOutcome.NoneFits),
            proposals.Count(p => p.Outcome == CodeSetMatchOutcome.Proposed && !p.Reviewed));
        return new CodeSetProposalList(proposals, coverage);
    }

    // ── FR-22 step 4 (ICodeSetSuggestionProvider) ───────────────────────────────

    /// <summary>
    /// Runs the same matcher as the proposals list for the record's matter type, then (if that matched none) its practice area, but only
    /// when NEITHER has a mapping: a record with a mapped matter type or practice area was resolved (or its stale mapping skipped) by the
    /// resolver's own steps 2 and 3, and a suggestion must not second-guess an admin's choice. A lookup that is not set counts as
    /// unmapped; with both unset there is nothing to match. A none-fits answer gives no suggestion. The result is used for that invoice's
    /// coding only (the resolver flags it <see cref="CodeSetSource.AiSuggestion"/> for review); it is never written as a mapping.
    /// </summary>
    public async Task<Guid?> SuggestCodeSetAsync(CodeSetSuggestionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var rows = new List<(CodeSetReferenceKind Kind, ReferenceRow Row)>();
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
                rows.Add((kind, row));
            }
        }

        foreach (var (kind, row) in rows)
        {
            var match = await MatchAsync(kind, row, cancellationToken).ConfigureAwait(false);
            if (match is { Outcome: CodeSetMatchOutcome.Proposed, Proposed: { } proposed })
            {
                return proposed.CodeSetId;
            }
        }

        return null;
    }

    // ── Confirm (POST) ──────────────────────────────────────────────────────────

    /// <summary>
    /// The ONLY writer of <c>sprk_defaultbillingcodeset</c>. Sets the lookup on exactly the row named, after re-reading it: a row that is
    /// already mapped is refused and nothing is written. The write is conditional on the row's version (If-Match), so a mapping another
    /// admin wrote after the re-read is never overwritten: the row is re-read and refused as <see cref="CodeSetConfirmOutcome.AlreadyMapped"/>.
    /// The caller's Write privilege on the table was checked by the route's filter; the write is made as the app (check as the caller,
    /// write as the app, owner decision G5).
    /// </summary>
    /// <param name="confirmedBy">The admin's object id, for the audit log.</param>
    public async Task<CodeSetConfirmOutcome> ConfirmAsync(
        CodeSetReferenceKind kind, Guid referenceId, Guid codeSetId, Guid? confirmedBy, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= MaxConfirmAttempts; attempt++)
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

            if (attempt == 1 && !await IsUsableCodeSetAsync(codeSetId, cancellationToken).ConfigureAwait(false))
            {
                return CodeSetConfirmOutcome.CodeSetNotFound;
            }

            // Every Dataverse row has a version. Without one the write cannot be conditional, so refuse rather than overwrite blind.
            var version = row.Version ?? throw new InvalidOperationException(
                $"The {CodeSetReferenceTables.Segment(kind)} row's {VersionColumn} could not be read, so the mapping cannot be written safely. Nothing was saved.");

            try
            {
                await _records.UpdateRecordFieldsIfUnchangedAsync(
                    CodeSetReferenceTables.Entity(kind),
                    referenceId,
                    new Dictionary<string, object?> { [DefaultSetBind] = $"/{CodeSetEntitySet}({codeSetId})" },
                    version,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (DBConcurrencyException changed)
            {
                // Someone changed the row after the re-read (most likely another admin mapped it): re-read and decide again.
                _logger.LogWarning(changed,
                    "Code-set mapping: {Table} {ReferenceId} changed before the confirm was written (attempt {Attempt} of {MaxAttempts})",
                    CodeSetReferenceTables.Segment(kind), referenceId, attempt, MaxConfirmAttempts);
                continue;
            }
            catch (KeyNotFoundException)
            {
                return CodeSetConfirmOutcome.ReferenceNotFound;
            }

            _logger.LogInformation(
                "Code-set mapping confirmed: {Table} {ReferenceId} -> code set {CodeSetId} by {ConfirmedBy}",
                CodeSetReferenceTables.Segment(kind),
                referenceId,
                codeSetId,
                confirmedBy);
            return CodeSetConfirmOutcome.Confirmed;
        }

        _logger.LogWarning(
            "Code-set mapping: {Table} {ReferenceId} kept changing; confirm refused after {MaxAttempts} attempts, nothing written",
            CodeSetReferenceTables.Segment(kind), referenceId, MaxConfirmAttempts);
        return CodeSetConfirmOutcome.AlreadyMapped;
    }

    // ── The matcher ─────────────────────────────────────────────────────────────

    private async Task<CodeSetProposal> MatchAsync(CodeSetReferenceKind kind, ReferenceRow row, CancellationToken ct)
    {
        var segment = CodeSetReferenceTables.Segment(kind);
        if (row.Name.Length == 0)
        {
            return new CodeSetProposal(
                segment, row.Id, row.Name, CodeSetMatchOutcome.NoneFits, null, null, null, null, false,
                "The row has no name to match. Name it, or map it by hand.");
        }

        var sets = await LoadCodeSetsAsync(ct).ConfigureAwait(false);
        if (sets.Count == 0)
        {
            return new CodeSetProposal(
                segment, row.Id, row.Name, CodeSetMatchOutcome.NoneFits, null, null, null, null, false,
                "No active code set with Task codes exists. Add a code set, then map this row.");
        }

        var ranked = await RankAsync(row.Name, ct).ConfigureAwait(false);
        var candidates = ranked.Count <= AllCandidatesUpTo ? ranked : ranked.Take(TopCandidates).ToList();
        var examples = await LoadExamplesAsync(ct).ConfigureAwait(false);

        InvoiceCodeSetMatchResult? reply;
        try
        {
            reply = await _ai.MatchCodeSetAsync(
                new InvoiceCodeSetMatchRequest
                {
                    ReferenceType = CodeSetReferenceTables.AiType(kind),
                    ReferenceName = row.Name,
                    Candidates = candidates
                        .Select(c => new InvoiceCodeSetCandidate(c.Set.Id, c.Set.Name, c.Set.Body, c.Set.Description, c.Set.MatchHints, c.Set.TaskCodes))
                        .ToList(),
                    Examples = examples,
                },
                ct).ConfigureAwait(false);
        }
        catch (InvoiceAiException ex)
        {
            _logger.LogWarning(
                "Code-set match for {Table} {ReferenceId}: AI review step failed ({ReasonCode}); using the embedding ranking, unreviewed",
                segment, row.Id, ex.ReasonCode);
            return Fallback(segment, row, ranked, "the AI review step failed");
        }
        catch (FeatureDisabledException ex)
        {
            _logger.LogWarning(
                "Code-set match for {Table} {ReferenceId}: AI review step is off ({ErrorCode}); using the embedding ranking, unreviewed",
                segment, row.Id, ex.ErrorCode);
            return Fallback(segment, row, ranked, "the AI review step is switched off");
        }

        var choice = reply?.CodeSetId?.Trim();
        if (reply is not null && string.Equals(choice, InvoiceCodeSetMatchResult.NoneFits, StringComparison.OrdinalIgnoreCase))
        {
            var closest = ranked[0].ToCandidate();
            return new CodeSetProposal(
                segment, row.Id, row.Name, CodeSetMatchOutcome.NoneFits, null, null, closest, reply.Confidence, true,
                $"No code set fits: {reply.Reason.Trim()} Map this row by hand or add a code set (closest by similarity: '{closest.Name}').");
        }

        // The output guard (same discipline as LineCodingGuard): only an id from THIS request's candidates is accepted.
        if (reply is not null && Guid.TryParse(choice, out var chosenId) && candidates.FirstOrDefault(c => c.Set.Id == chosenId) is { } chosen)
        {
            var runnerUp = ranked.FirstOrDefault(r => r.Set.Id != chosenId)?.ToCandidate();
            return new CodeSetProposal(
                segment, row.Id, row.Name, CodeSetMatchOutcome.Proposed, chosen.ToCandidate(), runnerUp, null, reply.Confidence, true,
                reply.Reason.Trim());
        }

        _logger.LogWarning(
            "Code-set match for {Table} {ReferenceId}: refused an AI answer that is not one of the {CandidateCount} candidates; using the embedding ranking, unreviewed",
            segment, row.Id, candidates.Count);
        return Fallback(segment, row, ranked, "the AI review step answered with a code set that was not one of the candidates");
    }

    /// <summary>The embedding ranking as an UNREVIEWED proposal: never a "none fits", never a failure.</summary>
    private static CodeSetProposal Fallback(string segment, ReferenceRow row, List<Ranked> ranked, string why)
    {
        var best = ranked[0].ToCandidate();
        var runnerUp = ranked.Count > 1 ? ranked[1].ToCandidate() : null;
        var rationale = string.Create(
            CultureInfo.InvariantCulture,
            $"Not reviewed by AI ({why}); low confidence, check before confirming. '{row.Name}' is closest by similarity to '{best.Name}' ({best.Score:0.000})");
        rationale += runnerUp is null
            ? "; it is the only usable code set."
            : string.Create(CultureInfo.InvariantCulture, $"; runner-up '{runnerUp.Name}' ({runnerUp.Score:0.000}).");
        return new CodeSetProposal(segment, row.Id, row.Name, CodeSetMatchOutcome.Proposed, best, runnerUp, null, null, false, rationale);
    }

    // ── Reference rows ──────────────────────────────────────────────────────────

    private async Task<List<ReferenceRow>> ListActiveAsync(CodeSetReferenceKind kind, CancellationToken ct)
    {
        var nameColumn = CodeSetReferenceTables.NameColumn(kind);
        var query = new QueryExpression(CodeSetReferenceTables.Entity(kind))
        {
            ColumnSet = new ColumnSet(nameColumn, DefaultSetColumn, "statecode"),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, ActiveState);
        query.AddOrder(nameColumn, OrderType.Ascending);

        var rows = await RetrieveAllAsync(query, ct).ConfigureAwait(false);
        return rows.Select(r => ToRow(kind, r)).ToList();
    }

    private async Task<ReferenceRow?> ReadAsync(CodeSetReferenceKind kind, Guid id, CancellationToken ct)
    {
        var query = new QueryExpression(CodeSetReferenceTables.Entity(kind))
        {
            ColumnSet = new ColumnSet(CodeSetReferenceTables.NameColumn(kind), DefaultSetColumn, "statecode", VersionColumn),
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
        row.GetAttributeValue<EntityReference>(DefaultSetColumn)?.Id,
        row.Contains(VersionColumn) && row[VersionColumn] is long version ? version : null);

    /// <summary>
    /// Confirmed mappings of this environment, as the Action's examples: active rows of both tables mapped to a usable set (a mapping to
    /// a stale set would teach the wrong thing), matter types first, each by name, at most <see cref="MaxExamples"/>.
    /// </summary>
    private async Task<List<InvoiceCodeSetMatchExample>> LoadExamplesAsync(CancellationToken ct)
    {
        if (_examples is not null)
        {
            return _examples;
        }

        var setNames = (await LoadCodeSetsAsync(ct).ConfigureAwait(false)).ToDictionary(s => s.Id, s => s.Name);
        var examples = new List<InvoiceCodeSetMatchExample>();
        foreach (var kind in new[] { CodeSetReferenceKind.MatterType, CodeSetReferenceKind.PracticeArea })
        {
            var nameColumn = CodeSetReferenceTables.NameColumn(kind);
            var query = new QueryExpression(CodeSetReferenceTables.Entity(kind))
            {
                ColumnSet = new ColumnSet(nameColumn, DefaultSetColumn, "statecode"),
                Criteria = new FilterExpression(LogicalOperator.And),
            };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, ActiveState);
            query.Criteria.AddCondition(DefaultSetColumn, ConditionOperator.NotNull);
            query.AddOrder(nameColumn, OrderType.Ascending);

            foreach (var row in (await RetrieveAllAsync(query, ct).ConfigureAwait(false)).Select(r => ToRow(kind, r)))
            {
                if (row.Name.Length > 0 && row.MappedTo is { } setId && setNames.TryGetValue(setId, out var setName))
                {
                    examples.Add(new InvoiceCodeSetMatchExample(CodeSetReferenceTables.AiType(kind), row.Name, setName));
                }
            }
        }

        _examples = examples.Take(MaxExamples).ToList();
        return _examples;
    }

    // ── Usable code sets, profiles and ranking ──────────────────────────────────

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

    /// <summary>
    /// The usable sets with their profiles, embedded once per instance. The profile's first line is <c>"{name} ({body})"</c>; then the
    /// tuning text and the Task codes, so a set named only "UTBMS Project" is ranked by what its codes actually cover.
    /// </summary>
    private async Task<List<ProfiledCodeSet>> LoadCodeSetsAsync(CancellationToken ct)
    {
        if (_codeSets is not null)
        {
            return _codeSets;
        }

        var setQuery = new QueryExpression(CodeSetEntity)
        {
            ColumnSet = new ColumnSet("sprk_name", "sprk_body", DescriptionColumn, MatchHintsColumn),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        setQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, ActiveState);
        setQuery.AddOrder("sprk_name", OrderType.Ascending);

        var codeQuery = new QueryExpression(CodeEntity)
        {
            ColumnSet = new ColumnSet("sprk_codeset", "sprk_code", "sprk_description"),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        codeQuery.Criteria.AddCondition("sprk_codetype", ConditionOperator.Equal, (int)BillingCodeKind.Task);
        codeQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, ActiveState);
        codeQuery.AddOrder("sprk_codeset", OrderType.Ascending);
        codeQuery.AddOrder("sprk_code", OrderType.Ascending);

        var sets = await RetrieveAllAsync(setQuery, ct).ConfigureAwait(false);
        var taskCodes = (await RetrieveAllAsync(codeQuery, ct).ConfigureAwait(false))
            .Where(c => c.GetAttributeValue<EntityReference>("sprk_codeset") is not null)
            .GroupBy(c => c.GetAttributeValue<EntityReference>("sprk_codeset").Id)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g
                    .Select(c => $"{c.GetAttributeValue<string>("sprk_code")?.Trim()} {c.GetAttributeValue<string>("sprk_description")?.Trim()}".Trim())
                    .Where(t => t.Length > 0)
                    .ToList());

        var loaded = new List<ProfiledCodeSet>();
        foreach (var set in sets.Where(s => taskCodes.ContainsKey(s.Id)))
        {
            var name = set.GetAttributeValue<string>("sprk_name")?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var body = Text(set, "sprk_body");
            var description = Text(set, DescriptionColumn);
            var hints = Text(set, MatchHintsColumn);
            var codes = taskCodes[set.Id];

            var profile = new List<string> { body is null ? name : $"{name} ({body})" };
            if (description is not null)
            {
                profile.Add(description);
            }

            if (hints is not null)
            {
                profile.Add("Fits: " + hints);
            }

            profile.Add("Task codes: " + string.Join("; ", codes));
            var vector = await EmbedAsync(string.Join('\n', profile), ct).ConfigureAwait(false);
            loaded.Add(new ProfiledCodeSet(set.Id, name, body, description, hints, codes, vector));
        }

        _codeSets = loaded;
        return loaded;
    }

    private static string? Text(Entity row, string column)
    {
        var value = row.GetAttributeValue<string>(column)?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>The usable sets for <paramref name="referenceName"/>, best first (ties by name, then id, so the order is stable).</summary>
    private async Task<List<Ranked>> RankAsync(string referenceName, CancellationToken ct)
    {
        var sets = await LoadCodeSetsAsync(ct).ConfigureAwait(false);
        var vector = await EmbedAsync(referenceName, ct).ConfigureAwait(false);
        return sets
            .Select(s => new Ranked(s, decimal.Round(TimekeeperMatcher.Cosine(vector, s.Vector), 3)))
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Set.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Set.Id)
            .ToList();
    }

    private async Task<float[]> EmbedAsync(string text, CancellationToken ct) =>
        (await _ai.GenerateEmbeddingAsync(text, cancellationToken: ct).ConfigureAwait(false)).ToArray();

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
