using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Api.FieldMappings.Dtos;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ChildAccessPermission;

/// <summary>
/// #2251 (uac-r2): the tables whose rows ARE access (<see cref="InheritedAccessPermission.AccessTables"/>: grants,
/// organisation membership, ethical walls, the Assigned-To ledger, team membership) and a secure root's secure flag, access
/// columns and container (<see cref="InheritedAccessPermission.SecureRootColumns"/>) are written only by the BFF's typed
/// writers. An app-only generic writer never writes them (<see cref="InheritedAccessPermission.RefuseAppOnlyWrite"/>, reasons
/// <see cref="InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode"/> and
/// <see cref="InheritedAccessPermission.AppOnlySecureRootWriteReasonCode"/>): the Update Record playbook node and the action
/// seam acting for no person (<see cref="UpdateRecordActionCore"/>) and a playbook's output mapping
/// (<see cref="DataverseUpdateHandler"/>). The field-mapping push runs as its caller and asks only the table-name check and
/// the refusals it asked before #2251 (#2253 fix round 1, K2). The BFF app user is a
/// System Administrator, so before this a playbook could point a grant at another contact, move a contact into an
/// organisation, repoint an ethical wall, un-secure a matter or point it at another root's container. Must fire: every
/// access table, in any case and with whitespace, with any column; every secure-root column on every root, in its plain,
/// case-variant, <c>@odata.bind</c> and <c>_{column}_value</c> forms. Must not fire: ordinary columns on a root (the live
/// <c>sprk_matter.sprk_performancesummary</c> node), the same columns on other tables, neighbouring table names. Drift: every
/// root the registry and the grant model name is covered; the tables the access code names are scanned from source by
/// <c>Spaarke.ArchTests.AccessTableReaderScanTests</c>. #2253 fix round 1 adds: a table that is not a bare identifier is
/// refused first (<see cref="InheritedAccessPermission.AppOnlyTableNameInvalidReasonCode"/>, F1-a), and a non-root table's
/// access columns (<see cref="InheritedAccessPermission.AccessColumns"/>, F1-b). #2253 fix round 4 adds the mailbox
/// configuration, the AI search index allow-list, the membership junction and the SPE admin audit trail (refused whole), and
/// a proposal's message (<c>sprk_emailreviewlog.sprk_communication</c>, refused per column). #2253 fix round 5 adds a
/// business unit's parent (<c>businessunit.parentbusinessunitid</c>, refused per column, F4) and the triage taxonomy
/// (<c>sprk_triagecategory</c>, refused whole, K1).
/// </summary>
public class AccessTableAppOnlyWriterTests
{
    private static readonly Guid Row = Guid.Parse("22510000-0000-0000-0000-00000000e401");
    private static readonly Guid Caller = Guid.Parse("22510000-0000-0000-0000-00000000e4ca");
    private static readonly Guid Forged = Guid.Parse("22510000-0000-0000-0000-00000000e4ff");

    private static readonly string[] Tables =
    [
        "sprk_externalrecordaccess", "sprk_contactorganization", "sprk_noaccessentry", "sprk_assignedaccess", "teammembership",
        "fieldpermission",

        // #2253 fix round 3: F3 (the record-type catalogue) and the wider reader scan (K).
        "sprk_recordtype_ref", "sprk_communicationparticipant", "sprk_communicationchannelref", "sprk_specontainertypeconfig",
        "sprk_speenvironment",

        // #2253 fix round 4: F-2 (mailbox configuration), K-4 (index allow-list, membership junction), K-2 (SPE audit).
        "sprk_communicationaccount", "sprk_aisearchindex", "sprk_userentityassociation", "sprk_speauditlog",

        // #2253 fix round 5 (K1): the classifier taxonomy whose category routes an unfiled communication's owner.
        "sprk_triagecategory",
    ];

    /// <summary>#2253 fix round 2: the platform tables that grant or scope access (<c>PlatformSecurityTables</c>).</summary>
    private static readonly string[] PlatformTables =
    [
        "role", "roleprivileges", "systemuserroles", "teamroles", "fieldsecurityprofile", "systemuserprofiles", "teamprofiles",
        "principalobjectaccess", "teamtemplate", "hierarchysecurityconfiguration", "position",
    ];

    /// <summary>#2253 fix round 1 (F1-b): each non-root table's access columns, as the readers name them.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> TableAccessColumns = new Dictionary<string, string[]>
    {
        ["contact"] =
        [
            "sprk_externalobjectid", "sprk_externalobjectidkey", "sprk_identityplane", "sprk_externaltenantid",
            "sprk_externalinvitedon", "sprk_externalinvitecodehash", "sprk_standinggrant", "sprk_accesspermissiongrant",
        ],
        ["sprk_organization"] = ["sprk_standinggrant", "sprk_accesspermissiongrant"],
        ["systemuser"] =
        [
            "sprk_hometenantid", "sprk_homeobjectid", "sprk_hometenantidkey", "sprk_homeobjectidkey", "sprk_primarycontact",
            "sprk_isexternal", "businessunitid", "azureactivedirectoryobjectid", "domainname", "internalemailaddress",
            "isdisabled", "accessmode", "applicationid", "parentsystemuserid", "positionid",
        ],
        ["team"] = ["businessunitid", "name", "teamtype", "isdefault"],
        ["businessunit"] = ["sprk_containerid", "name", "parentbusinessunitid"], // #2253 fix round 5 (F4): the parent

        // #2253 fix round 3 (K): found by the wider reader scan.
        ["sprk_communication"] = ["sprk_isinternalonly"],
        ["sprk_communicationthread"] = ["sprk_privacystate", "sprk_threadtype"],
        ["sprk_analysis"] =
        [
            "sprk_documentid", "sprk_outputfileid", "sprk_regardingdocument", "sprk_regardingmatter", "sprk_regardingproject",
            "sprk_regardingworkassignment", "sprk_regardinginvoice", "sprk_regardingbudget", "sprk_regardingcommunication",
            "sprk_regardingservicerequest",
        ],
        ["sprk_communicationattachment"] = ["sprk_graphdriveid", "sprk_graphitemid", "sprk_communication", "sprk_document"],

        // #2253 fix round 4 (F-1): the message a proposal belongs to decides who sees and applies it.
        ["sprk_emailreviewlog"] = ["sprk_communication"],

        // ebilling-r1 (master merge): the firm and core record scope the external submission reads.
        // UAC review F4 (on e371dce6bf): + its invoice and tracking number, which D-176's Duplicate withholding reads.
        ["sprk_invoicesubmission"] =
        [
            "sprk_vendororg", "sprk_regardingmatter", "sprk_regardingproject", "sprk_regardingworkassignment",
            "sprk_invoice", "sprk_trackingnumber",
        ],
    };

