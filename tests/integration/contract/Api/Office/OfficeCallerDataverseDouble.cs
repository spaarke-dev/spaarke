using System.Text.Json;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Tests.TestInfrastructure;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// The rows the Office quick-create suites' fixtures name, gathered in one place so the caller's Dataverse
/// (<see cref="OfficeCallerDataverseDouble"/>) holds the same world the app-only mocks model. Each id is copied from the suite
/// named beside it; <c>OfficeCallerDataverseDoubleTests</c> reads every suite's constant back and fails on any drift.
/// </summary>
public static class OfficeQuickCreateWorld
{
    // ── Users (the systemuser the endpoint's resolver names) ──
    /// <summary>OfficeQuickCreateContractTests / OfficeQuickCreateProjectContractTests <c>OwnerId</c>.</summary>
    public static readonly Guid ContractCaller = Guid.Parse("7d0e8a41-3b5c-4f2e-9a10-5c2b7e4f1a01");

    /// <summary>OfficeQuickCreateSourceFieldSecurityTests <c>OwnerId</c>.</summary>
    public static readonly Guid FieldSecurityCaller = Guid.Parse("7d0e8a41-3b5c-4f2e-9a10-5c2b7e4f1a02");

    /// <summary>OfficeQuickCreateFieldsContractTests <c>CallerId</c>.</summary>
    public static readonly Guid FieldsCaller = Guid.Parse("7d0e8a41-3b5c-4f2e-9a10-5c2b7e4f1100");

    /// <summary>Issue1079_InvoiceQuickCreateNameTests <c>CallerSystemUserId</c>.</summary>
    public static readonly Guid Issue1079Caller = Guid.Parse("7d0e8a41-3b5c-4f2e-9a10-5c2b7e4f1079");

    // ── Business units and their default team ──
    /// <summary>OfficeQuickCreateContractTests / OfficeQuickCreateProjectContractTests <c>BusinessUnitId</c>.</summary>
    public static readonly Guid ContractBusinessUnit = Guid.Parse("cb15f587-baa0-f111-aaac-000d3a99d1d7");

    /// <summary>OfficeQuickCreateFieldsContractTests <c>BusinessUnitId</c>.</summary>
    public static readonly Guid FieldsBusinessUnit = Guid.Parse("cb15f587-baa0-f111-aaac-000d3a990100");

    /// <summary>The owning team every suite asserts (<see cref="RecordOwnershipResolverDouble.DefaultTeamId"/>).</summary>
    public static Guid DefaultTeam => RecordOwnershipResolverDouble.DefaultTeamId;

    // ── Contacts and accounts ──
    /// <summary>OfficeQuickCreateFieldsContractTests <c>ChosenContact</c> (the Assigned To the request names).</summary>
    public static readonly Guid ChosenContact = Guid.Parse("cccccccc-1000-4000-8000-000000000100");

    /// <summary>OfficeQuickCreateFieldsContractTests <c>MakersContact</c> (the maker's linked contact).</summary>
    public static readonly Guid MakersContact = Guid.Parse("eeeeeeee-1000-4000-8000-000000000100");

    /// <summary>OfficeQuickCreateContractTests / OfficeQuickCreateProjectContractTests <c>AccountId</c> (a mapped <c>sprk_client</c>).</summary>
    public static readonly Guid ContractAccount = Guid.Parse("9f8e7d6c-5b4a-4c3d-8e2f-1a0b9c8d7e01");

    /// <summary>OfficeQuickCreateSourceFieldSecurityTests <c>AccountId</c>.</summary>
    public static readonly Guid FieldSecurityAccount = Guid.Parse("9f8e7d6c-5b4a-4c3d-8e2f-1a0b9c8d7e02");

    // ── Organization-owned reference rows ──
    /// <summary>OfficeQuickCreateContractTests / OfficeQuickCreateProjectContractTests <c>MatterTypeId</c>.</summary>
    public static readonly Guid ContractMatterType = Guid.Parse("b2c4e6a8-1d3f-4a5b-8c7d-9e0f1a2b3c01");

    /// <summary>OfficeQuickCreateSourceFieldSecurityTests <c>MatterTypeId</c>.</summary>
    public static readonly Guid FieldSecurityMatterType = Guid.Parse("b2c4e6a8-1d3f-4a5b-8c7d-9e0f1a2b3c02");

