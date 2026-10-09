using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
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

    private sealed class FakeUser(bool canCreate, string matterRights) : IDataverseUserClient
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

        public override Task<Guid?> SendAsync(BudgetInquiryRequest request, Guid serviceRequestId, CancellationToken ct)
        {
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

        public Rig()
        {
            Ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(RecordOwnerResolution.Owned(Team));
            AppOnly.Setup(a => a.UpdateRecordFieldsAsync(It.IsAny<string>(), It.IsAny<Guid>(),
                    It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
                .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>(
                    (t, id, f, _, _) => Writes.Add((t, id, new Dictionary<string, object?>(f))))
                .Returns(Task.CompletedTask);
        }
    }

    private static (BudgetInquiryExecutor Sut, Rig Rig) Build(FakeUser user, FakeSender sender)
    {
        var rig = new Rig();
        var sut = new BudgetInquiryExecutor(user, rig.Ownership.Object, rig.AppOnly.Object, sender,
            new Microsoft.AspNetCore.Http.HttpContextAccessor(), NullLogger<BudgetInquiryExecutor>.Instance);
        return (sut, rig);
    }

    private static BudgetInquiryRequest Req(bool confirmed = true) =>
        new(Matter, ["firm@example.com"], "Budget inquiry", "Please explain the overage.", confirmed);

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
}
