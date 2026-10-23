using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 188 (D7 T7, #2126 / ISS-015): THE external document narrowing,
/// <see cref="ExternalDocumentNarrowing"/>, driven over an in-memory Dataverse that answers each by-key read from seeded rows
/// and projects them through the query's own <c>$select</c> (a column the predicate does not select reads as absent). Each
/// leg has a must-fire test (hidden) and the legs share must-not-fire tests (visible); the task's mutation table names the
/// test that fails when a leg is removed. The ancestor leg checks every ancestor on the bounded walk; the communication leg
/// takes the first link (main-session decision, 2026-10-20).
/// </summary>
[Trait("category", "external-access")]
[Trait("status", "repaired")]
public sealed class ExternalDocumentNarrowingTests
{
    private const int Standard = 100000000;
    private const int Limited = 100000001;
    private const int Restricted = 100000002;
    private const int Open = 100000000;
    private const int Private = 100000001;

    private readonly World _world = new();

    // ── Leg 1: own Access Permission ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OwnRestricted_IsHidden_WithoutARead()
    {
        var doc = Row(Guid.NewGuid(), permission: Restricted);

        var result = await Decide(doc);

        result.ReasonFor(doc).Should().Be(DocumentNarrowingReason.OwnRestricted);
        _world.Reads.Should().BeEmpty("the row decides on its own column");
    }

    [Fact]
    public async Task OwnRestricted_IsHidden_EvenWhenLinkedToAVisibleCommunication()
    {
        var doc = Row(Guid.NewGuid(), permission: Restricted, communication: _world.VisibleCommunication());

        (await Decide(doc)).ReasonFor(doc).Should().Be(DocumentNarrowingReason.OwnRestricted);
    }

    [Theory]
    [InlineData(123)]
    [InlineData(100000009)]
    public async Task OwnValueThatIsNotAKnownLevel_IsHidden_FailClosed(int value)
    {
        var doc = Row(Guid.NewGuid(), permission: value);

        (await Decide(doc)).ReasonFor(doc).Should().Be(DocumentNarrowingReason.OwnUnreadable);
    }

    [Fact]
    public async Task OwnValueOfTheWrongJsonType_IsHidden_FailClosed()
    {
        var doc = ExternalDocumentNarrowing.FromWebApi(Guid.NewGuid(), new Dictionary<string, JsonElement>
        {
            ["sprk_accesspermission"] = JsonSerializer.SerializeToElement("100000000"),
        });

        (await Decide(doc)).ReasonFor(doc).Should().Be(DocumentNarrowingReason.OwnUnreadable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Standard)]
    [InlineData(Limited)]
    public async Task OwnStandardLimitedOrNull_WithNoLinkAnywhere_IsVisible_WithoutARead(int? permission)
    {
        var doc = Row(Guid.NewGuid(), permission: permission);

        (await Decide(doc)).IsVisible(doc).Should().BeTrue();
        _world.Reads.Should().BeEmpty("no link and no parent: nothing to read");
    }

    // ── Leg 2: the communication (the 065 rule, reused) ─────────────────────────────────────────────────────

    [Fact]
    public async Task LinkedToAVisibleCommunication_IsVisible()
    {
        var doc = Row(Guid.NewGuid(), communication: _world.VisibleCommunication());

        (await Decide(doc)).IsVisible(doc).Should().BeTrue();
    }

    [Fact]
    public async Task LinkedToAnInternalOnlyCommunication_IsHidden()
    {
        var doc = Row(Guid.NewGuid(), communication: _world.Communication(internalOnly: true, _world.Thread(Open)));

        (await Decide(doc)).ReasonFor(doc).Should().Be(DocumentNarrowingReason.CommunicationHidden);
    }

    [Fact]
    public async Task LinkedToACommunicationOnAPrivateThread_IsHidden()
    {
        var doc = Row(Guid.NewGuid(), communication: _world.Communication(internalOnly: false, _world.Thread(Private)));

        (await Decide(doc)).ReasonFor(doc).Should().Be(DocumentNarrowingReason.CommunicationHidden);
    }

    [Fact]
    public async Task LinkedToACommunicationWithNoThread_IsHidden()
    {
        var doc = Row(Guid.NewGuid(), communication: _world.Communication(internalOnly: false, thread: null));

        (await Decide(doc)).ReasonFor(doc).Should().Be(DocumentNarrowingReason.CommunicationHidden);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("absent")]
    [InlineData("string")]
    [InlineData("number")]
    public async Task LinkedToACommunicationWhoseFlagIsUnreadable_IsHidden(string flag)
    {
        var communication = _world.Communication(internalOnly: false, _world.Thread(Open));
        _world.SetFlag(communication, flag switch
        {
            "null" => JsonSerializer.SerializeToElement<object?>(null),
            "string" => JsonSerializer.SerializeToElement("false"),
            "number" => JsonSerializer.SerializeToElement(0),
            _ => null,
        });
        var doc = Row(Guid.NewGuid(), communication: communication);

        (await Decide(doc)).ReasonFor(doc).Should().Be(DocumentNarrowingReason.CommunicationHidden);
    }

    [Fact]
    public async Task LinkedToACommunicationThatDoesNotComeBack_IsHidden()
    {
        var doc = Row(Guid.NewGuid(), communication: Guid.NewGuid());

        (await Decide(doc)).ReasonFor(doc).Should().Be(DocumentNarrowingReason.CommunicationHidden);
    }

    [Fact]
    public async Task ACommunicationLinkThatIsNotAGuid_IsHidden_WithoutARead()
    {
        var doc = ExternalDocumentNarrowing.FromWebApi(Guid.NewGuid(), new Dictionary<string, JsonElement>
        {
            ["_sprk_relatedcommunication_value"] = JsonSerializer.SerializeToElement("not-a-guid"),
        });

        (await Decide(doc)).ReasonFor(doc).Should().Be(DocumentNarrowingReason.LinkUnreadable);
        _world.Reads.Should().BeEmpty();
    }

    // ── Leg 2 over the parent walk ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AttachmentWhoseParentEmlIsLinkedToAnInternalOnlyCommunication_IsHidden()
    {
        var eml = _world.Document(communication: _world.Communication(internalOnly: true, _world.Thread(Open)));
        var attachment = Row(Guid.NewGuid(), parent: eml);

        (await Decide(attachment)).ReasonFor(attachment).Should().Be(DocumentNarrowingReason.CommunicationHidden);
    }

    [Fact]
    public async Task AttachmentWhoseParentEmlIsLinkedToAVisibleCommunication_IsVisible()
    {
        var eml = _world.Document(communication: _world.VisibleCommunication());
        var attachment = Row(Guid.NewGuid(), parent: eml);

        (await Decide(attachment)).IsVisible(attachment).Should().BeTrue();
    }

    /// <summary>
    /// THE pin T8 looks for (design §6.1 rule 3, gap 1): a child whose parent .eml missed its communication link stays
    /// VISIBLE until T8 marks that .eml (T8's own test then flips this world by marking the parent Restricted). The parent
    /// here is unlinked AND Standard.
    /// </summary>
    [Fact]
    public async Task T8Pin_AttachmentWhoseParentEmlHasNoLink_StaysVisible()
    {
        var eml = _world.Document(permission: Standard);
        var attachment = Row(Guid.NewGuid(), parent: eml);

        (await Decide(attachment)).IsVisible(attachment).Should().BeTrue("T8 has not marked the unlinked .eml yet");
    }

    [Fact]
    public async Task AttachmentWhoseGrandparentIsLinkedToAnInternalOnlyCommunication_IsHidden()
    {
        var grandparent = _world.Document(communication: _world.Communication(internalOnly: true, _world.Thread(Open)));
        var parent = _world.Document(parent: grandparent);
        var doc = Row(Guid.NewGuid(), parent: parent);

        (await Decide(doc)).ReasonFor(doc).Should().Be(DocumentNarrowingReason.CommunicationHidden);
    }

    [Fact]
    public async Task AThreeHopChainEndingInAVisibleCommunication_IsVisible_TheThirdHopIsRead()
    {
        var p3 = _world.Document(communication: _world.VisibleCommunication());
        var p2 = _world.Document(parent: p3);
        var p1 = _world.Document(parent: p2);
        var doc = Row(Guid.NewGuid(), parent: p1);

        (await Decide(doc)).IsVisible(doc).Should().BeTrue();
        _world.ReadsOf("sprk_documents").Should().HaveCount(3, "one batched read per hop level");
    }

    [Fact]
    public async Task AThreeHopChainEndingInAnInternalOnlyCommunication_IsHidden()
    {
        var p3 = _world.Document(communication: _world.Communication(internalOnly: true, _world.Thread(Open)));
        var p2 = _world.Document(parent: p3);
        var p1 = _world.Document(parent: p2);
        var doc = Row(Guid.NewGuid(), parent: p1);

        (await Decide(doc)).ReasonFor(doc).Should().Be(DocumentNarrowingReason.CommunicationHidden);
    }

    [Fact]
    public async Task AThreeHopChainWithNoLink_IsVisible()
    {
        var p3 = _world.Document();
        var p2 = _world.Document(parent: p3);
        var p1 = _world.Document(parent: p2);
        var doc = Row(Guid.NewGuid(), parent: p1);

        (await Decide(doc)).IsVisible(doc).Should().BeTrue();
    }

    [Fact]
    public async Task AFourHopChain_IsHidden_EvenWhenTheFourthAncestorIsLinkedToAVisibleCommunication()
    {
        var p4 = _world.Document(communication: _world.VisibleCommunication());
        var p3 = _world.Document(parent: p4);
        var p2 = _world.Document(parent: p3);
        var p1 = _world.Document(parent: p2);
        var doc = Row(Guid.NewGuid(), parent: p1);

        (await Decide(doc)).ReasonFor(doc).Should().Be(DocumentNarrowingReason.ChainTooLong);
        _world.ReadsOf("sprk_documents").Should().HaveCount(3, "the walk never reads a fourth ancestor");
    }

    [Fact]
    public async Task ACycle_IsHidden()
    {
        var a = Guid.NewGuid();
        var b = _world.Document(parent: a);
        _world.Document(id: a, parent: b);
        var doc = Row(a, parent: b);

        (await Decide(doc)).ReasonFor(doc).Should().Be(DocumentNarrowingReason.Cycle);
        _world.ReadsOf("sprk_documents").Should().ContainSingle("the cycle is seen on the first ancestor that points back");
    }

    [Fact]
    public async Task ADocumentThatIsItsOwnParent_IsHidden()
    {
        var id = Guid.NewGuid();
        var doc = Row(id, parent: id);

        (await Decide(doc)).ReasonFor(doc).Should().Be(DocumentNarrowingReason.Cycle);
        _world.Reads.Should().BeEmpty();
    }

    [Fact]
    public async Task AParentThatDoesNotComeBack_IsHidden()
    {
        var doc = Row(Guid.NewGuid(), parent: Guid.NewGuid());

        (await Decide(doc)).ReasonFor(doc).Should().Be(DocumentNarrowingReason.ParentNotFound);
    }

    [Fact]
    public async Task AParentLinkThatIsNotAGuid_IsHidden_WithoutARead()
    {
        var doc = ExternalDocumentNarrowing.FromWebApi(Guid.NewGuid(), new Dictionary<string, JsonElement>
        {
            ["_sprk_parentdocument_value"] = JsonSerializer.SerializeToElement(42),
        });

        (await Decide(doc)).ReasonFor(doc).Should().Be(DocumentNarrowingReason.ParentUnreadable);
        _world.Reads.Should().BeEmpty();
    }

    // ── Leg 3: a Restricted ancestor (main-session scope addition, 2026-10-20) ─────────────────────────────

    [Fact]
    public async Task AttachmentWhoseParentEmlIsRestricted_WithNoLinkAnywhere_IsHidden()
    {
        var eml = _world.Document(permission: Restricted);
        var attachment = Row(Guid.NewGuid(), parent: eml);

        (await Decide(attachment)).ReasonFor(attachment).Should().Be(DocumentNarrowingReason.AncestorRestricted);
        _world.ReadsOf("sprk_documents").Should().ContainSingle("the ancestor's value rides on the walk's own read");
    }

    [Fact]
    public async Task ARestrictedGrandparent_HidesTheDocument()
    {
        var grandparent = _world.Document(permission: Restricted);
        var parent = _world.Document(permission: Standard, parent: grandparent);
        var doc = Row(Guid.NewGuid(), parent: parent);

        (await Decide(doc)).ReasonFor(doc).Should().Be(DocumentNarrowingReason.AncestorRestricted);
    }

    [Fact]
    public async Task ARestrictedParentLinkedToAVisibleCommunication_StillHidesTheDocument()
    {
        var eml = _world.Document(permission: Restricted, communication: _world.VisibleCommunication());
        var attachment = Row(Guid.NewGuid(), parent: eml);

        (await Decide(attachment)).ReasonFor(attachment).Should().Be(DocumentNarrowingReason.AncestorRestricted);
    }

    [Fact]
    public async Task AnAncestorWhoseValueIsUnreadable_HidesTheDocument_FailClosed()
    {
        var eml = _world.Document(permission: 7);
        var attachment = Row(Guid.NewGuid(), parent: eml);

        (await Decide(attachment)).ReasonFor(attachment).Should().Be(DocumentNarrowingReason.AncestorUnreadable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Standard)]
    [InlineData(Limited)]
    public async Task AStandardParentWithNoLink_StaysVisible(int? parentPermission)
    {
        var eml = _world.Document(permission: parentPermission);
        var attachment = Row(Guid.NewGuid(), parent: eml);

        (await Decide(attachment)).IsVisible(attachment).Should().BeTrue();
    }

    // ── Leg 3 checks EVERY ancestor, the communication leg takes the FIRST link (main-session decision, 2026-10-20) ──

    [Fact]
    public async Task ADocumentWithItsOwnVisibleLink_UnderARestrictedParentEml_IsHidden()
    {
        var eml = _world.Document(permission: Restricted);
        var doc = Row(Guid.NewGuid(), communication: _world.VisibleCommunication(), parent: eml);

        (await Decide(doc)).ReasonFor(doc).Should().Be(DocumentNarrowingReason.AncestorRestricted,
            "a Restricted ancestor anywhere on the walk hides the document, its own visible link notwithstanding");
        _world.ReadsOf("sprk_documents").Should().ContainSingle("the walk continues past the document's own link");
        _world.ReadsOf("sprk_communications").Should().BeEmpty("a walk that hid the document needs no communication read");
    }

    [Fact]
    public async Task ADocumentWithItsOwnVisibleLink_UnderARestrictedGrandparent_IsHidden()
    {
        var grandparent = _world.Document(permission: Restricted);
        var parent = _world.Document(communication: _world.VisibleCommunication(), parent: grandparent);
        var doc = Row(Guid.NewGuid(), communication: _world.VisibleCommunication(), parent: parent);

        (await Decide(doc)).ReasonFor(doc).Should().Be(DocumentNarrowingReason.AncestorRestricted);
        _world.ReadsOf("sprk_documents").Should().HaveCount(2, "one batched read per hop level, to the top of the chain");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Standard)]
    [InlineData(Limited)]
    public async Task ADocumentWithItsOwnVisibleLink_UnderAStandardParent_IsVisible_TheParentIsRead(int? parentPermission)
    {
        var eml = _world.Document(permission: parentPermission);
        var doc = Row(Guid.NewGuid(), communication: _world.VisibleCommunication(), parent: eml);

        (await Decide(doc)).IsVisible(doc).Should().BeTrue();
        _world.ReadsOf("sprk_documents").Should().ContainSingle();
    }

    /// <summary>
    /// The recorded trade-off (main-session decision, 2026-10-20): the ancestor leg cannot be decided past
    /// <see cref="ExternalDocumentNarrowing.MaxHops"/>, so a chain that needs a fourth hop hides the document even when its
    /// own link is visible.
    /// </summary>
    [Fact]
    public async Task ADocumentWithItsOwnVisibleLink_AndAChainThatNeedsAFourthHop_IsHidden()
    {
        var p4 = _world.Document();
        var p3 = _world.Document(parent: p4);
        var p2 = _world.Document(parent: p3);
        var p1 = _world.Document(parent: p2);
        var doc = Row(Guid.NewGuid(), communication: _world.VisibleCommunication(), parent: p1);

        (await Decide(doc)).ReasonFor(doc).Should().Be(DocumentNarrowingReason.ChainTooLong);
        _world.ReadsOf("sprk_documents").Should().HaveCount(3, "the walk never reads a fourth ancestor");
    }

    [Fact]
    public async Task ADocumentWithItsOwnVisibleLink_InACycle_IsHidden()
    {
        var a = Guid.NewGuid();
        var b = _world.Document(parent: a);
        _world.Document(id: a, parent: b);
        var doc = Row(a, communication: _world.VisibleCommunication(), parent: b);

        (await Decide(doc)).ReasonFor(doc).Should().Be(DocumentNarrowingReason.Cycle);
    }

    [Fact]
    public async Task ADocumentWithItsOwnInternalOnlyLink_UnderAParentLinkedToAVisibleCommunication_IsHidden()
    {
        var eml = _world.Document(communication: _world.VisibleCommunication());
        var doc = Row(Guid.NewGuid(), communication: _world.Communication(internalOnly: true, _world.Thread(Open)), parent: eml);

        (await Decide(doc)).ReasonFor(doc).Should().Be(DocumentNarrowingReason.CommunicationHidden,
            "the document's own link is the first link: it decides the communication leg");
    }

    [Fact]
    public async Task ADocumentWithItsOwnVisibleLink_UnderAParentLinkedToAnInternalOnlyCommunication_IsVisible()
    {
        var internalOnly = _world.Communication(internalOnly: true, _world.Thread(Open));
        var own = _world.VisibleCommunication();
        var eml = _world.Document(communication: internalOnly);
        var doc = Row(Guid.NewGuid(), communication: own, parent: eml);

        (await Decide(doc)).IsVisible(doc).Should().BeTrue(
            "the communication leg is unchanged: the FIRST link decides, an ancestor's link is not consulted");
        _world.ReadsOf("sprk_communications").Should().ContainSingle()
            .Which.Should().Contain(own.ToString("D")).And.NotContain(internalOnly.ToString("D"));
    }

    // ── Batching, reuse, faults ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AParentOnTheSamePage_IsNotReadAgain()
    {
        var emlId = Guid.NewGuid();
        var eml = Row(emlId, communication: _world.Communication(internalOnly: true, _world.Thread(Open)));
        var attachment = Row(Guid.NewGuid(), parent: emlId);

        var result = await Decide(eml, attachment);

        result.ReasonFor(attachment).Should().Be(DocumentNarrowingReason.CommunicationHidden);
        result.ReasonFor(eml).Should().Be(DocumentNarrowingReason.CommunicationHidden);
        _world.ReadsOf("sprk_documents").Should().BeEmpty("the parent came with the page");
    }

    [Fact]
    public async Task APageIsReadInBatches_NeverOneReadPerRow()
    {
        var communication = _world.VisibleCommunication();
        var rows = Enumerable.Range(0, 120)
            .Select(_ => Row(Guid.NewGuid(), parent: _world.Document(communication: communication)))
            .ToArray();

        var result = await Decide(rows);

        rows.Should().OnlyContain(r => result.IsVisible(r));
        _world.ReadsOf("sprk_documents").Should().HaveCount(3, "120 distinct parents in chunks of 50");
        _world.ReadsOf("sprk_communications").Should().ContainSingle("one distinct communication");
        _world.ReadsOf("sprk_communicationthreads").Should().ContainSingle();
        result.Cost.Should().Be(new DocumentNarrowingCost(Rows: 120, HopLevels: 1, DocumentReads: 3, CommunicationReads: 1, ThreadReads: 1),
            "the cost line reports what was read");
    }

    [Fact]
    public async Task ADecisionThatReadsNothing_CostsNothing()
    {
        var result = await Decide(Row(Guid.NewGuid()), Row(Guid.NewGuid(), permission: Restricted));

        result.Cost.Should().Be(new DocumentNarrowingCost(Rows: 2, HopLevels: 0, DocumentReads: 0, CommunicationReads: 0, ThreadReads: 0));
        result.Cost.Reads.Should().Be(0);
    }

    [Fact]
    public async Task TheChunksOfOneLevel_AreReadConcurrently_UpToTheCap()
    {
        // 5 chunks of parents. Every read is held until the test releases it: the cap's worth must start at once (read one
        // after another, only one ever would), and while they are held a 5th must not start (with no cap, it would).
        var cap = CommunicationOpenThreadGate.MaxChunksInFlight;
        var rows = Enumerable.Range(0, 5 * ExternalDocumentNarrowing.ChunkSize)
            .Select(_ => Row(Guid.NewGuid(), parent: _world.Document()))
            .ToArray();
        var sync = new object();
        var inFlight = 0;
        var peak = 0;
        var capReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<IReadOnlyList<Dictionary<string, JsonElement>>> Gated(string set, string odata, CancellationToken ct)
        {
            lock (sync)
            {
                inFlight++;
                peak = Math.Max(peak, inFlight);
                if (inFlight >= cap)
                    capReached.TrySetResult();
            }

            try
            {
                await release.Task.WaitAsync(ct);
                return await _world.Query(set, odata, ct);
            }
            finally
            {
                lock (sync) inFlight--;
            }
        }

        var decision = ExternalDocumentNarrowing.DecideAsync(rows, Gated, CancellationToken.None);
        try
        {
            var started = await Task.WhenAny(capReached.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            started.Should().BeSameAs(capReached.Task, "the chunks of one level are read at once, not one after another");
            await Task.Delay(TimeSpan.FromMilliseconds(300)); // room for a read past the cap to start, were the cap absent or wider
            lock (sync)
                peak.Should().Be(cap, $"at most {cap} chunks are in flight at once");
        }
        finally
        {
            release.TrySetResult();
        }

        var result = await decision;

        rows.Should().OnlyContain(r => result.IsVisible(r));
        peak.Should().Be(cap);
        _world.ReadsOf("sprk_documents").Should().HaveCount(5);
    }

    [Fact]
    public async Task AFailedRead_Throws_FromDecideAsync()
    {
        _world.Fault = "sprk_documents";
        var doc = Row(Guid.NewGuid(), parent: Guid.NewGuid());

        var act = () => Decide(doc);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("sprk_documents")]
    [InlineData("sprk_communications")]
    [InlineData("sprk_communicationthreads")]
    public async Task AFailedRead_HidesEveryRow_AndSaysSo_OnTheExternalPlane(string faultedSet)
    {
        _world.Fault = faultedSet;
        var eml = _world.Document(communication: _world.VisibleCommunication());
        var plain = Row(Guid.NewGuid());
        var attachment = Row(Guid.NewGuid(), parent: eml);

        var result = await ExternalDocumentNarrowing.DecideOrFailClosedAsync(
            new[] { plain, attachment }, _world.Query, NullLogger.Instance, CancellationToken.None);

        result.Faulted.Should().BeTrue();
        result.IsVisible(plain).Should().BeFalse("a failed read shows nothing");
        result.IsVisible(attachment).Should().BeFalse();
    }

    [Fact]
    public async Task ADocumentTheDecisionWasNotAskedAbout_IsNotVisible()
    {
        var doc = Row(Guid.NewGuid());

        var result = await Decide(doc);

        result.IsVisible(Guid.NewGuid()).Should().BeFalse();
        result.IsVisible(Row(Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task TwoReadsOfOneDocument_AreVisibleById_OnlyWhenBothAre()
    {
        var id = Guid.NewGuid();
        var open = Row(id);
        var restricted = Row(id, permission: Restricted);

        var result = await Decide(open, restricted);

        result.IsVisible(open).Should().BeTrue();
        result.IsVisible(id).Should().BeFalse();
    }

    // ── ReadAndDecideAsync (search hits) ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchHits_AreReadById_AndDecided_AHitThatDoesNotComeBackIsHidden()
    {
        var visible = _world.Document();
        var restricted = _world.Document(permission: Restricted);
        var underInternalOnly = _world.Document(parent: _world.Document(
            communication: _world.Communication(internalOnly: true, _world.Thread(Open))));
        var missing = Guid.NewGuid();

        var result = await ExternalDocumentNarrowing.ReadAndDecideAsync(
            new[] { visible, restricted, underInternalOnly, missing }, _world.Query, CancellationToken.None);

        result.IsVisible(visible).Should().BeTrue();
        result.ReasonFor(restricted).Should().Be(DocumentNarrowingReason.OwnRestricted);
        result.ReasonFor(underInternalOnly).Should().Be(DocumentNarrowingReason.CommunicationHidden);
        result.IsVisible(missing).Should().BeFalse();
        result.Cost.Should().Be(new DocumentNarrowingCost(Rows: 3, HopLevels: 1, DocumentReads: 2, CommunicationReads: 1, ThreadReads: 0),
            "the by-id read of the hits is a document read; one hop level read the .eml; an internal-only communication needs no " +
            "thread read");
    }

    // ── Task 188 round 4b: pinned defensive legs ───────────────────────────────────────────────────────────────

    [Theory(DisplayName = "Task 188 r4b pin: an id is visible only when EVERY row decided for it is visible, whatever the order")]
    [InlineData(true)]
    [InlineData(false)]
    public void AnIdWithAVisibleAndAHiddenRow_IsHidden_InEitherOrder(bool visibleFirst)
    {
        var id = Guid.NewGuid();
        var visible = new DocumentNarrowingRow(id, DocumentPermission.NotNarrowed, Guid.Empty, Guid.Empty);
        var hidden = new DocumentNarrowingRow(id, DocumentPermission.Restricted, Guid.Empty, Guid.Empty);
        var decided = new Dictionary<DocumentNarrowingRow, DocumentNarrowingReason>();
        foreach (var (row, reason) in visibleFirst
                     ? new[] { (visible, DocumentNarrowingReason.Visible), (hidden, DocumentNarrowingReason.OwnRestricted) }
                     : new[] { (hidden, DocumentNarrowingReason.OwnRestricted), (visible, DocumentNarrowingReason.Visible) })
        {
            decided[row] = reason;
        }

        var result = new DocumentNarrowingResult(decided, faulted: false);

        result.IsVisible(id).Should().BeFalse("two reads of one document cannot open it: one hidden row hides the id");
        result.ReasonFor(id).Should().Be(DocumentNarrowingReason.OwnRestricted);
    }

    [Fact(DisplayName = "Task 188 r4b pin: a by-id read that also returns a row for an id nobody asked about ignores that row")]
    public async Task AByIdRead_ARowForAnIdNobodyAskedAbout_IsIgnored()
    {
        var asked = _world.Document(permission: Restricted);
        var notAsked = _world.Document();
        CommunicationOpenThreadGate.RowQuery query = async (set, odata, ct) =>
        {
            var rows = (await _world.Query(set, odata, ct)).ToList();
            if (set == "sprk_documents" && odata.Contains(asked.ToString("D"), StringComparison.OrdinalIgnoreCase))
                rows.AddRange(await _world.Query(set, odata.Replace(asked.ToString("D"), notAsked.ToString("D"), StringComparison.OrdinalIgnoreCase), ct));
            return rows;
        };

        var result = await ExternalDocumentNarrowing.ReadAndDecideAsync(new[] { asked }, query, CancellationToken.None);

        result.IsVisible(asked).Should().BeFalse();
        result.IsVisible(notAsked).Should().BeFalse("an answer for another id is ignored: an id not asked is not decided, so not visible");
        result.ReasonFor(notAsked).Should().Be(DocumentNarrowingReason.ReadFailed);
    }

    [Fact(DisplayName = "Task 188 r4b pin: a derived row that was not resolved (absent from the source map) is hidden; one with no document is not")]
    public void ADerivedRowThatWasNotResolved_IsHidden()
    {
        var resolved = Guid.NewGuid();
        var noDocument = Guid.NewGuid();
        var unresolved = Guid.NewGuid();
        var sources = new Dictionary<Guid, IReadOnlyList<Guid>>
        {
            [resolved] = new[] { Guid.NewGuid() },
            [noDocument] = Array.Empty<Guid>(),
        };
        var hidden = new HashSet<Guid>();

        Sprk.Bff.Api.Services.Ai.Handlers.Dataverse.ExternalCallerCommunicationGuard
            .IsDerivedRowHidden(unresolved, sources, hidden, out _).Should().BeTrue("an unresolved row is undecidable (fail closed)");
        Sprk.Bff.Api.Services.Ai.Handlers.Dataverse.ExternalCallerCommunicationGuard
            .IsDerivedRowHidden(resolved, sources, hidden, out _).Should().BeFalse();
        Sprk.Bff.Api.Services.Ai.Handlers.Dataverse.ExternalCallerCommunicationGuard
            .IsDerivedRowHidden(noDocument, sources, hidden, out _).Should().BeFalse();
    }

    // ── Owner round 111: Limited does NOT hide (only Restricted does) ──────────────────────────────────────────

    [Fact]
    public async Task OwnLimited_StaysVisible_ToAnExternalCaller_OnTheByIdRead_EvenUnderALimitedParentAndAVisibleLink()
    {
        var limitedParent = _world.Document(permission: Limited);
        var limited = _world.Document(permission: Limited, communication: _world.VisibleCommunication(), parent: limitedParent);
        var limitedAlone = _world.Document(permission: Limited);

        var result = await ExternalDocumentNarrowing.ReadAndDecideAsync(new[] { limited, limitedAlone }, _world.Query, CancellationToken.None);

        result.IsVisible(limited).Should().BeTrue("owner round 111: Limited does not hide; only Restricted does");
        result.IsVisible(limitedAlone).Should().BeTrue();
        (await _world.Narrowing.HiddenDocumentIdsAsync(new[] { limited, limitedAlone }, CancellationToken.None))
            .Should().BeEmpty("the hide-only overload hides no Limited document either");
    }

    // ── Task 188 round 3 slice C: the hide-only overload ──────────────────────────────────────────────────────

    [Fact]
    public async Task HiddenDocumentIds_ReturnsOnlyTheHiddenSet_InOneCall_UndecidableIncluded_NoDocumentNever()
    {
        var visible = _world.Document();
        var restricted = _world.Document(permission: Restricted);
        var underInternalOnly = _world.Document(communication: _world.Communication(internalOnly: true, _world.Thread(Open)));
        var missing = Guid.NewGuid();

        var hidden = await _world.Narrowing.HiddenDocumentIdsAsync(
            new[] { visible, restricted, underInternalOnly, missing, ExternalDocumentNarrowing.UnreadableLink, Guid.Empty, restricted },
            CancellationToken.None);

        hidden.Should().BeEquivalentTo(new[] { restricted, underInternalOnly, missing, ExternalDocumentNarrowing.UnreadableLink },
            "own Restricted, an internal-only email, a document that does not come back and an undecidable id are hidden; " +
            "no document (Guid.Empty) and a visible one are not");
        _world.NarrowingCalls.Should().Be(1, "ONE call for the whole set");
        _world.ReadsOf("sprk_documents")[0].Should().NotContain(ExternalDocumentNarrowing.UnreadableLink.ToString("D"),
            "an undecidable id is never sent to Dataverse");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HiddenDocumentIds_NothingDecidable_MakesNoCall(bool onlyUndecidable)
    {
        var ids = onlyUndecidable ? new[] { ExternalDocumentNarrowing.UnreadableLink, Guid.Empty } : new[] { Guid.Empty };

        var hidden = await _world.Narrowing.HiddenDocumentIdsAsync(ids, CancellationToken.None);

        hidden.Should().BeEquivalentTo(onlyUndecidable ? new[] { ExternalDocumentNarrowing.UnreadableLink } : Array.Empty<Guid>());
        _world.NarrowingCalls.Should().Be(0);
        _world.Reads.Should().BeEmpty();
    }

    [Fact]
    public async Task HiddenDocumentIds_AFailedRead_Throws()
    {
        var doc = _world.Document(parent: _world.Document());
        _world.Fault = "sprk_documents";

        var act = () => _world.Narrowing.HiddenDocumentIdsAsync(new[] { doc }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>("a failed read never turns into 'nothing hidden'");
    }

    // ── Row parsing ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FromWebApi_ReadsTheThreeColumns()
    {
        var communication = Guid.NewGuid();
        var parent = Guid.NewGuid();
        var id = Guid.NewGuid();

        var row = ExternalDocumentNarrowing.FromWebApi(id, Json(new Dictionary<string, object?>
        {
            ["sprk_accesspermission"] = Restricted,
            ["_sprk_relatedcommunication_value"] = communication.ToString(),
            ["_sprk_parentdocument_value"] = parent.ToString(),
        }));

        row.Should().Be(new DocumentNarrowingRow(id, DocumentPermission.Restricted, communication, parent));
    }

    [Fact]
    public void FromWebApi_AnEmptyGuidLink_IsUnreadable_NotUnset()
    {
        var row = ExternalDocumentNarrowing.FromWebApi(Guid.NewGuid(), Json(new Dictionary<string, object?>
        {
            ["_sprk_relatedcommunication_value"] = Guid.Empty.ToString(),
        }));

        row.Communication.Should().Be(ExternalDocumentNarrowing.UnreadableLink);
    }

    [Fact]
    public void FromFetch_ReadsSdkTypes_AndTreatsAnUnknownShapeAsUnreadable()
    {
        var id = Guid.NewGuid();
        var communication = Guid.NewGuid();
        var parent = Guid.NewGuid();

        ExternalDocumentNarrowing.FromFetch(new Dictionary<string, object?>
        {
            ["sprk_documentid"] = id,
            ["sprk_accesspermission"] = new OptionSetValue(Restricted),
            ["sprk_relatedcommunication"] = new EntityReference("sprk_communication", communication),
            ["sprk_parentdocument"] = new EntityReference("sprk_document", parent),
        }).Should().Be(new DocumentNarrowingRow(id, DocumentPermission.Restricted, communication, parent));

        var odd = ExternalDocumentNarrowing.FromFetch(new Dictionary<string, object?>
        {
            ["sprk_accesspermission"] = "Restricted",
            ["sprk_relatedcommunication"] = 12,
        });
        odd.Own.Should().Be(DocumentPermission.Unreadable);
        odd.Communication.Should().Be(ExternalDocumentNarrowing.UnreadableLink);
        odd.Parent.Should().Be(Guid.Empty, "an attribute the SDK omitted is not set");
    }

    // ── Owner round 122: the communication walk alone (it releases a provisional own narrowing) ─────────────

    [Fact]
    public async Task Round122Walk_TheDocumentsOwnLink_IsFound_WithoutARead()
    {
        var communication = Guid.NewGuid();
        var doc = Row(Guid.NewGuid(), permission: Restricted, communication: communication, parent: Guid.NewGuid());

        var link = await FindLink(doc);

        link.IsLinked.Should().BeTrue("the walk ignores the own leg: a provisional .eml is Restricted until it is released");
        link.Communication.Should().Be(communication);
        link.Path.Should().Equal(doc);
        _world.Reads.Should().BeEmpty("the first link found ends the walk");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Round122Walk_TheFirstLinkUpTheParentChain_IsFound_WithinThreeHops_PastRestrictedAncestors(int hops)
    {
        var communication = Guid.NewGuid();
        var above = _world.Document(permission: Restricted, communication: communication);
        var ancestors = new List<Guid> { above };
        for (var i = 1; i < hops; i++)
        {
            above = _world.Document(permission: Restricted, parent: above);
            ancestors.Insert(0, above);
        }

        var doc = Row(Guid.NewGuid(), permission: Restricted, parent: above);

        var link = await FindLink(doc);

        link.IsLinked.Should().BeTrue();
        link.Communication.Should().Be(communication);
        link.Path.Select(r => r.DocumentId).Should().Equal(
            new[] { doc.DocumentId }.Concat(ancestors),
            "fix round 1 (F1): the rows it passed through, from the document itself up to the one carrying the link");
        _world.ReadsOf("sprk_documents").Should().HaveCount(hops, "one batched read per hop level");
        _world.ReadsOf("sprk_communications").Should().BeEmpty("the walk decides nothing about visibility");
    }

    [Fact]
    public async Task Round122Walk_ALinkFourHopsUp_IsNotFound_ChainTooLong()
    {
        var p4 = _world.Document(communication: Guid.NewGuid());
        var p3 = _world.Document(parent: p4);
        var p2 = _world.Document(parent: p3);
        var p1 = _world.Document(parent: p2);
        var doc = Row(Guid.NewGuid(), parent: p1);

        var link = await FindLink(doc);

        link.IsLinked.Should().BeFalse();
        link.Unknowable.Should().Be(DocumentNarrowingReason.ChainTooLong);
        _world.ReadsOf("sprk_documents").Should().HaveCount(3, "a fourth ancestor is never read");
    }

    [Fact]
    public async Task Round122Walk_NoLinkAnywhere_IsNotLinked_AndKnown()
    {
        var p2 = _world.Document();
        var p1 = _world.Document(parent: p2);
        var doc = Row(Guid.NewGuid(), parent: p1);

        var link = await FindLink(doc);

        link.Communication.Should().Be(Guid.Empty);
        link.Unknowable.Should().BeNull();
        link.IsLinked.Should().BeFalse();
        link.Path.Select(r => r.DocumentId).Should().Equal(doc.DocumentId, p1, p2);
    }

    [Fact]
    public async Task Round122Walk_ACycleBeforeAnyLink_IsUnknowable()
    {
        var a = Guid.NewGuid();
        var b = _world.Document(parent: a);
        _world.Document(id: a, parent: b);
        var doc = Row(a, parent: b);

        var link = await FindLink(doc);

        link.IsLinked.Should().BeFalse();
        link.Unknowable.Should().Be(DocumentNarrowingReason.Cycle);
    }

    [Fact]
    public async Task Round122Walk_AParentThatDoesNotComeBack_IsUnknowable()
    {
        var link = await FindLink(Row(Guid.NewGuid(), parent: Guid.NewGuid()));

        link.IsLinked.Should().BeFalse();
        link.Unknowable.Should().Be(DocumentNarrowingReason.ParentNotFound);
    }

    [Fact]
    public async Task Round122Walk_UnreadableLinks_AreUnknowable_NeverALink()
    {
        var badParent = ExternalDocumentNarrowing.FromWebApi(Guid.NewGuid(), new Dictionary<string, JsonElement>
        {
            ["_sprk_parentdocument_value"] = JsonSerializer.SerializeToElement(42),
        });
        var badLink = ExternalDocumentNarrowing.FromWebApi(Guid.NewGuid(), new Dictionary<string, JsonElement>
        {
            ["_sprk_relatedcommunication_value"] = JsonSerializer.SerializeToElement("not-a-guid"),
        });
        (await FindLink(badParent)).Unknowable.Should().Be(DocumentNarrowingReason.ParentUnreadable);
        (await FindLink(badLink)).Unknowable.Should().Be(DocumentNarrowingReason.LinkUnreadable);
    }

    [Fact]
    public async Task Round122Walk_AFailedRead_Throws()
    {
        _world.Fault = "sprk_documents";
        var doc = Row(Guid.NewGuid(), parent: _world.Document(communication: Guid.NewGuid()));

        var act = () => FindLink(doc);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "the caller turns a failed read into Undecidable (link_read_failed): no release, and the row is carried");
    }

    [Fact]
    public async Task Round122_D7IsUnchanged_AReleasedEmlAndItsReleasedChild_UnderAnInternalOnlyCommunication_StayHidden()
    {
        // After the release both hold the communication's value (here Standard): leg 1 and leg 3 no longer fire, and the
        // communication leg still hides them from partners.
        var internalOnly = _world.Communication(internalOnly: true, _world.Thread(Open));
        var eml = _world.Document(permission: Standard, communication: internalOnly);
        var releasedEml = Row(eml, permission: Standard, communication: internalOnly);
        var releasedChild = Row(Guid.NewGuid(), permission: Standard, parent: eml);

        (await FindLink(releasedChild)).Communication.Should().Be(internalOnly, "the walk that released it found this link");
        var result = await Decide(releasedEml, releasedChild);

        result.ReasonFor(releasedEml).Should().Be(DocumentNarrowingReason.CommunicationHidden);
        result.ReasonFor(releasedChild).Should().Be(DocumentNarrowingReason.CommunicationHidden);
    }

    [Fact]
    public void Round122_FromEntity_ReadsAnSdkRowAsFromFetchDoes_WithItsId()
    {
        var id = Guid.NewGuid();
        var communication = Guid.NewGuid();
        var row = new Entity("sprk_document", id)
        {
            ["sprk_accesspermission"] = new OptionSetValue(Restricted),
            ["sprk_relatedcommunication"] = new EntityReference("sprk_communication", communication),
        };

        ExternalDocumentNarrowing.FromEntity(row)
            .Should().Be(new DocumentNarrowingRow(id, DocumentPermission.Restricted, communication, Guid.Empty));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────────────

    private Task<DocumentNarrowingResult> Decide(params DocumentNarrowingRow[] rows) =>
        ExternalDocumentNarrowing.DecideAsync(rows, _world.Query, CancellationToken.None);

    /// <summary>Round 122: the communication walk over this world, reading the documents by key as the narrowing does.</summary>
    private async Task<DocumentCommunicationLink> FindLink(DocumentNarrowingRow row)
    {
        var found = await ExternalDocumentNarrowing.FindCommunicationLinksAsync([row], ReadDocuments, CancellationToken.None);
        return found[row];
    }

    private async Task<IReadOnlyList<DocumentNarrowingRow>> ReadDocuments(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var odata = $"$select=sprk_documentid,{string.Join(",", ExternalDocumentNarrowing.SelectColumns)}" +
                    $"&$filter={string.Join(" or ", ids.Select(id => $"sprk_documentid eq {id:D}"))}";
        var rows = await _world.Query("sprk_documents", odata, ct);
        return rows.Select(r => ExternalDocumentNarrowing.FromWebApi(Guid.Parse(r["sprk_documentid"].GetString()!), r)).ToList();
    }

    /// <summary>A document row as a surface reads it (with the predicate's columns).</summary>
    private static DocumentNarrowingRow Row(Guid id, int? permission = null, Guid? communication = null, Guid? parent = null) =>
        ExternalDocumentNarrowing.FromWebApi(id, Json(new Dictionary<string, object?>
        {
            ["sprk_documentid"] = id.ToString(),
            ["sprk_accesspermission"] = permission,
            ["_sprk_relatedcommunication_value"] = communication?.ToString(),
            ["_sprk_parentdocument_value"] = parent?.ToString(),
        }));

    private static Dictionary<string, JsonElement> Json(Dictionary<string, object?> row) =>
        row.ToDictionary(kv => kv.Key, kv => JsonSerializer.SerializeToElement(kv.Value));

    /// <summary>
    /// An in-memory Dataverse for the three sets the narrowing reads by key. Each read returns the seeded rows whose key is in
    /// the <c>$filter</c>, projected through <c>$select</c>, and is recorded.
    /// </summary>
    private sealed class World
    {
        private readonly Dictionary<string, (string Key, List<Dictionary<string, JsonElement>> Rows)> _sets = new()
        {
            ["sprk_documents"] = ("sprk_documentid", new()),
            ["sprk_communications"] = ("sprk_communicationid", new()),
            ["sprk_communicationthreads"] = ("sprk_communicationthreadid", new()),
        };

        private readonly List<(string Set, string OData)> _reads = new();

        public IReadOnlyList<(string Set, string OData)> Reads
        {
            get { lock (_reads) return _reads.ToList(); }
        }

        /// <summary>An entity set whose reads throw.</summary>
        public string? Fault { get; set; }

        public IReadOnlyList<string> ReadsOf(string set) => Reads.Where(r => r.Set == set).Select(r => r.OData).ToList();

        private int _narrowingCalls;

        /// <summary>The app-only seam over this world (the by-id read and decision), counting its calls.</summary>
        public AppOnlyDocumentNarrowing Narrowing => (ids, ct) =>
        {
            Interlocked.Increment(ref _narrowingCalls);
            return ExternalDocumentNarrowing.ReadAndDecideAsync(ids, Query, ct);
        };

        public int NarrowingCalls => Volatile.Read(ref _narrowingCalls);

        public Task<IReadOnlyList<Dictionary<string, JsonElement>>> Query(string set, string odata, CancellationToken ct)
        {
            lock (_reads) _reads.Add((set, odata));
            if (set == Fault)
                throw new InvalidOperationException($"read of {set} failed");

            var (key, rows) = _sets[set];
            var ids = Regex.Matches(odata, $@"{key} eq ([0-9a-fA-F-]{{36}})").Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var select = Regex.Match(odata, @"\$select=([^&]*)").Groups[1].Value.Split(',').ToHashSet(StringComparer.Ordinal);
            IReadOnlyList<Dictionary<string, JsonElement>> hit = rows
                .Where(r => ids.Contains(r[key].GetString()!))
                .Select(r => r.Where(kv => select.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value))
                .ToList();
            return Task.FromResult(hit);
        }

        public Guid Document(Guid? id = null, int? permission = null, Guid? communication = null, Guid? parent = null)
        {
            var documentId = id ?? Guid.NewGuid();
            _sets["sprk_documents"].Rows.Add(Json(new Dictionary<string, object?>
            {
                ["sprk_documentid"] = documentId.ToString(),
                ["sprk_documentname"] = "seeded",
                ["sprk_accesspermission"] = permission,
                ["_sprk_relatedcommunication_value"] = communication?.ToString(),
                ["_sprk_parentdocument_value"] = parent?.ToString(),
            }));
            return documentId;
        }

        public Guid Thread(int privacy)
        {
            var id = Guid.NewGuid();
            _sets["sprk_communicationthreads"].Rows.Add(Json(new Dictionary<string, object?>
            {
                ["sprk_communicationthreadid"] = id.ToString(),
                ["sprk_privacystate"] = privacy,
            }));
            return id;
        }

        public Guid Communication(bool internalOnly, Guid? thread)
        {
            var id = Guid.NewGuid();
            _sets["sprk_communications"].Rows.Add(Json(new Dictionary<string, object?>
            {
                ["sprk_communicationid"] = id.ToString(),
                ["sprk_isinternalonly"] = internalOnly,
                ["_sprk_communicationthread_value"] = thread?.ToString(),
            }));
            return id;
        }

        public Guid VisibleCommunication() => Communication(internalOnly: false, Thread(Open));

        /// <summary>Replaces a communication's flag (<c>null</c>: the column is absent).</summary>
        public void SetFlag(Guid communication, JsonElement? value)
        {
            var row = _sets["sprk_communications"].Rows.Single(r => r["sprk_communicationid"].GetString() == communication.ToString());
            row.Remove("sprk_isinternalonly");
            if (value is { } v)
                row["sprk_isinternalonly"] = v;
        }
    }
}
