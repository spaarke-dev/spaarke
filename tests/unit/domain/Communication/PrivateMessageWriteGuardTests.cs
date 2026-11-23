using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// field-mapping-server-write-path-r1 task 056 (owner 2026-11-22, Q5: a private message is never filed). An update of an existing
/// <c>sprk_communication</c> that sets only a typed regarding that is no ownership parent, or only the regarding pair, never reaches
/// ReparentAsync; <see cref="PrivateMessageWriteGuard.RefuseUpdateAsync"/> asks the rule before such an update is written: it reads
/// only for a trigger column of the communication table, refuses a private row with
/// <see cref="RecordOwnerRefusal.PrivateMessageNotFileable"/> (409) and a read fault with
/// <see cref="PrivateMessageWriteGuard.CheckUnavailable"/> (503), and passes everything else.
/// </summary>
public class PrivateMessageWriteGuardTests
{
    private const string Communication = "sprk_communication";
    private static readonly Guid MessageId = Guid.Parse("c056c056-0000-0000-0000-000000000001");
    private static readonly Guid Matter = Guid.Parse("c056c056-0000-0000-0000-000000000002");

    /// <summary>Strict: any read the test does not set up fails the test.</summary>
    private readonly Mock<IGenericEntityService> _entities = new(MockBehavior.Strict);

    private Task<RecordOwnerResolution?> GuardAsync(string table, params string[] columns) =>
        PrivateMessageWriteGuard.RefuseUpdateAsync(
            _entities.Object, table, MessageId, columns, NullLogger.Instance, CancellationToken.None);

    private void Metadata(IEnumerable<string> referenceColumns) =>
        _entities.Setup(e => e.GetColumnMetadataAsync(Communication, It.IsAny<CancellationToken>()))
            .ReturnsAsync(referenceColumns.Concat(new[] { "sprk_communicationid", "sprk_subject" })
                .ToDictionary(c => c, c => new ColumnMetadata(c, "Lookup", true, true, null), StringComparer.OrdinalIgnoreCase));

