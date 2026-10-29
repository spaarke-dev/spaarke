using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Services.Ontology;

/// <summary>
/// The Ontology admin Classification area (ontology platform R1 task 103; D-22, FR-59, D-49): the triage categories with their guidance,
/// On/Off, the policy versions that read each, the 30-day volume and the stored recall, and the three writes (create, edit, On/Off). EVERY
/// read and write runs AS THE CALLER through <see cref="IDataverseUserClient"/>, so Dataverse enforces the Spaarke Ontology Administrator
/// role on each one (<c>prvCreate/Write/Readsprk_TriageCategory</c>) and the audit names the human. This class never consults the
/// capability probe's answer (<c>OntologyAdminCallerBoundaryGuardTests</c>, rule A).
/// </summary>
/// <remarks>
/// <para><b>A category edit is live on the next enrichment.</b> The classifier reads <c>sprk_triagecategory</c> (enabled rows, name and
/// guidance) on every classification (task 072, <c>LookupChoicesResolver</c>), so a save here changes behaviour with no deployment. That is
/// the point of the area, and the reason for the three rules below.</para>
/// <list type="number">
/// <item><b>A new category saves Off.</b> The create body has no <c>enabled</c> member and the row is written with
/// <c>sprk_enabled = false</c>; an administrator turns it On with the separate On/Off route once its guidance reads right.</item>
/// <item><b>A rename is refused while any policy version reads the category.</b> A rule body names a category by id, so a rename does not
/// break an evaluation; it does change the name the classifier is held to (the constrained-decoding enum, NFR-09) and the words the rule
/// is read back in. The check scans EVERY policy version's body (current, draft and superseded) of EVERY policy the caller can read, with
/// no domain filter: a billing-domain policy that reads a category blocks the rename like any other (the owner's rule, 2026-10-16). A rule
/// reads a category by its id or by a string value equal to its name. When the versions cannot be read the rename is refused too (503):
/// an unchecked rename is never allowed. The check is also refused (409 <c>rename_unverifiable</c>) unless the caller's read of the
/// policies and their versions covers every business unit (Global depth, or Deep at the root business unit): a caller who cannot see a
/// version cannot be told no rule reads the category. A rename is also refused (409 <c>rename_routed</c>) while the routing
/// configuration (<c>Communication:CategoryRouting</c>, keyed by category name) names the category.</item>
/// <item><b>An edit is checked against the row it was made from.</b> The write is conditional on the row's <c>versionnumber</c> (the
/// version the BFF just read, and the version the form loaded when it sent one), so a concurrent edit is a 409 and nothing is written.</item>
/// </list>
/// <para><b>What the caller sees.</b> The references and the 30-day volume are what the CALLER can read. The administrator role reads
/// policies at Deep depth and communications by their ordinary access, so a policy owned outside the administrator's business-unit tree
/// is not counted. This is the cost of the owner's rule that admin reads run as the caller (D-22); it is recorded in
/// <c>notes/103-ontology-admin-classification.md</c>.</para>
/// <para><b>Placement (CLAUDE.md section 10).</b> A static class like the capability probe: it holds no state and registers nothing in
/// DI. It reaches Dataverse only through the <see cref="IDataverseUserClient"/> its caller passes. Logs carry ids, status codes and counts
/// only (ADR-015), never a category name or its guidance.</para>
/// </remarks>
internal static class OntologyAdminClassificationService
{
    internal const string CategorySet = "sprk_triagecategories";
    private const string PolicySet = "sprk_policies";
    private const string PolicyVersionSet = "sprk_policyversions";
    private const string CommunicationSet = "sprk_communications";

    /// <summary>The window of the volume figure.</summary>
    internal const int VolumeWindowDays = 30;

    /// <summary>The floor a measured recall is judged against: 80 percent on a labelled set of at least 50 items (D-10, D-49).</summary>
    internal static readonly ClassificationRecallFloor RecallFloor = new(Percent: 80, MinItems: 50);

    /// <summary>The longest category name accepted. The name becomes a value the classifier is held to, so it stays short (the column allows far more).</summary>
    internal const int MaxNameLength = 100;

    /// <summary><c>sprk_classifierguidance</c> is a 2000-character column.</summary>
    internal const int MaxGuidanceLength = 2000;

    private const int MaxPages = 20;
    private const string RowColumns =
        "sprk_triagecategoryid,sprk_name,sprk_classifierguidance,sprk_enabled,versionnumber,statecode,modifiedon,"
        + "sprk_measuredrecall,sprk_labelledsetsize,sprk_recallmeasuredon";

    // ───────────────────────────────────────────────────────────────────────────────────────────────────────────
    // Read
    // ───────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The list: every active category with its guidance, On/Off, readers, 30-day volume and recall against the floor.</summary>
    internal static async Task<ClassificationOperationResult<ClassificationList>> ListAsync(
        IDataverseUserClient user, ILogger logger, CancellationToken ct)
    {
        var categories = await ReadAllAsync(user, $"{CategorySet}?$select={RowColumns}&$filter=statecode eq 0&$orderby=sprk_name asc", logger, ct)
            .ConfigureAwait(false);
        if (categories.Failure is not null)
        {
            return Fail<ClassificationList>(categories.Failure, write: false);
        }

        var rows = categories.Rows.Select(ParseRow).Where(r => r is not null).Select(r => r!).ToList();

        var references = await LoadReferencesAsync(user, NamesById(rows), logger, ct).ConfigureAwait(false);
        var volume = await LoadVolumeAsync(user, logger, ct).ConfigureAwait(false);

        var list = rows
            .Select(r => new ClassificationCategory(
                r.Id,
                r.Name,
                r.Guidance,
                r.Enabled,
                r.Version,
                volume is null ? null : volume.GetValueOrDefault(r.Id),
                BuildRecall(r.Recall, r.SetSize, r.MeasuredOn, r.ModifiedOn),
                references is not null && references.TryGetValue(r.Id, out var readBy) ? readBy : []))
            .ToList();

        return new ClassificationOperationResult<ClassificationList>(
            ClassificationOutcome.Ok,
            new ClassificationList(list, RecallFloor, VolumeWindowDays, VolumeAvailable: volume is not null, ReferencesAvailable: references is not null));
    }

