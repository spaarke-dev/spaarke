using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.ServiceModel;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 082c-2 (ontology D-205; uac-r2 2026-11-04): <see cref="InvoiceReviewDetail"/>, the derived review detail, and
/// <see cref="ReviewDetailColumnProbe"/>, the column-missing check.
/// </summary>
public sealed class InvoiceReviewDetailTests
{
    private const string Firm = SubmissionReasonCodes.FirmMissing;
    private const string Unresolved = SubmissionReasonCodes.ProceduresUnresolved;
    private const string Gap = SubmissionReasonCodes.ProcedureGap;

    /// <summary>Every combination of the three hold reasons (with an unrelated code beside them half the time) and the value it derives.</summary>
    public static TheoryData<bool, bool, bool, bool, int?> EveryCombination()
    {
        var data = new TheoryData<bool, bool, bool, bool, int?>();
        foreach (var firm in new[] { false, true })
        {
            foreach (var unresolved in new[] { false, true })
            {
                foreach (var gap in new[] { false, true })
                {
                    foreach (var other in new[] { false, true })
                    {
                        int? expected = firm ? InvoiceReviewDetail.WaitingForFirm
                            : unresolved ? InvoiceReviewDetail.BillingTermsNotChecked
                            : gap ? InvoiceReviewDetail.WaitingForBillingTerms
                            : null;
                        data.Add(firm, unresolved, gap, other, expected);
                    }
                }
            }
        }

        return data;
    }

    [Theory(DisplayName = "082c-2 (D-205): ReviewDetailOf over every reason combination is its priority value - firm > could not be checked > waiting for terms > blank")]
    [MemberData(nameof(EveryCombination))]
    public void ReviewDetailOf_EveryCombination_MapsToItsPriorityValue(bool firm, bool unresolved, bool gap, bool other, int? expected)
    {
        var reasons = new List<string>();
        if (firm) reasons.Add(Firm);
        if (unresolved) reasons.Add(Unresolved);
        if (gap) reasons.Add(Gap);
        if (other) reasons.Add(SubmissionReasonCodes.Blocked);

        InvoiceReviewDetail.ReviewDetailOf(reasons).Should().Be(expected);
        InvoiceReviewDetail.ReviewDetailOf(reasons.AsEnumerable().Reverse()).Should().Be(expected, "the order of the reasons does not matter");
        InvoiceReviewDetail.ReviewDetailOf(reasons.ToHashSet()).Should().Be(expected);
    }

    [Fact(DisplayName = "082c-2: no reasons (or null) derive blank")]
    public void ReviewDetailOf_None_IsBlank()
    {
        InvoiceReviewDetail.ReviewDetailOf([]).Should().BeNull();
        InvoiceReviewDetail.ReviewDetailOf(null).Should().BeNull();
        InvoiceReviewDetail.ValueOf(null).Should().BeNull();
        InvoiceReviewDetail.ValueOf(InvoiceReviewDetail.WaitingForFirm)!.Value.Should().Be(659490002);
    }

    [Fact(DisplayName = "082c-2 (uac-r2 (a)): the option values are pinned, publisher-range, distinct, with the owner's labels")]
    public void Options_ArePinned()
    {
        InvoiceReviewDetail.Column.Should().Be("sprk_reviewdetail");
        InvoiceReviewDetail.Options.Should().Equal(
            (659490000, "Waiting for billing terms"),
            (659490001, "Billing terms could not be checked"),
            (659490002, "Waiting for the firm to be confirmed"));
        InvoiceReviewDetail.WaitingForBillingTerms.Should().Be(659490000);
        InvoiceReviewDetail.BillingTermsNotChecked.Should().Be(659490001);
        InvoiceReviewDetail.WaitingForFirm.Should().Be(659490002);
    }

    // ── The column probe ──

    private static FaultException<OrganizationServiceFault> MissingFault(string column) => new(
        new OrganizationServiceFault { ErrorCode = unchecked((int)0x80041103), Message = $"'sprk_invoice' entity doesn't contain attribute with Name = '{column}'" },
        new FaultReason($"'sprk_invoice' entity doesn't contain attribute with Name = '{column}'"));

