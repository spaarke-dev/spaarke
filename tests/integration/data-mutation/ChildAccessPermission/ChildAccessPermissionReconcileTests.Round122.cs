using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ChildAccessPermission;

/// <summary>
/// unified-access-control-r2, owner round 122 (2026-10-22): "the access should match the parent". An email-derived document
/// saved own-Restricted because its email could not be identified (I-19, #2177) follows its communication once it is linked:
/// the reconciler releases the PROVISIONAL narrowing when the communication walk (task 188's, reused) finds a link on the
/// document or up its <c>sprk_parentdocument</c> chain within 3 hops, and persists the release. Through the job exactly as the
/// scheduler runs it, over the in-memory Dataverse. Every provisional row here is created by the real save-time writer
/// (<see cref="OfficeDocumentPersistence.ApplyEmailCommunicationOutcome"/>).
/// </summary>
public partial class ChildAccessPermissionReconcileTests
{
    /// <summary>An email-derived document as the save creates it when its email cannot be identified, with its filing.</summary>
    private static Guid SavedUnidentified(Harness job, params (string Column, string Target, Guid Id)[] lookups)
    {
        var create = new CreateDocumentRequest { Name = "Unknown.eml", ContainerId = "b!c" };
        OfficeDocumentPersistence.ApplyEmailCommunicationOutcome(create, relatedCommunicationId: null).Should().BeTrue();
        var document = job.Child("sprk_document", create.AccessPermission, lookups);
        job.World.Set("sprk_document", document, Record, create.AccessInheritanceRecord);
        job.World.Modified("sprk_document", document, job.Now.UtcDateTime);
        return document;
    }

    /// <summary>What a link writer does (<c>CrossPathLink</c>, the sweep): the link alone, nothing else.</summary>
    private static void Link(Harness job, Guid document, Guid communication)
    {
        job.World.Set("sprk_document", document, "sprk_relatedcommunication", new EntityReference("sprk_communication", communication));
        job.World.Modified("sprk_document", document, job.Now.UtcDateTime);
    }

    private static (Guid Matter, Guid Communication) CommunicationUnder(Harness job, int matterValue)
    {
        var matter = job.Root("sprk_matter", matterValue);
        var communication = job.Child("sprk_communication", matterValue, ("sprk_regardingmatter", "sprk_matter", matter));
        return (matter, communication);
    }

