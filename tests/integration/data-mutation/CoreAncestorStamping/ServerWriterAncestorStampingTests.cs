using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Workspace;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;

/// <summary>
/// Protects the FR-26 write contract for SERVER-created child records (unified-access-control-r2 task 052):
/// <b>a child record filed against another child record must also carry that target's CORE-record ancestor,
/// or the write must not happen at all.</b>
/// </summary>
/// <remarks>
/// <para>
/// <b>The failure mode these tests exist to prevent.</b> The evaluator's child-inheritance term is a
/// set-membership test over a lookup the child ROW already carries — it cannot walk a chain. So a
/// <c>todo → communication → matter</c> chain only grants access if the todo itself carries
/// <c>sprk_regardingmatter</c>. Every writer here can be handed a child-class target today, and before task
/// 052 none of them stamped: a to-do filed under an email, a task created by a playbook against a
/// communication, an inbound email associated to an invoice — each was written with no ancestor and was
/// therefore invisible to every principal whose access came from the matter above it. Silent under-grant,
/// indistinguishable from "there are no records".
/// </para>
/// <para>
/// <b>Why the negative cases matter more than the positive ones.</b> An unstamped row looks exactly like a
/// correctly-written row until someone notices records missing from a client's view. So each writer is also
/// asserted to REFUSE the write when derivation fails (NFR-01), in that writer's own error contract — throw,
/// degraded-empty, or an aborted update. A test that only proved the happy path would pass just as well
/// against a writer that silently swallowed derivation errors.
/// </para>
/// <para>
/// Companion: <c>CoreAncestorResolverTests</c> pins the taxonomy and the derivation rules themselves
/// (including the TypeScript parity check). These tests pin that the WRITERS actually call it.
/// </para>
/// </remarks>
public class ServerWriterAncestorStampingTests
{
    private static readonly Guid MatterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CommunicationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid InvoiceId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    // =====================================================================================
    // sprk_todo host — TodoRegardingBuilder (Services/Workspace)
    // =====================================================================================

    [Fact]
    public async Task ApplyResolverFields_WhenTargetIsACommunicationUnderAMatter_StampsTheMatterOnTheTodo()
    {
        // The chain the evaluator cannot walk: todo → communication → matter.
        var builder = BuildTodoBuilder(
            CoreAncestorResolverFixtures.WithAncestors(("sprk_regardingmatter", MatterId)));
        var todo = new Entity("sprk_todo");

        await builder.ApplyResolverFieldsAsync(todo, "sprk_communication", CommunicationId, "Re: filing");

        todo.GetAttributeValue<EntityReference>("sprk_regardingcommunication")!.Id
            .Should().Be(CommunicationId, "the direct target is still bound");
        todo.GetAttributeValue<EntityReference>("sprk_regardingmatter")!.Id
            .Should().Be(MatterId, "without this stamp the to-do inherits nothing from the matter");
    }

    [Fact]
    public async Task ApplyResolverFields_WhenTargetIsACoreMatter_StampsOnlyThatMatter()
    {
        // A CORE target is its own stamp, and its own parent associations are NOT ancestors — stamping a
        // matter's project here would hand every Project holder every Matter beneath it.
        var builder = BuildTodoBuilder(
            CoreAncestorResolverFixtures.WithAncestors(("sprk_regardingproject", ProjectId)));
        var todo = new Entity("sprk_todo");

        await builder.ApplyResolverFieldsAsync(todo, "sprk_matter", MatterId, "Acme v. Widget");

        todo.GetAttributeValue<EntityReference>("sprk_regardingmatter")!.Id.Should().Be(MatterId);
        todo.Attributes.Should().NotContainKey("sprk_regardingproject",
            "Matter does NOT inherit from Project — both are core, and no read is performed for a core target");
    }

    [Fact]
    public async Task ApplyResolverFields_WhenAncestorDerivationFails_ThrowsAndLeavesNoWrittenTodo()
    {
        var builder = BuildTodoBuilder(CoreAncestorResolverFixtures.Failing());
        var todo = new Entity("sprk_todo");

        var act = async () =>
            await builder.ApplyResolverFieldsAsync(todo, "sprk_communication", CommunicationId, "Re: filing");

        // NFR-01: this builder's callers create the to-do only after this returns, so a throw IS the
        // "no unstamped row" guarantee.
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*refusing to write an unstamped sprk_todo*");
    }

