using System.ServiceModel;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// Task 168 (D-194): what Assign Task's notification sends, through the REAL <see cref="AssignTaskNotifier"/> (its two Graph sends are
/// the only doubles: no real email is sent), the REAL <see cref="NotificationService"/> (its <c>appnotification</c> create is captured
/// at <see cref="IGenericEntityService"/>), uac-r2's link-candidate read in memory, and the REAL system-mailbox job over the REAL
/// idempotency service. The commit route's half (after the record, never failing the decision) is in
/// <c>DecisionCommitServiceTests.AssignTaskNotify.cs</c>.
/// </summary>
public sealed class AssignTaskNotifierTests
{
    private static readonly Guid Contact = Guid.Parse("c1680000-0000-4000-8000-000000000001");
    private static readonly Guid TaskId = Guid.Parse("c1680000-0000-4000-8000-000000000002");
    private static readonly Guid Matter = Guid.Parse("c1680000-0000-4000-8000-000000000003");
    private static readonly Guid LinkedUser = Guid.Parse("c1680000-0000-4000-8000-000000000004");
    private const string Address = "pat.assignee@firm.example";
    private const string EnvironmentUrl = "https://org.crm.dynamics.com";
    private const string SystemMailbox = "noreply@firm.example";

    private sealed class Harness
    {
        public Mock<IGenericEntityService> Entities { get; } = new(MockBehavior.Strict);
        public AssignedAccessTestDoubles.FakeAssignedAccessStore Links { get; } = new();
        public Mock<JobSubmissionService> Jobs { get; } = new(MockBehavior.Loose, Options.Create(new ServiceBusOptions { QueueName = "test" }),
            NullLogger<JobSubmissionService>.Instance, new Mock<ServiceBusClient>().Object);
        public Mock<AssignTaskNotifier> Notifier { get; }
        public ListLogger<AssignTaskNotifier> Log { get; } = new();
        public List<JobContract> Queued { get; } = new();
        public List<SendCommunicationRequest> UserSends { get; } = new();
        public List<SendCommunicationRequest> SystemSends { get; } = new();
        public List<Entity> Bells { get; } = new();
        public string? ContactEmail { get; set; } = Address;
        public bool ContactGone { get; set; }
        public Exception? UserSendFault { get; set; }
        public bool UserSendHangs { get; set; }
        public Exception? EnqueueFault { get; set; }
        public Exception? BellFault { get; set; }
        public bool AlreadyRecorded { get; set; }
        public string? TaskDescription { get; set; } = "Call the client";
        public DateTime? TaskDue { get; set; } = new DateTime(2026, 11, 15);