    /// <summary>
    /// #2253 fix round 1 (F1-a): names that are not a bare identifier after trimming. The first is the reviewed attack: the
    /// metadata URL would answer for <c>sprk_externalrecordaccess</c> while every set saw an unknown string.
    /// </summary>
    public static TheoryData<string?> CraftedTableNames() => new()
    {
        "sprk_externalrecordaccess')?$select=EntitySetName#",
        "sprk_matter')?$select=EntitySetName#",
        "sprk_externalrecordaccess#",
        "sprk_externalrecordaccess'",
        "sprk_externalrecordaccess/../sprk_matter",
        "sprk_external recordaccess",
        "sprk_external\nrecordaccess",
        "sprk_matter?x=1",
        "sprk_matter(00000000-0000-0000-0000-000000000001)",
        "_sprk_matter",
        "1sprk_matter",
        "sprk_m\u00e4tter",
        "sprk_matter%27",
        "",
        "   ",
        null,
    };

    private static readonly string[] Roots = ["sprk_matter", "sprk_project", "sprk_workassignment"];

    private static readonly string[] RootColumns =
        ["sprk_issecure", "sprk_accesspermission", "sprk_accessinheritance", "sprk_securitybu", "sprk_containerid"];

    private readonly Mock<IFieldMappingDataverseService> _writes = new();

    /// <summary>Each access table in each spelling, with a column of each kind a configuration names.</summary>
    public static TheoryData<string, string> EveryAccessTableForm()
    {
        var data = new TheoryData<string, string>();
        foreach (var table in Tables)
        {
            foreach (var spelling in new[] { table, table.ToUpperInvariant(), $"  {table}\t", $" {char.ToUpperInvariant(table[0])}{table[1..]} " })
            {
                data.Add(spelling, "sprk_name");
                data.Add(spelling, "statecode");
                data.Add(spelling, "sprk_Contact@odata.bind");
                data.Add(spelling, "_sprk_organization_value");
            }
        }

        return data;
    }