    // =====================================================================================
    // sprk_event host — TaskActionCore (Services/Ai/Nodes/ActionCore)
    // =====================================================================================

    [Fact]
    public async Task CreateTask_WhenRegardingACommunicationUnderAMatter_StampsTheMatterOnTheEvent()
    {
        var created = new List<Entity>();
        var entityService = EntityServiceCapturingCreates(created);
        var core = new TaskActionCore(
            entityService.Object,
            CoreAncestorResolverFixtures.WithAncestors(("sprk_regardingmatter", MatterId)),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            Sprk.Bff.Api.Tests.TestInfrastructure.IdentityNormalizationFixtures.NoLinkedContact(),
            Moq.Mock.Of<Spaarke.Dataverse.ICommunicationDataverseService>(),
            NullLogger.Instance);

        var id = await core.CreateAsync(
            new TaskActionInput("Follow up", null, null, CommunicationId, "sprk_communication", null),
            CancellationToken.None);

        id.Should().NotBe(Guid.Empty);
        created.Should().ContainSingle();
        created[0].GetAttributeValue<EntityReference>("sprk_regardingcommunication")!.Id.Should().Be(CommunicationId);
        created[0].GetAttributeValue<EntityReference>("sprk_regardingmatter")!.Id.Should().Be(MatterId);
    }

