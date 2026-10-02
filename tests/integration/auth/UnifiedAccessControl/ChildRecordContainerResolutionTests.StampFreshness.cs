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

    [Fact(DisplayName = "Task 156: a record above that is ITSELF secure (an invoice with its own flag) is a secure root — its container, never the matter's BU")]
    public async Task SecureIntermediate_IsASecureRootInItsOwnRight()
    {
        var world = new World(securable: SecurableWithInvoice)
            .WithRow("sprk_todo", ChildId,
                [("sprk_regardinginvoice", "sprk_invoice", IntermediateId), ("sprk_regardingmatter", "sprk_matter", MatterId)],
                pairId: IntermediateId.ToString())
            .WithRow("sprk_invoice", IntermediateId, [("sprk_matter", "sprk_matter", MatterId)],
                isSecure: true, container: OtherRootContainer)
            .WithRoot("sprk_matter", MatterId, isSecure: false, containerId: null)
            .WithBusinessUnit(BusinessUnitContainer);

        (await world.Resolver().ResolveForRecordAsync("sprk_todo", ChildId)).ContainerId.Should().Be(OtherRootContainer,
            "a child of a secure record is secure (C10 part 2) — the invoice is that record");
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
}
