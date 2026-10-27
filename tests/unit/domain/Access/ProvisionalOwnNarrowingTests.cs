using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Access;

/// <summary>
/// unified-access-control-r2, owner round 122 (2026-10-22): "the access should match the parent". An email-derived document
/// saved own-Restricted because its email could not be identified carries a PROVISIONAL marker
/// (<see cref="AccessInheritance.OwnProvisional"/>); once it is linked to a communication its own narrowing counts as no own
/// value and it follows the communication. The pure rule: the record, its version handling, and
/// <see cref="InheritedAccessPermission.DecideDocument"/> over <see cref="AccessInheritance.OwnPermissionOf(int, int?, AccessInheritance?, int, bool, out bool)"/>.
/// The job-level tests are in <c>ChildAccessPermissionReconcileTests.Round122</c>, the walk's in
/// <c>ExternalDocumentNarrowingTests</c>.
/// </summary>
public class ProvisionalOwnNarrowingTests
{
    private const int Standard = InheritedAccessPermission.Standard;
    private const int Limited = InheritedAccessPermission.Limited;
    private const int Restricted = InheritedAccessPermission.Restricted;

    private static readonly IReadOnlyCollection<(string Table, Guid Id)> Communication =
        new[] { ("sprk_communication", Guid.Parse("12212212-0122-4122-8122-000000000c0c")) };

    /// <summary>The record the save writes for an email that could not be identified, through the real save-time writer.</summary>
    private static string SaveTimeRecord()
    {
        var create = new CreateDocumentRequest { Name = "Unknown.eml", ContainerId = "b!c" };
        OfficeDocumentPersistence.ApplyEmailCommunicationOutcome(create, relatedCommunicationId: null).Should().BeTrue();
        create.AccessPermission.Should().Be(Restricted);
        return create.AccessInheritanceRecord!;
    }

    // ── The record ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheSaveTimeRestricted_IsMarkedProvisional_AndRoundTrips()
    {
        var (record, unreadable) = AccessInheritance.Parse(SaveTimeRecord());

        unreadable.Should().BeFalse();
        record!.OwnPermission.Should().Be(Restricted);
        record.OwnProvisional.Should().BeTrue("only the save-time Restricted of an unidentified email is provisional");
        AccessInheritance.Parse(record.Serialize()).Value!.OwnProvisional.Should().BeTrue();
    }

    [Fact]
    public void ARecordThatIsNotProvisional_SerializesExactlyAsBefore_WithoutTheField()
    {
        // Every other record's text is unchanged, so an older BFF reads it as it always did.
        AccessInheritance.OwnNarrowing(Restricted).Serialize().Should().Be(
            """{"v":1,"parents":[],"floorSecure":false,"floorPermission":100000000,"ownSecure":false,"ownPermission":100000002}""");
        AccessInheritance.OwnNarrowingUntilEmailIdentified(Restricted).Serialize().Should().Be(
            """{"v":1,"parents":[],"floorSecure":false,"floorPermission":100000000,"ownSecure":false,"ownPermission":100000002,"ownProvisional":true}""");
    }

