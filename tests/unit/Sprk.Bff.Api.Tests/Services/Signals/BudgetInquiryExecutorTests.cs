using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Communication.Channels;
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
    private static readonly Guid MatterType = Guid.NewGuid();

    private sealed class FakeUser(bool canCreate, string matterRights, Guid? noAppendToOn = null, bool matterTypeCataloged = true) : IDataverseUserClient
    {
        public List<(string Path, string Body)> Posts { get; } = [];
        /// <summary>Every GET the core made, so a test can prove which records were asked about as the caller.</summary>
        public List<string> Gets { get; } = [];

        private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

        public Task<DataverseUserResponse> GetAsync(string p, CancellationToken ct)
        {
            Gets.Add(p);
            if (p.StartsWith("WhoAmI"))
                return Ok($"{{\"UserId\":\"{Me}\"}}");
            // uac-r2 #1612: a service request is a core table with no pair agreement column, so its ADR-024 pair must be TYPED.
            // The catalog is organization-owned (no RetrievePrincipalAccess; the AppendTo privilege plus a readable row instead).
            if (p.StartsWith("sprk_recordtype_refs?"))
                return Ok(matterTypeCataloged ? $"{{\"value\":[{{\"sprk_recordtype_refid\":\"{MatterType}\"}}]}}" : "{\"value\":[]}");
            if (p.StartsWith($"sprk_recordtype_refs({MatterType:D})"))
                return Ok("{\"sprk_recordlogicalname\":\"sprk_matter\"}");
            if (p.StartsWith("EntityDefinitions(LogicalName='sprk_matter')?"))
                return Ok("{\"EntitySetName\":\"sprk_matters\"}");
            if (p.StartsWith("EntityDefinitions(LogicalName='sprk_recordtype_ref')?"))
                return Ok("{\"LogicalName\":\"sprk_recordtype_ref\",\"OwnershipType\":\"OrganizationOwned\",\"PrimaryIdAttribute\":\"sprk_recordtype_refid\",\"Privileges\":[{\"PrivilegeType\":\"AppendTo\",\"Name\":\"prvAppendTosprk_recordtype_ref\"}]}");
            if (p.StartsWith("sprk_recordtype_refs(") && p.Contains("?$select=sprk_recordtype_refid"))
                return Ok("{}");
            if (p.Contains("RetrievePrincipalAccess") && p.Contains("sprk_recordtype_refs"))
                return Task.FromResult(DataverseUserResponse.Fail(400, "0x80040800", "RetrievePrincipalAccess does not support sprk_recordtype_ref"));
            if (p.StartsWith("EntityDefinitions(LogicalName='sprk_servicerequest')?"))
                return Ok("{\"Privileges\":[{\"PrivilegeType\":\"Create\",\"Name\":\"prvCreatesprk_servicerequest\"},{\"PrivilegeType\":\"Append\",\"Name\":\"prvAppendsprk_servicerequest\"}]}");
            if (p.Contains("RetrieveUserSetOfPrivilegesByNames"))
                return Ok(canCreate
                    ? "{\"RolePrivileges\":[{\"PrivilegeName\":\"prvCreatesprk_servicerequest\"},{\"PrivilegeName\":\"prvAppendsprk_servicerequest\"},{\"PrivilegeName\":\"prvAppendTosprk_recordtype_ref\"}]}"
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

        /// <summary>The communication core's archival warning this send reports (D-216), or null when the .eml was archived.</summary>
        public string? ArchivalWarning { get; init; }

        /// <summary>Task 191: failures thrown by the next sends, in order, before <see cref="Throw"/> and <c>fail</c> apply.</summary>
        public Queue<Exception> Failures { get; } = new();

        public List<Guid> ServiceRequestsSeen { get; } = [];

        public override Task<InquirySendResult> SendAsync(BudgetInquiryRequest request, Guid serviceRequestId, CancellationToken ct)
        {
            TokenSeen = ct;
            ServiceRequestsSeen.Add(serviceRequestId);
            if (Failures.TryDequeue(out var next))
                throw next;
            if (Throw is not null)
                throw Throw;
            Sends++;
            ServiceRequestSeen = serviceRequestId;
            return fail ? throw new InvalidOperationException("graph down") : Task.FromResult(new InquirySendResult(Guid.Parse("11111111-1111-1111-1111-111111111111"), ArchivalWarning));
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

    private static (BudgetInquiryExecutor Sut, Rig Rig) Build(
        FakeUser user, FakeSender sender, bool secure = false, Microsoft.Extensions.Logging.ILogger<BudgetInquiryExecutor>? logger = null)
    {
        var rig = new Rig(secure);
        // Field mapping task 017 (2d-3): the executor creates through the ONE server create, resolved from the request's
        // services (as DecisionRouteCores does); the rig's resolver and app-only writer are the service's.
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services,
            Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership.RouteRecordCreation.Default(rig.Ownership.Object, rig.AppOnly.Object));
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            RequestServices = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services),
        };
        var sut = new BudgetInquiryExecutor(user, rig.Ownership.Object, sender,
            new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = http }, logger ?? NullLogger<BudgetInquiryExecutor>.Instance);
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
    public async Task Pair_is_typed_and_the_pairs_record_is_checked_as_the_caller()
    {
        // Regression (uac-r2 #1612): an untyped pair on sprk_servicerequest is refused 400 Invalid (the table has no pair
        // agreement column), so every real inquiry was refused. The pair must name the matter by TYPE, and the core then
        // reads the type row and asks AppendTo on the matter AS THE CALLER.
        var user = new FakeUser(true, "ReadAccess,AppendToAccess");
        var (sut, rig) = Build(user, new FakeSender());

        var result = await sut.ExecuteAsync(Req(), default);

        result.Outcome.Should().Be(BudgetInquiryOutcome.Sent);
        var fields = rig.Writes.Single().Fields;
        ((JsonElement)fields["sprk_RegardingRecordType@odata.bind"]!).GetString().Should().Be($"/sprk_recordtype_refs({MatterType:D})");
        ((JsonElement)fields["sprk_regardingrecordid"]!).GetString().Should().Be(Matter.ToString("D"));
        user.Gets.Should().Contain(g => g.StartsWith($"sprk_recordtype_refs({MatterType:D})"), "the pair's type row is read as the caller");
        user.Gets.Should().Contain(g => g.Contains("RetrievePrincipalAccess") && g.Contains(Matter.ToString("D")),
            "AppendTo is asked on the record the pair names");
    }

    [Fact]
    public async Task Matter_record_type_not_cataloged_refuses_before_any_write_or_send()
    {
        var sender = new FakeSender();
        var (sut, rig) = Build(new FakeUser(true, "AppendToAccess", matterTypeCataloged: false), sender);

        (await sut.ExecuteAsync(Req(), default)).Outcome.Should().Be(BudgetInquiryOutcome.Refused);
        (await sut.PreflightAsync(Req(), default))!.Outcome.Should().Be(BudgetInquiryOutcome.Refused);

        rig.Writes.Should().BeEmpty();
        sender.Sends.Should().Be(0);
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

        // Task 191 (D-223): a failure that cannot be placed before the send may have delivered the email.
        result.Outcome.Should().Be(BudgetInquiryOutcome.SendOutcomeUnknown);
        result.ServiceRequestId.Should().Be(rig.Writes.Single().Id);
        result.Succeeded.Should().BeFalse();
    }

    /// <summary>Captures what the executor logs (level and rendered text).</summary>
    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<BudgetInquiryExecutor>
    {
        public List<(Microsoft.Extensions.Logging.LogLevel Level, string Text)> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) => Lines.Add((logLevel, formatter(state, exception)));
    }

    private const string ArchiveFailed = "Email sent successfully but archival failed: container could not be resolved for tenant-secret-text";

    [Fact]
    public async Task Sent_but_not_archived_carries_the_warning_and_logs_a_warning_with_ids_only_task176()
    {
        // D-216: the email is archived as .eml. When the archive fails (for example the matter's container cannot be resolved) the
        // send stands, but the result carries the core's warning and the log names the inquiry and the communication - never the text.
        var logger = new CapturingLogger();
        var (sut, rig) = Build(new FakeUser(true, "AppendToAccess"), new FakeSender { ArchivalWarning = ArchiveFailed }, logger: logger);

        var result = await sut.ExecuteAsync(Req(), default);

        result.Outcome.Should().Be(BudgetInquiryOutcome.Sent);
        result.ArchivalWarning.Should().Be(ArchiveFailed);
        var line = logger.Lines.Should().ContainSingle(l => l.Level == Microsoft.Extensions.Logging.LogLevel.Warning).Subject.Text;
        line.Should().Contain(rig.Writes.Single().Id.ToString()).And.Contain("11111111-1111-1111-1111-111111111111");
        line.Should().NotContain("tenant-secret-text", "ids only: the warning text can carry an exception message");
    }

    [Fact]
    public async Task Sent_and_archived_has_no_warning_and_logs_none_task176()
    {
        var logger = new CapturingLogger();
        var (sut, _) = Build(new FakeUser(true, "AppendToAccess"), new FakeSender(), logger: logger);

        var result = await sut.ExecuteAsync(Req(), default);

        result.Outcome.Should().Be(BudgetInquiryOutcome.Sent);
        result.ArchivalWarning.Should().BeNull();
        logger.Lines.Should().NotContain(l => l.Level >= Microsoft.Extensions.Logging.LogLevel.Warning);
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

    // ---- task 190 (D-222): Cc and Bcc end to end ---------------------------------------------------------------------------

    private static BudgetInquiryRequest CopyReq(string[]? cc, string[]? bcc, string[]? to = null) =>
        new(Matter, to ?? ["firm@example.com"], "Budget inquiry", "Please explain the overage.", true, null, cc, bcc);

    [Fact]
    public void Build_sets_To_Cc_and_Bcc_trimmed_and_deduplicated_and_changes_nothing_else_task190()
    {
        var sr = Guid.NewGuid();
        var withCopies = InquiryEmailSender.Build(
            CopyReq([" c1@example.com ", "C1@Example.com", "c2@example.com"], ["b1@example.com ", "B1@example.com"]), sr);
        var toOnly = InquiryEmailSender.Build(Req(), sr);

        withCopies.To.Should().Equal("firm@example.com");
        withCopies.Cc.Should().Equal("c1@example.com", "c2@example.com");
        withCopies.Bcc.Should().Equal("b1@example.com");
        // Everything else is the To-only request: associations, internal-only, archive, private thread, send mode, body.
        withCopies.Associations.Should().BeEquivalentTo(toOnly.Associations);
        withCopies.IsInternalOnly.Should().Be(toOnly.IsInternalOnly).And.BeTrue();
        withCopies.ArchiveToSpe.Should().Be(toOnly.ArchiveToSpe).And.BeTrue();
        withCopies.CreateThreadPrivate.Should().Be(toOnly.CreateThreadPrivate).And.BeTrue();
        withCopies.SendMode.Should().Be(SendMode.User);
        withCopies.Body.Should().Be(toOnly.Body);
        withCopies.Subject.Should().Be(toOnly.Subject);
        withCopies.BodyFormat.Should().Be(toOnly.BodyFormat);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(new string[0], new string[0])]
    [InlineData(new[] { " " }, new[] { "" })]
    public void Build_with_empty_or_absent_Cc_and_Bcc_is_the_To_only_request_task190(string[]? cc, string[]? bcc)
    {
        var built = InquiryEmailSender.Build(CopyReq(cc, bcc), Guid.NewGuid());

        built.Cc.Should().BeNull();
        built.Bcc.Should().BeNull();
        built.To.Should().Equal("firm@example.com");
    }

    [Fact]
    public void Build_sends_an_address_once_in_the_first_line_it_is_on_task190()
    {
        var built = InquiryEmailSender.Build(
            CopyReq(["FIRM@example.com", "c@example.com"], ["firm@example.com", "C@example.com", "b@example.com"]), Guid.NewGuid());

        built.To.Should().Equal("firm@example.com");
        built.Cc.Should().Equal(new[] { "c@example.com" }, "an address in To is sent once, in To");
        built.Bcc.Should().Equal(new[] { "b@example.com" }, "an address in To or Cc is sent once, in To or Cc");
    }

    [Fact]
    public void Build_never_puts_Bcc_in_To_Cc_subject_body_footer_or_link_lines_task190()
    {
        const string hidden = "hidden-recipient@example.com";
        var built = InquiryEmailSender.Build(
            CopyReq(["c@example.com"], [hidden]), Guid.NewGuid(),
            new InquiryEmailExtras("Acme v. Roe (MAT-000123)", ["Open this matter in Spaarke: https://org.example/record"]));

        built.Bcc.Should().Equal(hidden);
        built.To.Should().NotContain(hidden);
        built.Cc.Should().NotContain(hidden);
        built.Subject.Should().NotContain(hidden);
        built.Body.Should().NotContain(hidden);
        built.Associations!.Select(a => a.EntityName).Should().NotContain(n => n != null && n.Contains(hidden));
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
    public async Task A_cancelled_send_after_the_row_exists_is_SendOutcomeUnknown_with_the_id()
    {
        var (sut, rig) = Build(new FakeUser(true, "AppendToAccess"), new FakeSender { Throw = new OperationCanceledException() });

        var result = await sut.ExecuteAsync(Req(), default);

        // Task 191 (D-223): a timeout or cancellation during the send may have delivered the email: the person chooses.
        result.Outcome.Should().Be(BudgetInquiryOutcome.SendOutcomeUnknown);
        result.ServiceRequestId.Should().Be(rig.Writes.Single().Id);
    }

    // ---- task 191 (D-223): NotSent vs Unknown, and the send-only retry ----------------------------------------------

    private static SdapProblemException GraphSendFailed(int status, string? sendOutcome) =>
        new("GRAPH_SEND_FAILED", "Email send failed", "detail", status,
            sendOutcome is null ? null : new Dictionary<string, object> { [EmailChannelSender.SendOutcomeExtension] = sendOutcome });

    public static TheoryData<string, Exception, bool> SendFailures => new()
    {
        { "before the core", new InquiryEmailNotSentException(new InvalidOperationException("no request")), true },
        { "Graph refused (NotSent)", GraphSendFailed(500, EmailChannelSender.SendOutcomeNotSent), true },
        { "Graph 5xx/408 (Unknown)", GraphSendFailed(500, EmailChannelSender.SendOutcomeUnknown), false },
        { "core catch-all (no outcome)", GraphSendFailed(500, null), false },
        { "core catch-all 4xx (no outcome)", GraphSendFailed(400, null), false },
        { "core refusal 4xx", new SdapProblemException("INVALID_RECIPIENT", "bad", statusCode: 400), true },
        { "core fault 5xx", new SdapProblemException("ARCHIVE_FAILED", "x", statusCode: 500), false },
        { "transport", new HttpRequestException("reset"), false },
        { "timeout", new TaskCanceledException("timeout"), false },
        { "anything else", new InvalidOperationException("graph down"), false },
    };

    [Theory]
    [MemberData(nameof(SendFailures))]
    public void Only_a_send_that_provably_did_not_go_out_is_DefinitelyNotSent(string because, Exception failure, bool notSent) =>
        BudgetInquiryExecutor.DefinitelyNotSent(failure).Should().Be(notSent, because);

    [Fact]
    public async Task A_send_Graph_refused_after_the_row_exists_is_SendFailed_with_the_id()
    {
        var sender = new FakeSender { Throw = GraphSendFailed(500, EmailChannelSender.SendOutcomeNotSent) };
        var (sut, rig) = Build(new FakeUser(true, "AppendToAccess"), sender);

        var result = await sut.ExecuteAsync(Req(), default);

        result.Outcome.Should().Be(BudgetInquiryOutcome.SendFailed);
        result.ServiceRequestId.Should().Be(rig.Writes.Single().Id);
    }

    [Fact]
    public async Task A_send_that_fails_before_the_communication_core_is_marked_not_sent()
    {
        var sender = new InquiryEmailSender(null!, new Microsoft.AspNetCore.Http.HttpContextAccessor());

        var act = () => sender.SendAsync(Req(), Guid.NewGuid(), default);

        (await act.Should().ThrowAsync<InquiryEmailNotSentException>()).Which.InnerException.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task SendOnly_sends_once_with_the_landed_service_request_and_creates_nothing()
    {
        var landed = Guid.NewGuid();
        var sender = new FakeSender();
        var (sut, rig) = Build(new FakeUser(true, "AppendToAccess"), sender);

        var result = await sut.SendOnlyAsync(Req(), landed, default);

        result.Outcome.Should().Be(BudgetInquiryOutcome.Sent);
        result.ServiceRequestId.Should().Be(landed);
        rig.Writes.Should().BeEmpty("the service request already exists");
        sender.ServiceRequestsSeen.Should().Equal(landed);
        InquiryEmailSender.Build(Req(), landed).Associations![0].EntityId.Should().Be(landed, "the email links to the landed row");
    }

    [Fact]
    public async Task SendOnly_without_a_landed_id_is_Invalid_and_sends_nothing()
    {
        var sender = new FakeSender();
        var (sut, rig) = Build(new FakeUser(true, "AppendToAccess"), sender);

        var result = await sut.SendOnlyAsync(Req(), Guid.Empty, default);

        result.Outcome.Should().Be(BudgetInquiryOutcome.Invalid);
        sender.ServiceRequestsSeen.Should().BeEmpty();
        rig.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_retry_through_the_action_executor_reuses_the_landed_service_request_and_creates_exactly_one(bool definitelyNotSent)
    {
        var sender = new FakeSender();
        sender.Failures.Enqueue(definitelyNotSent
            ? GraphSendFailed(500, EmailChannelSender.SendOutcomeNotSent)
            : GraphSendFailed(500, EmailChannelSender.SendOutcomeUnknown));
        var (sut, rig) = Build(new FakeUser(true, "AppendToAccess"), sender);
        var action = new BudgetInquiryActionExecutor(sut);
        var request = new DecisionActionRequest(new Microsoft.AspNetCore.Http.DefaultHttpContext(), Guid.NewGuid(), null, null, Matter,
            new Dictionary<string, string?> { ["to"] = "firm@example.com", ["subject"] = "Budget inquiry", ["body"] = "Please explain." });

        var first = await action.ExecuteAsync(request, default);

        first.Status.Should().Be(DecisionActionStatus.Failed);
        first.SendFailure.Should().Be(definitelyNotSent ? DecisionSendFailure.NotSent : DecisionSendFailure.Unknown);
        var serviceRequest = rig.Writes.Should().ContainSingle().Subject.Id;
        first.Written.Should().Equal(new DecisionRecordRef(BudgetInquiryExecutor.Table, serviceRequest));

        var retried = await action.ExecuteAsync(request with { Landed = first.Written }, default);

        retried.Status.Should().Be(DecisionActionStatus.Done);
        rig.Writes.Should().ContainSingle("the retry never creates a second service request");
        sender.ServiceRequestsSeen.Should().Equal(serviceRequest, serviceRequest);
        retried.Written[0].Should().Be(new DecisionRecordRef(BudgetInquiryExecutor.Table, serviceRequest));
    }
}
