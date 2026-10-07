// unified-access-control-r2 task 152 — server-generated to-dos name a PERSON (#1044 split agreed with
// word-add-in-r1), and the job runs as an IScheduledJob (ADR-052 §1 migration).
//
// Created By is the BFF application user for every to-do this job creates, so the people-targeting surface can only
// find the to-do through sprk_assignedto. Precedence (AssignedToDefaults): a supplied assignee is kept; else the
// TRIGGERING person's contact; else the regarding parent's responsible internal contact (sprk_assignedtointernal,
// then sprk_assignedattorney1); else blank + todo_unassigned. Never a team, never an email match.

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Workspace;
using Sprk.Bff.Api.Tests.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Workspace;

[Trait("status", "task-152-uac-r2")]
public class TodoGenerationAssignedToTests
{
    private static readonly Guid MatterId = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid EventId = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly Guid InvoiceId = Guid.Parse("33333333-0000-0000-0000-000000000003");
    private static readonly Guid InternalContact = Guid.Parse("44444444-0000-0000-0000-000000000004");
    private static readonly Guid AttorneyContact = Guid.Parse("55555555-0000-0000-0000-000000000005");
    private static readonly Guid TriggeringContact = Guid.Parse("66666666-0000-0000-0000-000000000006");
    private static readonly Guid SuppliedContact = Guid.Parse("77777777-0000-0000-0000-000000000007");

    private readonly Mock<IDataverseService> _dataverse = new(MockBehavior.Loose);
    private readonly Mock<IEventDataverseService> _events = new(MockBehavior.Loose);
    private readonly Mock<ICommunicationDataverseService> _comm = new(MockBehavior.Loose);
    private readonly CapturingLogger<TodoGenerationService> _logger = new();
    private readonly List<Entity> _created = new();
    private readonly Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble _ownership = new();