    [Theory]
    [InlineData(Limited)]
    [InlineData(Standard)]
    public void TheFactory_OnlyMakesAProvisionalRestricted(int permission)
    {
        var act = () => AccessInheritance.OwnNarrowingUntilEmailIdentified(permission);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SameAs_SeesTheMarker()
    {
        var provisional = AccessInheritance.OwnNarrowingUntilEmailIdentified(Restricted);

        provisional.SameAs(AccessInheritance.OwnNarrowing(Restricted)).Should().BeFalse(
            "dropping the marker is a change the reconciler writes");
        provisional.SameAs(AccessInheritance.Parse(provisional.Serialize()).Value).Should().BeTrue();
    }

    [Fact]
    public void AWithOnAProvisionalRecord_DropsTheMarker_OnlyKeepingProvisionalWithKeepsIt()
    {
        // Fix round 1 (K3): the record's copy constructor does not copy the marker, so no `with` can carry it by accident.
        var provisional = AccessInheritance.OwnNarrowingUntilEmailIdentified(Restricted);

        var copied = provisional with { FloorPermission = Limited };
        var unchanged = provisional with { };

        copied.OwnProvisional.Should().BeFalse("a with drops the marker");
        copied.FloorPermission.Should().Be(Limited);
        copied.OwnPermission.Should().Be(Restricted, "every other fact is copied");
        unchanged.OwnProvisional.Should().BeFalse();
        unchanged.Parents.Should().BeSameAs(provisional.Parents);
        provisional.KeepingProvisionalWith(copied).OwnProvisional.Should().BeTrue("the one explicit way to keep it");
        provisional.KeepingProvisionalWith(copied).FloorPermission.Should().Be(Limited);
        AccessInheritance.OwnNarrowing(Restricted).KeepingProvisionalWith(copied).OwnProvisional.Should().BeFalse(
            "it keeps THIS record's marker, never sets one");
    }

    [Theory]
    [InlineData(Limited)]
    [InlineData(null)]
    public void KeepingProvisionalWith_ADifferentOwnValue_DropsTheMarker(int? ownValue)
    {
        // Fix round 2 (K3): the marker belongs to the save-time Restricted. Facts whose own value is anything else are not
        // that narrowing, so the record they become is never provisional, whoever calls it.
        var provisional = AccessInheritance.OwnNarrowingUntilEmailIdentified(Restricted);
        var facts = new AccessInheritance(
            new[] { "sprk_communication:12212212-0122-4122-8122-000000000c0c" }, false, Standard, false, ownValue);

        var kept = provisional.KeepingProvisionalWith(facts);

        kept.OwnProvisional.Should().BeFalse("its own value is not the provisional Restricted");
        kept.OwnPermission.Should().Be(ownValue, "every fact is still the new one");
        kept.Parents.Should().BeSameAs(facts.Parents);
        provisional.KeepingProvisionalWith(facts with { OwnPermission = Restricted }).OwnProvisional.Should().BeTrue(
            "the same own value keeps it");
    }

    // ── Versions: an older or unknown record fails closed ───────────────────────────────────────────────────

    [Fact]
    public void AnOlderRecordWithoutTheField_IsNotProvisional_SoALinkReleasesNothing()
    {
        const string older = """{"v":1,"parents":[],"floorSecure":false,"floorPermission":100000000,"ownSecure":false,"ownPermission":100000002}""";
        AccessInheritance.Parse(older).Value!.OwnProvisional.Should().BeFalse();

        var decision = InheritedAccessPermission.DecideDocument(Restricted, older, Communication, Standard, communicationLinked: true);

        decision.Target.Should().Be(Restricted, "a Restricted recorded before round 122 is a person's or unknown: it stays");
        decision.Own.Should().Be(Restricted);
        decision.WriteValue.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public void ARecordOfAnyOtherVersion_IsUnreadable_SoItsRestrictedIsNeverLowered(int version)
    {
        // The floor inside is Restricted, so reading the record as "no own value" would widen the document to Standard.
        var text = """{"v":VERSION,"parents":["sprk_communication:12212212-0122-4122-8122-000000000c0c"],"floorSecure":false,"floorPermission":100000002,"ownSecure":false,"ownPermission":100000002,"ownProvisional":true}"""
            .Replace("VERSION", version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AccessInheritance.Parse(text).Unreadable.Should().BeTrue();

        var decision = InheritedAccessPermission.DecideDocument(Restricted, text, Communication, Standard, communicationLinked: true);

        decision.RaiseOnly.Should().BeTrue("an unreadable record is only ever raised, never lowered");
        decision.Target.Should().Be(Restricted);
        decision.WriteValue.Should().BeFalse();
        decision.Record.Should().BeNull("the record is left for a person to look at");
        decision.Undetermined.Should().NotBeNull();
    }

    [Theory]
    [InlineData("\"yes\"")]
    [InlineData("1")]
    [InlineData("null")]
    public void AMarkerThatIsNotABoolean_IsUnreadable_SoTheRestrictedStays(string marker)
    {
        var text = $$"""{"v":1,"parents":[],"floorSecure":false,"floorPermission":100000000,"ownSecure":false,"ownPermission":100000002,"ownProvisional":{{marker}}}""";
        AccessInheritance.Parse(text).Unreadable.Should().BeTrue();

        var decision = InheritedAccessPermission.DecideDocument(Restricted, text, Communication, Standard, communicationLinked: true);

        decision.Target.Should().Be(Restricted);
        decision.WriteValue.Should().BeFalse();
    }

    /// <summary>
    /// The BFF that predates round 122, modelled by its own stored shape and serializer options (unchanged since task 175): it
    /// skips the unknown <c>ownProvisional</c> and reads the save-time record as an ordinary own Restricted. So during a rolling
    /// deploy, or after a rollback, the document stays Restricted: it fails closed.
    /// </summary>
    [Fact]
    public void ABffThatPredatesTheMarker_ReadsTheProvisionalRecordAsItsOwnRestricted()
    {
        var older = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        var read = JsonSerializer.Deserialize<StoredBeforeRound122>(SaveTimeRecord(), older);

        read!.V.Should().Be(1, "the version is not bumped, so the older parser accepts the record");
        read.OwnPermission.Should().Be(Restricted, "it keeps the document Restricted");
        read.Parents.Should().BeEmpty();
    }

    private sealed record StoredBeforeRound122(
        int V, IReadOnlyList<string>? Parents, bool FloorSecure, int FloorPermission, bool OwnSecure, int? OwnPermission);

    // ── The release rule (DecideDocument over OwnPermissionOf) ──────────────────────────────────────────────

    [Theory]
    [InlineData(Standard)]
    [InlineData(Limited)]
    [InlineData(Restricted)]
    public void AProvisionalRestricted_ThatIsLinked_FollowsWhatItsParentsGive_AndItsRecordDropsTheMarker(int inherited)
    {
        var decision = InheritedAccessPermission.DecideDocument(
            Restricted, SaveTimeRecord(), Communication, inherited, communicationLinked: true);

        decision.Target.Should().Be(inherited, "released: it follows its communication's tops");
        decision.WriteValue.Should().Be(inherited != Restricted);
        decision.Own.Should().BeNull();
        decision.OwnKept.Should().BeFalse();
        decision.Record.Should().NotBeNull("the release is persisted");
        decision.Record!.OwnPermission.Should().BeNull();
        decision.Record.OwnProvisional.Should().BeFalse("the release is one way");
        decision.Record.FloorPermission.Should().Be(inherited);
    }

    [Fact]
    public void AReleasedRow_IsNotNarrowedAgain_ByTheSameLinkOnTheNextRun()
    {
        var released = InheritedAccessPermission.DecideDocument(
            Restricted, SaveTimeRecord(), Communication, Standard, communicationLinked: true);

        var next = InheritedAccessPermission.DecideDocument(
            released.Target, released.Record!.Serialize(), Communication, Standard, communicationLinked: true);

        next.Target.Should().Be(Standard);
        next.WriteValue.Should().BeFalse();
        next.Record.Should().BeNull("converged");
    }

    [Fact]
    public void AProvisionalRestricted_WithNoLink_StaysRestricted_AndStaysProvisional()
    {
        var decision = InheritedAccessPermission.DecideDocument(
            Restricted, SaveTimeRecord(), Communication, Standard, communicationLinked: false);

        decision.Target.Should().Be(Restricted);
        decision.WriteValue.Should().BeFalse();
        decision.Own.Should().Be(Restricted);
        decision.OwnKept.Should().BeTrue();
        decision.Record!.OwnProvisional.Should().BeTrue("unreleased and untouched, so a later link still releases it");
        decision.Record.FloorPermission.Should().Be(Standard);
        decision.Record.Parents.Should().ContainSingle();
    }

    [Fact]
    public void AHandSetRestricted_OnALinkedDocument_Stays_EvenWhenTheWalkFindsALink()
    {
        // Must not fire (round 87): a Restricted a person set is the document's own; a link never releases it.
        var handSet = AccessInheritance.OwnNarrowing(Restricted).Serialize();

        var decision = InheritedAccessPermission.DecideDocument(
            Restricted, handSet, Communication, Standard, communicationLinked: true);

        decision.Target.Should().Be(Restricted);
        decision.Own.Should().Be(Restricted);
        decision.WriteValue.Should().BeFalse();
    }

    [Theory]
    [InlineData(Standard, null)]
    [InlineData(Limited, Limited)]
    public void AProvisionalRestricted_EditedByHand_DropsTheMarker(int edited, int? expectedOwn)
    {
        // The record says {own R, provisional, floor Standard}; a person changed the value since.
        var afterFirstRun = InheritedAccessPermission.DecideDocument(
            Restricted, SaveTimeRecord(), Communication, Standard, communicationLinked: false).Record!;

        var decision = InheritedAccessPermission.DecideDocument(
            edited, afterFirstRun.Serialize(), Communication, Standard, communicationLinked: true);

        decision.Own.Should().Be(expectedOwn);
        decision.Target.Should().Be(edited);
        decision.Record!.OwnProvisional.Should().BeFalse("any other change of the own value drops the marker");
    }

    [Fact]
    public void AProvisionalRestricted_ReSetByHand_StaysRestricted_WhenItIsLinkedLater()
    {
        var record = InheritedAccessPermission.DecideDocument(
            Restricted, SaveTimeRecord(), Communication, Standard, communicationLinked: false).Record!;
        var lowered = InheritedAccessPermission.DecideDocument(
            Standard, record.Serialize(), Communication, Standard, communicationLinked: false);
        var reSet = InheritedAccessPermission.DecideDocument(
            Restricted, lowered.Record!.Serialize(), Communication, Standard, communicationLinked: false);
        reSet.Record!.OwnProvisional.Should().BeFalse();

        var linked = InheritedAccessPermission.DecideDocument(
            Restricted, reSet.Record.Serialize(), Communication, Standard, communicationLinked: true);

        linked.Target.Should().Be(Restricted, "the person's Restricted stays");
        linked.Own.Should().Be(Restricted);
    }

    [Fact]
    public void AParentlessProvisionalRestricted_KeepsItsMarker_SoALaterLinkStillReleasesIt()
    {
        var parentless = InheritedAccessPermission.DecideDocument(Restricted, SaveTimeRecord(), [], inherited: null);

        parentless.Target.Should().Be(Restricted);
        parentless.Record.Should().BeNull("the save-time record already says it, marker included");

        var edited = InheritedAccessPermission.DecideDocument(Limited, SaveTimeRecord(), [], inherited: null);
        edited.Record!.OwnPermission.Should().Be(Limited);
        edited.Record.OwnProvisional.Should().BeFalse("a parentless value a person changed is theirs");
    }

    [Fact]
    public void TheWorkAssignmentAndProjectRule_NeverReleases()
    {
        // The four-argument overload (task 175's Decide) passes no link.
        var record = AccessInheritance.OwnNarrowingUntilEmailIdentified(Restricted);

        AccessInheritance.OwnPermissionOf(2, Restricted, record, 0).Should().Be(Restricted);
    }
}