    private void Row(params (string Column, object Value)[] values)
    {
        var row = new Entity(Communication, MessageId);
        foreach (var (column, value) in values)
            row[column] = value;
        _entities.Setup(e => e.RetrieveAsync(Communication, MessageId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
    }

    // ── the trigger list ──

    [Fact]
    public void TheTriggerList_IsTheFilingLinks_DerivedFromTheColumnMaps_AndTheThread()
    {
        var expected = SecureChildLineage.Children[Communication].LineageColumns
            .Concat(RegardingFieldMap.AllRegardingFields)
            .Concat(new[] { CoreAncestorResolver.RegardingRecordIdColumn, CoreAncestorResolver.RegardingRecordTypeColumn })
            .Distinct(StringComparer.OrdinalIgnoreCase);

        PrivateMessageWriteGuard.TriggerColumns.Should().BeEquivalentTo(expected);
        PrivateMessageWriteGuard.TriggerColumns.Should().Contain(new[]
        {
            "sprk_communicationthread", "sprk_regardingperson", "sprk_regardingaccount", "sprk_regardingorganization",
            "sprk_regardingmatter", "sprk_regardingproject", "sprk_regardingworkassignment",
            "sprk_regardingrecordid", "sprk_regardingrecordtype",
        });
        PrivateMessageWriteGuard.TriggerColumns.Should().NotContain(PrivateContextColumns.All);
        PrivateMessageWriteGuard.TriggerColumns.Should().NotContain(new[] { "sprk_subject", "sprk_body", "statuscode", "statecode" });
    }

    [Fact]
    public void TheUnfileList_IsTheTriggerListWithoutTheThread()
    {
        DirectMessageOwnerStep.PrivateFilingColumns.Should().Equal(
            PrivateMessageWriteGuard.TriggerColumns.Where(c => c != "sprk_communicationthread"));
    }

    // ── no read ──

    [Theory]
    [InlineData("sprk_subject")]
    [InlineData("sprk_body")]
    [InlineData("statuscode")]
    public async Task AChangeOutsideTheTriggerList_ReadsNothing_AndPasses(string column)
    {
        (await GuardAsync(Communication, column, "sprk_subject")).Should().BeNull();
        _entities.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("sprk_event")]
    [InlineData("sprk_document")]
    [InlineData("sprk_communicationthread")]
    public async Task AnotherTable_ReadsNothing_AndPasses(string table)
    {
        (await GuardAsync(table, "sprk_regardingperson@odata.bind", "sprk_regardingrecordid")).Should().BeNull();
        _entities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WithTheReferenceColumnsAbsentFromMetadata_NoRowIsRead_AndTheUpdatePasses()
    {
        Metadata(Array.Empty<string>());

        (await GuardAsync(Communication, "sprk_regardingperson@odata.bind")).Should().BeNull();
        _entities.Verify(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── a private message is refused ──

    public static TheoryData<string> TriggerKeys() => new()
    {
        "sprk_regardingperson@odata.bind",
        "sprk_RegardingAccount@odata.bind",
        "sprk_regardingorganization",
        "sprk_regardingrecordid",
        "sprk_regardingrecordtype@odata.bind",
        "_sprk_regardingmatter_value",
        "sprk_communicationthread@odata.bind",
    };

    [Theory]
    [MemberData(nameof(TriggerKeys))]
    public async Task APrivateMessage_IsRefusedAsNotFileable(string key)
    {
        Metadata(PrivateContextColumns.All);
        Row((PrivateContextColumns.Project, new EntityReference("sprk_project", Matter)));

        var refusal = await GuardAsync(Communication, key, "sprk_subject");

        refusal.Should().NotBeNull();
        refusal!.IsRefused.Should().BeTrue();
        refusal.RefusalCode.Should().Be(RecordOwnerRefusal.PrivateMessageNotFileable);
    }

    [Fact]
    public async Task ARegardingPairOnlyChange_OnAPrivateMessage_IsRefused()
    {
        Metadata(PrivateContextColumns.All);
        Row((PrivateContextColumns.Matter, new EntityReference("sprk_matter", Matter)));

        (await GuardAsync(Communication, CoreAncestorResolver.RegardingRecordIdColumn, CoreAncestorResolver.RegardingRecordTypeColumn + "@odata.bind"))!
            .RefusalCode.Should().Be(RecordOwnerRefusal.PrivateMessageNotFileable);
    }

    [Fact]
    public async Task OnlyTheReferenceColumnsThatExist_AreRead()
    {
        Metadata(new[] { PrivateContextColumns.WorkAssignment });
        string[]? selected = null;
        _entities.Setup(e => e.RetrieveAsync(Communication, MessageId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Callback((string _, Guid _, string[] columns, CancellationToken _) => selected = columns)
            .ReturnsAsync(new Entity(Communication, MessageId)
            {
                [PrivateContextColumns.WorkAssignment] = new EntityReference("sprk_workassignment", Matter),
            });

        (await GuardAsync(Communication, "sprk_regardingperson@odata.bind"))!.RefusalCode
            .Should().Be(RecordOwnerRefusal.PrivateMessageNotFileable);
        selected.Should().Equal(PrivateContextColumns.WorkAssignment);
    }

    // ── an ordinary message passes ──

    [Fact]
    public async Task AnOrdinaryDirectMessage_WithTheSameChange_Passes()
    {
        Metadata(PrivateContextColumns.All);
        Row();

        (await GuardAsync(Communication, "sprk_regardingperson@odata.bind")).Should().BeNull();
    }

    [Fact]
    public async Task AnEmptyReference_IsNoReference_AndPasses()
    {
        Metadata(PrivateContextColumns.All);
        Row((PrivateContextColumns.Matter, new EntityReference("sprk_matter", Guid.Empty)));

        (await GuardAsync(Communication, CoreAncestorResolver.RegardingRecordIdColumn)).Should().BeNull();
    }

    // ── a fault refuses ──

    [Fact]
    public async Task AMetadataFault_RefusesAsUnavailable_AndReadsNoRow()
    {
        _entities.Setup(e => e.GetColumnMetadataAsync(Communication, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test: metadata unavailable."));

        (await GuardAsync(Communication, "sprk_regardingperson@odata.bind"))!.RefusalCode
            .Should().Be(PrivateMessageWriteGuard.CheckUnavailable);
        _entities.Verify(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AMetadataReadWithNoAnswer_RefusesAsUnavailable()
    {
        _entities.Setup(e => e.GetColumnMetadataAsync(Communication, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, ColumnMetadata>)null!);

        (await GuardAsync(Communication, CoreAncestorResolver.RegardingRecordIdColumn))!.RefusalCode
            .Should().Be(PrivateMessageWriteGuard.CheckUnavailable);
    }

    [Fact]
    public async Task ARowReadFault_RefusesAsUnavailable()
    {
        Metadata(PrivateContextColumns.All);
        _entities.Setup(e => e.RetrieveAsync(Communication, MessageId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test: Dataverse unavailable."));

        (await GuardAsync(Communication, "sprk_regardingperson@odata.bind"))!.RefusalCode
            .Should().Be(PrivateMessageWriteGuard.CheckUnavailable);
    }

    [Fact]
    public async Task TheCallersCancellation_Propagates()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        _entities.Setup(e => e.GetColumnMetadataAsync(Communication, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancelled.Token));

        var act = () => PrivateMessageWriteGuard.RefuseUpdateAsync(
            _entities.Object, Communication, MessageId, new[] { "sprk_regardingperson@odata.bind" }, NullLogger.Instance, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── the HTTP shape ──

    [Theory]
    [InlineData(RecordOwnerRefusal.PrivateMessageNotFileable, StatusCodes.Status409Conflict)]
    [InlineData(PrivateMessageWriteGuard.CheckUnavailable, StatusCodes.Status503ServiceUnavailable)]
    public void TheRefusal_RendersAsTheOwnerRefusal_409ForAPrivateMessage_503ForAReadFault(string code, int status)
    {
        var result = ProblemDetailsHelper.RecordOwnerRefused(RecordOwnerResolution.Refused(code, "reason"), "communication", "trace");

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(status);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(code);
    }
}
