using System.ServiceModel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using NSubstitute;
using NSubstitute.Core;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 155 (#1080) — content filed to a CHILD record (to-do, event, invoice) lands in
/// the container of its SECURE root, or nowhere. Owner C10 part 2 (binding): every child of a secure record is
/// secure.
///
/// <para><b>What was wrong.</b> <c>RecordContainerResolver</c> returned the non-secure fallback for every
/// non-securable entity WITHOUT reading the record. On the record-keyed upload routes (no fallback) that was
/// Unresolved — the misleading "No storage container is configured" 409 for a to-do whose business unit had a
/// container. On the Office save path it fell through to a SHARED default container, so a to-do under a secure
/// project put that project's content where SPE cannot un-share it (the #1038 class, reached through the child).</para>
///
/// <para><b>The one REAL registry rule, simulated faithfully.</b> Every double here answers classification through
/// <see cref="TestEntityCatalog"/>, the shared model of the production rule. The world mirrors live dev
/// (2026-10-01): project / matter / work assignment / invoice carry <c>sprk_issecure</c>; service request does not.</para>
/// </summary>
public partial class ChildRecordContainerResolutionTests
{
    private const string RootContainer = "b!secure-root-container-0000000000";
    private const string OtherRootContainer = "b!other-secure-root-container-0000";
    private const string BusinessUnitContainer = "b!child-bu-container-00000000000000";
    private const string ArchiveContainer = "b!archive-container-000000000000000";

    private static readonly Guid ChildId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid ProjectId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid MatterId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid ServiceRequestId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid CommunicationId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid BusinessUnitId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid WorkAssignmentId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid AgreementId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid ContactId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    // Task 155 f4: records ABOVE a root (the transitive walk) and the communication's non-owner links.
    private static readonly Guid OtherMatterId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid OtherProjectId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    private static readonly Guid ThreadId = Guid.Parse("15515515-0000-0000-0000-000000000001");
    private static readonly Guid TriageCategoryId = Guid.Parse("15515515-0000-0000-0000-000000000002");

    /// <summary>A chain of distinct work assignments, for the depth / breadth / cycle bounds.</summary>
    private static readonly Guid[] WorkAssignmentChain =
    [
        Guid.Parse("15515515-0000-0000-0000-0000000000a1"), Guid.Parse("15515515-0000-0000-0000-0000000000a2"),
        Guid.Parse("15515515-0000-0000-0000-0000000000a3"), Guid.Parse("15515515-0000-0000-0000-0000000000a4"),
        Guid.Parse("15515515-0000-0000-0000-0000000000a5"), Guid.Parse("15515515-0000-0000-0000-0000000000a6"),
    ];

    // sprk_recordtype_ref ids — the LIVE spaarkedev1 ids (read-only, 2026-10-01), so a reader can match a row to dev.
    private static readonly Guid MatterTypeRef = Guid.Parse("e8547bb4-8600-f111-8407-7c1e520aa4df");
    private static readonly Guid ProjectTypeRef = Guid.Parse("ca68b3bb-8600-f111-8407-7c1e520aa4df");
    private static readonly Guid CommunicationTypeRef = Guid.Parse("0c0c0c0c-0000-0000-0000-00000000000c");
    private static readonly Guid ServiceRequestTypeRef = Guid.Parse("144c6790-d58a-f111-8076-7ced8d174eb8");
    private static readonly Guid ContactTypeRef = Guid.Parse("ca6d46d0-8600-f111-8406-7c1e525abd8b");
    private static readonly Guid UnclassifiedTypeRef = Guid.Parse("0f0f0f0f-0000-0000-0000-00000000000f");
    private static readonly Guid WorkAssignmentTypeRef = Guid.Parse("e8e1608c-781e-f111-88b3-7ced8d1dc988");
    private static readonly Guid InvoiceTypeRef = Guid.Parse("8e4a04f1-8600-f111-8406-7c1e525abd8b");

    // ============================================================================================
    // The positive cases — the acceptance criteria
    // ============================================================================================

    [Fact(DisplayName = "Task 155: a to-do under a SECURE project resolves to the PROJECT's own container; its business unit is never read")]
    public async Task Todo_UnderASecureProject_ResolvesTheProjectsOwnContainer()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId)
            .WithRoot("sprk_project", ProjectId, isSecure: true, RootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.ResolvedSecure);
        decision.ContainerId.Should().Be(RootContainer,
            "a child of a secure record is secure (owner C10 part 2) — its content belongs with the root");

        // A usable shared container must never be in scope when a secure root decides.
        world.Reads("businessunit").Should().Be(0);
    }

    [Theory(DisplayName = "Task 155: the same holds by ALIAS (the route value), for to-do and event")]
    [InlineData("todo", "sprk_todo")]
    [InlineData("event", "sprk_event")]
    public async Task Child_NamedByAlias_UnderASecureProject_ResolvesTheProjectsOwnContainer(string alias, string logical)
    {
        var world = new World()
            .WithChild(logical, regardingProject: ProjectId)
            .WithRoot("sprk_project", ProjectId, isSecure: true, RootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync(alias, ChildId);

        decision.ContainerId.Should().Be(RootContainer);
    }

    /// <summary>
    /// Task 150 — the owner's ABSENT-branch decision on the ANCESTOR path. Before it, a root above the record whose flag
    /// came back absent was read as not secure and the walk fell through to the business-unit container: exactly where
    /// a secure project's to-do would land if this service lost its field-level Read on <c>sprk_issecure</c>.
    /// </summary>
    [Fact(DisplayName = "Task 150: a to-do under a project whose sprk_issecure comes back ABSENT is refused — the business unit is never read")]
    public async Task Todo_UnderAProjectWhoseFlagIsAbsent_IsRefused()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId)
            .WithRoot("sprk_project", ProjectId, isSecure: null, RootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        var refusal = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        refusal.Code.Should().Be(RecordContainerResolver.SecureFlagUnreadableCode);
        refusal.StatusCode.Should().Be(503);
        refusal.Detail.Should().NotContain(ProjectId.ToString(), "another record's id is never returned to the caller");
        world.Reads("businessunit").Should().Be(0, "no shared container may be in scope on an unknown answer");
    }

    [Fact(DisplayName = "Task 147 r1: a MEMO (a CHILD since task 147) under a SECURE project resolves the PROJECT's own container — it is read, never refused for unknown links")]
    public async Task Memo_UnderASecureProject_ResolvesTheProjectsOwnContainer()
    {
        // Verifier item 2: with sprk_memo in the CHILD taxonomy and no ChildAncestorLinks entry, every memo resolution
        // refused 409 ("a child record type whose link ... is not known"). With its swept links it is read like a to-do.
        var world = new World()
            .WithChild("sprk_memo", regardingProject: ProjectId)
            .WithRoot("sprk_project", ProjectId, isSecure: true, RootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_memo", ChildId);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.ResolvedSecure);
        decision.ContainerId.Should().Be(RootContainer);
        world.Reads("businessunit").Should().Be(0);
    }

    [Fact(DisplayName = "Task 147 r1: a MEMO under a NON-secure project resolves its own business-unit container")]
    public async Task Memo_UnderANonSecureProject_ResolvesItsBusinessUnitContainer()
    {
        var world = new World()
            .WithChild("sprk_memo", regardingProject: ProjectId)
            .WithRoot("sprk_project", ProjectId, isSecure: false, "b!a-stale-project-stamp-00000000000")
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_memo", ChildId);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.ResolvedFallback);
        decision.ContainerId.Should().Be(BusinessUnitContainer);
    }

    [Fact(DisplayName = "Task 155: a to-do under a NON-secure project resolves its OWN business-unit container — no 409")]
    public async Task Todo_UnderANonSecureProject_ResolvesItsOwnBusinessUnitContainer()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId)
            .WithRoot("sprk_project", ProjectId, isSecure: false, "b!a-stale-project-stamp-00000000000")
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.ResolvedFallback,
            "Unresolved here is the misleading 'No storage container is configured' 409 this task removes");
        decision.ContainerId.Should().Be(BusinessUnitContainer,
            "a non-secure root's own stamp is never read as the child's container");
    }

    [Fact(DisplayName = "Task 155: an event with NO ancestor resolves its business-unit container")]
    public async Task Event_WithNoAncestor_ResolvesItsBusinessUnitContainer()
    {
        var world = new World()
            .WithChild("sprk_event")
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_event", ChildId);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.ResolvedFallback);
        decision.ContainerId.Should().Be(BusinessUnitContainer);
        world.RootReads().Should().Be(0, "no link, nothing to read");
    }

    [Fact(DisplayName = "Task 155: a business unit with NO container leaves a root-less child Unresolved (the documented 409)")]
    public async Task Event_WithNoAncestor_AndABusinessUnitWithoutContainer_IsUnresolved()
    {
        var world = new World()
            .WithChild("sprk_event")
            .WithBusinessUnit(container: null);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_event", ChildId);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.Unresolved);
        decision.ContainerId.Should().BeNull();
    }

    [Fact(DisplayName = "Task 155: a SECURE root and a NON-secure root — the secure one decides")]
    public async Task Todo_WithOneSecureAndOneNonSecureRoot_ResolvesTheSecureRoot()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId, regardingMatter: MatterId)
            .WithRoot("sprk_project", ProjectId, isSecure: false, containerId: null)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        decision.ContainerId.Should().Be(RootContainer,
            "the shared container is a disclosure; the secure one is merely conservative");
    }

    [Fact(DisplayName = "Task 155: a root whose entity CANNOT be secure in this org AND whose row names nothing above it is never read")]
    public async Task Todo_UnderARootTypeThatCannotBeSecure_AndNamesNothing_DoesNotReadIt_AndResolvesTheBusinessUnit()
    {
        // A root type whose metadata lacks sprk_issecure cannot be secure, and reading the flag would FAULT. Derived
        // from metadata, so the day it gains the flag it is read without a code change. (f3: until f3 this was pinned
        // with sprk_servicerequest, which is no longer a root link. f4: until f4 it was pinned with a work assignment —
        // but a work assignment's own row can name a SECURE matter above it, so since the walk became transitive a
        // non-securable work assignment is still READ for its links (NonSecurableRootType_WithLinks_IsStillFollowed).
        // The matter names nothing above itself, so it is the type this guarantee is about.)
        var world = new World(securable: ["sprk_project", "sprk_workassignment"])
            .WithChild("sprk_todo", regardingMatter: MatterId)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        decision.ContainerId.Should().Be(BusinessUnitContainer);
        world.Reads("sprk_matter").Should().Be(0);
    }

    [Fact(DisplayName = "Task 155: an invoice (own flag FALSE) under a secure matter takes the matter's container through its TYPED lookup")]
    public async Task SecurableChild_NotItselfSecure_UnderASecureMatter_ResolvesTheMattersContainer()
    {
        // Live dev: sprk_invoice carries sprk_issecure, and links to its root through typed sprk_matter /
        // sprk_project lookups, not sprk_regarding{core}. This world declares the column on the invoice as live does;
        // since task 150 the registry (and TestEntityCatalog) does not treat it as securable, and the links decide.
        var world = new World(securable: ["sprk_project", "sprk_matter", "sprk_workassignment", "sprk_invoice"])
            .WithChild("sprk_invoice", typedMatter: MatterId, isSecure: false)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("invoice", ChildId);

        decision.ContainerId.Should().Be(RootContainer);
        world.Reads("businessunit").Should().Be(0);
    }

    /// <summary>
    /// Task 150 (owner round 10 item 11: "invoices follow their matter"). Until task 150 an invoice flagged secure was a
    /// secure record in its own right — it kept a container of its own and never consulted its root, and with no
    /// container it refused every upload even under an ordinary matter. Its flag is no longer a security input: the
    /// matter decides, both ways. (The real registry's half — the invoice classified NotSecurable although its table
    /// carries the column — is pinned in <c>SecurableEntityRegistryTests</c>.)
    /// </summary>
    [Theory(DisplayName = "Task 150: an invoice FLAGGED secure follows its matter — secure under a secure matter, ordinary under an ordinary one")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnInvoiceFlaggedSecure_FollowsItsMatter(bool matterIsSecure)
    {
        var world = new World(securable: ["sprk_project", "sprk_matter", "sprk_workassignment", "sprk_invoice"])
            .WithChild("sprk_invoice", typedMatter: MatterId, isSecure: true, ownContainer: OtherRootContainer)
            .WithRoot("sprk_matter", MatterId, isSecure: matterIsSecure, matterIsSecure ? RootContainer : null)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_invoice", ChildId);

        decision.ContainerId.Should().Be(matterIsSecure ? RootContainer : BusinessUnitContainer,
            "the invoice's own flag and container decide nothing; its matter does");
        world.Reads("sprk_matter").Should().Be(1, "the invoice's root is always consulted now");
    }

    [Fact(DisplayName = "Task 155: a contact (no ancestor concept) resolves its own business-unit container by the two-argument overload")]
    public async Task Contact_TwoArgumentOverload_ResolvesItsBusinessUnitContainer()
    {
        var world = new World()
            .WithRecord("contact", owningBusinessUnit: true)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("contact", ChildId);

        decision.ContainerId.Should().Be(BusinessUnitContainer,
            "the two-argument overload's documented contract: the RECORD's own owningbusinessunit");
    }

    // ============================================================================================
    // The four-argument (explicit fallback) overload — unchanged for records with no secure root
    // ============================================================================================

    [Fact(DisplayName = "Task 155: with an explicit fallback, a child with NO secure root resolves to that fallback, as before")]
    public async Task ExplicitFallback_ChildWithoutASecureRoot_ResolvesTheFallback()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId)
            .WithRoot("sprk_project", ProjectId, isSecure: false, containerId: null)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId, ArchiveContainer);

        decision.ContainerId.Should().Be(ArchiveContainer);
        world.Reads("businessunit").Should().Be(0, "an explicit fallback is never second-guessed by the BU");
    }

    [Fact(DisplayName = "Task 155: with an explicit fallback, a child under a SECURE root still goes to the root — the fallback is never in scope")]
    public async Task ExplicitFallback_ChildUnderASecureRoot_ResolvesTheRootNotTheFallback()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId)
            .WithRoot("sprk_project", ProjectId, isSecure: true, RootContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId, ArchiveContainer);

        decision.ContainerId.Should().Be(RootContainer);
    }

    [Fact(DisplayName = "Task 155: with an explicit fallback, an entity whose row names no root still costs ZERO record reads")]
    public async Task ExplicitFallback_EntityWithNoAncestorConcept_CostsNoRecordRead()
    {
        // f3: moved from contact to account. A contact's own sprk_invoice lookup names a record that can belong to a
        // matter (live sweep), so a contact must now be read; account names no root by any column, so it keeps the
        // zero-read guarantee this pins.
        var world = new World();

        var decision = await world.Resolver().ResolveForRecordAsync("account", ChildId, ArchiveContainer);

        decision.ContainerId.Should().Be(ArchiveContainer);
        world.TotalReads().Should().Be(0);
    }

    // ============================================================================================
    // The refusals — none resolves a shared container
    // ============================================================================================

    [Fact(DisplayName = "Task 155: a SECURE root with no container FAILS CLOSED (secure_record_container_missing) — the BU is never read")]
    public async Task Todo_UnderASecureRootWithoutContainer_FailsClosed()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId)
            .WithRoot("sprk_project", ProjectId, isSecure: true, containerId: "   ")
            .WithBusinessUnit(BusinessUnitContainer);

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId, ArchiveContainer);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be("secure_record_container_missing");
        ex.StatusCode.Should().Be(409);
        world.Reads("businessunit").Should().Be(0);
    }

    [Fact(DisplayName = "Task 155: TWO secure roots — ambiguity refusal (container_ancestor_ambiguous)")]
    public async Task Todo_UnderTwoSecureRoots_IsRefusedAsAmbiguous()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId, regardingMatter: MatterId)
            .WithRoot("sprk_project", ProjectId, isSecure: true, RootContainer)
            .WithRoot("sprk_matter", MatterId, isSecure: true, OtherRootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorAmbiguousCode);
        ex.StatusCode.Should().Be(409);
    }

    [Fact(DisplayName = "Task 155: an UNREADABLE root refuses (container_ancestor_unresolved, 503) — never read as non-secure")]
    public async Task Todo_WhoseRootCannotBeRead_IsRefused()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId)
            .WithRootFault("sprk_project", ProjectId, new TimeoutException("Dataverse timed out"))
            .WithBusinessUnit(BusinessUnitContainer);

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
        ex.StatusCode.Should().Be(503, "a transient read failure is retryable, not a verdict");
        world.Reads("businessunit").Should().Be(0);
    }

    [Fact(DisplayName = "Task 155: a link to a root that does not EXIST refuses (container_ancestor_unresolved, 409)")]
    public async Task Todo_LinkedToARootThatDoesNotExist_IsRefused()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId)
            .WithRootFault("sprk_project", ProjectId, new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = -2147220969 }, new FaultReason("does not exist")))
            .WithBusinessUnit(BusinessUnitContainer);

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
        ex.StatusCode.Should().Be(409);
    }

    [Fact(DisplayName = "Task 155: an unreadable CHILD row (the ancestor LINK itself) refuses with container_ancestor_unresolved 503 — never 'no ancestor', never a raw fault")]
    public async Task Todo_WhoseOwnRowCannotBeRead_IsRefusedWithAReasonCode()
    {
        // AC4: "unreadable ancestor link ... -> refusal with a reason code". The child's row IS its link to its
        // root. As a raw fault the record-keyed routes rendered it as a generic 500 "Upload failed"; it must be
        // the same typed, retryable refusal as an unreadable ROOT.
        var world = new World()
            .WithRecordFault("sprk_todo", new TimeoutException("Dataverse timed out"))
            .WithBusinessUnit(BusinessUnitContainer);

        foreach (var fallback in new[] { null, ArchiveContainer })
        {
            var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId, fallback);

            var ex = (await act.Should().ThrowAsync<SdapProblemException>(
                "the explicit fallback is available, and using it on an unread link is the #1038 fail-open")).Which;
            ex.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
            ex.StatusCode.Should().Be(503, "a transient read failure is retryable, not a verdict");
            ex.Detail.Should().NotContain("timed out", "the raw fault text belongs in the log, not the response");
        }

        world.Reads("businessunit").Should().Be(0);
        world.RootReads().Should().Be(0);
    }

    [Fact(DisplayName = "Task 155 f2: a Dataverse HTTP TIMEOUT (TaskCanceledException, caller token live) on the child row or the root is the typed 503 — not a generic 500")]
    public async Task DataverseTimeout_SurfacingAsTaskCanceled_IsTheTyped503()
    {
        var childTimeout = new World()
            .WithRecordFault("sprk_todo", new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"))
            .WithBusinessUnit(BusinessUnitContainer);
        var rootTimeout = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId)
            .WithRootFault("sprk_project", ProjectId, new TaskCanceledException("HttpClient.Timeout"))
            .WithBusinessUnit(BusinessUnitContainer);

        foreach (var world in new[] { childTimeout, rootTimeout })
        {
            var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId, CancellationToken.None);

            var ex = (await act.Should().ThrowAsync<SdapProblemException>(
                "a timeout is an unreadable row — the caller did not cancel")).Which;
            ex.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
            ex.StatusCode.Should().Be(503);
            world.Reads("businessunit").Should().Be(0);
        }
    }

    [Fact(DisplayName = "Task 155 f2: a CALLER cancellation still propagates as cancellation — it is not re-labelled as an unreadable row")]
    public async Task CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var childCancelled = new World()
            .WithRecordFault("sprk_todo", new OperationCanceledException(cts.Token));
        var rootCancelled = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId)
            .WithRootFault("sprk_project", ProjectId, new OperationCanceledException(cts.Token));

        foreach (var world in new[] { childCancelled, rootCancelled })
        {
            var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
    }

    [Fact(DisplayName = "Task 155: a CHILD row that does NOT EXIST still answers container_record_not_found 404 — not re-labelled as unreadable")]
    public async Task Todo_ThatDoesNotExist_StillAnswersRecordNotFound()
    {
        var world = new World().WithRecordFault("sprk_todo", new FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault { ErrorCode = -2147220969 }, new FaultReason("does not exist")));

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be("container_record_not_found",
            "a missing record is permanent (the ingest path skips it); calling it 'unreadable, try again' would not be");
        ex.StatusCode.Should().Be(404);
    }

    [Fact(DisplayName = "Task 155: a root link whose entity this org does NOT KNOW refuses (container_ancestor_unresolved 409) — never read as 'not secure'")]
    public async Task Todo_LinkedToARootTypeTheOrgDoesNotKnow_IsRefused()
    {
        // The registry answers NotAnEntity for sprk_project (e.g. a metadata catalog that lost the entity). Reading
        // that as "cannot be secure" and moving on would put a secure project's to-do in its BU container.
        var world = new World(securable: ["sprk_matter", "sprk_workassignment"], unknown: ["sprk_project"])
            .WithChild("sprk_todo", regardingProject: ProjectId)
            .WithRoot("sprk_project", ProjectId, isSecure: true, RootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        foreach (var fallback in new[] { null, ArchiveContainer })
        {
            var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId, fallback);

            var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
            ex.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
            ex.StatusCode.Should().Be(409);
        }

        world.Reads("businessunit").Should().Be(0);
        world.Reads("sprk_project").Should().Be(0, "an entity the org does not know is not read");
    }

    [Fact(DisplayName = "Task 155: a root read that returns NO ROW refuses (container_ancestor_unresolved 409) — never read as 'not secure'")]
    public async Task Todo_WhoseRootReadReturnsNoRow_IsRefused()
    {
        // Defensive: production RetrieveAsync throws on not-found rather than returning null, but a null row is still
        // an UNKNOWN answer and must refuse rather than skip the root.
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId)
            .WithNullRoot("sprk_project", ProjectId)
            .WithBusinessUnit(BusinessUnitContainer);

        foreach (var fallback in new[] { null, ArchiveContainer })
        {
            var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId, fallback);

            var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
            ex.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
            ex.StatusCode.Should().Be(409);
        }

        world.Reads("businessunit").Should().Be(0);
    }

    // Task 156 (owner round 4 item 5, option b) REPLACED this theory's to-do / event rows: a child filed under a
    // communication, event, invoice, document, analysis, agreement, budget or report card is now read live and its copy
    // compared (ChildRecordContainerResolutionTests.StampFreshness.cs). What stays HELD is below.
    [Theory(DisplayName = "Task 155 / 156: a child filed under a SERVICE REQUEST, and a WORK ASSIGNMENT filed under a communication / event / invoice, stay refused as unverifiable — nothing on those rows is a copy to compare")]
    // Task 155 f3: a service request hangs off a matter / project / work assignment (live: its own
    // sprk_regarding{matter,project,workassignment}) and cannot carry sprk_issecure — an intermediate, not a root.
    [InlineData("sprk_todo", "sprk_regardingservicerequest")]
    [InlineData("sprk_event", "sprk_regardingservicerequest")]
    // Task 155 f3: a work assignment (a ROOT that is itself filed regarding something) under a communication / event /
    // invoice (live: 1 of 22 work assignments regards an invoice).
    [InlineData("sprk_workassignment", "sprk_regardingcommunication")]
    [InlineData("sprk_workassignment", "sprk_regardingevent")]
    [InlineData("sprk_workassignment", "sprk_regardinginvoice")]
    public async Task Child_FiledUnderAnotherChild_IsRefusedAsUnverifiable(string entity, string intermediateColumn)
    {
        // A service request is CORE (nothing is copied from it) and cannot carry sprk_issecure; a work assignment carries
        // no copy (its matter / project are its own links). Neither row has anything the resolver can compare.
        // A work assignment is itself a securable root: post-backfill it reads No (task 150), never absent.
        var world = new World()
            .WithChild(entity, regardingProject: ProjectId, intermediate: (intermediateColumn, CommunicationId),
                isSecure: entity == "sprk_workassignment" ? false : null)
            .WithRoot("sprk_project", ProjectId, isSecure: false, containerId: null)
            .WithBusinessUnit(BusinessUnitContainer);

        foreach (var fallback in new[] { null, ArchiveContainer })
        {
            var act = async () => await world.Resolver().ResolveForRecordAsync(entity, ChildId, fallback);

            var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
            ex.Code.Should().Be(RecordContainerResolver.AncestorUnverifiableCode);
            ex.StatusCode.Should().Be(409);
        }

        world.Reads("businessunit").Should().Be(0);
        world.RootReads().Should().Be(0, "the stamp is not consulted at all — consulting it is the papering-over");
    }

    // Task 156 moved this theory's to-do / event × agreement / budget / report card rows to the stamp-freshness
    // theories: those intermediates are now derived (CoreAncestorResolver.IntermediateRootColumns), read live and their
    // copies compared. A missing copy under one of them is STALE there (MissingOrOrphanedCopy_IsStale).
    [Theory(DisplayName = "Task 155 f2/f3: a record whose ONLY regarding is a service request, an invoice's agreement or a contact's invoice is refused — never the BU container")]
    // Task 155 f3: the same shape through a service request (core, so CoreAncestorResolver stamps nothing above it),
    // and the f3 BLOCKING fail-open — an INVOICE regarding an agreement (live lookup sprk_regardingagreement), which
    // the f2 table gave no intermediate column at all.
    [InlineData("sprk_todo", "sprk_regardingservicerequest")]
    [InlineData("sprk_event", "sprk_regardingservicerequest")]
    [InlineData("sprk_invoice", "sprk_regardingagreement")]
    // Task 155 f3: a CONTACT's own sprk_invoice lookup (live: 0 contacts set it) — read and held, not trusted.
    [InlineData("contact", "sprk_invoice")]
    public async Task Child_UnderARootOwnedNonCoreRecord_WithNoStamp_IsRefused(string entity, string column)
    {
        // The live dev shape (to-do a01477e8-… → report card 9d1477e8-… → matter b68299c6-…): CoreAncestorResolver
        // does not classify report cards, so the to-do carries NO sprk_regarding{core}. Before f2 this read "no root"
        // and resolved to the BU container, even if the matter is SECURE — and the report card was never read.
        var world = new World()
            .WithChild(entity, intermediate: (column, CommunicationId))
            .WithBusinessUnit(BusinessUnitContainer);

        foreach (var fallback in new[] { null, ArchiveContainer })
        {
            var act = async () => await world.Resolver().ResolveForRecordAsync(entity, ChildId, fallback);

            var ex = (await act.Should().ThrowAsync<SdapProblemException>(
                "'no stamp' here is not 'no root' — the record it is filed under belongs to a matter or project")).Which;
            ex.Code.Should().Be(RecordContainerResolver.AncestorUnverifiableCode);
            ex.StatusCode.Should().Be(409);
        }

        world.Reads("businessunit").Should().Be(0, "a shared container must never be in scope");
    }

    /// <summary>
    /// Targets that are NOT ownership and are never followed: security principals, currency, reference / lookup-value
    /// tables, the AI search index and OOB service entities. Every one was swept for its own lookups (f3): none has a
    /// lookup to a project, matter or work assignment.
    /// </summary>
    private static readonly HashSet<string> NonOwnerTargets = new(StringComparer.Ordinal)
    {
        "systemuser", "team", "businessunit", "transactioncurrency", "externalparty", "sla",
        "sprk_recordtype_ref", "sprk_eventtype_ref", "sprk_mattertype_ref", "sprk_practicearea_ref",
        "sprk_projecttype_ref", "sprk_contacttype_ref", "sprk_eventset", "sprk_aisearchindex", "sprk_chartdefinition",
        // f4 (sprk_communication sweep, 2026-10-02). sprk_triagecategory's own lookups are the four system columns and
        // organizationid — a reference. sprk_communicationthread is a GROUPING: its regarding anchor is COPIED from its
        // messages' regarding (IThreadResolver), live threads hold messages filed under DIFFERENT matters, and every
        // message gets one by the 3-tier ladder — so holding on it would refuse every threaded message. Owner-
        // reversible interpretation (vii) in the task note.
        "sprk_triagecategory", "sprk_communicationthread",
        // Task 156 (sweep of sprk_analysis / sprk_document / sprk_agreement, 2026-10-02): configuration and storage
        // references, none with a lookup to a root or an intermediate.
        "sprk_analysisaction", "sprk_agreementtype", "sprk_analysisplaybook", "sprk_container", "sprk_fileversion",
    };

    [Theory(DisplayName = "Task 155 f3: the ONE record read requests EXACTLY every column whose target is or can hang off a root, plus the polymorphic pair — pinned against the live lookup sweep")]
    [InlineData("sprk_todo", "both",
        "createdby>systemuser;createdonbehalfby>systemuser;modifiedby>systemuser;modifiedonbehalfby>systemuser;"
        + "ownerid>systemuser|team;owningbusinessunit>businessunit;owningteam>team;owninguser>systemuser;"
        + "sprk_assignedto>contact;sprk_regardingagreement>sprk_agreement;sprk_regardinganalysis>sprk_analysis;"
        + "sprk_regardingbudget>sprk_budget;sprk_regardingcommunication>sprk_communication;"
        + "sprk_regardingcontact>contact;sprk_regardingdocument>sprk_document;sprk_regardingevent>sprk_event;"
        + "sprk_regardinginvoice>sprk_invoice;sprk_regardingmatter>sprk_matter;"
        + "sprk_regardingorganization>sprk_organization;sprk_regardingproject>sprk_project;"
        + "sprk_regardingrecordtype>sprk_recordtype_ref;sprk_regardingreportcard>sprk_reportcard;"
        + "sprk_regardingservicerequest>sprk_servicerequest;sprk_regardingworkassignment>sprk_workassignment;"
        + "sprk_relatedrecordtype>sprk_recordtype_ref")]
    [InlineData("sprk_event", "both",
        "createdby>systemuser;createdonbehalfby>systemuser;modifiedby>systemuser;modifiedonbehalfby>systemuser;"
        + "ownerid>systemuser|team;owningbusinessunit>businessunit;owningteam>team;owninguser>systemuser;"
        + "sprk_ai_search_index>sprk_aisearchindex;sprk_approvedby>contact;sprk_assignedattorney1>contact;"
        + "sprk_assignedattorney2>contact;sprk_assignedlawfirm1>sprk_organization;"
        + "sprk_assignedlawfirm2>sprk_organization;sprk_assignedparalegal1>contact;sprk_assignedparalegal2>contact;"
        + "sprk_assignedto>contact;sprk_assignedto1>contact;sprk_assignedto2>contact;"
        + "sprk_assignedtoexternal>contact;sprk_assignedtointernal>contact;sprk_completedby>contact;"
        + "sprk_eventset>sprk_eventset;sprk_eventtype_ref>sprk_eventtype_ref;sprk_reassignedby>contact;"
        + "sprk_regardingaccount>account;sprk_regardingagreement>sprk_agreement;"
        + "sprk_regardinganalysis>sprk_analysis;sprk_regardingbudget>sprk_budget;"
        + "sprk_regardingcommunication>sprk_communication;sprk_regardingcontact>contact;"
        + "sprk_regardingevent>sprk_event;sprk_regardinginvoice>sprk_invoice;sprk_regardingmatter>sprk_matter;"
        + "sprk_regardingorganization>sprk_organization;sprk_regardingproject>sprk_project;"
        + "sprk_regardingrecordtype>sprk_recordtype_ref;sprk_regardingreportcard>sprk_reportcard;"
        + "sprk_regardingservicerequest>sprk_servicerequest;sprk_regardingworkassignment>sprk_workassignment;"
        + "sprk_rescheduledby>contact;sprk_todoassigned>contact")]
    [InlineData("sprk_invoice", "both",
        "createdby>systemuser;createdonbehalfby>systemuser;modifiedby>systemuser;modifiedonbehalfby>systemuser;"
        + "ownerid>systemuser|team;owningbusinessunit>businessunit;owningteam>team;owninguser>systemuser;"
        + "sprk_ai_search_index>sprk_aisearchindex;sprk_assignedto1>contact;sprk_assignedto2>contact;"
        + "sprk_assignedtoattorney1>contact;sprk_assignedtoattorney2>contact;sprk_assignedtoparalegal1>contact;"
        + "sprk_assignedtoparalegal2>contact;sprk_matter>sprk_matter;sprk_project>sprk_project;"
        + "sprk_regardingagreement>sprk_agreement;sprk_regardingrecordtype>sprk_recordtype_ref;"
        + "sprk_securitybu>businessunit;sprk_vendororg>sprk_organization;transactioncurrencyid>transactioncurrency")]
    [InlineData("sprk_workassignment", "both",
        "createdby>systemuser;createdonbehalfby>systemuser;modifiedby>systemuser;modifiedonbehalfby>systemuser;"
        + "ownerid>systemuser|team;owningbusinessunit>businessunit;owningteam>team;owninguser>systemuser;"
        + "sprk_ai_search_index>sprk_aisearchindex;sprk_assignedattorney1>contact;sprk_assignedattorney2>contact;"
        + "sprk_assignedlawfirm1>sprk_organization;sprk_assignedlawfirm2>sprk_organization;"
        + "sprk_assignedlawfirmattorney1>contact;sprk_assignedparalegal1>contact;sprk_assignedparalegal2>contact;"
        + "sprk_assignedto>contact;sprk_assignedtoexternal>contact;sprk_assignedtointernal>contact;"
        + "sprk_mattertype>sprk_mattertype_ref;sprk_practicearea>sprk_practicearea_ref;"
        + "sprk_regardingcommunication>sprk_communication;sprk_regardingevent>sprk_event;"
        + "sprk_regardinginvoice>sprk_invoice;sprk_regardingmatter>sprk_matter;sprk_regardingproject>sprk_project;"
        + "sprk_regardingrecordtype>sprk_recordtype_ref;sprk_securitybu>businessunit")]
    [InlineData("sprk_project", "both",
        "createdby>systemuser;createdonbehalfby>systemuser;modifiedby>systemuser;modifiedonbehalfby>systemuser;"
        + "ownerid>systemuser|team;owningbusinessunit>businessunit;owningteam>team;owninguser>systemuser;"
        + "sprk_ai_search_index>sprk_aisearchindex;sprk_assignedattorney1>contact;sprk_assignedattorney2>contact;"
        + "sprk_assignedlawfirm1>sprk_organization;sprk_assignedlawfirm2>sprk_organization;"
        + "sprk_assignedparalegal1>contact;sprk_assignedparalegal2>contact;sprk_assignedtoexternal>contact;"
        + "sprk_assignedtointernal>contact;sprk_externalaccount>account;sprk_mattertype>sprk_mattertype_ref;"
        + "sprk_practicearea>sprk_practicearea_ref;sprk_projecttype_ref>sprk_projecttype_ref;"
        + "sprk_regardingrecordtype>sprk_recordtype_ref;sprk_securitybu>businessunit;"
        + "transactioncurrencyid>transactioncurrency")]
    // sprk_matter: a sprk_regardingrecordtype lookup but NO sprk_regardingrecordid column (live), so its row can name
    // no record — no pair, no links.
    [InlineData("sprk_matter", "none",
        "createdby>systemuser;createdonbehalfby>systemuser;modifiedby>systemuser;modifiedonbehalfby>systemuser;"
        + "ownerid>systemuser|team;owningbusinessunit>businessunit;owningteam>team;owninguser>systemuser;"
        + "sprk_ai_search_index>sprk_aisearchindex;sprk_assignedattorney1>contact;sprk_assignedattorney2>contact;"
        + "sprk_assignedlawfirm1>sprk_organization;sprk_assignedlawfirm2>sprk_organization;"
        + "sprk_assignedparalegal1>contact;sprk_assignedparalegal2>contact;sprk_assignedtoexternal>contact;"
        + "sprk_assignedtointernal>contact;sprk_chartdefinition>sprk_chartdefinition;"
        + "sprk_externalaccount>account;sprk_mattertype>sprk_mattertype_ref;"
        + "sprk_practicearea>sprk_practicearea_ref;sprk_regardingrecordtype>sprk_recordtype_ref;"
        + "sprk_securitybu>businessunit;transactioncurrencyid>transactioncurrency")]
    [InlineData("contact", "none",
        "accountid>account;createdby>systemuser;createdbyexternalparty>externalparty;createdonbehalfby>systemuser;"
        + "masterid>contact;modifiedby>systemuser;modifiedbyexternalparty>externalparty;"
        + "modifiedonbehalfby>systemuser;msa_managingpartnerid>account;ownerid>systemuser|team;"
        + "owningbusinessunit>businessunit;owningteam>team;owninguser>systemuser;parentcontactid>contact;"
        + "parentcustomerid>account|contact;preferredsystemuserid>systemuser;slaid>sla;slainvokedid>sla;"
        + "sprk_contacttype>sprk_contacttype_ref;sprk_invoice>sprk_invoice;sprk_organization>sprk_organization;"
        + "sprk_systemuser>systemuser;transactioncurrencyid>transactioncurrency")]
    // f4: sprk_communication (24 lookups, live 2026-10-02) — the communication pipeline's record since f4.
    [InlineData("sprk_communication", "both",
        "createdby>systemuser;createdonbehalfby>systemuser;modifiedby>systemuser;modifiedonbehalfby>systemuser;"
        + "ownerid>systemuser|team;owningbusinessunit>businessunit;owningteam>team;owninguser>systemuser;"
        + "sprk_communicationthread>sprk_communicationthread;sprk_regardingaccount>account;"
        + "sprk_regardinganalysis>sprk_analysis;sprk_regardingbudget>sprk_budget;sprk_regardingevent>sprk_event;"
        + "sprk_regardinginvoice>sprk_invoice;sprk_regardingmatter>sprk_matter;"
        + "sprk_regardingorganization>sprk_organization;sprk_regardingperson>contact;"
        + "sprk_regardingproject>sprk_project;sprk_regardingrecordtype>sprk_recordtype_ref;"
        + "sprk_regardingreportcard>sprk_reportcard;sprk_regardingservicerequest>sprk_servicerequest;"
        + "sprk_regardingworkassignment>sprk_workassignment;sprk_sentby>systemuser;"
        + "sprk_triagecategory>sprk_triagecategory")]
    // Task 156: the five intermediates a child can be filed under that f3 classified but did not describe — each is READ
    // when a child is filed under it (live sweep, Dataverse MCP describe, read-only, 2026-10-02).
    [InlineData("sprk_analysis", "both",
        "createdby>systemuser;createdonbehalfby>systemuser;modifiedby>systemuser;modifiedonbehalfby>systemuser;"
        + "ownerid>systemuser|team;owningbusinessunit>businessunit;owningteam>team;owninguser>systemuser;"
        + "sprk_actionid>sprk_analysisaction;sprk_agreementtype>sprk_agreementtype;sprk_assignedattorney1>contact;"
        + "sprk_assignedattorney2>contact;sprk_assignedparalegal1>contact;sprk_assignedparalegal2>contact;"
        + "sprk_documentid>sprk_document;sprk_outputfileid>sprk_document;sprk_playbook>sprk_analysisplaybook;"
        + "sprk_regardingbudget>sprk_budget;sprk_regardingcommunication>sprk_communication;"
        + "sprk_regardingdocument>sprk_document;sprk_regardinginvoice>sprk_invoice;sprk_regardingmatter>sprk_matter;"
        + "sprk_regardingproject>sprk_project;sprk_regardingrecordtype>sprk_recordtype_ref;"
        + "sprk_regardingservicerequest>sprk_servicerequest;sprk_regardingworkassignment>sprk_workassignment;"
        + "sprk_reviewerby>systemuser")]
    // sprk_document: a sprk_regardingrecordid STRING but NO sprk_regardingrecordtype column (live) — the pair is id-only.
    [InlineData("sprk_document", "id",
        "createdby>systemuser;createdonbehalfby>systemuser;modifiedby>systemuser;modifiedonbehalfby>systemuser;"
        + "ownerid>systemuser|team;owningbusinessunit>businessunit;owningteam>team;owninguser>systemuser;"
        + "sprk_ai_search_index>sprk_aisearchindex;sprk_canonicaldocument>sprk_document;sprk_checkedinby>systemuser;"
        + "sprk_checkedoutby>systemuser;sprk_containername>sprk_container;sprk_currentversionid>sprk_fileversion;"
        + "sprk_email>email;sprk_invoice>sprk_invoice;sprk_invoicereviewedby>systemuser;sprk_matter>sprk_matter;"
        + "sprk_parentdocument>sprk_document;sprk_project>sprk_project;sprk_relatedagreement>sprk_agreement;"
        + "sprk_relatedcommunication>sprk_communication;sprk_relatedcontact>contact;sprk_relatedevent>sprk_event;"
        + "sprk_relatedinvoice>sprk_invoice;sprk_relatedmatter>sprk_matter;sprk_relatedorganization>sprk_organization;"
        + "sprk_relatedproject>sprk_project;sprk_relatedservicerequest>sprk_servicerequest;sprk_relatedtodo>sprk_todo;"
        + "sprk_relatedvendororg>sprk_organization;sprk_relatedworkassignment>sprk_workassignment;"
        + "sprk_workassignment>sprk_workassignment;transactioncurrencyid>transactioncurrency")]
    [InlineData("sprk_agreement", "none",
        "createdby>systemuser;createdonbehalfby>systemuser;modifiedby>systemuser;modifiedonbehalfby>systemuser;"
        + "ownerid>systemuser|team;owningbusinessunit>businessunit;owningteam>team;owninguser>systemuser;"
        + "sprk_agreementtype>sprk_agreementtype;sprk_aisearchindex>sprk_aisearchindex;sprk_assignedattorney1>contact;"
        + "sprk_assignedattorney2>contact;sprk_assignedlawfirm1>sprk_organization;"
        + "sprk_assignedlawfirm2>sprk_organization;sprk_assignedparalegal1>contact;sprk_assignedparalegal2>contact;"
        + "sprk_assignedtoexternal>contact;sprk_assignedtointernal>contact;sprk_regardingdocument>sprk_document;"
        + "sprk_regardingmatter>sprk_matter;sprk_regardingproject>sprk_project")]
    [InlineData("sprk_budget", "none",
        "createdby>systemuser;createdonbehalfby>systemuser;modifiedby>systemuser;modifiedonbehalfby>systemuser;"
        + "ownerid>systemuser|team;owningbusinessunit>businessunit;owningteam>team;owninguser>systemuser;"
        + "sprk_matter>sprk_matter;sprk_project>sprk_project;transactioncurrencyid>transactioncurrency")]
    // Task 147 r1: sprk_memo joined the CHILD taxonomy (owner round 2 item 6) — live sweep, Dataverse MCP describe,
    // read-only, 2026-10-04 (every lookup / owner column of sprk_memo with its target).
    [InlineData("sprk_memo", "both",
        "createdby>systemuser;createdonbehalfby>systemuser;modifiedby>systemuser;modifiedonbehalfby>systemuser;"
        + "ownerid>systemuser|team;owningbusinessunit>businessunit;owningteam>team;owninguser>systemuser;"
        + "sprk_regardingagreement>sprk_agreement;sprk_regardinganalysis>sprk_analysis;"
        + "sprk_regardingbudget>sprk_budget;sprk_regardingcommunication>sprk_communication;"
        + "sprk_regardingcontact>contact;sprk_regardingdocument>sprk_document;sprk_regardingevent>sprk_event;"
        + "sprk_regardinginvoice>sprk_invoice;sprk_regardingmatter>sprk_matter;"
        + "sprk_regardingorganization>sprk_organization;sprk_regardingproject>sprk_project;"
        + "sprk_regardingrecordtype>sprk_recordtype_ref;sprk_regardingservicerequest>sprk_servicerequest;"
        + "sprk_regardingtimekeeper>sprk_timekeeper;sprk_regardingworkassignment>sprk_workassignment;"
        + "sprk_reportcard>sprk_reportcard")]
    [InlineData("sprk_reportcard", "both",
        "createdby>systemuser;createdonbehalfby>systemuser;modifiedby>systemuser;modifiedonbehalfby>systemuser;"
        + "ownerid>systemuser|team;owningbusinessunit>businessunit;owningteam>team;owninguser>systemuser;"
        + "sprk_assignedattorney1>contact;sprk_assignedattorney2>contact;sprk_assignedlawfirm2>sprk_organization;"
        + "sprk_assignedparalegal1>contact;sprk_assignedparalegal2>contact;sprk_assignedtoexternal>contact;"
        + "sprk_assignedtointernal>contact;sprk_assignedtolawfirm1>sprk_organization;"
        + "sprk_regardingmatter>sprk_matter;sprk_regardingproject>sprk_project;"
        + "sprk_regardingrecordtype>sprk_recordtype_ref")]
    public async Task ChildRecordRead_RequestsEveryLinkAndIntermediateColumn(
        string entity, string pairShape, string sweptLookups)
    {
        var hasPolymorphicPair = pairShape != "none";
        // The data is a LITERAL snapshot of the f3 live sweep — spaarkedev1, read-only, 2026-10-01: EVERY
        // Lookup / Customer / Owner column of the entity with its targets, from
        // EntityDefinitions(LogicalName='x')/Attributes/Microsoft.Dynamics.CRM.LookupAttributeMetadata — and the pair
        // flag is whether the entity has a sprk_regardingrecordid column (same sweep). The f2 version of this test
        // claimed "verified on live" for a hand-picked list that missed sprk_invoice.sprk_regardingagreement; here the
        // expected read is DERIVED from the whole snapshot by target kind, so leaving out a column whose target is or
        // can hang off a root — from the table OR from the read — goes red, and so does reading anything else.
        var swept = sweptLookups.Split(';')
            .Select(item => item.Split('>'))
            .Select(parts => (Column: parts[0], Targets: parts[1].Split('|')))
            .ToList();

        foreach (var target in swept.SelectMany(c => c.Targets))
        {
            (RecordContainerResolver.ChildAncestorLinks.KindOf(target) is not null || NonOwnerTargets.Contains(target))
                .Should().BeTrue($"'{target}' must be classified (root / intermediate / party, or a non-owner reference) "
                                 + "before a re-sweep that contains it can pass");
        }

        var ownershipColumns = swept
            .Where(c => c.Targets.Any(t => RecordContainerResolver.ChildAncestorLinks.KindOf(t) is
                RecordContainerResolver.ChildAncestorLinks.RecordKind.Root
                or RecordContainerResolver.ChildAncestorLinks.RecordKind.Intermediate))
            .Select(c => c.Column);

        // f5: on a row that carries the pair, its typed PARTY REGARDING lookups ride on the same read — the pair's rule 3
        // compares its id with them (the outbound sender writes a party's typed lookup and the pair id, never the type).
        // Derived by name and target kind: a sprk_regarding* column whose target is a party. Assignees, vendors and
        // law firms are parties too, but no builder pairs them with the pair id, so they are not read.
        var partyRegardingColumns = swept
            .Where(c => c.Column.StartsWith("sprk_regarding", StringComparison.Ordinal)
                        && c.Targets.Any(t => RecordContainerResolver.ChildAncestorLinks.KindOf(t) is
                            RecordContainerResolver.ChildAncestorLinks.RecordKind.Party))
            .Select(c => c.Column);

        // Task 150 (owner round 10 item 11): the invoice's table carries sprk_issecure (this world declares it, as live
        // metadata does), but its flag is not a security input — so its row is read for its links only, never for a flag
        // or a container of its own.
        var securable = SecurableWithInvoice.Contains(entity) && entity != "sprk_invoice";
        var expected = ownershipColumns
            .Concat(pairShape switch
            {
                "both" => ["sprk_regardingrecordid", "sprk_regardingrecordtype"],
                "id" => ["sprk_regardingrecordid"],
                _ => Array.Empty<string>(),
            })
            .Concat(hasPolymorphicPair ? partyRegardingColumns : Array.Empty<string>())
            .Concat(securable ? ["sprk_issecure", "sprk_containerid"] : Array.Empty<string>())
            .Append("owningbusinessunit")
            .ToList();

        var world = new World(securable: SecurableWithInvoice)
            .WithChild(entity, isSecure: securable ? false : null)
            .WithBusinessUnit(BusinessUnitContainer);

        await world.Resolver().ResolveForRecordAsync(entity, ChildId);

        world.Reads(entity).Should().Be(1);
        world.ColumnsRead(entity).Should().BeEquivalentTo(expected);
    }

    [Fact(DisplayName = "Task 155 (escalation trigger 1) / 156: sprk_document now has swept links — it is READ, never refused for unknown links (the guard that would refuse an unswept child is pinned by CoreAncestorStampTopologyLockstepTests)")]
    public async Task Document_HasKnownLinks_AndIsRead()
    {
        // Until task 156 sprk_document had no entry and this refused with no read (trigger 1). A child can now be filed
        // under a document, so the document is swept and read — and a document filed under nothing resolves its BU.
        var world = new World().WithRecord("sprk_document", owningBusinessUnit: true).WithBusinessUnit(BusinessUnitContainer);

        (await world.Resolver().ResolveForRecordAsync("sprk_document", ChildId)).ContainerId.Should().Be(BusinessUnitContainer);
        world.Reads("sprk_document").Should().Be(1);
    }

    // ============================================================================================
    // Cost — ONE child read carrying its links, ONE root read
    // ============================================================================================

    [Fact(DisplayName = "Task 155: the child's links ride on ONE record read, and the secure root costs ONE read")]
    public async Task Todo_UnderASecureProject_CostsOneChildReadAndOneRootRead()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId)
            .WithRoot("sprk_project", ProjectId, isSecure: true, RootContainer);

        await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        world.Reads("sprk_todo").Should().Be(1);
        world.Reads("sprk_project").Should().Be(1);
        world.TotalReads().Should().Be(2);

        world.ColumnsRead("sprk_todo").Should().Contain(
            CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute),
            "every root link must come back with the child row");
    }

    // ============================================================================================
    // Guard — every child type the upload routes accept has known links
    // ============================================================================================

    [Fact(DisplayName = "Task 155: every CHILD type the record-keyed upload routes accept has known root links (else every upload to it is refused)")]
    public void EveryRouteReachableChildType_HasKnownRootLinks()
    {
        var routeChildTypes = EntityAccessFilter.SupportedEntityTypes
            .Select(DocumentAssociationMap.ToLogicalName)
            .OfType<string>()
            .Where(CoreAncestorResolver.IsChildRecordEntity)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        routeChildTypes.Should().NotBeEmpty("the routes accept to-dos and events");
        routeChildTypes.Should().BeSubsetOf(RecordContainerResolver.ChildAncestorLinks.KnownChildEntities);
    }

    // ============================================================================================
    // The inbound communication pipeline — an email regarding an INVOICE (securable AND a child)
    // ============================================================================================

    private static readonly string[] SecurableWithInvoice =
        ["sprk_project", "sprk_matter", "sprk_workassignment", "sprk_invoice"];

    [Fact(DisplayName = "Task 155: an email regarding an invoice under a SECURE matter routes to the MATTER's own container, not the archive")]
    public async Task Communication_RegardingAnInvoiceUnderASecureMatter_RoutesToTheMattersContainer()
    {
        // The deliberate behaviour change: the invoice, a CHILD, resolves through its secure root. (This world declares
        // sprk_issecure on the invoice as live metadata does; since task 150 the invoice's own flag is not a security
        // input, so its matter alone decides.)
        var world = new World(securable: SecurableWithInvoice)
            .WithCommunicationRegardingInvoice(copyMatter: MatterId)
            .WithChild("sprk_invoice", typedMatter: MatterId, isSecure: false)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        var container = await world.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer);

        container.Should().Be(RootContainer,
            "the archive container is shared; an invoice under a secure matter is secure (owner C10 part 2)");
    }

    [Fact(DisplayName = "Task 155: an email regarding an invoice with NO secure root still routes to the archive container, as before")]
    public async Task Communication_RegardingAnInvoiceUnderANonSecureMatter_RoutesToTheArchiveAsBefore()
    {
        var world = new World(securable: SecurableWithInvoice)
            .WithCommunicationRegardingInvoice(copyMatter: MatterId)
            .WithChild("sprk_invoice", typedMatter: MatterId, isSecure: false)
            .WithRoot("sprk_matter", MatterId, isSecure: false, containerId: null)
            .WithBusinessUnit(BusinessUnitContainer);

        var container = await world.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer);

        container.Should().Be(ArchiveContainer);
    }

    [Fact(DisplayName = "Task 155: an email regarding an invoice under TWO secure roots is refused (container_ancestor_ambiguous) and the inbound processor treats it as PERMANENT")]
    public async Task Communication_RegardingAnInvoiceUnderTwoSecureRoots_IsAPermanentRefusal()
    {
        var world = new World(securable: SecurableWithInvoice)
            .WithCommunicationRegardingInvoice(copyMatter: MatterId, copyProject: ProjectId)
            .WithChild("sprk_invoice", typedMatter: MatterId, typedProject: ProjectId, isSecure: false)
            .WithRoot("sprk_project", ProjectId, isSecure: true, RootContainer)
            .WithRoot("sprk_matter", MatterId, isSecure: true, OtherRootContainer);

        var act = async () => await world.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorAmbiguousCode);
        IncomingCommunicationProcessor.IsPermanentContainerRefusal(ex).Should().BeTrue(
            "retrying cannot un-link a secure root; as 'transient' it is a retry loop that loses the message capture");
    }

    [Fact(DisplayName = "Task 155: an email regarding an invoice whose root does NOT EXIST is a PERMANENT refusal (409); an UNREADABLE root is transient (503)")]
    public async Task Communication_RegardingAnInvoiceWithAMissingOrUnreadableRoot_IsClassifiedByStatus()
    {
        var missing = new World(securable: SecurableWithInvoice)
            .WithCommunicationRegardingInvoice(copyMatter: MatterId)
            .WithChild("sprk_invoice", typedMatter: MatterId, isSecure: false)
            .WithRootFault("sprk_matter", MatterId, new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = -2147220969 }, new FaultReason("does not exist")));

        var missingAct = async () =>
            await missing.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer);
        var missingEx = (await missingAct.Should().ThrowAsync<SdapProblemException>()).Which;
        missingEx.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
        missingEx.StatusCode.Should().Be(409);
        IncomingCommunicationProcessor.IsPermanentContainerRefusal(missingEx).Should().BeTrue();

        var unreadable = new World(securable: SecurableWithInvoice)
            .WithCommunicationRegardingInvoice(copyMatter: MatterId)
            .WithChild("sprk_invoice", typedMatter: MatterId, isSecure: false)
            .WithRootFault("sprk_matter", MatterId, new TimeoutException("Dataverse timed out"));

        var unreadableAct = async () =>
            await unreadable.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer);
        var unreadableEx = (await unreadableAct.Should().ThrowAsync<SdapProblemException>()).Which;
        unreadableEx.StatusCode.Should().Be(503);
        IncomingCommunicationProcessor.IsPermanentContainerRefusal(unreadableEx).Should().BeFalse(
            "an unreadable root may answer on retry; skipping it would withhold content that has a destination");
    }

    [Theory(DisplayName = "Task 155: the inbound processor's permanent/transient split — every permanent 409 container refusal skips, every read failure retries")]
    [InlineData("secure_record_container_missing", 409, true)]
    [InlineData("communication_secure_container_ambiguous", 409, true)]
    [InlineData("container_ancestor_ambiguous", 409, true)]
    [InlineData("container_ancestor_unverifiable", 409, true)]
    [InlineData("container_ancestor_unresolved", 409, true)]
    [InlineData("container_ancestor_unresolved", 503, false)]
    [InlineData("container_record_not_found", 404, false)]
    [InlineData("securable_entities_unknown", 409, false)]
    public void InboundProcessor_ClassifiesContainerRefusals(string code, int status, bool permanent)
    {
        // The last two rows pin that this task changed only the ancestor codes: pre-existing codes keep their
        // pre-existing (propagating) classification.
        var ex = new SdapProblemException(code, "t", statusCode: status);

        IncomingCommunicationProcessor.IsPermanentContainerRefusal(ex).Should().Be(permanent);
    }

    // ============================================================================================
    // Task 155 f3, item 1 (BLOCKING) — an invoice regarding an AGREEMENT
    // ============================================================================================

    [Theory(DisplayName = "Task 155 f3: an invoice regarding an agreement is refused (container_ancestor_unverifiable) — with no typed root, or with a typed link to a different NON-secure root — never the BU container")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invoice_RegardingAnAgreement_IsRefused(bool withTypedNonSecureProject)
    {
        // Live spaarkedev1: sprk_invoice.sprk_regardingagreement → sprk_agreement, and an agreement belongs to a matter /
        // project through its own sprk_regardingmatter / sprk_regardingproject. The f2 table gave the invoice NO
        // intermediate column, so an invoice regarding an agreement of a SECURE matter read "no secure root" and resolved
        // the shared business-unit container. The invoice is securable here, as it is live; its own flag is false.
        var world = new World(securable: SecurableWithInvoice)
            .WithChild("sprk_invoice",
                typedProject: withTypedNonSecureProject ? ProjectId : null,
                isSecure: false,
                intermediate: ("sprk_regardingagreement", AgreementId))
            .WithRoot("sprk_project", ProjectId, isSecure: false, containerId: null)
            .WithBusinessUnit(BusinessUnitContainer);

        foreach (var fallback in new[] { null, ArchiveContainer })
        {
            var act = async () => await world.Resolver().ResolveForRecordAsync("invoice", ChildId, fallback);

            var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
            ex.Code.Should().Be(RecordContainerResolver.AncestorUnverifiableCode);
            ex.StatusCode.Should().Be(409);
        }

        world.Reads("businessunit").Should().Be(0, "a shared container must never be in scope");
        world.RootReads().Should().Be(0, "the typed non-secure root is not consulted — consulting it is the fail-open");
    }

    [Fact(DisplayName = "Task 155 f3: an email regarding an invoice that regards an agreement is refused (unverifiable, PERMANENT) — never the archive container")]
    public async Task Communication_RegardingAnInvoiceThatRegardsAnAgreement_IsAPermanentRefusal()
    {
        // The communication path's fail-open: CommunicationContainerResolver asks about the securable invoice, which
        // read "no secure root", so the attachment went to the shared ARCHIVE container.
        var world = new World(securable: SecurableWithInvoice)
            .WithCommunicationRegardingInvoice()
            .WithChild("sprk_invoice", isSecure: false, intermediate: ("sprk_regardingagreement", AgreementId))
            .WithBusinessUnit(BusinessUnitContainer);

        var act = async () => await world.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorUnverifiableCode);
        IncomingCommunicationProcessor.IsPermanentContainerRefusal(ex).Should().BeTrue();
    }

    // ============================================================================================
    // Task 155 f3, item 3 — the POLYMORPHIC regarding pair (sprk_regardingrecordid + sprk_regardingrecordtype)
    // ============================================================================================

    [Fact(DisplayName = "Task 155 f3: an event linked to a SECURE matter ONLY through the polymorphic pair resolves the MATTER's own container — never the BU")]
    public async Task Event_LinkedOnlyByThePair_ToASecureMatter_ResolvesTheMattersContainer()
    {
        // Live: 10 events have every typed regarding NULL and the pair naming a sprk_matter. Ignoring the pair read
        // "no root" and resolved the business-unit container even if that matter is secure.
        var world = new World()
            .WithChild("sprk_event", pairId: MatterId.ToString("D").ToUpperInvariant(), pairType: MatterTypeRef)
            .WithRecordType(MatterTypeRef, "sprk_matter")
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        foreach (var fallback in new[] { null, ArchiveContainer })
        {
            var decision = await world.Resolver().ResolveForRecordAsync("event", ChildId, fallback);

            decision.Outcome.Should().Be(ContainerDecisionOutcome.ResolvedSecure);
            decision.ContainerId.Should().Be(RootContainer);
        }

        world.Reads("businessunit").Should().Be(0);
    }

    [Fact(DisplayName = "Task 155 f3: a pair-only link to a NON-secure matter resolves the BU container — at the cost of one type read and one root read")]
    public async Task Event_LinkedOnlyByThePair_ToANonSecureMatter_ResolvesItsBusinessUnit()
    {
        var world = new World()
            .WithChild("sprk_event", pairId: MatterId.ToString(), pairType: MatterTypeRef)
            .WithRecordType(MatterTypeRef, "sprk_matter")
            .WithRoot("sprk_matter", MatterId, isSecure: false, containerId: null)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_event", ChildId);

        decision.ContainerId.Should().Be(BusinessUnitContainer);
        world.Reads("sprk_event").Should().Be(1);
        world.Reads("sprk_recordtype_ref").Should().Be(1);
        world.Reads("sprk_matter").Should().Be(1);
        world.Reads("businessunit").Should().Be(1);
    }

    [Fact(DisplayName = "Task 155 f3: a pair naming a matter that does NOT EXIST is refused (container_ancestor_unresolved 409) — the 10 live dangling events")]
    public async Task Event_LinkedOnlyByThePair_ToAMatterThatDoesNotExist_IsRefused()
    {
        // The pair is a STRING with no referential integrity: deleting the matter leaves the id behind (all 10 live
        // pair-only events name matters that no longer exist). Unknown is not "no root".
        var world = new World()
            .WithChild("sprk_event", pairId: MatterId.ToString(), pairType: MatterTypeRef)
            .WithRecordType(MatterTypeRef, "sprk_matter")
            .WithRootFault("sprk_matter", MatterId, new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = -2147220969 }, new FaultReason("does not exist")))
            .WithBusinessUnit(BusinessUnitContainer);

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_event", ChildId);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
        ex.StatusCode.Should().Be(409);
        world.Reads("businessunit").Should().Be(0);
    }

    [Theory(DisplayName = "Task 155 f3: a pair naming an INTERMEDIATE type (a record that itself hangs off a root) takes the held path — container_ancestor_unverifiable")]
    [InlineData("sprk_communication")]
    [InlineData("sprk_servicerequest")]
    public async Task Pair_NamingAnIntermediate_IsRefusedAsUnverifiable(string intermediateType)
    {
        var typeRef = intermediateType == "sprk_servicerequest" ? ServiceRequestTypeRef : CommunicationTypeRef;
        var namedId = intermediateType == "sprk_servicerequest" ? ServiceRequestId : CommunicationId;

        var world = new World()
            .WithChild("sprk_todo", pairId: namedId.ToString(), pairType: typeRef)
            .WithRecordType(typeRef, intermediateType)
            .WithBusinessUnit(BusinessUnitContainer);

        foreach (var fallback in new[] { null, ArchiveContainer })
        {
            var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId, fallback);

            var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
            ex.Code.Should().Be(RecordContainerResolver.AncestorUnverifiableCode);
            ex.StatusCode.Should().Be(409);
        }

        world.Reads("businessunit").Should().Be(0);
        world.RootReads().Should().Be(0);
    }

    [Fact(DisplayName = "Task 155 f3: a pair naming a PARTY (contact) adds no root — the same as the typed party columns — and resolves the BU container")]
    public async Task Pair_NamingAParty_AddsNoRoot()
    {
        var world = new World()
            .WithChild("sprk_todo", pairId: ContactId.ToString(), pairType: ContactTypeRef)
            .WithRecordType(ContactTypeRef, "contact")
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        decision.ContainerId.Should().Be(BusinessUnitContainer);
        world.Reads("contact").Should().Be(0, "a party is referenced, never followed");
    }

    [Fact(DisplayName = "Task 155 f3: a pair with a record id but NO type is refused (container_ancestor_unresolved 409) — the live to-do 4ff4dc1f shape")]
    public async Task Pair_WithAnIdButNoType_IsRefused()
    {
        var world = new World()
            .WithChild("sprk_todo", pairId: MatterId.ToString())
            .WithBusinessUnit(BusinessUnitContainer);

        foreach (var fallback in new[] { null, ArchiveContainer })
        {
            var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId, fallback);

            var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
            ex.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
            ex.StatusCode.Should().Be(409);
        }

        world.Reads("businessunit").Should().Be(0);
    }

    [Fact(DisplayName = "Task 155 f3: a pair with a TYPE but no record id names no record — no type read, and the typed links decide (21 live events)")]
    public async Task Pair_WithATypeButNoId_NamesNoRecord()
    {
        var world = new World()
            .WithChild("sprk_event", pairType: MatterTypeRef)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_event", ChildId);

        decision.ContainerId.Should().Be(BusinessUnitContainer);
        world.Reads("sprk_recordtype_ref").Should().Be(0);
    }

    [Fact(DisplayName = "Task 155 f3: a pair whose record id is not a GUID is refused (container_ancestor_unresolved 409)")]
    public async Task Pair_WithAnUnparseableId_IsRefused()
    {
        var world = new World()
            .WithChild("sprk_todo", pairId: "MAT-2026-01234", pairType: MatterTypeRef)
            .WithBusinessUnit(BusinessUnitContainer);

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
        ex.StatusCode.Should().Be(409);
        world.Reads("businessunit").Should().Be(0);
    }

    [Theory(DisplayName = "Task 155 f3: a pair whose TYPE is unclassified, blank or missing is refused (409); an unreadable type row is 503 — never 'no root'")]
    // Task 147 r1: this row used sprk_memo as its unclassified type; the memo is an intermediate now (CHILD taxonomy), so
    // an unclassified table that names nothing above itself takes its place. The memo case is pinned below.
    [InlineData("sprk_spendsnapshot", 409)]
    [InlineData(null, 409)]
    [InlineData("<not-found>", 409)]
    [InlineData("<timeout>", 503)]
    public async Task Pair_WhoseTypeCannotBeClassified_IsRefused(string? typeRow, int expectedStatus)
    {
        var world = new World()
            .WithChild("sprk_todo", pairId: MatterId.ToString(), pairType: UnclassifiedTypeRef)
            .WithBusinessUnit(BusinessUnitContainer);

        _ = typeRow switch
        {
            "<not-found>" => world.WithRecordTypeFault(UnclassifiedTypeRef, new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = -2147220969 }, new FaultReason("does not exist"))),
            "<timeout>" => world.WithRecordTypeFault(UnclassifiedTypeRef, new TimeoutException("Dataverse timed out")),
            _ => world.WithRecordType(UnclassifiedTypeRef, typeRow),
        };

        foreach (var fallback in new[] { null, ArchiveContainer })
        {
            var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId, fallback);

            var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
            ex.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
            ex.StatusCode.Should().Be(expectedStatus);
        }

        world.Reads("businessunit").Should().Be(0);
        world.RootReads().Should().Be(0);
    }

    [Fact(DisplayName = "Task 147 r1: a to-do whose pair names a MEMO (an intermediate since task 147, never a to-do stamp source) is refused as unverifiable — never 'no root', never the BU")]
    public async Task Pair_NamingAMemo_OnAToDo_IsRefusedAsUnverifiable()
    {
        var memoTypeRef = Guid.Parse("14714714-0000-0000-0000-00000000000e");
        var world = new World()
            .WithChild("sprk_todo", pairId: MatterId.ToString(), pairType: memoTypeRef)
            .WithBusinessUnit(BusinessUnitContainer);
        world.WithRecordType(memoTypeRef, "sprk_memo");

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorUnverifiableCode);
        ex.StatusCode.Should().Be(409);
        world.Reads("businessunit").Should().Be(0);
    }

    [Fact(DisplayName = "Task 155 f3: a pair naming the SAME record as the typed root agrees by identity — no type read, and the secure root decides")]
    public async Task Pair_AgreeingWithTheTypedRoot_CostsNoTypeRead()
    {
        // The common live shape (the regarding builders stamp both): typed sprk_regardingproject = P, pair = P. The
        // pair's id is the typed link's id, so it names THAT record — upper-case, as live rows store it.
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId,
                pairId: ProjectId.ToString("D").ToUpperInvariant(), pairType: ProjectTypeRef)
            .WithRoot("sprk_project", ProjectId, isSecure: true, RootContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        decision.ContainerId.Should().Be(RootContainer);
        world.Reads("sprk_recordtype_ref").Should().Be(0);
        world.TotalReads().Should().Be(2, "the child and its one root — the pair adds no read when it agrees");
    }

    [Fact(DisplayName = "Task 155 f3: a typed root and a pair naming a DIFFERENT record DISAGREE — refused as ambiguous; neither is picked, nothing more is read")]
    public async Task Pair_DisagreeingWithTheTypedRoot_IsRefusedAsAmbiguous()
    {
        // Both secure, with different containers: picking either would put content where the other's members read it.
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId, pairId: MatterId.ToString(), pairType: MatterTypeRef)
            .WithRecordType(MatterTypeRef, "sprk_matter")
            .WithRoot("sprk_project", ProjectId, isSecure: true, RootContainer)
            .WithRoot("sprk_matter", MatterId, isSecure: true, OtherRootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        foreach (var fallback in new[] { null, ArchiveContainer })
        {
            var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId, fallback);

            var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
            ex.Code.Should().Be(RecordContainerResolver.AncestorAmbiguousCode);
            ex.StatusCode.Should().Be(409);
        }

        world.RootReads().Should().Be(0);
        world.Reads("businessunit").Should().Be(0);
    }

    // ============================================================================================
    // Task 155 f3, item 2 — roots and a party whose OWN rows can name a root (live sweep)
    // ============================================================================================

    [Fact(DisplayName = "Task 155 f3: a NON-secure work assignment filed regarding a SECURE matter resolves the MATTER's own container")]
    public async Task WorkAssignment_NotSecure_RegardingASecureMatter_ResolvesTheMattersContainer()
    {
        // Live: 9 of 22 work assignments carry sprk_regardingmatter / sprk_regardingproject. A work assignment is a
        // root with its own flag, but one that is not secure and is filed under a secure matter is that matter's child
        // (owner C10 part 2) — the same rule as a non-secure invoice under a secure matter.
        var world = new World()
            .WithChild("sprk_workassignment", regardingMatter: MatterId, isSecure: false)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("workassignment", ChildId);

        decision.ContainerId.Should().Be(RootContainer);
        world.Reads("businessunit").Should().Be(0);
    }

    [Fact(DisplayName = "Task 155 f3: a SECURE work assignment keeps its OWN container and never consults the matter it regards")]
    public async Task WorkAssignment_ItselfSecure_KeepsItsOwnContainer()
    {
        var world = new World()
            .WithChild("sprk_workassignment", regardingMatter: MatterId, isSecure: true, ownContainer: OtherRootContainer)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_workassignment", ChildId);

        decision.ContainerId.Should().Be(OtherRootContainer);
        world.RootReads().Should().Be(0);
    }

    /// <remarks>
    /// CONVERTED by task 150. This was "a work assignment with a NULL flag … resolves its BU container (live: most
    /// rows)". The owner's ABSENT-branch decision backfills every NULL <c>sprk_issecure</c> to No
    /// (<c>scripts/Repair-SecureFlagNulls.ps1</c>; the column already defaults to No, and every live NULL row predates
    /// the column), so the live shape is now an explicit FALSE — pinned here — and a NULL refuses (next test).
    /// </remarks>
    [Fact(DisplayName = "Task 155 f3 / 150: a work assignment flagged No under a NON-secure matter resolves its BU container")]
    public async Task WorkAssignment_FlaggedNo_UnderANonSecureMatter_ResolvesItsBusinessUnit()
    {
        var world = new World()
            .WithChild("sprk_workassignment", regardingMatter: MatterId, isSecure: false)
            .WithRoot("sprk_matter", MatterId, isSecure: false, containerId: null)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_workassignment", ChildId);

        decision.ContainerId.Should().Be(BusinessUnitContainer);
        world.Reads("sprk_matter").Should().Be(1);
    }

    [Fact(DisplayName = "Task 150: a work assignment whose OWN sprk_issecure is ABSENT is refused before its matter is read")]
    public async Task WorkAssignment_AbsentFlag_IsRefused()
    {
        var world = new World()
            .WithChild("sprk_workassignment", regardingMatter: MatterId)
            .WithRoot("sprk_matter", MatterId, isSecure: false, containerId: null)
            .WithBusinessUnit(BusinessUnitContainer);

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_workassignment", ChildId);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.Code.Should().Be(RecordContainerResolver.SecureFlagUnreadableCode);
        world.Reads("businessunit").Should().Be(0);
    }

    [Fact(DisplayName = "Task 155 f3: a NON-secure project whose polymorphic pair names a SECURE matter resolves the matter's container (live: 0 projects carry the pair)")]
    public async Task Project_NotSecure_WhosePairNamesASecureMatter_ResolvesTheMattersContainer()
    {
        var world = new World()
            .WithChild("sprk_project", isSecure: false, pairId: MatterId.ToString(), pairType: MatterTypeRef)
            .WithRecordType(MatterTypeRef, "sprk_matter")
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("project", ChildId);

        decision.ContainerId.Should().Be(RootContainer);
        world.Reads("businessunit").Should().Be(0);
    }

    // ============================================================================================
    // Task 155 f3, item 4 — a classification outside the registry's contract is REFUSED
    // ============================================================================================

    [Theory(DisplayName = "Task 155 f3: an undefined EntitySecurability value is refused (securable_entities_unknown 409) on both overloads, with no read — never the not-securable path")]
    [InlineData("contact", 3)]
    [InlineData("account", 99)]
    [InlineData("sprk_todo", -1)]
    [InlineData("sprk_project", 7)]
    public async Task UndefinedClassification_IsRefused(string entity, int undefinedValue)
    {
        // Before f3 the code computed isSecurable = (value == Securable), so an undefined value on an entity with no
        // ancestor links took the NOT-securable branch to a shared container — while the comment claimed the secure
        // path. account is the sharpest case: with a fallback it would have resolved it with zero reads.
        var world = new World()
            .WithClassification(entity, (EntitySecurability)undefinedValue)
            .WithChild(entity)
            .WithBusinessUnit(BusinessUnitContainer);

        foreach (var fallback in new[] { null, ArchiveContainer })
        {
            var act = async () => await world.Resolver().ResolveForRecordAsync(entity, ChildId, fallback);

            var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
            ex.Code.Should().Be("securable_entities_unknown");
            ex.StatusCode.Should().Be(409);
        }

        world.TotalReads().Should().Be(0);
    }

    // ============================================================================================
    // Task 155 f4, item 1 — the TRANSITIVE root walk (a root ABOVE a root)
    // ============================================================================================

    private static FaultException<OrganizationServiceFault> NotFound() =>
        new(new OrganizationServiceFault { ErrorCode = -2147220969 }, new FaultReason("does not exist"));

    [Theory(DisplayName = "Task 155 f4: a record under a NON-secure work assignment / project that is itself filed under a SECURE matter resolves the MATTER's container — never the BU (the two-hop fail-open)")]
    [InlineData("sprk_event", "typed work assignment")]
    [InlineData("sprk_todo", "typed work assignment")]
    [InlineData("sprk_todo", "pair naming a work assignment")]
    [InlineData("sprk_invoice", "typed project whose pair names the matter")]
    [InlineData("sprk_todo", "pair naming a project whose pair names the matter")]
    public async Task Record_UnderANonSecureRoot_FiledUnderASecureMatter_ResolvesTheMattersContainer(
        string entity, string shape)
    {
        // Live (read-only, 2026-10-02): event a30254d0 regards work assignment 9c0254d0, whose own row names a matter. f3
        // stored the work assignment's OWN files in that matter's container when the matter is secure (interpretation
        // iii), but the event read only the work assignment's flag — "not secure" — and took the shared BU container.
        var world = new World().WithBusinessUnit(BusinessUnitContainer);

        _ = shape switch
        {
            "typed work assignment" => world
                .WithChild(entity, regardingWorkAssignment: WorkAssignmentId)
                .WithRoot("sprk_workassignment", WorkAssignmentId, isSecure: false, containerId: null,
                    regardingMatter: MatterId),
            "pair naming a work assignment" => world
                .WithChild(entity, pairId: WorkAssignmentId.ToString("D").ToUpperInvariant(),
                    pairType: WorkAssignmentTypeRef)
                .WithRecordType(WorkAssignmentTypeRef, "sprk_workassignment")
                .WithRoot("sprk_workassignment", WorkAssignmentId, isSecure: false, containerId: null,
                    regardingMatter: MatterId),
            "typed project whose pair names the matter" => world
                .WithChild(entity, typedProject: ProjectId)
                .WithRoot("sprk_project", ProjectId, isSecure: false, containerId: null,
                    pairId: MatterId.ToString(), pairType: MatterTypeRef)
                .WithRecordType(MatterTypeRef, "sprk_matter"),
            "pair naming a project whose pair names the matter" => world
                .WithChild(entity, pairId: ProjectId.ToString(), pairType: ProjectTypeRef)
                .WithRecordType(ProjectTypeRef, "sprk_project")
                .WithRoot("sprk_project", ProjectId, isSecure: false, containerId: null,
                    pairId: MatterId.ToString(), pairType: MatterTypeRef)
                .WithRecordType(MatterTypeRef, "sprk_matter"),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        world.WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer);

        foreach (var fallback in new[] { null, ArchiveContainer })
        {
            var decision = await world.Resolver().ResolveForRecordAsync(entity, ChildId, fallback);

            decision.Outcome.Should().Be(ContainerDecisionOutcome.ResolvedSecure, shape);
            decision.ContainerId.Should().Be(RootContainer,
                "a secure root anywhere above the record decides — never a shared container (owner C10 part 2)");
        }

        world.Reads("businessunit").Should().Be(0, "a usable shared container must never be in scope");
    }

    [Fact(DisplayName = "Task 155 f4: an event under a NON-secure work assignment under a NON-secure matter resolves its BU container — ONE read per row (event, work assignment, matter, BU)")]
    public async Task Event_UnderANonSecureWorkAssignment_UnderANonSecureMatter_CostsOneReadPerRow()
    {
        var world = new World()
            .WithChild("sprk_event", regardingWorkAssignment: WorkAssignmentId)
            .WithRoot("sprk_workassignment", WorkAssignmentId, isSecure: false, containerId: null,
                regardingMatter: MatterId)
            .WithRoot("sprk_matter", MatterId, isSecure: false, containerId: null)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_event", ChildId);

        decision.ContainerId.Should().Be(BusinessUnitContainer);
        world.Reads("sprk_event").Should().Be(1);
        world.Reads("sprk_workassignment").Should().Be(1);
        world.Reads("sprk_matter").Should().Be(1);
        world.TotalReads().Should().Be(4, "the work assignment's links ride on its one read — no extra round trip");
        world.ColumnsRead("sprk_workassignment").Should().Contain(
            ["sprk_issecure", "sprk_containerid", "sprk_regardingmatter", "sprk_regardingproject",
             "sprk_regardingrecordid", "sprk_regardingrecordtype", "sprk_regardinginvoice"],
            "a root above the record is read with its OWN links, so the walk can continue from it");
    }

    [Fact(DisplayName = "Task 155 f4: an event under a work assignment whose pair names a matter that NO LONGER EXISTS is refused (container_ancestor_unresolved 409) — the live a30254d0 → 9c0254d0 shape")]
    public async Task Event_UnderAWorkAssignmentWhosePairNamesADeletedMatter_IsRefused()
    {
        var world = new World()
            .WithChild("sprk_event", regardingWorkAssignment: WorkAssignmentId)
            .WithRoot("sprk_workassignment", WorkAssignmentId, isSecure: false, containerId: null,
                pairId: MatterId.ToString(), pairType: MatterTypeRef)
            .WithRecordType(MatterTypeRef, "sprk_matter")
            .WithRootFault("sprk_matter", MatterId, NotFound())
            .WithBusinessUnit(BusinessUnitContainer);

        foreach (var fallback in new[] { null, ArchiveContainer })
        {
            var act = async () => await world.Resolver().ResolveForRecordAsync("event", ChildId, fallback);

            var ex = (await act.Should().ThrowAsync<SdapProblemException>(
                "a missing hop is unknown, and unknown is never 'not secure'")).Which;
            ex.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
            ex.StatusCode.Should().Be(409, "a missing record does not appear on retry");
        }

        world.Reads("businessunit").Should().Be(0);
    }

    [Fact(DisplayName = "Task 155 f4: a row two hops above the record that cannot be READ refuses with the retryable 503 — never read as non-secure")]
    public async Task Todo_WhoseRootTwoHopsUpCannotBeRead_IsThe503()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingWorkAssignment: WorkAssignmentId)
            .WithRoot("sprk_workassignment", WorkAssignmentId, isSecure: false, containerId: null,
                regardingMatter: MatterId)
            .WithRootFault("sprk_matter", MatterId, new TimeoutException("Dataverse timed out"))
            .WithBusinessUnit(BusinessUnitContainer);

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
        ex.StatusCode.Should().Be(503);
        ex.Detail.Should().NotContain("timed out", "the raw fault text belongs in the log");
        world.Reads("businessunit").Should().Be(0);
    }

    [Fact(DisplayName = "Task 155 f4: a to-do under a work assignment that is itself filed under an INVOICE takes the held path (unverifiable) — the live work assignment b10b7dab shape, one level down")]
    public async Task Todo_UnderAWorkAssignmentFiledUnderAnInvoice_IsRefusedAsUnverifiable()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingWorkAssignment: WorkAssignmentId)
            .WithRoot("sprk_workassignment", WorkAssignmentId, isSecure: false, containerId: null,
                regardingMatter: MatterId, intermediate: ("sprk_regardinginvoice", ChildId))
            .WithRoot("sprk_matter", MatterId, isSecure: false, containerId: null)
            .WithBusinessUnit(BusinessUnitContainer);

        foreach (var fallback in new[] { null, ArchiveContainer })
        {
            var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId, fallback);

            var ex = (await act.Should().ThrowAsync<SdapProblemException>(
                "the work assignment itself cannot be placed, so neither can what is filed under it")).Which;
            ex.Code.Should().Be(RecordContainerResolver.AncestorUnverifiableCode);
            ex.StatusCode.Should().Be(409);
            ex.Detail.Should().Contain("sprk_workassignment", "the advice names the record that is mis-filed");
        }

        world.Reads("sprk_matter").Should().Be(0, "the held work assignment's typed matter is not consulted");
        world.Reads("businessunit").Should().Be(0);
    }

    [Theory(DisplayName = "Task 155 f4: two DIFFERENT secure roots anywhere in the chain refuse as ambiguous (container_ancestor_ambiguous) — on two branches, or stacked one above the other")]
    [InlineData("branches")]
    [InlineData("stacked")]
    public async Task TwoDifferentSecureRoots_AnywhereInTheChain_AreAmbiguous(string shape)
    {
        var world = shape == "branches"
            // The to-do names a secure project directly AND a non-secure work assignment that sits under a secure matter.
            ? new World()
                .WithChild("sprk_todo", regardingProject: ProjectId, regardingWorkAssignment: WorkAssignmentId)
                .WithRoot("sprk_project", ProjectId, isSecure: true, RootContainer)
                .WithRoot("sprk_workassignment", WorkAssignmentId, isSecure: false, containerId: null,
                    regardingMatter: MatterId)
                .WithRoot("sprk_matter", MatterId, isSecure: true, OtherRootContainer)
            // A SECURE work assignment (own container) filed regarding a DIFFERENT secure matter. The walk continues past
            // the first secure root to find the second (owner-reversible interpretation vi).
            : new World()
                .WithChild("sprk_todo", regardingWorkAssignment: WorkAssignmentId)
                .WithRoot("sprk_workassignment", WorkAssignmentId, isSecure: true, OtherRootContainer,
                    regardingMatter: MatterId)
                .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer);

        world.WithBusinessUnit(BusinessUnitContainer);

        foreach (var fallback in new[] { null, ArchiveContainer })
        {
            var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId, fallback);

            var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
            ex.Code.Should().Be(RecordContainerResolver.AncestorAmbiguousCode);
            ex.StatusCode.Should().Be(409);
        }

        world.Reads("businessunit").Should().Be(0);
    }

    [Fact(DisplayName = "Task 155 f4: the SAME secure root reached twice (a diamond) is ONE root — its container, read once")]
    public async Task Diamond_TheSameSecureRootReachedTwice_IsOneRoot()
    {
        // The to-do names matter M directly AND through its work assignment, which regards M too.
        var world = new World()
            .WithChild("sprk_todo", regardingMatter: MatterId, regardingWorkAssignment: WorkAssignmentId)
            .WithRoot("sprk_workassignment", WorkAssignmentId, isSecure: false, containerId: null,
                regardingMatter: MatterId)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        decision.ContainerId.Should().Be(RootContainer, "one record reached by two paths is not two secure roots");
        world.Reads("sprk_matter").Should().Be(1);
    }

    [Fact(DisplayName = "Task 155 f4: a CYCLE (work assignments naming each other through the pair) is guarded — each row is read once, and the walk ends")]
    public async Task Cycle_IsGuarded_EachRowReadOnce()
    {
        var (first, second) = (WorkAssignmentChain[0], WorkAssignmentChain[1]);
        var world = new World()
            .WithChild("sprk_todo", regardingWorkAssignment: first)
            .WithRecordType(WorkAssignmentTypeRef, "sprk_workassignment")
            .WithRoot("sprk_workassignment", first, isSecure: false, containerId: null,
                pairId: second.ToString(), pairType: WorkAssignmentTypeRef)
            .WithRoot("sprk_workassignment", second, isSecure: false, containerId: null,
                pairId: first.ToString(), pairType: WorkAssignmentTypeRef)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        decision.ContainerId.Should().Be(BusinessUnitContainer);
        world.Reads("sprk_workassignment").Should().Be(2, "a record already on the walk is not read again");
    }

    [Fact(DisplayName = "Task 155 f4: a cycle back to the record ITSELF (a project whose pair names a work assignment that regards the project) is guarded")]
    public async Task Cycle_BackToTheRecordItself_IsGuarded()
    {
        var world = new World()
            .WithChild("sprk_project", isSecure: false, pairId: WorkAssignmentId.ToString(), pairType: WorkAssignmentTypeRef)
            .WithRecordType(WorkAssignmentTypeRef, "sprk_workassignment")
            .WithRoot("sprk_workassignment", WorkAssignmentId, isSecure: false, containerId: null,
                regardingProject: ChildId)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_project", ChildId);

        decision.ContainerId.Should().Be(BusinessUnitContainer);
        world.Reads("sprk_project").Should().Be(1, "only the record's own read — the cycle back to it is not followed");
    }

    [Theory(DisplayName = "Task 155 f4: the walk is BOUNDED — a chain exactly MaxRootChainDepth deep resolves; one row deeper is REFUSED (409), never truncated into 'no secure root'")]
    [InlineData(RecordContainerResolver.MaxRootChainDepth, false)]
    [InlineData(RecordContainerResolver.MaxRootChainDepth + 1, true)]
    public async Task ChainDepth_IsBounded(int depth, bool refused)
    {
        // to-do → WA[0] → (pair) WA[1] → … → WA[depth-1]; the LAST one is secure.
        var world = new World()
            .WithChild("sprk_todo", regardingWorkAssignment: WorkAssignmentChain[0])
            .WithRecordType(WorkAssignmentTypeRef, "sprk_workassignment")
            .WithBusinessUnit(BusinessUnitContainer);

        for (var i = 0; i < depth; i++)
        {
            var isLast = i == depth - 1;
            world.WithRoot("sprk_workassignment", WorkAssignmentChain[i], isSecure: isLast,
                isLast ? RootContainer : null,
                pairId: isLast ? null : WorkAssignmentChain[i + 1].ToString(),
                pairType: isLast ? null : WorkAssignmentTypeRef);
        }

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        if (refused)
        {
            var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
            ex.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
            ex.StatusCode.Should().Be(409);
            world.Reads("sprk_workassignment").Should().Be(RecordContainerResolver.MaxRootChainDepth,
                "the row past the bound is never read");
            world.Reads("businessunit").Should().Be(0);
        }
        else
        {
            (await act()).ContainerId.Should().Be(RootContainer);
        }
    }

    [Fact(DisplayName = "Task 155 f4: the walk is bounded in BREADTH too — more than MaxRootReads rows above the record is REFUSED (409)")]
    public async Task RowsAboveTheRecord_AreBounded()
    {
        // Every row non-secure except the last, and every chain at most three deep, so ONLY the breadth bound can stop it.
        // Level 1 (3 rows): project P1 (its pair → WA[1]), matter M1, WA[0] (→ OtherProject, OtherMatter).
        // Level 2 (6): WA[1] (→ project WA[2]-id, matter WA[3]-id), OtherProject (its pair → WA[4]), OtherMatter.
        // Level 3: rows 7 and 8 are read; the 9th — the SECURE WA[4] — would exceed MaxRootReads (8) and is refused.
        // (A typed root and a pair naming a DIFFERENT record on one row is the ambiguity rule, so each row branches
        // through its typed links OR its pair, never both.)
        var world = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId, regardingMatter: MatterId,
                regardingWorkAssignment: WorkAssignmentChain[0])
            .WithRecordType(WorkAssignmentTypeRef, "sprk_workassignment")
            .WithRoot("sprk_project", ProjectId, isSecure: false, containerId: null,
                pairId: WorkAssignmentChain[1].ToString(), pairType: WorkAssignmentTypeRef)
            .WithRoot("sprk_matter", MatterId, isSecure: false, containerId: null)
            .WithRoot("sprk_workassignment", WorkAssignmentChain[0], isSecure: false, containerId: null,
                regardingProject: OtherProjectId, regardingMatter: OtherMatterId)
            .WithRoot("sprk_workassignment", WorkAssignmentChain[1], isSecure: false, containerId: null,
                regardingProject: WorkAssignmentChain[2], regardingMatter: WorkAssignmentChain[3])
            .WithRoot("sprk_project", OtherProjectId, isSecure: false, containerId: null,
                pairId: WorkAssignmentChain[4].ToString(), pairType: WorkAssignmentTypeRef)
            .WithRoot("sprk_matter", OtherMatterId, isSecure: false, containerId: null)
            .WithRoot("sprk_project", WorkAssignmentChain[2], isSecure: false, containerId: null)
            .WithRoot("sprk_matter", WorkAssignmentChain[3], isSecure: false, containerId: null)
            .WithRoot("sprk_workassignment", WorkAssignmentChain[4], isSecure: true, RootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
        ex.StatusCode.Should().Be(409);
        world.Reads("sprk_workassignment").Should().Be(2, "the secure row past the bound is never reached");
        world.Reads("businessunit").Should().Be(0);
    }

    [Fact(DisplayName = "Task 155 f4: a root type that CANNOT be secure but whose row names a root above it is still followed — its links read, its flag not")]
    public async Task NonSecurableRootType_WithLinks_IsStillFollowed()
    {
        var world = new World(securable: ["sprk_project", "sprk_matter"])
            .WithChild("sprk_todo", regardingWorkAssignment: WorkAssignmentId)
            .WithRoot("sprk_workassignment", WorkAssignmentId, isSecure: false, containerId: null,
                regardingMatter: MatterId)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        decision.ContainerId.Should().Be(RootContainer);
        world.ColumnsRead("sprk_workassignment").Should().NotContain("sprk_issecure",
            "reading a flag the entity does not carry would fault");
        world.ColumnsRead("sprk_workassignment").Should().Contain("sprk_regardingmatter");
    }

    [Fact(DisplayName = "Task 155 f4: a to-do under a work assignment under a SECURE matter with NO container FAILS CLOSED (secure_record_container_missing) — the BU is never read")]
    public async Task Todo_UnderAWorkAssignment_UnderASecureMatterWithoutContainer_FailsClosed()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingWorkAssignment: WorkAssignmentId)
            .WithRoot("sprk_workassignment", WorkAssignmentId, isSecure: false, containerId: null,
                regardingMatter: MatterId)
            .WithRoot("sprk_matter", MatterId, isSecure: true, containerId: null)
            .WithBusinessUnit(BusinessUnitContainer);

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId, ArchiveContainer);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be("secure_record_container_missing");
        world.Reads("businessunit").Should().Be(0);
    }

    [Fact(DisplayName = "Task 155 f4: a row ABOVE the record whose typed root and pair DISAGREE refuses as ambiguous — the pair rules hold on every row the walk reads")]
    public async Task Hop_WhosePairDisagreesWithItsTypedRoot_IsAmbiguous()
    {
        var world = new World()
            .WithChild("sprk_todo", regardingWorkAssignment: WorkAssignmentId)
            .WithRoot("sprk_workassignment", WorkAssignmentId, isSecure: false, containerId: null,
                regardingMatter: MatterId, pairId: OtherProjectId.ToString(), pairType: ProjectTypeRef)
            .WithBusinessUnit(BusinessUnitContainer);

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorAmbiguousCode);
        ex.Detail.Should().Contain("sprk_workassignment");
        world.Reads("sprk_matter").Should().Be(0, "neither side of a disagreement is picked");
    }

    // ============================================================================================
    // Task 155 f4, item 2 — the COMMUNICATION is the record (the same child resolution)
    // ============================================================================================

    [Fact(DisplayName = "Task 155 f4: an email linked to a SECURE matter ONLY through the polymorphic pair routes to the MATTER's container — never the archive")]
    public async Task Communication_LinkedOnlyByThePair_ToASecureMatter_RoutesToTheMattersContainer()
    {
        // Live: 161 of 276 communications carry the pair. The f3 adapter read only the SECURABLE typed regardings.
        var world = new World()
            .WithCommunication(pairId: MatterId.ToString("D").ToUpperInvariant(), pairType: MatterTypeRef)
            .WithRecordType(MatterTypeRef, "sprk_matter")
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer);

        foreach (var archive in new[] { ArchiveContainer, null })
        {
            var container = await world.CommunicationResolver().ResolveContainerAsync(CommunicationId, archive);

            container.Should().Be(RootContainer);
        }
    }

    // Task 156: an email filed under an event / analysis / budget / report card is now compared live (StampFreshness
    // EveryStampSource rows for sprk_communication); only the service request stays held.
    [Theory(DisplayName = "Task 155 f4 / 156: an email filed under a SERVICE REQUEST takes the held path — a PERMANENT refusal, never the archive")]
    [InlineData("sprk_regardingservicerequest")]
    public async Task Communication_FiledUnderANonSecurableIntermediate_IsAPermanentRefusal(string column)
    {
        // None of the five is securable, so the f3 adapter never asked about them and sent the content to the archive —
        // even when the service request / event / … belongs to a SECURE matter. Live: 1 event, 1 analysis.
        var world = new World()
            .WithCommunication(intermediate: (column, ServiceRequestId));

        var act = async () => await world.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorUnverifiableCode);
        IncomingCommunicationProcessor.IsPermanentContainerRefusal(ex).Should().BeTrue(
            "retrying cannot place it; as 'transient' it is a retry loop that loses the message capture");
    }

    [Fact(DisplayName = "Task 155 f4: an email regarding a NON-secure work assignment under a SECURE matter routes to the MATTER's container (transitive)")]
    public async Task Communication_UnderANonSecureWorkAssignment_UnderASecureMatter_RoutesToTheMattersContainer()
    {
        var world = new World()
            .WithCommunication(regardingWorkAssignment: WorkAssignmentId)
            .WithRoot("sprk_workassignment", WorkAssignmentId, isSecure: false, containerId: null,
                regardingMatter: MatterId)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer);

        var container = await world.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer);

        container.Should().Be(RootContainer);
    }

    [Fact(DisplayName = "Task 155 f4: an email whose pair names a matter that NO LONGER EXISTS is a PERMANENT refusal (409)")]
    public async Task Communication_WhosePairNamesADeletedMatter_IsAPermanentRefusal()
    {
        var world = new World()
            .WithCommunication(pairId: MatterId.ToString(), pairType: MatterTypeRef)
            .WithRecordType(MatterTypeRef, "sprk_matter")
            .WithRootFault("sprk_matter", MatterId, NotFound());

        var act = async () => await world.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
        ex.StatusCode.Should().Be(409);
        IncomingCommunicationProcessor.IsPermanentContainerRefusal(ex).Should().BeTrue();
    }

    [Fact(DisplayName = "Task 155 f4: an email with NO link to any root (a person, its thread, a triage category) keeps the ARCHIVE — and with no archive configured, NOTHING; its business unit is never derived")]
    public async Task Communication_WithNoLinkToAnyRoot_KeepsTheArchive_AndNeverDerivesABusinessUnit()
    {
        var world = new World()
            .WithCommunication(withNonOwnerLinks: true)
            .WithBusinessUnit(BusinessUnitContainer);

        (await world.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer))
            .Should().Be(ArchiveContainer, "the archive fallback stays when no root can be involved");
        (await world.CommunicationResolver().ResolveContainerAsync(CommunicationId, archiveContainerId: null))
            .Should().BeNull("an unconfigured archive has always meant 'skip' — not the communication's BU container");

        world.Reads("businessunit").Should().Be(0);
        world.TotalReads().Should().Be(2, "the communication row, once per call — a person, a thread and a triage "
                                           + "category are not ownership and are never followed");
    }

    [Fact(DisplayName = "Task 155 f4: an email regarding TWO DIFFERENT secure records (a secure matter, and an invoice under a different secure project) is a PERMANENT ambiguity refusal")]
    public async Task Communication_RegardingTwoDifferentSecureRecords_IsAPermanentRefusal()
    {
        // The row IncomingAssociationResolver writes for a matter AND an invoice: the pair names the primary — the matter
        // (RegardingFieldMap priority) — so the matter is a direct link and the invoice a carrier (task 156).
        var world = new World(securable: SecurableWithInvoice)
            .WithCommunication(regardingMatter: MatterId, regardingInvoice: ChildId, pairId: MatterId.ToString())
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer)
            .WithChild("sprk_invoice", typedProject: ProjectId, isSecure: false)
            .WithRoot("sprk_project", ProjectId, isSecure: true, OtherRootContainer);

        var act = async () => await world.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorAmbiguousCode);
        IncomingCommunicationProcessor.IsPermanentContainerRefusal(ex).Should().BeTrue();
    }

    // f5: live communications 83349fe9 and 84d04780 were cited here in f4. Neither is this shape: each pair names the row's
    // OWN typed person, which rule 3 now reads as agreement (see the f5 tests below). No live row is this shape.
    [Fact(DisplayName = "Task 155 f4: an email whose typed matter and polymorphic pair name DIFFERENT records is a PERMANENT ambiguity refusal")]
    public async Task Communication_WhoseTypedRegardingAndPairDisagree_IsAPermanentRefusal()
    {
        var world = new World()
            .WithCommunication(regardingMatter: MatterId, pairId: OtherMatterId.ToString(), pairType: MatterTypeRef)
            .WithRoot("sprk_matter", MatterId, isSecure: false, containerId: null);

        var act = async () => await world.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorAmbiguousCode);
        IncomingCommunicationProcessor.IsPermanentContainerRefusal(ex).Should().BeTrue();
        world.RootReads().Should().Be(0, "neither side of a disagreement is picked");
    }

    [Fact(DisplayName = "Task 155 f4: an email whose OWN row cannot be read is the transient 503 (retried), and one that does not exist is the pre-existing propagating 404")]
    public async Task Communication_WhoseRowCannotBeRead_IsTransient()
    {
        var unreadable = new World().WithCommunicationFault(new TimeoutException("Dataverse timed out"));
        var unreadableAct = async () =>
            await unreadable.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer);

        var unreadableEx = (await unreadableAct.Should().ThrowAsync<SdapProblemException>()).Which;
        unreadableEx.StatusCode.Should().Be(503);
        IncomingCommunicationProcessor.IsPermanentContainerRefusal(unreadableEx).Should().BeFalse(
            "a row that may answer on retry must not be skipped");

        var missing = new World().WithCommunicationFault(NotFound());
        var missingAct = async () =>
            await missing.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer);

        var missingEx = (await missingAct.Should().ThrowAsync<SdapProblemException>()).Which;
        missingEx.Code.Should().Be("container_record_not_found");
        IncomingCommunicationProcessor.IsPermanentContainerRefusal(missingEx).Should().BeFalse();
    }

    [Fact(DisplayName = "Task 155 f4: an EMPTY securable-entity set still refuses (securable_entities_unknown) before anything is read — never the archive")]
    public async Task Communication_WithNoSecurableEntitiesKnown_IsRefusedBeforeAnyRead()
    {
        var world = new World(securable: [])
            .WithCommunication(regardingMatter: MatterId)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer);

        var act = async () => await world.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be("securable_entities_unknown");
        world.TotalReads().Should().Be(0);
    }

    // ============================================================================================
    // Task 155 f5 — the row the OUTBOUND sender actually writes; rule 3 reads the row's typed PARTY regarding
    // ============================================================================================

    [Theory(DisplayName = "Task 155 f5: an outbound email regarding a person / organization / account — the row the REAL sender writes (typed party + pair id, NO type) — keeps the ARCHIVE; nothing but its own row is read")]
    [InlineData("contact", "sprk_regardingperson")]
    [InlineData("sprk_organization", "sprk_regardingorganization")]
    [InlineData("account", "sprk_regardingaccount")]
    public async Task OutboundEmail_RegardingAParty_AsTheSenderWritesIt_KeepsTheArchive(string partyEntity, string typedColumn)
    {
        // f4 refused this 409 container_ancestor_unresolved (permanent): the pair id named no followed link and carried
        // no type. Every ArchiveToSpe send regarding a person, organization or account lost its .eml (live d3516503).
        var written = await OutboundCommunicationRow.WriteAsync(partyEntity, ContactId);

        written.GetAttributeValue<EntityReference>(typedColumn)?.Id.Should().Be(ContactId);
        written.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(ContactId.ToString());
        written.Contains("sprk_regardingrecordtype").Should().BeFalse(
            "MapAssociationFieldsAsync never writes the pair's type — this IS the shape the resolver must place");

        var world = new World().WithCommunicationRow(written).WithBusinessUnit(BusinessUnitContainer);

        (await world.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer))
            .Should().Be(ArchiveContainer, "a person, organization or account is not ownership — no root can be involved");
        (await world.CommunicationResolver().ResolveContainerAsync(CommunicationId, archiveContainerId: null))
            .Should().BeNull("an unconfigured archive still means 'skip'");

        world.TotalReads().Should().Be(2, "the communication row once per call: no type read, no party read, no BU");
    }

    [Fact(DisplayName = "Task 155 f5: an outbound email regarding a SECURE matter — the row the REAL sender writes — routes to the MATTER's container (the writer-shape harness reaches the walk)")]
    public async Task OutboundEmail_RegardingASecureMatter_AsTheSenderWritesIt_RoutesToTheMattersContainer()
    {
        var written = await OutboundCommunicationRow.WriteAsync("sprk_matter", MatterId);

        var world = new World()
            .WithCommunicationRow(written)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer);

        (await world.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer))
            .Should().Be(RootContainer);
        world.Reads("sprk_recordtype_ref").Should().Be(0, "the pair id is the typed matter's id — agreement by identity");
    }

    [Theory(DisplayName = "Task 155 f5: an outbound email whose primary is a type the sender does NOT map (to-do, document) — a pair id with no typed column and no type — stays a PERMANENT refusal: a to-do or a document can belong to a secure matter")]
    [InlineData("sprk_todo")]
    [InlineData("sprk_document")]
    public async Task OutboundEmail_RegardingAnUnmappedType_AsTheSenderWritesIt_IsRefused(string entityType)
    {
        // Live: 4971a3c2 (the to-do wizard's follow-on email) and ab302254 (a document). The writer logs "Unknown entity
        // type" and writes only the pair id. Nothing on the row says what the id is, and either type can hang off a root,
        // so the archive would be a guess — the brief's "archive only when no root can be involved" does not hold.
        var written = await OutboundCommunicationRow.WriteAsync(entityType, ChildId);

        written.Contains("sprk_regardingrecordtype").Should().BeFalse();

        var world = new World().WithCommunicationRow(written);

        var act = async () => await world.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
        ex.StatusCode.Should().Be(409);
        IncomingCommunicationProcessor.IsPermanentContainerRefusal(ex).Should().BeTrue();
    }

    [Theory(DisplayName = "Task 155 f5: an email whose pair names its OWN typed person NEXT TO a typed root follows the ROOT — with or without the pair's type (live 84d04780; the reply-inheritance shape)")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Communication_WhosePairNamesItsOwnTypedPerson_NextToARoot_FollowsTheRoot(bool pairTyped)
    {
        // f4 refused this as ambiguous (rule 4: a typed root and a pair naming a different record). The pair names the
        // row's own person — referentially enforced — so it says nothing about ownership.
        var world = new World()
            .WithCommunication(
                regardingMatter: MatterId,
                regardingParty: ("sprk_regardingperson", "contact", ContactId),
                pairId: ContactId.ToString("D").ToUpperInvariant(),
                pairType: pairTyped ? ContactTypeRef : null)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer);

        (await world.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer))
            .Should().Be(RootContainer);
        world.Reads("sprk_recordtype_ref").Should().Be(0, "identity decides; the type label is not read (interpretation ii)");
    }

    [Fact(DisplayName = "Task 155 f5 / 156: an email regarding an invoice under a SECURE matter, whose pair names its own typed person with NO type, carrying the invoice's matter as its copy, routes to the MATTER's container (live 83349fe9)")]
    public async Task Communication_RegardingAnInvoice_WithAPairNamingItsOwnTypedPerson_FollowsTheInvoice()
    {
        // The pair names the row's own person, which rule 3 reads as "no pair": the one intermediate (the invoice) is the
        // source of the copy, and the copy equals the invoice's live root.
        var world = new World(securable: SecurableWithInvoice)
            .WithCommunication(
                regardingInvoice: ChildId,
                regardingMatter: MatterId,
                regardingParty: ("sprk_regardingperson", "contact", ContactId),
                pairId: ContactId.ToString())
            .WithChild("sprk_invoice", typedMatter: MatterId, isSecure: false)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer);

        (await world.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer))
            .Should().Be(RootContainer);
    }

    [Fact(DisplayName = "Task 155 f5: a typed person with a DIFFERENT id does not vouch for the pair — an untyped pair naming some other record is still refused (identity, not presence)")]
    public async Task Communication_WhosePairIsNotItsTypedPerson_IsStillRefused()
    {
        var world = new World()
            .WithCommunication(
                regardingParty: ("sprk_regardingperson", "contact", OtherMatterId),
                pairId: ContactId.ToString());

        var act = async () => await world.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
    }

    [Theory(DisplayName = "Task 155 f5: a to-do / event whose untyped pair names its OWN typed contact / organization / account resolves like any non-secure record — not refused")]
    [InlineData("sprk_todo", "sprk_regardingcontact", "contact")]
    [InlineData("sprk_todo", "sprk_regardingorganization", "sprk_organization")]
    [InlineData("sprk_event", "sprk_regardingcontact", "contact")]
    [InlineData("sprk_event", "sprk_regardingorganization", "sprk_organization")]
    [InlineData("sprk_event", "sprk_regardingaccount", "account")]
    public async Task Child_WhoseUntypedPairNamesItsOwnTypedParty_ResolvesTheBusinessUnit(
        string entity, string column, string target)
    {
        var world = new World()
            .WithChild(entity, party: (column, target, ContactId), pairId: ContactId.ToString())
            .WithBusinessUnit(BusinessUnitContainer);

        var decision = await world.Resolver().ResolveForRecordAsync(entity, ChildId);

        decision.ContainerId.Should().Be(BusinessUnitContainer);
        world.Reads("sprk_recordtype_ref").Should().Be(0);
    }

    [Fact(DisplayName = "Task 155 f5 (AC6): two DIFFERENT secure roots that share ONE container still refuse as ambiguous — on the record path and the communication path")]
    public async Task TwoDifferentSecureRoots_SharingOneContainer_AreStillAmbiguous()
    {
        // Two secure records sharing a container is co-mingling; the refusal is about WHICH RECORD owns the content, not
        // which drive it lands in. f4's note claimed this; nothing pinned it (verifier seed V4: dedup by container).
        var todo = new World()
            .WithChild("sprk_todo", regardingProject: ProjectId, regardingMatter: MatterId)
            .WithRoot("sprk_project", ProjectId, isSecure: true, RootContainer)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer);

        var todoAct = async () => await todo.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        (await todoAct.Should().ThrowAsync<SdapProblemException>()).Which.Code
            .Should().Be(RecordContainerResolver.AncestorAmbiguousCode);

        var communication = new World()
            .WithCommunication(regardingMatter: MatterId, regardingWorkAssignment: WorkAssignmentId)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer)
            .WithRoot("sprk_workassignment", WorkAssignmentId, isSecure: true, RootContainer);

        var communicationAct = async () =>
            await communication.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer);

        var ex = (await communicationAct.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorAmbiguousCode);
        IncomingCommunicationProcessor.IsPermanentContainerRefusal(ex).Should().BeTrue();
    }

    [Fact(DisplayName = "Task 156: an email whose pair ALONE names an invoice (no typed column) is HELD like any pair-only intermediate — task 155 f5 followed it, but the communication no longer follows invoices and nothing on the row carries a copy to compare")]
    public async Task Communication_WhosePairAloneNamesAnInvoice_IsHeld()
    {
        // Task 156 made the communication's invoice an intermediate whose copy is compared (f4 interpretation viii
        // superseded). A PAIR-ONLY intermediate has no typed source column, so the restamper would never write a copy for
        // it: comparing would refuse it stale forever. It is held instead — permanent, fail closed.
        var world = new World(securable: SecurableWithInvoice)
            .WithCommunication(pairId: ChildId.ToString(), pairType: InvoiceTypeRef)
            .WithRecordType(InvoiceTypeRef, "sprk_invoice")
            .WithChild("sprk_invoice", typedMatter: MatterId, isSecure: false)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer);

        var act = async () => await world.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorUnverifiableCode);
        IncomingCommunicationProcessor.IsPermanentContainerRefusal(ex).Should().BeTrue();
    }

    // ============================================================================================
    // MACHINERY — an in-memory org
    // ============================================================================================

    private sealed class World
    {
        private readonly HashSet<string> _securable;
        private readonly Dictionary<(string Entity, Guid Id), Func<Entity>> _rows = new();
        private readonly IGenericEntityService _service = Substitute.For<IGenericEntityService>();

        private readonly HashSet<string> _known = new(StringComparer.Ordinal)
        {
            "sprk_project", "sprk_matter", "sprk_workassignment", "sprk_servicerequest", "sprk_invoice",
            "sprk_event", "sprk_todo", "sprk_document", "sprk_communication", "contact", "businessunit",
            "account", "sprk_organization", "sprk_agreement", "sprk_budget", "sprk_reportcard", "sprk_analysis",
            "sprk_recordtype_ref", "sprk_memo", "sprk_timekeeper",
        };

        private readonly Dictionary<string, EntitySecurability> _classificationOverrides = new(StringComparer.Ordinal);

        /// <param name="securable">Entities carrying sprk_issecure in this world.</param>
        /// <param name="unknown">Entities this world's org does NOT know (classified NotAnEntity).</param>
        public World(string[]? securable = null, string[]? unknown = null)
        {
            _securable = new HashSet<string>(
                securable ?? ["sprk_project", "sprk_matter", "sprk_workassignment"], StringComparer.Ordinal);
            _known.ExceptWith(unknown ?? []);

            _service
                .RetrieveAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var key = (call.ArgAt<string>(0), call.ArgAt<Guid>(1));
                    return _rows.TryGetValue(key, out var row)
                        ? Task.FromResult(OnlyRequestedColumns(row(), call.ArgAt<string[]>(2)))
                        : throw new InvalidOperationException($"Unmodelled read: {key.Item1} {key.Item2}");
                });
        }

        /// <summary>
        /// Faithful to Dataverse (task 155 f2): a Retrieve returns ONLY the requested columns. A double that returned
        /// the whole row hid a real dependency — the "filed under another record" refusal only fires when the
        /// intermediate columns ride on the record read; leave them out and the column reads as "not set".
        /// </summary>
        private static Entity OnlyRequestedColumns(Entity row, string[]? columns)
        {
            if (row is null || columns is null)
            {
                return row!;
            }

            var projected = new Entity(row.LogicalName, row.Id);
            foreach (var column in columns.Where(row.Contains))
            {
                projected[column] = row[column];
            }

            return projected;
        }

        public World WithChild(
            string entity,
            Guid? regardingProject = null,
            Guid? regardingMatter = null,
            Guid? regardingWorkAssignment = null,
            Guid? typedMatter = null,
            Guid? typedProject = null,
            bool? isSecure = null,
            string? ownContainer = null,
            (string Column, Guid Id)? intermediate = null,
            string? pairId = null,
            Guid? pairType = null,
            (string Column, string Target, Guid Id)? party = null)
        {
            var row = new Entity(entity, ChildId)
            {
                ["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId)
            };

            // f5: a typed PARTY regarding (sprk_regardingcontact / …organization / …account) — the pair's rule 3 witness.
            if (party is { } pt) row[pt.Column] = new EntityReference(pt.Target, pt.Id);

            if (regardingProject is { } p) row["sprk_regardingproject"] = new EntityReference("sprk_project", p);
            if (regardingMatter is { } m) row["sprk_regardingmatter"] = new EntityReference("sprk_matter", m);
            if (regardingWorkAssignment is { } w)
                row["sprk_regardingworkassignment"] = new EntityReference("sprk_workassignment", w);
            if (typedMatter is { } tm) row["sprk_matter"] = new EntityReference("sprk_matter", tm);
            if (typedProject is { } tp) row["sprk_project"] = new EntityReference("sprk_project", tp);
            if (isSecure is { } secure) row["sprk_issecure"] = secure;
            if (ownContainer is not null) row["sprk_containerid"] = ownContainer;
            // Live metadata: sprk_regarding{x} targets sprk_{x}; contact's typed sprk_invoice targets sprk_invoice.
            if (intermediate is { } i)
            {
                var target = i.Column.StartsWith("sprk_regarding", StringComparison.Ordinal)
                    ? "sprk_" + i.Column["sprk_regarding".Length..]
                    : i.Column;
                row[i.Column] = new EntityReference(target, i.Id);
            }

            // The polymorphic regarding pair: a STRING id and a lookup to sprk_recordtype_ref (live metadata).
            if (pairId is not null) row["sprk_regardingrecordid"] = pairId;
            if (pairType is { } t) row["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", t);

            _rows[(entity, ChildId)] = () => row;
            return this;
        }

        /// <summary>A <c>sprk_recordtype_ref</c> row: the pair's type id → the entity logical name it stands for.</summary>
        public World WithRecordType(Guid typeRefId, string? logicalName)
        {
            var row = new Entity("sprk_recordtype_ref", typeRefId);
            if (logicalName is not null) row["sprk_recordlogicalname"] = logicalName;

            _rows[("sprk_recordtype_ref", typeRefId)] = () => row;
            return this;
        }

        public World WithRecordTypeFault(Guid typeRefId, Exception fault)
        {
            _rows[("sprk_recordtype_ref", typeRefId)] = () => throw fault;
            return this;
        }

        /// <summary>The registry answers <paramref name="value"/> for <paramref name="entity"/> — any value, defined or not.</summary>
        public World WithClassification(string entity, EntitySecurability value)
        {
            _classificationOverrides[entity] = value;
            return this;
        }

        public World WithRecord(string entity, bool owningBusinessUnit)
        {
            var row = new Entity(entity, ChildId);
            if (owningBusinessUnit)
            {
                row["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId);
            }

            _rows[(entity, ChildId)] = () => row;
            return this;
        }

        public World WithRecordFault(string entity, Exception fault)
        {
            _rows[(entity, ChildId)] = () => throw fault;
            return this;
        }

        /// <summary>
        /// A root row. Task 155 f4: a root can itself name records ABOVE it — a work assignment's
        /// <c>sprk_regardingproject</c> / <c>sprk_regardingmatter</c> / held columns, and the polymorphic pair on a work
        /// assignment or project — which the transitive walk reads on the same row.
        /// </summary>
        public World WithRoot(
            string entity,
            Guid id,
            bool? isSecure,
            string? containerId,
            Guid? regardingProject = null,
            Guid? regardingMatter = null,
            string? pairId = null,
            Guid? pairType = null,
            (string Column, Guid Id)? intermediate = null)
        {
            // isSecure null = the attribute ABSENT on the row (task 150: a field-secured value masked from the reader).
            var row = new Entity(entity, id);
            if (isSecure is { } flag) row["sprk_issecure"] = flag;
            if (containerId is not null) row["sprk_containerid"] = containerId;
            if (regardingProject is { } p) row["sprk_regardingproject"] = new EntityReference("sprk_project", p);
            if (regardingMatter is { } m) row["sprk_regardingmatter"] = new EntityReference("sprk_matter", m);
            if (pairId is not null) row["sprk_regardingrecordid"] = pairId;
            if (pairType is { } t) row["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", t);
            if (intermediate is { } i)
            {
                row[i.Column] = new EntityReference("sprk_" + i.Column["sprk_regarding".Length..], i.Id);
            }

            _rows[(entity, id)] = () => row;
            return this;
        }

        /// <summary>
        /// The communication row (<see cref="CommunicationId"/>) — task 155 f4: the communication ITSELF is the record
        /// the pipeline resolves. <paramref name="withNonOwnerLinks"/> sets a person (party), a thread (grouping) and a
        /// triage category (reference): none of them may move its content.
        /// </summary>
        public World WithCommunication(
            Guid? regardingMatter = null,
            Guid? regardingWorkAssignment = null,
            Guid? regardingInvoice = null,
            (string Column, Guid Id)? intermediate = null,
            string? pairId = null,
            Guid? pairType = null,
            bool withNonOwnerLinks = false,
            (string Column, string Target, Guid Id)? regardingParty = null)
        {
            var row = new Entity("sprk_communication", CommunicationId)
            {
                ["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId)
            };

            if (regardingParty is { } party) row[party.Column] = new EntityReference(party.Target, party.Id);
            if (regardingMatter is { } m) row["sprk_regardingmatter"] = new EntityReference("sprk_matter", m);
            if (regardingWorkAssignment is { } w)
                row["sprk_regardingworkassignment"] = new EntityReference("sprk_workassignment", w);
            if (regardingInvoice is { } inv) row["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", inv);
            if (intermediate is { } i)
            {
                row[i.Column] = new EntityReference("sprk_" + i.Column["sprk_regarding".Length..], i.Id);
            }

            if (pairId is not null) row["sprk_regardingrecordid"] = pairId;
            if (pairType is { } t) row["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", t);

            if (withNonOwnerLinks)
            {
                row["sprk_regardingperson"] = new EntityReference("contact", ContactId);
                row["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", ThreadId);
                row["sprk_triagecategory"] = new EntityReference("sprk_triagecategory", TriageCategoryId);
            }

            _rows[("sprk_communication", CommunicationId)] = () => row;
            return this;
        }

        /// <summary>
        /// Task 155 f5: the communication row is EXACTLY the entity <paramref name="written"/> — the row the real outbound
        /// sender produced (<see cref="OutboundCommunicationRow"/>) — served for <see cref="CommunicationId"/>.
        /// </summary>
        public World WithCommunicationRow(Entity written)
        {
            _rows[("sprk_communication", CommunicationId)] = () => written;
            return this;
        }

        public World WithCommunicationFault(Exception fault)
        {
            _rows[("sprk_communication", CommunicationId)] = () => throw fault;
            return this;
        }

        public World WithRootFault(string entity, Guid id, Exception fault)
        {
            _rows[(entity, id)] = () => throw fault;
            return this;
        }

        public World WithNullRoot(string entity, Guid id)
        {
            _rows[(entity, id)] = () => null!;
            return this;
        }

        /// <summary>
        /// A communication whose regarding INVOICE is <see cref="ChildId"/> (the pair names it) and — task 156 — which
        /// carries the COPY of the invoice's root that IncomingAssociationResolver stamps (<paramref name="copyMatter"/> /
        /// <paramref name="copyProject"/>). Task 155 FOLLOWED the invoice live and ignored the copy (f4 interpretation
        /// viii); task 156 compares the copy with the invoice's live root like every other intermediate.
        /// </summary>
        public World WithCommunicationRegardingInvoice(Guid? copyMatter = null, Guid? copyProject = null)
        {
            var row = new Entity("sprk_communication", CommunicationId)
            {
                ["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", ChildId),
                ["sprk_regardingrecordid"] = ChildId.ToString(),
            };
            if (copyMatter is { } m) row["sprk_regardingmatter"] = new EntityReference("sprk_matter", m);
            if (copyProject is { } p) row["sprk_regardingproject"] = new EntityReference("sprk_project", p);

            _rows[("sprk_communication", CommunicationId)] = () => row;
            return this;
        }

        public World WithBusinessUnit(string? container)
        {
            var bu = new Entity("businessunit", BusinessUnitId);
            if (container is not null) bu["sprk_containerid"] = container;

            _rows[("businessunit", BusinessUnitId)] = () => bu;
            return this;
        }

        /// <summary>
        /// Task 156: any row, by its lookups — an intermediate a child is filed under (a communication, event, document,
        /// invoice, analysis, agreement, budget or report card) with its own root columns, pair and, when its type can be
        /// secure, its flag and container.
        /// </summary>
        public World WithRow(
            string entity,
            Guid id,
            (string Column, string Target, Guid Id)[]? lookups = null,
            string? pairId = null,
            Guid? pairType = null,
            bool? isSecure = null,
            string? container = null)
        {
            var row = new Entity(entity, id)
            {
                ["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId)
            };
            foreach (var (column, target, targetId) in lookups ?? [])
            {
                row[column] = new EntityReference(target, targetId);
            }

            if (pairId is not null) row["sprk_regardingrecordid"] = pairId;
            if (pairType is { } t) row["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", t);
            if (isSecure is { } secure) row["sprk_issecure"] = secure;
            if (container is not null) row["sprk_containerid"] = container;

            _rows[(entity, id)] = () => row;
            return this;
        }

        /// <summary>Task 156: where a <c>container_ancestor_stale</c> refusal enqueues the stale row.</summary>
        public RecordingRestampQueue Queue { get; } = new();

        public RecordContainerResolver Resolver() =>
            new(Registry(), _service, NullLogger<RecordContainerResolver>.Instance, Queue);

        /// <summary>The REAL communication adapter over the REAL record resolver — only Dataverse rows are doubled.</summary>
        public CommunicationContainerResolver CommunicationResolver() =>
            new(Resolver(), Registry(), NullLogger<CommunicationContainerResolver>.Instance);

        private ISecurableEntityRegistry Registry()
        {
            var registry = Substitute.For<ISecurableEntityRegistry>();
            registry.ClassifyEntityAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(
                    _classificationOverrides.TryGetValue(call.Arg<string>(), out var overridden)
                        ? overridden
                        : TestEntityCatalog.Classify(call.Arg<string>(), _securable, _known)));
            registry.GetSecurableEntitiesAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlySet<string>>(_securable));
            return registry;
        }

        private IEnumerable<ICall> ReadCalls() =>
            _service.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IGenericEntityService.RetrieveAsync));

        public int Reads(string entity) => ReadCalls().Count(c => (string)c.GetArguments()[0]! == entity);

        public int TotalReads() => ReadCalls().Count();

        /// <summary>Reads of a project / matter / work assignment / service request OTHER than the record being resolved.</summary>
        public int RootReads() => ReadCalls().Count(c =>
            CoreAncestorResolver.IsCoreRecordEntity((string)c.GetArguments()[0]!)
            && (Guid)c.GetArguments()[1]! != ChildId);

        public IEnumerable<string> ColumnsRead(string entity) => ReadCalls()
            .Where(c => (string)c.GetArguments()[0]! == entity)
            .SelectMany(c => (string[])c.GetArguments()[2]!);
    }
}
