using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Signals;

/// <summary>
/// Task 160 (D-179, D-181; owner 2026-10-23): the budget inquiry's composer hint and To pre-fill on the plan, the record link chosen
/// by recipient at send time, and the footer that names the record. The Dataverse boundaries are faked at
/// <see cref="IDataverseUserClient"/> (as the caller), <see cref="IFieldMappingDataverseService"/> (the BFF's own read) and
/// <see cref="IContactIdentityStore"/> (the A9 licensed-user read).
/// </summary>
public sealed class BudgetInquiryComposerAndLinkTests
{
    private const string Env = "https://spaarkedev1.crm.dynamics.com";
    private const string Portal = "https://portal.spaarke.test";
    private const string Key = "acme";

    private static readonly Guid Matter = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Firm = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid Contact1 = Guid.Parse("66666666-6666-6666-6666-666666666661");
    private static readonly Guid Contact2 = Guid.Parse("66666666-6666-6666-6666-666666666662");
    private static readonly Guid UserId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    // =================================================================================================
    // Composer hint (D-179)
    // =================================================================================================

    [Theory]
    [InlineData("send-budget-inquiry")]
    [InlineData("send-reminder")]
    [InlineData("send-email")]
    public void The_three_message_actions_declare_the_email_composer(string code)
    {
        var action = DecisionActionCatalog.All.Single(a => a.Code == code);

        action.Composer.Should().Be(DecisionActionCatalog.EmailComposer);
        DecisionActionDto.From(action, isRecommended: false).Composer.Should().Be("email");
    }

    [Fact]
    public void No_other_action_declares_a_composer()
    {
        DecisionActionCatalog.All
            .Where(a => a.Code is not ("send-budget-inquiry" or "send-reminder" or "send-email"))
            .Should().OnlyContain(a => a.Composer == null);
    }

    // =================================================================================================
    // To pre-fill (D-179): the matter's outside firm's primary contact, read AS THE CALLER
    // =================================================================================================

    [Fact]
    public async Task Prefill_is_the_outside_firms_primary_contact_with_an_email_address()
    {
        var users = new Users()
            .On($"sprk_matters({Matter:D})", new { _sprk_assignedlawfirm1_value = Firm.ToString("D") })
            .On($"sprk_organizations({Firm:D})", new { _sprk_primarycontact1_value = Contact1.ToString("D"), _sprk_primarycontact2_value = Contact2.ToString("D") })
            .On($"contacts({Contact1:D})", new { emailaddress1 = (string?)null, fullname = "No Mail" })
            .On($"contacts({Contact2:D})", new { emailaddress1 = "lead@firm.example", fullname = "Lead Counsel" });

        var recipient = await Access(users).ReadInquiryRecipientAsync(InquiryPlan(), Allowed(), CancellationToken.None);

        recipient.Should().Be(new OutsideFirmContact(Contact2, "Lead Counsel", "lead@firm.example"));
        users.Gets.Should().OnlyContain(p => p.StartsWith("sprk_matters(") || p.StartsWith("sprk_organizations(") || p.StartsWith("contacts("),
            "every read is the caller's own, through the caller's client");
    }

