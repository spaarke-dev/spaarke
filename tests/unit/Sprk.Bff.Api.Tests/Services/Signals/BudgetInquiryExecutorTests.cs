using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Signals;

/// <summary>
/// Task 070 (the Inquiry executor). The Dataverse boundary is faked at <see cref="IDataverseUserClient"/> (module
/// boundary, ADR-038): the fake answers what Dataverse would answer for the simulated caller.
/// </summary>
/// <remarks>
/// ADR-038 pairing: the column names, entity name and option value asserted here (sprk_servicerequest, sprk_direction
/// Outbound 100000001, sprk_regardingmatter, no sprk_responseduedate) were checked against the real spaarkedev1 schema on
/// 2026-10-09 (Dataverse describe of sprk_servicerequest); the evidence is in notes/070-inquiry-executor.md.
/// </remarks>
public sealed class BudgetInquiryExecutorTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Matter = Guid.NewGuid();
    private static readonly Guid Team = Guid.NewGuid();

    private sealed class FakeUser(bool canCreate, string matterRights, Guid? noAppendToOn = null) : IDataverseUserClient
    {
        public List<(string Path, string Body)> Posts { get; } = [];

        private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

        public Task<DataverseUserResponse> GetAsync(string p, CancellationToken ct)
        {
            if (p.StartsWith("WhoAmI"))
                return Ok($"{{\"UserId\":\"{Me}\"}}");
            if (p.StartsWith("EntityDefinitions(LogicalName='sprk_servicerequest')?"))
                return Ok("{\"Privileges\":[{\"PrivilegeType\":\"Create\",\"Name\":\"prvCreatesprk_servicerequest\"},{\"PrivilegeType\":\"Append\",\"Name\":\"prvAppendsprk_servicerequest\"}]}");
            if (p.Contains("RetrieveUserSetOfPrivilegesByNames"))
                return Ok(canCreate
                    ? "{\"RolePrivileges\":[{\"PrivilegeName\":\"prvCreatesprk_servicerequest\"},{\"PrivilegeName\":\"prvAppendsprk_servicerequest\"}]}"
                    : "{\"RolePrivileges\":[]}");
            if (p.Contains("/Attributes?"))
                return Ok("{\"value\":[]}");
            if (p.Contains("RetrievePrincipalAccess") && noAppendToOn is { } denied && p.Contains(denied.ToString("D"), StringComparison.OrdinalIgnoreCase))
                return Ok("{\"AccessRights\":\"ReadAccess\"}");
            if (p.Contains("RetrievePrincipalAccess"))
                return Ok($"{{\"AccessRights\":\"{matterRights}\"}}");
            return Task.FromResult(DataverseUserResponse.Fail(404, "not-found", p));
        }

        public Task<DataverseUserResponse> PostAsync(string a, string j, CancellationToken ct) => PostAsync(a, j, false, ct);

        public Task<DataverseUserResponse> PostAsync(string a, string j, bool prefer, CancellationToken ct)
        {
            Posts.Add((a, j));
            return Ok("{}");
        }

        public Task<DataverseUserResponse> PatchAsync(string p, string j, CancellationToken ct) => throw new InvalidOperationException("no patch expected");
        public Task<DataverseUserResponse> DeleteAsync(string p, CancellationToken ct) => throw new InvalidOperationException("no delete expected");

        private static Task<DataverseUserResponse> Ok(string json) => Task.FromResult(DataverseUserResponse.Ok(200, J(json)));
    }

    private sealed class FakeSender(bool fail = false) : InquiryEmailSender(null!, null!)
    {
        public int Sends { get; private set; }
        public Guid? ServiceRequestSeen { get; private set; }
        public CancellationToken TokenSeen { get; private set; }
        public Exception? Throw { get; init; }

        public override Task<Guid?> SendAsync(BudgetInquiryRequest request, Guid serviceRequestId, CancellationToken ct)
        {
            TokenSeen = ct;
            if (Throw is not null)
                throw Throw;
            Sends++;
            ServiceRequestSeen = serviceRequestId;
            return fail ? throw new InvalidOperationException("graph down") : Task.FromResult<Guid?>(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        }
    }

    /// <summary>The application-side writer the G5 create uses once the caller's rights check passed.</summary>
    private sealed class Rig
    {
        public Mock<IFieldMappingDataverseService> AppOnly { get; } = new();
        public Mock<IRecordOwnershipResolver> Ownership { get; } = new();
        public List<(string Table, Guid Id, Dictionary<string, object?> Fields)> Writes { get; } = [];

        public Rig(bool secure = false)
        {
            Ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(secure ? RecordOwnerResolution.Owned(Team) with { IsSecureOwner = true } : RecordOwnerResolution.Owned(Team));
            AppOnly.Setup(a => a.UpdateRecordFieldsAsync(It.IsAny<string>(), It.IsAny<Guid>(),
                    It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
                .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>(
                    (t, id, f, _, _) => Writes.Add((t, id, new Dictionary<string, object?>(f))))
                .Returns(Task.CompletedTask);
        }
    }

    private static (BudgetInquiryExecutor Sut, Rig Rig) Build(FakeUser user, FakeSender sender, bool secure = false)
    {
        var rig = new Rig(secure);
        var sut = new BudgetInquiryExecutor(user, rig.Ownership.Object, rig.AppOnly.Object, sender,
            new Microsoft.AspNetCore.Http.HttpContextAccessor(), NullLogger<BudgetInquiryExecutor>.Instance);
        return (sut, rig);
    }

    private static BudgetInquiryRequest Req(bool confirmed = true, Guid? firm = null) =>
        new(Matter, ["firm@example.com"], "Budget inquiry", "Please explain the overage.", confirmed, firm);

    [Fact]
    public async Task Confirmed_authorised_caller_creates_outbound_service_request_and_sends()
    {
        var user = new FakeUser(true, "ReadAccess,AppendToAccess");
        var sender = new FakeSender();
        var (sut, rig) = Build(user, sender);

        var result = await sut.ExecuteAsync(Req(), default);

        var write = rig.Writes.Should().ContainSingle().Subject;
        result.Outcome.Should().Be(BudgetInquiryOutcome.Sent);
        result.ServiceRequestId.Should().Be(write.Id);
        result.CommunicationId.Should().NotBeNull();
        sender.Sends.Should().Be(1);
        sender.ServiceRequestSeen.Should().Be(write.Id);

        write.Table.Should().Be("sprk_servicerequest");
        ((JsonElement)write.Fields["sprk_direction"]!).GetInt32().Should().Be(100000001);
        ((JsonElement)write.Fields["sprk_RegardingMatter@odata.bind"]!).GetString().Should().Be($"/sprk_matters({Matter:D})");
        write.Fields["ownerid@odata.bind"].Should().Be($"/teams({Team})");
        user.Posts.Should().BeEmpty("the row is written by the application after the caller's rights check, not by a run-as-user POST");
    }

    [Fact]
    public async Task Service_request_carries_no_response_due_date_and_no_disposition()
    {
        var (sut, rig) = Build(new FakeUser(true, "AppendToAccess"), new FakeSender());
        await sut.ExecuteAsync(Req(), default);

        var names = rig.Writes.Single().Fields.Keys.Select(k => k.ToLowerInvariant()).ToList();
        names.Should().NotContain(n => n.Contains("responseduedate") || n.Contains("disposition"));
    }

    [Fact]
    public async Task Unconfirmed_creates_nothing_and_sends_nothing()
    {
        var sender = new FakeSender();
        var (sut, rig) = Build(new FakeUser(true, "AppendToAccess"), sender);

        var result = await sut.ExecuteAsync(Req(confirmed: false), default);

        result.Outcome.Should().Be(BudgetInquiryOutcome.NotConfirmed);
        rig.Writes.Should().BeEmpty();
        sender.Sends.Should().Be(0);
    }

    [Fact]
    public async Task Caller_without_Create_is_refused_before_any_write_or_send()
    {
        var sender = new FakeSender();
        var (sut, rig) = Build(new FakeUser(false, "AppendToAccess"), sender);

        var result = await sut.ExecuteAsync(Req(), default);

        result.Outcome.Should().Be(BudgetInquiryOutcome.Refused);
        result.ServiceRequestId.Should().BeNull();
        rig.Writes.Should().BeEmpty();
        sender.Sends.Should().Be(0);
    }

    [Fact]
    public async Task Caller_without_AppendTo_on_the_matter_is_refused_before_any_write_or_send()
    {
        var sender = new FakeSender();
        var (sut, rig) = Build(new FakeUser(true, "ReadAccess,WriteAccess"), sender);

        var result = await sut.ExecuteAsync(Req(), default);

        result.Outcome.Should().Be(BudgetInquiryOutcome.Refused);
        rig.Writes.Should().BeEmpty();
        sender.Sends.Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Malformed_request_writes_nothing(string subject)
    {
        var sender = new FakeSender();
        var (sut, rig) = Build(new FakeUser(true, "AppendToAccess"), sender);

        var result = await sut.ExecuteAsync(new BudgetInquiryRequest(Matter, ["a@b.com"], subject, "body", true), default);

        result.Outcome.Should().Be(BudgetInquiryOutcome.Invalid);
        rig.Writes.Should().BeEmpty();
        sender.Sends.Should().Be(0);
    }

    [Fact]
    public async Task Send_failure_after_create_reports_the_service_request_id()
    {
        var (sut, rig) = Build(new FakeUser(true, "AppendToAccess"), new FakeSender(fail: true));

        var result = await sut.ExecuteAsync(Req(), default);

        result.Outcome.Should().Be(BudgetInquiryOutcome.SendFailed);
        result.ServiceRequestId.Should().Be(rig.Writes.Single().Id);
        result.Succeeded.Should().BeFalse();
    }

    // ---- round 2 ----------------------------------------------------------------------------------------------

    [Fact]
    public void Email_links_to_the_service_request_as_the_primary_association()
    {
        var sr = Guid.NewGuid();
        var built = InquiryEmailSender.Build(Req(), sr);

        // CommunicationService maps only associations[0] to sprk_regarding*: it must be the inquiry (the matter stays second).
        built.Associations![0].EntityType.Should().Be("sprk_servicerequest");
        built.Associations![0].EntityId.Should().Be(sr);
        built.Associations![1].EntityType.Should().Be("sprk_matter");
        built.Associations![1].EntityId.Should().Be(Matter);
    }

    [Fact]
    public void Email_is_plain_text_and_sent_as_the_signed_in_user()
    {
        var built = InquiryEmailSender.Build(Req(), Guid.NewGuid());

        built.BodyFormat.Should().Be(BodyFormat.PlainText);
        built.SendMode.Should().Be(SendMode.User);
        built.Body.Should().Be("Please explain the overage.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Email_without_a_real_recipient_is_refused(int blanks)
    {
        var to = blanks == 0 ? Array.Empty<string>() : new[] { "  " };
        var act = () => InquiryEmailSender.Build(new BudgetInquiryRequest(Matter, to, "s", "b", true), Guid.NewGuid());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Outside_firm_is_written_to_regardingorganization()
    {
        var firm = Guid.NewGuid();
        var (sut, rig) = Build(new FakeUser(true, "AppendToAccess"), new FakeSender());

        var result = await sut.ExecuteAsync(Req(firm: firm), default);

        result.Outcome.Should().Be(BudgetInquiryOutcome.Sent);
        ((JsonElement)rig.Writes.Single().Fields["sprk_RegardingOrganization@odata.bind"]!).GetString()
            .Should().Be($"/sprk_organizations({firm:D})");
    }

    [Fact]
    public async Task No_firm_means_no_organization_lookup()
    {
        var (sut, rig) = Build(new FakeUser(true, "AppendToAccess"), new FakeSender());
        await sut.ExecuteAsync(Req(), default);

        rig.Writes.Single().Fields.Keys.Should().NotContain(k => k.Contains("RegardingOrganization", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Caller_without_AppendTo_on_the_firm_is_refused_before_any_write()
    {
        var firm = Guid.NewGuid();
        var sender = new FakeSender();
        var (sut, rig) = Build(new FakeUser(true, "AppendToAccess", noAppendToOn: firm), sender);

        var result = await sut.ExecuteAsync(Req(firm: firm), default);

        result.Outcome.Should().Be(BudgetInquiryOutcome.Refused);
        rig.Writes.Should().BeEmpty();
        sender.Sends.Should().Be(0);
    }

    [Fact]
    public async Task Preflight_clears_an_allowed_inquiry_without_writing()
    {
        var sender = new FakeSender();
        var (sut, rig) = Build(new FakeUser(true, "AppendToAccess"), sender);

        (await sut.PreflightAsync(Req(), default)).Should().BeNull();
        rig.Writes.Should().BeEmpty();
        sender.Sends.Should().Be(0);
    }

    [Fact]
    public async Task Preflight_refuses_a_caller_without_Create_with_the_outcome_Execute_gives()
    {
        var (sut, rig) = Build(new FakeUser(false, "AppendToAccess"), new FakeSender());

        var pre = await sut.PreflightAsync(Req(), default);
        var exec = await sut.ExecuteAsync(Req(), default);

        pre!.Outcome.Should().Be(BudgetInquiryOutcome.Refused).And.Be(exec.Outcome);
        rig.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Preflight_refuses_without_AppendTo_on_the_matter_and_on_the_firm()
    {
        var firm = Guid.NewGuid();
        (await Build(new FakeUser(true, "ReadAccess"), new FakeSender()).Sut.PreflightAsync(Req(), default))!
            .Outcome.Should().Be(BudgetInquiryOutcome.Refused);
        (await Build(new FakeUser(true, "AppendToAccess", noAppendToOn: firm), new FakeSender()).Sut.PreflightAsync(Req(firm: firm), default))!
            .Outcome.Should().Be(BudgetInquiryOutcome.Refused);
    }

    [Fact]
    public async Task Secure_matter_is_refused_by_Preflight_and_by_Execute_with_nothing_written()
    {
        var sender = new FakeSender();
        var (sut, rig) = Build(new FakeUser(true, "AppendToAccess"), sender, secure: true);

        var pre = await sut.PreflightAsync(Req(), default);
        var exec = await sut.ExecuteAsync(Req(), default);

        pre!.Outcome.Should().Be(BudgetInquiryOutcome.Refused);
        exec.Outcome.Should().Be(pre.Outcome);
        rig.Writes.Should().BeEmpty();
        sender.Sends.Should().Be(0);
    }

    [Fact]
    public async Task Preflight_reports_unconfirmed_and_malformed_like_Execute()
    {
        var (sut, _) = Build(new FakeUser(true, "AppendToAccess"), new FakeSender());

        (await sut.PreflightAsync(Req(confirmed: false), default))!.Outcome.Should().Be(BudgetInquiryOutcome.NotConfirmed);
        (await sut.PreflightAsync(new BudgetInquiryRequest(Matter, [], "s", "b", true), default))!.Outcome
            .Should().Be(BudgetInquiryOutcome.Invalid);
    }

    [Fact]
    public async Task Cancellation_after_the_row_exists_still_returns_the_service_request_id()
    {
        using var cts = new CancellationTokenSource();
        var sender = new FakeSender();
        var (sut, rig) = Build(new FakeUser(true, "AppendToAccess"), sender);
        rig.AppOnly.Setup(a => a.UpdateRecordFieldsAsync(It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((t, id, f, _, _) =>
            {
                rig.Writes.Add((t, id, new Dictionary<string, object?>(f)));
                cts.Cancel();
            })
            .Returns(Task.CompletedTask);

        var result = await sut.ExecuteAsync(Req(), cts.Token);

        result.ServiceRequestId.Should().Be(rig.Writes.Single().Id);
        result.Outcome.Should().Be(BudgetInquiryOutcome.Sent);
        sender.TokenSeen.CanBeCanceled.Should().BeFalse("the send must not be cancellable once the row exists");
    }

    [Fact]
    public async Task A_cancelled_send_after_the_row_exists_is_SendFailed_with_the_id()
    {
        var (sut, rig) = Build(new FakeUser(true, "AppendToAccess"), new FakeSender { Throw = new OperationCanceledException() });

        var result = await sut.ExecuteAsync(Req(), default);

        result.Outcome.Should().Be(BudgetInquiryOutcome.SendFailed);
        result.ServiceRequestId.Should().Be(rig.Writes.Single().Id);
    }
}
