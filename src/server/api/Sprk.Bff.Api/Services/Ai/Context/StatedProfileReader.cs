// spaarkeai-assistant-enhancements-r1 task 030 (FR-E2): User-scope STATED-profile reader — the sibling
// of the User-memory RECALL fragment (ResolveUserMemoryFragmentAsync → IMemoryItemStore). Reads the
// caller's typed sprk_userprofile row (keyed by sprk_systemuser = the resolved systemuserid) plus its
// N:N sprk_practicearea_ref names, and returns the raw stated facts. ContextBinder renders them
// (StatedProfileRenderer) and folds the block into the User slice's ContextEnvelope.userFragment.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — nothing on the platform reads the sprk_userprofile row into a prompt. The User-memory
//       RECALL fragment (IMemoryItemStore.ToUserPromptFragmentAsync) reads LEARNED signals from Cosmos;
//       this reads the STATED typed profile from Dataverse (ADR-042: stated ≠ learned; different store,
//       different governance). ICallerSystemUserResolver resolves the identity KEY only, not the profile.
//   (2) Extension — this is the sibling producer of the memory-recall fragment, not a new pipeline. It
//       reuses ContextBinder's existing ResolveCallerSystemUserIdAsync for identity (no second identity
//       mechanism) and the same IDataverseService one-hop read facade the caller resolvers use.
//   (3) Cost-of-doing-nothing — a user who completed the My Assistant questionnaire (task 042) has a
//       typed profile that never reaches any prompt: the assistant cannot bias its one turn on the user's
//       stated role / practice areas / preferences (the exact FR-E2 gap this reader closes).
//
// Placement Justification (bff-extensions.md): lives in Services/Ai/Context/ (ADR-013 in-zone AI code)
// alongside the Context Binder — its ONE consumer — exactly like ICallerSystemUserResolver. Latency-coupled
// to the per-turn bind (runs inside ContextBinder.BindAsync), so it belongs in the BFF, not a separate
// service. ADR-039: this is a preference-only surface — it MUST NOT touch AgentToolFilterContext or any
// grounding/tool-projection path; it only biases the one agent turn's prompt.

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Ai.Context;

/// <summary>
/// Reads the caller's STATED typed profile (<c>sprk_userprofile</c>, keyed by
/// <c>sprk_systemuser</c> = the resolved Dataverse <c>systemuserid</c>) plus the related N:N
/// <c>sprk_practicearea_ref</c> names. Server-side only: the systemuserid is always the output of
/// <see cref="ContextBinder"/>'s deterministic claims→systemuser resolution (ADR-028), never a value from
/// client Args or an LLM completion. Consumed by <see cref="ContextBinder"/> to produce the stated-profile
/// block folded into the User slice's <c>userFragment</c> (ADR-042 stated-facts, NOT a memory store).
/// </summary>
public interface IStatedProfileReader
{
    /// <summary>
    /// Reads the stated profile for <paramref name="systemUserId"/> (a Dataverse <c>systemuserid</c>,
    /// "D"-format GUID string). Returns <c>null</c> when the user has no profile row, the id is not a valid
    /// GUID, or any Dataverse read fails — the caller degrades the stated-profile fragment to absent
    /// (ADR-032 P2 quiet no-op; the bind never fails on this path).
    /// </summary>
    Task<StatedProfile?> ReadAsync(string systemUserId, CancellationToken ct);
}

/// <summary>
/// The raw STATED facts read from a <c>sprk_userprofile</c> row (task 030 / FR-E2). Rendering into a prompt
/// block is <see cref="StatedProfileRenderer"/>'s concern (read ≠ render). Every field is optional; a row
/// with no populated stated fields renders to nothing (<see cref="HasAny"/> is <c>false</c>).
/// </summary>
public sealed record StatedProfile
{
    /// <summary>The <c>sprk_primaryrole</c> choice LABEL (mapped from the stored value per the task-001 contract). Null when unset or an unknown value.</summary>
    public string? RoleLabel { get; init; }

    /// <summary>Related <c>sprk_practicearea_ref.sprk_practiceareaname</c> values (N:N). Empty when none.</summary>
    public IReadOnlyList<string> PracticeAreaNames { get; init; } = Array.Empty<string>();

    /// <summary>User-authored <c>sprk_focusareas</c> free text. Null/blank when unset.</summary>
    public string? FocusAreas { get; init; }

    /// <summary><c>sprk_officelocation</c>. Null/blank when unset.</summary>
    public string? OfficeLocation { get; init; }

    /// <summary>User-authored <c>sprk_assistantpreferences</c> free text. Null/blank when unset.</summary>
    public string? AssistantPreferences { get; init; }

    /// <summary>True when at least one stated field is present (otherwise the profile renders to nothing).</summary>
    public bool HasAny =>
        !string.IsNullOrWhiteSpace(RoleLabel)
        || PracticeAreaNames.Count > 0
        || !string.IsNullOrWhiteSpace(FocusAreas)
        || !string.IsNullOrWhiteSpace(OfficeLocation)
        || !string.IsNullOrWhiteSpace(AssistantPreferences);
}

/// <summary>Default <see cref="IStatedProfileReader"/> — see file header for Component/Placement Justification.</summary>
public sealed class StatedProfileReader : IStatedProfileReader
{
    private const string ProfileEntity = "sprk_userprofile";
    private const string ProfileIdColumn = "sprk_userprofileid";
    private const string SystemUserColumn = "sprk_systemuser";
    private const string PrimaryRoleColumn = "sprk_primaryrole";
    private const string FocusAreasColumn = "sprk_focusareas";
    private const string OfficeLocationColumn = "sprk_officelocation";
    private const string AssistantPreferencesColumn = "sprk_assistantpreferences";

