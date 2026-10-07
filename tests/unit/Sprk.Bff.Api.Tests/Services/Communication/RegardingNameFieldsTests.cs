using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// <see cref="RegardingNameFields"/> is the live-verified catalogue every caller-scoped communication read uses to
/// address a regarding record (task 127's delegated candidate reads; task 161's record gate, template merge and
/// record-thread name). The class remarks have promised this lock-step test since task 127; task 161 adds it.
/// </summary>
/// <remarks>
/// A wrong entity set or name field never fails loudly: a delegated read of the wrong collection is a 404, which is
/// indistinguishable from "the caller may not read this record", so a record the caller WAS entitled to see is
/// silently treated as hidden (or, for a name field, every read 400s). That is why each value is pinned to the value
/// read from live metadata (spaarkedev1, read-only, 2026-10-03 — task-161 note §2), not to a convention.
/// </remarks>
public class RegardingNameFieldsTests
{
    /// <summary>logical name → (live EntitySetName, live display-name attribute), read from EntityDefinitions / Attributes.</summary>
    public static readonly TheoryData<string, string, string> LiveVerified = new()
    {
        { "sprk_matter", "sprk_matters", "sprk_mattername" },
        { "sprk_project", "sprk_projects", "sprk_projectname" },
        { "sprk_invoice", "sprk_invoices", "sprk_name" },
        { "sprk_event", "sprk_events", "sprk_eventname" },
        { "sprk_workassignment", "sprk_workassignments", "sprk_name" },
        { "sprk_servicerequest", "sprk_servicerequests", "sprk_name" },
        { "sprk_budget", "sprk_budgets", "sprk_name" },
        { "sprk_reportcard", "sprk_reportcards", "sprk_name" },
        // Live: the set is sprk_analysises (NOT sprk_analyses, which this class said until task 161).
        { "sprk_analysis", "sprk_analysises", "sprk_name" },
        // Live: the display name is sprk_organizationname — sprk_name does NOT exist on sprk_organization.
        { "sprk_organization", "sprk_organizations", "sprk_organizationname" },
        { "contact", "contacts", "fullname" },
        { "account", "accounts", "name" },
    };

    [Theory]
    [MemberData(nameof(LiveVerified))]
    public void EachCoveredType_AddressesItsLiveEntitySetAndNameField(string logicalName, string entitySet, string nameField)
    {
        RegardingNameFields.EntitySetName(logicalName).Should().Be(entitySet);
        RegardingNameFields.PrimaryNameField(logicalName).Should().Be(nameField);
    }

    [Fact]
    public void BothFunctions_AcceptExactlyTheSameKeys()
    {
        // Read from the SOURCE, because a switch expression cannot be enumerated: a key added to one function only
        // must fail here even when no test names that key.
        var source = BffSource("Services", "Communication", "RegardingNameFields.cs");

        // Task 097 round 9: the display-name map has ONE home, RegardingRecordType.GetPrimaryNameField in the shared
        // library (PrimaryNameField delegates to it), so its keys are read from there.
        var nameKeys = SwitchKeys(SharedDataverseSource("Models.cs"), "GetPrimaryNameField");
        var setKeys = SwitchKeys(source, "EntitySetName");

        nameKeys.Should().NotBeEmpty();
        setKeys.Should().BeEquivalentTo(nameKeys.Where(k => k != NameOnlyType),
            "a type with a display name must also be addressable, and vice versa — RegardingNameFields' own contract, "
            + "with the ONE documented name-only exception");
        nameKeys.Should().Contain(NameOnlyType);
    }

    /// <summary>
    /// The one name-only entry (sweep integration of task 156 with task 161): <c>TaskActionCore</c> names a task filed under
    /// the email it follows up (owner round 8 item 2), so the name is listed; the task 161 routes use
    /// <see cref="RegardingNameFields.EntitySetName"/> as their regarding allow-list, so the type stays out of it and those
    /// routes keep refusing it. Live (spaarkedev1 EntityDefinitions, read-only, 2026-10-04): PrimaryNameAttribute
    /// <c>sprk_name</c>, EntitySetName <c>sprk_communications</c>.
    /// </summary>
    private const string NameOnlyType = "sprk_communication";

    [Fact]
    public void TheCommunication_IsANameOnlyEntry_NamedButNeverARouteRegardingTarget()
    {
        RegardingNameFields.PrimaryNameField(NameOnlyType).Should().Be("sprk_name");
        RegardingNameFields.EntitySetName(NameOnlyType).Should().BeNull(
            "the task 161 routes must keep answering 400/403 for a communication named as a regarding record");
    }

    [Fact]
    public void TheCatalogue_CoversEveryLiveVerifiedTypeAndNothingElse()
    {
        var source = BffSource("Services", "Communication", "RegardingNameFields.cs");
        var pinned = LiveVerified.Select(row => (string)row[0]).ToList();

        SwitchKeys(source, "EntitySetName").Should().BeEquivalentTo(pinned,
            "every type in the catalogue must have a live-verified pin above — add the row when adding the type");
    }

    [Theory]
    [InlineData("sprk_memo")]
    [InlineData("sprk_recordtype_ref")]
    [InlineData("Sprk_Matter")]
    public void ATypeOutsideTheCatalogue_IsNotGuessed(string logicalName)
    {
        // Case-sensitive by design: callers normalize with ToLowerInvariant() first.
        RegardingNameFields.EntitySetName(logicalName).Should().BeNull();
        RegardingNameFields.PrimaryNameField(logicalName).Should().BeNull();
    }

    private static IReadOnlyList<string> SwitchKeys(string source, string methodName)
    {
        var start = source.IndexOf($"public static string? {methodName}(", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{methodName} must exist");
        var end = source.IndexOf("_ => null", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"{methodName} must end with a '_ => null' default arm");

        return Regex.Matches(source[start..end], "\"([a-z_]+)\"\\s*=>")
            .Select(m => m.Groups[1].Value)
            .ToList();
    }

    private static string SharedDataverseSource(string file) =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(BffPath())!, "..", "..", "shared", "Spaarke.Dataverse", file));

    private static string BffPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var marker = Path.Combine(dir, ".git");
            if (Directory.Exists(marker) || File.Exists(marker))
                return Path.Combine(dir, "src", "server", "api", "Sprk.Bff.Api", "x");
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }

    [Fact]
    public void PrimaryNameField_IsTheSharedMap_NotASecondCopy()
    {
        // Task 097 round 9: one source. The BFF function delegates; it carries no switch of its own to drift.
        BffSource("Services", "Communication", "RegardingNameFields.cs")
            .Should().Contain("RegardingRecordType.GetPrimaryNameField(entityLogicalName)");
        foreach (var key in SwitchKeys(SharedDataverseSource("Models.cs"), "GetPrimaryNameField"))
            RegardingNameFields.PrimaryNameField(key).Should().Be(Spaarke.Dataverse.RegardingRecordType.GetPrimaryNameField(key));
    }

    private static string BffSource(params string[] relative)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            // In a worktree the repository marker is a FILE, not a directory.
            var marker = Path.Combine(dir, ".git");
            if (Directory.Exists(marker) || File.Exists(marker))
            {
                var path = Path.Combine(new[] { dir, "src", "server", "api", "Sprk.Bff.Api" }.Concat(relative).ToArray());
                File.Exists(path).Should().BeTrue($"{relative[^1]} must exist at {path}");
                return File.ReadAllText(path);
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