    /// <summary>Each secure-root column in each form a payload key can take, on each root, with the column it names.</summary>
    public static TheoryData<string, string, string> EverySecureRootForm()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var root in Roots)
        {
            foreach (var column in RootColumns)
            {
                data.Add(root, column, column);
                data.Add($" {root.ToUpperInvariant()} ", $" {column.ToUpperInvariant()} ", column);
                data.Add(root, $"{column}@odata.bind", column);
                data.Add(root, $"_{column}_value", column);
            }

            // The navigation-property spelling a configuration writes for the business-unit lookup.
            data.Add(root, "sprk_SecurityBU@odata.bind", "sprk_securitybu");
        }

        return data;
    }

    /// <summary>A case of each rule, on the tables the end-to-end writers are driven over.</summary>
    public static TheoryData<string, string, string> WriterCases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var table in Tables)
            data.Add(table, "sprk_name", InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode);
        data.Add(" SPRK_ExternalRecordAccess ", "sprk_accesslevel", InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode);
        foreach (var root in Roots)
        {
            data.Add(root, "sprk_issecure", InheritedAccessPermission.AppOnlySecureRootWriteReasonCode);
            data.Add(root, "sprk_containerid", InheritedAccessPermission.AppOnlySecureRootWriteReasonCode);
            data.Add(root, "sprk_SecurityBU@odata.bind", InheritedAccessPermission.AppOnlySecureRootWriteReasonCode);
            data.Add(root, "SPRK_AccessPermission", InheritedAccessPermission.AppOnlySecureRootWriteReasonCode);
        }

        // #2253 fix round 1: a non-root table's access columns, and the crafted table name of F1-a.
        data.Add("contact", "sprk_externalobjectid", InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
        data.Add("contact", "sprk_standinggrant", InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
        data.Add("sprk_organization", "sprk_accesspermissiongrant", InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
        data.Add("systemuser", "sprk_isexternal", InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
        data.Add("businessunit", "sprk_containerid", InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
        data.Add("systemuser", "businessunitid", InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
        data.Add("systemuser", "businessunitid@odata.bind", InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
        data.Add("team", "businessunitid", InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
        data.Add("role", "name", InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode);
        // #2253 fix round 3: F3, and the wider reader scan's tables and columns.
        data.Add("sprk_recordtype_ref", "sprk_recordlogicalname", InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode);
        data.Add("sprk_communication", "sprk_isinternalonly", InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
        data.Add("sprk_communicationthread", "sprk_privacystate", InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
        data.Add("sprk_analysis", "sprk_regardingmatter@odata.bind", InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
        data.Add("sprk_communicationattachment", "sprk_graphitemid", InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
        data.Add("principalobjectaccess", "accessrightsmask", InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode);
        // #2253 fix round 4: F-1 in each form, and F-2's mailbox columns.
        data.Add("sprk_emailreviewlog", "sprk_communication", InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
        data.Add("sprk_emailreviewlog", "sprk_Communication@odata.bind", InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
        data.Add("sprk_emailreviewlog", "_sprk_communication_value", InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
        data.Add("sprk_communicationaccount", "sprk_receiveenabled", InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode);
        data.Add("sprk_aisearchindex", "sprk_searchindexname", InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode);
        // #2253 fix round 5: F4 (re-parenting a business unit) in each form, and K1's classifier guidance.
        data.Add("businessunit", "parentbusinessunitid", InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
        data.Add("businessunit", "parentbusinessunitid@odata.bind", InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
        data.Add("businessunit", "_parentbusinessunitid_value", InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
        data.Add("sprk_triagecategory", "sprk_classifierguidance", InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode);
        data.Add("sprk_externalrecordaccess')?$select=EntitySetName#", "sprk_name",
            InheritedAccessPermission.AppOnlyTableNameInvalidReasonCode);
        data.Add("sprk_matter')?$select=EntitySetName#", "sprk_issecure", InheritedAccessPermission.AppOnlyTableNameInvalidReasonCode);

        return data;
    }

    // ── The refusal itself ─────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryAccessTableForm))]
    public void AnAccessTable_InAnySpelling_IsRefusedWhole(string table, string key)
    {
        var refusal = InheritedAccessPermission.RefuseAppOnlyWrite(table, [key]);

        refusal.Should().NotBeNull();
        refusal!.RefusalCode.Should().Be(InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode);
        refusal.Reason.Should().StartWith(table.Trim().ToLowerInvariant() + " records ");
    }

    [Fact]
    public void AnAccessTable_IsRefused_EvenWhenThePayloadNamesNoColumn()
    {
        foreach (var table in Tables)
            Code(table).Should().Be(InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode, table);
    }

    [Theory]
    [MemberData(nameof(EverySecureRootForm))]
    public void ASecureRootColumn_InAnyForm_IsRefusedOnEveryRoot(string table, string key, string column)
    {
        var refusal = InheritedAccessPermission.RefuseAppOnlyWrite(table, ["sprk_description", key]);

        refusal.Should().NotBeNull();
        refusal!.RefusalCode.Should().Be(InheritedAccessPermission.AppOnlySecureRootWriteReasonCode);
        refusal.Reason.Should().StartWith(column + " of a secure root ");
    }

    [Theory]
    [InlineData("sprk_description")]
    [InlineData("sprk_performancesummary")]
    [InlineData("sprk_name")]
    [InlineData("sprk_issecurex")]
    [InlineData("sprk_issecurename")]
    [InlineData("sprk_securitybuname")]
    [InlineData("sprk_containeridx")]
    [InlineData("sprk_accesspermissionname")]
    [InlineData("sprk_accesspermissiongrant")]
    [InlineData("sprk_regardingmatter")]
    public void AnOrdinaryColumnOnARoot_IsNotRefused(string key)
    {
        foreach (var root in Roots)
            Code(root, key).Should().BeNull($"'{key}' is not refused on {root}");
    }

    [Theory]
    [InlineData("sprk_event", "sprk_issecure")]
    [InlineData("sprk_todo", "sprk_containerid")]
    [InlineData("sprk_invoice", "sprk_issecure")]
    [InlineData("sprk_servicerequest", "sprk_securitybu")]
    [InlineData("sprk_communication", "sprk_accesspermission")]
    [InlineData("contact", "sprk_containerid")]
    [InlineData("businessunit", "sprk_name")]
    public void TheSameColumnOnANonRoot_IsNotRefusedAsASecureRoot(string table, string key) =>
        Code(table, key).Should().BeNull($"'{key}' on {table} is no secure root's column");

    [Theory]
    [InlineData("sprk_externalrecordaccesses")]
    [InlineData("sprk_externalrecordaccessx")]
    [InlineData("sprk_contactorganizationrole")]
    [InlineData("sprk_noaccess")]
    [InlineData("sprk_assignedaccessrule")]
    [InlineData("team")]
    [InlineData("contact")]
    [InlineData("sprk_organization")]
    [InlineData("sprk_matterx")]
    [InlineData("sprk_recordtype_refs")]
    [InlineData("sprk_recordtype")]
    [InlineData("sprk_eventtype_ref")]
    [InlineData("sprk_communication")]
    [InlineData("sprk_communicationthread")]
    [InlineData("sprk_communicationattachment")]
    [InlineData("sprk_analysis")]
    [InlineData("sprk_emailupdatefield")]
    // #2253 fix round 4: refused per column, not whole; and the communication tables beside the mailbox configuration.
    [InlineData("sprk_emailreviewlog")]
    [InlineData("sprk_communicationrule")]
    // #2253 fix round 5 (K1): the taxonomy's neighbours (sprk_triagecategory itself is refused whole).
    [InlineData("sprk_triagecategoryx")]
    [InlineData("sprk_triagepriority")]
    public void ANeighbouringTable_IsNotRefusedWhole(string? table)
    {
        Code(table, "sprk_name").Should().BeNull($"'{table}' is not an access table");
        Code(table, "sprk_issecure").Should().BeNull($"'{table}' is not a secure root");
    }

    [Fact]
    public void TheAccessTableSet_IsExactlyTheseTables() =>
        InheritedAccessPermission.AccessTables.Should().BeEquivalentTo(Tables.Concat(PlatformTables));

    [Fact]
    public void ThePlatformSecurityTables_AreExactlyThese_AndAllRefusedWhole()
    {
        InheritedAccessPermission.PlatformSecurityTables.Should().BeEquivalentTo(PlatformTables);
        foreach (var table in PlatformTables)
        {
            Code(table, "name").Should().Be(InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode, table);
            Code($" {table.ToUpperInvariant()} ").Should().Be(InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode, table);
            CallerCode(table, "name").Should().BeNull($"{table}: the caller's own privileges decide");
        }
    }

    [Fact]
    public void TheSecureRoots_AreExactlyTheseTables_EachWithExactlyTheseColumns()
    {
        InheritedAccessPermission.SecureRootColumns.Keys.Should().BeEquivalentTo(Roots);
        foreach (var root in Roots)
            InheritedAccessPermission.SecureRootColumns[root].Should().BeEquivalentTo(RootColumns, root);
    }

    [Fact]
    public void TheExistingRefusals_AreUnchanged_AndTheirPrecedenceHolds()
    {
        Code("sprk_document", "sprk_accesspermission").Should().Be(InheritedAccessPermission.AppOnlyDocumentWriteReasonCode);
        Code("sprk_document", "sprk_parentdocument").Should().Be(InheritedAccessPermission.AppOnlyDocumentLinkWriteReasonCode);
        Code("sprk_document", "sprk_graphitemidbound").Should().Be(InheritedAccessPermission.AppOnlyDocumentBffColumnWriteReasonCode);
        Code("sprk_event", "ownerid").Should().Be(InheritedAccessPermission.AppOnlyOwnerWriteReasonCode);
        Code("sprk_event", "sprk_createdbyperson").Should().Be(InheritedAccessPermission.AppOnlyCreatorWriteReasonCode);
        Code("sprk_document", "sprk_issecure", "sprk_containerid").Should().BeNull("a document is no secure root");

        // An access table comes first, whatever else the payload names; then a secure root's columns before the owner and
        // the creator.
        Code("sprk_externalrecordaccess", "ownerid", "sprk_createdbyperson")
            .Should().Be(InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode);
        Code("sprk_matter", "sprk_createdbyperson", "ownerid", "sprk_issecure")
            .Should().Be(InheritedAccessPermission.AppOnlySecureRootWriteReasonCode);
        Code("sprk_matter", "sprk_createdbyperson", "ownerid").Should().Be(InheritedAccessPermission.AppOnlyOwnerWriteReasonCode);
    }

    // ── Drift ────────────────────────────────────────────────────────────────────────────────────────────────────
    // #2253 fix round 1 (K3): which tables the access code names is scanned from source by
    // Spaarke.ArchTests.AccessTableReaderScanTests (refused whole here, or listed there with a reason).

    [Fact]
    public void EveryRootTheRegistryAndTheGrantModelName_IsASecureRoot()
    {
        var roots = RecordTableRules.All.Where(r => r.Kind == RecordTableKind.Root).Select(r => r.Table)
            .Concat(Enum.GetValues<ExternalGrantRootType>().Select(ExternalGrantRoot.LogicalNameFor))
            .Concat([SecureRootInheritance.Matter, SecureRootInheritance.Project, SecureRootInheritance.WorkAssignment]);

        roots.Where(r => !InheritedAccessPermission.SecureRootColumns.ContainsKey(r)).Should().BeEmpty();
    }

    [Fact]
    public void EverySecureRootColumnTheCreateProtects_AndTheContainer_IsRefusedOnEveryRoot()
    {
        // One source of truth with #2220's create side, plus the readers' own constants.
        string[] columns =
        [
            .. RecordTableRules.SecureRootAccessColumns,
            RecordContainerResolver.ContainerColumn,
            SecurableEntityRegistry.SecureFlagAttribute,
            SecurableEntityRegistry.SecurityBusinessUnitAttribute,
            InheritedAccessPermission.Column,
            AccessInheritance.Column,
        ];
        foreach (var root in Roots)
        {
            foreach (var column in columns)
                Code(root, column).Should().Be(InheritedAccessPermission.AppOnlySecureRootWriteReasonCode, $"{column} on {root}");
        }
    }

    private static string? Code(string? table, params string[] keys) =>
        InheritedAccessPermission.RefuseAppOnlyWrite(table, keys)?.RefusalCode;

    private static string? CallerCode(string? table, params string[] keys) =>
        InheritedAccessPermission.RefuseAppOnlyWrite(table, keys, InheritedAccessPermission.WriteIdentity.Caller)?.RefusalCode;

    // ── #2253 fix round 1 (F1-a): a table that is not a bare identifier ─────────────────────────────────────────

    [Theory]
    [MemberData(nameof(CraftedTableNames))]
    public void ATableThatIsNotAnIdentifier_IsRefusedFirst_ForEveryIdentity(string? table)
    {
        // Named with the columns of every other refusal: the name check answers before any of them.
        string[] keys = ["sprk_name", "sprk_issecure", "ownerid", "sprk_externalobjectid"];

        Code(table, keys).Should().Be(InheritedAccessPermission.AppOnlyTableNameInvalidReasonCode);
        CallerCode(table, keys).Should().Be(InheritedAccessPermission.AppOnlyTableNameInvalidReasonCode);
        Code(table).Should().Be(InheritedAccessPermission.AppOnlyTableNameInvalidReasonCode, "an empty payload too");
    }

    [Theory]
    [InlineData("sprk_matter")]
    [InlineData("  sprk_Matter\t")]
    [InlineData("SPRK_EVENT")]
    [InlineData("sprk_eventtype_ref")] // was sprk_recordtype_ref, refused whole since #2253 fix round 3 (F3)
    [InlineData("account")]
    [InlineData("sprk_x2")]
    public void AnIdentifierTable_PassesTheNameCheck(string table)
    {
        Code(table, "sprk_description").Should().BeNull();
        CallerCode(table, "sprk_description").Should().BeNull();
    }

    // ── #2253 fix round 3: F3 (sprk_recordtype_ref) and the wider reader scan's whole tables ─────────────────────

    [Fact]
    public void TheNewWholeTables_AreTheBffsOwnConstants_AndRefusedOnlyToAnAppOnlyWriter()
    {
        // The constant the BFF names each table by: a reader's where one exists; for the membership junction it is its
        // writer's (MembershipJunctionUpdater), the only constant naming that table (#2253 fix round 5, K3).
        string[] constants =
        [
            SecureRootInheritance.RecordTypeRefEntity,
            Sprk.Bff.Api.Services.Communication.CommunicationFanOutTargetingService.ParticipantEntity,
            Sprk.Bff.Api.Services.Communication.Membership.MembershipReconciler.ChannelRefEntity,
            // #2253 fix round 4 (K-4).
            DataverseAllowedIndexesProvider.CatalogEntity,
            Sprk.Bff.Api.Services.Ai.Membership.MembershipJunctionUpdater.JunctionEntityLogicalName,
        ];
        foreach (var table in constants)
        {
            Code(table, "sprk_name").Should().Be(InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode, table);
            CallerCode(table, "sprk_name").Should().BeNull($"{table}: the caller's own privileges decide");
        }
    }

    [Theory]
    [InlineData("sprk_recordlogicalname")]
    [InlineData("sprk_recorddisplayname")]
    [InlineData("sprk_SprkRecordLogicalName@odata.bind")]
    public void TheRecordTypeCatalogue_IsRefusedWhole_WhateverColumnIsNamed(string key)
    {
        // F3: sprk_recordlogicalname names the table a polymorphic pair points at, and the secure-root walk, container
        // resolution and the No Access walls read it, so no app-only writer may touch a catalogue row.
        Code("sprk_recordtype_ref", key).Should().Be(InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode);
        Code(" SPRK_RecordType_Ref ", key).Should().Be(InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode);
    }

    [Theory]
    [InlineData("sprk_emailaddress")]
    [InlineData("sprk_receiveenabled")]
    [InlineData("sprk_autocreaterecords")]
    [InlineData("sprk_sendstoday")]
    [InlineData("sprk_SubscriptionId")]
    public void TheMailboxConfiguration_IsRefusedWhole_ToAnAppOnlyWriter_NotToTheCaller(string key)
    {
        // #2253 fix round 4 (F-2): which mailbox the BFF subscribes to and ingests is decided by these rows, read app-only.
        Code("sprk_communicationaccount", key).Should().Be(InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode);
        Code(" SPRK_CommunicationAccount ", key).Should().Be(InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode);
        CallerCode("sprk_communicationaccount", key).Should().BeNull("the caller's own privileges decide");
    }

    [Theory]
    [InlineData("sprk_name")]
    [InlineData("sprk_classifierguidance")]
    [InlineData("sprk_enabled")]
    [InlineData("statecode")]
    public void TheTriageTaxonomy_IsRefusedWhole_ToAnAppOnlyWriter_NotToTheCaller(string key)
    {
        // #2253 fix round 5 (K1): the offered rows are the classifier's choices and guidance, and the category it picks
        // routes an unfiled communication's owner (CategoryRoutingGate), so no app-only writer may touch a row.
        Code("sprk_triagecategory", key).Should().Be(InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode);
        Code(" SPRK_TriageCategory ", key).Should().Be(InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode);
        CallerCode("sprk_triagecategory", key).Should().BeNull("the caller's own privileges decide");
    }

    // ── #2253 fix round 1 (F1-b): a non-root table's access columns ──────────────────────────────────────────────

    /// <summary>Each access column on its table, in each form a payload key can take.</summary>
    public static TheoryData<string, string, string> EveryAccessColumnForm()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (table, columns) in TableAccessColumns)
        {
            foreach (var column in columns)
            {
                data.Add(table, column, column);
                data.Add($" {table.ToUpperInvariant()} ", $" {column.ToUpperInvariant()} ", column);
                data.Add(table, $"{column}@odata.bind", column);
                data.Add(table, $"_{column}_value", column);
            }
        }

        // The navigation-property spelling a configuration writes for the task-141 link, and for a proposal's message.
        data.Add("systemuser", "sprk_PrimaryContact@odata.bind", "sprk_primarycontact");
        data.Add("sprk_emailreviewlog", "sprk_Communication@odata.bind", "sprk_communication");
        data.Add("businessunit", "ParentBusinessUnitId@odata.bind", "parentbusinessunitid");
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryAccessColumnForm))]
    public void AnAccessColumn_InAnyForm_IsRefusedOnItsTable(string table, string key, string column)
    {
        var refusal = InheritedAccessPermission.RefuseAppOnlyWrite(table, ["sprk_description", key]);

        refusal.Should().NotBeNull();
        refusal!.RefusalCode.Should().Be(InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
        refusal.Reason.Should().StartWith($"{column} of a {table.Trim().ToLowerInvariant()} ");
    }

    [Fact]
    public void TheAccessColumnMap_IsExactlyTheseColumns()
    {
        InheritedAccessPermission.AccessColumns.Keys.Should().BeEquivalentTo(TableAccessColumns.Keys);
        foreach (var (table, columns) in TableAccessColumns)
            InheritedAccessPermission.AccessColumns[table].Should().BeEquivalentTo(columns, table);
    }

    [Fact]
    public void TheAccessColumnMap_IsBuiltFromTheReadersOwnConstants()
    {
        // One source of truth with each reader: a renamed reader constant drifts here.
        (string Table, string Column)[] readers =
        [
            (SubjectStandingGrantReader.ContactEntity, ContactBindingDecision.ExternalObjectIdColumn),
            (SubjectStandingGrantReader.ContactEntity, ContactBindingDecision.KeyMirrorColumn),
            (SubjectStandingGrantReader.ContactEntity, ContactBindingDecision.IdentityPlaneColumn),
            (SubjectStandingGrantReader.ContactEntity, ContactBindingDecision.ExternalTenantIdColumn),
            (SubjectStandingGrantReader.ContactEntity, ContactBindingDecision.ExternalInvitedOnColumn),
            (SubjectStandingGrantReader.ContactEntity, ContactBindingDecision.ExternalInviteCodeHashColumn),
            (SubjectStandingGrantReader.ContactEntity, SubjectStandingGrantReader.StandingGrantAttribute),
            (SubjectStandingGrantReader.ContactEntity, SubjectStandingGrantReader.BaselineAttribute),
            (SubjectStandingGrantReader.OrganizationEntity, SubjectStandingGrantReader.StandingGrantAttribute),
            (SubjectStandingGrantReader.OrganizationEntity, SubjectStandingGrantReader.BaselineAttribute),
            ("systemuser", ContactBindingDecision.HomeTenantIdColumn),
            ("systemuser", ContactBindingDecision.HomeObjectIdColumn),
            ("systemuser", DataverseContactIdentityStore.HomeTenantIdKeyColumn),
            ("systemuser", DataverseContactIdentityStore.HomeObjectIdKeyColumn),
            ("systemuser", AssignedAccessStore.PrimaryContactColumn),
            ("systemuser", AssignedAccessStore.IsExternalColumn),
            ("systemuser", RecordContainerResolver.BusinessUnitLookupColumn),
            ("systemuser", RecordContainerResolver.AzureAdObjectIdColumn),
            ("systemuser", RecordContainerResolver.DomainNameColumn),
            ("systemuser", RecordContainerResolver.IsDisabledColumn),
            ("systemuser", RecordContainerResolver.AccessModeColumn),
            ("systemuser", RecordContainerResolver.ApplicationIdColumn),
            ("systemuser", DataverseContactIdentityStore.InternalEmailColumn),
            (SecureRecordOwnerTeam.TeamEntity, SecureRecordOwnerTeam.BusinessUnitColumn),
            (SecureRecordOwnerTeam.TeamEntity, SecureRecordOwnerTeam.NameColumn),
            (SecureRecordOwnerTeam.TeamEntity, SecureRecordOwnerTeam.TeamTypeColumn),
            (SecureRecordOwnerTeam.TeamEntity, SecureRecordOwnerTeam.IsDefaultColumn),
            ("businessunit", RecordContainerResolver.ContainerColumn),
            ("businessunit", SecureRecordOwnerTeam.NameColumn),
            (RecordContainerResolver.BusinessUnitEntity, RecordContainerResolver.ParentBusinessUnitColumn),
            (RecordThreadFiling.CommunicationTable, Sprk.Bff.Api.Services.Communication.CommunicationInternalOnlyFloor.InternalOnlyField),
            (Sprk.Bff.Api.Services.Communication.CommunicationInternalOnlyFloor.ThreadEntity,
                Sprk.Bff.Api.Services.Communication.CommunicationInternalOnlyFloor.ThreadPrivacyField),
            (RecordThreadFiling.ThreadTable, RecordThreadFiling.ThreadTypeColumn),
            .. Sprk.Bff.Api.Api.Filters.AnalysisAuthorizationFilter.AnchorColumns.Select(c =>
                (Sprk.Bff.Api.Api.Filters.AnalysisAuthorizationFilter.AnalysisEntityLogicalName, c)),
            (Sprk.Bff.Api.Services.Documents.DocumentContainerRelocator.AttachmentEntity,
                Sprk.Bff.Api.Services.Documents.DocumentContainerRelocator.DriveColumn),
            (Sprk.Bff.Api.Services.Documents.DocumentContainerRelocator.AttachmentEntity,
                Sprk.Bff.Api.Services.Documents.DocumentContainerRelocator.ItemColumn),
            (Sprk.Bff.Api.Services.Documents.DocumentContainerRelocator.AttachmentEntity,
                Sprk.Bff.Api.Services.Documents.DocumentContainerRelocator.AttachmentCommunicationColumn),
            (Sprk.Bff.Api.Services.Documents.DocumentContainerRelocator.AttachmentEntity,
                Sprk.Bff.Api.Services.Documents.DocumentContainerRelocator.AttachmentDocumentColumn),
            (Sprk.Bff.Api.Api.Filters.CommunicationRecordAuthorizationFilter.EmailReviewLogEntity,
                Sprk.Bff.Api.Api.Filters.CommunicationRecordAuthorizationFilter.ProposalCommunicationColumn),
            (Sprk.Bff.Api.Services.Finance.Submission.TrackingNumberAllocator.SubmissionEntity,
                Sprk.Bff.Api.Services.Finance.Submission.TrackingNumberAllocator.FirmColumn),
            .. Sprk.Bff.Api.Services.Dataverse.InvoiceFamilyCoreRecordGuard.CoreRecordLookups[
                    Sprk.Bff.Api.Services.Finance.Submission.TrackingNumberAllocator.SubmissionEntity]
                .Select(l => (Sprk.Bff.Api.Services.Finance.Submission.TrackingNumberAllocator.SubmissionEntity, l.Column)),
            (Sprk.Bff.Api.Services.Finance.Submission.TrackingNumberAllocator.SubmissionEntity,
                Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionPersister.SubmissionInvoice),
            (Sprk.Bff.Api.Services.Finance.Submission.TrackingNumberAllocator.SubmissionEntity,
                Sprk.Bff.Api.Services.Finance.Submission.TrackingNumberAllocator.TrackingNumberColumn),
        ];
        foreach (var (table, column) in readers)
            Code(table, column).Should().Be(InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode, $"{column} on {table}");
    }

    [Fact]
    public void TheColumnsTheDuplicateWithholdingReads_AreRefusedOnTheSubmission_InTheFormsItReadsThem()
    {
        // UAC review F4 (on e371dce6bf): D-176 resolves (vendor org, tracking number) to a submission and gates a Duplicate's
        // earlier-invoice detail on that submission's invoice (EarlierInvoiceReadability.ResolveByTrackingNumberAsync). The
        // reader's own Web API names, so a renamed reader constant drifts here.
        const string submission = Sprk.Bff.Api.Services.Finance.Submission.TrackingNumberAllocator.SubmissionEntity;
        foreach (var key in new[]
                 {
                     Sprk.Bff.Api.Services.Signals.EarlierInvoiceReadability.InvoiceColumn,
                     Sprk.Bff.Api.Services.Signals.EarlierInvoiceReadability.TrackingNumberColumn,
                     "sprk_invoice", "sprk_Invoice@odata.bind", " SPRK_TRACKINGNUMBER ",
                 })
            Code(submission, key).Should().Be(InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode, $"{key} on {submission}");

        // Must not fire: the same columns on the invoice itself, and the submission's ordinary columns.
        Code("sprk_invoice", "sprk_trackingnumber").Should().BeNull("the invoice's own tracking number decides no access");
        Code(submission, "sprk_processingstatus").Should().BeNull("an ordinary column");
        CallerCode(submission, "sprk_invoice").Should().BeNull("the caller's own privileges decide");
    }

    [Theory]
    [InlineData("contact", "firstname")]
    [InlineData("contact", "emailaddress1")]
    [InlineData("contact", "sprk_containerid")]
    [InlineData("contact", "sprk_isexternal")]
    [InlineData("contact", "sprk_identitycollisionon")]
    [InlineData("contact", "sprk_externalobjectidx")]
    [InlineData("sprk_organization", "sprk_name")]
    [InlineData("sprk_organization", "sprk_externalobjectid")]
    [InlineData("systemuser", "firstname")]
    [InlineData("systemuser", "sprk_standinggrant")]
    [InlineData("systemuser", "sprk_primarycontactname")]
    [InlineData("businessunit", "description")]
    [InlineData("systemuser", "lastname")]
    [InlineData("systemuser", "title")]
    [InlineData("systemuser", "mobilephone")]
    [InlineData("team", "description")]
    [InlineData("team", "emailaddress")]
    [InlineData("contact", "domainname")]
    [InlineData("account", "name")]
    [InlineData("sprk_event", "sprk_externalobjectid")]
    [InlineData("sprk_matter", "sprk_standinggrant")]
    [InlineData("account", "sprk_isexternal")]
    // #2253 fix round 3 (K): the wider scan's tables keep their ordinary columns.
    [InlineData("sprk_communication", "sprk_subject")]
    [InlineData("sprk_communication", "sprk_privilegeclassification")]
    [InlineData("sprk_communicationthread", "sprk_name")]
    [InlineData("sprk_analysis", "sprk_name")]
    [InlineData("sprk_analysis", "sprk_playbook")]
    [InlineData("sprk_analysis", "sprk_assignedattorney1")]
    [InlineData("sprk_communicationattachment", "sprk_name")]
    [InlineData("sprk_event", "sprk_isinternalonly")]
    [InlineData("sprk_event", "sprk_regardingmatter")]
    [InlineData("sprk_document", "sprk_privacystate")]
    // #2253 fix round 4 (F-1): a proposal's other columns, and its message's column on other tables.
    [InlineData("sprk_emailreviewlog", "sprk_action")]
    [InlineData("sprk_emailreviewlog", "sprk_aisuggestion")]
    [InlineData("sprk_emailreviewlog", "sprk_targetentity")]
    [InlineData("sprk_emailreviewlog", "sprk_targetrecordid")]
    [InlineData("sprk_emailreviewlog", "sprk_communicationname")]
    [InlineData("sprk_event", "sprk_communication")]
    [InlineData("sprk_emailupdatefield", "sprk_communication")]
    // #2253 fix round 5 (F4): a business unit's other columns, and the parent's name on other tables.
    [InlineData("businessunit", "parentbusinessunitidname")]
    [InlineData("businessunit", "costcenter")]
    [InlineData("businessunit", "divisionname")]
    [InlineData("team", "parentbusinessunitid")]
    // #2253 fix round 5 (K1): the communication's category lookup is a column, not the taxonomy table.
    [InlineData("sprk_communication", "sprk_triagecategory")]
    [InlineData("sprk_communication", "sprk_TriageCategory@odata.bind")]
    // ebilling-r1 (master merge): a submission's other columns, and its firm and core lookups on the other family tables.
    [InlineData("sprk_invoicesubmission", "sprk_processingstatus")]
    [InlineData("sprk_invoicesubmission", "sprk_submitter")]
    [InlineData("sprk_invoicesubmission", "sprk_adminattention")]
    // UAC review F4 (option b): D-176 takes the vendor org from the invoice's submission, so the invoice's own decides no access.
    [InlineData("sprk_invoice", "sprk_vendororg")]
    [InlineData("sprk_invoiceapproval", "sprk_regardingmatter")]
    [InlineData("sprk_externalidentifier", "sprk_regardingproject")]
    public void AnOrdinaryColumn_OrAnAccessColumnOnAnotherTable_IsNotRefused(string table, string key) =>
        Code(table, key).Should().BeNull($"'{key}' on {table} decides no access");

    // ── #2253 fix round 1 (K2): the push asks as its caller ──────────────────────────────────────────────────────

    [Fact]
    public void AsTheCaller_TheAccessTablesTheSecureRootColumnsAndTheAccessColumns_AreNotAsked()
    {
        foreach (var table in Tables.Concat(PlatformTables))
            CallerCode(table, "sprk_name").Should().BeNull($"{table}: the caller's own privileges decide");
        foreach (var root in Roots)
        {
            foreach (var column in RootColumns)
                CallerCode(root, column).Should().BeNull($"{column} on {root}: the caller's own privileges decide");
        }

        foreach (var (table, columns) in TableAccessColumns)
        {
            foreach (var column in columns)
                CallerCode(table, column).Should().BeNull($"{column} on {table}: the caller's own privileges decide");
        }
    }

    [Fact]
    public void AsTheCaller_TheRefusalsThePushAskedBefore2251_StillHold()
    {
        CallerCode("sprk_document", "sprk_accesspermission").Should().Be(InheritedAccessPermission.AppOnlyDocumentWriteReasonCode);
        CallerCode("sprk_document", "sprk_parentdocument").Should().Be(InheritedAccessPermission.AppOnlyDocumentLinkWriteReasonCode);
        CallerCode("sprk_document", "sprk_graphitemidbound").Should().Be(InheritedAccessPermission.AppOnlyDocumentBffColumnWriteReasonCode);
        CallerCode("sprk_matter", "ownerid").Should().Be(InheritedAccessPermission.AppOnlyOwnerWriteReasonCode);
        CallerCode("sprk_externalrecordaccess", "ownerid").Should().Be(InheritedAccessPermission.AppOnlyOwnerWriteReasonCode);
        CallerCode("contact", "sprk_createdbyperson").Should().Be(InheritedAccessPermission.AppOnlyCreatorWriteReasonCode);
    }

    // ── Update Record node / action seam (app-only) ──────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(WriterCases))]
    public async Task APlaybookUpdate_OfAnAccessTableOrASecureRootColumn_IsRefused_AndNothingIsWritten(
        string table, string key, string code)
    {
        var act = () => Core().UpdateAsync(
            new UpdateRecordActionInput(
                table, Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_description"] = "x", [key] = Forged.ToString("D") },
                Lookups: null),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(code);
        _writes.Invocations.Should().BeEmpty("the refusal comes before anything is written");
    }

    [Theory]
    [InlineData("sprk_externalrecordaccess", "sprk_Contact", "contact", InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode)]
    [InlineData("sprk_contactorganization", "sprk_Organization", "sprk_organization", InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode)]
    [InlineData("sprk_noaccessentry", "sprk_SubjectContact", "contact", InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode)]
    // the refusal reads the bind key; the target only needs a pinned set
    [InlineData("sprk_matter", "sprk_SecurityBU", "team", InheritedAccessPermission.AppOnlySecureRootWriteReasonCode)]
    [InlineData("systemuser", "sprk_PrimaryContact", "contact", InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode)]
    public async Task APlaybookLookup_IntoAnAccessTableOrOntoASecureRootColumn_IsRefused_AndNothingIsWritten(
        string table, string field, string target, string code)
    {
        var act = () => Core().UpdateAsync(
            new UpdateRecordActionInput(
                table, Row, FieldMappings: null, LegacyFields: null,
                Lookups: [new RenderedLookup(field, target, Forged.ToString("D"))]),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(code);
        _writes.Invocations.Should().BeEmpty("the refusal comes before anything is written");
    }

    [Theory]
    [InlineData("sprk_matter", "sprk_performancesummary")]
    [InlineData("sprk_matter", "sprk_description")]
    [InlineData("sprk_project", "sprk_description")]
    [InlineData("sprk_workassignment", "sprk_description")]
    public async Task APlaybookUpdate_OfAnOrdinaryColumnOnARoot_IsWritten(string table, string column)
    {
        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                table, Row, FieldMappings: null, LegacyFields: new Dictionary<string, string?> { [column] = "a summary" },
                Lookups: null),
            CancellationToken.None);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            table, Row, It.Is<Dictionary<string, object?>>(f => f.Count == 1 && f.ContainsKey(column)),
            It.IsAny<CancellationToken>(), null), Times.Once);
    }

    [Fact]
    public async Task AnImpersonatedUpdate_OfAnAccessTable_IsNotAskedTheAppOnlyRefusal()
    {
        // A user's own write runs as that user, so Dataverse's privileges decide it (the refusal is for app-only writers).
        var user = Guid.Parse("22510000-0000-0000-0000-00000000e4ee");
        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_externalrecordaccess", Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_name"] = "x" },
                Lookups: null, ImpersonateSystemUserId: user),
            CancellationToken.None);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            "sprk_externalrecordaccess", Row, It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), user),
            Times.Once);
    }

    // ── Output mapping (DataverseUpdateHandler, app-only) ────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(WriterCases))]
    public async Task AnOutputMapping_OfAnAccessTableOrASecureRootColumn_IsRefused_AndNothingIsWritten(
        string table, string key, string code)
    {
        var act = () => Handler().UpdateAsync(
            table, Row, new Dictionary<string, object?> { ["sprk_description"] = "x", [key] = Forged.ToString("D") },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(code);
        _writes.Invocations.Should().BeEmpty("the refusal comes before anything is written");
    }

    [Fact]
    public async Task AnOutputMapping_OfAnInvoicesOwner_IsRefusedBeforeTheInvoiceFamilyGuardReadsAnything()
    {
        // UAC review K2 (on e371dce6bf): the app-only refusal is asked before the invoice-family guard, which reads the stored
        // row app-only for EVERY write of an invoice or line (#2081). The owner is refused on every table (#2247).
        var reads = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var world = new StampWorld();
        var handler = new DataverseUpdateHandler(
            _writes.Object, reads.Object, world.Restamper, new RecordOwnershipResolverDouble(),
            SecureRootFilingGateFixtures.NothingSecure(), NullLogger<DataverseUpdateHandler>.Instance,
            SecureChildReconcilerFixtures.OverEmptyWorld());

        var act = () => handler.UpdateAsync(
            "sprk_invoice", Row,
            new Dictionary<string, object?> { ["ownerid"] = new Microsoft.Xrm.Sdk.EntityReference("team", Forged) },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode
            .Should().Be(InheritedAccessPermission.AppOnlyOwnerWriteReasonCode);
        reads.Invocations.Should().BeEmpty("the refusal comes before the invoice-family guard reads the stored row");
        _writes.Invocations.Should().BeEmpty("nothing is written");
    }

    [Theory]
    [InlineData("sprk_matter", "sprk_performancesummary")]
    [InlineData("sprk_project", "sprk_description")]
    [InlineData("sprk_workassignment", "sprk_description")]
    public async Task AnOutputMapping_OfAnOrdinaryColumnOnARoot_IsWritten(string table, string column)
    {
        await Handler().UpdateAsync(
            table, Row, new Dictionary<string, object?> { [column] = "a summary" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            table, Row, It.Is<Dictionary<string, object?>>(f => f.Count == 1 && f.ContainsKey(column)),
            It.IsAny<CancellationToken>(), null), Times.Once);
    }

    // ── Field-mapping push (a rule naming one is skipped; the rest of the push applies) ──────────────────────────

    /// <summary>
    /// #2253 fix round 1 (K2): the push runs as its caller, so a rule onto a matter's Access Permission (editable by a Write
    /// holder; <c>scripts/Set-SecureFlagFieldSecurity.ps1</c>), a root's container or business unit, or an access table is
    /// not refused here: it is sent as the caller, whose own Dataverse privileges and field security decide it.
    /// </summary>
    [Theory]
    [InlineData("sprk_matter", "sprk_accesspermission")]
    [InlineData("sprk_matter", "sprk_securitybu")]
    [InlineData("sprk_project", "sprk_containerid")]
    [InlineData("sprk_externalrecordaccess", "sprk_accesslevel")]
    [InlineData("contact", "sprk_standinggrant")]
    public async Task APushRule_OntoAColumnTheCallersPrivilegesDecide_IsSentAsTheCaller(string table, string target)
    {
        var (updated, failed, skipped, errors, results) = await Push(
            table, Rule("sprk_name", "sprk_description"), Rule("sprk_name", target));

        (updated, failed, skipped).Should().Be((1, 0, 0));
        errors.Should().BeEmpty();
        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            table, Row, It.Is<Dictionary<string, object?>>(f => f.Count == 2 && f.ContainsKey("sprk_description") && f.ContainsKey(target)),
            It.IsAny<CancellationToken>(), Caller), Times.Once);
        results.Should().NotContain(r => r.Status == FieldMappingStatus.Skipped);
    }

    [Fact]
    public async Task APushIntoACraftedTableName_SkipsEveryRule_AndWritesNothing()
    {
        var (updated, failed, skipped, errors, results) = await Push(
            "sprk_externalrecordaccess')?$select=EntitySetName#", Rule("sprk_name", "sprk_description"),
            Rule("sprk_name", "sprk_accesslevel"));

        (updated, failed, skipped).Should().Be((0, 0, 1));
        errors.Should().BeEmpty();
        _writes.Invocations.Should().BeEmpty();
        results.Should().HaveCount(2).And.OnlyContain(r => r.Status == FieldMappingStatus.Skipped);
    }

    private static FieldMappingRuleDto Rule(string source, string target) =>
        new() { SourceField = source, TargetField = target, SourceFieldType = "Text", TargetFieldType = "Text" };

    private Task<(int Updated, int Failed, int Skipped, PushFieldMappingsError[] Errors, FieldMappingResultDto[] FieldResults)> Push(
        string table, params FieldMappingRuleDto[] rules) =>
        FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            _writes.Object,
            new StampWorld().Restamper,
            rules,
            new Dictionary<string, object?> { ["sprk_name"] = "Matter 2251" },
            table,
            new[] { Row },
            Caller,
            NullLogger.Instance,
            CancellationToken.None);

    private UpdateRecordActionCore Core() => new(
        _writes.Object,
        new ServiceCollection().AddSingleton(EntitySetNameFixtures.Service()).BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>(),
        NullLogger.Instance);

    private DataverseUpdateHandler Handler()
    {
        var world = new StampWorld();
        return new DataverseUpdateHandler(
            _writes.Object, world.Service, world.Restamper, new RecordOwnershipResolverDouble(),
            SecureRootFilingGateFixtures.NothingSecure(), NullLogger<DataverseUpdateHandler>.Instance,
            SecureChildReconcilerFixtures.OverEmptyWorld());
    }
}