    [Fact(DisplayName = "Task 156 (owner round 8 item 2): a TaskActionCore task under a communication carries the standard ADR-024 regarding pair — id, name, url (and no type: no sprk_recordtype_ref row exists for a communication) — written by the to-do builder's own pair method")]
    public async Task CreateTask_WhenRegardingACommunication_WritesTheAdr024RegardingPair()
    {
        var created = new List<Entity>();
        var entityService = EntityServiceCapturingCreates(created);
        entityService
            .Setup(s => s.RetrieveAsync("sprk_communication", CommunicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_communication", CommunicationId) { ["sprk_name"] = "Email: Re: filing" });
        var recordTypes = new Mock<ICommunicationDataverseService>();
        recordTypes
            .Setup(r => r.QueryRecordTypeRefAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity?)null);

        var core = new TaskActionCore(
            entityService.Object,
            CoreAncestorResolverFixtures.WithAncestors(("sprk_regardingmatter", MatterId)),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            Sprk.Bff.Api.Tests.TestInfrastructure.IdentityNormalizationFixtures.NoLinkedContact(),
            recordTypes.Object,
            NullLogger.Instance);

        var id = await core.CreateAsync(
            new TaskActionInput("Follow up", null, null, CommunicationId, "sprk_communication", null),
            CancellationToken.None);

        id.Should().NotBe(Guid.Empty);
        var task = created.Should().ContainSingle().Subject;
        var cleanId = CommunicationId.ToString("D").ToLowerInvariant();
        task["sprk_regardingrecordid"].Should().Be(cleanId, "the pair's id is what the reconciliation job finds a form clear by (F-051-6)");
        task["sprk_regardingrecordname"].Should().Be("Email: Re: filing", "the communication's own primary name");
        task["sprk_regardingrecordurl"].Should().Be(
            $"/main.aspx?pagetype=entityrecord&etn=sprk_communication&id={cleanId}", "the same relative record URL every builder writes");
        task.Contains("sprk_regardingrecordtype").Should().BeFalse("no sprk_recordtype_ref row exists for a communication (live 2026-10-02)");
        task.GetAttributeValue<EntityReference>("sprk_regardingcommunication")!.Id.Should().Be(CommunicationId);
        task.GetAttributeValue<EntityReference>("sprk_regardingmatter")!.Id.Should().Be(MatterId, "the stamp is unchanged");
        recordTypes.Verify(r => r.QueryRecordTypeRefAsync("sprk_communication", It.IsAny<CancellationToken>()), Times.Once);
    }

    // The regarding NAME is for display only, so reading it must never cost the task (TaskActionCore.ReadRegardingNameAsync,
    // owner round 8 item 2). Each of its three non-happy branches is pinned below.

    [Fact(DisplayName = "Task 156 (owner round 8 item 2): a regarding name longer than sprk_event.sprk_regardingrecordname (NVARCHAR 1000) is cut to 1000, so Dataverse does not refuse the whole task create")]
    public async Task CreateTask_WhenTheRegardingNameIsLongerThanTheColumn_CapsItAndTheTaskIsStillCreated()
    {
        var created = new List<Entity>();
        var entityService = EntityServiceCapturingCreates(created);
        var longName = "Email: " + new string('x', 1200);
        entityService
            .Setup(s => s.RetrieveAsync("sprk_communication", CommunicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_communication", CommunicationId) { ["sprk_name"] = longName });
        // Dataverse's own answer to an over-long string: the WHOLE create is refused (verified live column length, read-only
        // describe of sprk_event 2026-10-03: sprk_regardingrecordname NVARCHAR(1000)).
        entityService
            .Setup(s => s.CreateAsync(
                It.Is<Entity>(e => (e.GetAttributeValue<string>("sprk_regardingrecordname") ?? string.Empty).Length > 1000),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "The length of the 'sprk_regardingrecordname' attribute of the 'sprk_event' entity exceeded the maximum allowed length of '1000'."));

        var id = await TaskCore(entityService).CreateAsync(
            new TaskActionInput("Follow up", null, null, CommunicationId, "sprk_communication", null),
            CancellationToken.None);

        id.Should().NotBe(Guid.Empty, "a name the column cannot hold must not sink the task");
        var task = created.Should().ContainSingle().Subject;
        task.GetAttributeValue<string>("sprk_regardingrecordname").Should().Be(longName[..1000],
            "the name is cut to the column's length, keeping its start");
        task["sprk_regardingrecordid"].Should().Be(CommunicationId.ToString("D").ToLowerInvariant());
    }

    [Fact(DisplayName = "Task 156 (owner round 8 item 2): a regarding name that cannot be read leaves the pair's name empty and the task is still created with the pair's id and url")]
    public async Task CreateTask_WhenTheRegardingNameReadFails_WritesThePairWithAnEmptyNameAndStillCreatesTheTask()
    {
        var created = new List<Entity>();
        var entityService = EntityServiceCapturingCreates(created);
        entityService
            .Setup(s => s.RetrieveAsync("sprk_communication", CommunicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("sprk_communication read timed out"));

        var id = await TaskCore(entityService).CreateAsync(
            new TaskActionInput("Follow up", null, null, CommunicationId, "sprk_communication", null),
            CancellationToken.None);

        id.Should().NotBe(Guid.Empty, "the name is for display only; a failed read of it must not cost the task");
        var task = created.Should().ContainSingle().Subject;
        var cleanId = CommunicationId.ToString("D").ToLowerInvariant();
        task["sprk_regardingrecordname"].Should().Be(string.Empty, "the builders' 'empty when unknown' convention");
        task["sprk_regardingrecordid"].Should().Be(cleanId, "the pair's id, which F-051-6 detection uses, never depends on the name");
        task["sprk_regardingrecordurl"].Should().Be($"/main.aspx?pagetype=entityrecord&etn=sprk_communication&id={cleanId}");
        task.GetAttributeValue<EntityReference>("sprk_regardingcommunication")!.Id.Should().Be(CommunicationId);
        task.GetAttributeValue<EntityReference>("sprk_regardingmatter")!.Id.Should().Be(MatterId, "the stamp is unaffected");
    }

    // Sweep integration (task 161 x task 156): this test used a report card as "a regarding type with no known name column".
    // Task 161 LIVE-VERIFIED sprk_reportcard's primary name (sprk_name) and added it to RegardingNameFields, so a report card
    // now has a known column — and, after that change, so does every type TaskActionCore files under except
    // sprk_recordtype_ref (which writes no pair; next test). The "never guess a column" rule is unchanged: the name is read
    // ONLY through RegardingNameFields' live-verified map, with exactly that column.
    [Fact(DisplayName = "Task 156 x 161: a report card's pair name is read through RegardingNameFields' live-verified column (sprk_name) and nothing else; no column is guessed")]
    public async Task CreateTask_WhenRegardingAReportCard_ReadsItsNameThroughTheLiveVerifiedColumnOnly()
    {
        var reportCardId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var created = new List<Entity>();
        var entityService = EntityServiceCapturingCreates(created);
        entityService
            .Setup(s => s.RetrieveAsync("sprk_reportcard", reportCardId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_reportcard", reportCardId) { ["sprk_name"] = "Q3 report card" });

        var id = await TaskCore(entityService).CreateAsync(
            new TaskActionInput("Review", null, null, reportCardId, "sprk_reportcard", null),
            CancellationToken.None);

        id.Should().NotBe(Guid.Empty);
        var task = created.Should().ContainSingle().Subject;
        var cleanId = reportCardId.ToString("D").ToLowerInvariant();
        task["sprk_regardingrecordname"].Should().Be("Q3 report card", "RegardingNameFields names sprk_reportcard's live column");
        task["sprk_regardingrecordid"].Should().Be(cleanId);
        task["sprk_regardingrecordurl"].Should().Be($"/main.aspx?pagetype=entityrecord&etn=sprk_reportcard&id={cleanId}");
        task.GetAttributeValue<EntityReference>("sprk_regardingreportcard")!.Id.Should().Be(reportCardId);
        entityService.Verify(
            s => s.RetrieveAsync("sprk_reportcard", reportCardId,
                It.Is<string[]>(c => c.Length == 1 && c[0] == "sprk_name"), It.IsAny<CancellationToken>()),
            Times.Once,
            "exactly the live-verified column is read — never a guessed one");
        entityService.Verify(
            s => s.RetrieveAsync("sprk_reportcard", It.IsAny<Guid>(),
                It.Is<string[]>(c => c.Length != 1 || c[0] != "sprk_name"), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // A sprk_recordtype_ref row is the one regarding target whose typed lookup on sprk_event IS the pair's type column
    // (sprk_regardingrecordtype), so TaskActionCore writes no pair for it (owner round 8 item 2). Reachable: the playbook
    // create-task node and the communication follow-up apply both pass the regarding type through as text.
    [Fact(DisplayName = "Task 156 (owner round 8 item 2): a task filed under a sprk_recordtype_ref row keeps that row in its typed lookup — which is also the pair's type column — and gets no pair id, name or url naming the record-type row")]
    public async Task CreateTask_WhenRegardingARecordTypeRow_KeepsItsTypedLookupAndWritesNoPair()
    {
        var recordTypeRowId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var created = new List<Entity>();
        var entityService = EntityServiceCapturingCreates(created);
        // Every type has a record-type row here, sprk_recordtype_ref included, so a pair write would ALSO replace the typed
        // lookup with a different record.
        var recordTypes = new Mock<ICommunicationDataverseService>();
        recordTypes
            .Setup(r => r.QueryRecordTypeRefAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string entity, CancellationToken _) =>
                new Entity("sprk_recordtype_ref", Guid.NewGuid()) { ["sprk_recorddisplayname"] = entity });

        var id = await new TaskActionCore(
                entityService.Object,
                CoreAncestorResolverFixtures.Inert(),
                new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
                Sprk.Bff.Api.Tests.TestInfrastructure.IdentityNormalizationFixtures.NoLinkedContact(),
                recordTypes.Object,
                NullLogger.Instance)
            .CreateAsync(
                new TaskActionInput("Review the type", null, null, recordTypeRowId, "sprk_recordtype_ref", null),
                CancellationToken.None);

        id.Should().NotBe(Guid.Empty, "a record-type row is outside the core-ancestor taxonomy, so nothing refuses the task");
        var task = created.Should().ContainSingle().Subject;
        var typed = task.GetAttributeValue<EntityReference>("sprk_regardingrecordtype");
        typed.Should().NotBeNull();
        typed!.LogicalName.Should().Be("sprk_recordtype_ref");
        typed.Id.Should().Be(recordTypeRowId, "the typed lookup names the record the task is filed under, not a record-type row for its type");
        task.Contains("sprk_regardingrecordid").Should().BeFalse("a pair id would name the record-type row as if it were a filed record");
        task.Contains("sprk_regardingrecordurl").Should().BeFalse("no record URL is written for a record-type row");
        task.Contains("sprk_regardingrecordname").Should().BeFalse("no pair is written at all");
        recordTypes.Verify(
            r => r.QueryRecordTypeRefAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the pair's type lookup is not consulted for this target");
    }

    [Fact(DisplayName = "Task 156 (owner round 8 item 2): a create cancelled during the regarding-name read is reported as cancelled — never swallowed into a 'created' result with an empty id — and nothing after the read runs")]
    public async Task CreateTask_WhenCancelledDuringTheRegardingNameRead_PropagatesTheCancellationAndCreatesNothing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var created = new List<Entity>();
        // Every Dataverse call honours the token, as the real client does — the core-ancestor resolver's reads and column
        // probe included. Without the name read's rethrow, the cancellation would be logged at Debug, the resolver would
        // swallow its own as a failed derivation, and the caller would get the "degraded success" Guid.Empty: a cancelled
        // request reported to ActionSeam as CreateTaskResult(true, Guid.Empty) and to the playbook as "Task created".
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entityService
            .Setup(s => s.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Guid __, string[] ___, CancellationToken ct) => Task.FromCanceled<Entity>(ct));
        entityService
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity e, CancellationToken ct) =>
            {
                ct.ThrowIfCancellationRequested();
                created.Add(e);
                return Guid.NewGuid();
            });
        var coreAncestors = new CoreAncestorResolver(
            entityService.Object,
            (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return CoreAncestorResolverFixtures.ProbeReturning(CoreAncestorResolverFixtures.AllRootColumns)(string.Empty, ct);
            },
            NullLogger<CoreAncestorResolver>.Instance);
        var recordTypes = new Mock<ICommunicationDataverseService>(MockBehavior.Loose);

        var act = () => new TaskActionCore(
                entityService.Object,
                coreAncestors,
                new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
                Sprk.Bff.Api.Tests.TestInfrastructure.IdentityNormalizationFixtures.NoLinkedContact(),
                recordTypes.Object,
                NullLogger.Instance)
            .CreateAsync(
                new TaskActionInput("Follow up", null, null, CommunicationId, "sprk_communication", null),
                cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "a cancelled create surfaces as a cancellation, not as a task 'created' with an empty id");
        created.Should().BeEmpty();
        recordTypes.Verify(
            r => r.QueryRecordTypeRefAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the create stops at the name read: the pair's type lookup after it never runs");
    }

    /// <summary>The task create core over <paramref name="entityService"/>, a communication under <see cref="MatterId"/>, and no record-type rows.</summary>
    private static TaskActionCore TaskCore(Mock<IGenericEntityService> entityService) => new(
        entityService.Object,
        CoreAncestorResolverFixtures.WithAncestors(("sprk_regardingmatter", MatterId)),
        new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
        Sprk.Bff.Api.Tests.TestInfrastructure.IdentityNormalizationFixtures.NoLinkedContact(),
        Mock.Of<ICommunicationDataverseService>(),
        NullLogger.Instance);

    [Fact]
    public async Task CreateTask_WhenAncestorDerivationFails_ReturnsEmptyAndNeverCreatesTheEvent()
    {
        var created = new List<Entity>();
        var entityService = EntityServiceCapturingCreates(created);
        var core = new TaskActionCore(
            entityService.Object, CoreAncestorResolverFixtures.Failing(),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            Sprk.Bff.Api.Tests.TestInfrastructure.IdentityNormalizationFixtures.NoLinkedContact(), Moq.Mock.Of<Spaarke.Dataverse.ICommunicationDataverseService>(), NullLogger.Instance);

        var id = await core.CreateAsync(
            new TaskActionInput("Follow up", null, null, CommunicationId, "sprk_communication", null),
            CancellationToken.None);

        // The class's existing "degraded success" contract, reused for the fail-closed branch. The
        // load-bearing assertion is the second one: an unstamped task is worse than an absent task,
        // because it looks like success to the playbook and is unreachable to the people who need it.
        id.Should().Be(Guid.Empty);
        created.Should().BeEmpty("no sprk_event may be created without its ancestor stamp");
    }

    // =====================================================================================
    // sprk_communication host - IncomingAssociationResolver (the inbound association write)
    //
    // Read this before changing these four: TODAY the inbound engine cannot write a child target at all.
    // AssociationStatusMapper.AddWrites persists a regarding lookup only when the target's entity is in
    // AutoFileOptions.CoreWritableEntities, which defaults to {matter, project, servicerequest} - all
    // CORE, each its own stamp. So the stamp here is not fixing a live under-grant.
    //
    // It is closing a LATENT one. That set is deliberately operator-tunable per ADR-018 "without a
    // redeploy", globally and per tenant, and the option's own docs invite operators to add entity types
    // to it. sprk_invoice, sprk_analysis and sprk_event are as addable as sprk_workassignment - and those
    // three are child-class. Adding one would silently start writing unstamped child regardings on every
    // inbound email. These tests pin that widening the set stays safe, which is the only reason the
    // convergence at this writer is worth its weight.
    // =====================================================================================

    [Fact]
    public async Task ApplyDecision_WhenTheCoreWritableSetIsWidenedToInvoices_StampsTheInvoicesMatter()
    {
        var (resolver, updates) = BuildAssociationResolver(
            CoreAncestorResolverFixtures.WithAncestors(("sprk_regardingmatter", MatterId)),
            coreWritableEntities: ["sprk_matter", "sprk_project", "sprk_servicerequest", "sprk_invoice"]);

        await ResolveWithCallerSuppliedRegardingAsync(resolver, "sprk_invoice", InvoiceId);

        updates.Should().ContainSingle();
        updates[0].Should().ContainKey("sprk_regardinginvoice", "the widened set makes the engine write it");
        updates[0]["sprk_regardingmatter"].Should().BeOfType<EntityReference>()
            .Which.Id.Should().Be(MatterId,
                "an email filed against an invoice must still reach the invoice's matter holders");
    }

    [Fact]
    public async Task ApplyDecision_WhenTheCoreWritableSetIsWidenedToBudgets_StampsTheBudgetsMatter()
    {
        // Task 156: a budget is not a CHILD (access taxonomy) but a child's copy comes from it, so an email filed against
        // a budget is stamped with the budget's matter too (CoreAncestorResolver.IsStampSourceEntity). Before task 156 the
        // engine skipped every non-child target and the email inherited nothing from the budget's matter.
        var (resolver, updates) = BuildAssociationResolver(
            CoreAncestorResolverFixtures.WithAncestors(("sprk_regardingmatter", MatterId)),
            coreWritableEntities: ["sprk_matter", "sprk_project", "sprk_servicerequest", "sprk_budget"]);

        await ResolveWithCallerSuppliedRegardingAsync(resolver, "sprk_budget", InvoiceId);

        updates.Should().ContainSingle();
        updates[0].Should().ContainKey("sprk_regardingbudget");
        updates[0]["sprk_regardingmatter"].Should().BeOfType<EntityReference>()
            .Which.Id.Should().Be(MatterId, "an email filed against a budget must reach the budget's matter holders");
    }

    [Fact]
    public async Task ApplyDecision_UnderTheDefaultCoreWritableSet_NeverWritesAChildTargetAtAll()
    {
        // The pin behind the comment above: with the shipped defaults an invoice match is surfaced as a
        // review candidate and never persisted, so no unstamped child regarding exists today. If this
        // test starts failing, the default set gained a child entity and the stamp above became
        // load-bearing in production rather than latently.
        var (resolver, updates) = BuildAssociationResolver(CoreAncestorResolverFixtures.Failing());

        await ResolveWithCallerSuppliedRegardingAsync(resolver, "sprk_invoice", InvoiceId);

        updates.Should().ContainSingle("status and provenance are still recorded");
        updates[0].Should().NotContainKey("sprk_regardinginvoice");
        updates[0].Should().NotContainKey("sprk_regardingmatter");
    }

    [Fact]
    public async Task ApplyDecision_WhenARungFilesAgainstACoreMatter_WritesNoDerivedStampOverIt()
    {
        // A rung that asserted a matter directly observed evidence about THIS message; a derived stamp is
        // only an inherited pointer. The explicit write must win, and no read is performed for a core target.
        var (resolver, updates) = BuildAssociationResolver(
            CoreAncestorResolverFixtures.WithAncestors(("sprk_regardingproject", ProjectId)));

        await ResolveWithCallerSuppliedRegardingAsync(resolver, "sprk_matter", MatterId);

        updates.Should().ContainSingle();
        updates[0]["sprk_regardingmatter"].Should().BeOfType<EntityReference>().Which.Id.Should().Be(MatterId);
        updates[0].Should().NotContainKey("sprk_regardingproject");
    }

    [Fact]
    public async Task ApplyDecision_WhenAncestorDerivationFails_WritesNothingToTheCommunication()
    {
        var (resolver, updates) = BuildAssociationResolver(
            CoreAncestorResolverFixtures.Failing(),
            coreWritableEntities: ["sprk_matter", "sprk_project", "sprk_servicerequest", "sprk_invoice"]);

        var act = async () => await ResolveWithCallerSuppliedRegardingAsync(resolver, "sprk_invoice", InvoiceId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*refusing to write an unstamped regarding*");
        updates.Should().BeEmpty(
            "the regarding lookups and the ancestor stamp must land together or not at all");
    }

    // =====================================================================================
    // Helpers
    // =====================================================================================

    private static TodoRegardingBuilder BuildTodoBuilder(CoreAncestorResolver coreAncestors)
    {
        var comm = new Mock<ICommunicationDataverseService>(MockBehavior.Loose);
        comm.Setup(c => c.QueryRecordTypeRefAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity?)null);
        return new TodoRegardingBuilder(comm.Object, coreAncestors, NullLogger<TodoRegardingBuilder>.Instance);
    }

    private static Mock<IGenericEntityService> EntityServiceCapturingCreates(List<Entity> sink)
    {
        var mock = new Mock<IGenericEntityService>(MockBehavior.Loose);
        mock.Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity e, CancellationToken _) => { sink.Add(e); return Guid.NewGuid(); });
        return mock;
    }

    private static (IncomingAssociationResolver Resolver, List<Dictionary<string, object>> Updates)
        BuildAssociationResolver(
            CoreAncestorResolver coreAncestors,
            string[]? coreWritableEntities = null)
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Loose);
        var updates = new List<Dictionary<string, object>>();

        dataverse.Setup(d => d.UpdateAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback((string _, Guid __, Dictionary<string, object> fields, CancellationToken ___) =>
                updates.Add(new Dictionary<string, object>(fields)))
            .Returns(Task.CompletedTask);

        // The engine's denormalization step reads the primary record back; an empty row is enough here
        // (name/number degrade gracefully per NFR-06 - only the ancestor stamp is fail-closed).
        dataverse.Setup(d => d.RetrieveAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, Guid id, string[] _, CancellationToken __) => new Entity(name, id));

        var resolver = new IncomingAssociationResolver(
            new IAssociationRung[] { new ExplicitReferenceRung(dataverse.Object) },
            dataverse.Object,
            dataverse.Object,
            MapperWithCoreWritable(coreWritableEntities),
            coreAncestors,
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            NullLogger<IncomingAssociationResolver>.Instance);

        return (resolver, updates);
    }

    /// <summary>
    /// The status mapper with an explicit core-writable set - the ADR-018 knob an operator can turn
    /// without a redeploy. Null keeps the shipped <see cref="AutoFileOptions"/> default.
    /// </summary>
    private static AssociationStatusMapper MapperWithCoreWritable(string[]? coreWritableEntities)
    {
        var options = new AutoFileOptions { Enabled = true, Threshold = 0.85 };
        if (coreWritableEntities is not null)
        {
            options.CoreWritableEntities = [.. coreWritableEntities];
        }

        var monitor = Mock.Of<IOptionsMonitor<AutoFileOptions>>(m => m.CurrentValue == options);
        return new AssociationStatusMapper(new AutoFileGate(monitor), NullLogger<AssociationStatusMapper>.Instance);
    }

    private static Task ResolveWithCallerSuppliedRegardingAsync(
        IncomingAssociationResolver resolver, string entityType, Guid entityId)
    {
        var message = new NormalizedMessage
        {
            Direction = CommunicationDirection.Incoming,
            From = "sender@example.com",
            Subject = "No token in this subject",
        };
        var context = new AssociationContext
        {
            CallerSuppliedRegarding =
            [
                new CommunicationAssociation { EntityType = entityType, EntityId = entityId, EntityName = "target" },
            ],
        };

        return resolver.ResolveAsync(Guid.NewGuid(), message, context, CancellationToken.None);
    }

    /// <summary>Several caller-supplied regardings at once (each confidence 1.0 — two of one type CONFLICT → Ambiguous).</summary>
    private static Task ResolveWithCallerSuppliedRegardingsAsync(
        IncomingAssociationResolver resolver, params (string EntityType, Guid EntityId)[] regardings)
    {
        var message = new NormalizedMessage
        {
            Direction = CommunicationDirection.Incoming,
            From = "sender@example.com",
            Subject = "No token in this subject",
        };
        var context = new AssociationContext
        {
            CallerSuppliedRegarding = regardings
                .Select(r => new CommunicationAssociation { EntityType = r.EntityType, EntityId = r.EntityId, EntityName = "target" })
                .ToList(),
        };

        return resolver.ResolveAsync(Guid.NewGuid(), message, context, CancellationToken.None);
    }

    // =====================================================================================
    // Task 156 verifier round 1 item 9: the inbound write follows the ONE classification rule
    // (CoreAncestorResolver.ClassifyStampSource) — only reachable with CoreWritableEntities widened to an intermediate
    // =====================================================================================

    private static readonly string[] WidenedToInvoices = ["sprk_matter", "sprk_project", "sprk_servicerequest", "sprk_invoice"];

    [Fact]
    public async Task ApplyDecision_RootAndInvoiceWritten_PairNamesTheRoot_CopiesNothingFromTheInvoice()
    {
        // Resolved: the pair names the matter (ADR-024 priority — a root before any intermediate), so the matter is the
        // direct filing and the invoice a CARRIER. Copying the invoice's PROJECT onto the email made a partial copy the
        // restamper never refreshes (the row is a direct link): an access over-grant once the invoice moved.
        var (resolver, updates) = BuildAssociationResolver(
            CoreAncestorResolverFixtures.WithAncestors(("sprk_regardingproject", ProjectId)),
            coreWritableEntities: WidenedToInvoices);

        await ResolveWithCallerSuppliedRegardingsAsync(resolver, ("sprk_matter", MatterId), ("sprk_invoice", InvoiceId));

        var written = updates.Should().ContainSingle().Subject;
        written["sprk_regardingmatter"].Should().BeOfType<EntityReference>().Which.Id.Should().Be(MatterId);
        written.Should().ContainKey("sprk_regardinginvoice");
        written["sprk_regardingrecordid"].Should().Be(MatterId.ToString("D"));
        written.Should().NotContainKey("sprk_regardingproject",
            "a carrier contributes no copy to a row filed directly under a root (the Office carrier to-do never did either)");
    }

    [Fact]
    public async Task ApplyDecision_Ambiguous_RootAndInvoiceWithNoPair_WithholdsTheInvoice_TheRootStands()
    {
        // Ambiguous (two matters conflict): no pair is written (P2b). The engine still writes the clean project and — with
        // invoices widened — the invoice. Without a pair the one rule reads the lone invoice as what the email is filed
        // under and the project as the invoice's COPY, which the restamper / the job would overwrite or clear. So the
        // invoice is withheld (a review candidate, not written) and the engine's explicit project stands.
        var otherMatter = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var (resolver, updates) = BuildAssociationResolver(
            CoreAncestorResolverFixtures.WithAncestors(("sprk_regardingmatter", MatterId)),
            coreWritableEntities: WidenedToInvoices);

        await ResolveWithCallerSuppliedRegardingsAsync(resolver,
            ("sprk_matter", MatterId), ("sprk_matter", otherMatter), ("sprk_project", ProjectId), ("sprk_invoice", InvoiceId));

        var written = updates.Should().ContainSingle().Subject;
        ((OptionSetValue)written["sprk_associationstatus"]).Value.Should().Be(AssociationStatusCodes.Ambiguous);
        written["sprk_regardingproject"].Should().BeOfType<EntityReference>().Which.Id.Should().Be(ProjectId);
        written.Should().NotContainKey("sprk_regardinginvoice");
        written.Should().NotContainKey("sprk_regardingmatter", "nothing is copied from a withheld invoice");
        written.Should().NotContainKey("sprk_regardingrecordid", "P2b: no headline on an Ambiguous decision");

        using var provenance = System.Text.Json.JsonDocument.Parse((string)written["sprk_associationprovenance"]);
        provenance.RootElement.GetProperty("candidates").EnumerateArray()
            .Single(c => c.GetProperty("field").GetString() == "sprk_regardinginvoice")
            .GetProperty("written").GetBoolean().Should().BeFalse("the provenance never claims a write that did not happen");
    }

    [Fact]
    public async Task ApplyDecision_Ambiguous_LoneInvoiceWithNoRoot_IsFiledUnderTheInvoice_AndStamped()
    {
        // The rule-5 shape that IS consistent: no pair, one intermediate, no explicit root — the invoice is what the email
        // is filed under, so it is written and its matter copied (nothing to withhold).
        var otherMatter = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var invoiceMatter = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var (resolver, updates) = BuildAssociationResolver(
            CoreAncestorResolverFixtures.WithAncestors(("sprk_regardingmatter", invoiceMatter)),
            coreWritableEntities: WidenedToInvoices);

        await ResolveWithCallerSuppliedRegardingsAsync(resolver,
            ("sprk_matter", MatterId), ("sprk_matter", otherMatter), ("sprk_invoice", InvoiceId));

        var written = updates.Should().ContainSingle().Subject;
        written.Should().ContainKey("sprk_regardinginvoice");
        written["sprk_regardingmatter"].Should().BeOfType<EntityReference>().Which.Id.Should().Be(invoiceMatter,
            "the invoice's matter — the conflicting matters were never written");
    }
}