    [Fact]
    public async Task Prefill_is_none_when_the_matter_has_no_outside_firm()
    {
        var users = new Users().On($"sprk_matters({Matter:D})", new { _sprk_assignedlawfirm1_value = (string?)null });

        (await Access(users).ReadInquiryRecipientAsync(InquiryPlan(), Allowed(), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Prefill_is_never_returned_when_the_caller_cannot_read_the_contact()
    {
        var users = new Users()
            .On($"sprk_matters({Matter:D})", new { _sprk_assignedlawfirm1_value = Firm.ToString("D") })
            .On($"sprk_organizations({Firm:D})", new { _sprk_primarycontact1_value = Contact1.ToString("D") })
            .Deny($"contacts({Contact1:D})");

        (await Access(users).ReadInquiryRecipientAsync(InquiryPlan(), Allowed(), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Prefill_is_never_returned_when_the_caller_cannot_read_the_firm()
    {
        var users = new Users()
            .On($"sprk_matters({Matter:D})", new { _sprk_assignedlawfirm1_value = Firm.ToString("D") })
            .Deny($"sprk_organizations({Firm:D})")
            .On($"contacts({Contact1:D})", new { emailaddress1 = "lead@firm.example", fullname = "Lead" });

        (await Access(users).ReadInquiryRecipientAsync(InquiryPlan(), Allowed(), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Prefill_reads_nothing_for_a_plan_without_the_inquiry_or_a_core_that_is_not_a_matter()
    {
        var users = new Users();

        (await Access(users).ReadInquiryRecipientAsync(Plan("revise-budget"), Allowed(), CancellationToken.None)).Should().BeNull();
        (await Access(users).ReadInquiryRecipientAsync(InquiryPlan(), Allowed() with { CoreTable = "sprk_project" }, CancellationToken.None))
            .Should().BeNull();
        users.Gets.Should().BeEmpty("must-not-fire: only the inquiry on a matter reads a pre-fill");
    }

    [Fact]
    public void The_plan_dto_carries_the_prefill_on_the_inquiry_only()
    {
        var plan = Plan("send-budget-inquiry", "revise-budget");
        var response = DecisionPlanResponse.From(Guid.NewGuid(), DecisionLane.Decide, Guid.NewGuid(), plan,
            inquiryRecipient: new OutsideFirmContact(Contact2, "Lead Counsel", "lead@firm.example"));

        response.Actions[0].PrefillTo.Should().ContainSingle().Which.Should().Be(new DecisionRecipientDto(Contact2, "Lead Counsel", "lead@firm.example"));
        response.Actions[1].PrefillTo.Should().BeNull();
        DecisionPlanResponse.From(Guid.NewGuid(), DecisionLane.Decide, Guid.NewGuid(), plan).Actions[0].PrefillTo.Should().BeNull();
    }

    // =================================================================================================
    // The portal link builder (D-181): the token overload, and the enum overload unchanged (must-not-fire)
    // =================================================================================================

    [Theory]
    [InlineData(ExternalGrantRootType.Project, "project")]
    [InlineData(ExternalGrantRootType.Matter, "matter")]
    [InlineData(ExternalGrantRootType.WorkAssignment, "workassignment")]
    public void The_enum_overload_returns_the_same_link_as_before(ExternalGrantRootType type, string token)
    {
        var id = Guid.NewGuid();

        GrantAccessNotifier.BuildPortalLink(Portal + "/", Key, type, id).Should().Be($"{Portal}/?customer={Key}&record={token}:{id:D}");
        GrantAccessNotifier.BuildPortalLink(Portal, Key, token, id).Should().Be($"{Portal}/?customer={Key}&record={token}:{id:D}");
    }

    [Theory]
    [InlineData("event")]
    [InlineData("Matter")]
    [InlineData("matter:x")]
    [InlineData("")]
    public void The_token_overload_refuses_a_token_the_portal_does_not_parse(string token)
    {
        GrantAccessNotifier.BuildPortalLink(Portal, Key, token, Guid.NewGuid()).Should().BeNull();
    }

    [Fact]
    public void The_token_overload_applies_the_same_url_and_key_rules()
    {
        GrantAccessNotifier.BuildPortalLink("http://portal.test", Key, "matter", Guid.NewGuid()).Should().BeNull();
        GrantAccessNotifier.BuildPortalLink(Portal, " ", "matter", Guid.NewGuid()).Should().BeNull();
        GrantAccessNotifier.BuildPortalLink(Portal, Key, "matter", Guid.Empty).Should().BeNull();
    }

    // =================================================================================================
    // The link by recipient (D-181; owner 2026-10-23: an ENABLED systemuser gets the app link, sprk_isexternal included)
    // =================================================================================================

    [Fact]
    public async Task All_internal_recipients_get_the_app_link_and_no_portal_link()
    {
        var facts = Facts(internalAddresses: ["a@spaarke.example", "b@spaarke.example"]);

        var extras = await facts.ResolveAsync("sprk_matter", Matter, ["a@spaarke.example", "b@spaarke.example"], CancellationToken.None);

        extras.LinkLines.Should().Equal($"Open this matter in Spaarke: {Env}/main.aspx?etn=sprk_matter&id={Matter:D}&pagetype=entityrecord");
    }

    [Fact]
    public async Task All_external_recipients_get_the_portal_link_and_no_app_link()
    {
        var facts = Facts(internalAddresses: []);

        var extras = await facts.ResolveAsync("sprk_matter", Matter, ["counsel@firm.example"], CancellationToken.None);

        extras.LinkLines.Should().Equal(
            $"Open this matter in the Spaarke portal (if you have portal access): {Portal}/?customer={Key}&record=matter:{Matter:D}");
    }

    [Fact]
    public async Task Mixed_recipients_get_both_lines_each_labelled_for_its_audience()
    {
        var facts = Facts(internalAddresses: ["a@spaarke.example"]);

        var extras = await facts.ResolveAsync("sprk_matter", Matter, ["a@spaarke.example", "counsel@firm.example"], CancellationToken.None);

        extras.LinkLines.Should().Equal(
            $"Spaarke users, open this matter in Spaarke: {Env}/main.aspx?etn=sprk_matter&id={Matter:D}&pagetype=entityrecord",
            $"External recipients, open this matter in the Spaarke portal (if you have portal access): {Portal}/?customer={Key}&record=matter:{Matter:D}");
    }

    [Fact]
    public async Task A_disabled_systemuser_is_external()
    {
        var facts = Facts(internalAddresses: ["a@spaarke.example"], disabled: true);

        var extras = await facts.ResolveAsync("sprk_matter", Matter, ["a@spaarke.example"], CancellationToken.None);

        extras.LinkLines.Should().ContainSingle().Which.Should().StartWith("Open this matter in the Spaarke portal (if you have portal access): ");
    }

    [Fact]
    public async Task A_recipient_whose_user_read_fails_is_treated_as_external_and_logged()
    {
        var log = new Warnings();
        var facts = Facts(internalAddresses: [], readFails: true, log: log);

        var extras = await facts.ResolveAsync("sprk_matter", Matter, ["a@spaarke.example"], CancellationToken.None);

        extras.LinkLines.Should().ContainSingle().Which.Should().StartWith("Open this matter in the Spaarke portal (if you have portal access): ");
        log.Lines.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("sprk_event", Portal, Key)]
    [InlineData("sprk_matter", Portal, null)]
    [InlineData("sprk_matter", "http://portal.spaarke.test", Key)]
    [InlineData("sprk_matter", null, Key)]
    public async Task No_portal_line_for_an_unparsed_type_no_key_or_an_unusable_portal_url_and_a_warning_is_logged(
        string coreType, string? portalUrl, string? key)
    {
        var log = new Warnings();
        var facts = Facts(internalAddresses: [], portalUrl: portalUrl, customerKey: key, log: log);

        var extras = await facts.ResolveAsync(coreType, Matter, ["counsel@firm.example"], CancellationToken.None);

        extras.LinkLines.Should().BeEmpty();
        log.Lines.Should().ContainSingle(w => w.Contains("portal link", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task No_app_line_when_the_environment_url_is_not_https()
    {
        var facts = Facts(internalAddresses: ["a@spaarke.example"], environmentUrl: "http://spaarkedev1.crm.dynamics.com");

        (await facts.ResolveAsync("sprk_matter", Matter, ["a@spaarke.example"], CancellationToken.None)).LinkLines.Should().BeEmpty();
    }

    [Fact]
    public async Task A_link_is_built_only_from_configured_bases_a_token_and_a_guid_never_from_a_recipient()
    {
        var facts = Facts(internalAddresses: []);

        var extras = await facts.ResolveAsync("sprk_matter", Matter, ["x@evil.example?record=matter:00000000-0000-0000-0000-000000000000"], CancellationToken.None);

        extras.LinkLines.Should().ContainSingle().Which.Should().NotContain("evil");
    }

    // =================================================================================================
    // K3 (fix round 1): the recipient reads run concurrently, capped, under ONE deadline
    // =================================================================================================

    [Fact]
    public async Task Recipients_are_checked_concurrently_and_at_most_ten_at_a_time()
    {
        var inFlight = 0;
        var max = 0;
        var addresses = Enumerable.Range(0, 12).Select(i => $"u{i}@spaarke.example").ToArray();
        var facts = Facts(internalAddresses: [], classify: async email =>
        {
            var now = Interlocked.Increment(ref inFlight);
            lock (addresses) max = Math.Max(max, now);
            await Task.Delay(40);
            Interlocked.Decrement(ref inFlight);
            return SystemUserEmailLookup.Of(new SystemUserEmailRow(UserId, false, email, null));
        });

        var extras = await facts.ResolveAsync("sprk_matter", Matter, addresses, CancellationToken.None);

        max.Should().Be(10, "twelve recipients are checked ten at a time, not one after another");
        extras.LinkLines.Should().ContainSingle().Which.Should().StartWith("Open this matter in Spaarke: ", "all twelve were classified");
    }

    [Fact]
    public async Task A_recipient_check_that_never_returns_is_external_after_the_one_deadline_and_the_rest_are_kept()
    {
        var log = new Warnings();
        var facts = Facts(
            internalAddresses: [], log: log, readDeadline: TimeSpan.FromMilliseconds(150),
            classify: email => email.StartsWith("slow@", StringComparison.Ordinal)
                ? new TaskCompletionSource<SystemUserEmailLookup>().Task // never completes, ignores the token
                : Task.FromResult(SystemUserEmailLookup.Of(new SystemUserEmailRow(UserId, false, email, null))));
        var clock = Stopwatch.StartNew();

        var extras = await facts.ResolveAsync("sprk_matter", Matter, ["a@spaarke.example", "slow@firm.example"], CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(20));

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "the send does not wait on a hung read");
        extras.LinkLines.Should().HaveCount(2, "the checked user is internal (app link), the hung one is external (portal link)");
        extras.LinkLines[0].Should().StartWith("Spaarke users, open this matter in Spaarke: ");
        extras.LinkLines[1].Should().StartWith("External recipients, open this matter in the Spaarke portal");
        extras.RecordLabel.Should().Be("Acme v. Roe (MAT-000123)", "the label read finished before the deadline");
        log.Lines.Should().Contain(w => w.Contains("1 of 2 recipient(s)"));
        log.Lines.Should().OnlyContain(w => !w.Contains('@'), "a warning carries counts only, never an address");
    }

    [Fact]
    public async Task When_every_read_hangs_the_email_still_resolves_with_the_portal_link_and_no_label()
    {
        var facts = Facts(
            internalAddresses: [], readDeadline: TimeSpan.FromMilliseconds(100),
            classify: _ => new TaskCompletionSource<SystemUserEmailLookup>().Task);

        var extras = await facts.ResolveAsync("sprk_matter", Matter, ["a@spaarke.example", "b@firm.example"], CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(20));

        extras.LinkLines.Should().ContainSingle().Which.Should().StartWith("Open this matter in the Spaarke portal");
    }

    [Fact]
    public async Task A_cancelled_request_surfaces_the_cancellation_and_does_not_log_the_deadline_warning()
    {
        var log = new Warnings();
        var facts = Facts(
            internalAddresses: [], log: log, readDeadline: TimeSpan.FromSeconds(30),
            classify: _ => new TaskCompletionSource<SystemUserEmailLookup>().Task); // never completes, ignores the token
        using var request = new CancellationTokenSource();
        request.CancelAfter(TimeSpan.FromMilliseconds(100));

        var act = () => facts.ResolveAsync("sprk_matter", Matter, ["a@spaarke.example"], request.Token)
            .WaitAsync(TimeSpan.FromSeconds(20));

        await act.Should().ThrowAsync<OperationCanceledException>();
        log.Lines.Should().NotContain(w => w.Contains("did not finish"), "the request was cancelled, the deadline did not pass");
    }

    // =================================================================================================
    // F3a (fix round 1): the send-time wiring of InquiryRecordFacts, through the real InquiryEmailSender.PrepareAsync
    // (SendAsync calls it, then the communication core). The executor tests fake SendAsync and never reach it.
    // =================================================================================================

    [Fact]
    public async Task PrepareAsync_asks_the_facts_about_the_matter_and_the_recipients_and_puts_the_result_in_the_send()
    {
        var link = $"{Env}/main.aspx?etn=sprk_matter&id={Matter:D}&pagetype=entityrecord";
        var facts = StubFacts(new InquiryEmailExtras("Acme v. Roe (MAT-000123)", [$"Open this matter in Spaarke: {link}"]));
        var sender = new InquiryEmailSender(null!, new HttpContextAccessor(), facts.Object);
        var request = new BudgetInquiryRequest(Matter, ["counsel@firm.example", "second@firm.example"], "Budget inquiry: Acme", "Please explain.", true, Firm);
        var serviceRequest = Guid.NewGuid();

        var send = await sender.PrepareAsync(request, serviceRequest, CancellationToken.None);

        facts.Verify(f => f.ResolveAsync(
            "sprk_matter", Matter, It.Is<IReadOnlyList<string>>(r => r.SequenceEqual(new[] { "counsel@firm.example", "second@firm.example" })),
            It.IsAny<CancellationToken>()), Times.Once);
        send.To.Should().Equal("counsel@firm.example", "second@firm.example");
        send.Body.Should().Be($"Please explain.\n\nOpen this matter in Spaarke: {link}");
        send.Associations![0].EntityType.Should().Be("sprk_servicerequest");
        send.Associations![0].EntityId.Should().Be(serviceRequest);
        send.Associations![0].EntityName.Should().Be("Acme v. Roe (MAT-000123)");
        send.Associations![1].EntityType.Should().Be("sprk_matter");
        send.Associations![1].EntityId.Should().Be(Matter);
    }

    [Fact]
    public async Task PrepareAsync_with_the_real_facts_links_each_audience_and_names_the_matter()
    {
        var sender = new InquiryEmailSender(null!, new HttpContextAccessor(), Facts(internalAddresses: ["a@spaarke.example"]));
        var request = new BudgetInquiryRequest(Matter, ["a@spaarke.example", "counsel@firm.example"], "Budget inquiry: Acme", "Please explain.", true);

        var send = await sender.PrepareAsync(request, Guid.NewGuid(), CancellationToken.None);

        send.Body.Should().Be(
            "Please explain.\n\n" +
            $"Spaarke users, open this matter in Spaarke: {Env}/main.aspx?etn=sprk_matter&id={Matter:D}&pagetype=entityrecord\n" +
            $"External recipients, open this matter in the Spaarke portal (if you have portal access): {Portal}/?customer={Key}&record=matter:{Matter:D}");
        send.Associations![0].EntityName.Should().Be("Acme v. Roe (MAT-000123)");
    }

    [Fact]
    public async Task PrepareAsync_without_facts_sends_the_body_unchanged_and_names_the_inquiry_by_its_subject()
    {
        var sender = new InquiryEmailSender(null!, new HttpContextAccessor());

        var send = await sender.PrepareAsync(Request(), Guid.NewGuid(), CancellationToken.None);

        send.Body.Should().Be("Please explain.");
        send.Associations![0].EntityName.Should().Be("Budget inquiry: Acme");
    }

    // =================================================================================================
    // The footer (D-181 as decided by the owner 2026-10-23): EntityName names the RECORD, with no prefix
    // =================================================================================================

    [Theory]
    [InlineData("Acme v. Roe", "MAT-000123", "Acme v. Roe (MAT-000123)")]
    [InlineData("Acme v. Roe", null, "Acme v. Roe")]
    [InlineData("Acme v. Roe", " ", "Acme v. Roe")]
    [InlineData(null, "MAT-000123", "MAT-000123")]
    public async Task The_record_label_is_the_core_name_and_number(string? name, string? number, string expected)
    {
        var facts = Facts(internalAddresses: [], matterName: name, matterNumber: number);

        (await facts.ResolveAsync("sprk_matter", Matter, ["counsel@firm.example"], CancellationToken.None)).RecordLabel.Should().Be(expected);
    }

    [Fact]
    public async Task The_record_label_is_none_when_the_matter_cannot_be_read_and_the_email_still_goes()
    {
        var facts = Facts(internalAddresses: [], matterReadThrows: true);

        var extras = await facts.ResolveAsync("sprk_matter", Matter, ["counsel@firm.example"], CancellationToken.None);

        extras.RecordLabel.Should().BeNull();
        extras.LinkLines.Should().ContainSingle("a failed name read never stops the link");
    }

    [Fact]
    public void Build_names_the_record_on_the_service_request_association_and_keeps_the_association_order()
    {
        var sr = Guid.NewGuid();
        var built = InquiryEmailSender.Build(Request(), sr, new InquiryEmailExtras("Acme v. Roe (MAT-000123)", ["Open this matter in Spaarke: x"]));

        built.Associations![0].EntityType.Should().Be("sprk_servicerequest");
        built.Associations![0].EntityId.Should().Be(sr);
        built.Associations![0].EntityName.Should().Be("Acme v. Roe (MAT-000123)");
        built.Associations![1].EntityType.Should().Be("sprk_matter");
        built.Body.Should().Be("Please explain.\n\nOpen this matter in Spaarke: x");
    }

    [Fact]
    public void Build_without_a_record_label_names_the_inquiry_by_its_subject_never_a_logical_name_or_guid()
    {
        var built = InquiryEmailSender.Build(Request(), Guid.NewGuid(), new InquiryEmailExtras(null, []));

        built.Associations![0].EntityName.Should().Be("Budget inquiry: Acme");
        built.Body.Should().Be("Please explain.");
    }

    [Fact]
    public void A_record_label_longer_than_the_regarding_column_is_cut_to_100()
    {
        InquiryRecordFacts.RecordLabel(new string('n', 150), "MAT-1")!.Length.Should().Be(100);
    }

    // =================================================================================================
    // Rig
    // =================================================================================================

    private static BudgetInquiryRequest Request() =>
        new(Matter, ["counsel@firm.example"], "Budget inquiry: Acme", "Please explain.", true);

    private static ResolvedDecisionPlan InquiryPlan() => Plan("send-budget-inquiry", "revise-budget", "approve-variance");

    private static ResolvedDecisionPlan Plan(params string[] codes) =>
        new(codes.Select(c => DecisionActionCatalog.All.Single(a => a.Code == c)).ToList(), [], DecisionActionCatalog.DismissalReasons(DecisionLane.Decide));

    private static SignalAccessDecision Allowed() =>
        new(SignalAccessOutcome.Allowed, DecisionLane.Decide, Guid.NewGuid(), Guid.NewGuid(), Matter, CoreTable: "sprk_matter");

    private static SignalCoreRecordAccess Access(Users users) =>
        new(users, Mock.Of<IGenericEntityService>(), Mock.Of<Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService>(),
            NullLogger<SignalCoreRecordAccess>.Instance);

    private static InquiryRecordFacts Facts(
        string[] internalAddresses, bool disabled = false, bool readFails = false, string? portalUrl = Portal, string? customerKey = Key,
        string environmentUrl = Env, string? matterName = "Acme v. Roe", string? matterNumber = "MAT-000123", bool matterReadThrows = false,
        Warnings? log = null, Func<string, Task<SystemUserEmailLookup>>? classify = null, TimeSpan? readDeadline = null)
    {
        var appOnly = new Mock<IFieldMappingDataverseService>();
        var read = appOnly.Setup(a => a.RetrieveRecordFieldsAsync("sprk_matter", Matter, It.IsAny<string[]>(), It.IsAny<CancellationToken>()));
        if (matterReadThrows)
            read.ThrowsAsync(new InvalidOperationException("dataverse down"));
        else
            read.ReturnsAsync(new Dictionary<string, object?> { ["sprk_mattername"] = matterName, ["sprk_matternumber"] = matterNumber });

        var identities = new Mock<IContactIdentityStore>();
        identities.Setup(i => i.FindEnabledSystemUsersByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string email, CancellationToken _) => classify is not null
                ? classify(email)
                : Task.FromResult(
                    readFails
                        ? SystemUserEmailLookup.Failed
                        : internalAddresses.Contains(email, StringComparer.OrdinalIgnoreCase)
                            ? SystemUserEmailLookup.Of(new SystemUserEmailRow(UserId, disabled, email, null))
                            : SystemUserEmailLookup.Of()));

        var (config, customer) = Settings(portalUrl, customerKey);
        return new InquiryRecordFacts(appOnly.Object, identities.Object, customer, config,
            Options.Create(new DataverseOptions { EnvironmentUrl = environmentUrl }), (ILogger<InquiryRecordFacts>?)log ?? NullLogger<InquiryRecordFacts>.Instance)
        {
            ReadDeadline = readDeadline ?? TimeSpan.FromSeconds(10),
        };
    }

    private static (IConfiguration Config, CustomerIdentity Customer) Settings(string? portalUrl, string? customerKey)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ExternalAccess:PortalUrl"] = portalUrl })
            .Build();
        var customer = new CustomerIdentity(
            Options.Create(new CustomerOptions { Id = customerKey ?? string.Empty }), config, NullLogger<CustomerIdentity>.Instance);
        return (config, customer);
    }

    /// <summary>A facts double: <c>ResolveAsync</c> (virtual) answers <paramref name="extras"/> and records its arguments.</summary>
    private static Mock<InquiryRecordFacts> StubFacts(InquiryEmailExtras extras)
    {
        var (config, customer) = Settings(Portal, Key);
        var facts = new Mock<InquiryRecordFacts>(
            Mock.Of<IFieldMappingDataverseService>(), Mock.Of<IContactIdentityStore>(), customer, config,
            Options.Create(new DataverseOptions { EnvironmentUrl = Env }), NullLogger<InquiryRecordFacts>.Instance);
        facts.Setup(f => f.ResolveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(extras);
        return facts;
    }

    private sealed class Warnings : ILogger<InquiryRecordFacts>
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Lines.Add(formatter(state, exception));
        }
    }

    /// <summary>The caller's client: a GET answers the first registered prefix, else 404.</summary>
    private sealed class Users : IDataverseUserClient
    {
        private readonly List<(string Prefix, DataverseUserResponse Response)> _gets = [];
        public List<string> Gets { get; } = [];

        public Users On(string prefix, object body)
        {
            _gets.Add((prefix, DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(body))));
            return this;
        }

        public Users Deny(string prefix)
        {
            _gets.Add((prefix, DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied, "denied")));
            return this;
        }

        public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
        {
            Gets.Add(relativePath);
            var hit = _gets.FirstOrDefault(g => relativePath.StartsWith(g.Prefix, StringComparison.Ordinal));
            return Task.FromResult(hit.Prefix is null ? DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "no") : hit.Response);
        }

        public Task<DataverseUserResponse> PatchAsync(string p, string j, CancellationToken ct) => throw new InvalidOperationException("no patch");
        public Task<DataverseUserResponse> PostAsync(string a, string j, CancellationToken ct) => throw new InvalidOperationException("no post");
        public Task<DataverseUserResponse> PostAsync(string a, string j, bool prefer, CancellationToken ct) => throw new InvalidOperationException("no post");
        public Task<DataverseUserResponse> DeleteAsync(string p, CancellationToken ct) => throw new InvalidOperationException("no delete");
    }
}
