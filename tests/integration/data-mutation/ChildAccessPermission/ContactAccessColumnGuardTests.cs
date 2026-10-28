using System.Reflection;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ChildAccessPermission;

/// <summary>
/// ISS-005: every contact column the BFF's typed identity writers write is classified, so a new one cannot reach an app-only
/// generic writer (<see cref="InheritedAccessPermission.RefuseAppOnlyWrite"/>) unnoticed. Each is in
/// <see cref="InheritedAccessPermission.AccessColumns"/> for <c>contact</c> (refused with
/// <see cref="InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode"/>), refused on every table by the owner rule
/// (<see cref="InheritedAccessPermission.OwnerColumns"/>), or on <see cref="OrdinaryContactColumns"/> with its reason.
/// Two sources, so neither a new constant nor a new payload key slips past:
/// <list type="bullet">
/// <item>every <c>const string</c> whose value starts with <c>sprk_</c> on the identity writers and readers
/// (<see cref="ScannedTypes"/>), found by reflection; a constant naming another table's column or a table is listed in
/// <see cref="NotContactColumns"/> with where it is checked;</item>
/// <item>every key the identity store's pure contact payload builders emit (<see cref="ContactPayloadKeys"/>).</item>
/// </list>
/// Not reachable by either: the invite create in <c>ContactIdentityBinder</c> builds its payload inline
/// (<c>emailaddress1</c>, <c>firstname</c>, <c>lastname</c>, <c>ownerid@odata.bind</c>), all classified below.
/// </summary>
public class ContactAccessColumnGuardTests
{
    /// <summary>The types that declare the contact identity columns the typed writers write or the readers decide on.</summary>
    private static readonly Type[] ScannedTypes =
    [
        typeof(ContactBindingDecision),
        typeof(DataverseContactIdentityStore),
        typeof(SubjectStandingGrantReader),
        typeof(ContactIdentityBinder),
        typeof(ContactCreateOwner),
    ];