    /// <summary>OfficeQuickCreateFieldsContractTests <c>MatterTypeId</c>.</summary>
    public static readonly Guid FieldsMatterType = Guid.Parse("b2c4e6a8-1d3f-4a5b-8c7d-9e0f1a2b3100");

    /// <summary>OfficeQuickCreateFieldsContractTests <c>PracticeAreaId</c>.</summary>
    public static readonly Guid PracticeArea = Guid.Parse("b41377db-690e-f111-8342-7c1e520aa4df");

    /// <summary>OfficeQuickCreateFieldsContractTests <c>ProjectTypeId</c> (a <c>sprk_projecttype_ref</c> row).</summary>
    public static readonly Guid ProjectType = Guid.Parse("0ed9d8ac-b018-f111-8343-7ced8d1dc988");

    // ── Mapping sources (the records a quick-create is made from) ──
    /// <summary>OfficeQuickCreateContractTests / OfficeQuickCreateProjectContractTests <c>SourceProjectId</c>.</summary>
    public static readonly Guid ContractSourceProject = Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c01");

    /// <summary>OfficeQuickCreateSourceFieldSecurityTests <c>SourceProjectId</c>.</summary>
    public static readonly Guid FieldSecuritySourceProject = Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b2188");
}

/// <summary>
/// Thrown by <see cref="OfficeCallerDataverseDouble"/> for a question it was not scripted to answer, naming the request, so a
/// fixture gap is never mistaken for a Dataverse answer.
/// </summary>
public sealed class UnscriptedCallerRequestException(string request, string why)
    : InvalidOperationException($"OfficeCallerDataverseDouble was not scripted for {request}: {why}")
{
    public string Request { get; } = request;
}

/// <summary>
/// Field mapping task 018b-1b: the CALLER's Dataverse (<see cref="IDataverseUserClient"/>, the documented mock boundary for
/// user-OBO access) for the Office quick-create host, as <c>RecordCreationService</c> questions it once the Matter and Project
/// legs run on it. A closed world: every question it answers is scripted below, with defaults that model the suites'
/// fixtures (<see cref="OfficeQuickCreateWorld"/>); anything else throws <see cref="UnscriptedCallerRequestException"/>.
/// </summary>
/// <remarks>
/// <para>The questions, in the order the service asks them:</para>
/// <list type="number">
/// <item><c>WhoAmI()</c> → <see cref="Me"/> (default: the systemuser the endpoint's resolver named for this request).</item>
/// <item>Table metadata <c>EntityDefinitions(LogicalName='t')?$select=LogicalName[,OwnershipType,PrimaryIdAttribute],Privileges</c>.</item>
/// <item><c>systemusers(me)/…RetrieveUserSetOfPrivilegesByNames</c> → <see cref="Held"/> (depth Basic, anchored at the caller's
/// business unit when the world knows it).</item>
/// <item>Column security <c>…/Attributes?$select=LogicalName,IsSecured</c> → <see cref="SecuredColumns"/>.</item>
/// <item><c>systemusers(me)/…RetrievePrincipalAccess</c> per user-owned lookup → full rights on a world row unless
/// <see cref="NoAppendTo"/>; 400 on an organization- or business-owned table, as live.</item>
/// <item>Caller reads of rows <c>{set}({id})?$select=…</c>: reference rows, the record-type catalog, a business-owned row's
/// business unit → <see cref="Rows"/>; <see cref="Missing"/> answers 404, <see cref="Unreadable"/> 403.</item>
/// <item>The business-unit hierarchy question (<c>UnderOrEqual</c>) → <see cref="BusinessUnitParents"/>.</item>
/// <item>Mapping metadata: <c>ManyToOneRelationships</c> → <see cref="Lookups"/>; <c>EntitySetName</c> → the world's entity
/// sets; <c>AttributeType,IsValidForCreate</c> → <see cref="Columns"/>; <c>DateTimeAttributeMetadata</c> →
/// <see cref="DateBehaviours"/>.</item>
/// <item>The #2188 mask read <c>{set}({id})</c> (no query) → <see cref="MaskRead"/>, unchanged: the request is handed to it as
/// is, so its answer is exactly the <see cref="MappingSourceCallerReadDouble"/> answer.</item>
/// </list>
/// <para>Per-test overrides: the collections above, plus <see cref="Respond"/> (answer one request with a status) and
/// <see cref="Fault"/> (throw on one request). Neither hook sees a mask read; fault that through
/// <see cref="MaskRead"/>'s own switches. Writes as the caller are not scripted (the service writes app-only, at step 11).</para>
/// </remarks>
public sealed partial class OfficeCallerDataverseDouble : IDataverseUserClient
{
    private const string OrganizationOwned = "OrganizationOwned";
    private const string BusinessOwned = "BusinessOwned";
    private const string UserOwned = "UserOwned";

