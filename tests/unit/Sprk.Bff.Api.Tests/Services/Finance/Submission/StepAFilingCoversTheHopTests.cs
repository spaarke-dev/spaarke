using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 094, uac-r2 re-check K1: step A's "filed" (<see cref="InvoiceSubmissionService.IsCommunicationFiledAsync"/>) and the
/// D-140 communication hop of the filing chain (<see cref="InvoiceSubmissionService.ResolveFilingAsync"/>) are two readers of one fact.
/// They stay two on purpose (the chain reads the thread's core columns and counts a gone thread as filing nothing; step A reads the
/// thread with every column and counts a thread it cannot find as filed), so this pins the one property that keeps them safe together:
/// whenever the hop reports the communication filed anywhere, step A reads it as filed too, so step A never files a communication the
/// chain already places. Every combination of the communication's own core columns, another filing column, and its thread (none, gone,
/// record, Direct, type unset; with or without a core column) is checked over ONE row set served to both readers.
/// </summary>
public sealed class StepAFilingCoversTheHopTests
{
    private static readonly Guid DocumentId = Guid.Parse("d0c00000-0000-4000-8000-000000000094");
    private static readonly Guid CommunicationId = Guid.Parse("c0c00000-0000-4000-8000-000000000094");
    private static readonly Guid ThreadId = Guid.Parse("7e7e0000-0000-4000-8000-000000000094");
    private static readonly Guid Matter = Guid.Parse("a0000000-0000-4000-8000-000000000094");

    private static readonly string?[] OwnCoreColumns = [null, "sprk_regardingmatter", "sprk_regardingproject", "sprk_regardingworkassignment"];

    private static readonly string[] CoreTables = ["sprk_matter", "sprk_project", "sprk_workassignment"];

    public enum ThreadShape { None, Gone, Record, Direct, TypeUnset }

    public static TheoryData<string?, bool, ThreadShape, string?> Shapes()
    {
        var data = new TheoryData<string?, bool, ThreadShape, string?>();
        foreach (var own in OwnCoreColumns)
        {
            foreach (var otherFiling in new[] { false, true })
            {
                foreach (var thread in Enum.GetValues<ThreadShape>())
                {
                    foreach (var threadCore in OwnCoreColumns)
                    {
                        if (thread is ThreadShape.None or ThreadShape.Gone && threadCore is not null)
                        {
                            continue;
                        }

                        data.Add(own, otherFiling, thread, threadCore);
                    }
                }
            }
        }

        return data;
    }

    [Theory(DisplayName = "094 K1 (must not fire): whenever the D-140 hop reports the communication filed, step A reads it as filed (never re-files it)")]
    [MemberData(nameof(Shapes))]
    public async Task WhenTheHopReportsFiled_StepAReadsFiled(string? ownCore, bool otherFiling, ThreadShape thread, string? threadCore)
    {
        var entities = World(ownCore, otherFiling, thread, threadCore);
        var document = new Entity("sprk_document", DocumentId) { ["sprk_relatedcommunication"] = new EntityReference("sprk_communication", CommunicationId) };

        var hop = await InvoiceSubmissionService.ResolveFilingAsync(entities, DocumentId, document, CancellationToken.None);
        var stepAFiled = await InvoiceSubmissionService.IsCommunicationFiledAsync(entities, CommunicationId, CancellationToken.None);

        if (hop is null || hop.Count > 0)
        {
            stepAFiled.Should().BeTrue($"the hop reports {(hop is null ? "an unplaceable chain" : string.Join(",", hop.Select(h => h.Column)))}");
        }
    }

    [Fact(DisplayName = "094 K1 (control): the shape set is not vacuous - the hop reports filed through the communication's own column AND through the thread alone, and reports unfiled for a Direct thread")]
    public async Task TheShapeSet_CoversBothHopsAndAnUnfiledCase()
    {
        var document = new Entity("sprk_document", DocumentId) { ["sprk_relatedcommunication"] = new EntityReference("sprk_communication", CommunicationId) };

        (await InvoiceSubmissionService.ResolveFilingAsync(World("sprk_regardingmatter", false, ThreadShape.None, null), DocumentId, document, CancellationToken.None))
            .Should().ContainSingle(h => h.Id == Matter);
        (await InvoiceSubmissionService.ResolveFilingAsync(World(null, false, ThreadShape.Record, "sprk_regardingproject"), DocumentId, document, CancellationToken.None))
            .Should().ContainSingle(h => h.Id == Matter && h.Column.StartsWith("sprk_communicationthread.", StringComparison.Ordinal));
        (await InvoiceSubmissionService.ResolveFilingAsync(World(null, false, ThreadShape.Direct, "sprk_regardingmatter"), DocumentId, document, CancellationToken.None))
            .Should().BeEmpty();
        (await InvoiceSubmissionService.IsCommunicationFiledAsync(World(null, false, ThreadShape.Direct, "sprk_regardingmatter"), CommunicationId, CancellationToken.None))
            .Should().BeFalse("a Direct thread files nothing, so step A may file it (the case it exists for)");
    }

    /// <summary>One row set served to both readers: RetrieveAsync (the chain's reads) and the thread query (step A's committed read).</summary>
    private static IGenericEntityService World(string? ownCore, bool otherFiling, ThreadShape thread, string? threadCore)
    {
        var communication = new Entity("sprk_communication", CommunicationId);
        if (ownCore is not null)
        {
            communication[ownCore] = new EntityReference(TableOf(ownCore), Matter);
        }

        if (otherFiling)
        {
            communication["sprk_regardingorganization"] = new EntityReference("sprk_organization", Guid.NewGuid());
        }

        Entity? threadRow = null;
        if (thread != ThreadShape.None)
        {
            communication["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", ThreadId);
        }

        if (thread is ThreadShape.Record or ThreadShape.Direct or ThreadShape.TypeUnset)
        {
            threadRow = new Entity("sprk_communicationthread", ThreadId);
            if (thread == ThreadShape.Record)
            {
                threadRow[RecordThreadFiling.ThreadTypeColumn] = new OptionSetValue(100000000);
            }
            else if (thread == ThreadShape.Direct)
            {
                threadRow[RecordThreadFiling.ThreadTypeColumn] = new OptionSetValue(RecordThreadFiling.DirectThreadType);
            }

            if (threadCore is not null)
            {
                threadRow[threadCore] = new EntityReference(TableOf(threadCore), Matter);
            }
        }

        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities.Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] columns, CancellationToken _) => table switch
            {
                "sprk_communication" when id == CommunicationId => Only(communication, columns),
                "sprk_communicationthread" when id == ThreadId && threadRow is not null => Only(threadRow, columns),
                _ => throw new System.ServiceModel.FaultException<OrganizationServiceFault>(
                    new OrganizationServiceFault { ErrorCode = -2147220969 }, "Does Not Exist"),
            });
        entities.Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationthread"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => threadRow is null ? new EntityCollection() : new EntityCollection([threadRow]));
        return entities.Object;
    }

    private static string TableOf(string column) => CoreTables.Single(t => column.EndsWith(t["sprk_".Length..], StringComparison.Ordinal));

    private static Entity Only(Entity row, string[] columns)
    {
        var copy = new Entity(row.LogicalName, row.Id);
        foreach (var column in columns.Where(row.Attributes.ContainsKey))
        {
            copy[column] = row[column];
        }

        return copy;
    }
}