    /// <summary>
    /// Ordinary contact data the identity writers also send. Each is a person's own profile detail: no access decision reads
    /// it to decide who a contact is or what it may see.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> OrdinaryContactColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["firstname"] = "the person's given name, shown in lists; no decision reads it",
            ["lastname"] = "the person's family name, shown in lists; no decision reads it",
            ["department"] = "the registration form's optional department; no decision reads it",
            ["emailaddress1"] = "the contact email: an email bind needs the field-secured invite stamp "
                + "(sprk_externalinvitedemailhash) to match both the token email and this column, so it never binds alone",
        };

    /// <summary>
    /// Scanned constants that are not contact columns, each with where its own refusal is checked. Keyed by the constant's
    /// declaring type and name, so a new constant with the same value is still classified on its own.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> NotContactColumns = new Dictionary<string, string>
    {
        [$"{nameof(ContactBindingDecision)}.{nameof(ContactBindingDecision.HomeTenantIdColumn)}"] = "systemuser",
        [$"{nameof(ContactBindingDecision)}.{nameof(ContactBindingDecision.HomeObjectIdColumn)}"] = "systemuser",
        [$"{nameof(DataverseContactIdentityStore)}.{nameof(DataverseContactIdentityStore.HomeTenantIdKeyColumn)}"] = "systemuser",
        [$"{nameof(DataverseContactIdentityStore)}.{nameof(DataverseContactIdentityStore.HomeObjectIdKeyColumn)}"] = "systemuser",

        // A table name, not a column: the organisation's standing grant is checked under its own AccessColumns entry.
        [$"{nameof(SubjectStandingGrantReader)}.{nameof(SubjectStandingGrantReader.OrganizationEntity)}"] = "(table)",
    };

    // ── The checker ──────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The contact columns that are neither in <paramref name="contactAccessColumns"/>, nor refused by the owner rule, nor
    /// ordinary data. Payload keys are compared in the form the refusal compares them (<c>@odata.bind</c> and
    /// <c>_{column}_value</c> reduced to the column). Pure.
    /// </summary>
    internal static IReadOnlyList<string> Unclassified(IEnumerable<string> columns, IReadOnlySet<string> contactAccessColumns) =>
        columns
            .Select(SecureRootInheritance.NormalizeColumn)
            .Where(c => !contactAccessColumns.Contains(c)
                && !InheritedAccessPermission.OwnerColumns.Contains(c)
                && !OrdinaryContactColumns.ContainsKey(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Every <c>sprk_</c> string constant on <see cref="ScannedTypes"/>, by declaring type and name.</summary>
    internal static IReadOnlyList<(string Name, string Value)> ScannedConstants() =>
        ScannedTypes
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (Name: $"{f.DeclaringType!.Name}.{f.Name}", Value: (string)f.GetRawConstantValue()!))
            .Where(c => c.Value.StartsWith("sprk_", StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>Every key the identity store's pure contact payload builders emit, over each plane and form.</summary>
    internal static IReadOnlyList<string> ContactPayloadKeys()
    {
        var oid = Guid.Parse("00500000-0000-0000-0000-0000000000a1");
        var tenant = Guid.Parse("00500000-0000-0000-0000-0000000000b1");
        var team = Guid.Parse("00500000-0000-0000-0000-0000000000c1");
        var now = new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero);
        var details = new NewContactDetails("Ada", "Lovelace", "ada@example.test") { Department = "Legal", OwningTeamId = team };

        Dictionary<string, object?>[] payloads =
        [
            DataverseContactIdentityStore.BindPayload(oid, IdentityPlaneMarker.External),
            DataverseContactIdentityStore.BindAndConsumeInvitePayload(oid, IdentityPlaneMarker.Workforce),
            DataverseContactIdentityStore.HomeBindPayload(tenant, oid),
            DataverseContactIdentityStore.HomeBindAndConsumeInvitePayload(tenant, oid),
            DataverseContactIdentityStore.CreatePayload(oid, IdentityPlaneMarker.Workforce, details),
            DataverseContactIdentityStore.CreatePayload(
                oid, IdentityPlaneMarker.WorkforceHome, details with { ExternalTenantId = tenant }),
            DataverseContactIdentityStore.InviteMarkerPayload(now, "v1.code", new InviteStamp("v1.email", null)),
            DataverseContactIdentityStore.InviteMarkerPayload(now, null, new InviteStamp("v1.email", tenant)),
            DataverseContactIdentityStore.PendingInviteClearPayload(),
            DataverseContactIdentityStore.FlagPayload(
                new CollisionFlag(now, oid, IdentityPlaneMarker.External, IdentityCollisionReason.EmailAmbiguous)),
            DataverseContactIdentityStore.ClearFlagPayload(),
            DataverseContactIdentityStore.RemovedMarkerPayload(now),
            DataverseContactIdentityStore.RemovedMarkerPayload(null),
            DataverseContactIdentityStore.RemovedPersonAccessStatePayload(),
            DataverseContactIdentityStore.StandingGrantClearPayload(),
        ];

        return payloads.SelectMany(p => p.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ── The guard ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryContactColumnConstant_IsAnAccessColumn_OrClassified()
    {
        var constants = ScannedConstants();
        constants.Should().Contain(c => c.Value == ContactBindingDecision.ExternalAccessRemovedOnColumn, "the scan reaches the types");

        var contact = constants.Where(c => !NotContactColumns.ContainsKey(c.Name)).Select(c => c.Value);
        Unclassified(contact, InheritedAccessPermission.AccessColumns["contact"]).Should().BeEmpty(
            "every contact column constant is an access column, refused by the owner rule, or ordinary data with a reason");
    }

    [Fact]
    public void EveryScannedConstantOnAnotherTable_IsAnAccessColumnThere()
    {
        var constants = ScannedConstants().ToDictionary(c => c.Name, c => c.Value);
        foreach (var (name, table) in NotContactColumns)
        {
            constants.Should().ContainKey(name, "a classified constant that no longer exists is removed from the list");
            if (table == "(table)") continue;
            InheritedAccessPermission.AccessColumns[table].Should().Contain(constants[name], $"{name} on {table}");
        }
    }

    [Fact]
    public void EveryKeyTheContactPayloadBuildersWrite_IsAnAccessColumn_OrClassified()
    {
        var keys = ContactPayloadKeys();
        keys.Should().Contain(ContactBindingDecision.ExternalInvitedTenantIdColumn, "the builders are reached");

        Unclassified(keys, InheritedAccessPermission.AccessColumns["contact"]).Should().BeEmpty(
            "every column the identity store writes on a contact is an access column, refused by the owner rule, or ordinary "
            + "data with a reason");
    }

    [Fact]
    public void EveryOrdinaryContactColumn_IsWrittenByTheBinderOrTheStore_AndRefusedByNoRule()
    {
        // The allow-list stays as small as what the writers send: an entry nothing writes is removed.
        var keys = ContactPayloadKeys().Concat(["emailaddress1", "firstname", "lastname"]); // the binder's inline invite create
        foreach (var column in OrdinaryContactColumns.Keys)
        {
            keys.Should().Contain(column);
            InheritedAccessPermission.RefuseAppOnlyWrite("contact", [column]).Should().BeNull($"{column} is ordinary data");
        }
    }

    // ── The checker's controls ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MustFire_AColumnMissingFromTheSet_IsReported()
    {
        var withoutRemovedMarker = new HashSet<string>(InheritedAccessPermission.AccessColumns["contact"], StringComparer.OrdinalIgnoreCase);
        withoutRemovedMarker.Remove(ContactBindingDecision.ExternalAccessRemovedOnColumn);

        var contact = ScannedConstants().Where(c => !NotContactColumns.ContainsKey(c.Name)).Select(c => c.Value);
        Unclassified(contact, withoutRemovedMarker).Should().Equal(ContactBindingDecision.ExternalAccessRemovedOnColumn);
        Unclassified(ContactPayloadKeys(), withoutRemovedMarker).Should().Equal(ContactBindingDecision.ExternalAccessRemovedOnColumn);
        Unclassified(["sprk_somethingnew"], InheritedAccessPermission.AccessColumns["contact"]).Should().Equal("sprk_somethingnew");
    }

    [Fact]
    public void MustNotFire_AnOrdinaryColumn_AndTheOwnerBind_AreNotReported()
    {
        var empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Unclassified(["firstname", "EmailAddress1", "lastname", "department"], empty).Should().BeEmpty();
        Unclassified(["ownerid@odata.bind"], empty).Should().BeEmpty("the owner rule refuses it on every table");
        Unclassified(["_sprk_externalaccessremovedon_value"], InheritedAccessPermission.AccessColumns["contact"])
            .Should().BeEmpty("a key is compared in the form the refusal compares it");
    }
}