    private readonly Func<Guid?> _resolvedCaller;

    /// <param name="maskRead">The #2188 mapping-source caller read, answered unchanged.</param>
    /// <param name="resolvedCaller">The default <see cref="Me"/>: the systemuser the host resolved for the request, if any.</param>
    public OfficeCallerDataverseDouble(MappingSourceCallerReadDouble maskRead, Func<Guid?> resolvedCaller)
    {
        MaskRead = maskRead ?? throw new ArgumentNullException(nameof(maskRead));
        _resolvedCaller = resolvedCaller ?? throw new ArgumentNullException(nameof(resolvedCaller));
        SeedWorld();
    }

    /// <summary>The #2188 mask read, which answers every <c>{set}({id})</c> read with no query.</summary>
    public MappingSourceCallerReadDouble MaskRead { get; }

    /// <summary>The caller <c>WhoAmI()</c> names; <see langword="null"/> = the resolved caller (none → the question throws).</summary>
    public Guid? Me { get; set; }

    /// <summary>When set, <c>WhoAmI()</c> answers this failure (e.g. 0 <c>UserContextRequired</c>, 403 <c>AccessDenied</c>).</summary>
    public DataverseUserResponse? WhoAmIFailure { get; set; }

    /// <summary>The caller's effective <see cref="Me"/>, or throws when the world names nobody.</summary>
    public Guid Caller => Me ?? _resolvedCaller()
        ?? throw new UnscriptedCallerRequestException("WhoAmI()", "no caller is scripted (set Me, or arrange the CallerResolver)");

    /// <summary>table logical name → (entity set, ownership, privilege schema name, primary id attribute).</summary>
    public Dictionary<string, (string EntitySet, string Ownership, string PrivilegeSchema, string PrimaryId)> Tables { get; } =
        new(StringComparer.Ordinal);

    /// <summary>table → its lookups (column, target table, navigation property), as <c>ManyToOneRelationships</c> lists them.</summary>
    public Dictionary<string, List<(string Column, string Target, string Navigation)>> Lookups { get; } = new(StringComparer.Ordinal);

    /// <summary>table → column → (<c>AttributeType</c>, <c>IsValidForCreate</c>). A column not listed does not exist.</summary>
    public Dictionary<string, Dictionary<string, (string Type, bool ValidForCreate)>> Columns { get; } = new(StringComparer.Ordinal);

    /// <summary>(table, column) → <c>DateTimeBehavior</c> of a date column.</summary>
    public Dictionary<(string Table, string Column), string> DateBehaviours { get; } = new();

