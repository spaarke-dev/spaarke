using System.Security.Claims;
using System.ServiceModel;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// Task 168 (D-194) and its fix round (D-199): what Assign Task's notification sends, and to whom, through the REAL
/// <see cref="AssignTaskNotifier"/> and the REAL <see cref="AssignTaskRecipientGate"/> decision. Doubled are only the I/O boundaries: the
/// two Graph sends (no real email is sent), the identity store and uac-r2's link-candidate read (in memory), the gate's four Dataverse
/// questions (the No Access wall, the user's rights on the task, the record's Restricted state, the contact's portal grants), the
/// record-name read (<see cref="InquiryRecordFacts"/>), and the <c>appnotification</c> create (captured at
/// <see cref="IGenericEntityService"/> under the REAL <see cref="NotificationService"/>). The system-mailbox job runs for real over the
/// REAL idempotency service. The commit route's half is in <c>DecisionCommitServiceTests.AssignTaskNotify.cs</c>.
/// </summary>
public sealed class AssignTaskNotifierTests
{
    private static readonly Guid Contact = Guid.Parse("c1680000-0000-4000-8000-000000000001");
    private static readonly Guid TaskId = Guid.Parse("c1680000-0000-4000-8000-000000000002");
    private static readonly Guid Matter = Guid.Parse("c1680000-0000-4000-8000-000000000003");
    private static readonly Guid LinkedUser = Guid.Parse("c1680000-0000-4000-8000-000000000004");
    private static readonly Guid AssignerOid = Guid.Parse("c1680000-0000-4000-8000-000000000005");
    private static readonly Guid AssignerUser = Guid.Parse("c1680000-0000-4000-8000-000000000006");
    private static readonly Guid PortalOid = Guid.Parse("c1680000-0000-4000-8000-000000000007");
    private static readonly Guid OtherMatter = Guid.Parse("c1680000-0000-4000-8000-000000000008");
    private const string Address = "pat.assignee@firm.example";
    private const string EnvironmentUrl = "https://org.crm.dynamics.com";
    private const string PortalUrl = "https://portal.spaarke.test";
    private const string CustomerKey = "acme";
    private const string SystemMailbox = "noreply@firm.example";
    private const string Label = "Acme <Holdings> & Co (MAT-0042)";
    private const string AssignerName = "Avery Assigner";
    private const string AssignerUpn = "avery@firm.example";

    private sealed class Harness
    {
        public Mock<IGenericEntityService> Entities { get; } = new(MockBehavior.Strict);
        public AssignedAccessTestDoubles.FakeAssignedAccessStore Links { get; } = new();
        public Mock<IContactIdentityStore> Identities { get; } = new();
        public Mock<JobSubmissionService> Jobs { get; } = new(MockBehavior.Loose, Options.Create(new ServiceBusOptions { QueueName = "test" }),
            NullLogger<JobSubmissionService>.Instance, new Mock<ServiceBusClient>().Object);
        public Mock<AssignTaskRecipientGate> Gate { get; }
        public Mock<InquiryRecordFacts> Facts { get; }
        public Mock<AssignTaskNotifier> Notifier { get; }
        public ListLogger<AssignTaskNotifier> Log { get; } = new();
        public ListLogger<AssignTaskRecipientGate> GateLog { get; } = new();
        public List<JobContract> Queued { get; } = new();
        public List<SendCommunicationRequest> UserSends { get; } = new();
        public List<(SendCommunicationRequest Request, HttpContext? Sender)> SystemSends { get; } = new();
        public List<Entity> Bells { get; } = new();

        // The contact and its binding (an internal contact: active, linked by sprk_primarycontact, no oid of its own).
        public string? ContactEmail { get; set; } = Address;
        public int ContactState { get; set; }
        public Guid? ContactOid { get; set; }
        public int? ContactPlane { get; set; }
        public bool ContactMissing { get; set; }

        // The gate's Dataverse answers (an ordinary Standard matter the internal user can read).
        public SecureShareWallOutcome Wall { get; set; } = SecureShareWallOutcome.NotSecure;
        public bool WallThrows { get; set; }
        public AccessRights? TaskRights { get; set; } = AccessRights.Read | AccessRights.Write;
        public bool? Restricted { get; set; } = false;
        public AccessRights? PortalRights { get; set; } = AccessRights.None;

        /// <summary>Per-record grants overriding <see cref="PortalRights"/> (a re-filed task's new record).</summary>
        public Dictionary<Guid, AccessRights?> PortalRightsByRecord { get; } = new();

        /// <summary>Records whose No Access wall stops the user (overrides <see cref="Wall"/>).</summary>
        public HashSet<Guid> WalledRecords { get; } = new();

        /// <summary>The portal events list's per-row verdict on the task (true listed, false withheld, null undecided).</summary>
        public bool? PortalListsTask { get; set; } = true;
        public List<string> GateQuestions { get; } = new();

        // The record name InquiryRecordFacts would show (Standard + confirmed internal).
        public string? RecordLabel { get; set; } = Label;

        /// <summary>The Communication options the notifier reads (the system mailbox, <c>Communication:DefaultMailbox</c>).</summary>
        public CommunicationOptions Communication { get; } = new() { DefaultMailbox = SystemMailbox };

        public Exception? UserSendFault { get; set; }
        public Exception? SystemSendFault { get; set; }
        public bool UserSendHangs { get; set; }
        public Exception? EnqueueFault { get; set; }
        public Exception? BellFault { get; set; }
        public bool AlreadyRecorded { get; set; }
        public string? TaskDescription { get; set; } = "Call the client";
        public DateTime? TaskDue { get; set; } = new DateTime(2026, 11, 15);
        public Guid? TaskAssignee { get; set; } = Contact;
        public int TaskState { get; set; }

        /// <summary>The task's core filing lookups as Dataverse holds them NOW (only returned when the read selects them).</summary>
        public Dictionary<string, EntityReference?> TaskFiling { get; } = new() { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Matter) };

        public Harness()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ExternalAccess:PortalUrl"] = PortalUrl }).Build();
            var customer = new CustomerIdentity(Options.Create(new CustomerOptions { Id = CustomerKey }), config, NullLogger<CustomerIdentity>.Instance);