    /// <summary>
    /// A stored recall measurement judged against the floor (D-49). No recall means <c>NotMeasured</c> with no figures: a category is
    /// never shown as zero. Recall below the floor percent is <c>BelowFloor</c>; at or above it, the figure counts only when the labelled
    /// set is at least the floor size (otherwise <c>SetTooSmall</c>).
    /// </summary>
    internal static ClassificationRecall BuildRecall(decimal? recall, int? labelledSetSize, string? measuredOn, string? modifiedOn = null)
    {
        if (recall is null)
        {
            return new ClassificationRecall(ClassificationRecallStatus.NotMeasured, null, null, null);
        }

        var status = recall.Value < RecallFloor.Percent
            ? ClassificationRecallStatus.BelowFloor
            : (labelledSetSize ?? 0) >= RecallFloor.MinItems ? ClassificationRecallStatus.MeetsFloor : ClassificationRecallStatus.SetTooSmall;
        return new ClassificationRecall(status, recall, labelledSetSize, measuredOn, EditedAfter(modifiedOn, measuredOn));
    }

    /// <summary>
    /// True when the row was modified (UTC) on a later DAY than the recall was measured. Day granularity on purpose: the measurement
    /// writes the recall columns onto the same row, which moves <c>modifiedon</c> on the measuring day, so a same-day edit cannot be told
    /// from the measurement itself. False when either date cannot be read.
    /// </summary>
    internal static bool EditedAfter(string? modifiedOn, string? measuredOn)
    {
        if (string.IsNullOrWhiteSpace(modifiedOn)
            || string.IsNullOrWhiteSpace(measuredOn)
            || !DateOnly.TryParseExact(measuredOn.Length >= 10 ? measuredOn[..10] : measuredOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var measured)
            || !DateTimeOffset.TryParse(modifiedOn, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var modified))
        {
            return false;
        }

        return DateOnly.FromDateTime(modified.UtcDateTime) > measured;
    }