    /// <summary>Privileges the caller holds (names as the table metadata declares them; case-insensitive, as live).</summary>
    public HashSet<string> Held { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Columns under field security the caller may not set, as <c>(table, column)</c>.</summary>
    public HashSet<(string Table, string Column)> SecuredColumns { get; } = new();

    /// <summary>Rows that exist for the caller: (table, id) → Web API properties a <c>$select</c> read answers.</summary>
    public Dictionary<(string Table, Guid Id), Dictionary<string, object?>> Rows { get; } = new();

    /// <summary>World rows the caller holds no AppendTo on (RetrievePrincipalAccess answers Read and Write only).</summary>
    public HashSet<(string Table, Guid Id)> NoAppendTo { get; } = new();

    /// <summary>Rows that do not exist: a read or RetrievePrincipalAccess of one answers 404.</summary>
    public HashSet<(string Table, Guid Id)> Missing { get; } = new();

    /// <summary>Rows that exist but the caller may not read: a read answers 403.</summary>
    public HashSet<(string Table, Guid Id)> Unreadable { get; } = new();

    /// <summary>The business-unit tree, child → parent (a unit not listed is a root).</summary>
    public Dictionary<Guid, Guid> BusinessUnitParents { get; } = new();

    /// <summary>A per-test answer for one request (unescaped path), in place of the scripted one; <see langword="null"/> = no.</summary>
    public Func<string, DataverseUserResponse?>? Respond { get; set; }

    /// <summary>A per-test transport fault for one request (unescaped path); <see langword="null"/> = no.</summary>
    public Func<string, Exception?>? Fault { get; set; }

    /// <summary>Every request other than a mask read, unescaped, in order (mask reads land in <see cref="MaskRead"/>.Paths).</summary>
    public List<string> Requests { get; } = new();

    /// <summary>Registers a row the caller can see (values are Web API properties, e.g. <c>_businessunitid_value</c>).</summary>
    public OfficeCallerDataverseDouble WithRow(string table, Guid id, Dictionary<string, object?>? values = null)
    {
        if (!Tables.ContainsKey(table))
            throw new UnscriptedCallerRequestException($"row {table}({id:D})", $"table '{table}' is not in the world");
        Rows[(table, id)] = values ?? new Dictionary<string, object?>();
        return this;
    }

    /// <summary>The table an entity set names, or throws.</summary>
    public string TableOfSet(string entitySet, string request) =>
        Tables.FirstOrDefault(t => t.Value.EntitySet == entitySet).Key
        ?? throw new UnscriptedCallerRequestException(request, $"entity set '{entitySet}' is not in the world");

    private (string EntitySet, string Ownership, string PrivilegeSchema, string PrimaryId) TableNamed(string table, string request) =>
        Tables.TryGetValue(table, out var known)
            ? known
            : throw new UnscriptedCallerRequestException(request, $"table '{table}' is not in the world");

    [GeneratedRegex(@"^[a-z_]+\([0-9a-fA-F-]{36}\)$")]
    private static partial Regex MaskReadPath();

    [GeneratedRegex(@"^systemusers\((?<me>[0-9a-fA-F-]{36})\)/Microsoft\.Dynamics\.CRM\.RetrievePrincipalAccess\(Target=@p1\)\?@p1=\{""@odata\.id"":""(?<set>[a-z_]+)\((?<id>[0-9a-fA-F-]{36})\)""\}$")]
    private static partial Regex PrincipalAccess();

    [GeneratedRegex(@"^systemusers\((?<me>[0-9a-fA-F-]{36})\)/Microsoft\.Dynamics\.CRM\.RetrieveUserSetOfPrivilegesByNames\(PrivilegeNames=@p1\)\?@p1=(?<names>\[.*\])$")]
    private static partial Regex PrivilegesByNames();

    [GeneratedRegex(@"^EntityDefinitions\(LogicalName='(?<t>[a-z_]+)'\)(?<rest>.*)$")]
    private static partial Regex Definition();

    [GeneratedRegex(@"LogicalName eq '(?<c>[a-z0-9_]+)'")]
    private static partial Regex AttributeNamed();

    [GeneratedRegex(@"^businessunits\?\$select=businessunitid&\$filter=businessunitid eq (?<candidate>[0-9a-fA-F-]{36}) and Microsoft\.Dynamics\.CRM\.UnderOrEqual\(PropertyName='businessunitid',PropertyValue='(?<ancestor>[0-9a-fA-F-]{36})'\)$")]
    private static partial Regex UnderOrEqual();

    [GeneratedRegex(@"^(?<set>[a-z_]+)\((?<id>[0-9a-fA-F-]{36})\)\?\$select=(?<columns>[A-Za-z0-9_,]+)$")]
    private static partial Regex RowRead();

    public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
    {
        // The #2188 mask read goes to MappingSourceCallerReadDouble untouched — its exact answer, its switches, its Paths.
        if (MaskReadPath().IsMatch(relativePath))
            return MaskRead.GetAsync(relativePath, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        var path = Uri.UnescapeDataString(relativePath);
        lock (Requests)
        {
            Requests.Add(path);
        }

        if (Fault?.Invoke(path) is { } fault)
            throw fault;
        if (Respond?.Invoke(path) is { } scripted)
            return Task.FromResult(scripted);

        return Task.FromResult(Answer(path));
    }

    private DataverseUserResponse Answer(string path)
    {
        var request = $"GET {path}";
        if (path == "WhoAmI()")
            return WhoAmIFailure ?? Ok(new { UserId = Caller });

        if (PrincipalAccess().Match(path) is { Success: true } access)
        {
            AssertIsCaller(access.Groups["me"].Value, request);
            var table = TableOfSet(access.Groups["set"].Value, request);
            var id = Guid.Parse(access.Groups["id"].Value);
            var ownership = TableNamed(table, request).Ownership;
            if (ownership is OrganizationOwned or BusinessOwned)
            {
                // Live (spaarkedev1, 2026-10-07): the message refuses the table before looking at the row.
                return DataverseUserResponse.Fail(400, DataverseUserClientErrorCodes.BadRequest,
                    $"0x80040800 The 'RetrievePrincipalAccess' method does not support entities of type '{table}'.");
            }

            if (Missing.Contains((table, id)))
                return DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "Does not exist.");
            if (!Rows.ContainsKey((table, id)))
                throw new UnscriptedCallerRequestException(request, $"row {table}({id:D}) is not in the world");

            return Ok(new
            {
                AccessRights = NoAppendTo.Contains((table, id))
                    ? "ReadAccess,WriteAccess"
                    : "ReadAccess,WriteAccess,AppendAccess,AppendToAccess",
            });
        }

        if (PrivilegesByNames().Match(path) is { Success: true } privileges)
        {
            AssertIsCaller(privileges.Groups["me"].Value, request);
            var asked = JsonSerializer.Deserialize<string[]>(privileges.Groups["names"].Value)
                ?? throw new UnscriptedCallerRequestException(request, "no privilege names");
            var anchor = Rows.TryGetValue(("systemuser", Caller), out var me)
                         && me.TryGetValue("_businessunitid_value", out var unit) && unit is Guid business
                ? business
                : Guid.Empty;
            return Ok(new
            {
                RolePrivileges = asked.Where(Held.Contains).Select(name => anchor == Guid.Empty
                    ? (object)new { Depth = "Basic", PrivilegeName = name }
                    : new { Depth = "Basic", BusinessUnitId = anchor, PrivilegeName = name }),
            });
        }

        if (UnderOrEqual().Match(path) is { Success: true } under)
        {
            var candidate = Guid.Parse(under.Groups["candidate"].Value);
            var ancestor = Guid.Parse(under.Groups["ancestor"].Value);
            var reaches = false;
            for (Guid? unit = candidate; unit is { } current; unit = BusinessUnitParents.TryGetValue(current, out var parent) ? parent : null)
            {
                if (current == ancestor)
                {
                    reaches = true;
                    break;
                }
            }

            return Ok(new { value = reaches ? new object[] { new { businessunitid = candidate } } : Array.Empty<object>() });
        }

        if (Definition().Match(path) is { Success: true } definition)
            return AnswerDefinition(definition.Groups["t"].Value, definition.Groups["rest"].Value, request);

        if (RowRead().Match(path) is { Success: true } read)
        {
            var table = TableOfSet(read.Groups["set"].Value, request);
            var id = Guid.Parse(read.Groups["id"].Value);
            if (Missing.Contains((table, id)))
                return DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "Not found.");
            if (Unreadable.Contains((table, id)))
            {
                return DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied,
                    $"Principal user (Id={Caller:D}, type=8) is missing prvRead privilege on the record (Id={id:D}).");
            }

            if (!Rows.TryGetValue((table, id), out var values))
                throw new UnscriptedCallerRequestException(request, $"row {table}({id:D}) is not in the world");

            var primaryId = TableNamed(table, request).PrimaryId;
            var body = new Dictionary<string, object?> { [primaryId] = id };
            foreach (var column in read.Groups["columns"].Value.Split(','))
            {
                if (column != primaryId)
                    body[column] = values.GetValueOrDefault(column);
            }

            return Ok(body);
        }