    public TodoGenerationAssignedToTests()
    {
        _comm.Setup(c => c.QueryRecordTypeRefAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Entity?)null);
        _dataverse.Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => _created.Add(e))
            .ReturnsAsync(Guid.NewGuid());
        // No duplicates exist (idempotency query returns nothing).
        _dataverse.Setup(d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_todo"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        _events.Setup(e => e.QueryEventsAsync(
                It.IsAny<int?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<int>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<EventEntity>(), 0));
    }

    private TodoGenerationService CreateService(bool eventSourced = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_dataverse.Object);
        services.AddSingleton(_events.Object);
        services.AddSingleton(_comm.Object);
        // Task 146 (merged after 152): every generated to-do's OWNER comes from the one resolver; the double owns it by
        // a fixed team so these tests stay about the person it is FOR (sprk_assignedto).
        services.AddSingleton<Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver>(_ownership);
        var svc = new TodoGenerationService(
            services.BuildServiceProvider(),
            _logger,
            Options.Create(new TodoGenerationOptions { EnableEventSourcedGeneration = eventSourced }));

        typeof(TodoGenerationService).GetField("_dataverse", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(svc, _dataverse.Object);
        typeof(TodoGenerationService).GetField("_events", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(svc, _events.Object);
        svc.SetRegardingBuilderForTest(new TodoRegardingBuilder(
            _comm.Object,
            Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
            Mock.Of<ILogger<TodoRegardingBuilder>>()));
        svc.SetOwnershipResolverForTest(_ownership);
        return svc;
    }

    private void ParentHas(string entity, Guid id, Guid? internalContact, Guid? attorney)
    {
        var row = new Entity(entity, id);
        if (internalContact is { } i) row["sprk_assignedtointernal"] = new EntityReference("contact", i);
        if (attorney is { } a) row["sprk_assignedattorney1"] = new EntityReference("contact", a);
        _dataverse.Setup(d => d.RetrieveAsync(entity, id, It.IsAny<string[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(row);
    }

    private static Guid? AssignedTo(Entity todo) =>
        todo.Contains("sprk_assignedto") && todo["sprk_assignedto"] is EntityReference r ? r.Id : null;

    // ── The precedence rule ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SuppliedAssignee_IsNeverOverwritten()
    {
        ParentHas("sprk_matter", MatterId, InternalContact, AttorneyContact);

        await CreateService().CreateTodoAsync("Budget Alert: X", "sprk_matter", MatterId, "X",
            assignedToContactId: SuppliedContact, triggeringContactId: TriggeringContact, ct: CancellationToken.None);

        AssignedTo(_created.Single()).Should().Be(SuppliedContact);
    }

    [Fact]
    public async Task TriggeringPerson_WinsOverTheParent()
    {
        ParentHas("sprk_matter", MatterId, InternalContact, AttorneyContact);

        await CreateService().CreateTodoAsync("Budget Alert: X", "sprk_matter", MatterId, "X",
            triggeringContactId: TriggeringContact, ct: CancellationToken.None);

        AssignedTo(_created.Single()).Should().Be(TriggeringContact);
    }

    [Fact]
    public async Task NoTriggeringPerson_ParentInternalContactThenAttorney()
    {
        ParentHas("sprk_matter", MatterId, internalContact: null, attorney: AttorneyContact);

        await CreateService().CreateTodoAsync("Budget Alert: X", "sprk_matter", MatterId, "X", ct: CancellationToken.None);

        AssignedTo(_created.Single()).Should().Be(AttorneyContact, "sprk_assignedtointernal is empty, so sprk_assignedattorney1");
    }

    [Fact]
    public async Task NobodyToName_LeftBlank_TodoUnassignedLogged_NoTeamNoEmail()
    {
        ParentHas("sprk_matter", MatterId, internalContact: null, attorney: null);

        await CreateService().CreateTodoAsync("Budget Alert: X", "sprk_matter", MatterId, "X", ct: CancellationToken.None);

        var todo = _created.Single();
        todo.Contains("sprk_assignedto").Should().BeFalse();
        todo.Attributes.Where(a => a.Key != "ownerid").Select(a => a.Value).OfType<EntityReference>()
            .Should().NotContain(r => r.LogicalName == "team", "a team is never named as the person a to-do is for");
        todo.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(
            Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble.DefaultTeamId,
            "the OWNER is the record's team (task 146) — a different column from the person it is for");
        _logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("todo_unassigned"));
        _dataverse.Verify(d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "contact"), It.IsAny<CancellationToken>()),
            Times.Never, "no email/name lookup of a contact");
    }

    // ── Each of the five rules ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rule1_OverdueEvent_AssignsTheEventsResponsibleContact()
    {
        _events.Setup(e => e.QueryEventsAsync(It.IsAny<int?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.Is<DateTime?>(d => d == null), It.Is<DateTime?>(d => d != null), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<int>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { new EventEntity { Id = EventId, Name = "Filing", StatusCode = EventStatusCode.Open, DueDate = DateTime.UtcNow.Date.AddDays(-3) } }, 1));
        ParentHas("sprk_event", EventId, InternalContact, AttorneyContact);

        await CreateService(eventSourced: true).RunGenerationPassAsync(CancellationToken.None);

        AssignedTo(_created.Single(t => ((string)t["sprk_name"]).StartsWith("Overdue:"))).Should().Be(InternalContact);
    }

    [Fact]
    public async Task Rule2_BudgetAlert_AssignsTheMattersResponsibleContact()
    {
        _dataverse.Setup(d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_matter"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { new("sprk_matter", MatterId) { ["sprk_name"] = "Acme" } }));
        ParentHas("sprk_matter", MatterId, InternalContact, null);

        await CreateService().RunGenerationPassAsync(CancellationToken.None);

        AssignedTo(_created.Single(t => ((string)t["sprk_name"]).StartsWith("Budget Alert:"))).Should().Be(InternalContact);
    }

    [Fact]
    public async Task Rule3_Deadline_AssignsTheEventsResponsibleContact()
    {
        _events.Setup(e => e.QueryEventsAsync(It.IsAny<int?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.Is<DateTime?>(d => d != null), It.Is<DateTime?>(d => d != null), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<int>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { new EventEntity { Id = EventId, Name = "Hearing", StatusCode = EventStatusCode.Open, DueDate = DateTime.UtcNow.Date.AddDays(4) } }, 1));
        ParentHas("sprk_event", EventId, null, AttorneyContact);

        await CreateService(eventSourced: true).RunGenerationPassAsync(CancellationToken.None);

        AssignedTo(_created.Single(t => ((string)t["sprk_name"]).StartsWith("Deadline:"))).Should().Be(AttorneyContact);
    }

    [Fact]
    public async Task Rule4_PendingInvoice_ParentWithoutResponsibleColumns_LeftBlankWhenNoCoreRecordStamped()
    {
        // sprk_invoice carries no responsible-contact columns; the rule defers to the core record FR-26 stamps on the
        // to-do. With no stamp (the inert core-ancestor fixture), nobody can be named: blank + todo_unassigned.
        _dataverse.Setup(d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_invoice"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { new("sprk_invoice", InvoiceId) { ["sprk_name"] = "INV-005" } }));

        await CreateService().RunGenerationPassAsync(CancellationToken.None);

        var todo = _created.Single(t => ((string)t["sprk_name"]).StartsWith("Invoice Pending:"));
        todo.Contains("sprk_assignedto").Should().BeFalse();
        _dataverse.Verify(d => d.RetrieveAsync("sprk_invoice", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never,
            "the invoice has no responsible columns to read");
        _logger.Entries.Should().Contain(e => e.Message.Contains("todo_unassigned"));
    }

    [Fact]
    public async Task Rule4_PendingInvoice_UsesTheStampedCoreMatter()
    {
        var todoWithStamp = new Entity("sprk_todo") { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId) };
        ParentHas("sprk_matter", MatterId, InternalContact, null);

        // The core-ancestor path in isolation: an invoice parent defers to the stamped core matter.
        var outcome = await Sprk.Bff.Api.Services.Dataverse.AssignedToDefaults.ApplyAsync(
            _dataverse.Object, todoWithStamp, triggeringContactId: null, "sprk_invoice", InvoiceId, _logger, CancellationToken.None);

        outcome.Should().Be(Sprk.Bff.Api.Services.Dataverse.AssignedToDefaults.Outcome.ParentResponsibleContact);
        AssignedTo(todoWithStamp).Should().Be(InternalContact);
    }

    [Fact]
    public async Task Rule5_AssignedTask_TheSourceEventsAssigneeIsTheTriggeringPerson()
    {
        _dataverse.Setup(d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_event"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>
            {
                new("sprk_event", EventId)
                {
                    ["sprk_eventname"] = "Review NDA",
                    ["sprk_assignedto"] = new EntityReference("contact", TriggeringContact),
                },
            }));
        ParentHas("sprk_event", EventId, InternalContact, AttorneyContact);

        await CreateService().RunGenerationPassAsync(CancellationToken.None);

        AssignedTo(_created.Single(t => ((string)t["sprk_name"]).StartsWith("Assigned:"))).Should().Be(TriggeringContact);
    }

    // ── ADR-052 §1 migration: an IScheduledJob, not a timer ──────────────────────────────────────────────

    [Theory]
    [InlineData(24, 2, "0 2 * * *")]
    [InlineData(48, 5, "0 5 * * *")]
    [InlineData(6, 2, "0 2/6 * * *")]
    [InlineData(6, 8, "0 2/6 * * *")]
    public void BuildCronSchedule_CompilesTheOptions(int intervalHours, int startHour, string expected)
    {
        TodoGenerationService.BuildCronSchedule(new TodoGenerationOptions { IntervalHours = intervalHours, StartHourUtc = startHour })
            .Should().Be(expected);
        Cronos.CronExpression.Parse(expected); // a value the host (Cronos) accepts
    }

    [Fact]
    public async Task ExecuteAsync_RunsOnePass_AndReportsCreatedCount()
    {
        _dataverse.Setup(d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_matter"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { new("sprk_matter", MatterId) { ["sprk_name"] = "Acme" } }));
        ParentHas("sprk_matter", MatterId, InternalContact, null);

        var result = await CreateService().ExecuteAsync(
            new JobRunContext(Guid.NewGuid(), "corr", JobRunTrigger.Scheduled, new Dictionary<string, object>()),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ProcessedItems.Should().Be(1);
        result.ResultJson.Should().Contain("\"created\":1");
    }
}
