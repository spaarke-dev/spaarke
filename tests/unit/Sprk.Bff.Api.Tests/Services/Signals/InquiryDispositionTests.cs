using System.Data;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Signals;

/// <summary>
/// Task 071 (disposition accrual; spec FR-37, criterion 10; the action-rate source rule of spec section 7).
/// The Dataverse boundary is faked (module boundary, ADR-038).
/// </summary>
/// <remarks>
/// ADR-038 pairing: the entity names, columns and option values asserted here were checked against the real spaarkedev1
/// schema on 2026-10-09 by Dataverse describe AND by running the same GROUP BY shapes through read_query (both tables
/// returned no rows and no error): sprk_servicerequest (sprk_direction Inbound 100000000 / Outbound 100000001,
/// sprk_disposition 100000000..100000003, sprk_regardingmatter, sprk_regardingorganization, statecode), sprk_communication
/// (sprk_direction Incoming 100000000, sprk_regardingservicerequest), sprk_signal (sprk_policycode, sprk_lane Decide
/// 100000000 / Do 100000001, sprk_resolutiontype Acted 100000000 .. Policy Retired 100000004, sprk_decisionrecord).
/// </remarks>
public sealed class InquiryDispositionTests
{
    private static readonly Guid Reply = Guid.NewGuid();
    private static readonly Guid Inquiry = Guid.NewGuid();
    private static readonly Guid Matter = Guid.NewGuid();
    private static readonly Guid FirmA = Guid.NewGuid();
    private static readonly Guid FirmB = Guid.NewGuid();

    private sealed class FakeUser(string json, bool fail = false) : IDataverseUserClient
    {
        public List<string> Gets { get; } = [];

        public Task<DataverseUserResponse> GetAsync(string p, CancellationToken ct)
        {
            Gets.Add(Uri.UnescapeDataString(p));
            return Task.FromResult(fail
                ? DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied, "no")
                : DataverseUserResponse.Ok(200, JsonDocument.Parse(json).RootElement.Clone()));
        }

