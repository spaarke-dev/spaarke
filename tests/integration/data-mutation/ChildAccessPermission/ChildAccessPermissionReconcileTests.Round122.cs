using FluentAssertions;
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
    public async Task Round122_AProvisionalChildInACycle_KeepsRestricted()
    {
        // Fail closed: the walk meets a document already on its chain before any link. The floor is still decidable here
        // (the cycle's first node is also filed under a matter), so only the walk keeps it.
        var job = new Harness();
        var matter = job.Root("sprk_matter", Standard);
        var a = SavedUnidentified(job, ("sprk_matter", "sprk_matter", matter));
        var b = SavedUnidentified(job, ("sprk_parentdocument", "sprk_document", a));
        job.World.Set("sprk_document", a, "sprk_parentdocument", new EntityReference("sprk_document", b));
        var child = SavedUnidentified(job, ("sprk_parentdocument", "sprk_document", a));

        await job.RunAsync();
        await job.RunAsync();

        ValueOf(job, child).Should().Be(Restricted);
        RecordOf(job, child).OwnProvisional.Should().BeTrue();
        job.World.AccessPermissionWrites.Should().NotContain(w => w.Id == child);
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
}
