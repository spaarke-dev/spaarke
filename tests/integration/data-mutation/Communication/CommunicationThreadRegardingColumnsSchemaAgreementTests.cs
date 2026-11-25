using System.Xml.Linq;
using FluentAssertions;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.CommunicationThreadRegardingColumns;

/// <summary>
/// DI-151 / #2775: the thread regarding column list is paired with the real schema. A thread read that selects a column
/// <c>sprk_communicationthread</c> lacks faults the whole read (the arrival producer's fault was swallowed and every thread
/// was then treated as private), so the one shared list, <see cref="RegardingFieldMap.ThreadRegardingColumns"/>, must equal
/// the typed regarding lookups the SpaarkeMaster thread entity source declares. A new regarding field added to the
/// communication but not the thread fails the first test; a field the thread later gains (reportcard) fails the second.
/// Read as text from the repo (no network).
/// </summary>
public class CommunicationThreadRegardingColumnsSchemaAgreementTests
{
    [Fact(DisplayName = "DI-151: every column the thread read selects exists on sprk_communicationthread in the solution source")]
    public void EveryThreadRegardingColumn_ExistsOnTheThreadEntity()
    {
        var threadColumns = ColumnsOf("sprk_CommunicationThread");

        RegardingFieldMap.ThreadRegardingColumns.Should().NotBeEmpty();
        threadColumns.Should().Contain(RegardingFieldMap.ThreadRegardingColumns,
            "a thread read that selects a column the thread lacks faults, and the arrival producer then treats the thread as private");
    }

    [Fact(DisplayName = "DI-151: the thread list is exactly the regarding family the thread entity declares (nothing it has is left out)")]
    public void TheThreadList_IsTheFamilyTheThreadEntityDeclares()
    {
        var threadColumns = ColumnsOf("sprk_CommunicationThread");
        var communicationColumns = ColumnsOf("sprk_Communication");

        RegardingFieldMap.AllRegardingFields.Should().BeSubsetOf(communicationColumns,
            "the family is the communication's: every member exists there");
        RegardingFieldMap.ThreadRegardingColumns.Should().BeEquivalentTo(
            RegardingFieldMap.AllRegardingFields.Where(threadColumns.Contains),
            "the thread list is the family members the thread entity declares; one it lacks is excluded, one it gains must be admitted");
    }

    [Fact(DisplayName = "DI-151: ThreadResolver and the shared list agree, and the thread tuple list matches the column list")]
    public void TheSharedDefinitions_Agree()
    {
        ThreadResolver.ThreadRegardingColumns.Should().Equal(RegardingFieldMap.ThreadRegardingColumns);
        RegardingFieldMap.ThreadRegardingFields.Select(x => x.RegardingField).Should().Equal(RegardingFieldMap.ThreadRegardingColumns);
    }

    // Must-fire control: the reader of the solution source sees a column that is on one entity and not the other, so the
    // two tests above are not vacuous (the report card is on the communication, not on the thread).
    [Fact(DisplayName = "DI-151: control - the source reader tells the communication's report card lookup from the thread's")]
    public void Control_TheReaderSeesTheReportCardOnTheCommunicationOnly()
    {
        ColumnsOf("sprk_Communication").Should().Contain("sprk_regardingreportcard");
        ColumnsOf("sprk_CommunicationThread").Should().NotContain("sprk_regardingreportcard");
    }

    private static HashSet<string> ColumnsOf(string entityDirectory)
    {
        var path = Path.Combine(RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster", "Entities", entityDirectory, "Entity.xml");
        File.Exists(path).Should().BeTrue("the solution source for " + entityDirectory + " is part of the repo");
        return XDocument.Load(path).Descendants("attribute")
            .Select(a => (string?)a.Element("LogicalName"))
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