        public Harness()
        {
            Notifier = new Mock<AssignTaskNotifier>(
                null!, new NotificationService(Entities.Object, NullLogger<NotificationService>.Instance), Entities.Object, Links, Jobs.Object,
                Options.Create(new CommunicationOptions { DefaultMailbox = SystemMailbox }),
                Options.Create(new DataverseOptions { EnvironmentUrl = EnvironmentUrl }), Log)
            { CallBase = true };

            Notifier.Setup(n => n.SendAsUserAsync(It.IsAny<SendCommunicationRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
                .Returns(async (SendCommunicationRequest r, HttpContext _, CancellationToken ct) =>
                {
                    UserSends.Add(r);
                    if (UserSendHangs) await Task.Delay(Timeout.Infinite, ct);
                    if (UserSendFault is { } fault) throw fault;
                });
            Notifier.Setup(n => n.SendAsSystemAsync(It.IsAny<SendCommunicationRequest>(), It.IsAny<CancellationToken>()))
                .Callback((SendCommunicationRequest r, CancellationToken _) => SystemSends.Add(r))
                .Returns(Task.CompletedTask);

            Jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
                .Returns((JobContract job, CancellationToken _) =>
                {
                    if (EnqueueFault is { } fault) return Task.FromException(fault);
                    Queued.Add(job);
                    return Task.CompletedTask;
                });

            Entities.Setup(e => e.RetrieveAsync("contact", Contact, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .Returns(() => ContactGone
                    ? Task.FromException<Entity>(new FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = -2147220969 }))
                    : Task.FromResult(new Entity("contact", Contact) { ["emailaddress1"] = ContactEmail }));
            Entities.Setup(e => e.RetrieveAsync("sprk_matter", Matter, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Entity("sprk_matter", Matter) { ["sprk_mattername"] = "Acme <Holdings> & Co", ["sprk_matternumber"] = "MAT-0042" });
            Entities.Setup(e => e.RetrieveAsync("sprk_event", TaskId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new Entity("sprk_event", TaskId) { ["sprk_description"] = TaskDescription, ["sprk_duedate"] = TaskDue });
            Entities.Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_communication"), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new EntityCollection(AlreadyRecorded ? [new Entity("sprk_communication", Guid.NewGuid())] : []));
            Entities.Setup(e => e.CreateAsync(It.Is<Entity>(x => x.LogicalName == "appnotification"), It.IsAny<CancellationToken>()))
                .Returns((Entity e, CancellationToken _) =>
                {
                    if (BellFault is { } fault) return Task.FromException<Guid>(fault);
                    Bells.Add(e);
                    return Task.FromResult(Guid.NewGuid());
                });

            LinkUser(LinkedUser);
        }

        public void LinkUser(Guid user, bool disabled = false) =>
            Links.UsersByLink.GetOrAdd(Contact, _ => new List<AssignedLinkCandidate>())
                .Add(new AssignedLinkCandidate(user, Contact, Guid.NewGuid(), disabled, 0, null, false));

        public Task<IReadOnlyList<string>> NotifyAsync(AssignTaskNotice? notice = null) =>
            Notifier.Object.NotifyAsync(new DefaultHttpContext(), notice ?? Notice(), CancellationToken.None);

        public AssignTaskEmailJobHandler Handler(IIdempotencyService? idempotency = null) =>
            new(Notifier.Object, idempotency ?? Idempotency(), NullLogger<AssignTaskEmailJobHandler>.Instance);
    }

    private static AssignTaskNotice Notice(string description = "Call the client <b>today</b>\nand confirm", DateOnly? due = null, bool noDue = false) =>
        new(Contact, TaskId, description, noDue ? null : due ?? new DateOnly(2026, 11, 15), new DecisionCoreRecord("sprk_matter", Matter));

    private static IIdempotencyService Idempotency() =>
        new IdempotencyService(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<IdempotencyService>.Instance);

    private static string MatterLink => $"{EnvironmentUrl}/main.aspx?etn=sprk_matter&id={Matter:D}&pagetype=entityrecord";

    // ── The email, as the assigner ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NotifyOn_SendsOneEmailAsTheAssigner_ToTheBoundContact_WithTheMatterLink_WhatToDo_AndDue_AllEncoded()
    {
        var h = new Harness();

        var warnings = await h.NotifyAsync();

        warnings.Should().BeEmpty();
        var sent = h.UserSends.Should().ContainSingle().Which;
        sent.To.Should().Equal(Address);
        sent.Cc.Should().BeNull();
        sent.Bcc.Should().BeNull();
        sent.SendMode.Should().Be(SendMode.User, "D-194: from the assigner's own mailbox first");
        sent.BodyFormat.Should().Be(BodyFormat.HTML);
        sent.CorrelationId.Should().Be($"assign-task-{TaskId:N}");
        sent.Associations.Should().ContainSingle().Which.Should().Match<CommunicationAssociation>(a => a.EntityType == "sprk_matter" && a.EntityId == Matter);
        sent.Subject.Should().Be("A task was assigned to you: Acme <Holdings> & Co (MAT-0042)");

        sent.Body.Should().Contain($"href=\"{System.Net.WebUtility.HtmlEncode(MatterLink)}\"");
        sent.Body.Should().Contain("Acme &lt;Holdings&gt; &amp; Co (MAT-0042)");
        sent.Body.Should().Contain("Call the client &lt;b&gt;today&lt;/b&gt;<br />and confirm");
        sent.Body.Should().NotContain("<b>today</b>", "every value is HTML-encoded");
        sent.Body.Should().Contain("15 November 2026");
        sent.Body.Should().NotContain("{{", "every placeholder is filled");

        h.SystemSends.Should().BeEmpty("never both");
        h.Queued.Should().BeEmpty("never both");
    }

    [Fact]
    public async Task NoDueDate_SaysSo()
    {
        var h = new Harness();

        await h.NotifyAsync(Notice(noDue: true));

        h.UserSends.Single().Body.Should().Contain("No due date");
    }

    [Fact]
    public async Task AValueThatLooksLikeAPlaceholder_IsText()
    {
        var h = new Harness();

        await h.NotifyAsync(Notice(description: "Use {{Due}} and {{Record}}"));

        h.UserSends.Single().Body.Should().Contain("Use {{Due}} and {{Record}}", "the template is filled in one pass");
        h.UserSends.Single().Body.Should().Contain("15 November 2026");
    }

    [Fact]
    public void TheTemplate_IsEmbedded_AndBuildEmailSaysNothingButTheRecord_WhatToDo_AndDue()
    {
        var (subject, body) = AssignTaskNotifier.BuildEmail(
            "{{RecordNoun}}|{{Record}}|{{WhatToDo}}|{{Due}}|{{Year}}",
            new AssignTaskNotifier.RecordFacts("Matter", "M", "https://x.example/a?b=1&c=2", "/rel"), "Do it", null, 2026);

        subject.Should().Be("A task was assigned to you: M");
        body.Should().Be("Matter|<a href=\"https://x.example/a?b=1&amp;c=2\" style=\"color: #0f6cbd;\">M</a>|Do it|No due date|2026");
    }

    // ── The fallback: the system mailbox, from a job ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("graph")]
    [InlineData("consent")]
    [InlineData("timeout")]
    public async Task TheAssignersSendFails_QueuesOneSystemMailboxJob_WithIdsOnly_AndTheJobSendsItOnce(string failure)
    {
        var h = new Harness();
        switch (failure)
        {
            case "graph": h.UserSendFault = new HttpRequestException("graph 500"); break;
            case "consent": h.UserSendFault = new InvalidOperationException("no mailbox / consent required"); break;
            default:
                h.UserSendHangs = true;
                h.Notifier.Object.UserSendTimeout = TimeSpan.FromMilliseconds(50);
                break;
        }

        var warnings = await h.NotifyAsync();

        warnings.Should().BeEmpty("the email is still going, from the system mailbox");
        h.UserSends.Should().ContainSingle();
        h.SystemSends.Should().BeEmpty("the request does not wait for a second send");
        var job = h.Queued.Should().ContainSingle().Which;
        job.JobType.Should().Be(AssignTaskNotifier.JobTypeName);
        job.IdempotencyKey.Should().Be($"assign-task-{TaskId:N}");
        var payload = job.Payload!.RootElement.GetRawText();
        payload.Should().NotContain(Address).And.NotContain("Call the client", "the payload holds ids only");

        // The job: one send from the approved system mailbox; a redelivery sends nothing more.
        var handler = h.Handler();
        (await handler.ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Completed);
        (await handler.ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Completed);

        var system = h.SystemSends.Should().ContainSingle().Which;
        system.SendMode.Should().Be(SendMode.SharedMailbox);
        system.FromMailbox.Should().Be(SystemMailbox);
        system.To.Should().Equal(Address);
        system.CorrelationId.Should().Be($"assign-task-{TaskId:N}");
        system.Body.Should().Contain("Call the client").And.Contain("15 November 2026").And.Contain(System.Net.WebUtility.HtmlEncode(MatterLink));
    }

    [Fact]
    public async Task TheJob_SendsNothing_WhenAnEmailForTheTaskIsAlreadyRecorded()
    {
        var h = new Harness { AlreadyRecorded = true };
        var job = Job(new AssignTaskEmailPayload(TaskId, Contact, "sprk_matter", Matter));

        (await h.Handler().ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Completed);

        h.SystemSends.Should().BeEmpty();
    }

    [Fact]
    public async Task TheJob_ASendFault_IsAFailureToRetry_AndAnUnreadablePayloadIsPoisoned()
    {
        var h = new Harness();
        h.Notifier.Setup(n => n.SendAsSystemAsync(It.IsAny<SendCommunicationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sender not approved"));

        (await h.Handler().ProcessAsync(Job(new AssignTaskEmailPayload(TaskId, Contact, "sprk_matter", Matter)), CancellationToken.None))
            .Status.Should().Be(JobStatus.Failed);
        (await h.Handler().ProcessAsync(new JobContract { JobType = AssignTaskNotifier.JobTypeName, Payload = JsonDocument.Parse("{}") }, CancellationToken.None))
            .Status.Should().Be(JobStatus.Poisoned);
    }

    [Fact]
    public async Task TheJobCannotBeQueued_ReturnsTheNotSentWarning()
    {
        var h = new Harness { UserSendFault = new HttpRequestException("graph 500"), EnqueueFault = new ServiceBusException("down", ServiceBusFailureReason.ServiceCommunicationProblem) };

        var warnings = await h.NotifyAsync();

        warnings.Should().Equal(AssignTaskNotifier.EmailNotSentWarning);
        h.SystemSends.Should().BeEmpty();
    }

    // ── Must not fire: no address ──────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-address")]
    public async Task AContactWithNoUsableEmail_GetsNoEmail_AndTheWarningSaysSo_MustNotFire(string? email)
    {
        var h = new Harness { ContactEmail = email };

        var warnings = await h.NotifyAsync();

        warnings.Should().Equal(AssignTaskNotifier.EmailMissingWarning);
        h.UserSends.Should().BeEmpty();
        h.Queued.Should().BeEmpty();
        h.Bells.Should().ContainSingle("the bell does not depend on the email");
    }

    [Fact]
    public async Task AContactThatNoLongerExists_GetsNoEmail_MustNotFire()
    {
        var h = new Harness { ContactGone = true };

        (await h.NotifyAsync()).Should().Equal(AssignTaskNotifier.EmailMissingWarning);
        h.UserSends.Should().BeEmpty();
        h.Queued.Should().BeEmpty();
    }

    // ── The bell ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheSingleLinkedUser_GetsOneBell_WithTheMattersLink_AndNoDecisionContent()
    {
        var h = new Harness();

        await h.NotifyAsync(Notice(description: "Secret strategy text"));

        var bell = h.Bells.Should().ContainSingle().Which;
        bell.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(LinkedUser);
        bell.GetAttributeValue<string>("title").Should().Be("A task was assigned to you");
        bell.GetAttributeValue<string>("body").Should().Be("On matter Acme <Holdings> & Co (MAT-0042).");
        var data = bell.GetAttributeValue<string>("data");
        var action = JsonDocument.Parse(data).RootElement;
        action.GetProperty("actionUrl").GetString().Should().Be($"/main.aspx?etn=sprk_matter&id={Matter:D}&pagetype=entityrecord",
            "the matter's model-driven link (the GrantAccessNotifier form: a same-origin path)");
        action.GetProperty("actions")[0].GetProperty("title").GetString().Should().Be("Open matter");
        (bell.GetAttributeValue<string>("title") + bell.GetAttributeValue<string>("body") + data).Should().NotContain("Secret strategy text");
    }

    [Fact]
    public async Task NoLinkedUser_NoBell_ButTheEmailGoes_MustNotFire()
    {
        var h = new Harness();
        h.Links.UsersByLink.Clear();

        (await h.NotifyAsync()).Should().BeEmpty();

        h.Bells.Should().BeEmpty();
        h.UserSends.Should().ContainSingle();
    }

    [Fact]
    public async Task SeveralLinkedUsers_NoBell_MustNotFire()
    {
        var h = new Harness();
        h.LinkUser(Guid.NewGuid());

        await h.NotifyAsync();

        h.Bells.Should().BeEmpty("zero or several linked users get no bell");
    }

    [Fact]
    public async Task OnlyADisabledLinkedUser_NoBell_MustNotFire()
    {
        var h = new Harness();
        h.Links.UsersByLink.Clear();
        h.LinkUser(Guid.NewGuid(), disabled: true);

        await h.NotifyAsync();

        h.Bells.Should().BeEmpty();
    }

    [Fact]
    public async Task ABellOrLinkReadFault_NeverThrows_AndTheEmailIsUnaffected_MustNotFire()
    {
        var h = new Harness { BellFault = new TimeoutException("dataverse") };
        (await h.NotifyAsync()).Should().BeEmpty();
        h.UserSends.Should().ContainSingle();

        var g = new Harness();
        g.Links.FailLinkRead = true;
        (await g.NotifyAsync()).Should().BeEmpty();
        g.Bells.Should().BeEmpty();
        g.UserSends.Should().ContainSingle();
    }

    // ── Security: ids only in the logs ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheLogs_CarryRecordIds_NeverTheAddress()
    {
        var h = new Harness { UserSendFault = new HttpRequestException("graph 500") };
        await h.NotifyAsync();
        var g = new Harness { ContactEmail = "" };
        await g.NotifyAsync();

        h.Log.Messages.Concat(g.Log.Messages).Should().NotBeEmpty()
            .And.OnlyContain(m => !m.Contains(Address, StringComparison.OrdinalIgnoreCase));
        h.Log.Messages.Should().Contain(m => m.Contains(TaskId.ToString(), StringComparison.Ordinal));
    }

    private static JobContract Job(AssignTaskEmailPayload payload) => new()
    {
        JobId = Guid.NewGuid(),
        JobType = AssignTaskNotifier.JobTypeName,
        IdempotencyKey = $"assign-task-{payload.TaskId:N}",
        Payload = JsonSerializer.SerializeToDocument(payload),
    };

    internal sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