        public Task<DataverseUserResponse> PostAsync(string a, string j, CancellationToken ct) => throw new InvalidOperationException();
        public Task<DataverseUserResponse> PostAsync(string a, string j, bool p, CancellationToken ct) => throw new InvalidOperationException();
        public Task<DataverseUserResponse> PatchAsync(string p, string j, CancellationToken ct) => throw new InvalidOperationException();
        public Task<DataverseUserResponse> DeleteAsync(string p, CancellationToken ct) => throw new InvalidOperationException();
    }

    private sealed class Rig
    {
        public Mock<IFieldMappingDataverseService> Db { get; } = new();
        public List<Dictionary<string, object?>> Writes { get; } = [];

        public Rig(Dictionary<string, object?> reply, Dictionary<string, object?> inquiry)
        {
            Db.Setup(d => d.RetrieveRecordFieldsAsync("sprk_communication", Reply, It.IsAny<string[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(reply);
            Db.Setup(d => d.RetrieveRecordFieldsAsync("sprk_servicerequest", Inquiry, It.IsAny<string[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(inquiry);
            Db.Setup(d => d.UpdateRecordFieldsIfUnchangedAsync("sprk_servicerequest", Inquiry, It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .Callback<string, Guid, Dictionary<string, object?>, long, CancellationToken>((_, _, f, _, _) => Writes.Add(f)).Returns(Task.CompletedTask);
            Db.Setup(d => d.UpdateExistingRecordFieldsAsync("sprk_servicerequest", Inquiry, It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
                .Callback<string, Guid, Dictionary<string, object?>, CancellationToken>((_, _, f, _) => Writes.Add(f)).Returns(Task.CompletedTask);
        }

        public InquiryDispositionService Sut(string json = "{\"value\":[]}") =>
            new(Db.Object, new FakeUser(json), NullLogger<InquiryDispositionService>.Instance);
    }

    private static InquiryDispositionService Reader(FakeUser user) =>
        new(new Mock<IFieldMappingDataverseService>().Object, user, NullLogger<InquiryDispositionService>.Instance);

    private static Dictionary<string, object?> InboundReply(Guid? toInquiry = null, long direction = 100000000) => new()
    {
        ["sprk_direction"] = direction,
        ["_sprk_regardingservicerequest_value"] = (toInquiry ?? Inquiry).ToString("D"),
    };

    private static Dictionary<string, object?> OpenInquiry(long direction = 100000001, long? disposition = null, long version = 42) => new()
    {
        ["sprk_direction"] = direction,
        ["sprk_disposition"] = disposition,
        ["versionnumber"] = version,
    };

    // ---- criterion 1: the reply resolves the Inquiry and sets the disposition -------------------------------------

    [Theory]
    [InlineData(InquiryDisposition.WriteOff, 100000000)]
    [InlineData(InquiryDisposition.BudgetRevised, 100000001)]
    [InlineData(InquiryDisposition.ScopeApproved, 100000002)]
    [InlineData(InquiryDisposition.NoAction, 100000003)]
    public async Task Inbound_reply_to_an_outbound_inquiry_sets_the_disposition_and_closes_it(InquiryDisposition d, int value)
    {
        var rig = new Rig(InboundReply(), OpenInquiry());

        var result = await rig.Sut().ResolveFromReplyAsync(Reply, d, default);

        result.Outcome.Should().Be(InquiryResolutionOutcome.Resolved);
        result.ServiceRequestId.Should().Be(Inquiry);
        rig.Writes.Should().ContainSingle();
        rig.Writes[0]["sprk_disposition"].Should().Be(value);
        rig.Writes[0]["statecode"].Should().Be(1);
        rig.Writes[0]["statuscode"].Should().Be(2);
        rig.Db.Verify(x => x.UpdateRecordFieldsIfUnchangedAsync("sprk_servicerequest", Inquiry, It.IsAny<Dictionary<string, object?>>(), 42, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Resolution_without_a_known_version_still_never_creates_a_row()
    {
        var inquiry = OpenInquiry();
        inquiry.Remove("versionnumber");
        var rig = new Rig(InboundReply(), inquiry);

        var result = await rig.Sut().ResolveFromReplyAsync(Reply, InquiryDisposition.NoAction, default);

        result.Succeeded.Should().BeTrue();
        rig.Db.Verify(x => x.UpdateExistingRecordFieldsAsync("sprk_servicerequest", Inquiry, It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Once);
        rig.Db.Verify(x => x.UpdateRecordFieldsAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()), Times.Never);
    }

    [Fact]
    public async Task An_outgoing_communication_does_not_resolve_anything()
    {
        var rig = new Rig(InboundReply(direction: 100000001), OpenInquiry());

        var result = await rig.Sut().ResolveFromReplyAsync(Reply, InquiryDisposition.NoAction, default);

        result.Outcome.Should().Be(InquiryResolutionOutcome.NotAReply);
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_reply_filed_against_nothing_or_a_missing_communication_resolves_nothing()
    {
        var unfiled = new Dictionary<string, object?> { ["sprk_direction"] = 100000000L, ["_sprk_regardingservicerequest_value"] = null };
        foreach (var reply in new[] { unfiled, new Dictionary<string, object?>() })
        {
            var rig = new Rig(reply, OpenInquiry());
            (await rig.Sut().ResolveFromReplyAsync(Reply, InquiryDisposition.NoAction, default)).Outcome
                .Should().Be(InquiryResolutionOutcome.NotAReply);
            rig.Writes.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task An_inbound_service_request_is_not_an_inquiry()
    {
        var rig = new Rig(InboundReply(), OpenInquiry(direction: 100000000));

        var result = await rig.Sut().ResolveFromReplyAsync(Reply, InquiryDisposition.WriteOff, default);

        result.Outcome.Should().Be(InquiryResolutionOutcome.NotAnInquiry);
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_second_reply_never_overwrites_the_first_disposition()
    {
        var rig = new Rig(InboundReply(), OpenInquiry(disposition: 100000000));

        var result = await rig.Sut().ResolveFromReplyAsync(Reply, InquiryDisposition.NoAction, default);

        result.Outcome.Should().Be(InquiryResolutionOutcome.AlreadyResolved);
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Two_replies_racing_cannot_both_win()
    {
        var rig = new Rig(InboundReply(), OpenInquiry());
        rig.Db.Setup(d => d.UpdateRecordFieldsIfUnchangedAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DBConcurrencyException());

        var result = await rig.Sut().ResolveFromReplyAsync(Reply, InquiryDisposition.NoAction, default);

        result.Outcome.Should().Be(InquiryResolutionOutcome.AlreadyResolved);
    }

    [Fact]
    public async Task A_dataverse_failure_is_reported_and_cancellation_is_not_swallowed()
    {
        var rig = new Rig(InboundReply(), OpenInquiry());
        rig.Db.Setup(d => d.UpdateRecordFieldsIfUnchangedAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));
        (await rig.Sut().ResolveFromReplyAsync(Reply, InquiryDisposition.NoAction, default)).Outcome.Should().Be(InquiryResolutionOutcome.Failed);

        rig.Db.Setup(d => d.UpdateRecordFieldsIfUnchangedAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var act = () => rig.Sut().ResolveFromReplyAsync(Reply, InquiryDisposition.NoAction, default);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Malformed_requests_touch_nothing()
    {
        var rig = new Rig(InboundReply(), OpenInquiry());

        (await rig.Sut().ResolveFromReplyAsync(Guid.Empty, InquiryDisposition.NoAction, default)).Outcome.Should().Be(InquiryResolutionOutcome.Invalid);
        (await rig.Sut().ResolveFromReplyAsync(Reply, (InquiryDisposition)7, default)).Outcome.Should().Be(InquiryResolutionOutcome.Invalid);
        rig.Db.VerifyNoOtherCalls();
    }

    // ---- criterion 5 (negative): two vocabularies, two columns ----------------------------------------------------

    [Fact]
    public async Task Disposition_and_decision_outcome_stay_distinct()
    {
        Enum.GetNames<InquiryDisposition>().Should().BeEquivalentTo("WriteOff", "BudgetRevised", "ScopeApproved", "NoAction");
        Enum.GetNames<InquiryDisposition>().Should().NotContain(["Authorized", "Denied", "Dismissed"]);

        var rig = new Rig(InboundReply(), OpenInquiry());
        await rig.Sut().ResolveFromReplyAsync(Reply, InquiryDisposition.ScopeApproved, default);
        rig.Writes[0].Keys.Should().NotContain("sprk_decisionoutcome");
        rig.Db.Invocations.Select(i => i.ToString()).Should().NotContain(s => s!.Contains("decisionrecord"));

        InquiryDispositionService.BuildFetch(["disposition"], Matter, null).Should().NotContain("decision");
    }

    // ---- criterion 2: per matter ----------------------------------------------------------------------------------

    [Fact]
    public async Task Per_matter_returns_every_resolved_inquiry_by_disposition()
    {
        var user = new FakeUser("{\"value\":[{\"disposition\":100000000,\"n\":2},{\"disposition\":100000001,\"n\":1},{\"disposition\":100000003,\"n\":4}]}");

        var tally = await Reader(user).GetByMatterAsync(Matter, default);

        tally.Should().Be(new DispositionTally(2, 1, 0, 4));
        tally.Total.Should().Be(7);
        var q = user.Gets.Single();
        q.Should().StartWith("sprk_servicerequests?fetchXml=");
        q.Should().Contain($"attribute=\"sprk_regardingmatter\" operator=\"eq\" value=\"{Matter:D}\"");
        q.Should().Contain("attribute=\"sprk_direction\" operator=\"eq\" value=\"100000001\"");
        q.Should().Contain("attribute=\"sprk_disposition\" operator=\"not-null\"");
        q.Should().NotContain("statecode", "resolving closes the row; the closed ones are what accrues");
    }

    // ---- criterion 3: per outside firm ----------------------------------------------------------------------------

    [Fact]
    public async Task Per_outside_firm_groups_dispositions_by_firm()
    {
        var json = $"{{\"value\":[{{\"firm\":\"{FirmA}\",\"disposition\":100000000,\"n\":3}},{{\"firm\":\"{FirmB}\",\"disposition\":100000002,\"n\":1}},"
                 + $"{{\"firm\":\"{FirmA}\",\"disposition\":100000001,\"n\":2}},{{\"disposition\":100000003,\"n\":5}}]}}";
        var user = new FakeUser(json);

        var byFirm = await Reader(user).GetByOutsideFirmAsync(null, default);

        byFirm.Should().HaveCount(3);
        byFirm.Single(f => f.FirmId == FirmA).Dispositions.Should().Be(new DispositionTally(3, 2, 0, 0));
        byFirm.Single(f => f.FirmId == FirmB).Dispositions.Should().Be(new DispositionTally(0, 0, 1, 0));
        byFirm.Single(f => f.FirmId is null).Dispositions.Should().Be(new DispositionTally(0, 0, 0, 5));
        var q = user.Gets.Single();
        q.Should().Contain("alias=\"firm\" groupby=\"true\"").And.Contain("attribute name=\"sprk_regardingorganization\"");
        q.Should().NotContain("sprk_regardingmatter");
    }

    [Fact]
    public async Task One_firm_across_all_matters_filters_on_the_firm_lookup_only()
    {
        var user = new FakeUser("{\"value\":[]}");

        (await Reader(user).GetByOutsideFirmAsync(FirmA, default)).Should().BeEmpty();

        user.Gets.Single().Should().Contain($"attribute=\"sprk_regardingorganization\" operator=\"eq\" value=\"{FirmA:D}\"")
            .And.NotContain("sprk_regardingmatter");
    }

    [Fact]
    public async Task A_refused_read_fails_loudly_never_as_an_empty_tally()
    {
        var act = () => Reader(new FakeUser("{}", fail: true)).GetByMatterAsync(Matter, default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---- criterion 4: action rate reads sprk_resolutiontype, not Decision Records ---------------------------------

    [Fact]
    public async Task Do_lane_action_rate_comes_from_the_signal_resolution_type()
    {
        // 10 Do-lane Signals: 6 Acted, 2 Dismissed, 1 Condition Cleared, 1 still open. None has a Decision Record
        // (a Do action runs with no gate), so a Decision-Record-based rate would be 0 of 10.
        var json = "{\"value\":["
            + "{\"policy\":\"POL-OVERDUE\",\"lane\":100000001,\"resolution\":100000000,\"n\":6},"
            + "{\"policy\":\"POL-OVERDUE\",\"lane\":100000001,\"resolution\":100000001,\"n\":2},"
            + "{\"policy\":\"POL-OVERDUE\",\"lane\":100000001,\"resolution\":100000002,\"n\":1},"
            + "{\"policy\":\"POL-OVERDUE\",\"lane\":100000001,\"n\":1},"
            + "{\"policy\":\"POL-BUDGET\",\"lane\":100000000,\"resolution\":100000001,\"n\":4}]}";
        var user = new FakeUser(json);

        var rates = await new PolicyActionRateService(user).GetAsync(default);

        var overdue = rates.Single(r => r.PolicyCode == "POL-OVERDUE");
        overdue.Lane.Should().Be(SignalLane.Do);
        overdue.Surfaced.Should().Be(10);
        overdue.Acted.Should().Be(6);
        overdue.Rate.Should().BeApproximately(0.6, 1e-9);
        rates.Single(r => r.PolicyCode == "POL-BUDGET").Rate.Should().Be(0);

        var q = user.Gets.Single();
        q.Should().StartWith("sprk_signals?fetchXml=");
        q.Should().Contain("attribute name=\"sprk_resolutiontype\"");
        q.Should().NotContain("decisionrecord").And.NotContain("sprk_decisionoutcome");
    }

    [Fact]
    public void Action_rate_with_nothing_surfaced_has_no_rate_and_acted_is_the_live_Acted_value()
    {
        new PolicyActionRate("P", SignalLane.Do, 0, 0).Rate.Should().BeNull();
        PolicyActionRateService.Acted.Should().Be(100000000);
        ((int)SignalLane.Do).Should().Be(100000001);
    }
}