    [Fact(DisplayName = "082c-2: IsColumnMissing matches the attribute-missing fault naming this column only")]
    public void IsColumnMissing_MatchesThisColumnOnly()
    {
        InvoiceReviewDetail.IsColumnMissing(MissingFault("sprk_reviewdetail")).Should().BeTrue();
        InvoiceReviewDetail.IsColumnMissing(new InvalidOperationException("wrapped", MissingFault("sprk_reviewdetail"))).Should().BeTrue();
        InvoiceReviewDetail.IsColumnMissing(MissingFault("sprk_firmhold")).Should().BeFalse("another column's fault is not this one");
        InvoiceReviewDetail.IsColumnMissing(new TimeoutException("sprk_reviewdetail timed out")).Should().BeFalse();
    }

    private static Mock<IGenericEntityService> Entities(Func<Exception?> fault)
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression q, CancellationToken _) =>
            {
                q.EntityName.Should().Be("sprk_invoice");
                q.ColumnSet.Columns.Should().Equal("sprk_reviewdetail");
                q.TopCount.Should().Be(1);
                return fault() is { } ex ? Task.FromException<EntityCollection>(ex) : Task.FromResult(new EntityCollection());
            });
        return entities;
    }

    [Fact(DisplayName = "082c-2: a present column is read once and cached for good")]
    public async Task Probe_Present_IsCached()
    {
        var probe = new ReviewDetailColumnProbe(new FakeTimeProvider());
        var entities = Entities(() => null);

        (await probe.IsPresentAsync(entities.Object, Mock.Of<ILogger>(), CancellationToken.None)).Should().BeTrue();
        (await probe.IsPresentAsync(entities.Object, Mock.Of<ILogger>(), CancellationToken.None)).Should().BeTrue();

        entities.Invocations.Should().HaveCount(1);
        probe.WriteColumn.Should().BeTrue();
    }

    [Fact(DisplayName = "082c-2: a missing column is skipped with one warning, re-checked after the refresh interval, and found once the script ran")]
    public async Task Probe_Missing_SkipsWithOneWarning_AndRechecks()
    {
        var clock = new FakeTimeProvider();
        var probe = new ReviewDetailColumnProbe(clock);
        var missing = true;
        var entities = Entities(() => missing ? MissingFault("sprk_reviewdetail") : null);
        var logger = new Mock<ILogger>();

        (await probe.IsPresentAsync(entities.Object, logger.Object, CancellationToken.None)).Should().BeFalse();
        probe.WriteColumn.Should().BeFalse();
        (await probe.IsPresentAsync(entities.Object, logger.Object, CancellationToken.None)).Should().BeFalse();
        entities.Invocations.Should().HaveCount(1, "a fresh missing answer is not re-read");

        clock.Advance(ReviewDetailColumnProbe.RefreshInterval);
        probe.WriteColumn.Should().BeTrue("a stale missing answer is no reason to leave the column out");
        (await probe.IsPresentAsync(entities.Object, logger.Object, CancellationToken.None)).Should().BeFalse();
        entities.Invocations.Should().HaveCount(2);

        clock.Advance(ReviewDetailColumnProbe.RefreshInterval);
        missing = false;
        (await probe.IsPresentAsync(entities.Object, logger.Object, CancellationToken.None)).Should().BeTrue();

        logger.Invocations.Count(i => i.Method.Name == nameof(ILogger.Log) && (LogLevel)i.Arguments[0] == LogLevel.Warning)
            .Should().Be(1, "the missing column is logged once per process");
    }

    [Fact(DisplayName = "082c-2: a probe that faults for another reason answers present (the write then names the column: never a detail left behind)")]
    public async Task Probe_OtherFault_AnswersPresent_Uncached()
    {
        var probe = new ReviewDetailColumnProbe(new FakeTimeProvider());
        var entities = Entities(() => new TimeoutException("read"));

        (await probe.IsPresentAsync(entities.Object, Mock.Of<ILogger>(), CancellationToken.None)).Should().BeTrue();
        (await probe.IsPresentAsync(entities.Object, Mock.Of<ILogger>(), CancellationToken.None)).Should().BeTrue();
        entities.Invocations.Should().HaveCount(2, "an unknown answer is not cached");
    }
}
