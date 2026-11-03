using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ontology-r1 review F1 (049, 2026-10-19): Pending Spend (D-104) must never stop the counted rollup. Dataverse refuses a whole PATCH
/// that names an unknown column, and the pending query joins <c>sprk_invoicesubmission</c>; on an environment the BFF reaches before
/// the schema script, a single combined write would fail every recalculate and leave total spend, budget and the timeline stale.
/// </summary>
public class FinanceRollupPendingSpendWriteTests
{
    private static readonly Guid ParentId = Guid.NewGuid();

    private static readonly Dictionary<string, object?> Counted = new() { ["sprk_totalspendtodate"] = 100m, ["sprk_invoicecount"] = 2 };

    private readonly Mock<IFieldMappingDataverseService> _writer = new();
    private readonly Mock<ILogger> _logger = new();
    private readonly List<Dictionary<string, object?>> _written = new();

    public FinanceRollupPendingSpendWriteTests()
    {
        _writer
            .Setup(w => w.UpdateExistingRecordFieldsAsync("sprk_matter", ParentId, It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken>((_, _, fields, _) => _written.Add(fields))
            .Returns(Task.CompletedTask);
    }

    private Task<bool> Write(Func<CancellationToken, Task<decimal>> readPending, CancellationToken ct = default) =>
        FinanceRollupService.WriteRollupAsync(_writer.Object, _logger.Object, "sprk_matter", ParentId, Counted, readPending, ct);

    [Fact]
    public async Task BothWrites_Succeed_CountedFirst_ThenPendingAlone()
    {
        var wrote = await Write(_ => Task.FromResult(42.5m));

        wrote.Should().BeTrue();
        _written.Should().HaveCount(2);
        _written[0].Should().BeSameAs(Counted);
        _written[1].Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, object?>("sprk_pendingspend", 42.5m));
    }

    [Fact]
    public async Task BothWrites_Succeed_WithZeroPending_WritesZero()
    {
        // ontology-r1 re-check F3: 0 is a real value (the last pending invoice was approved), not "unchanged". Skipping the write at 0
        // would leave the matter showing the old pending amount forever.
        var wrote = await Write(_ => Task.FromResult(0m));

        wrote.Should().BeTrue();
        _written.Should().HaveCount(2);
        _written[1]["sprk_pendingspend"].Should().Be(0m);
    }

    [Fact]
    public async Task APendingTimeout_WithTheCallersTokenLive_IsContainedLikeAnyPendingFailure()
    {
        // An HttpClient timeout surfaces as TaskCanceledException although the caller did not cancel.
        var wrote = await Write(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));

        wrote.Should().BeFalse();
        _written.Should().ContainSingle().Which.Should().BeSameAs(Counted);
        VerifyErrorLogged();
    }

    [Fact]
    public async Task APendingQueryFailure_StillWritesTheCountedFields_AndWritesNoPendingValue()
    {
        // The pending query joins sprk_invoicesubmission; on an environment without it the query throws.
        var wrote = await Write(_ => throw new InvalidOperationException("'sprk_invoicesubmission' entity doesn't exist"));

        wrote.Should().BeFalse();
        _written.Should().ContainSingle().Which.Should().BeSameAs(Counted, "the counted rollup is written whatever happens to pending");
        _written.Should().NotContain(f => f.ContainsKey("sprk_pendingspend"), "pending keeps its old value; it is never written as 0");
        VerifyErrorLogged();
    }

    [Fact]
    public async Task APendingWriteFailure_LeavesTheCountedWriteDone_AndDoesNotThrow()
    {
        // Dataverse refuses a PATCH naming an unknown column (the schema script has not run): 400 → HttpRequestException.
        _writer
            .Setup(w => w.UpdateExistingRecordFieldsAsync("sprk_matter", ParentId,
                It.Is<Dictionary<string, object?>>(f => f.ContainsKey("sprk_pendingspend")), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Could not find a property named 'sprk_pendingspend'"));

        var wrote = await Write(_ => Task.FromResult(42.5m));

        wrote.Should().BeFalse();
        _written.Should().ContainSingle().Which.Should().BeSameAs(Counted);
        VerifyErrorLogged();
    }

    [Fact]
    public async Task ACountedWriteFailure_StillPropagates_AndPendingIsNotAttempted()
    {
        // Control: only the new Pending Spend write is contained; the established counted write keeps its contract (404 for a
        // missing parent, 500 otherwise).
        _writer
            .Setup(w => w.UpdateExistingRecordFieldsAsync("sprk_matter", ParentId, Counted, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());
        var pendingRead = false;

        var act = () => Write(_ => { pendingRead = true; return Task.FromResult(1m); });

        await act.Should().ThrowAsync<KeyNotFoundException>();
        pendingRead.Should().BeFalse();
    }

    [Fact]
    public async Task Cancellation_IsNotSwallowedAsAPendingFailure()
    {
        using var cts = new CancellationTokenSource();
        var act = () => Write(_ => { cts.Cancel(); throw new OperationCanceledException(cts.Token); }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private void VerifyErrorLogged() =>
        _logger.Verify(l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
}
