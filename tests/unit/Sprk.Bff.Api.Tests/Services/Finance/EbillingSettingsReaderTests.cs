using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// <see cref="EbillingSettingsReader"/> (ebilling-r1 task 025, FR-48, D-59): the typed view of the one
/// <c>sprk_ebillingsetting</c> row, with a short cache and typed defaults when no row exists.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the rejection threshold, the confidence weights and the approver defaults decide what the AI may reject
/// and who approves an invoice, so the contract "a changed value is picked up after the TTL, an outage is never papered over with
/// defaults" is pinned. Time is a <see cref="FakeTimeProvider"/> (no real waiting).
/// </remarks>
public sealed class EbillingSettingsReaderTests
{
    private readonly Mock<IGenericEntityService> _entityService = new();
    private readonly Mock<ILogger<EbillingSettingsReader>> _logger = new();
    private readonly FakeTimeProvider _time = new();

    private EbillingSettingsReader CreateReader()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_entityService.Object);
        var cache = new MemoryCache(new MemoryCacheOptions { Clock = new FakeClock(_time) });
        return new EbillingSettingsReader(services.BuildServiceProvider(), cache, _logger.Object);
    }

    private void ReturnRows(params Entity[] rows)
    {
        var collection = new EntityCollection();
        foreach (var row in rows)
        {
            collection.Entities.Add(row);
        }

        _entityService
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
    }

    private static Entity SettingsRow(decimal threshold = 0.90m)
    {
        var row = new Entity("sprk_ebillingsetting", Guid.NewGuid());
        row["sprk_reliabilitythreshold"] = threshold;
        return row;
    }

    private static OptionSetValueCollection Roles(params BillingApproverRole[] roles) =>
        new(roles.Select(r => new OptionSetValue((int)r)).ToList());

    [Fact]
    public async Task GetAsync_RowPresent_MapsEveryColumn()
    {
        var codeSet = Guid.NewGuid();
        var row = SettingsRow(0.75m);
        row["sprk_matterapproverroles"] = Roles(BillingApproverRole.AssignedAttorney2, BillingApproverRole.AssignedTo);
        row["sprk_projectapproverroles"] = Roles(BillingApproverRole.AssignedParalegal1);
        row["sprk_workassignmentapproverroles"] = Roles(BillingApproverRole.AssignedToExternal, BillingApproverRole.AssignedAttorney1);
        row["sprk_weightanalyzer"] = 0.40m;
        row["sprk_weightarithmetic"] = 0.30m;
        row["sprk_weightreferencematch"] = 0.20m;
        row["sprk_weightselfreport"] = 0.10m;
        row["sprk_arithmeticfailcap"] = 0.45m;
        row["sprk_referencemissingcap"] = 0.70m;
        row["sprk_relatedwindowdays"] = 14;
        row["sprk_codingchunksize"] = 50;
        row["sprk_firmnoticeaienabled"] = false;
        row["sprk_defaultcodeset"] = new EntityReference("sprk_billingcodeset", codeSet);
        row["sprk_apexportmapping"] = "{\"version\":1}";
        ReturnRows(row);

        var settings = await CreateReader().GetAsync();

        settings.MatterApproverRoles.Should().Equal(BillingApproverRole.AssignedAttorney2, BillingApproverRole.AssignedTo);
        settings.ProjectApproverRoles.Should().Equal(BillingApproverRole.AssignedParalegal1);
        settings.WorkAssignmentApproverRoles.Should().Equal(BillingApproverRole.AssignedToExternal, BillingApproverRole.AssignedAttorney1);
        settings.ReliabilityThreshold.Should().Be(0.75m);
        settings.WeightAnalyzer.Should().Be(0.40m);
        settings.WeightArithmetic.Should().Be(0.30m);
        settings.WeightReferenceMatch.Should().Be(0.20m);
        settings.WeightSelfReport.Should().Be(0.10m);
        settings.ArithmeticFailCap.Should().Be(0.45m);
        settings.ReferenceMissingCap.Should().Be(0.70m);
        settings.RelatedWorkWindowDays.Should().Be(14);
        settings.CodingChunkSize.Should().Be(50);
        settings.FirmNoticeAiEnabled.Should().BeFalse();
        settings.DefaultCodeSetId.Should().Be(codeSet);
        settings.ApExportMappingJson.Should().Be("{\"version\":1}");
    }

    [Fact]
    public async Task GetAsync_OptionalColumnsMissing_KeepDefaultsAndNoCodeSet()
    {
        // A row that carries only the threshold: the other columns are null on the row (Dataverse omits them).
        ReturnRows(SettingsRow(0.80m));

        var settings = await CreateReader().GetAsync();
        var defaults = new EbillingSettings();

        settings.ReliabilityThreshold.Should().Be(0.80m);
        settings.DefaultCodeSetId.Should().BeNull("the optional lookup is not set on the row");
        settings.ApExportMappingJson.Should().BeNull();
        settings.MatterApproverRoles.Should().Equal(defaults.MatterApproverRoles, "a cleared multi-select keeps the default, so approvers never resolve to nobody");
        settings.CodingChunkSize.Should().Be(defaults.CodingChunkSize);
        settings.FirmNoticeAiEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetAsync_UnknownRoleValue_IsIgnoredAndWarned()
    {
        var row = SettingsRow();
        row["sprk_matterapproverroles"] = new OptionSetValueCollection(new List<OptionSetValue>
        {
            new((int)BillingApproverRole.AssignedParalegal2),
            new(100000099),
        });
        ReturnRows(row);

        var settings = await CreateReader().GetAsync();

        settings.MatterApproverRoles.Should().Equal(BillingApproverRole.AssignedParalegal2);
        VerifyWarning("unknown role value", Times.Once());
    }

    [Fact]
    public async Task GetAsync_NoRow_ReturnsTypedDefaultsEqualToTheSeedAndWarnsOnce()
    {
        ReturnRows();
        var reader = CreateReader();

        var first = await reader.GetAsync();
        var second = await reader.GetAsync();

        first.Should().BeEquivalentTo(new EbillingSettings());
        second.Should().BeSameAs(first, "the defaults are cached for the TTL like a loaded row");
        first.ReliabilityThreshold.Should().Be(0.90m);
        first.RelatedWorkWindowDays.Should().Be(7);
        first.CodingChunkSize.Should().Be(25);
        first.MatterApproverRoles.Should().Equal(BillingApproverRole.AssignedAttorney1, BillingApproverRole.AssignedToInternal);
        first.ProjectApproverRoles.Should().Equal(BillingApproverRole.AssignedAttorney1, BillingApproverRole.AssignedToInternal);
        first.WorkAssignmentApproverRoles.Should().Equal(BillingApproverRole.AssignedAttorney1, BillingApproverRole.AssignedToInternal);
        (first.WeightAnalyzer + first.WeightArithmetic + first.WeightReferenceMatch + first.WeightSelfReport).Should().Be(1.00m);
        VerifyWarning("no active", Times.Once());
    }

    [Fact]
    public async Task GetAsync_SecondCallWithinTtl_DoesNotQueryDataverseAgain()
    {
        ReturnRows(SettingsRow());
        var reader = CreateReader();

        var first = await reader.GetAsync();
        _time.Advance(EbillingSettingsReader.CacheTtl - TimeSpan.FromSeconds(1));
        var second = await reader.GetAsync();

        second.Should().BeSameAs(first);
        VerifyQueries(Times.Once());
    }

    [Fact]
    public async Task GetAsync_AfterTtl_ReturnsTheChangedValue()
    {
        ReturnRows(SettingsRow(0.90m));
        var reader = CreateReader();
        (await reader.GetAsync()).ReliabilityThreshold.Should().Be(0.90m);

        // An admin changes the threshold; the cached copy is still served until the TTL passes.
        ReturnRows(SettingsRow(0.85m));
        (await reader.GetAsync()).ReliabilityThreshold.Should().Be(0.90m);

        _time.Advance(EbillingSettingsReader.CacheTtl + TimeSpan.FromSeconds(1));
        (await reader.GetAsync()).ReliabilityThreshold.Should().Be(0.85m);
        VerifyQueries(Times.Exactly(2));
    }

    [Fact]
    public async Task GetAsync_DataverseFails_PropagatesAndIsNotCached()
    {
        _entityService
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse is down"));
        var reader = CreateReader();

        var act = () => reader.GetAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Dataverse is down");

        // Recovery: the next call reads again (the failure was not cached as defaults).
        ReturnRows(SettingsRow(0.82m));
        (await reader.GetAsync()).ReliabilityThreshold.Should().Be(0.82m);
    }

    [Fact]
    public void CacheTtl_IsShort()
    {
        // Task 025 constraint (ADR-009 exception): an absolute TTL of at most 60 seconds.
        EbillingSettingsReader.CacheTtl.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(60)).And.BeGreaterThan(TimeSpan.Zero);
    }

    // Task 073 (FR-26, I-5, D-89): the cap-below-threshold pair is not a platform-checked column rule, so the reader says so loudly.

    [Fact]
    public async Task GetAsync_ReferenceMissingCapNotBelowTheThreshold_LogsOneErrorPerLoad_ButStillReturnsTheRow()
    {
        var row = SettingsRow(0.80m);
        row["sprk_referencemissingcap"] = 0.80m;
        row["sprk_codingchunksize"] = 40;
        ReturnRows(row);
        var reader = CreateReader();

        var first = await reader.GetAsync();
        var second = await reader.GetAsync();

        first.ReliabilityThreshold.Should().Be(0.80m);
        first.CodingChunkSize.Should().Be(40, "the other settings are unaffected");
        first.RejectionInvariantProblem.Should().NotBeNull();
        second.Should().BeSameAs(first);
        VerifyError("breaks the rejection rule's invariant", Times.Once());

        _time.Advance(EbillingSettingsReader.CacheTtl + TimeSpan.FromSeconds(1));
        await reader.GetAsync();
        VerifyError("breaks the rejection rule's invariant", Times.Exactly(2));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1.5")]
    public async Task GetAsync_ThresholdOutOfRange_LogsAnError(string threshold)
    {
        ReturnRows(SettingsRow(decimal.Parse(threshold, System.Globalization.CultureInfo.InvariantCulture)));

        var settings = await CreateReader().GetAsync();

        settings.RejectionInvariantProblem.Should().NotBeNull();
        VerifyError("breaks the rejection rule's invariant", Times.Once());
    }

    [Fact]
    public async Task GetAsync_SeededValues_LogNoInvariantError()
    {
        var row = SettingsRow(0.90m);
        row["sprk_referencemissingcap"] = 0.80m;
        ReturnRows(row);
        var reader = CreateReader();

        var seeded = await reader.GetAsync();

        seeded.RejectionInvariantProblem.Should().BeNull();
        VerifyError("breaks the rejection rule's invariant", Times.Never());
    }

    [Fact]
    public async Task GetAsync_NoRow_TheDefaults_KeepTheInvariant_AndLogNoError()
    {
        ReturnRows();

        var settings = await CreateReader().GetAsync();

        settings.RejectionInvariantProblem.Should().BeNull();
        VerifyError("breaks the rejection rule's invariant", Times.Never());
    }

    [Theory]
    [InlineData("0.90", "0.80", false)]
    [InlineData("0.90", "0.89", false)]
    [InlineData("0.90", "0.90", true)]
    [InlineData("0.90", "0.95", true)]
    [InlineData("0.00", "0.00", true)]
    [InlineData("1.01", "0.50", true)]
    [InlineData("1.00", "0.99", false)]
    public void RejectionInvariantProblem_IsSetWhenTheCapIsNotBelowTheThreshold_OrTheThresholdIsOutsideZeroExclusiveToOne(
        string threshold, string cap, bool broken)
    {
        var settings = new EbillingSettings
        {
            ReliabilityThreshold = decimal.Parse(threshold, System.Globalization.CultureInfo.InvariantCulture),
            ReferenceMissingCap = decimal.Parse(cap, System.Globalization.CultureInfo.InvariantCulture),
        };

        (settings.RejectionInvariantProblem is not null).Should().Be(broken);
    }

    [Fact]
    public void RejectionInvariantProblem_AnArithmeticFailCapAtTheThreshold_IsInvalid_TheNfr06GridSetting()
    {
        // 073 review F1-c: the NFR-06 grid's threshold 0.50 with 046's default arithmetic cap 0.50. A field whose arithmetic check failed
        // would be capped at 0.50 and still reach the threshold, so it could support a rejection. The reference cap is fine here.
        var settings = new EbillingSettings { ReliabilityThreshold = 0.50m, ReferenceMissingCap = 0.40m, ArithmeticFailCap = 0.50m };

        settings.RejectionInvariantProblem.Should().NotBeNull().And.Contain("arithmetic-fail cap");
    }

    [Fact]
    public void RejectionInvariantProblem_AnArithmeticFailCapBelowTheThreshold_IsValid()
    {
        var settings = new EbillingSettings { ReliabilityThreshold = 0.50m, ReferenceMissingCap = 0.40m, ArithmeticFailCap = 0.49m };

        settings.RejectionInvariantProblem.Should().BeNull();
    }

    private void VerifyError(string messageFragment, Times times) =>
        _logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(messageFragment)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    private void VerifyQueries(Times times) =>
        _entityService.Verify(
            x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()),
            times);

    private void VerifyWarning(string messageFragment, Times times) =>
        _logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(messageFragment)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    /// <summary>Drives the memory cache's expiry from the fake time provider (no real waiting).</summary>
    private sealed class FakeClock(TimeProvider time) : Microsoft.Extensions.Internal.ISystemClock
    {
        public DateTimeOffset UtcNow => time.GetUtcNow();
    }
}