    private static Dictionary<Guid, string> NamesById(IEnumerable<CategoryRow> rows)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var row in rows)
        {
            names[row.Id] = row.Name;
        }

        return names;
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────────────────
    // Writes
    // ───────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Creates a category, always Off.</summary>
    internal static async Task<ClassificationOperationResult<ClassificationRow>> CreateAsync(
        IDataverseUserClient user, CreateClassificationCategoryRequest? request, ILogger logger, CancellationToken ct)
    {
        var existing = await ReadAllAsync(user, $"{CategorySet}?$select=sprk_triagecategoryid,sprk_name&$filter=statecode eq 0", logger, ct)
            .ConfigureAwait(false);
        if (existing.Failure is not null)
        {
            return Fail<ClassificationRow>(existing.Failure, write: true);
        }

        var errors = Validate(request?.Name, request?.Guidance, nameRequired: true, out var name, out var guidance);
        if (request is null)
        {
            errors["body"] = ["A request body is required."];
        }

        if (errors.Count > 0)
        {
            return Invalid<ClassificationRow>(errors);
        }

        if (NameInUse(existing.Rows, name!, except: null))
        {
            return new ClassificationOperationResult<ClassificationRow>(
                ClassificationOutcome.NameTaken, Detail: "Another triage category already has this name.");
        }

        var body = new Dictionary<string, object?> { ["sprk_name"] = name, ["sprk_enabled"] = false };
        if (guidance is not null)
        {
            body["sprk_classifierguidance"] = guidance;
        }

        var created = await CallAsync(
            () => user.PostAsync($"/api/data/v9.2/{CategorySet}", JsonSerializer.Serialize(body), preferRepresentation: true, ct), logger, ct)
            .ConfigureAwait(false);
        if (!created.IsSuccess)
        {
            return Fail<ClassificationRow>(created, write: true);
        }

        var row = created.Body is { } json ? ParseRow(json) : null;
        if (row is null)
        {
            // The write is done; a body-less success is read back by name rather than reported as a failure.
            var reread = await ReadAllAsync(user, $"{CategorySet}?$select={RowColumns}&$filter=statecode eq 0", logger, ct).ConfigureAwait(false);
            row = reread.Failure is null
                ? reread.Rows.Select(ParseRow).FirstOrDefault(r => r is not null && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
                : null;
        }

        return row is null
            ? new ClassificationOperationResult<ClassificationRow>(
                ClassificationOutcome.Unavailable, Detail: "The category was created but could not be read back. Reload the list.")
            : new ClassificationOperationResult<ClassificationRow>(ClassificationOutcome.Created, ToWriteRow(row));
    }

    /// <summary>Edits a category's name and/or guidance. A rename is refused while a policy version reads the category.</summary>
    internal static async Task<ClassificationOperationResult<ClassificationRow>> UpdateAsync(
        IDataverseUserClient user,
        Guid id,
        UpdateClassificationCategoryRequest? request,
        IOptionsMonitor<CategoryRoutingOptions> routing,
        ILogger logger,
        CancellationToken ct)
    {
        var loaded = await LoadRowAsync(user, id, logger, ct).ConfigureAwait(false);
        if (loaded.Failure is not null)
        {
            return loaded.Failure;
        }

        var current = loaded.Row!;
        if (request is null)
        {
            return Invalid<ClassificationRow>(new Dictionary<string, string[]> { ["body"] = ["A request body is required."] });
        }

        if (request.Version is { } sent && sent != current.Version)
        {
            return Stale<ClassificationRow>();
        }

        var errors = Validate(request.Name, request.Guidance, nameRequired: false, out var name, out var guidance);
        if (errors.Count > 0)
        {
            return Invalid<ClassificationRow>(errors);
        }

        var renamed = name is not null && !string.Equals(name, current.Name, StringComparison.Ordinal);
        var guidanceChanged = request.Guidance is not null && !string.Equals(guidance ?? string.Empty, current.Guidance ?? string.Empty, StringComparison.Ordinal);

        if (renamed)
        {
            var blocked = await CheckRenameAsync(user, current, name!, routing, logger, ct).ConfigureAwait(false);
            if (blocked is not null)
            {
                return blocked;
            }
        }

        if (!renamed && !guidanceChanged)
        {
            return new ClassificationOperationResult<ClassificationRow>(ClassificationOutcome.Ok, ToWriteRow(current));
        }

        var body = new Dictionary<string, object?>();
        if (renamed)
        {
            body["sprk_name"] = name;
        }

        if (guidanceChanged)
        {
            body["sprk_classifierguidance"] = guidance;
        }

        return await PatchAsync(user, id, body, current, logger, ct).ConfigureAwait(false);
    }

    /// <summary>Turns a category On or Off.</summary>
    internal static async Task<ClassificationOperationResult<ClassificationRow>> SetEnabledAsync(
        IDataverseUserClient user, Guid id, SetClassificationCategoryEnabledRequest? request, ILogger logger, CancellationToken ct)
    {
        var loaded = await LoadRowAsync(user, id, logger, ct).ConfigureAwait(false);
        if (loaded.Failure is not null)
        {
            return loaded.Failure;
        }

        var current = loaded.Row!;
        if (request?.Enabled is not { } enabled)
        {
            return Invalid<ClassificationRow>(new Dictionary<string, string[]> { ["enabled"] = ["Enabled must be true or false."] });
        }

        if (request.Version is { } sent && sent != current.Version)
        {
            return Stale<ClassificationRow>();
        }

        if (enabled == current.Enabled)
        {
            return new ClassificationOperationResult<ClassificationRow>(ClassificationOutcome.Ok, ToWriteRow(current));
        }

        if (enabled)
        {
            // Turning a category On offers its name to the classifier. Two active rows with one name (made outside this area, or by two
            // creates racing) would put a duplicate in the constrained-decoding enum, so the name-in-use check runs again here (K2).
            var names = await ReadAllAsync(user, $"{CategorySet}?$select=sprk_triagecategoryid,sprk_name&$filter=statecode eq 0", logger, ct)
                .ConfigureAwait(false);
            if (names.Failure is not null)
            {
                return Fail<ClassificationRow>(names.Failure, write: true);
            }

            if (NameInUse(names.Rows, current.Name.Trim(), except: current.Id))
            {
                return new ClassificationOperationResult<ClassificationRow>(
                    ClassificationOutcome.NameTaken, Detail: "Another triage category already has this name, so this one cannot be turned On.");
            }
        }

        return await PatchAsync(user, id, new Dictionary<string, object?> { ["sprk_enabled"] = enabled }, current, logger, ct).ConfigureAwait(false);
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────────────────
    // Rename check
    // ───────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Null when the rename may proceed. Otherwise the refusal: the routing configuration is keyed by the name, the caller's read cannot
    /// show every policy version (409 unverifiable), the name is taken, a policy version reads the category (the blockers are named), or
    /// the versions could not be read (503, because an unchecked rename is never allowed).
    /// </summary>
    private static async Task<ClassificationOperationResult<ClassificationRow>?> CheckRenameAsync(
        IDataverseUserClient user,
        CategoryRow current,
        string newName,
        IOptionsMonitor<CategoryRoutingOptions> routing,
        ILogger logger,
        CancellationToken ct)
    {
        // In process and free, so it runs first: the routing configuration is keyed by category NAME (CategoryRoutingOptions), and a rename
        // would silently orphan the entry.
        if (RoutingEntryNaming(routing.CurrentValue, current.Name) is { } entry)
        {
            return new ClassificationOperationResult<ClassificationRow>(
                ClassificationOutcome.RenameRouted,
                Detail: $"This category cannot be renamed while the routing configuration names it ({entry}). Change that entry first.");
        }

        // The reference read below runs as the caller, and Dataverse trims it to what the caller can read. Unless that covers EVERY policy
        // version, "no rule reads this category" is not an answer, so the rename is refused rather than waved through (F2).
        var refusal = await CheckReadCoverageAsync(user, logger, ct).ConfigureAwait(false);
        if (refusal is not null)
        {
            return refusal;
        }

        var references = await LoadReferencesAsync(user, new Dictionary<Guid, string> { [current.Id] = current.Name }, logger, ct).ConfigureAwait(false);
        if (references is null)
        {
            return new ClassificationOperationResult<ClassificationRow>(
                ClassificationOutcome.Unavailable,
                Detail: "Which rules read this category could not be checked, so it cannot be renamed now. Try again.");
        }

        if (references.TryGetValue(current.Id, out var blockers) && blockers.Count > 0)
        {
            return new ClassificationOperationResult<ClassificationRow>(
                ClassificationOutcome.RenameBlocked,
                Blockers: blockers,
                Detail: $"This category cannot be renamed while a rule reads it: {DescribeBlockers(blockers)}.");
        }

        var names = await ReadAllAsync(user, $"{CategorySet}?$select=sprk_triagecategoryid,sprk_name&$filter=statecode eq 0", logger, ct)
            .ConfigureAwait(false);
        if (names.Failure is not null)
        {
            return Fail<ClassificationRow>(names.Failure, write: true);
        }

        return NameInUse(names.Rows, newName, except: current.Id)
            ? new ClassificationOperationResult<ClassificationRow>(
                ClassificationOutcome.NameTaken, Detail: "Another triage category already has this name.")
            : null;
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────────────────
    // Routing configuration (keyed by category name)
    // ───────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Where the routing configuration names <paramref name="categoryName"/>, or null. Looks at the global map and at every per-tenant map
    /// (the consumer, <c>CategoryRoutingGate</c>, uses one or the other per tenant), whatever the on/off switch says: a dormant entry
    /// would break the moment routing is switched on. Keys are matched trimmed and case-insensitively, a superset of the gate's lookup.
    /// </summary>
    internal static string? RoutingEntryNaming(CategoryRoutingOptions options, string categoryName)
    {
        var wanted = categoryName.Trim();
        if (Names(options.CategoryToTeam, wanted))
        {
            return $"{CategoryRoutingOptions.SectionName}:CategoryToTeam";
        }

        foreach (var (tenant, overrideEntry) in options.Tenants)
        {
            if (Names(overrideEntry?.CategoryToTeam, wanted))
            {
                return $"{CategoryRoutingOptions.SectionName}:Tenants:{tenant}:CategoryToTeam";
            }
        }

        return null;

        static bool Names(Dictionary<string, string>? map, string wanted) =>
            map is not null && map.Keys.Any(k => string.Equals(k?.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────────────────
    // Read coverage: can the caller's own read show every policy version?
    // ───────────────────────────────────────────────────────────────────────────────────────────────────────────

    private const string PolicyReadPrivilegeName = "prvReadsprk_Policy";
    private const string VersionReadPrivilegeName = "prvReadsprk_PolicyVersion";

    /// <summary>The depth a privilege is held at, lowest to highest.</summary>
    private enum ReadReach
    {
        Basic = 1,
        Local = 2,
        Deep = 3,
        Global = 4,
    }

    /// <summary>
    /// One RolePrivilege row of <c>RetrieveUserPrivilegeByPrivilegeName</c>: the depth, the business unit of the role copy that grants it
    /// (Deep reaches only the subtree of THAT unit, which for a team role is the team's unit), and whether the grant is unconditional
    /// (no <c>RecordFilterId</c>; a conditional privilege limits rows even at Global).
    /// </summary>
    private sealed record PrivilegeGrant(ReadReach Reach, Guid? BusinessUnitId, bool Unconditional);

    /// <summary>
    /// Null when the caller's read of the policies AND of their versions covers every business unit, row by row: a privilege is covered when
    /// some row that grants it is unconditional and is either Global, or Deep on the ROOT business unit (Deep at the root reaches every unit
    /// under it). The maximum depth over the rows is not enough: a Deep grant on a child unit's team role reaches only that subtree.
    /// Otherwise the refusal: 409 <c>rename_unverifiable</c> when the coverage is incomplete, 503 when it could not be established, and the
    /// single 403 when the caller cannot be named. Reads as the caller: <c>WhoAmI()</c>, the privilege function once per privilege and, only
    /// when a Deep grant is the deciding row, the root business unit (once).
    /// </summary>
    private static async Task<ClassificationOperationResult<ClassificationRow>?> CheckReadCoverageAsync(
        IDataverseUserClient user, ILogger logger, CancellationToken ct)
    {
        var who = await CallAsync(() => user.GetAsync("WhoAmI()", ct), logger, ct).ConfigureAwait(false);
        if (!who.IsSuccess
            || who.Body is not { ValueKind: JsonValueKind.Object } whoBody
            || ReadGuid(whoBody, "UserId") is not { } userId)
        {
            return CoverageFault(who, "the caller could not be identified", logger);
        }

        var policies = await ReadGrantsAsync(user, userId, PolicyReadPrivilegeName, logger, ct).ConfigureAwait(false);
        if (policies.Failure is not null)
        {
            return policies.Failure;
        }

        var versions = await ReadGrantsAsync(user, userId, VersionReadPrivilegeName, logger, ct).ConfigureAwait(false);
        if (versions.Failure is not null)
        {
            return versions.Failure;
        }

        Guid? rootUnit = null;
        foreach (var grants in new[] { policies.Grants, versions.Grants })
        {
            if (grants.Any(g => g.Unconditional && g.Reach == ReadReach.Global))
            {
                continue;
            }

            // Only a Deep grant needs the root unit, and only the first such grant reads it.
            if (!grants.Any(g => g.Unconditional && g.Reach == ReadReach.Deep && g.BusinessUnitId is not null))
            {
                return Unverifiable();
            }

            if (rootUnit is null)
            {
                var root = await ReadRootBusinessUnitAsync(user, logger, ct).ConfigureAwait(false);
                if (root.Failure is not null)
                {
                    return root.Failure;
                }

                rootUnit = root.Id;
            }

            if (!grants.Any(g => g.Unconditional && g.Reach == ReadReach.Deep && g.BusinessUnitId == rootUnit))
            {
                return Unverifiable();
            }
        }

        return null;

        static ClassificationOperationResult<ClassificationRow> Unverifiable() => new(
            ClassificationOutcome.RenameUnverifiable,
            Detail: "Whether a rule reads this category cannot be checked from your business unit, so it cannot be renamed here. "
                + "Ask an administrator whose access covers every business unit to rename it.");
    }

    private sealed record GrantsRead(IReadOnlyList<PrivilegeGrant> Grants, ClassificationOperationResult<ClassificationRow>? Failure);

    /// <summary>Every row that grants one privilege to the caller (the function lists one row per role that grants it).</summary>
    private static async Task<GrantsRead> ReadGrantsAsync(
        IDataverseUserClient user, Guid userId, string privilegeName, ILogger logger, CancellationToken ct)
    {
        var response = await CallAsync(
            () => user.GetAsync($"systemusers({userId:D})/Microsoft.Dynamics.CRM.RetrieveUserPrivilegeByPrivilegeName(PrivilegeName='{privilegeName}')", ct),
            logger,
            ct).ConfigureAwait(false);
        if (!response.IsSuccess
            || response.Body is not { ValueKind: JsonValueKind.Object } body
            || !body.TryGetProperty("RolePrivileges", out var rows)
            || rows.ValueKind != JsonValueKind.Array)
        {
            return new GrantsRead([], CoverageFault(response, "the caller's read depth could not be read", logger));
        }

        var grants = new List<PrivilegeGrant>();
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object
                || !string.Equals(ReadString(row, "PrivilegeName"), privilegeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // A depth that cannot be read counts as the lowest, a unit that cannot be read as none, a filter that cannot be read as a
            // condition: each can only make the check stricter.
            var depth = row.TryGetProperty("Depth", out var d) ? ParseDepth(d) : ReadReach.Basic;
            grants.Add(new PrivilegeGrant(depth, ReadGuid(row, "BusinessUnitId"), HasNoRecordFilter(row)));
        }

        return new GrantsRead(grants, null);
    }

    /// <summary>
    /// True when the row carries no record filter: <c>RecordFilterId</c> absent, null or the all-zero GUID. A filter that is present but not
    /// a GUID counts as a filter. The conditional-privilege semantics are inferred (not confirmed on Microsoft Learn); this fails closed.
    /// </summary>
    private static bool HasNoRecordFilter(JsonElement row)
    {
        if (!row.TryGetProperty("RecordFilterId", out var filter) || filter.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        return filter.ValueKind == JsonValueKind.String && Guid.TryParse(filter.GetString(), out var id) && id == Guid.Empty;
    }

    private sealed record RootUnitRead(Guid Id, ClassificationOperationResult<ClassificationRow>? Failure);

    /// <summary>The one business unit with no parent, read as the caller. Anything but exactly one row is an unexpected shape (503).</summary>
    private static async Task<RootUnitRead> ReadRootBusinessUnitAsync(IDataverseUserClient user, ILogger logger, CancellationToken ct)
    {
        var response = await CallAsync(
            () => user.GetAsync("businessunits?$select=businessunitid&$filter=_parentbusinessunitid_value eq null", ct), logger, ct).ConfigureAwait(false);
        if (!response.IsSuccess
            || response.Body is not { ValueKind: JsonValueKind.Object } body
            || !body.TryGetProperty("value", out var rows)
            || rows.ValueKind != JsonValueKind.Array
            || rows.GetArrayLength() != 1
            || ReadGuid(rows[0], "businessunitid") is not { } id)
        {
            return new RootUnitRead(Guid.Empty, CoverageFault(response, "the root business unit could not be read", logger));
        }

        return new RootUnitRead(id, null);
    }

    /// <summary>Dataverse names the depth ("Basic", "Local", "Deep", "Global"); the numeric form (0 to 3) is read too. Anything else is Basic.</summary>
    private static ReadReach ParseDepth(JsonElement depth)
    {
        if (depth.ValueKind == JsonValueKind.String)
        {
            return depth.GetString()?.Trim().ToUpperInvariant() switch
            {
                "GLOBAL" => ReadReach.Global,
                "DEEP" => ReadReach.Deep,
                "LOCAL" => ReadReach.Local,
                _ => ReadReach.Basic,
            };
        }

        if (depth.ValueKind == JsonValueKind.Number && depth.TryGetInt32(out var n))
        {
            return n switch
            {
                3 => ReadReach.Global,
                2 => ReadReach.Deep,
                1 => ReadReach.Local,
                _ => ReadReach.Basic,
            };
        }

        return ReadReach.Basic;
    }

    /// <summary>
    /// A coverage read that did not give an answer: the single 403 for a caller Dataverse cannot name, otherwise a 503 (the rename is
    /// refused, never allowed unchecked). A 200 whose body is not the expected shape is a 503 too. Logs a code, never a value (ADR-015).
    /// </summary>
    private static ClassificationOperationResult<ClassificationRow> CoverageFault(DataverseUserResponse response, string what, ILogger logger)
    {
        if (!response.IsSuccess
            && (response.StatusCode == 401
                || response.ErrorCode is DataverseUserClientErrorCodes.UserContextRequired
                    or DataverseUserClientErrorCodes.OboExchangeFailed
                    or DataverseUserClientErrorCodes.OboNotConfigured))
        {
            return new ClassificationOperationResult<ClassificationRow>(ClassificationOutcome.CallerUnresolved);
        }

        logger.LogWarning("Ontology admin classification: {What} ({ErrorCode}); a rename is refused as unavailable.", what, response.ErrorCode ?? "unparseable-body");
        return new ClassificationOperationResult<ClassificationRow>(
            ClassificationOutcome.Unavailable,
            Detail: "Which rules read this category could not be checked, so it cannot be renamed now. Try again.");
    }

    /// <summary>"POL-COMMIT-BUDGET (version 2, in force), POL-X (version 1, earlier)": every distinct rule and version, in a stable order.</summary>
    internal static string DescribeBlockers(IReadOnlyList<ClassificationRuleReference> blockers) =>
        string.Join(
            ", ",
            blockers
                .OrderBy(b => b.PolicyCode, StringComparer.Ordinal)
                .ThenByDescending(b => b.VersionNumber)
                .Select(b => $"{b.PolicyCode} (version {(b.VersionNumber is { } n ? n.ToString(CultureInfo.InvariantCulture) : "?")}, {StateWords(b.VersionState)})"));

    private static string StateWords(string state) => state switch
    {
        ClassificationVersionState.InForce => "in force",
        ClassificationVersionState.Draft => "draft",
        _ => "earlier version",
    };

    // ───────────────────────────────────────────────────────────────────────────────────────────────────────────
    // References: which policy versions read which category
    // ───────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// For each category in <paramref name="categories"/> (id to current name), the policy versions whose rule body names it by id or by
    /// name; null when the policies or versions could not be read. Scans EVERY version of EVERY policy the caller can read, whatever its
    /// state, enabled flag or domain.
    /// </summary>
    private static async Task<Dictionary<Guid, List<ClassificationRuleReference>>?> LoadReferencesAsync(
        IDataverseUserClient user, IReadOnlyDictionary<Guid, string> categories, ILogger logger, CancellationToken ct)
    {
        var policies = await ReadAllAsync(
            user, $"{PolicySet}?$select=sprk_policyid,sprk_policycode,sprk_name,sprk_enabled,_sprk_currentversion_value", logger, ct).ConfigureAwait(false);
        if (policies.Failure is not null)
        {
            logger.LogWarning("Ontology admin classification: the policies could not be read ({ErrorCode}); references unavailable.", policies.Failure.ErrorCode);
            return null;
        }

        var versions = await ReadAllAsync(
            user,
            $"{PolicyVersionSet}?$select=sprk_policyversionid,sprk_versionnumber,sprk_rulebody,sprk_inforceto,_sprk_policy_value",
            logger,
            ct).ConfigureAwait(false);
        if (versions.Failure is not null)
        {
            logger.LogWarning("Ontology admin classification: the policy versions could not be read ({ErrorCode}); references unavailable.", versions.Failure.ErrorCode);
            return null;
        }

        var policyById = new Dictionary<Guid, PolicyRow>();
        foreach (var row in policies.Rows)
        {
            if (ReadGuid(row, "sprk_policyid") is { } policyId)
            {
                policyById[policyId] = new PolicyRow(
                    ReadString(row, "sprk_policycode") ?? "(no code)",
                    ReadString(row, "sprk_name"),
                    ReadBool(row, "sprk_enabled"),
                    ReadGuid(row, "_sprk_currentversion_value"));
            }
        }

        var result = new Dictionary<Guid, List<ClassificationRuleReference>>();
        foreach (var row in versions.Rows)
        {
            if (ReadGuid(row, "sprk_policyversionid") is not { } versionId)
            {
                continue;
            }

            var named = OntologyAdminClassificationReferences.CategoriesNamedIn(ReadString(row, "sprk_rulebody"), categories);
            if (named.Count == 0)
            {
                continue;
            }

            var policyId = ReadGuid(row, "_sprk_policy_value") ?? Guid.Empty;
            policyById.TryGetValue(policyId, out var policy);
            var inforceTo = ReadString(row, "sprk_inforceto");
            var state = policy?.CurrentVersionId == versionId
                ? ClassificationVersionState.InForce
                : string.IsNullOrEmpty(inforceTo) ? ClassificationVersionState.Draft : ClassificationVersionState.Earlier;
            var reference = new ClassificationRuleReference(
                policyId,
                policy?.Code ?? "(unreadable policy)",
                policy?.Name,
                policy?.Enabled ?? false,
                versionId,
                ReadInt(row, "sprk_versionnumber"),
                state);

            foreach (var categoryId in named)
            {
                if (!result.TryGetValue(categoryId, out var list))
                {
                    result[categoryId] = list = [];
                }

                list.Add(reference);
            }
        }

        foreach (var list in result.Values)
        {
            list.Sort((a, b) => string.CompareOrdinal(a.PolicyCode, b.PolicyCode) is var c and not 0 ? c : Nullable.Compare(b.VersionNumber, a.VersionNumber));
        }

        return result;
    }

    private sealed record PolicyRow(string Code, string? Name, bool Enabled, Guid? CurrentVersionId);

    // ───────────────────────────────────────────────────────────────────────────────────────────────────────────
    // Volume
    // ───────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Communications classified into each category in the last 30 days, among those the caller can read (one aggregate FetchXML as the
    /// caller). Null when the count could not be read (Dataverse caps an aggregate at 50,000 rows). A category with no communications has
    /// no entry, which the caller reads as zero; unclassified communications (no category) are not counted.
    /// </summary>
    private static async Task<Dictionary<Guid, int>?> LoadVolumeAsync(IDataverseUserClient user, ILogger logger, CancellationToken ct)
    {
        var fetch =
            "<fetch aggregate='true'><entity name='sprk_communication'>"
            + "<attribute name='sprk_communicationid' alias='n' aggregate='count'/>"
            + "<attribute name='sprk_triagecategory' alias='cat' groupby='true'/>"
            + $"<filter><condition attribute='sprk_receiveddate' operator='last-x-days' value='{VolumeWindowDays.ToString(CultureInfo.InvariantCulture)}'/></filter>"
            + "</entity></fetch>";

        var response = await CallAsync(() => user.GetAsync($"{CommunicationSet}?fetchXml={Uri.EscapeDataString(fetch)}", ct), logger, ct)
            .ConfigureAwait(false);
        if (!response.IsSuccess || response.Body is not { ValueKind: JsonValueKind.Object } body
            || !body.TryGetProperty("value", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            logger.LogWarning("Ontology admin classification: the 30-day volume could not be read ({ErrorCode}); shown as unavailable.",
                response.ErrorCode ?? "unparseable-body");
            return null;
        }

        var volume = new Dictionary<Guid, int>();
        foreach (var row in rows.EnumerateArray())
        {
            if (ReadGuid(row, "cat") is { } category && row.TryGetProperty("n", out var n) && n.TryGetInt32(out var count))
            {
                volume[category] = count;
            }
        }

        return volume;
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────────────────
    // Dataverse plumbing (all as the caller)
    // ───────────────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record CategoryRow(
        Guid Id, string Name, string? Guidance, bool Enabled, long Version, decimal? Recall, int? SetSize, string? MeasuredOn, string? ModifiedOn);

    private sealed record LoadedRow(CategoryRow? Row, ClassificationOperationResult<ClassificationRow>? Failure);

    private sealed record PagedRead(DataverseUserResponse? Failure, List<JsonElement> Rows);

    private static async Task<LoadedRow> LoadRowAsync(IDataverseUserClient user, Guid id, ILogger logger, CancellationToken ct)
    {
        var response = await CallAsync(() => user.GetAsync($"{CategorySet}({id:D})?$select={RowColumns}", ct), logger, ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            return new LoadedRow(null, Fail<ClassificationRow>(response, write: false));
        }

        var row = response.Body is { } json && ReadInt(json, "statecode") is null or 0 ? ParseRow(json) : null;
        return row is null
            ? new LoadedRow(null, new ClassificationOperationResult<ClassificationRow>(ClassificationOutcome.NotFound))
            : new LoadedRow(row, null);
    }

    private static async Task<ClassificationOperationResult<ClassificationRow>> PatchAsync(
        IDataverseUserClient user, Guid id, Dictionary<string, object?> body, CategoryRow current, ILogger logger, CancellationToken ct)
    {
        var patch = await CallAsync(
            () => user.PatchAsync($"{CategorySet}({id:D})", JsonSerializer.Serialize(body), current.Version, ct), logger, ct).ConfigureAwait(false);
        if (!patch.IsSuccess)
        {
            return Fail<ClassificationRow>(patch, write: true);
        }

        // The save is done. Read the row back for its new version; if that read fails the write still stands, and the list reload shows it.
        var after = await LoadRowAsync(user, id, logger, ct).ConfigureAwait(false);
        var row = after.Row ?? ApplyLocally(current, body);
        return new ClassificationOperationResult<ClassificationRow>(ClassificationOutcome.Ok, ToWriteRow(row));
    }

    private static CategoryRow ApplyLocally(CategoryRow row, Dictionary<string, object?> body) => row with
    {
        Name = body.TryGetValue("sprk_name", out var n) && n is string name ? name : row.Name,
        Guidance = body.TryGetValue("sprk_classifierguidance", out var g) ? g as string : row.Guidance,
        Enabled = body.TryGetValue("sprk_enabled", out var e) && e is bool enabled ? enabled : row.Enabled,
    };

    /// <summary>Every row of a collection read, following <c>@odata.nextLink</c> (capped); a failure on any page fails the read.</summary>
    private static async Task<PagedRead> ReadAllAsync(IDataverseUserClient user, string path, ILogger logger, CancellationToken ct)
    {
        var rows = new List<JsonElement>();
        var next = path;
        for (var page = 0; page < MaxPages && next is not null; page++)
        {
            var current = next;
            var response = await CallAsync(() => user.GetAsync(current, ct), logger, ct).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                return new PagedRead(response, rows);
            }

            if (response.Body is not { ValueKind: JsonValueKind.Object } body || !body.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
            {
                return new PagedRead(
                    DataverseUserResponse.Fail(response.StatusCode, DataverseUserClientErrorCodes.ServiceError, "Dataverse returned an unreadable list."), rows);
            }

            rows.AddRange(value.EnumerateArray());
            next = body.TryGetProperty("@odata.nextLink", out var link) && link.ValueKind == JsonValueKind.String ? RelativeOf(link.GetString()) : null;
            if (next is null && body.TryGetProperty("@odata.nextLink", out _))
            {
                return new PagedRead(
                    DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.ServiceError, "Dataverse returned a next page that cannot be followed."), rows);
            }
        }

        return next is null
            ? new PagedRead(null, rows)
            : new PagedRead(DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.ServiceError, "The list is larger than this route reads."), rows);
    }

    private static string? RelativeOf(string? nextLink)
    {
        const string marker = "/api/data/v9.2/";
        var at = nextLink?.IndexOf(marker, StringComparison.OrdinalIgnoreCase) ?? -1;
        return at < 0 ? null : nextLink![(at + marker.Length)..];
    }

    private static async Task<DataverseUserResponse> CallAsync(Func<Task<DataverseUserResponse>> call, ILogger logger, CancellationToken ct)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // A transport timeout is a TaskCanceledException while the request's own token is not cancelled: a fault, never a 500.
            logger.LogWarning("Ontology admin classification: a Dataverse call threw ({ExceptionType}); unavailable.", ex.GetType().Name);
            return DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.ServiceError, "The Dataverse call failed.");
        }
    }

    /// <summary>
    /// A failed Dataverse response as an outcome. The caller who cannot be named (no user context, OBO not configured or refused,
    /// Dataverse 401) is the single 403; Dataverse's own 403 is "not permitted" (a Console User); a 412 on a write is a concurrent change.
    /// </summary>
    private static ClassificationOperationResult<T> Fail<T>(DataverseUserResponse response, bool write)
    {
        ClassificationOutcome outcome;
        if (response.StatusCode == 401
            || response.ErrorCode is DataverseUserClientErrorCodes.UserContextRequired
                or DataverseUserClientErrorCodes.OboExchangeFailed
                or DataverseUserClientErrorCodes.OboNotConfigured)
        {
            outcome = ClassificationOutcome.CallerUnresolved;
        }
        else if (response.ErrorCode == DataverseUserClientErrorCodes.AccessDenied)
        {
            outcome = ClassificationOutcome.Forbidden;
        }
        else if (response.ErrorCode == DataverseUserClientErrorCodes.NotFound)
        {
            outcome = ClassificationOutcome.NotFound;
        }
        else if (write && response.StatusCode == 412)
        {
            outcome = ClassificationOutcome.Stale;
        }
        else if (write && response.ErrorCode == DataverseUserClientErrorCodes.BadRequest)
        {
            return new ClassificationOperationResult<T>(
                ClassificationOutcome.Invalid,
                Errors: new Dictionary<string, string[]> { ["category"] = ["Dataverse refused the change."] });
        }
        else
        {
            outcome = ClassificationOutcome.Unavailable;
        }

        return new ClassificationOperationResult<T>(outcome);
    }

    private static ClassificationOperationResult<T> Invalid<T>(IReadOnlyDictionary<string, string[]> errors) =>
        new(ClassificationOutcome.Invalid, Errors: errors);

    private static ClassificationOperationResult<T> Stale<T>() =>
        new(ClassificationOutcome.Stale, Detail: "This category was changed by someone else. Reload it and try again.");

    // ───────────────────────────────────────────────────────────────────────────────────────────────────────────
    // Validation and row reading
    // ───────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks and normalises the editable members. A name is trimmed, 1 to <see cref="MaxNameLength"/> characters, with no control
    /// characters (it becomes a value the classifier is held to). Guidance is trimmed, at most <see cref="MaxGuidanceLength"/>; blank
    /// guidance is null. A member left out (null) is not validated and comes back null.
    /// </summary>
    internal static Dictionary<string, string[]> Validate(
        string? rawName, string? rawGuidance, bool nameRequired, out string? name, out string? guidance)
    {
        var errors = new Dictionary<string, string[]>();
        name = rawName?.Trim();
        guidance = rawGuidance?.Trim();

        if (name is null)
        {
            if (nameRequired)
            {
                errors["name"] = ["Name is required."];
            }
        }
        else if (name.Length == 0)
        {
            errors["name"] = ["Name cannot be empty."];
        }
        else if (name.Length > MaxNameLength)
        {
            errors["name"] = [$"Name can be at most {MaxNameLength} characters."];
        }
        else if (name.Any(char.IsControl))
        {
            errors["name"] = ["Name cannot contain line breaks or other control characters."];
        }

        if (guidance is not null && guidance.Length > MaxGuidanceLength)
        {
            errors["guidance"] = [$"Guidance can be at most {MaxGuidanceLength} characters."];
        }

        if (guidance is { Length: 0 })
        {
            guidance = null;
        }

        return errors;
    }

    private static bool NameInUse(List<JsonElement> rows, string name, Guid? except) =>
        rows.Any(r => ReadGuid(r, "sprk_triagecategoryid") is { } id
            && id != except
            && string.Equals(ReadString(r, "sprk_name")?.Trim(), name, StringComparison.OrdinalIgnoreCase));

    private static ClassificationRow ToWriteRow(CategoryRow row) => new(row.Id, row.Name, row.Guidance, row.Enabled, row.Version);

    private static CategoryRow? ParseRow(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object || ReadGuid(row, "sprk_triagecategoryid") is not { } id)
        {
            return null;
        }

        decimal? recall = row.TryGetProperty("sprk_measuredrecall", out var r) && r.ValueKind == JsonValueKind.Number && r.TryGetDecimal(out var d) ? d : null;
        var measuredOn = ReadString(row, "sprk_recallmeasuredon");
        return new CategoryRow(
            id,
            ReadString(row, "sprk_name") ?? string.Empty,
            ReadString(row, "sprk_classifierguidance"),
            ReadBool(row, "sprk_enabled"),
            row.TryGetProperty("versionnumber", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var version) ? version : 0,
            recall,
            ReadInt(row, "sprk_labelledsetsize"),
            measuredOn is { Length: >= 10 } ? measuredOn[..10] : measuredOn,
            ReadString(row, "modifiedon"));
    }

    private static string? ReadString(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static Guid? ReadGuid(JsonElement row, string name) =>
        ReadString(row, name) is { } text && Guid.TryParse(text, out var id) && id != Guid.Empty ? id : null;

    private static bool ReadBool(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static int? ReadInt(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
}
