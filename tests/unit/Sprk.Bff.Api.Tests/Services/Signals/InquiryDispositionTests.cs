using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Signals;

/// <summary>
/// Task 071 (disposition accrual; spec FR-37, criterion 10; decision D-111; the H-7 action rate of spec section 7).
/// The Dataverse boundary is faked at <see cref="IDataverseUserClient"/> (module boundary, ADR-038).
/// </summary>
/// <remarks>
/// ADR-038 pairing: the entity names, columns and option values asserted here were checked against the real spaarkedev1
/// schema on 2026-10-09 by Dataverse describe AND by running the same GROUP BY shapes through read_query (both tables
/// returned no rows and no error): sprk_servicerequest (sprk_direction Inbound 100000000 / Outbound 100000001,
/// sprk_disposition 100000000..100000003, sprk_regardingmatter, sprk_regardingorganization, statecode, statuscode,
/// versionnumber), sprk_communication (sprk_direction Incoming 100000000, sprk_regardingservicerequest,
/// sprk_associationstatus Resolved 100000000 / Suggested 100000003), sprk_todo (statuscode Completed 2 with state Inactive 1,
/// sprk_completedon, sprk_regardingservicerequest), sprk_signal (sprk_policycode, sprk_policyversion, sprk_lane Decide
/// 100000000 / Do 100000001, sprk_resolutiontype Acted 100000000 / Dismissed 100000001, sprk_resolvedon,
/// sprk_decisionrecord).
/// </remarks>
public sealed class InquiryDispositionTests
{
    private static readonly Guid Reply = Guid.NewGuid();
    private static readonly Guid Inquiry = Guid.NewGuid();
    private static readonly Guid Matter = Guid.NewGuid();
    private static readonly Guid FirmA = Guid.NewGuid();
    private static readonly Guid FirmB = Guid.NewGuid();
    private static readonly Guid VersionA = Guid.NewGuid();

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly Clock Now = new(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));

    /// <summary>A scripted caller-identity Dataverse: the reply row, the service request row (one per read), and what each PATCH answers.</summary>
    private sealed class FakeUser : IDataverseUserClient
    {
        public string ReplyJson { get; set; } = Json.Reply();
        public Queue<string> RowJson { get; } = new();
        public Queue<int> InquiryPatchStatuses { get; } = new();
        public int TodoPatchStatus { get; set; } = 204;
        public int ReplyGetStatus { get; set; } = 200;
        public string? TallyJson { get; set; }
        public int TallyStatus { get; set; } = 200;

        public List<string> Gets { get; } = [];
        public List<(string Path, string Body, long? Version)> Patches { get; } = [];

        private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

        public Task<DataverseUserResponse> GetAsync(string p, CancellationToken ct)
        {
            var path = Uri.UnescapeDataString(p);
            Gets.Add(path);
            if (path.StartsWith("sprk_communications("))
                return Task.FromResult(ReplyGetStatus == 200
                    ? DataverseUserResponse.Ok(200, J(ReplyJson))
                    : DataverseUserResponse.Fail(ReplyGetStatus, "X", "x"));
            if (path.StartsWith("sprk_servicerequests("))
                return Task.FromResult(DataverseUserResponse.Ok(200, J(RowJson.Count > 1 ? RowJson.Dequeue() : RowJson.Peek())));
            return Task.FromResult(TallyStatus == 200
                ? DataverseUserResponse.Ok(200, J(TallyJson ?? "{\"value\":[]}"))
                : DataverseUserResponse.Fail(TallyStatus, DataverseUserClientErrorCodes.AccessDenied, "no"));
        }

        public Task<DataverseUserResponse> PatchAsync(string p, string j, CancellationToken ct)
        {
            Patches.Add((p, j, null));
            return Task.FromResult(p.StartsWith("sprk_todos(") && TodoPatchStatus != 204
                ? DataverseUserResponse.Fail(TodoPatchStatus, "X", "x")
                : DataverseUserResponse.Ok(204, null));
        }

        public Task<DataverseUserResponse> PatchAsync(string p, string j, long version, CancellationToken ct)
        {
            Patches.Add((p, j, version));
            var status = InquiryPatchStatuses.Count > 0 ? InquiryPatchStatuses.Dequeue() : 204;
            return Task.FromResult(status is >= 200 and < 300
                ? DataverseUserResponse.Ok(status, null)
                : DataverseUserResponse.Fail(status, "X", "x"));
        }

        public Task<DataverseUserResponse> PostAsync(string a, string j, CancellationToken ct) => throw new InvalidOperationException("no post expected");
        public Task<DataverseUserResponse> PostAsync(string a, string j, bool p, CancellationToken ct) => throw new InvalidOperationException("no post expected");
        public Task<DataverseUserResponse> DeleteAsync(string p, CancellationToken ct) => throw new InvalidOperationException("no delete expected");
    }

    private static class Json
    {
        public static string Reply(long direction = 100000000, Guid? toInquiry = null, long association = 100000000) =>
            $"{{\"sprk_direction\":{direction},\"_sprk_regardingservicerequest_value\":\"{toInquiry ?? Inquiry}\",\"sprk_associationstatus\":{association}}}";

        public static string Row(long direction = 100000001, long? disposition = null, long state = 0, long? version = 42) =>
            "{\"sprk_direction\":" + direction
            + ",\"sprk_disposition\":" + (disposition?.ToString() ?? "null")
            + ",\"statecode\":" + state
            + (version is null ? "" : ",\"versionnumber\":" + version) + "}";
    }

    private static InquiryDispositionService Sut(FakeUser user) =>
        new(user, Now, NullLogger<InquiryDispositionService>.Instance);

    private static FakeUser Open(string? reply = null, string? row = null)
    {
        var user = new FakeUser();
        if (reply is not null)
            user.ReplyJson = reply;
        user.RowJson.Enqueue(row ?? Json.Row());
        return user;
    }

    private static string TodoPath => $"sprk_todos({InquiryDispositionService.TodoIdFor(Inquiry):D})";

    // ---- criterion 1: a reply resolves the Inquiry with a disposition, recorded by a person AS THE CALLER ----------

    [Theory]
    [InlineData(100000000)]
    [InlineData(100000001)]
    [InlineData(100000002)]
    [InlineData(100000003)]
    public async Task Recording_sets_the_disposition_closes_the_inquiry_and_completes_the_todo_as_the_caller(int disposition)
    {
        var user = Open();

        var result = await Sut(user).RecordDispositionAsync(Inquiry, Reply, disposition, default);

        result.Outcome.Should().Be(InquiryResolutionOutcome.Resolved);
        result.TodoCompleted.Should().BeTrue();

        var inquiryPatch = user.Patches.Single(p => p.Path == $"sprk_servicerequests({Inquiry:D})");
        inquiryPatch.Version.Should().Be(42, "the write is conditional on the version that was read");
        using var body = JsonDocument.Parse(inquiryPatch.Body);
        body.RootElement.GetProperty("sprk_disposition").GetInt32().Should().Be(disposition);
        body.RootElement.GetProperty("statecode").GetInt32().Should().Be(1);
        body.RootElement.GetProperty("statuscode").GetInt32().Should().Be(2);

        var todoPatch = user.Patches.Single(p => p.Path == TodoPath);
        todoPatch.Body.Should().Contain("\"statecode\":1").And.Contain("\"statuscode\":2").And.Contain("2026-10-09T12:00:00");
    }

    [Fact]
    public async Task A_suggested_association_is_confirmed_by_the_choice_and_a_resolved_one_is_left_alone()
    {
        var suggested = Open(reply: Json.Reply(association: 100000003));
        await Sut(suggested).RecordDispositionAsync(Inquiry, Reply, 100000001, default);
        var confirm = suggested.Patches.Single(p => p.Path.StartsWith("sprk_communications("));
        confirm.Body.Should().Be("{\"sprk_associationstatus\":100000000}");

        var resolved = Open(reply: Json.Reply(association: 100000000));
        await Sut(resolved).RecordDispositionAsync(Inquiry, Reply, 100000001, default);
        resolved.Patches.Should().NotContain(p => p.Path.StartsWith("sprk_communications("));
    }

    [Fact]
    public async Task A_missing_todo_does_not_stop_the_disposition()
    {
        var user = Open();
        user.TodoPatchStatus = 404;

        var result = await Sut(user).RecordDispositionAsync(Inquiry, Reply, 100000003, default);

        result.Outcome.Should().Be(InquiryResolutionOutcome.Resolved);
        result.TodoCompleted.Should().BeFalse();
    }

    // ---- the refusals: nothing is written ------------------------------------------------------------------------

    [Fact]
    public async Task A_reply_that_is_not_incoming_is_refused()
    {
        var user = Open(reply: Json.Reply(direction: 100000001));

        (await Sut(user).RecordDispositionAsync(Inquiry, Reply, 100000003, default)).Outcome.Should().Be(InquiryResolutionOutcome.NotAReply);
        user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task A_reply_filed_against_another_service_request_is_refused()
    {
        var user = Open(reply: Json.Reply(toInquiry: Guid.NewGuid()));

        (await Sut(user).RecordDispositionAsync(Inquiry, Reply, 100000003, default)).Outcome.Should().Be(InquiryResolutionOutcome.ReplyNotForInquiry);
        user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task A_reply_the_caller_cannot_read_or_that_is_missing_gets_one_answer()
    {
        foreach (var status in new[] { 403, 404 })
        {
            var user = Open();
            user.ReplyGetStatus = status;
            (await Sut(user).RecordDispositionAsync(Inquiry, Reply, 100000003, default)).Outcome.Should().Be(InquiryResolutionOutcome.NotFound);
            user.Patches.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task An_inbound_service_request_is_not_an_inquiry()
    {
        var user = Open(row: Json.Row(direction: 100000000));

        (await Sut(user).RecordDispositionAsync(Inquiry, Reply, 100000000, default)).Outcome.Should().Be(InquiryResolutionOutcome.NotAnInquiry);
        user.Patches.Should().BeEmpty();
    }

    [Theory]
    [InlineData(100000000L, 0L)]
    [InlineData(null, 1L)]
    public async Task A_second_disposition_never_overwrites_the_first(long? disposition, long state)
    {
        var user = Open(row: Json.Row(disposition: disposition, state: state));

        (await Sut(user).RecordDispositionAsync(Inquiry, Reply, 100000003, default)).Outcome.Should().Be(InquiryResolutionOutcome.AlreadyResolved);
        user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task A_missing_version_writes_nothing()
    {
        var user = Open(row: Json.Row(version: null));

        var result = await Sut(user).RecordDispositionAsync(Inquiry, Reply, 100000003, default);

        result.Outcome.Should().Be(InquiryResolutionOutcome.Failed);
        user.Patches.Should().BeEmpty("an unconditional write would let two people both win");
    }

    [Fact]
    public async Task A_caller_without_Write_is_denied_by_dataverse_and_the_todo_is_not_touched()
    {
        var user = Open();
        user.InquiryPatchStatuses.Enqueue(403);

        var result = await Sut(user).RecordDispositionAsync(Inquiry, Reply, 100000003, default);

        result.Outcome.Should().Be(InquiryResolutionOutcome.Denied);
        user.Patches.Should().ContainSingle().Which.Path.Should().StartWith("sprk_servicerequests(");
    }

    [Fact]
    public async Task Malformed_requests_touch_nothing()
    {
        var user = Open();

        (await Sut(user).RecordDispositionAsync(Guid.Empty, Reply, 100000003, default)).Outcome.Should().Be(InquiryResolutionOutcome.Invalid);
        (await Sut(user).RecordDispositionAsync(Inquiry, Guid.Empty, 100000003, default)).Outcome.Should().Be(InquiryResolutionOutcome.Invalid);
        (await Sut(user).RecordDispositionAsync(Inquiry, Reply, 7, default)).Outcome.Should().Be(InquiryResolutionOutcome.Invalid);
        user.Gets.Should().BeEmpty();
        user.Patches.Should().BeEmpty();
    }

    // ---- the 412 rule ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task On_a_412_the_row_is_re_read_and_the_write_retried_once_if_the_disposition_is_still_empty()
    {
        var user = new FakeUser();
        user.RowJson.Enqueue(Json.Row(version: 42));
        user.RowJson.Enqueue(Json.Row(version: 43)); // changed (an unrelated field), disposition still empty
        user.InquiryPatchStatuses.Enqueue(412);

        var result = await Sut(user).RecordDispositionAsync(Inquiry, Reply, 100000002, default);

        result.Outcome.Should().Be(InquiryResolutionOutcome.Resolved);
        user.Patches.Where(p => p.Path.StartsWith("sprk_servicerequests(")).Select(p => p.Version).Should().Equal(42L, 43L);
    }

    [Fact]
    public async Task On_a_412_where_someone_else_has_recorded_it_the_answer_is_already_resolved()
    {
        var user = new FakeUser();
        user.RowJson.Enqueue(Json.Row(version: 42));
        user.RowJson.Enqueue(Json.Row(disposition: 100000000, state: 1, version: 43));
        user.InquiryPatchStatuses.Enqueue(412);

        var result = await Sut(user).RecordDispositionAsync(Inquiry, Reply, 100000002, default);

        result.Outcome.Should().Be(InquiryResolutionOutcome.AlreadyResolved);
        user.Patches.Should().ContainSingle("the second attempt never wrote");
    }

    [Fact]
    public async Task A_second_412_stops_after_one_retry()
    {
        var user = new FakeUser();
        user.RowJson.Enqueue(Json.Row(version: 42));
        user.RowJson.Enqueue(Json.Row(version: 43));
        user.InquiryPatchStatuses.Enqueue(412);
        user.InquiryPatchStatuses.Enqueue(412);

        var result = await Sut(user).RecordDispositionAsync(Inquiry, Reply, 100000002, default);

        result.Outcome.Should().Be(InquiryResolutionOutcome.AlreadyResolved);
        user.Patches.Count(p => p.Path.StartsWith("sprk_servicerequests(")).Should().Be(2);
    }

    // ---- the route maps each outcome ------------------------------------------------------------------------------

    [Theory]
    [InlineData(200, "ok")]
    [InlineData(404, "notfound")]
    [InlineData(409, "notareply")]
    [InlineData(409, "mismatch")]
    [InlineData(403, "denied")]
    [InlineData(400, "invalid")]
    public async Task The_route_maps_outcomes_to_statuses(int expected, string scenario)
    {
        var user = scenario switch
        {
            "notfound" => new FakeUser { ReplyGetStatus = 404 },
            "notareply" => Open(reply: Json.Reply(direction: 100000001)),
            "mismatch" => Open(reply: Json.Reply(toInquiry: Guid.NewGuid())),
            "denied" => Open(),
            _ => Open(),
        };
        if (scenario == "denied")
            user.InquiryPatchStatuses.Enqueue(403);
        if (scenario == "notfound")
            user.RowJson.Enqueue(Json.Row());
        var disposition = scenario == "invalid" ? 5 : 100000001;

        var result = await InquiryEndpoints.RecordDispositionAsync(
            Inquiry, new RecordDispositionRequest(Reply, disposition), Sut(user), new DefaultHttpContext(), default);

        ((IStatusCodeHttpResult)result).StatusCode.Should().Be(expected);
    }

    // ---- criterion 5 (negative): two vocabularies, two columns ----------------------------------------------------

    [Fact]
    public async Task Disposition_and_decision_outcome_stay_distinct()
    {
        Enum.GetNames<InquiryDisposition>().Should().BeEquivalentTo("WriteOff", "BudgetRevised", "ScopeApproved", "NoAction");
        Enum.GetNames<InquiryDisposition>().Should().NotContain(["Authorized", "Denied", "Dismissed"]);

        var user = Open();
        await Sut(user).RecordDispositionAsync(Inquiry, Reply, 100000002, default);
        user.Patches.Select(p => p.Body).Should().NotContain(b => b.Contains("decisionoutcome") || b.Contains("decisionrecord"));
        InquiryDispositionService.BuildFetch(["disposition"], Matter, null).Should().NotContain("decision");
    }

    // ---- criterion 2: per matter ----------------------------------------------------------------------------------

    [Fact]
    public async Task Per_matter_returns_every_resolved_inquiry_by_disposition()
    {
        var user = new FakeUser { TallyJson = "{\"value\":[{\"disposition\":100000000,\"n\":2},{\"disposition\":100000001,\"n\":1},{\"disposition\":100000003,\"n\":4}]}" };

        var tally = await Sut(user).GetByMatterAsync(Matter, default);

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
        var user = new FakeUser { TallyJson = json };

        var byFirm = await Sut(user).GetByOutsideFirmAsync(null, default);

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
        var user = new FakeUser();

        (await Sut(user).GetByOutsideFirmAsync(FirmA, default)).Should().BeEmpty();

        user.Gets.Single().Should().Contain($"attribute=\"sprk_regardingorganization\" operator=\"eq\" value=\"{FirmA:D}\"")
            .And.NotContain("sprk_regardingmatter");
    }

    [Fact]
    public async Task A_refused_read_fails_loudly_never_as_an_empty_tally()
    {
        var act = () => Sut(new FakeUser { TallyStatus = 403 }).GetByMatterAsync(Matter, default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---- criterion 4 / H-7: action rate reads sprk_resolutiontype, not Decision Records ---------------------------

    private static PolicyActionRateService Rates(FakeUser user) => new(user, Now, NullLogger<PolicyActionRateService>.Instance);

    [Fact]
    public async Task Do_lane_action_rate_comes_from_the_signal_resolution_type_and_counts_only_acted_and_dismissed()
    {
        // Do lane, version A: 6 Acted + 2 Dismissed. None has a Decision Record (a Do action runs with no gate), so a
        // Decision-Record-based rate would be 0 of 8. Decide lane POL-BUDGET: 1 Acted + 4 Dismissed.
        var json = "{\"value\":["
            + $"{{\"policy\":\"POL-OVERDUE\",\"version\":\"{VersionA}\",\"lane\":100000001,\"resolution\":100000000,\"n\":6}},"
            + $"{{\"policy\":\"POL-OVERDUE\",\"version\":\"{VersionA}\",\"lane\":100000001,\"resolution\":100000001,\"n\":2}},"
            + "{\"policy\":\"POL-BUDGET\",\"lane\":100000000,\"resolution\":100000000,\"n\":1},"
            + "{\"policy\":\"POL-BUDGET\",\"lane\":100000000,\"resolution\":100000001,\"n\":4}]}";
        var user = new FakeUser { TallyJson = json };

        var rates = await Rates(user).GetAsync(default);

        var overdue = rates.Single(r => r.PolicyCode == "POL-OVERDUE");
        overdue.Lane.Should().Be(SignalLane.Do);
        overdue.PolicyVersionId.Should().Be(VersionA);
        (overdue.Acted, overdue.Dismissed).Should().Be((6, 2));
        overdue.Rate.Should().BeApproximately(0.75, 1e-9);

        var budget = rates.Single(r => r.PolicyCode == "POL-BUDGET");
        budget.Lane.Should().Be(SignalLane.Decide);
        budget.PolicyVersionId.Should().BeNull();
        budget.Judged.Should().Be(5);
        budget.Rate.Should().BeApproximately(0.2, 1e-9);

        var q = user.Gets.Single();
        q.Should().StartWith("sprk_signals?fetchXml=");
        q.Should().Contain("attribute name=\"sprk_resolutiontype\"").And.Contain("attribute name=\"sprk_policyversion\"");
        q.Should().NotContain("decisionrecord").And.NotContain("sprk_decisionoutcome");
    }

    [Fact]
    public void The_window_is_the_last_90_days_of_resolution_and_only_acted_and_dismissed_count()
    {
        var q = PolicyActionRateService.BuildFetch(Now.GetUtcNow().UtcDateTime.AddDays(-PolicyActionRateService.WindowDays));

        q.Should().Contain("attribute=\"sprk_resolvedon\" operator=\"ge\" value=\"2026-07-11T12:00:00Z\"");
        q.Should().Contain("<value>100000000</value><value>100000001</value>");
        q.Should().NotContain("100000002").And.NotContain("100000003").And.NotContain("100000004");
        PolicyActionRateService.WindowDays.Should().Be(90);
    }

    [Theory]
    [InlineData(4, 0, true)]
    [InlineData(2, 2, true)]
    [InlineData(5, 0, false)]
    [InlineData(3, 2, false)]
    public void Below_five_judged_Signals_is_too_few_to_judge(int acted, int dismissed, bool tooFew)
    {
        var rate = new PolicyActionRate("P", null, SignalLane.Do, acted, dismissed);

        rate.TooFewToJudge.Should().Be(tooFew);
        (rate.Rate is null).Should().Be(tooFew);
        PolicyActionRateService.MinimumJudged.Should().Be(5);
        PolicyActionRateService.Acted.Should().Be(100000000);
        PolicyActionRateService.Dismissed.Should().Be(100000001);
    }

    [Fact]
    public async Task A_signal_group_with_no_lane_is_skipped_not_thrown()
    {
        var json = "{\"value\":[{\"policy\":\"POL-X\",\"resolution\":100000000,\"n\":3},"
            + "{\"policy\":\"POL-OK\",\"lane\":100000001,\"resolution\":100000000,\"n\":5}]}";

        var rates = await Rates(new FakeUser { TallyJson = json }).GetAsync(default);

        rates.Should().ContainSingle().Which.PolicyCode.Should().Be("POL-OK");
    }
}
