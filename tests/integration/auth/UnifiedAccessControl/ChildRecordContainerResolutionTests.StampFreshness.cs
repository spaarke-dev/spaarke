using System.ServiceModel;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 156 (owner round 4 item 5 — option b) — task 155's held branch
/// (<c>container_ancestor_unverifiable</c> for every child filed under another record) is REPLACED: the record the child is
/// filed under is read LIVE, once, and its root compared with the child's COPY.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Equal → resolves exactly as a direct root link would (secure root → the root's own container; otherwise the
/// record's business-unit container).</item>
/// <item>Different → <c>container_ancestor_stale</c> 409, nothing resolved, the stale row enqueued for re-stamping.</item>
/// <item>The record above unreadable → 503; missing → 409.</item>
/// <item>The Office CARRIER to-do (a direct matter / project plus the document or email it came from): the carrier's root
/// equal to the direct link → resolves; different and anything secure → ambiguous; different and nothing secure → the
/// direct link (the user chose it).</item>
/// </list>
/// Fail closed throughout (ADR-003): no branch here resolves a shared container while a secure record could be involved.
/// </remarks>
public partial class ChildRecordContainerResolutionTests
{
    private static readonly Guid IntermediateId = Guid.Parse("15600000-0000-0000-0000-0000000000a1");
    private static readonly Guid UpperIntermediateId = Guid.Parse("15600000-0000-0000-0000-0000000000a2");
    private static readonly Guid CarrierId = Guid.Parse("15600000-0000-0000-0000-0000000000a3");

    /// <summary>Every (child table, intermediate) the stamp is copied along — the topology the restamper writes.</summary>
    public static TheoryData<string, string> EveryStampSource()
    {
        var data = new TheoryData<string, string>();
        foreach (var (table, sources) in CoreAncestorResolver.StampSourceColumns)
        {
            // The analysis is never a record the storage resolver is asked about; its copy is covered by the restamper.
            if (table == "sprk_analysis") continue;
            foreach (var source in sources) data.Add(table, source.Intermediate);
        }

        return data;
    }

    /// <summary>The column on an intermediate that names its matter (typed on invoice / budget / document).</summary>
    private static string MatterColumnOf(string intermediate) =>
        intermediate is "sprk_invoice" or "sprk_budget" or "sprk_document" ? "sprk_matter" : "sprk_regardingmatter";

    /// <summary>The column on a child that names an intermediate (<c>sprk_regarding{x}</c>).</summary>
    private static string SourceColumnOf(string child, string intermediate) =>
        CoreAncestorResolver.StampSourceColumns[child].Single(s => s.Intermediate == intermediate).Column;

    /// <summary>
    /// A child filed under an intermediate (the pair names it, as the regarding builders write it) whose COPY says
    /// <paramref name="copy"/>, and the intermediate whose LIVE root is <paramref name="live"/>.
    /// </summary>
    private static World ChildUnderIntermediate(
        string child, string intermediate, Guid? copy, Guid? live, string[]? securable = null)
    {
        var childLookups = new List<(string, string, Guid)> { (SourceColumnOf(child, intermediate), intermediate, IntermediateId) };
        if (copy is { } c) childLookups.Add(("sprk_regardingmatter", "sprk_matter", c));

        return new World(securable: securable)
            .WithRow(child, ChildId, [.. childLookups], pairId: IntermediateId.ToString())
            .WithRow(intermediate, IntermediateId,
                live is { } l ? [(MatterColumnOf(intermediate), "sprk_matter", l)] : [])
            .WithBusinessUnit(BusinessUnitContainer);
    }

    // =============================================================================================
    // A FRESH copy resolves exactly as a direct root link would (AC4)
    // =============================================================================================

    [Theory(DisplayName = "Task 156: a child whose copy EQUALS its intermediate's live root, under a SECURE matter, resolves the matter's own container — every intermediate type")]
    [MemberData(nameof(EveryStampSource))]
    public async Task FreshCopy_UnderASecureRoot_ResolvesTheRootsOwnContainer(string child, string intermediate)
    {
        var world = ChildUnderIntermediate(child, intermediate, copy: MatterId, live: MatterId)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer);