    private const string PracticeAreaEntity = "sprk_practicearea_ref";
    private const string PracticeAreaIdColumn = "sprk_practicearea_refid";
    private const string PracticeAreaNameColumn = "sprk_practiceareaname";
    private const string IntersectEntity = "sprk_userprofile_sprk_practicearea_ref";

    /// <summary>
    /// <c>sprk_primaryrole</c> value→label map (task-001 schema contract — the authoritative
    /// value↔label pairing regardless of the global/local binding, finding F-1). An unknown/future value
    /// maps to null (fail-safe — never a guessed label).
    /// </summary>
    private static readonly IReadOnlyDictionary<int, string> PrimaryRoleLabels = new Dictionary<int, string>
    {
        [100000000] = "Senior Partner",
        [100000001] = "Partner",
        [100000002] = "Senior Associate",
        [100000003] = "Associate",
        [100000004] = "Paralegal",
        [100000005] = "Specialist",
        [100000006] = "Other",
        [100000007] = "Practice Support",
        [100000008] = "Administrator",
        [100000009] = "Legal Operations",
        [100000010] = "Senior Counsel",
        [100000011] = "General Counsel",
        [100000012] = "Counsel",
        [100000013] = "Associate General Counsel",
    };

    private readonly IDataverseService _dataverse;
    private readonly ILogger<StatedProfileReader> _logger;

    public StatedProfileReader(IDataverseService dataverse, ILogger<StatedProfileReader> logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<StatedProfile?> ReadAsync(string systemUserId, CancellationToken ct)
    {
        if (!Guid.TryParse(systemUserId, out var systemUserGuid) || systemUserGuid == Guid.Empty)
        {
            return null;
        }

        try
        {
            var profileQuery = new QueryExpression(ProfileEntity)
            {
                ColumnSet = new ColumnSet(
                    ProfileIdColumn,
                    PrimaryRoleColumn,
                    FocusAreasColumn,
                    OfficeLocationColumn,
                    AssistantPreferencesColumn),
                TopCount = 1,
                NoLock = true,
            };
            profileQuery.Criteria.AddCondition(SystemUserColumn, ConditionOperator.Equal, systemUserGuid);

            var profileResult = await _dataverse.RetrieveMultipleAsync(profileQuery, ct).ConfigureAwait(false);
            var profileRow = profileResult?.Entities is { Count: > 0 } ? profileResult.Entities[0] : null;
            if (profileRow is null || profileRow.Id == Guid.Empty)
            {
                // No profile row for this user — everyday default; the stated-profile fragment is absent.
                return null;
            }

            var practiceAreaNames = await ReadPracticeAreaNamesAsync(profileRow.Id, ct).ConfigureAwait(false);

            return new StatedProfile
            {
                RoleLabel = MapRoleLabel(profileRow.GetAttributeValue<OptionSetValue>(PrimaryRoleColumn)),
                PracticeAreaNames = practiceAreaNames,
                FocusAreas = profileRow.GetAttributeValue<string>(FocusAreasColumn),
                OfficeLocation = profileRow.GetAttributeValue<string>(OfficeLocationColumn),
                AssistantPreferences = profileRow.GetAttributeValue<string>(AssistantPreferencesColumn),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Soft-fail-to-null (ADR-032 P2 quiet no-op): any Dataverse read error degrades the
            // stated-profile fragment to absent — a bind is NEVER taken down by the profile read.
            _logger.LogWarning(ex,
                "StatedProfileReader: sprk_userprofile read failed for the resolved caller — stated-profile " +
                "fragment degrades to absent (soft-fail; a bind is never taken down by the profile).");
            return null;
        }
    }

    /// <summary>
    /// Reads the related <c>sprk_practicearea_ref.sprk_practiceareaname</c> values for the profile via the
    /// N:N intersect (<c>sprk_userprofile_sprk_practicearea_ref</c>). Names are returned in a deterministic
    /// ordinal order so the rendered block is stable turn-to-turn.
    /// </summary>
    private async Task<IReadOnlyList<string>> ReadPracticeAreaNamesAsync(Guid profileId, CancellationToken ct)
    {
        var query = new QueryExpression(PracticeAreaEntity)
        {
            ColumnSet = new ColumnSet(PracticeAreaNameColumn),
            NoLock = true,
        };

        // Link sprk_practicearea_ref → intersect on the shared FK, filtered to this profile's rows.
        var link = query.AddLink(IntersectEntity, PracticeAreaIdColumn, PracticeAreaIdColumn);
        link.LinkCriteria.AddCondition(ProfileIdColumn, ConditionOperator.Equal, profileId);

        var result = await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        if (result?.Entities is null || result.Entities.Count == 0)
        {
            return Array.Empty<string>();
        }

        return result.Entities
            .Select(e => e.GetAttributeValue<string>(PracticeAreaNameColumn))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private static string? MapRoleLabel(OptionSetValue? role) =>
        role is not null && PrimaryRoleLabels.TryGetValue(role.Value, out var label) ? label : null;
}

/// <summary>
/// ADR-032 Null-Object default for <see cref="IStatedProfileReader"/> — used by <see cref="ContextBinder"/>
/// when no real reader is DI-registered (or a caller/test constructs <see cref="ContextBinder"/> directly
/// without supplying one). Always returns <c>null</c> (P2 quiet no-op) so the stated-profile fragment
/// degrades to absent rather than the Binder throwing.
/// </summary>
public sealed class NullStatedProfileReader : IStatedProfileReader
{
    /// <summary>Shared stateless instance (the reader holds no per-call state).</summary>
    public static readonly NullStatedProfileReader Instance = new();

    public Task<StatedProfile?> ReadAsync(string systemUserId, CancellationToken ct) =>
        Task.FromResult<StatedProfile?>(null);
}