            Gate = new Mock<AssignTaskRecipientGate>(Identities.Object, Links, null!, null!, null!, null!, null!, GateLog) { CallBase = true };
            Gate.Setup(g => g.WallAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((string entity, Guid id, Guid user, CancellationToken _) =>
                {
                    GateQuestions.Add($"wall {entity} {id} {user}");
                    return WallThrows
                        ? Task.FromException<SecureShareWallOutcome>(new TimeoutException("dataverse"))
                        : Task.FromResult(WalledRecords.Contains(id) ? SecureShareWallOutcome.Walled : Wall);
                });
            Gate.Setup(g => g.TaskRightsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid user, Guid task, CancellationToken _) =>
                {
                    GateQuestions.Add($"task {task} {user}");
                    return TaskRights;
                });
            Gate.Setup(g => g.RestrictedAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string entity, Guid id, CancellationToken _) =>
                {
                    GateQuestions.Add($"restricted {entity} {id}");
                    return Restricted;
                });
            // Fix round 3: one composition per decision; its rights and the events list's verdict are this harness's answers.
            Gate.Setup(g => g.ComposePortalPrincipalAsync(It.IsAny<ContactBindingRow>(), It.IsAny<ExternalContactPlane>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ContactBindingRow contact, ExternalContactPlane _, CancellationToken _) => Composition(contact.ContactId));
            Gate.Setup(g => g.ContactRights(It.IsAny<ContactPrincipalComposition>(), It.IsAny<string>(), It.IsAny<Guid>()))
                .Returns((ContactPrincipalComposition composed, string table, Guid id) =>
                {
                    GateQuestions.Add($"grants {composed.Principal.ContactId} {table} {id}");
                    return PortalRightsByRecord.TryGetValue(id, out var byRecord) ? byRecord : PortalRights;
                });
            Gate.Setup(g => g.PortalListsTaskAsync(It.IsAny<ContactBindingRow>(), It.IsAny<ContactPrincipalComposition>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ContactBindingRow contact, ContactPrincipalComposition _, Guid task, CancellationToken _) =>
                {
                    GateQuestions.Add($"portal {contact.ContactId} {task}");
                    return PortalListsTask;
                });

            Identities.Setup(i => i.GetContactAsync(Contact, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => ContactMissing
                    ? ContactLookup.Of()
                    : ContactLookup.Of(new ContactBindingRow(Contact, ContactState, ContactOid?.ToString("D"), ContactPlane, Email: ContactEmail)));

            Facts = new Mock<InquiryRecordFacts>(Mock.Of<IFieldMappingDataverseService>(), Identities.Object, Mock.Of<ISystemUserIdentityResolver>(),
                customer, config, Options.Create(new DataverseOptions { EnvironmentUrl = EnvironmentUrl }), NullLogger<InquiryRecordFacts>.Instance);
            Facts.Setup(f => f.ResolveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new InquiryEmailExtras(RecordLabel, []));

            Notifier = new Mock<AssignTaskNotifier>(
                null!, new NotificationService(Entities.Object, NullLogger<NotificationService>.Instance), Entities.Object, Gate.Object, Facts.Object,
                Jobs.Object, Options.Create(Communication), customer, config,
                Options.Create(new DataverseOptions { EnvironmentUrl = EnvironmentUrl }), Log)
            { CallBase = true };

            Notifier.Setup(n => n.SendAsUserAsync(It.IsAny<SendCommunicationRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
                .Returns(async (SendCommunicationRequest r, HttpContext _, CancellationToken ct) =>
                {
                    UserSends.Add(r);
                    if (UserSendHangs) await Task.Delay(Timeout.Infinite, ct);
                    if (UserSendFault is { } fault) throw fault;
                });
            Notifier.Setup(n => n.SendAsSystemAsync(It.IsAny<SendCommunicationRequest>(), It.IsAny<HttpContext?>(), It.IsAny<CancellationToken>()))
                .Returns((SendCommunicationRequest r, HttpContext? sender, CancellationToken _) =>
                {
                    SystemSends.Add((r, sender));
                    return SystemSendFault is { } fault ? Task.FromException(fault) : Task.CompletedTask;
                });

            Jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
                .Returns((JobContract job, CancellationToken _) =>
                {
                    if (EnqueueFault is { } fault) return Task.FromException(fault);
                    Queued.Add(job);
                    return Task.CompletedTask;
                });

            Entities.Setup(e => e.RetrieveAsync("contact", Contact, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new Entity("contact", Contact) { ["emailaddress1"] = ContactEmail });
            Entities.Setup(e => e.RetrieveAsync("sprk_event", TaskId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, Guid _, string[] columns, CancellationToken _) =>
                {
                    var row = new Entity("sprk_event", TaskId)
                    {
                        ["sprk_description"] = TaskDescription,
                        ["sprk_duedate"] = TaskDue,
                        ["sprk_assignedto"] = TaskAssignee is { } a ? new EntityReference("contact", a) : null,
                        ["statecode"] = new OptionSetValue(TaskState),
                    };
                    // Dataverse returns only the columns asked for: a re-read that does not select the filing reads none.
                    foreach (var (column, value) in TaskFiling)
                        if (columns.Contains(column)) row[column] = value;
                    return row;
                });
            Entities.Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_communication"), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new EntityCollection(AlreadyRecorded ? [new Entity("sprk_communication", Guid.NewGuid())] : []));
            Entities.Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "systemuser"), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QueryExpression q, CancellationToken _) =>
                    new EntityCollection(q.Criteria.Conditions.Any(c => c.AttributeName == "azureactivedirectoryobjectid" && Equals(c.Values[0], AssignerOid))
                        // Answers only a lookup that excludes disabled users (issue #1576): a disabled row keeps its oid.
                        && q.Criteria.Conditions.Any(c => c.AttributeName == "isdisabled" && Equals(c.Values[0], false))
                        ? [new Entity("systemuser", AssignerUser) { ["fullname"] = AssignerName }]
                        : []));
            Entities.Setup(e => e.CreateAsync(It.Is<Entity>(x => x.LogicalName == "appnotification"), It.IsAny<CancellationToken>()))
                .Returns((Entity e, CancellationToken _) =>
                {
                    if (BellFault is { } fault) return Task.FromException<Guid>(fault);
                    Bells.Add(e);
                    return Task.FromResult(Guid.NewGuid());
                });

            LinkUser(LinkedUser);
        }

        public void LinkUser(Guid user, bool disabled = false, bool external = false) =>
            Links.UsersByLink.GetOrAdd(Contact, _ => new List<AssignedLinkCandidate>())
                .Add(new AssignedLinkCandidate(user, Contact, Guid.NewGuid(), disabled, 0, null, external));

        /// <summary>The contact as an onboarded external party: no internal user represents it, a CIAM binding, an email.</summary>
        public void MakeExternal(AccessRights? portalRights = AccessRights.Read)
        {
            Links.UsersByLink.Clear();
            ContactOid = PortalOid;
            ContactPlane = (int)IdentityPlaneMarker.External;
            PortalRights = portalRights;
        }

        /// <summary>A composition naming <paramref name="contactId"/> (its sets are this harness's <see cref="PortalRights"/>, asked
        /// through the stubbed <c>ContactRights</c>).</summary>
        private static ContactPrincipalComposition Composition(Guid contactId)
        {
            AccessibleRecordSet Empty(string entity) => new()
            {
                PrincipalKind = WorkforcePrincipalKind.ContactOnly,
                EntityType = entity,
                Rights = new Dictionary<Guid, AccessRights>(),
                Sources = new AccessibleRecordSetSources(false, true, false),
            };

            return new ContactPrincipalComposition(
                new CallerPrincipal { Plane = CallerPrincipalPlane.CiamContact, ContactId = contactId, ProjectAccess = [] },
                Empty(AccessibleRecordSetService.ProjectEntity), Empty(AccessibleRecordSetService.MatterEntity),
                Empty(AccessibleRecordSetService.WorkAssignmentEntity));
        }

        public Task<IReadOnlyList<string>> NotifyAsync(AssignTaskNotice? notice = null, ClaimsPrincipal? caller = null) =>
            Notifier.Object.NotifyAsync(new DefaultHttpContext { User = caller ?? Assigner() }, notice ?? Notice(), CancellationToken.None);

        public AssignTaskEmailJobHandler Handler(IIdempotencyService? idempotency = null) =>
            new(Notifier.Object, idempotency ?? Idempotency(), NullLogger<AssignTaskEmailJobHandler>.Instance);

        /// <summary>Nothing reached the assignee: no email (either path), no job, no bell.</summary>
        public void NothingWasSent()
        {
            UserSends.Should().BeEmpty();
            SystemSends.Should().BeEmpty();
            Queued.Should().BeEmpty();
            Bells.Should().BeEmpty();
        }
    }

    private static ClaimsPrincipal Assigner(string name = AssignerName, Guid? oid = null) => new(new ClaimsIdentity(
        [new Claim("oid", (oid ?? AssignerOid).ToString("D")), new Claim("name", name), new Claim("preferred_username", AssignerUpn)], "Test"));

    private static AssignTaskNotice Notice(string description = "Call the client <b>today</b>\nand confirm", DateOnly? due = null, bool noDue = false) =>
        new(Contact, TaskId, description, noDue ? null : due ?? new DateOnly(2026, 11, 15), new DecisionCoreRecord("sprk_matter", Matter));

    private static IIdempotencyService Idempotency() =>
        new IdempotencyService(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<IdempotencyService>.Instance);

    private static string MatterLink => $"{EnvironmentUrl}/main.aspx?etn=sprk_matter&id={Matter:D}&pagetype=entityrecord";

    private static string PortalLink => $"{PortalUrl}/?customer={CustomerKey}&record=matter:{Matter:D}";

    private static JobContract Job(AssignTaskEmailPayload payload) => new()
    {
        JobId = Guid.NewGuid(),
        JobType = AssignTaskNotifier.JobTypeName,
        IdempotencyKey = $"assign-task-{payload.TaskId:N}",
        Payload = JsonSerializer.SerializeToDocument(payload),
    };

    private static AssignTaskEmailPayload Payload() => new(TaskId, Contact, "sprk_matter", Matter, AssignerOid);

    // ── Must fire: an eligible internal assignee ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EligibleInternal_GetsOneEmailAsTheAssigner_WithTheMatterLink_WhatToDo_Due_AndAssignedBy_AllEncoded_InternalOnly()
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
        sent.IsInternalOnly.Should().BeTrue("review finding 2: the tracked row is never shown in a partner's message thread");
        sent.CorrelationId.Should().Be($"assign-task-{TaskId:N}");
        sent.Associations.Should().ContainSingle().Which.Should().Match<CommunicationAssociation>(a => a.EntityType == "sprk_matter" && a.EntityId == Matter);
        sent.Subject.Should().Be($"A task was assigned to you: {Label}");

        sent.Body.Should().Contain($"href=\"{System.Net.WebUtility.HtmlEncode(MatterLink)}\"");
        sent.Body.Should().Contain("Acme &lt;Holdings&gt; &amp; Co (MAT-0042)");
        sent.Body.Should().Contain("Call the client &lt;b&gt;today&lt;/b&gt;<br />and confirm");
        sent.Body.Should().NotContain("<b>today</b>", "every value is HTML-encoded");
        sent.Body.Should().Contain("15 November 2026");
        sent.Body.Should().Contain($"Assigned by {AssignerName}", "review finding 4");
        sent.Body.Should().NotContain(AssignerUpn, "the assigner is named by display name only");
        sent.Body.Should().NotContain("{{", "every placeholder is filled");

        h.Facts.Verify(f => f.ResolveAsync("sprk_matter", Matter, It.Is<IReadOnlyList<string>>(r => r.SequenceEqual(new[] { Address })), It.IsAny<CancellationToken>()),
            Times.Once, "the record's name is InquiryRecordFacts' decision (Standard, confirmed internal)");
        h.GateQuestions.Should().Contain($"wall sprk_matter {Matter} {LinkedUser}").And.Contain($"task {TaskId} {LinkedUser}");
        h.SystemSends.Should().BeEmpty("never both");
        h.Queued.Should().BeEmpty("never both");
    }

    [Fact]
    public async Task ARecordThatIsNotStandard_OrAnAddressNotConfirmedInternal_IsNotNamed_TheEmailStillGoes()
    {
        var h = new Harness { RecordLabel = null };

        (await h.NotifyAsync()).Should().BeEmpty();

        var sent = h.UserSends.Single();
        sent.Subject.Should().Be("A task was assigned to you");
        sent.Body.Should().NotContain("Acme").And.Contain($"href=\"{System.Net.WebUtility.HtmlEncode(MatterLink)}\"").And.Contain("Open in Spaarke");
        h.Bells.Single().GetAttributeValue<string>("body").Should().BeNull("the bell names the record only when the email may");
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

        await h.NotifyAsync(Notice(description: "Use {{Due}} and {{Record}} and {{AssignedBy}}"));

        h.UserSends.Single().Body.Should().Contain("Use {{Due}} and {{Record}} and {{AssignedBy}}", "the template is filled in one pass");
        h.UserSends.Single().Body.Should().Contain("15 November 2026");
    }

    [Fact]
    public async Task AnAssignerWithNoDisplayName_OrOneShapedLikeAnAddress_IsNotNamed()
    {
        var h = new Harness();

        await h.NotifyAsync(caller: Assigner(name: AssignerUpn));

        h.UserSends.Single().Body.Should().NotContain("Assigned by").And.NotContain(AssignerUpn);
    }

    [Fact]
    public void TheTemplate_IsEmbedded_AndBuildEmailSaysNothingButTheRecord_WhatToDo_Due_AndWhoAssignedIt()
    {
        var (subject, body) = AssignTaskNotifier.BuildEmail(
            "{{RecordNoun}}|{{Record}}|{{WhatToDo}}|{{Due}}|{{AssignedBy}}|{{Year}}",
            new AssignTaskNotifier.RecordFacts("Matter", "M", "https://x.example/a?b=1&c=2", "Open in Spaarke", "/rel"), "Do it", null, "A <B>", 2026);

        subject.Should().Be("A task was assigned to you: M");
        body.Should().Be("Matter|<a href=\"https://x.example/a?b=1&amp;c=2\" style=\"color: #0f6cbd;\">M</a>|Do it|No due date|"
                         + "<p style=\"margin: 20px 0 0; color: #616161; font-size: 13px; line-height: 1.4;\">Assigned by A &lt;B&gt;</p>|2026");
    }

    // ── Must fire: an onboarded external assignee with a grant (D-199) ───────────────────────────────────────────────────

    [Fact]
    public async Task OnboardedExternal_WithAGrant_GetsThePortalLink_NoModelDrivenLink_NoRecordName_NoBell_AndTheAssignerByDisplayNameOnly()
    {
        var h = new Harness();
        h.MakeExternal();

        var warnings = await h.NotifyAsync();

        warnings.Should().BeEmpty();
        // D-199 amendment: from the system mailbox, never as the assigner.
        h.UserSends.Should().BeEmpty("an external assignee is never emailed from the assigner's mailbox");
        var (sent, sender) = h.SystemSends.Should().ContainSingle().Which;
        sent.SendMode.Should().Be(SendMode.SharedMailbox);
        sent.FromMailbox.Should().Be(SystemMailbox, "Communication:DefaultMailbox");
        sent.To.Should().Equal(Address);
        sent.IsInternalOnly.Should().BeTrue();
        sent.CorrelationId.Should().Be($"assign-task-{TaskId:N}");
        sent.Subject.Should().Be("A task was assigned to you", "an external recipient is never told the record's name");
        sent.Body.Should().Contain($"href=\"{System.Net.WebUtility.HtmlEncode(PortalLink)}\"").And.Contain("Open in the Spaarke portal");
        sent.Body.Should().NotContain("main.aspx").And.NotContain(EnvironmentUrl, "never a model-driven link");
        sent.Body.Should().NotContain("Acme");
        sent.Body.Should().Contain($"Assigned by {AssignerName}").And.NotContain(AssignerUpn);
        sent.Subject.Should().NotContain("@");
        sent.Body.Should().NotContain("@", "no email address or UPN anywhere in the body");
        sender!.User.FindFirst("oid")!.Value.Should().Be(AssignerOid.ToString("D"), "the send path stamps sprk_sentby from it");
        sender.User.Claims.Should().ContainSingle("the sender context carries the oid and nothing else, never the assigner's UPN");
        h.Queued.Should().BeEmpty();
        h.Bells.Should().BeEmpty("an external contact has no model-driven app");
        h.Facts.Verify(f => f.ResolveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        h.GateQuestions.Should().Equal($"grants {Contact} sprk_matter {Matter}", $"portal {Contact} {TaskId}");
    }

    [Fact]
    public async Task OnboardedExternal_TheSystemSendFails_QueuesTheSystemMailboxJob_NeverTheAssignersMailbox()
    {
        var h = new Harness { SystemSendFault = new SdapProblemException("GRAPH_SEND_FAILED", "Send failed", "graph 500", 502) };
        h.MakeExternal();

        var warnings = await h.NotifyAsync();

        warnings.Should().BeEmpty("the email is still going, from the system mailbox, in a job");
        h.UserSends.Should().BeEmpty("a failed system send is retried from the system mailbox, never as the assigner");
        h.SystemSends.Should().ContainSingle();
        var job = h.Queued.Should().ContainSingle().Which;
        h.SystemSends.Clear();
        h.SystemSendFault = null;
        (await h.Handler().ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Completed);
        h.SystemSends.Should().ContainSingle().Which.Request.FromMailbox.Should().Be(SystemMailbox);
        h.UserSends.Should().BeEmpty();
    }

    [Fact]
    public async Task OnboardedExternal_WithNoSystemMailboxConfigured_SendsNothing_AndSaysSo_NeverFallsBackToTheAssigner_MustNotFire()
    {
        var h = new Harness();
        h.Communication.DefaultMailbox = " ";
        h.MakeExternal();

        var warnings = await h.NotifyAsync();

        warnings.Should().Equal(AssignTaskNotifier.EmailNotSentWarning);
        h.NothingWasSent();
    }

    [Fact]
    public async Task OnboardedExternal_ThroughTheRealSendPath_IsStoredInternalOnly_AndSentByTheAssigner()
    {
        var h = new Harness();
        h.MakeExternal();
        await h.NotifyAsync();
        var (request, sender) = h.SystemSends.Single();

        var dataverse = new Mock<ICommunicationDataverseService>();
        dataverse.Setup(d => d.QuerySystemUserByAzureAdOidAsync(AssignerOid.ToString("D"), It.IsAny<CancellationToken>())).ReturnsAsync(AssignerUser);
        var row = await OutboundCommunicationRow.WriteAsync(request, sender, dataverse.Object);

        row.GetAttributeValue<bool>("sprk_isinternalonly").Should().BeTrue();
        row.GetAttributeValue<EntityReference>("sprk_sentby")!.Id.Should().Be(AssignerUser, "sprk_sentby still records the assigner");
        row.GetAttributeValue<string>("sprk_correlationid").Should().Be($"assign-task-{TaskId:N}");
    }

    // ── Must not fire: the gate refuses (D-199) ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AWalledUser_GetsNoEmail_AndNoBell_MustNotFire()
    {
        var h = new Harness { Wall = SecureShareWallOutcome.Walled };

        (await h.NotifyAsync()).Should().Equal(AssignTaskRecipient.NoAccessWarning);

        h.NothingWasSent();
    }

    [Fact]
    public async Task ASecureMattersNonMember_WhoCannotReadTheTask_GetsNoEmail_MustNotFire()
    {
        var h = new Harness { Wall = SecureShareWallOutcome.NotWalled, TaskRights = AccessRights.None };

        (await h.NotifyAsync()).Should().Equal(AssignTaskRecipient.NoAccessWarning);

        h.NothingWasSent();
    }

    [Fact]
    public async Task AUserFlaggedExternal_OnARestrictedMatter_GetsNoEmail_MustNotFire()
    {
        var h = new Harness { Restricted = true };
        h.Links.UsersByLink.Clear();
        h.LinkUser(LinkedUser, external: true);

        (await h.NotifyAsync()).Should().Equal(AssignTaskRecipient.NoAccessWarning);

        h.NothingWasSent();
        h.GateQuestions.Should().Contain($"restricted sprk_matter {Matter}");
    }

    [Fact]
    public async Task AUserFlaggedExternal_OnAStandardMatter_IsEmailed_AndOneNotFlagged_IsNeverAskedAboutRestricted()
    {
        var flagged = new Harness { Restricted = false };
        flagged.Links.UsersByLink.Clear();
        flagged.LinkUser(LinkedUser, external: true);
        (await flagged.NotifyAsync()).Should().BeEmpty();
        flagged.UserSends.Should().ContainSingle();

        var ordinary = new Harness { Restricted = true };
        (await ordinary.NotifyAsync()).Should().BeEmpty();
        ordinary.GateQuestions.Should().NotContain(q => q.StartsWith("restricted", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("wall-unverifiable")]
    [InlineData("wall-throws")]
    [InlineData("task-unanswered")]
    [InlineData("restricted-unreadable")]
    [InlineData("contact-unreadable")]
    [InlineData("links-unreadable")]
    [InlineData("grants-incomplete")]
    [InlineData("portal-rule-undecided")]
    public async Task ACheckThatCannotBeMade_SendsNothing_FailClosed_MustNotFire(string fault)
    {
        var h = new Harness();
        switch (fault)
        {
            case "wall-unverifiable": h.Wall = SecureShareWallOutcome.Unverifiable; break;
            case "wall-throws": h.WallThrows = true; break;
            case "task-unanswered": h.TaskRights = null; break;
            case "restricted-unreadable":
                h.Restricted = null;
                h.Links.UsersByLink.Clear();
                h.LinkUser(LinkedUser, external: true);
                break;
            case "contact-unreadable":
                h.Identities.Setup(i => i.GetContactAsync(Contact, It.IsAny<CancellationToken>())).ReturnsAsync(ContactLookup.Failed);
                break;
            case "links-unreadable": h.Links.FailLinkRead = true; break;
            case "portal-rule-undecided":
                h.MakeExternal();
                h.PortalListsTask = null;
                break;
            default: h.MakeExternal(portalRights: null); break;
        }

        (await h.NotifyAsync()).Should().Equal(AssignTaskRecipient.UnverifiedWarning);

        h.NothingWasSent();
    }

    [Fact]
    public async Task AnExternalContact_WithAGrant_ButTheTaskWithheldByThePortalsEventsList_GetsNoEmail_InTheRequestOrTheJob_MustNotFire()
    {
        // Fix round 2, item 1: Read on the matter, but the task is also filed to a secure record the contact cannot read.
        var h = new Harness { PortalListsTask = false };
        h.MakeExternal();

        (await h.NotifyAsync()).Should().Equal(AssignTaskRecipient.NoAccessWarning);
        h.NothingWasSent();
        h.GateQuestions.Should().Equal($"grants {Contact} sprk_matter {Matter}", $"portal {Contact} {TaskId}");

        (await h.Handler().ProcessAsync(Job(Payload()), CancellationToken.None)).Status.Should().Be(JobStatus.Completed, "a refusal is final");
        h.SystemSends.Should().BeEmpty();
    }

    // ── r3 task 079 (#2491): a WorkforceHome assignee AS THE REAL STORE READS IT ─────────────────────────────────────────
    //
    // The harness's GetContactAsync row is the real store's shape: ContactSelect carries no sprk_externaltenantid (by design),
    // so the row has no tenant. The stored home directory comes only from the home-tenant read, in its own query.

    private static readonly Guid HomeTenant = Guid.Parse("c1680079-0000-4000-8000-0000000000e1");
    private static readonly Guid HomeOid = Guid.Parse("c1680079-0000-4000-8000-0000000000e2");
    private static readonly Guid LicensedUser = Guid.Parse("c1680079-0000-4000-8000-0000000000e3");

    /// <summary>The contact as an onboarded WorkforceHome party with a grant; its row carries no tenant (the real store's shape).</summary>
    private static void MakeHome(Harness h, HomeTenantLookup? tenant = null, HomeLinkLookup? link = null)
    {
        h.MakeExternal();
        h.ContactOid = HomeOid;
        h.ContactPlane = (int)IdentityPlaneMarker.WorkforceHome;
        h.Identities.Setup(i => i.ReadHomeTenantAsync(Contact, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant ?? HomeTenantLookup.FromStored(HomeTenant.ToString("D")));
        h.Identities.Setup(i => i.FindHomeLinkedSystemUsersAsync(HomeTenant, HomeOid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(link ?? HomeLinkLookup.Of());
    }

    [Fact]
    public async Task AWorkforceHomeAssignee_AsTheRealStoreReadsIt_NotLinked_WithAGrant_GetsOneEmail()
    {
        var h = new Harness();
        MakeHome(h);

        (await h.NotifyAsync()).Should().BeEmpty();

        h.SystemSends.Should().ContainSingle().Which.Request.To.Should().Equal(Address);
        h.UserSends.Should().BeEmpty();
        h.Identities.Verify(i => i.ReadHomeTenantAsync(Contact, It.IsAny<CancellationToken>()), Times.Once);
        h.Identities.Verify(i => i.FindHomeLinkedSystemUsersAsync(HomeTenant, HomeOid, It.IsAny<CancellationToken>()), Times.Once,
            "the stored-link read is asked with the STORED tenant (the row has none) and the bound oid");
        h.GateQuestions.Should().Equal($"grants {Contact} sprk_matter {Matter}", $"portal {Contact} {TaskId}");
    }

    [Fact]
    public async Task AWorkforceHomeAssignee_AsTheRealStoreReadsIt_WhoseHomePairALicensedUserCarries_IsUnverified_MustNotFire()
    {
        var h = new Harness();
        MakeHome(h, link: HomeLinkLookup.Of(new HomeLinkRow(LicensedUser, false, HomeTenant.ToString("D"), HomeOid.ToString("D"))));

        (await h.Gate.Object.DecideAsync(Contact, new DecisionCoreRecord("sprk_matter", Matter), TaskId, AssignerOid, CancellationToken.None))
            .Should().Be(AssignTaskRecipient.Refused(AssignTaskRefusal.Unverified));
        (await h.NotifyAsync()).Should().Equal(AssignTaskRecipient.UnverifiedWarning);

        h.NothingWasSent();
        h.GateQuestions.Should().BeEmpty("the sign-in is the licensed user's, so the contact's grants are never asked about");
    }

    public static TheoryData<string, HomeTenantLookup?, bool> UnreadableTenants => new()
    {
        { "the stored tenant reads empty (null, masked or unparsable)", HomeTenantLookup.FromStored(null), false },
        { "the stored tenant is not a GUID", HomeTenantLookup.FromStored("not-a-guid"), false },
        { "the tenant read failed", HomeTenantLookup.Failed, false },
        { "the tenant column is not provisioned", HomeTenantLookup.ColumnMissing, false },
        { "the tenant read throws", null, true },
    };

    [Theory]
    [MemberData(nameof(UnreadableTenants))]
    public async Task AWorkforceHomeAssignee_WhoseStoredTenantCannotBeRead_IsUnverified_NeverNotLinked_MustNotFire(
        string because, HomeTenantLookup? tenant, bool throws)
    {
        var h = new Harness();
        MakeHome(h, tenant);
        if (throws)
        {
            h.Identities.Setup(i => i.ReadHomeTenantAsync(Contact, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("tenant read down"));
        }

        (await h.Gate.Object.DecideAsync(Contact, new DecisionCoreRecord("sprk_matter", Matter), TaskId, AssignerOid, CancellationToken.None))
            .Should().Be(AssignTaskRecipient.Refused(AssignTaskRefusal.Unverified), because);
        (await h.NotifyAsync()).Should().Equal(new[] { AssignTaskRecipient.UnverifiedWarning }, because);

        h.NothingWasSent();
        h.GateQuestions.Should().BeEmpty(because);
        h.Identities.Verify(i => i.FindHomeLinkedSystemUsersAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never, "no stored-link read without a stored tenant");
    }

    [Fact]
    public async Task ACiamAssignee_IsDecidedAsToday_WithoutTheHomeReads()
    {
        var h = new Harness();
        h.MakeExternal();

        (await h.NotifyAsync()).Should().BeEmpty();

        h.SystemSends.Should().ContainSingle();
        h.Identities.Verify(i => i.ReadHomeTenantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Identities.Verify(i => i.FindHomeLinkedSystemUsersAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnExternalContact_WithNoGrant_GetsNoEmail_MustNotFire()
    {
        var h = new Harness();
        h.MakeExternal(portalRights: AccessRights.None);

        (await h.NotifyAsync()).Should().Equal(AssignTaskRecipient.NoAccessWarning);

        h.NothingWasSent();
    }

    [Theory]
    [InlineData("no-binding")]
    [InlineData("workforce-binding")]
    [InlineData("no-email")]
    public async Task AnExternalContact_NotOnboarded_GetsNoEmail_EvenWithAGrant_MustNotFire(string why)
    {
        var h = new Harness();
        h.MakeExternal();
        switch (why)
        {
            case "no-binding": h.ContactOid = null; h.ContactPlane = null; break;
            case "workforce-binding": h.ContactPlane = (int)IdentityPlaneMarker.Workforce; break;
            default: h.ContactEmail = null; break;
        }

        (await h.NotifyAsync()).Should().Equal(AssignTaskRecipient.NoAccessWarning);

        h.NothingWasSent();
        h.GateQuestions.Should().BeEmpty("the grant is not even asked about");
    }

    [Fact]
    public async Task AnExternalContact_WhoIsTheAssigner_GetsNoEmail_MustNotFire()
    {
        var h = new Harness();
        h.MakeExternal();

        (await h.NotifyAsync(caller: Assigner(oid: PortalOid))).Should().Equal(AssignTaskRecipient.NoAccessWarning);

        h.NothingWasSent();
    }

    [Fact]
    public async Task AContactMappingToSeveralUsers_GetsNoEmail_AndNoBell_MustNotFire()
    {
        var h = new Harness();
        h.LinkUser(Guid.NewGuid());

        (await h.NotifyAsync()).Should().Equal(AssignTaskRecipient.SeveralUsersWarning);

        h.NothingWasSent();
    }

    [Fact]
    public async Task OnlyADisabledLinkedUser_GetsNoEmail_AndNoBell_MustNotFire()
    {
        var h = new Harness();
        h.Links.UsersByLink.Clear();
        h.LinkUser(Guid.NewGuid(), disabled: true);

        (await h.NotifyAsync()).Should().Equal(AssignTaskRecipient.NoAccessWarning);

        h.NothingWasSent();
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("missing")]
    public async Task AnInactiveOrMissingContact_GetsNoEmail_MustNotFire(string state)
    {
        var h = new Harness();
        if (state == "inactive") h.ContactState = 1; else h.ContactMissing = true;

        (await h.NotifyAsync()).Should().Equal(AssignTaskRecipient.NoAccessWarning);

        h.NothingWasSent();
    }

    // ── The fallback: the system mailbox, from a job, behind the same gate (D-199 Q2) ─────────────────────────────────────

    [Theory]
    [InlineData("graph")]
    [InlineData("consent")]
    [InlineData("timeout")]
    public async Task TheAssignersSendFails_QueuesOneSystemMailboxJob_WithIdsOnly_AndTheJobSendsItOnce_AsTheAssigner(string failure)
    {
        var h = new Harness();
        switch (failure)
        {
            case "graph": h.UserSendFault = new SdapProblemException("GRAPH_SEND_FAILED", "Send failed", "graph 500", 502); break;
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
        payload.Should().NotContain(Address).And.NotContain("Call the client").And.NotContain(AssignerName, "the payload holds ids only");
        payload.Should().Contain(AssignerOid.ToString("D"));

        // The job: one send from the approved system mailbox; a redelivery sends nothing more.
        var handler = h.Handler();
        (await handler.ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Completed);
        (await handler.ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Completed);

        var (system, sender) = h.SystemSends.Should().ContainSingle().Which;
        system.SendMode.Should().Be(SendMode.SharedMailbox);
        system.FromMailbox.Should().Be(SystemMailbox);
        system.To.Should().Equal(Address);
        system.IsInternalOnly.Should().BeTrue();
        system.CorrelationId.Should().Be($"assign-task-{TaskId:N}");
        system.Body.Should().Contain("Call the client").And.Contain("15 November 2026").And.Contain(System.Net.WebUtility.HtmlEncode(MatterLink));
        system.Body.Should().Contain($"Assigned by {AssignerName}", "review finding 4: the system-mailbox email names who assigned it");
        sender!.User.FindFirst("oid")!.Value.Should().Be(AssignerOid.ToString("D"), "the send path stamps sprk_sentby from it");
        sender.User.Claims.Should().ContainSingle("the sender context carries the oid and nothing else");
    }

    [Fact]
    public async Task TheFallback_NeverSendsToARecipientTheGateRefuses_InTheRequestOrAtSendTime_MustNotFire()
    {
        // In the request: a refused assignee is never queued for the system mailbox either.
        var refused = new Harness { Wall = SecureShareWallOutcome.Walled, UserSendFault = new HttpRequestException("graph 500") };
        (await refused.NotifyAsync()).Should().Equal(AssignTaskRecipient.NoAccessWarning);
        refused.NothingWasSent();

        // At send time: the assignee was let through, the assigner's send failed, and by the time the job runs the gate refuses.
        var h = new Harness { UserSendFault = new HttpRequestException("graph 500") };
        await h.NotifyAsync();
        var job = h.Queued.Single();
        h.Wall = SecureShareWallOutcome.Walled;

        (await h.Handler().ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Completed, "a refusal is final, not retried");
        h.SystemSends.Should().BeEmpty();

        // And an external contact whose grant was removed before the job ran.
        // (An external assignee's in-request send is the system mailbox, D-199 amendment, so it is the SYSTEM send that fails here.)
        var x = new Harness { SystemSendFault = new HttpRequestException("graph 500") };
        x.MakeExternal();
        await x.NotifyAsync();
        x.SystemSends.Clear();
        x.PortalRights = AccessRights.None;
        (await x.Handler().ProcessAsync(x.Queued.Single(), CancellationToken.None)).Status.Should().Be(JobStatus.Completed);
        x.SystemSends.Should().BeEmpty();
        x.UserSends.Should().BeEmpty();
    }

    [Fact]
    public async Task TheJob_AGateThatCannotCheck_SendsNothing_AndIsRetried_MustNotFire()
    {
        var h = new Harness { TaskRights = null };

        (await h.Handler().ProcessAsync(Job(Payload()), CancellationToken.None)).Status.Should().Be(JobStatus.Failed);

        h.SystemSends.Should().BeEmpty();
    }

    [Theory]
    [InlineData("reassigned")]
    [InlineData("closed")]
    [InlineData("contact-inactive")]
    public async Task TheJob_RereadsTheTaskAndTheContact_AReassignedOrClosedTask_OrAnInactiveContact_GetsNoEmail_MustNotFire(string change)
    {
        var h = new Harness();
        switch (change)
        {
            case "reassigned": h.TaskAssignee = Guid.NewGuid(); break;
            case "closed": h.TaskState = 1; break;
            default: h.ContactState = 1; break;
        }

        (await h.Handler().ProcessAsync(Job(Payload()), CancellationToken.None)).Status.Should().Be(JobStatus.Completed);

        h.SystemSends.Should().BeEmpty();
    }

    [Fact]
    public async Task TheJob_SendsToAnOnboardedExternalWithAGrant_ThePortalLink()
    {
        var h = new Harness();
        h.MakeExternal();

        (await h.Handler().ProcessAsync(Job(Payload()), CancellationToken.None)).Status.Should().Be(JobStatus.Completed);

        var (system, _) = h.SystemSends.Should().ContainSingle().Which;
        system.Body.Should().Contain(System.Net.WebUtility.HtmlEncode(PortalLink)).And.NotContain("main.aspx").And.NotContain("Acme");
        h.Bells.Should().BeEmpty();
    }

    // ── Fix round 2, review K2: the job follows the task's CURRENT filing, not the queued record ──────────────────────────

    [Fact]
    public async Task TheJob_ATaskReFiledToARecordTheExternalContactCannotSee_GetsNoEmail_MustNotFire()
    {
        var h = new Harness();
        h.MakeExternal();
        h.PortalRightsByRecord[OtherMatter] = AccessRights.None; // a grant on the queued matter only
        h.TaskFiling["sprk_regardingmatter"] = new EntityReference("sprk_matter", OtherMatter);

        (await h.Handler().ProcessAsync(Job(Payload()), CancellationToken.None)).Status.Should().Be(JobStatus.Completed, "a refusal is final");

        h.SystemSends.Should().BeEmpty();
        h.GateQuestions.Should().Equal($"grants {Contact} sprk_matter {OtherMatter}");
    }

    [Fact]
    public async Task TheJob_ATaskReFiledToARecordTheInternalUserIsWalledOff_GetsNoEmail_MustNotFire()
    {
        var h = new Harness();
        h.WalledRecords.Add(OtherMatter);
        h.TaskFiling["sprk_regardingmatter"] = null;
        h.TaskFiling["sprk_regardingproject"] = new EntityReference("sprk_project", OtherMatter);

        (await h.Handler().ProcessAsync(Job(Payload()), CancellationToken.None)).Status.Should().Be(JobStatus.Completed);

        h.SystemSends.Should().BeEmpty();
        h.GateQuestions.Should().Contain($"wall sprk_project {OtherMatter} {LinkedUser}");
    }

    [Fact]
    public async Task TheJob_ATaskReFiledToARecordTheContactCanSee_LinksThatRecord_NotTheQueuedOne()
    {
        var h = new Harness();
        h.MakeExternal();
        h.PortalRightsByRecord[OtherMatter] = AccessRights.Read;
        h.TaskFiling["sprk_regardingmatter"] = new EntityReference("sprk_matter", OtherMatter);

        (await h.Handler().ProcessAsync(Job(Payload()), CancellationToken.None)).Status.Should().Be(JobStatus.Completed);

        var (system, _) = h.SystemSends.Should().ContainSingle().Which;
        system.Body.Should().Contain(System.Net.WebUtility.HtmlEncode($"{PortalUrl}/?customer={CustomerKey}&record=matter:{OtherMatter:D}"))
            .And.NotContain(Matter.ToString("D"));
    }

    [Fact]
    public async Task TheJob_ATaskStillFiledToTheQueuedRecord_AndAlsoToAnotherRoot_StaysAboutTheQueuedRecord()
    {
        var h = new Harness();
        h.MakeExternal();
        h.TaskFiling["sprk_regardingproject"] = new EntityReference("sprk_project", OtherMatter);

        (await h.Handler().ProcessAsync(Job(Payload()), CancellationToken.None)).Status.Should().Be(JobStatus.Completed);

        h.SystemSends.Should().ContainSingle().Which.Request.Body.Should().Contain(System.Net.WebUtility.HtmlEncode(PortalLink));
        h.GateQuestions.Should().Equal($"grants {Contact} sprk_matter {Matter}", $"portal {Contact} {TaskId}");
    }

    [Fact]
    public async Task TheJob_ATaskReFiledUnderSeveralOtherRecords_GetsNoEmail_MustNotFire()
    {
        var h = new Harness();
        h.TaskFiling["sprk_regardingmatter"] = new EntityReference("sprk_matter", OtherMatter);
        h.TaskFiling["sprk_regardingproject"] = new EntityReference("sprk_project", Guid.NewGuid());

        (await h.Handler().ProcessAsync(Job(Payload()), CancellationToken.None)).Status.Should().Be(JobStatus.Completed);

        h.SystemSends.Should().BeEmpty();
        h.GateQuestions.Should().BeEmpty("which record the email is about cannot be told, so the gate is not asked");
    }

    [Fact]
    public async Task TheJob_ATaskNoLongerFiledUnderAnyRecord_IsNotEmailedToAnExternalContact_MustNotFire()
    {
        var h = new Harness();
        h.MakeExternal();
        h.TaskFiling["sprk_regardingmatter"] = null;

        (await h.Handler().ProcessAsync(Job(Payload()), CancellationToken.None)).Status.Should().Be(JobStatus.Completed);

        h.SystemSends.Should().BeEmpty("an external grant names a record; a task under none has no portal page");
    }

    [Fact]
    public async Task TheJob_SendsNothing_WhenAnEmailForTheTaskIsAlreadyRecorded()
    {
        var h = new Harness { AlreadyRecorded = true };

        (await h.Handler().ProcessAsync(Job(Payload()), CancellationToken.None)).Status.Should().Be(JobStatus.Completed);

        h.SystemSends.Should().BeEmpty();
    }

    [Theory]
    [InlineData("graph-5xx", JobStatus.Failed)]
    [InlineData("timeout", JobStatus.Failed)]
    [InlineData("sender-not-approved-4xx", JobStatus.Poisoned)]
    [InlineData("owner-refusal-409", JobStatus.Poisoned)]
    [InlineData("request-timeout-408", JobStatus.Failed)]
    [InlineData("graph-throttled-429", JobStatus.Failed)]
    [InlineData("daily-send-limit-429", JobStatus.Failed)]
    [InlineData("invalid-operation", JobStatus.Poisoned)]
    public async Task TheJob_ATransientFaultIsRetried_AFaultNoRetryCanFixIsPoisoned(string fault, JobStatus expected)
    {
        var h = new Harness();
        Exception ex = fault switch
        {
            "graph-5xx" => new SdapProblemException("GRAPH_SEND_FAILED", "Send failed", "x", 502),
            "timeout" => new TimeoutException("graph"),
            "sender-not-approved-4xx" => new SdapProblemException("INVALID_SENDER", "Invalid Sender", "x", 400),
            "owner-refusal-409" => new SdapProblemException("OWNER_REFUSED", "Refused", "x", 409),
            "request-timeout-408" => new SdapProblemException("GRAPH_SEND_FAILED", "Request Timeout", "x", 408),
            "graph-throttled-429" => new SdapProblemException("GRAPH_SEND_FAILED", "Too Many Requests", "x", 429),
            "daily-send-limit-429" => new SdapProblemException("DAILY_SEND_LIMIT_REACHED", "Daily send limit reached", "x", 429),
            _ => new InvalidOperationException("DefaultMailbox is not set"),
        };
        h.Notifier.Setup(n => n.SendAsSystemAsync(It.IsAny<SendCommunicationRequest>(), It.IsAny<HttpContext?>(), It.IsAny<CancellationToken>())).ThrowsAsync(ex);

        (await h.Handler().ProcessAsync(Job(Payload()), CancellationToken.None)).Status.Should().Be(expected);
    }

    [Fact]
    public async Task TheJob_AnUnreadablePayload_IsPoisoned()
    {
        var h = new Harness();

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

    // ── Read-back through the REAL send path (review findings 2 and 4) ───────────────────────────────────────────────────

    [Fact]
    public async Task TheSystemMailboxEmail_ThroughTheRealSendPath_IsStoredInternalOnly_AndSentByTheAssigner()
    {
        var h = new Harness { UserSendFault = new HttpRequestException("graph 500") };
        await h.NotifyAsync();
        await h.Handler().ProcessAsync(h.Queued.Single(), CancellationToken.None);
        var (request, sender) = h.SystemSends.Single();

        var dataverse = new Mock<ICommunicationDataverseService>();
        dataverse.Setup(d => d.QuerySystemUserByAzureAdOidAsync(AssignerOid.ToString("D"), It.IsAny<CancellationToken>())).ReturnsAsync(AssignerUser);
        var row = await OutboundCommunicationRow.WriteAsync(request, sender, dataverse.Object);

        row.GetAttributeValue<bool>("sprk_isinternalonly").Should().BeTrue("review finding 2");
        var sentBy = row.GetAttributeValue<EntityReference>("sprk_sentby");
        sentBy.Should().NotBeNull("review finding 4: the system-mailbox row names who assigned the task");
        sentBy!.Id.Should().Be(AssignerUser);
        row.GetAttributeValue<string>("sprk_correlationid").Should().Be($"assign-task-{TaskId:N}");
    }

    // ── Must not fire: no address ──────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-address")]
    public async Task AnInternalContactWithNoUsableEmail_GetsNoEmail_AndTheWarningSaysSo_ButTheBellGoes_MustNotFire(string? email)
    {
        var h = new Harness { ContactEmail = email };

        var warnings = await h.NotifyAsync();

        warnings.Should().Equal(AssignTaskNotifier.EmailMissingWarning);
        h.UserSends.Should().BeEmpty();
        h.Queued.Should().BeEmpty();
        h.Bells.Should().ContainSingle("the bell does not depend on the email");
    }

    // ── The bell ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheInternalUser_GetsOneBell_WithTheMattersLink_AndNoDecisionContent()
    {
        var h = new Harness();

        await h.NotifyAsync(Notice(description: "Secret strategy text"));

        var bell = h.Bells.Should().ContainSingle().Which;
        bell.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(LinkedUser);
        bell.GetAttributeValue<string>("title").Should().Be("A task was assigned to you");
        bell.GetAttributeValue<string>("body").Should().Be($"On matter {Label}.");
        var data = bell.GetAttributeValue<string>("data");
        var action = JsonDocument.Parse(data).RootElement;
        action.GetProperty("actionUrl").GetString().Should().Be($"/main.aspx?etn=sprk_matter&id={Matter:D}&pagetype=entityrecord",
            "the matter's model-driven link (the GrantAccessNotifier form: a same-origin path)");
        action.GetProperty("actions")[0].GetProperty("title").GetString().Should().Be("Open matter");
        (bell.GetAttributeValue<string>("title") + bell.GetAttributeValue<string>("body") + data).Should().NotContain("Secret strategy text");
    }

    [Fact]
    public async Task ABellFault_NeverThrows_AndTheEmailIsUnaffected_MustNotFire()
    {
        var h = new Harness { BellFault = new TimeoutException("dataverse") };

        (await h.NotifyAsync()).Should().BeEmpty();

        h.UserSends.Should().ContainSingle();
    }

    // ── Security: ids only in the logs ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheLogs_CarryRecordIds_NeverTheAddress_OrAFaultsMessage()
    {
        var h = new Harness { UserSendFault = new HttpRequestException($"graph 500 for {Address}") };
        await h.NotifyAsync();
        var g = new Harness { ContactEmail = "" };
        await g.NotifyAsync();
        var w = new Harness { Wall = SecureShareWallOutcome.Walled };
        await w.NotifyAsync();

        var all = h.Log.Messages.Concat(g.Log.Messages).Concat(w.GateLog.Messages).ToList();
        all.Should().NotBeEmpty().And.OnlyContain(m => !m.Contains(Address, StringComparison.OrdinalIgnoreCase));
        h.Log.Exceptions.Should().OnlyContain(e => e == null, "a fault is logged by its type and code, never the exception object");
        h.Log.Messages.Should().Contain(m => m.Contains(TaskId.ToString(), StringComparison.Ordinal));
    }

    // ── DecisionCommitFollowUps hands every notice to the notifier (review finding 3) ────────────────────────────────────

    [Fact]
    public async Task NotifyAssigneesAsync_HandsEachNoticeToTheNotifier_AndAFaultBecomesTheNotSentWarning()
    {
        var h = new Harness();
        var calls = new List<AssignTaskNotice>();
        h.Notifier.Setup(n => n.NotifyAsync(It.IsAny<HttpContext>(), It.IsAny<AssignTaskNotice>(), It.IsAny<CancellationToken>()))
            .Returns((HttpContext _, AssignTaskNotice n, CancellationToken _) =>
            {
                calls.Add(n);
                return calls.Count == 1
                    ? Task.FromResult<IReadOnlyList<string>>([AssignTaskRecipient.NoAccessWarning])
                    : Task.FromException<IReadOnlyList<string>>(new InvalidOperationException("boom"));
            });
        var followUps = new DecisionCommitFollowUps(
            new SignalEvaluationTrigger(h.Jobs.Object, NullLogger<SignalEvaluationTrigger>.Instance),
            new SignalSuppressionService(h.Entities.Object,
                new OntologyWriterDataverseClient(() => Mock.Of<Microsoft.PowerPlatform.Dataverse.Client.IOrganizationServiceAsync2>(), NullLogger<OntologyWriterDataverseClient>.Instance),
                TimeProvider.System, new ConfigurationBuilder().Build(), Mock.Of<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
                NullLogger<SignalSuppressionService>.Instance),
            h.Entities.Object, NullLogger<DecisionCommitFollowUps>.Instance, h.Notifier.Object);
        var second = Notice() with { AssigneeContactId = Guid.NewGuid() };

        var warnings = await followUps.NotifyAssigneesAsync(new DefaultHttpContext(), [Notice(), second], CancellationToken.None);

        calls.Should().HaveCount(2);
        warnings.Should().Equal(AssignTaskRecipient.NoAccessWarning, AssignTaskNotifier.EmailNotSentWarning);
    }

    internal sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public List<Exception?> Exceptions { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }
}