        throw new UnscriptedCallerRequestException(request, "no scripted question has this shape");
    }

    private DataverseUserResponse AnswerDefinition(string table, string rest, string request)
    {
        var known = TableNamed(table, request);
        if (rest.StartsWith("/Attributes/Microsoft.Dynamics.CRM.DateTimeAttributeMetadata?$select=LogicalName,DateTimeBehavior&$filter=", StringComparison.Ordinal))
        {
            var asked = AskedColumns(rest);
            return Ok(new
            {
                value = DateBehaviours.Where(d => d.Key.Table == table && asked.Contains(d.Key.Column))
                    .Select(d => new { LogicalName = d.Key.Column, DateTimeBehavior = new { Value = d.Value } }),
            });
        }

        if (rest.StartsWith("/Attributes?$select=LogicalName,IsSecured&$filter=", StringComparison.Ordinal))
        {
            var asked = AskedColumns(rest);
            return Ok(new
            {
                value = DeclaredColumns(table).Where(asked.Contains)
                    .Select(c => new { LogicalName = c, IsSecured = SecuredColumns.Contains((table, c)) }),
            });
        }

        if (rest.StartsWith("/Attributes?$select=LogicalName,AttributeType,IsValidForCreate&$filter=", StringComparison.Ordinal))
        {
            var asked = AskedColumns(rest);
            var columns = Columns.GetValueOrDefault(table) ?? new();
            return Ok(new
            {
                value = columns.Where(c => asked.Contains(c.Key))
                    .Select(c => new { LogicalName = c.Key, AttributeType = c.Value.Type, IsValidForCreate = c.Value.ValidForCreate }),
            });
        }

        if (rest is "?$select=LogicalName,Privileges" or "?$select=LogicalName,OwnershipType,PrimaryIdAttribute,Privileges")
        {
            var schema = known.PrivilegeSchema;
            return Ok(new
            {
                LogicalName = table,
                OwnershipType = known.Ownership,
                PrimaryIdAttribute = known.PrimaryId,
                Privileges = new[]
                {
                    new { Name = $"prvCreate{schema}", PrivilegeType = "Create" },
                    new { Name = $"prvRead{schema}", PrivilegeType = "Read" },
                    new { Name = $"prvAppend{schema}", PrivilegeType = "Append" },
                    new { Name = $"prvAppendTo{schema}", PrivilegeType = "AppendTo" },
                },
            });
        }

        if (rest == "?$select=LogicalName&$expand=ManyToOneRelationships($select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntity)")
        {
            return Ok(new
            {
                LogicalName = table,
                ManyToOneRelationships = (Lookups.GetValueOrDefault(table) ?? new())
                    .Select(l => new
                    {
                        ReferencingAttribute = l.Column,
                        ReferencingEntityNavigationPropertyName = l.Navigation,
                        ReferencedEntity = l.Target,
                    }),
            });
        }

        if (rest == "?$select=EntitySetName")
            return Ok(new { EntitySetName = known.EntitySet });

        throw new UnscriptedCallerRequestException(request, $"no scripted metadata question on '{table}' has this shape");
    }

    /// <summary>A table's columns: those <see cref="Columns"/> declares, plus its lookups.</summary>
    private IEnumerable<string> DeclaredColumns(string table) =>
        (Columns.GetValueOrDefault(table)?.Keys ?? Enumerable.Empty<string>())
        .Concat((Lookups.GetValueOrDefault(table) ?? new()).Select(l => l.Column))
        .Distinct(StringComparer.Ordinal);

    private static HashSet<string> AskedColumns(string rest) =>
        AttributeNamed().Matches(rest).Select(m => m.Groups["c"].Value).ToHashSet(StringComparer.Ordinal);

    private void AssertIsCaller(string asked, string request)
    {
        if (Guid.Parse(asked) != Caller)
            throw new UnscriptedCallerRequestException(request, $"it asks about {asked}, not the caller {Caller:D}");
    }

    private static DataverseUserResponse Ok(object body) =>
        DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(body));

    public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) =>
        throw new UnscriptedCallerRequestException($"POST {absoluteApiPath}", "writes as the caller are not scripted");

    public Task<DataverseUserResponse> PostAsync(
        string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken) =>
        throw new UnscriptedCallerRequestException($"POST {absoluteApiPath}", "writes as the caller are not scripted");

    public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken) =>
        throw new UnscriptedCallerRequestException($"PATCH {relativePath}", "writes as the caller are not scripted");

    public Task<DataverseUserResponse> PatchAsync(
        string relativePath, string jsonBody, long expectedVersion, CancellationToken cancellationToken) =>
        throw new UnscriptedCallerRequestException($"PATCH {relativePath}", "writes as the caller are not scripted");

    public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
        throw new UnscriptedCallerRequestException($"DELETE {relativePath}", "writes as the caller are not scripted");

    // ── The default world ────────────────────────────────────────────────────────────────────────────────────────────

    private void SeedWorld()
    {
        // Tables (entity sets and ownership as live metadata declares them; privilege names use the schema name).
        Table("sprk_matter", "sprk_matters", UserOwned, "sprk_Matter");
        Table("sprk_project", "sprk_projects", UserOwned, "sprk_Project");
        Table("sprk_invoice", "sprk_invoices", UserOwned, "sprk_Invoice");
        Table("contact", "contacts", UserOwned, "Contact");
        Table("account", "accounts", UserOwned, "Account");
        Table("systemuser", "systemusers", BusinessOwned, "User");
        Table("team", "teams", BusinessOwned, "Team");
        Table("businessunit", "businessunits", BusinessOwned, "BusinessUnit");
        Table("sprk_mattertype_ref", "sprk_mattertype_refs", OrganizationOwned, "sprk_MatterType_Ref");
        Table("sprk_practicearea_ref", "sprk_practicearea_refs", OrganizationOwned, "sprk_PracticeArea_Ref");
        Table("sprk_projecttype_ref", "sprk_projecttype_refs", OrganizationOwned, "sprk_ProjectType_Ref");
        Table("sprk_recordtype_ref", "sprk_recordtype_refs", OrganizationOwned, "sprk_RecordType_Ref");
        Table("sprk_aisearchindex", "sprk_aisearchindexes", OrganizationOwned, "sprk_AISearchIndex");

        // Lookups (live navigation-property casing: the schema name).
        Lookups["sprk_matter"] =
        [
            ("sprk_mattertype", "sprk_mattertype_ref", "sprk_MatterType"),
            ("sprk_practicearea", "sprk_practicearea_ref", "sprk_PracticeArea"),
            ("sprk_assignedtointernal", "contact", "sprk_AssignedToInternal"),
            ("sprk_createdbyperson", "systemuser", "sprk_CreatedByPerson"),
            ("sprk_client", "account", "sprk_Client"),
            ("sprk_ai_search_index", "sprk_aisearchindex", "sprk_AI_Search_Index"),
            ("ownerid", "team", "ownerid"),
        ];
        Lookups["sprk_project"] =
        [
            ("sprk_projecttype_ref", "sprk_projecttype_ref", "sprk_ProjectType_Ref"),
            ("sprk_assignedtointernal", "contact", "sprk_AssignedToInternal"),
            ("sprk_createdbyperson", "systemuser", "sprk_CreatedByPerson"),
            ("sprk_regardingrecordtype", "sprk_recordtype_ref", "sprk_RegardingRecordType"),
            ("sprk_client", "account", "sprk_Client"),
            ("sprk_ai_search_index", "sprk_aisearchindex", "sprk_AI_Search_Index"),
            ("ownerid", "team", "ownerid"),
        ];

        // Columns: the request's own and every profile target the suites' rules name.
        Columns["sprk_matter"] = new(StringComparer.Ordinal)
        {
            ["sprk_mattername"] = ("String", true),
            ["sprk_matterdescription"] = ("Memo", true),
            ["sprk_matternotes"] = ("Memo", true),
            ["sprk_matternumber"] = ("String", true),
            ["sprk_clientreference"] = ("String", true),
            ["sprk_billingstatus"] = ("Picklist", true),
            ["sprk_searchindexname"] = ("String", true),
            ["sprk_containerid"] = ("String", true),
            ["sprk_mattertype"] = ("Lookup", true),
            ["sprk_practicearea"] = ("Lookup", true),
            ["sprk_assignedtointernal"] = ("Lookup", true),
            ["sprk_createdbyperson"] = ("Lookup", true),
            ["sprk_client"] = ("Lookup", true),
            ["sprk_ai_search_index"] = ("Lookup", true),
            ["ownerid"] = ("Owner", true),
        };
        Columns["sprk_project"] = new(StringComparer.Ordinal)
        {
            ["sprk_projectname"] = ("String", true),
            ["sprk_projectdescription"] = ("Memo", true),
            ["sprk_projectnotes"] = ("Memo", true),
            ["sprk_projectnumber"] = ("String", true),
            ["sprk_clientreference"] = ("String", true),
            ["sprk_billingstatus"] = ("Picklist", true),
            ["sprk_searchindexname"] = ("String", true),
            ["sprk_containerid"] = ("String", true),
            ["sprk_regardingrecordid"] = ("String", true),
            ["sprk_issecure"] = ("Boolean", true),
            ["sprk_projecttype_ref"] = ("Lookup", true),
            ["sprk_assignedtointernal"] = ("Lookup", true),
            ["sprk_createdbyperson"] = ("Lookup", true),
            ["sprk_regardingrecordtype"] = ("Lookup", true),
            ["sprk_client"] = ("Lookup", true),
            ["sprk_ai_search_index"] = ("Lookup", true),
            ["ownerid"] = ("Owner", true),
        };

        // The caller may create matters and projects (and set their lookups) and use every reference table.
        Held.UnionWith(
        [
            "prvCreatesprk_Matter", "prvAppendsprk_Matter", "prvCreatesprk_Project", "prvAppendsprk_Project",
            "prvAppendTosprk_Matter", "prvAppendTosprk_Project", "prvAppendToContact", "prvAppendToAccount",
            "prvAppendTosprk_MatterType_Ref", "prvAppendTosprk_PracticeArea_Ref", "prvAppendTosprk_ProjectType_Ref",
            "prvAppendTosprk_RecordType_Ref",
        ]);

        // Rows: the suites' users, business units and default team, contacts, accounts, references and sources.
        User(OfficeQuickCreateWorld.ContractCaller, OfficeQuickCreateWorld.ContractBusinessUnit);
        User(OfficeQuickCreateWorld.FieldsCaller, OfficeQuickCreateWorld.FieldsBusinessUnit);
        User(OfficeQuickCreateWorld.FieldSecurityCaller, businessUnit: null);
        User(OfficeQuickCreateWorld.Issue1079Caller, businessUnit: null);
        WithRow("businessunit", OfficeQuickCreateWorld.ContractBusinessUnit,
            new() { ["businessunitid"] = OfficeQuickCreateWorld.ContractBusinessUnit });
        WithRow("businessunit", OfficeQuickCreateWorld.FieldsBusinessUnit,
            new() { ["businessunitid"] = OfficeQuickCreateWorld.FieldsBusinessUnit });
        WithRow("team", OfficeQuickCreateWorld.DefaultTeam,
            new() { ["_businessunitid_value"] = OfficeQuickCreateWorld.ContractBusinessUnit });
        WithRow("contact", OfficeQuickCreateWorld.ChosenContact);
        WithRow("contact", OfficeQuickCreateWorld.MakersContact);
        WithRow("account", OfficeQuickCreateWorld.ContractAccount);
        WithRow("account", OfficeQuickCreateWorld.FieldSecurityAccount);
        WithRow("sprk_mattertype_ref", OfficeQuickCreateWorld.ContractMatterType);
        WithRow("sprk_mattertype_ref", OfficeQuickCreateWorld.FieldSecurityMatterType);
        WithRow("sprk_mattertype_ref", OfficeQuickCreateWorld.FieldsMatterType);
        WithRow("sprk_practicearea_ref", OfficeQuickCreateWorld.PracticeArea);
        WithRow("sprk_projecttype_ref", OfficeQuickCreateWorld.ProjectType);
        WithRow("sprk_recordtype_ref", RecordTypeCatalogSetup.MatterTypeId, new() { ["sprk_recordlogicalname"] = "sprk_matter" });
        WithRow("sprk_recordtype_ref", RecordTypeCatalogSetup.ProjectTypeId, new() { ["sprk_recordlogicalname"] = "sprk_project" });
        WithRow("sprk_project", OfficeQuickCreateWorld.ContractSourceProject);
        WithRow("sprk_project", OfficeQuickCreateWorld.FieldSecuritySourceProject);
    }

    private void Table(string table, string entitySet, string ownership, string privilegeSchema) =>
        Tables[table] = (entitySet, ownership, privilegeSchema, table + "id");

    private void User(Guid id, Guid? businessUnit) =>
        WithRow("systemuser", id, businessUnit is { } unit ? new() { ["_businessunitid_value"] = unit } : new());
}