        var decision = await world.Resolver().ResolveForRecordAsync(child, ChildId);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.ResolvedSecure);
        decision.ContainerId.Should().Be(RootContainer);
        world.Reads(intermediate).Should().Be(child == intermediate ? 2 : 1,
            "the record it is filed under is read live, once (an event under an event: the event itself, then its source)");
        world.Reads("businessunit").Should().Be(0);
        world.Queue.Children.Should().BeEmpty();
    }

    [Theory(DisplayName = "Task 156: a fresh copy under a NON-secure matter resolves the child's own business-unit container — the 155 refusal is gone")]
    [MemberData(nameof(EveryStampSource))]
    public async Task FreshCopy_UnderANonSecureRoot_ResolvesItsBusinessUnit(string child, string intermediate)
    {
        var world = ChildUnderIntermediate(child, intermediate, copy: MatterId, live: MatterId)
            .WithRoot("sprk_matter", MatterId, isSecure: false, containerId: null);

        var decision = await world.Resolver().ResolveForRecordAsync(child, ChildId);

        decision.ContainerId.Should().Be(BusinessUnitContainer);
    }

    [Fact(DisplayName = "Task 156: a fresh copy with NO root on either side resolves the business unit — nothing is secure")]
    public async Task FreshCopy_NoRootAnywhere_ResolvesItsBusinessUnit()
    {
        var world = ChildUnderIntermediate("sprk_todo", "sprk_communication", copy: null, live: null);

        (await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId)).ContainerId.Should().Be(BusinessUnitContainer);
    }

    [Fact(DisplayName = "Task 156: the cost of a to-do under a communication — one extra read (the communication), no metadata round trip")]
    public async Task FreshCopy_Cost()
    {
        var world = ChildUnderIntermediate("sprk_todo", "sprk_communication", copy: MatterId, live: MatterId)
            .WithRoot("sprk_matter", MatterId, isSecure: false, containerId: null);

        await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        world.TotalReads().Should().Be(4, "to-do + communication + matter + business unit");
    }

    // =============================================================================================
    // A STALE copy refuses, resolves nothing, and is enqueued for re-stamping (AC5)
    // =============================================================================================

    [Theory(DisplayName = "Task 156: copy ≠ the intermediate's live root → 409 container_ancestor_stale, NO container, the child ENQUEUED — every intermediate type")]
    [MemberData(nameof(EveryStampSource))]
    public async Task StaleCopy_IsRefused_AndEnqueued(string child, string intermediate)
    {
        // The leak this replaces: the intermediate was re-filed to SECURE matter M; the child still says "other matter".
        var world = ChildUnderIntermediate(child, intermediate, copy: OtherMatterId, live: MatterId)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer)
            .WithRoot("sprk_matter", OtherMatterId, isSecure: false, containerId: null);

        foreach (var fallback in new[] { null, ArchiveContainer })
        {
            var act = async () => await world.Resolver().ResolveForRecordAsync(child, ChildId, fallback);

            var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
            ex.Code.Should().Be(RecordContainerResolver.AncestorStaleCode);
            ex.StatusCode.Should().Be(409);
        }

        world.Queue.Children.Should().AllBeEquivalentTo((child, ChildId), "the stale row is the one re-stamped");
        world.Queue.Children.Should().NotBeEmpty();
        world.Reads("businessunit").Should().Be(0, "a shared container must never be in scope");
        world.Reads("sprk_matter").Should().Be(0, "a stale copy is not walked at all");
    }

    [Theory(DisplayName = "Task 156: a MISSING copy (the live 9 to-dos under invoices) and a copy whose source no longer names a root are both stale")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingOrOrphanedCopy_IsStale(bool copyMissing)
    {
        var world = ChildUnderIntermediate("sprk_todo", "sprk_invoice",
                copy: copyMissing ? null : MatterId,
                live: copyMissing ? MatterId : null)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer);

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.Code.Should().Be(RecordContainerResolver.AncestorStaleCode);
        world.Queue.Children.Should().ContainSingle();
    }

    [Fact(DisplayName = "Task 156: the stale code is TRANSIENT for the inbound processor — a re-stamp makes the retry succeed, so it must not be skipped as permanent")]
    public void StaleCode_IsNotAPermanentRefusal()
        => IncomingCommunicationProcessor.IsPermanentContainerRefusal(
                new SdapProblemException(RecordContainerResolver.AncestorStaleCode, "t", statusCode: 409))
            .Should().BeFalse();

    [Fact(DisplayName = "Task 156: an UNREADABLE intermediate → 503 container_ancestor_unresolved; a MISSING one → 409 — neither resolves any container")]
    public async Task IntermediateUnreadableOrMissing_IsRefused()
    {
        var unreadable = ChildUnderIntermediate("sprk_todo", "sprk_communication", copy: MatterId, live: MatterId)
            .WithRootFault("sprk_communication", IntermediateId, new TimeoutException("Dataverse timed out"));
        var missing = ChildUnderIntermediate("sprk_todo", "sprk_communication", copy: MatterId, live: MatterId)
            .WithRootFault("sprk_communication", IntermediateId, new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = -2147220969 }, new FaultReason("does not exist")));

        foreach (var (world, status) in new[] { (unreadable, 503), (missing, 409) })
        {
            var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId, ArchiveContainer);

            var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
            ex.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
            ex.StatusCode.Should().Be(status);
            world.Reads("businessunit").Should().Be(0);
            world.Queue.Children.Should().BeEmpty("an unreadable answer is not a stale one — nothing to re-stamp");
        }
    }

    // =============================================================================================
    // Transitive: the comparison applies at every row the walk reads
    // =============================================================================================

    [Fact(DisplayName = "Task 156: a to-do under an event under a communication — the EVENT's copy is stale → refused, and the EVENT is enqueued")]
    public async Task StaleCopyAboveTheRecord_IsRefused_AndThatRowEnqueued()
    {
        var world = new World()
            .WithRow("sprk_todo", ChildId,
                [("sprk_regardingevent", "sprk_event", IntermediateId), ("sprk_regardingmatter", "sprk_matter", OtherMatterId)],
                pairId: IntermediateId.ToString())
            .WithRow("sprk_event", IntermediateId,
                [("sprk_regardingcommunication", "sprk_communication", UpperIntermediateId), ("sprk_regardingmatter", "sprk_matter", OtherMatterId)],
                pairId: UpperIntermediateId.ToString())
            .WithRow("sprk_communication", UpperIntermediateId, [("sprk_regardingmatter", "sprk_matter", MatterId)])
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorStaleCode);
        ex.Detail.Should().Contain("sprk_event", "the advice names the record whose copy is stale");
        world.Queue.Children.Should().ContainSingle().Which.Should().Be(("sprk_event", IntermediateId),
            "re-stamping the event cascades to the to-do under it");
    }

    [Fact(DisplayName = "Task 156: the same chain, every copy fresh, under a SECURE matter → the matter's own container")]
    public async Task FreshChain_ResolvesTheSecureRoot()
    {
        var world = new World()
            .WithRow("sprk_todo", ChildId,
                [("sprk_regardingevent", "sprk_event", IntermediateId), ("sprk_regardingmatter", "sprk_matter", MatterId)],
                pairId: IntermediateId.ToString())
            .WithRow("sprk_event", IntermediateId,
                [("sprk_regardingcommunication", "sprk_communication", UpperIntermediateId), ("sprk_regardingmatter", "sprk_matter", MatterId)],
                pairId: UpperIntermediateId.ToString())
            .WithRow("sprk_communication", UpperIntermediateId, [("sprk_regardingmatter", "sprk_matter", MatterId)])
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        (await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId)).ContainerId.Should().Be(RootContainer);
    }

    [Fact(DisplayName = "Task 156: a filing LOOP (an event under an event under the first) refuses 409 — no row in it says which root")]
    public async Task FilingLoop_IsRefused()
    {
        var world = new World()
            .WithRow("sprk_event", ChildId,
                [("sprk_regardingevent", "sprk_event", IntermediateId), ("sprk_regardingmatter", "sprk_matter", MatterId)],
                pairId: IntermediateId.ToString())
            .WithRow("sprk_event", IntermediateId,
                [("sprk_regardingevent", "sprk_event", ChildId), ("sprk_regardingmatter", "sprk_matter", MatterId)],
                pairId: ChildId.ToString())
            .WithBusinessUnit(BusinessUnitContainer);

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_event", ChildId);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
        ex.StatusCode.Should().Be(409);
        ex.Detail.Should().Contain("lead back to themselves", "the loop is named, not mistaken for a long chain");
    }

    [Fact(DisplayName = "Task 156 (verifier round 1, M13): the walk's DEPTH bound counts the records a child is filed under — a to-do under a chain of five events refuses 409, never follows it to the end")]
    public async Task ChainOfIntermediatesPastTheDepthBound_IsRefused_NeverResolved()
    {
        // to-do → E1 → E2 → E3 → E4 → E5, every copy FRESH (a non-secure matter). Unbounded, the walk would read all five
        // and resolve the business unit; bounded, the fifth record above the to-do is past MaxRootChainDepth (4).
        var chain = Enumerable.Range(1, RecordContainerResolver.MaxRootChainDepth + 1)
            .Select(i => Guid.Parse($"15600000-0000-0000-0000-0000000b{i:D4}")).ToArray();
        var world = new World()
            .WithRow("sprk_todo", ChildId,
                [("sprk_regardingevent", "sprk_event", chain[0]), ("sprk_regardingmatter", "sprk_matter", MatterId)],
                pairId: chain[0].ToString());
        for (var i = 0; i < chain.Length; i++)
        {
            var above = i + 1 < chain.Length ? chain[i + 1] : (Guid?)null;
            world.WithRow("sprk_event", chain[i],
                above is { } next
                    ? [("sprk_regardingevent", "sprk_event", next), ("sprk_regardingmatter", "sprk_matter", MatterId)]
                    : [("sprk_regardingmatter", "sprk_matter", MatterId)],
                pairId: above?.ToString());
        }

        world.WithRoot("sprk_matter", MatterId, isSecure: false, containerId: null).WithBusinessUnit(BusinessUnitContainer);

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
        ex.StatusCode.Should().Be(409);
        ex.Detail.Should().Contain("longer than the storage resolver follows");
        world.Reads("sprk_event").Should().Be(RecordContainerResolver.MaxRootChainDepth,
            "the record past the bound is never read");
        world.Reads("businessunit").Should().Be(0, "an unbounded walk is never resolved into a shared container");
    }

    [Fact(DisplayName = "Task 156 (verifier round 1, M13): the walk's READ bound counts the records a child is filed under — a to-do naming nine intermediates refuses 409 before the ninth read")]
    public async Task IntermediateReadsPastTheReadBound_AreRefused_NeverResolved()
    {
        // The pair names event E1 (its source; E1 is itself filed under E2); every other source column is set too, so each
        // is a CARRIER read live. Nine intermediate reads, none naming a root: unbounded, the walk would read them all and
        // resolve the business unit with no root ever read.
        var e1 = Guid.Parse("15600000-0000-0000-0000-0000000c0001");
        var e2 = Guid.Parse("15600000-0000-0000-0000-0000000c0002");
        var carriers = CoreAncestorResolver.StampSourceColumns["sprk_todo"]
            .Where(s => s.Intermediate != "sprk_event")
            .Select((s, i) => (s.Column, s.Intermediate, Id: Guid.Parse($"15600000-0000-0000-0000-0000000c01{i:D2}")))
            .ToArray();

        var world = new World()
            .WithRow("sprk_todo", ChildId,
                [("sprk_regardingevent", "sprk_event", e1), .. carriers.Select(c => (c.Column, c.Intermediate, c.Id))],
                pairId: e1.ToString())
            .WithRow("sprk_event", e1, [("sprk_regardingevent", "sprk_event", e2)], pairId: e2.ToString())
            .WithRow("sprk_event", e2, [])
            .WithBusinessUnit(BusinessUnitContainer);
        foreach (var carrier in carriers)
        {
            world.WithRow(carrier.Intermediate, carrier.Id, []);
        }

        (2 + carriers.Length).Should().BeGreaterThan(RecordContainerResolver.MaxRootReads, "the shape must exceed the bound");

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorUnresolvedCode);
        ex.StatusCode.Should().Be(409);
        (world.TotalReads() - 1).Should().Be(RecordContainerResolver.MaxRootReads,
            "the record's own row, then exactly MaxRootReads rows above it — never one more");
        world.Reads("businessunit").Should().Be(0);
    }

    [Fact(DisplayName = "Task 156: a root type the source CANNOT carry is the row's own direct link — an email under an invoice with its own work assignment is not stale")]
    public async Task RootTypeTheSourceCannotCarry_IsNotPartOfTheComparison()
    {
        // An invoice names a project or matter, never a work assignment: the email's work assignment cannot be a copy of
        // the invoice, so it is not compared — only the matter copy is, and it is fresh.
        var world = new World()
            .WithRow("sprk_communication", ChildId,
            [
                ("sprk_regardinginvoice", "sprk_invoice", IntermediateId), ("sprk_regardingmatter", "sprk_matter", MatterId),
                ("sprk_regardingworkassignment", "sprk_workassignment", WorkAssignmentId),
            ], pairId: IntermediateId.ToString())
            .WithRow("sprk_invoice", IntermediateId, [("sprk_matter", "sprk_matter", MatterId)])
            .WithRoot("sprk_matter", MatterId, isSecure: false, containerId: null)
            .WithRoot("sprk_workassignment", WorkAssignmentId, isSecure: true, RootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        (await world.Resolver().ResolveForRecordAsync("sprk_communication", ChildId)).ContainerId
            .Should().Be(RootContainer, "the copy is fresh; the secure work assignment the email names directly decides");
        world.Queue.Children.Should().BeEmpty();
    }

    /// <summary>
    /// Task 156's "a record above that is ITSELF secure" case, for the one intermediate that carries <c>sprk_issecure</c> —
    /// the invoice — under owner round 10 item 11 (task 150, later than task 156's premise): invoices follow their matter.
    /// The invoice's own flag and container are not a security input, so a to-do under an invoice FLAGGED secure resolves
    /// through the invoice's matter: the matter's container when the matter is secure, the business unit's when it is not.
    /// </summary>
    [Theory(DisplayName = "Task 156 x task 150: a to-do under an invoice FLAGGED secure follows the invoice's matter — the invoice's own flag decides nothing")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IntermediateInvoiceFlaggedSecure_FollowsItsMatter(bool matterIsSecure)
    {
        var world = new World(securable: SecurableWithInvoice)
            .WithRow("sprk_todo", ChildId,
                [("sprk_regardinginvoice", "sprk_invoice", IntermediateId), ("sprk_regardingmatter", "sprk_matter", MatterId)],
                pairId: IntermediateId.ToString())
            .WithRow("sprk_invoice", IntermediateId, [("sprk_matter", "sprk_matter", MatterId)],
                isSecure: true, container: OtherRootContainer)
            .WithRoot("sprk_matter", MatterId, isSecure: matterIsSecure, matterIsSecure ? RootContainer : null)
            .WithBusinessUnit(BusinessUnitContainer);

        (await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId)).ContainerId.Should().Be(
            matterIsSecure ? RootContainer : BusinessUnitContainer,
            "the invoice's own flag and container are not a security input (owner round 10 item 11); its matter decides");
    }

    // =============================================================================================
    // The Office CARRIER to-do (AC6)
    // =============================================================================================

    /// <summary>
    /// The Office add-in's to-do: the matter the user chose (the pair names it) plus the document it was created from —
    /// <paramref name="carrierRoot"/> is that document's own matter.
    /// </summary>
    private static World CarrierTodo(Guid? carrierRoot, bool directSecure, bool carrierRootSecure, string carrierColumn = "sprk_regardingdocument")
    {
        var carrierEntity = carrierColumn == "sprk_regardingdocument" ? "sprk_document" : "sprk_communication";
        var world = new World()
            .WithRow("sprk_todo", ChildId,
                [("sprk_regardingmatter", "sprk_matter", MatterId), (carrierColumn, carrierEntity, CarrierId)],
                pairId: MatterId.ToString())
            .WithRow(carrierEntity, CarrierId,
                carrierRoot is { } r ? [(MatterColumnOf(carrierEntity), "sprk_matter", r)] : [])
            .WithRoot("sprk_matter", MatterId, isSecure: directSecure, directSecure ? RootContainer : null)
            .WithBusinessUnit(BusinessUnitContainer);

        if (carrierRoot is { } other && other != MatterId)
        {
            world.WithRoot("sprk_matter", other, isSecure: carrierRootSecure, carrierRootSecure ? OtherRootContainer : null);
        }

        return world;
    }

    [Theory(DisplayName = "Task 156 carrier: the carrier's root EQUALS the direct link (or it names none) → resolves through the direct link")]
    [InlineData(true, "sprk_regardingdocument")]
    [InlineData(false, "sprk_regardingdocument")]
    [InlineData(true, "sprk_regardingcommunication")]
    public async Task Carrier_AgreeingWithTheDirectLink_Resolves(bool directSecure, string carrierColumn)
    {
        foreach (var carrierRoot in new Guid?[] { MatterId, null })
        {
            var world = CarrierTodo(carrierRoot, directSecure, carrierRootSecure: false, carrierColumn);

            var decision = await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

            decision.ContainerId.Should().Be(directSecure ? RootContainer : BusinessUnitContainer);
            world.Queue.Children.Should().BeEmpty("a direct link is never a stale copy");
        }
    }

    [Theory(DisplayName = "Task 156 carrier: the carrier's root DIFFERS and EITHER root is secure → container_ancestor_ambiguous, no container")]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Carrier_Disagreeing_WithASecureRoot_IsAmbiguous(bool directSecure, bool carrierRootSecure)
    {
        var world = CarrierTodo(OtherMatterId, directSecure, carrierRootSecure);

        foreach (var fallback in new[] { null, ArchiveContainer })
        {
            var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId, fallback);

            var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
            ex.Code.Should().Be(RecordContainerResolver.AncestorAmbiguousCode);
            ex.StatusCode.Should().Be(409);
        }

        world.Reads("businessunit").Should().Be(0);
    }

    [Fact(DisplayName = "Task 156 carrier: the carrier's root DIFFERS and NEITHER is secure → the direct link (the user chose it): the to-do's business-unit container")]
    public async Task Carrier_Disagreeing_NothingSecure_UsesTheDirectLink()
    {
        var world = CarrierTodo(OtherMatterId, directSecure: false, carrierRootSecure: false);

        (await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId)).ContainerId.Should().Be(BusinessUnitContainer);
    }

    // =============================================================================================
    // Shapes that stay refused
    // =============================================================================================

    [Fact(DisplayName = "Task 156: two intermediates and nothing saying which one the row belongs to → container_ancestor_ambiguous")]
    public async Task TwoIntermediates_NoPair_IsAmbiguous()
    {
        var world = new World()
            .WithRow("sprk_todo", ChildId,
            [
                ("sprk_regardingevent", "sprk_event", IntermediateId),
                ("sprk_regardingcommunication", "sprk_communication", UpperIntermediateId),
            ])
            .WithBusinessUnit(BusinessUnitContainer);

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.Code.Should().Be(RecordContainerResolver.AncestorAmbiguousCode);
        world.TotalReads().Should().Be(1, "nothing above it is read on an ambiguous row");
    }

    [Fact(DisplayName = "Task 156: a source naming two DIFFERENT roots of one type (a document's sprk_matter and sprk_relatedmatter) → container_ancestor_ambiguous (permanent), never stale — no copy can match it, so nothing is enqueued")]
    public async Task SourceNamingTwoRootsOfOneType_IsAmbiguous_NotStale()
    {
        var world = new World()
            .WithRow("sprk_todo", ChildId,
                [("sprk_regardingdocument", "sprk_document", IntermediateId), ("sprk_regardingmatter", "sprk_matter", MatterId)],
                pairId: IntermediateId.ToString())
            .WithRow("sprk_document", IntermediateId,
                [("sprk_matter", "sprk_matter", MatterId), ("sprk_relatedmatter", "sprk_matter", OtherMatterId)])
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer)
            .WithRoot("sprk_matter", OtherMatterId, isSecure: false, containerId: null)
            .WithBusinessUnit(BusinessUnitContainer);

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorAmbiguousCode,
            "the restamper cannot derive this root either, so a 'stale' answer would be a refresh that never comes");
        IncomingCommunicationProcessor.IsPermanentContainerRefusal(ex).Should().BeTrue();
        world.Queue.Children.Should().BeEmpty("enqueuing a re-stamp that cannot be derived would loop");
        world.Reads("businessunit").Should().Be(0);
    }

    [Fact(DisplayName = "Task 156: the document's links ARE the shared document link vocabulary — every column classified, every non-party column on the document's read (a column the resolver could not classify would be silently ignored: fail open)")]
    public void DocumentLinks_AreTheSharedVocabulary_EveryColumnClassifiedAndRead()
    {
        var read = RecordContainerResolver.ChildAncestorLinks.For("sprk_document")!.AllColumns
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var field in DocumentLinkFields.All)
        {
            var kind = RecordContainerResolver.ChildAncestorLinks.KindOf(field.TargetEntityLogicalName);
            kind.Should().NotBeNull($"{field.LogicalName} targets {field.TargetEntityLogicalName}, which must be classified");

            if (kind == RecordContainerResolver.ChildAncestorLinks.RecordKind.Party)
            {
                read.Should().NotContain(field.LogicalName, "a person or organization is not ownership");
            }
            else
            {
                read.Should().Contain(field.LogicalName, "a link to a root or an intermediate is followed or held — never ignored");
            }
        }

        CoreAncestorResolver.IntermediateRootColumns["sprk_document"].Select(c => c.Column)
            .Should().BeEquivalentTo(
                DocumentLinkFields.All
                    .Where(f => RecordContainerResolver.ChildAncestorLinks.KindOf(f.TargetEntityLogicalName)
                        == RecordContainerResolver.ChildAncestorLinks.RecordKind.Root)
                    .Select(f => f.LogicalName),
                "a child of a document copies the root its typed and related root links name");
    }

    [Fact(DisplayName = "Task 156: a pair naming a record the row does NOT carry, while an intermediate is set → ambiguous (pair rule 4)")]
    public async Task PairDisagreeingWithAnIntermediate_IsAmbiguous()
    {
        var world = new World()
            .WithRow("sprk_todo", ChildId, [("sprk_regardingcommunication", "sprk_communication", IntermediateId)],
                pairId: Guid.NewGuid().ToString())
            .WithBusinessUnit(BusinessUnitContainer);

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.Code.Should().Be(RecordContainerResolver.AncestorAmbiguousCode);
    }

    [Fact(DisplayName = "Task 156: an intermediate filed under a record it carries no copy of (an agreement under a document) is still HELD")]
    public async Task IntermediateWithAHeldLinkOfItsOwn_IsHeld()
    {
        var world = new World()
            .WithRow("sprk_todo", ChildId,
                [("sprk_regardingagreement", "sprk_agreement", IntermediateId), ("sprk_regardingmatter", "sprk_matter", MatterId)],
                pairId: IntermediateId.ToString())
            .WithRow("sprk_agreement", IntermediateId,
                [("sprk_regardingmatter", "sprk_matter", MatterId), ("sprk_regardingdocument", "sprk_document", UpperIntermediateId)])
            .WithBusinessUnit(BusinessUnitContainer);

        var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorUnverifiableCode);
        ex.Detail.Should().Contain("sprk_agreement");
    }

    // =============================================================================================
    // The communication pipeline — the invoice is now an intermediate like any other
    // =============================================================================================

    [Fact(DisplayName = "Task 156: an email whose copy of its invoice's matter is FRESH routes to the secure matter's container; an UNSTAMPED one is stale (transient, enqueued)")]
    public async Task Communication_UnderAnInvoice_ComparesItsCopy()
    {
        var stamped = new World(securable: SecurableWithInvoice)
            .WithCommunication(regardingInvoice: ChildId, regardingMatter: MatterId, pairId: ChildId.ToString())
            .WithChild("sprk_invoice", typedMatter: MatterId, isSecure: false)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer);

        (await stamped.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer))
            .Should().Be(RootContainer);

        var unstamped = new World(securable: SecurableWithInvoice)
            .WithCommunication(regardingInvoice: ChildId, pairId: ChildId.ToString())
            .WithChild("sprk_invoice", typedMatter: MatterId, isSecure: false)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer);

        var act = async () => await unstamped.CommunicationResolver().ResolveContainerAsync(CommunicationId, ArchiveContainer);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.Code.Should().Be(RecordContainerResolver.AncestorStaleCode);
        IncomingCommunicationProcessor.IsPermanentContainerRefusal(ex).Should().BeFalse();
        unstamped.Queue.Children.Should().ContainSingle().Which.Should().Be(("sprk_communication", CommunicationId));
    }

    // =============================================================================================
    // Verifier round 2 (AC7): three guards in the Source branch that no test pinned. Each test below is the shape the
    // guard exists for; with the guard removed each one resolves a SHARED container where a secure record is involved
    // (the #1038 leak class). Seeds V1-V3 in notes/task-156-stamp-freshness.md.
    // =============================================================================================

    [Fact(DisplayName = "Task 156 (verifier round 2, V2): a to-do under a communication whose SECURE matter is named ONLY by the communication's polymorphic pair resolves that matter's own container — never the to-do's business unit")]
    public async Task SourceWhoseSecureRootIsNamedOnlyByItsPair_ResolvesThatRootsOwnContainer()
    {
        // Live: 161 of 276 communications carry the pair. CoreAncestorResolver copies only TYPED root columns, so the
        // to-do's copy is empty and so is the communication's typed root: the copy is fresh. The record the pair alone
        // names is not part of the copy, but it must still be WALKED for its flag — that is the guard.
        var world = new World()
            .WithRow("sprk_todo", ChildId, [("sprk_regardingcommunication", "sprk_communication", IntermediateId)],
                pairId: IntermediateId.ToString())
            .WithRow("sprk_communication", IntermediateId, [],
                pairId: MatterId.ToString("D").ToUpperInvariant(), pairType: MatterTypeRef)
            .WithRecordType(MatterTypeRef, "sprk_matter")
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        foreach (var fallback in new[] { null, ArchiveContainer })
        {
            var decision = await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId, fallback);

            decision.Outcome.Should().Be(ContainerDecisionOutcome.ResolvedSecure);
            decision.ContainerId.Should().Be(RootContainer,
                "a child of a secure record is secure (C10 part 2), however the record above names that root");
        }

        world.Reads("sprk_matter").Should().Be(2, "the matter the communication's pair names is read for its flag (once per resolution)");
        world.Reads("businessunit").Should().Be(0, "a shared container must never be in scope");
        world.Queue.Children.Should().BeEmpty("the copy is fresh: neither side names a typed root");
    }

    [Theory(DisplayName = "Task 156 (verifier round 2, V3): a to-do under an analysis filed under a NON-secure matter, whose input or output document belongs to a different SECURE matter → container_ancestor_ambiguous, no container (interpretation xviii)")]
    [InlineData("sprk_documentid")]
    [InlineData("sprk_outputfileid")]
    public async Task AnalysisWhoseDocumentBelongsToADifferentSecureRoot_IsAmbiguous(string documentColumn)
    {
        // The analysis's copy source is its own regarding (the plain matter); the document it analyses (or produced) is a
        // CARRIER — the analysis's NOT NULL sprk_documentid / sprk_outputfileid. Unread, the to-do would resolve the plain
        // matter's business-unit container while its content concerns a document of the SECURE matter.
        var world = new World()
            .WithRow("sprk_todo", ChildId,
                [("sprk_regardinganalysis", "sprk_analysis", IntermediateId), ("sprk_regardingmatter", "sprk_matter", OtherMatterId)],
                pairId: IntermediateId.ToString())
            .WithRow("sprk_analysis", IntermediateId,
                [("sprk_regardingmatter", "sprk_matter", OtherMatterId), (documentColumn, "sprk_document", CarrierId)])
            .WithRow("sprk_document", CarrierId, [("sprk_matter", "sprk_matter", MatterId)])
            .WithRoot("sprk_matter", OtherMatterId, isSecure: false, containerId: null)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        foreach (var fallback in new[] { null, ArchiveContainer })
        {
            var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId, fallback);

            var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
            ex.Code.Should().Be(RecordContainerResolver.AncestorAmbiguousCode);
            ex.StatusCode.Should().Be(409);
        }

        world.Reads("sprk_document").Should().Be(2, "the analysis's document is read live as a carrier (once per resolution)");
        world.Reads("businessunit").Should().Be(0, "a shared container must never be in scope");
        world.Queue.Children.Should().BeEmpty("the copy is fresh — this is a disagreement, not a stale copy");
    }

    [Fact(DisplayName = "Task 156 (verifier round 2, V3): the same analysis whose document belongs to the SAME matter as the analysis agrees — the to-do resolves its business-unit container (a carrier is read, not refused on sight)")]
    public async Task AnalysisWhoseDocumentBelongsToTheSameRoot_Resolves()
    {
        var world = new World()
            .WithRow("sprk_todo", ChildId,
                [("sprk_regardinganalysis", "sprk_analysis", IntermediateId), ("sprk_regardingmatter", "sprk_matter", OtherMatterId)],
                pairId: IntermediateId.ToString())
            .WithRow("sprk_analysis", IntermediateId,
                [("sprk_regardingmatter", "sprk_matter", OtherMatterId), ("sprk_documentid", "sprk_document", CarrierId)])
            .WithRow("sprk_document", CarrierId, [("sprk_matter", "sprk_matter", OtherMatterId)])
            .WithRoot("sprk_matter", OtherMatterId, isSecure: false, containerId: null)
            .WithBusinessUnit(BusinessUnitContainer);

        (await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId)).ContainerId.Should().Be(BusinessUnitContainer);
        world.Reads("sprk_document").Should().Be(1);
    }

    [Fact(DisplayName = "Task 156 (verifier round 2, V1): a to-do filed under a communication (copy FRESH, a NON-secure matter) that ALSO names an event of a different SECURE matter → container_ancestor_ambiguous, no container")]
    public async Task RowFiledUnderASource_AlsoNamingAnIntermediateOfADifferentSecureRoot_IsAmbiguous()
    {
        // The pair names the communication, so it is the copy's source; the event set beside it is a CARRIER
        // (ClassifyStampSource rule 3). Unread, the to-do would resolve the plain matter's business-unit container.
        var world = new World()
            .WithRow("sprk_todo", ChildId,
            [
                ("sprk_regardingcommunication", "sprk_communication", IntermediateId),
                ("sprk_regardingevent", "sprk_event", CarrierId),
                ("sprk_regardingmatter", "sprk_matter", OtherMatterId),
            ], pairId: IntermediateId.ToString())
            .WithRow("sprk_communication", IntermediateId, [("sprk_regardingmatter", "sprk_matter", OtherMatterId)])
            .WithRow("sprk_event", CarrierId, [("sprk_regardingmatter", "sprk_matter", MatterId)])
            .WithRoot("sprk_matter", OtherMatterId, isSecure: false, containerId: null)
            .WithRoot("sprk_matter", MatterId, isSecure: true, RootContainer)
            .WithBusinessUnit(BusinessUnitContainer);

        foreach (var fallback in new[] { null, ArchiveContainer })
        {
            var act = async () => await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId, fallback);

            var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
            ex.Code.Should().Be(RecordContainerResolver.AncestorAmbiguousCode);
            ex.StatusCode.Should().Be(409);
        }

        world.Reads("sprk_event").Should().Be(2, "the event beside the copy's source is read live as a carrier (once per resolution)");
        world.Reads("businessunit").Should().Be(0, "a shared container must never be in scope");
        world.Queue.Children.Should().BeEmpty("the copy is fresh — this is a disagreement, not a stale copy");
    }
}
