using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Microsoft.Extensions.Options;
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
            NullLogger.Instance);

        var id = await core.CreateAsync(
            new TaskActionInput("Follow up", null, null, CommunicationId, "sprk_communication", null),
            CancellationToken.None);

        id.Should().NotBe(Guid.Empty);
        created.Should().ContainSingle();
        created[0].GetAttributeValue<EntityReference>("sprk_regardingcommunication")!.Id.Should().Be(CommunicationId);
        created[0].GetAttributeValue<EntityReference>("sprk_regardingmatter")!.Id.Should().Be(MatterId);
    }

    [Fact]
    public async Task CreateTask_WhenAncestorDerivationFails_ReturnsEmptyAndNeverCreatesTheEvent()
    {
        var created = new List<Entity>();
        var entityService = EntityServiceCapturingCreates(created);
        var core = new TaskActionCore(
            entityService.Object, CoreAncestorResolverFixtures.Failing(), NullLogger.Instance);

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
}
