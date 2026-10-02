using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;

/// <summary>
/// How a chat tool writes a row FILED under a project, matter or work assignment (unified-access-control-r2 task 146 r2,
/// verifier items 2 and 6): the owner decision belongs to <see cref="IRecordOwnershipResolver"/> — the named Secure team
/// for a child of a secure record, the record's business-unit team otherwise — never the calling user.
/// </summary>
/// <remarks>
/// <para><b>Owner decision S1, option (1)</b> (session-27 round 3, accepted as recommended; it names "AI email draft, AI
/// create-record" explicitly), <b>refined by G5</b> (round 3b): the BFF checks AS THE USER that they could make the row
/// themselves, then the APPLICATION creates it OWNED BY THE TEAM — "same rule that assigned to the bu/team not
/// individual". A run-as-user create leaves the row owned by the caller in their own business unit, readable by every
/// colleague with ordinary depth even when it is filed to a secure record. Create-then-assign (option 2) leaves that
/// window open; widening user roles with prvAssign (option 3) is forbidden.</para>
/// <para><b>CLAUDE.md §6.5 path B — owner round 7 item 3 (2026-10-02), superseding the r2 path-A exception.</b>
/// spaarke-ai-architecture-redesign-r1 bound its tool plane "MUST run user-OBO for all Dataverse tool access"
/// (FR-P0-10, the handlers' "User-OBO ONLY" rule). The owner amended that rule for <c>dataverse.create_record</c> and
/// <c>email.draft</c>: "apply the G5 pattern — check the caller's rights AS THE USER, create AS THE APP owned by the team
/// (the named secure team under a secure parent; otherwise the RecordOwnershipResolver team), record the person in the
/// table's Assigned-To / 'for' column where one exists" (amendment recorded in that project's spec and in the task 146
/// note §13). <c>dataverse.create_record</c> therefore takes this path for EVERY create (<see cref="PathFor"/>), except
/// where the resolver keeps the creator (unfiled communications/threads, E1/E2), per-user tables, and tables with no
/// user/team ownership. <c>email.draft</c> takes it for a filed draft (an unfiled draft keeps its creator, E1).
/// Reads, updates and deletes stay user-OBO.</para>
/// <para><b>What "as the user" covers</b> — everything a run-as-user create would have had Dataverse check, so the
/// app-only create grants nothing the caller lacks: (1) the table's Create privilege, and Append when the row sets a
/// lookup, by the privilege names the table's own metadata declares (activity tables share <c>prvCreateActivity</c>);
/// (2) AppendTo on every record a lookup names (<c>RetrievePrincipalAccess</c>); (3) no column under field-level security
/// (refused: an app-only write would pass the caller's column security); (4) no owner or audit column (the server sets
/// the owner). Every question is asked through <see cref="IDataverseUserClient"/> under the caller's own token, so the
/// identity is the credential, not data. Any failure denies.</para>
/// <para><b>Component justification (CLAUDE.md §11).</b> Existing — <c>CallerRecordAccessProbe</c> answers the same
/// questions from a raw bearer token the tool handlers do not hold, and <c>FinanceAuthorizationFilter</c> is a route
/// filter; neither runs inside a tool call. Extension — the probe's pure parsers (<c>ResponseGrantsPrivilege</c>,
/// <c>DataverseAccessRightsMapper</c>) are reused, not copied. Cost of doing nothing — the two run-as-user create tools
/// file a secure record's child in an ordinary business unit (verifier item 6), and the update tool re-files one with no
/// owner re-derivation (item 2).</para>
/// </remarks>
internal static class OwnedChildWrite
{
    /// <summary>Columns the server owns on the app-only path; a request that sets one is refused, never honoured.</summary>
    internal static readonly IReadOnlySet<string> ServerOwnedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ownerid", "owninguser", "owningteam", "owningbusinessunit",
        "createdby", "createdon", "createdonbehalfby",
        "modifiedby", "modifiedon", "modifiedonbehalfby",
        "overriddencreatedon", "importsequencenumber",
    };

    /// <summary>The ownership parents a mapped row is filed under (resolver's definition — no per-table list).</summary>
    internal static IReadOnlyList<RecordOwnershipParent> ParentsOf(DataverseWriteItemMapper.MappedItem item) =>
        item.Lookups
            .Where(l => RecordOwnershipResolver.IsOwnershipParent(l.RelatedTable))
            .Select(l => new RecordOwnershipParent(l.RelatedTable, l.RecordId))
            .Distinct()
            .ToArray();

    /// <summary>
    /// True when an <c>email.draft</c> takes the owned path: a draft (a <c>sprk_communication</c>, a CHILD in the ownership
    /// set) that names a parent. An unfiled draft keeps its creator — the resolver's own answer for an unfiled
    /// communication (escalation E1: the per-user master thread and Direct-thread privacy rest on it).
    /// </summary>
    internal static bool AppliesTo(string table, DataverseWriteItemMapper.MappedItem item) =>
        RecordOwnershipResolver.IsReparentableChild(table) && ParentsOf(item).Count > 0;

    // ── Owner round 7 item 3 (2026-10-02): G5 for EVERY create of dataverse.create_record ────────────────────────────

    /// <summary>Which way a <c>dataverse.create_record</c> create is written.</summary>
    internal enum WritePath
    {
        /// <summary>G5: checked AS THE CALLER, created by the APPLICATION owned by the resolver's team.</summary>
        Owned,

        /// <summary>Created as the caller (their own create; Dataverse authorizes it and the caller owns it).</summary>
        RunAsUser,
    }

    /// <summary>
    /// Tables whose rows are per-user by design and stay owned by their user (task 146 constraint "per-user artifacts"):
    /// a team owner would show one person's notification, layout, navigation pins, preferences or chat history to their
    /// whole business unit.
    /// </summary>
    internal static readonly IReadOnlySet<string> PerUserTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "appnotification", "sprk_notificationoutbox", "sprk_workspacelayout", "sprk_navitem",
        "sprk_userpreferences", "sprk_userprofile", "sprk_aichatmessage", "sprk_aichatsummary",
    };

    /// <summary>
    /// Owner round 7 item 3: the path a <c>dataverse.create_record</c> create of <paramref name="table"/> takes. Every
    /// create is G5 (<see cref="WritePath.Owned"/>) — owned by the team <see cref="IRecordOwnershipResolver"/> names, never
    /// the individual — except where the resolver's own rules keep the creator:
    /// <list type="bullet">
    /// <item>a table whose metadata declares no user/team ownership (<paramref name="ownershipType"/> is neither
    /// <c>UserOwned</c> nor <c>TeamOwned</c> — organization-owned rows have no owner to decide);</item>
    /// <item>a per-user table (<see cref="PerUserTables"/>);</item>
    /// <item>an UNFILED communication or thread (escalation E1 / E2: <see cref="UnfiledOwnership.KeepCreator"/>).</item>
    /// </list>
    /// A run-as-user create still refuses a filing under a SECURE record (the handler's secure-filing check).
    /// </summary>
    internal static WritePath PathFor(string table, DataverseWriteItemMapper.MappedItem item, string? ownershipType)
    {
        if (ownershipType is not ("UserOwned" or "TeamOwned"))
            return WritePath.RunAsUser;
        if (PerUserTables.Contains(table))
            return WritePath.RunAsUser;
        if (KeepsItsCreatorWhenUnfiled(table) && ParentsOf(item).Count == 0)
            return WritePath.RunAsUser;
        return WritePath.Owned;
    }

    /// <summary>Communications and threads filed under nothing keep their creator (E1 / E2) — the resolver's
    /// <see cref="UnfiledOwnership.KeepCreator"/> answer, honoured here rather than overridden.</summary>
    internal static bool KeepsItsCreatorWhenUnfiled(string table) =>
        string.Equals(table, "sprk_communication", StringComparison.OrdinalIgnoreCase)
        || string.Equals(table, "sprk_communicationthread", StringComparison.OrdinalIgnoreCase);

    /// <summary>The column a table names the person a record is FOR in, and the table that column looks up.</summary>
    internal sealed record ForPersonColumn(string Column, string RelatedTable);

    /// <summary>
    /// Owner round 7 item 3: "record the person in the table's Assigned-To / 'for' column where one exists". Each entry
    /// follows the precedent already shipped for that table, so one table never has two "for" columns:
    /// <c>sprk_todo</c> / <c>sprk_event</c> → <c>sprk_assignedto</c> (contact; task 152, #1044);
    /// <c>sprk_matter</c> / <c>sprk_project</c> → <c>sprk_assignedtointernal</c> (contact; owner A7, Office quick-create);
    /// <c>sprk_communication</c> → <c>sprk_sentby</c> (systemuser; S1, the email draft). <c>sprk_workassignment</c> carries
    /// both <c>sprk_assignedto</c> and <c>sprk_assignedtointernal</c> with no shipped precedent for either, so it is not
    /// defaulted (task note §13). A table not listed has no "for" column.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, ForPersonColumn> ForPersonColumns =
        new Dictionary<string, ForPersonColumn>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_todo"] = new(AssignedToDefaults.AssignedToAttribute, "contact"),
            ["sprk_event"] = new(AssignedToDefaults.AssignedToAttribute, "contact"),
            ["sprk_matter"] = new("sprk_assignedtointernal", "contact"),
            ["sprk_project"] = new("sprk_assignedtointernal", "contact"),
            ["sprk_communication"] = new("sprk_sentby", "systemuser"),
        };

    /// <summary>
    /// <paramref name="item"/> with <paramref name="column"/> set to a lookup of <paramref name="relatedTable"/>
    /// (<paramref name="recordId"/>) in the write mapper's object form — so the mapper resolves its navigation property
    /// from metadata, as for every other lookup. The caller adds it only when the item does not set the column itself (a
    /// supplied value is never overwritten).
    /// </summary>
    internal static JsonElement WithLookup(JsonElement item, string column, string relatedTable, Guid recordId)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in item.EnumerateObject())
            {
                property.WriteTo(writer);
            }

            writer.WritePropertyName(column);
            writer.WriteStartObject();
            writer.WriteString("relatedTable", relatedTable);
            writer.WriteString("recordId", recordId.ToString("D"));
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    /// <summary>True when <paramref name="item"/> sets <paramref name="column"/> itself (any value, including null).</summary>
    internal static bool Sets(JsonElement item, string column) =>
        item.ValueKind == JsonValueKind.Object
        && item.EnumerateObject().Any(p => string.Equals(p.Name.Trim(), column, StringComparison.OrdinalIgnoreCase));

    /// <summary>What the owned create (or a check) answered.</summary>
    internal sealed record Outcome
    {
        public Guid? CreatedId { get; init; }

        /// <summary>Refused by the caller's own rights — surfaced as the user's access error.</summary>
        public string? Denied { get; init; }

        /// <summary>Refused by the owner decision — the stable <see cref="RecordOwnerRefusal"/> code and its reason.</summary>
        public RecordOwnerResolution? OwnerRefusal { get; init; }

        /// <summary>A question asked as the caller failed at Dataverse — the caller's own error.</summary>
        public DataverseUserResponse? ClientFailure { get; init; }

        /// <summary>
        /// The row would be owned by the Secure team, but its table is not one that team may own from here — a root
        /// (task 158 secures a work assignment or project filed under a secure record) or a table outside the ownership
        /// set (the Secure Record Owner role holds no Read on it). Nothing is created.
        /// </summary>
        public string? SecureFilingRefused { get; init; }

        public bool Succeeded =>
            CreatedId is not null
            || (Denied is null && OwnerRefusal is null && ClientFailure is null && SecureFilingRefused is null);

        public static readonly Outcome Allowed = new();
    }

    /// <summary>
    /// Creates a FILED child row (S1 / G5): as the caller, checks they could create it themselves; resolves its owner
    /// through the one resolver (secure-if-any); then the application creates it owned by that team, by PATCHing a fresh
    /// id — the create-by-upsert <c>InvoiceReviewService</c> uses for the G5 invoice. Nothing is written on any refusal; a
    /// Dataverse fault propagates.
    /// </summary>
    /// <param name="serverSetLookupColumns">Lookups the SERVER added to the row (e.g. the email draft's sender) — they
    /// name the caller themselves and cost no AppendTo check.</param>
    internal static async Task<Outcome> CreateAsync(
        IDataverseUserClient user,
        IRecordOwnershipResolver ownership,
        IFieldMappingDataverseService appOnly,
        string table,
        DataverseWriteItemMapper.MappedItem item,
        IReadOnlySet<string>? serverSetLookupColumns,
        Guid? callerObjectId,
        CancellationToken ct)
    {
        var me = await WhoAmIAsync(user, ct).ConfigureAwait(false);
        if (me.Failure is not null)
            return new Outcome { ClientFailure = me.Failure };

        var check = await CheckCallerMayCreateAsync(user, me.SystemUserId, table, item, serverSetLookupColumns, ct)
            .ConfigureAwait(false);
        if (!check.Succeeded)
            return check;

        var owner = await ownership.ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(ParentsOf(item), callerObjectId, me.SystemUserId), ct).ConfigureAwait(false);
        if (!owner.IsOwned)
            return new Outcome { OwnerRefusal = owner.IsRefused ? owner : RecordOwnerResolution.Refused(RecordOwnerRefusal.NoOwnerSource, owner.Reason ?? "no owner was resolved") };

        // The Secure team may own only the CHILD tables of the ownership set (each in the codified Secure Record Owner role
        // set, config/secure-record-owner-role.json). A root filed under a secure record is task 158's (it is secured
        // through provisioning, never a bare re-own); any other table would be refused by Dataverse (no Read).
        if (owner.IsSecureOwner && !RecordOwnershipResolver.IsReparentableChild(table))
        {
            return new Outcome
            {
                SecureFilingRefused = RecordOwnershipResolver.IsOwnershipParent(table)
                    ? $"A '{table}' record cannot be created under a secure record from chat: it would itself have to be "
                      + "made secure (its own owner team, container and sharing — the record's Make Secure path). It was "
                      + "NOT created. Create it from the secure record itself."
                    : $"A '{table}' record cannot be filed under a secure record from chat, and was NOT created. Create it "
                      + "from the record itself.",
            };
        }

        var fields = BodyFields(item.JsonBody);
        fields["ownerid@odata.bind"] = $"/teams({owner.OwningTeamId!.Value})";

        var id = Guid.NewGuid();
        await appOnly.UpdateRecordFieldsAsync(table, id, fields, ct).ConfigureAwait(false);
        return new Outcome { CreatedId = id };
    }

    /// <summary>
    /// AS THE CALLER, whether they could create this row themselves (see the class remarks for the four questions).
    /// </summary>
    internal static async Task<Outcome> CheckCallerMayCreateAsync(
        IDataverseUserClient user,
        Guid me,
        string table,
        DataverseWriteItemMapper.MappedItem item,
        IReadOnlySet<string>? serverSetLookupColumns,
        CancellationToken ct)
    {
        var serverOwned = item.Columns.FirstOrDefault(ServerOwnedColumns.Contains);
        if (serverOwned is not null)
        {
            return new Outcome
            {
                Denied = $"Column '{serverOwned}' is set by the server on a record the application creates for you — omit it.",
            };
        }

        // (1) The table's Create (and Append, when the row sets a lookup) privilege, by the names its metadata declares.
        var metadata = await user.GetAsync($"EntityDefinitions(LogicalName='{table}')?$select=LogicalName,Privileges", ct)
            .ConfigureAwait(false);
        if (!metadata.IsSuccess)
            return new Outcome { ClientFailure = metadata };

        var needed = new List<string>();
        if (PrivilegeNamed(metadata.Body, "Create", 1) is not { } create)
            return new Outcome { Denied = $"Table '{table}' declares no Create privilege; it cannot be created here." };
        needed.Add(create);
        if (item.Lookups.Count > 0)
        {
            if (PrivilegeNamed(metadata.Body, "Append", 7) is not { } append)
                return new Outcome { Denied = $"Table '{table}' declares no Append privilege; its lookups cannot be set here." };
            needed.Add(append);
        }

        var names = Uri.EscapeDataString(JsonSerializer.Serialize(needed));
        var held = await user.GetAsync(
            $"systemusers({me:D})/Microsoft.Dynamics.CRM.RetrieveUserSetOfPrivilegesByNames(PrivilegeNames=@p1)?@p1={names}", ct)
            .ConfigureAwait(false);
        if (!held.IsSuccess)
            return new Outcome { ClientFailure = held };

        var heldBody = held.Body?.GetRawText();
        var missing = needed.FirstOrDefault(name => !CallerRecordAccessProbe.ResponseGrantsPrivilege(heldBody, name));
        if (missing is not null)
            return new Outcome { Denied = $"You do not have permission to create records in '{table}' ({missing})." };

        // (3) No column under field-level security: an app-only write would pass the caller's column security.
        var columnFilter = string.Join(" or ", item.Columns.Select(c => $"LogicalName eq '{c}'"));
        var attributes = await user.GetAsync(
            $"EntityDefinitions(LogicalName='{table}')/Attributes?$select=LogicalName,IsSecured&$filter={Uri.EscapeDataString(columnFilter)}", ct)
            .ConfigureAwait(false);
        if (!attributes.IsSuccess)
            return new Outcome { ClientFailure = attributes };

        if (SecuredColumnIn(attributes.Body) is { } secured)
        {
            return new Outcome
            {
                Denied = $"Column '{secured}' is field-secured and cannot be set from chat on a record the application creates for you.",
            };
        }

        // (2) AppendTo on every record a lookup names (the server-set ones name the caller).
        return await CheckCallerMayAppendToAsync(
                user, me, item.Lookups.Where(l => serverSetLookupColumns?.Contains(l.Column) != true), ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// AS THE CALLER, AppendTo on each record in <paramref name="lookups"/> (<c>RetrievePrincipalAccess</c>) — the right a
    /// lookup onto a record costs. Shared by the create path and the update tool's re-file (verifier item 2), so a re-file
    /// onto a record the caller cannot see is denied before the owner decision is asked about it.
    /// </summary>
    internal static async Task<Outcome> CheckCallerMayAppendToAsync(
        IDataverseUserClient user, Guid me, IEnumerable<DataverseWriteItemMapper.MappedLookup> lookups, CancellationToken ct)
    {
        foreach (var lookup in lookups.DistinctBy(l => (l.RelatedEntitySet, l.RecordId)))
        {
            var target = Uri.EscapeDataString($"{{\"@odata.id\":\"{lookup.RelatedEntitySet}({lookup.RecordId:D})\"}}");
            var access = await user.GetAsync(
                $"systemusers({me:D})/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@p1)?@p1={target}", ct)
                .ConfigureAwait(false);

            var rights = access.IsSuccess
                && access.Body is { } body
                && body.TryGetProperty("AccessRights", out var value)
                && value.ValueKind == JsonValueKind.String
                    ? DataverseAccessRightsMapper.FromAccessRightsString(value.GetString())
                    : AccessRights.None;

            if (!rights.HasFlag(AccessRights.AppendTo))
            {
                return new Outcome
                {
                    Denied = $"You do not have permission to file this record under the {lookup.RelatedTable} it names.",
                };
            }
        }

        return Outcome.Allowed;
    }

    /// <summary>The caller's <c>systemuserid</c> — <c>WhoAmI()</c> under their own token, which cannot name anyone else.</summary>
    internal static async Task<(Guid SystemUserId, DataverseUserResponse? Failure)> WhoAmIAsync(
        IDataverseUserClient user, CancellationToken ct)
    {
        var response = await user.GetAsync("WhoAmI()", ct).ConfigureAwait(false);
        if (!response.IsSuccess)
            return (Guid.Empty, response);

        return response.Body is { } body
               && body.TryGetProperty("UserId", out var id)
               && id.ValueKind == JsonValueKind.String
               && Guid.TryParse(id.GetString(), out var userId)
               && userId != Guid.Empty
            ? (userId, null)
            : (Guid.Empty, DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.UserContextRequired,
                "The calling user could not be identified in Dataverse."));
    }

    /// <summary>The mapped JSON body as the field dictionary the app-only PATCH serializes (values kept as JSON).</summary>
    internal static Dictionary<string, object?> BodyFields(string jsonBody)
    {
        using var document = JsonDocument.Parse(jsonBody);
        return document.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => (object?)p.Value.Clone(), StringComparer.Ordinal);
    }

    /// <summary>The name of the table privilege of <paramref name="type"/> in an <c>EntityDefinitions?$select=Privileges</c>
    /// body; <c>PrivilegeType</c> arrives as its name or its number.</summary>
    private static string? PrivilegeNamed(JsonElement? body, string type, int typeNumber)
    {
        if (body is not { } root
            || !root.TryGetProperty("Privileges", out var privileges)
            || privileges.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var privilege in privileges.EnumerateArray())
        {
            if (!privilege.TryGetProperty("PrivilegeType", out var kind))
                continue;

            var matches = kind.ValueKind switch
            {
                JsonValueKind.String => string.Equals(kind.GetString(), type, StringComparison.OrdinalIgnoreCase),
                JsonValueKind.Number => kind.TryGetInt32(out var n) && n == typeNumber,
                _ => false,
            };

            if (matches && privilege.TryGetProperty("Name", out var name) && name.ValueKind == JsonValueKind.String)
                return name.GetString();
        }

        return null;
    }

    /// <summary>The first attribute marked <c>IsSecured</c> in an <c>Attributes</c> collection body, if any.</summary>
    private static string? SecuredColumnIn(JsonElement? body)
    {
        if (body is not { } root
            || !root.TryGetProperty("value", out var attributes)
            || attributes.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var attribute in attributes.EnumerateArray())
        {
            if (attribute.TryGetProperty("IsSecured", out var secured) && secured.ValueKind == JsonValueKind.True
                && attribute.TryGetProperty("LogicalName", out var name))
            {
                return name.GetString();
            }
        }

        return null;
    }
}