    [Fact]
    public async Task Round122_ALinkedProvisionalEml_FollowsItsCommunicationsTops_AndTheReleaseIsPersistedOnce()
    {
        // Must fire. Before round 122 the save-time Restricted was the document's own for good.
        var job = new Harness();
        var (matter, communication) = CommunicationUnder(job, Limited);
        var eml = SavedUnidentified(job);
        await job.RunAsync();
        ValueOf(job, eml).Should().Be(Restricted, "unlinked, it stays Restricted");
        RecordOf(job, eml).OwnProvisional.Should().BeTrue();

        Link(job, eml, communication);
        await job.RunAsync();

        ValueOf(job, eml).Should().Be(Limited, "the communication's tops (its matter) give Limited");
        var record = RecordOf(job, eml);
        record.OwnPermission.Should().BeNull("released: no own value");
        record.OwnProvisional.Should().BeFalse("the release is one way");
        record.FloorPermission.Should().Be(Limited);
        record.Parents.Should().BeEquivalentTo(new[] { $"sprk_communication:{communication:D}" });
        job.World.CombinedAccessWrites.Should().ContainSingle("value and record in ONE update");
        job.World.AccessPermissionWrites.Should().ContainSingle().Which.Should().Be(("sprk_document", eml, Limited));

        // It now follows the communication both ways, as any inherited value does.
        job.World.Set("sprk_matter", matter, Column, new OptionSetValue(Standard));
        job.World.Modified("sprk_matter", matter, job.Now.UtcDateTime);
        await job.RunAsync();
        ValueOf(job, eml).Should().Be(Standard);
        job.LastResult!.Success.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Round122_AnAttachmentChild_IsReleased_WhenTheEmlItHangsUnderIsLinked(int hops)
    {
        // Must fire: the child is linked to its .eml through sprk_parentdocument, never to the communication itself.
        var job = new Harness();
        var (_, communication) = CommunicationUnder(job, Standard);
        var eml = SavedUnidentified(job);
        var chain = new List<Guid> { eml };
        for (var i = 0; i < hops; i++)
            chain.Add(SavedUnidentified(job, ("sprk_parentdocument", "sprk_document", chain[^1])));
        await job.RunAsync();
        chain.Should().OnlyContain(d => ValueOf(job, d) == Restricted);

        Link(job, eml, communication);
        await job.RunAsync();

        foreach (var document in chain)
        {
            ValueOf(job, document).Should().Be(Standard, $"it is {chain.IndexOf(document)} hop(s) below the link");
            RecordOf(job, document).OwnProvisional.Should().BeFalse();
            RecordOf(job, document).OwnPermission.Should().BeNull();
        }
    }

    [Fact]
    public async Task Round122_AChildFourHopsBelowTheLink_KeepsRestricted_TheThreeAboveItAreReleased()
    {
        // Must not fire past the bound: the walk is at most 3 parent hops (D7's), whatever the floor says.
        var job = new Harness();
        var (_, communication) = CommunicationUnder(job, Standard);
        var eml = SavedUnidentified(job);
        var chain = new List<Guid> { eml };
        for (var i = 0; i < 4; i++)
            chain.Add(SavedUnidentified(job, ("sprk_parentdocument", "sprk_document", chain[^1])));

        Link(job, eml, communication);
        await job.RunAsync();

        chain.Take(4).Should().OnlyContain(d => ValueOf(job, d) == Standard);
        ValueOf(job, chain[4]).Should().Be(Restricted, "its link is 4 hops up");
        RecordOf(job, chain[4]).OwnProvisional.Should().BeTrue("unreleased, so it keeps the marker");
        RecordOf(job, chain[4]).FloorPermission.Should().Be(Standard, "its floor was decided; only the walk keeps it");
    }

    [Fact]
    public async Task Round122_AProvisionalChildWhoseChainLoops_KeepsRestricted_ThoughItsFloorIsDecided_UntilTheLoopGivesWayToALink()
    {
        // Fix round 1 (K4): the loop decides the outcome. The walk visits every document on a loop before it sees the loop,
        // so a link ON the loop would be found; what the loop does is end the walk before the document that carries the
        // link. Here b's parent is a (the loop) instead of the linked c: the floor is decided (a is also filed under a
        // matter), and only the loop keeps the child Restricted. Re-pointing b to c, nothing else changed, releases it.
        var job = new Harness();
        var (matter, communication) = CommunicationUnder(job, Standard);
        var c = SavedUnidentified(job);
        Link(job, c, communication);
        var a = SavedUnidentified(job, ("sprk_matter", "sprk_matter", matter));
        var b = SavedUnidentified(job, ("sprk_parentdocument", "sprk_document", a));
        job.World.Set("sprk_document", a, "sprk_parentdocument", new EntityReference("sprk_document", b));
        var child = SavedUnidentified(job, ("sprk_parentdocument", "sprk_document", a));

        var first = await job.RunAsync();
        await job.RunAsync();

        ValueOf(job, child).Should().Be(Restricted, "its walk ends at the loop, before any link");
        RecordOf(job, child).OwnProvisional.Should().BeTrue();
        RecordOf(job, child).FloorPermission.Should().Be(Standard, "its floor was decided: only the loop keeps it Restricted");
        job.World.AccessPermissionWrites.Should().NotContain(w => w.Id == child);
        first.GetProperty("accessPermission").GetProperty("undetermined").GetInt32().Should().Be(
            0, "a loop is no link within the bound, not an undecidable step: it is not carried");
        ValueOf(job, c).Should().Be(Standard, "the linked document beside it is released");

        job.World.Set("sprk_document", b, "sprk_parentdocument", new EntityReference("sprk_document", c)); // the loop gives way
        job.World.Modified("sprk_document", b, job.Now.UtcDateTime);
        await job.RunAsync();

        ValueOf(job, child).Should().Be(Standard, "the same chain without the loop reaches c's link at 3 hops");
        RecordOf(job, child).OwnProvisional.Should().BeFalse();
    }

    [Fact]
    public async Task Round122_AProvisionalRowWithNoLink_StaysRestricted_UnderAWiderMatter()
    {
        // Must not fire: filed under a Standard matter, but no communication link anywhere.
        var job = new Harness();
        var matter = job.Root("sprk_matter", Standard);
        var eml = SavedUnidentified(job, ("sprk_matter", "sprk_matter", matter));

        await job.RunAsync();
        await job.RunAsync();

        ValueOf(job, eml).Should().Be(Restricted);
        job.World.AccessPermissionWrites.Should().BeEmpty();
        var record = RecordOf(job, eml);
        record.OwnPermission.Should().Be(Restricted);
        record.OwnProvisional.Should().BeTrue("the marker survives the reconcile that fills in its parents");
        record.Parents.Should().BeEquivalentTo(new[] { $"sprk_matter:{matter:D}" });
    }

    [Fact]
    public async Task Round122_AHandSetRestrictedOnALinkedArchive_Stays()
    {
        // Must not fire (round 87): a person's Restricted on a linked .eml is never released by its link, before or after a
        // provisional release.
        var job = new Harness();
        var (_, communication) = CommunicationUnder(job, Standard);
        var linked = job.Child("sprk_document", Standard, ("sprk_relatedcommunication", "sprk_communication", communication));
        var released = SavedUnidentified(job);
        Link(job, released, communication);
        await job.RunAsync();
        ValueOf(job, released).Should().Be(Standard);

        foreach (var document in new[] { linked, released })
        {
            job.World.Set("sprk_document", document, Column, new OptionSetValue(Restricted)); // a person sets it
            job.World.Modified("sprk_document", document, job.Now.UtcDateTime);
        }

        await job.RunAsync();
        await job.RunAsync();

        foreach (var document in new[] { linked, released })
        {
            ValueOf(job, document).Should().Be(Restricted);
            RecordOf(job, document).OwnPermission.Should().Be(Restricted);
            RecordOf(job, document).OwnProvisional.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Round122_AProvisionalRowReSetByHand_StaysRestricted_WhenItIsLinkedLater()
    {
        var job = new Harness();
        var (matter, communication) = CommunicationUnder(job, Standard);
        var eml = SavedUnidentified(job, ("sprk_matter", "sprk_matter", matter));
        await job.RunAsync();

        job.World.Set("sprk_document", eml, Column, new OptionSetValue(Standard)); // a person lowers it ...
        job.World.Modified("sprk_document", eml, job.Now.UtcDateTime);
        await job.RunAsync();
        RecordOf(job, eml).OwnProvisional.Should().BeFalse("the person's change drops the marker");
        job.World.Set("sprk_document", eml, Column, new OptionSetValue(Restricted)); // ... and sets Restricted again
        job.World.Modified("sprk_document", eml, job.Now.UtcDateTime);
        await job.RunAsync();

        Link(job, eml, communication);
        await job.RunAsync();

        ValueOf(job, eml).Should().Be(Restricted, "the person's Restricted is theirs");
        RecordOf(job, eml).OwnPermission.Should().Be(Restricted);
    }

    // ── Fix round 1 ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A person sets a document's value by hand (the form, a grid, the Web API).</summary>
    private static void HandSet(Harness job, Guid document, int value)
    {
        job.World.Set("sprk_document", document, Column, new OptionSetValue(value));
        job.World.Modified("sprk_document", document, job.Now.UtcDateTime);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Round122Fix1_APersonRestrictedEml_KeepsItsProvisionalChildrenRestricted_AfterItIsLinked(int hops)
    {
        // F1 (word-add-in), must not fire: the children find the .eml's link 1 to 3 hops up, but a person made the .eml's
        // Restricted their own (lowered it, then raised it again: round 87), so no child is released wider than it.
        var job = new Harness();
        var (_, communication) = CommunicationUnder(job, Standard);
        var eml = SavedUnidentified(job);
        var chain = new List<Guid> { eml };
        for (var i = 0; i < hops; i++)
            chain.Add(SavedUnidentified(job, ("sprk_parentdocument", "sprk_document", chain[^1])));
        await job.RunAsync();
        HandSet(job, eml, Standard);
        await job.RunAsync();
        HandSet(job, eml, Restricted);
        await job.RunAsync();
        RecordOf(job, eml).OwnProvisional.Should().BeFalse("the person's change made it theirs");
        RecordOf(job, eml).OwnPermission.Should().Be(Restricted);

        Link(job, eml, communication);
        var linked = await job.RunAsync();
        await job.RunAsync();

        ValueOf(job, eml).Should().Be(Restricted, "a person's Restricted stays (round 87)");
        foreach (var child in chain.Skip(1))
        {
            ValueOf(job, child).Should().Be(Restricted, $"{chain.IndexOf(child)} hop(s) below a person's Restricted");
            RecordOf(job, child).OwnProvisional.Should().BeTrue("unreleased, so it keeps the marker");
            RecordOf(job, child).FloorPermission.Should().Be(Standard, "its floor is the communication's tops: only the rule keeps it");
        }

        linked.GetProperty("accessPermission").GetProperty("undetermined").GetInt32().Should().Be(
            0, "a person's narrowing on the way is decided, not carried");
    }

    [Fact]
    public async Task Round122Fix1_APersonRestrictedDocumentBetweenAChildAndTheLink_KeepsTheChildRestricted()
    {
        // F1, must not fire: the document a person narrowed is in the middle; the provisional .eml above it is released. The
        // middle document's Restricted is its own: set while it had no parent, then it was filed under the .eml (its own
        // narrowing is kept; a Restricted set while under the Restricted .eml would read as inherited, round 87).
        var job = new Harness();
        var (_, communication) = CommunicationUnder(job, Standard);
        var eml = SavedUnidentified(job);
        var middle = job.Child("sprk_document", Restricted);
        await job.RunAsync();
        job.World.Set("sprk_document", middle, "sprk_parentdocument", new EntityReference("sprk_document", eml));
        job.World.Modified("sprk_document", middle, job.Now.UtcDateTime);
        var child = SavedUnidentified(job, ("sprk_parentdocument", "sprk_document", middle));
        await job.RunAsync();
        RecordOf(job, middle).OwnPermission.Should().Be(Restricted);
        RecordOf(job, middle).OwnProvisional.Should().BeFalse();

        Link(job, eml, communication);
        await job.RunAsync();
        await job.RunAsync();

        ValueOf(job, eml).Should().Be(Standard, "the provisional .eml is released");
        ValueOf(job, middle).Should().Be(Restricted, "its own");
        ValueOf(job, child).Should().Be(Restricted, "never wider than the person's Restricted above it");
        RecordOf(job, child).OwnProvisional.Should().BeTrue();
        RecordOf(job, child).FloorPermission.Should().Be(Standard, "the floor is the tops only: the rule keeps it");
    }

    [Fact]
    public async Task Round122Fix1_AnUnreadableRecordOnTheWayToTheLink_KeepsTheChildRestricted_AndCarriesIt()
    {
        // F1 + B: whether the .eml above holds a person's narrowing cannot be read, so the child stays Restricted and is
        // decided again next run; once the record reads again the child is released.
        var job = new Harness();
        var (_, communication) = CommunicationUnder(job, Standard);
        var eml = SavedUnidentified(job);
        var child = SavedUnidentified(job, ("sprk_parentdocument", "sprk_document", eml));
        await job.RunAsync();
        var emlRecord = job.World.ValueOf<string>("sprk_document", eml, Record);
        job.World.Set("sprk_document", eml, Record, """{"v":99}""");

        Link(job, eml, communication);
        var report = await job.RunAsync();

        ValueOf(job, child).Should().Be(Restricted);
        RecordOf(job, child).OwnProvisional.Should().BeTrue();
        var accessPermission = report.GetProperty("accessPermission");
        accessPermission.GetProperty("undetermined").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        accessPermission.GetProperty("problems").EnumerateArray().Select(p => p.GetString())
            .Should().Contain(p => p!.Contains(child.ToString("D")) && p.Contains("chain_record_unreadable"));

        job.World.Set("sprk_document", eml, Record, emlRecord); // the record reads again; nothing lists the child but its carry
        await job.RunAsync();

        ValueOf(job, child).Should().Be(Standard, "carried, so it is decided again and released");
    }

    [Fact]
    public async Task Round122Fix1_AHopReadThatFaultsOnce_IsCarried_AndTheReleaseHappensOnTheNextRun()
    {
        // B: a failed link walk is never read as "not linked". The .eml's link lands without this pass listing the .eml (its
        // write fell outside the window, e.g. after a restart), and the child is touched, so the child is decided while the
        // walk must read the .eml: that read faults once. The child stays Restricted that run, is carried, and is released
        // on the next run; nothing else lists it (a write does not move modifiedon in this world).
        var job = new Harness();
        var (_, communication) = CommunicationUnder(job, Standard);
        var eml = SavedUnidentified(job);
        var child = SavedUnidentified(job, ("sprk_parentdocument", "sprk_document", eml));
        await job.RunAsync();
        ValueOf(job, child).Should().Be(Restricted);

        job.World.Set("sprk_document", eml, "sprk_relatedcommunication", new EntityReference("sprk_communication", communication));
        job.World.Modified("sprk_document", child, job.Now.UtcDateTime);
        job.World.TransientIdListReadFaultsOf("sprk_document", eml, read => read == 1);
        var faulted = await job.RunAsync();

        job.World.TransientIdListReadFaultsFired("sprk_document", eml).Should().Be(1, "the walk's hop read faulted");
        ValueOf(job, child).Should().Be(Restricted, "fail closed this run");
        var accessPermission = faulted.GetProperty("accessPermission");
        accessPermission.GetProperty("undetermined").GetInt32().Should().Be(1);
        accessPermission.GetProperty("carriedRows").GetInt32().Should().Be(1);
        accessPermission.GetProperty("problems").EnumerateArray().Select(p => p.GetString())
            .Should().ContainSingle(p => p!.Contains(child.ToString("D")) && p.Contains("link_read_failed"));

        await job.RunAsync();

        ValueOf(job, child).Should().Be(Standard, "carried, and released on the next run");
        RecordOf(job, child).OwnProvisional.Should().BeFalse();
        RecordOf(job, child).OwnPermission.Should().BeNull();
    }

    [Fact]
    public async Task Round122Fix1_ALinkColumnThatIsNotFieldSecured_ReleasesNothing_UntilItIs()
    {
        // K1: a user who can write a document (or an .eml but not its children) could set a link and release a Restricted.
        // Without both link columns field-secured, nothing is released. Fix round 2 (K2): that is a refusal, not an undecidable
        // row: it is counted apart and NOT carried; once the columns are secured, it is released when it is next listed.
        var job = new Harness();
        job.LinkColumns.Secured[ChildAccessPermissionReconciler.ReleaseLinkColumns[1]] = false;
        var (_, communication) = CommunicationUnder(job, Standard);
        var eml = SavedUnidentified(job);
        await job.RunAsync();

        Link(job, eml, communication);
        var unsecured = await job.RunAsync();

        ValueOf(job, eml).Should().Be(Restricted);
        RecordOf(job, eml).OwnProvisional.Should().BeTrue("unreleased");
        var accessPermission = unsecured.GetProperty("accessPermission");
        accessPermission.GetProperty("releaseRefusedLinkColumnsNotSecured").GetInt32().Should().Be(1);
        accessPermission.GetProperty("undetermined").GetInt32().Should().Be(0, "a definite 'not secured' is a refusal");
        accessPermission.GetProperty("carriedRows").GetInt32().Should().Be(0, "a refusal is not carried");
        accessPermission.GetProperty("problems").EnumerateArray().Should().BeEmpty();

        job.LinkColumns.Secured[ChildAccessPermissionReconciler.ReleaseLinkColumns[1]] = true; // the script is applied
        job.World.Modified("sprk_document", eml, job.Now.UtcDateTime); // its own change lists it again
        await job.RunAsync();

        ValueOf(job, eml).Should().Be(Standard, "a 'not secured' answer is asked again, so no restart is needed");
    }

    [Fact]
    public async Task Round122Fix2_LinkColumnsNotSecured_OverMoreRowsThanTheJobCarries_DoNotHoldTheWindow()
    {
        // K2, must fire: before fix round 2 every linked provisional row was carried as undecidable on every run, and more than
        // MaxCarriedRows of them overflowed the carried set and froze the access-permission watermark.
        var job = new Harness(maxWritesPerRun: 5000);
        job.LinkColumns.Secured[ChildAccessPermissionReconciler.ReleaseLinkColumns[0]] = false;
        var (_, communication) = CommunicationUnder(job, Standard);
        var count = SecureChildReconciliationJob.MaxCarriedRows + 1;
        var emls = Enumerable.Range(0, count)
            .Select(_ => SavedUnidentified(job, ("sprk_relatedcommunication", "sprk_communication", communication)))
            .ToList();

        var first = (await job.RunAsync()).GetProperty("accessPermission");
        var second = (await job.RunAsync()).GetProperty("accessPermission");

        first.GetProperty("releaseRefusedLinkColumnsNotSecured").GetInt32().Should().Be(count);
        first.GetProperty("undetermined").GetInt32().Should().Be(0);
        first.GetProperty("carriedRows").GetInt32().Should().Be(0, "no overflow: nothing is carried");
        first.GetProperty("writeCapReached").GetBoolean().Should().BeFalse();
        second.GetProperty("since").GetDateTime().Should().BeAfter(first.GetProperty("since").GetDateTime(),
            "the watermark advances");
        emls.Should().OnlyContain(d => ValueOf(job, d) == Restricted, "nothing is released");
        emls.Should().OnlyContain(d => RecordOf(job, d).OwnProvisional, "each stays provisional");
    }

    [Fact]
    public async Task Round122Fix1_AFailedFieldSecurityRead_ReleasesNothing_AndTheNextRunReleases()
    {
        var job = new Harness();
        job.LinkColumns.Fails = read => read == 1;
        var (_, communication) = CommunicationUnder(job, Standard);
        var eml = SavedUnidentified(job);
        await job.RunAsync();

        Link(job, eml, communication);
        var failed = await job.RunAsync();

        ValueOf(job, eml).Should().Be(Restricted);
        var accessPermission = failed.GetProperty("accessPermission");
        accessPermission.GetProperty("problems").EnumerateArray().Select(p => p.GetString())
            .Should().ContainSingle(p => p!.Contains(eml.ToString("D")) && p.Contains("link_columns_security_unreadable"));
        // Fix round 2 (K2): a READ FAULT stays undecidable and carried (only a definite "not secured" is a refusal).
        accessPermission.GetProperty("undetermined").GetInt32().Should().Be(1);
        accessPermission.GetProperty("carriedRows").GetInt32().Should().Be(1, "a failed read is carried");
        accessPermission.GetProperty("releaseRefusedLinkColumnsNotSecured").GetInt32().Should().Be(0);

        await job.RunAsync();

        ValueOf(job, eml).Should().Be(Standard);
    }

    [Fact]
    public async Task Round122Fix1_SecuredLinkColumns_AreAskedOnce_AndRemembered()
    {
        // K1, must fire: both columns secured, so every release goes ahead; the answer is read once and kept for the process.
        var job = new Harness();
        var (_, communication) = CommunicationUnder(job, Standard);
        var first = SavedUnidentified(job);
        var second = SavedUnidentified(job);
        await job.RunAsync();
        job.LinkColumns.Asked.Should().BeEmpty("nothing was due for release, so nothing was asked");

        Link(job, first, communication);
        await job.RunAsync();
        Link(job, second, communication);
        await job.RunAsync();

        ValueOf(job, first).Should().Be(Standard);
        ValueOf(job, second).Should().Be(Standard);
        job.LinkColumns.Asked.Should().Equal(
            "sprk_document.sprk_relatedcommunication", "sprk_document.sprk_parentdocument");
    }

    /// <summary>
    /// Round 122 fix round 1 (K1): the attribute metadata (<c>EntityDefinitions(…)/Attributes</c>, <c>IsSecured</c>) the job
    /// reads for the two document link columns, through the access record's reader. No HTTP.
    /// </summary>
    private sealed class LinkColumnSecurity : DataverseWebApiClient
    {
        private static readonly Regex AttributesOf = new(@"^EntityDefinitions\(LogicalName='([^']+)'\)/Attributes$");
        private static readonly Regex LogicalNameIs = new(@"^LogicalName eq '([^']+)'$");

        public LinkColumnSecurity()
            : base(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            }).Build(), NullLogger<DataverseWebApiClient>.Instance, new NoCredential())
        {
        }

        /// <summary>IsSecured per <c>sprk_document</c> column; a column not listed has no definition.</summary>
        public Dictionary<string, bool> Secured { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_relatedcommunication"] = true,
            ["sprk_parentdocument"] = true,
        };

        /// <summary>Which reads (counted from 1) throw.</summary>
        public Func<int, bool>? Fails { get; set; }

        /// <summary>Every "table.column" asked about, in order.</summary>
        public List<string> Asked { get; } = new();

        public override Task<List<T>> QueryAsync<T>(
            string entitySetName, string? filter = null, string? select = null, int? top = null, int? skip = null,
            CancellationToken cancellationToken = default)
        {
            var table = AttributesOf.Match(entitySetName);
            var column = LogicalNameIs.Match(filter ?? string.Empty);
            if (!table.Success || !column.Success || select != "IsSecured")
                throw new InvalidOperationException($"Test: unexpected Web API query {entitySetName} {filter} {select}.");

            Asked.Add($"{table.Groups[1].Value}.{column.Groups[1].Value}");
            if (Fails?.Invoke(Asked.Count) == true)
                throw new HttpRequestException("Test: the attribute metadata could not be read.");

            var rows = table.Groups[1].Value == "sprk_document" && Secured.TryGetValue(column.Groups[1].Value, out var secured)
                ? new[] { new Dictionary<string, object?> { ["IsSecured"] = secured } }
                : Array.Empty<Dictionary<string, object?>>();
            return Task.FromResult(JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(rows))!);
        }

        private sealed class NoCredential : TokenCredential
        {
            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("Test: this double never authenticates.");

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("Test: this double never authenticates.");
        }
    }
}
