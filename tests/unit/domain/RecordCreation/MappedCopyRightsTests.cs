using FluentAssertions;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 064 — the one predicate that lets step 5 write an ordinary mapped value without the
/// caller's AppendTo or field-security check (<see cref="RecordCreationService.MayCopyWithoutCallerChecks"/>), and the closed
/// list of BFF-owned invariant columns it never applies to (<see cref="RecordCreationService.IsCopyInvariantColumn"/>). The
/// end-to-end behaviour is the seam suite's (<c>RecordCreationServiceMappingSeamTests.Task064.cs</c>).
/// </summary>
public class MappedCopyRightsTests
{
    private const string Event = "sprk_event";

    // ── The predicate ──

    [Fact]
    public void AnOrdinaryValueFromTheFilingParent_IsCopiedWithoutTheCallersChecks() =>
        RecordCreationService.MayCopyWithoutCallerChecks(Event, "sprk_eventtype_ref", fromFilingParent: true, isDefault: false, isFilingLookup: false)
            .Should().BeTrue();

    [Fact]
    public void AValueFromASourceThatIsNotTheFilingParent_KeepsTheCallersChecks() =>
        RecordCreationService.MayCopyWithoutCallerChecks(Event, "sprk_eventtype_ref", fromFilingParent: false, isDefault: false, isFilingLookup: false)
            .Should().BeFalse();

    [Fact]
    public void AnAdaptersDefault_KeepsTheCallersChecks() =>
        RecordCreationService.MayCopyWithoutCallerChecks(Event, "sprk_eventtype_ref", fromFilingParent: true, isDefault: true, isFilingLookup: false)
            .Should().BeFalse();

    [Fact]
    public void AFilingLookup_KeepsTheCallersChecks() =>
        RecordCreationService.MayCopyWithoutCallerChecks(Event, "sprk_eventtype_ref", fromFilingParent: true, isDefault: false, isFilingLookup: true)
            .Should().BeFalse();

    // ── Boundary 2: one must-fire per invariant family, through the predicate ──

    [Theory]
    [InlineData(Event, "ownerid")]                       // the owner
    [InlineData(Event, "owningteam")]
    [InlineData("sprk_matter", "sprk_issecure")]         // the secure flag
    [InlineData(Event, "sprk_issecure")]
    [InlineData(Event, "sprk_accesspermission")]         // Access Permission
    [InlineData(Event, "sprk_accessinheritance")]        // the access record
    [InlineData(Event, "sprk_securitybu")]
    [InlineData(Event, "sprk_containerid")]
    [InlineData(Event, "sprk_assignedto")]               // every Assigned-To column, by prefix
    [InlineData(Event, "sprk_assignedtointernal")]
    [InlineData(Event, "sprk_assignedreviewer_future")]
    [InlineData(Event, "sprk_regardingmatter")]          // the regarding / core-ancestor stamps, by prefix
    [InlineData(Event, "sprk_regardingrecordid")]
    [InlineData(Event, "sprk_regardingrecordtype")]
    [InlineData(Event, "sprk_corerecordid")]
    [InlineData(Event, "sprk_corerecordtype")]
    [InlineData("sprk_document", "sprk_relatedcommunication")] // the related filing lookups, by prefix
    [InlineData(Event, "sprk_createdbyperson")]          // the creator
    [InlineData(Event, "sprk_createdbycontact")]
    [InlineData(Event, "sprk_requestedby")]
    [InlineData("sprk_document", "sprk_graphitemid")]    // the file binding
    [InlineData("sprk_document", "sprk_graphitemidbound")]
    [InlineData("systemuser", "sprk_isexternal")]        // the external flag
    [InlineData(Event, "sprk_isexternal")]
    [InlineData("sprk_noaccessentry", "sprk_name")]      // the No Access list: any column of its table
    [InlineData("contact", "sprk_externalobjectid")]     // contact identity and binding
    [InlineData("contact", "sprk_identityplane")]
    public void AnInvariantColumn_IsNeverCopiedWithoutTheCallersChecks_FromTheFilingParentEither(string table, string column)
    {
        RecordCreationService.IsCopyInvariantColumn(table, column).Should().BeTrue();
        RecordCreationService.MayCopyWithoutCallerChecks(table, column, fromFilingParent: true, isDefault: false, isFilingLookup: false)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(Event, "sprk_eventtype_ref")]
    [InlineData(Event, "sprk_description")]
    [InlineData(Event, "sprk_location")]
    [InlineData("sprk_project", "sprk_projecttype_ref")]
    public void AnOrdinaryColumn_IsNotAnInvariant(string table, string column) =>
        RecordCreationService.IsCopyInvariantColumn(table, column).Should().BeFalse();

    /// <summary>The closed list: fails if an entry is lost (an addition is allowed).</summary>
    [Fact]
    public void TheClosedList_KeepsEveryNamedEntry()
    {
        RecordCreationService.CopyInvariantColumns.Should().Contain(
        [
            "ownerid", "owninguser", "owningteam", "owningbusinessunit",
            "sprk_issecure", "sprk_accesspermission", "sprk_accessinheritance", "sprk_securitybu", "sprk_containerid",
            "sprk_createdbyperson", "sprk_createdbycontact", "sprk_requestedby",
            "sprk_regardingrecordid", "sprk_regardingrecordtype", "sprk_regardingrecordname", "sprk_regardingrecordurl",
            "sprk_regardingrecordnumber",
            "sprk_corerecordtype", "sprk_corerecordid",
            "sprk_graphitemid", "sprk_graphitemidbound", "sprk_isexternal",
        ]);
        RecordCreationService.CopyInvariantPrefixes.Should().Contain(["sprk_assigned", "sprk_regarding", "sprk_related"]);
    }
}
