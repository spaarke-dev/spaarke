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
/// test that fails when a leg is removed.
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

    /// <summary>
    /// The walk stops at the FIRST link (design §6.1 rule 3), so ancestors above a linked document are not visited and their
    /// value is not read (the scope addition rides on that walk with no extra read). No writer sets both
    /// <c>sprk_relatedcommunication</c> and <c>sprk_parentdocument</c> on one document (task 188 notes); this pins the
    /// shape so a change to "always walk the ancestors" is a visible, deliberate one.
    /// </summary>
    [Fact]
    public async Task ADocumentWithItsOwnVisibleLink_IsDecidedByThatLink_ItsAncestorsAreNotVisited()
    {
        var eml = _world.Document(permission: Restricted);
        var doc = Row(Guid.NewGuid(), communication: _world.VisibleCommunication(), parent: eml);

        (await Decide(doc)).IsVisible(doc).Should().BeTrue();
        _world.ReadsOf("sprk_documents").Should().BeEmpty();
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

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────────────

    private Task<DocumentNarrowingResult> Decide(params DocumentNarrowingRow[] rows) =>
        ExternalDocumentNarrowing.DecideAsync(rows, _world.Query, CancellationToken.None);

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

        public List<(string Set, string OData)> Reads { get; } = new();

        /// <summary>An entity set whose reads throw.</summary>
        public string? Fault { get; set; }

        public IReadOnlyList<string> ReadsOf(string set) => Reads.Where(r => r.Set == set).Select(r => r.OData).ToList();

        public Task<IReadOnlyList<Dictionary<string, JsonElement>>> Query(string set, string odata, CancellationToken ct)
        {
            Reads.Add((set, odata));
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
