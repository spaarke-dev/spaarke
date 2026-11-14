using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// #2799 review 1 (F4), C3: the output orchestrator's Optimistic update (<see cref="DataverseUpdateHandler"/>) adds the row's
/// <c>versionnumber</c> to its fields, and the locked-column split put it with the person's columns. Must not fire: a write whose
/// person part is that version alone sends NO person write — one application write of the locked columns, conditional on the
/// version; a write with other columns sends the person's write carrying the version, then the application's. Must fire: a
/// row changed since the version was read is the path's own concurrency conflict (retried, then refused), with nothing written.
/// </summary>
public partial class InvoiceFamilyGenericUpdatePathTests
{
    private const long ReadVersion = 42L;

    /// <summary>
    /// <see cref="Handler"/> whose person query also answers the Optimistic update's version read AS the person (the table's
    /// entity set and key, then its <c>versionnumber</c>).
    /// </summary>
    private DataverseUpdateHandler OptimisticHandler(IGenericEntityService app)
    {
        AnswerDeriveReads(app);
        if (app is IMocked<IGenericEntityService> mocked)
        {
            mocked.Mock.Setup(a => a.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string t, CancellationToken _) => EntitySetNameFixtures.SetOf(t));
        }

        static Dictionary<string, JsonElement> Row(string json) => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
        var query = CallerQuery()!;
        var set = EntitySetNameFixtures.SetOf(LockedTable);
        Mock.Get(query).Setup(q => q.QueryAsync("EntityDefinitions", It.Is<string>(o => o.Contains($"LogicalName eq '{LockedTable}'")), Caller,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([Row($$"""{"EntitySetName":"{{set}}","PrimaryIdAttribute":"{{LockedTable}}id"}""")]);
        Mock.Get(query).Setup(q => q.QueryAsync(set, $"$select=versionnumber&$filter={LockedTable}id eq {FamilyRow:D}", Caller,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([Row($$"""{"versionnumber":{{ReadVersion}}}""")]);

        return new(_writes.Object, app, new StampWorld().Restamper, new RecordOwnershipResolverDouble(),
            SecureRootFilingGateFixtures.NothingSecure(), NullLogger<DataverseUpdateHandler>.Instance,
            SecureChildReconcilerFixtures.OverEmptyWorld(), readAsPerson: query, lockedWriteProbe: LockedProbe());
    }

    /// <summary>The row's own core lookup re-named (allowed by the guard), plus <paramref name="plain"/>, Optimistic, as <see cref="Caller"/>.</summary>
    private Task RunOptimistic(IReadOnlyDictionary<string, object?> plain, int maxRetries = 1)
    {
        var fields = new Dictionary<string, object?>(plain) { ["sprk_regardingmatter"] = MatterRef(Matter) };
        return OptimisticHandler(FamilyRowUnderAMatter(LockedTable).Object).UpdateAsync(
            LockedTable, FamilyRow, fields, ConcurrencyMode.Optimistic, maxRetries, CancellationToken.None, Caller);
    }

    /// <summary>Each conditional application write: its fields and the version it was conditioned on.</summary>
    private IEnumerable<(Dictionary<string, object?> Fields, long Version)> ConditionalWrites() =>
        _writes.Invocations
            .Where(i => i.Method.Name == nameof(IFieldMappingDataverseService.UpdateRecordFieldsIfUnchangedAsync))
            .Select(i => ((Dictionary<string, object?>)i.Arguments[2], (long)i.Arguments[3]));

    [Fact]
    public async Task Locked_Optimistic_PersonsPartIsTheVersionAlone_OneApplicationWriteOnThatVersion_NoPersonWrite()
    {
        await RunOptimistic(NoPlain);

        Sent().Should().BeEmpty("the row version alone is no column of the person's: no person write is sent");
        var conditional = ConditionalWrites().Should().ContainSingle().Subject;
        conditional.Version.Should().Be(ReadVersion);
        conditional.Fields.Keys.Should().BeEquivalentTo(new[] { "sprk_regardingmatter" }, "the application's write carries only the locked column");
    }

    [Fact]
    public async Task Locked_Optimistic_WithOtherColumns_ThePersonsWriteCarriesTheVersion_ThenTheApplications()
    {
        await RunOptimistic(RenameOnly);

        ConditionalWrites().Should().BeEmpty();
        var sent = Sent().ToList();
        sent.Should().HaveCount(2);
        sent[0].As.Should().Be(Caller);
        sent[1].As.Should().BeNull();
        var personFields = (Dictionary<string, object?>)_writes.Invocations
            .First(i => i.Method.Name == nameof(IFieldMappingDataverseService.UpdateExistingRecordFieldsAsync)).Arguments[2];
        personFields.Should().Contain("versionnumber", ReadVersion).And.ContainKey("sprk_name");
        LockedColumnsWrittenOnlyByTheApp(LockedTable, FamilyRow);
    }

    [Fact]
    public async Task Locked_Optimistic_VersionAlone_RowChangedSince_IsThePathsConcurrencyConflict_NothingWritten()
    {
        _writes.Setup(w => w.UpdateRecordFieldsIfUnchangedAsync(LockedTable, FamilyRow, It.IsAny<Dictionary<string, object?>>(),
                ReadVersion, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Data.DBConcurrencyException("sprk_invoiceapproval record changed since it was read; the update was not applied."));

        var act = () => RunOptimistic(NoPlain, maxRetries: 2);

        var refused = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        refused.Message.Should().StartWith("Optimistic concurrency update failed after 2 attempts");
        refused.InnerException.Should().BeOfType<System.Data.DBConcurrencyException>();
        ConditionalWrites().Should().HaveCount(2, "a conflict is retried as the path's other conflicts are");
        Sent().Should().BeEmpty();
        refused.Message.Should().NotContain(InvoiceFamilyLockedWrite.PartlySavedMessage);
    }
}
