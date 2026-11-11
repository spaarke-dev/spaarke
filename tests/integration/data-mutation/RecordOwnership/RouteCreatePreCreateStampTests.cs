using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 017, Phase 2d-1 (owner decision 2a): POST /api/v1/child-records/{table} through the
/// REAL route and the REAL <see cref="RecordCreationService"/>, whose step 10 now derives the I-1 stamp before the create. The
/// two new refusals pass the route's edge (<see cref="ChildRecordEndpoints.RefusalResult"/>) unchanged, through the shared map:
/// 400 <c>record_creation.stamp_underivable</c> with the service's words, and 503 <c>record_creation.stamp_unavailable</c> with
/// the map's fixed words. Nothing is created on either. Every existing route row (017b's NFR-07 table) is pinned elsewhere and
/// unchanged.
/// </summary>
/// <remarks>A partial of <see cref="SecureChildOwnershipAiToolTests"/> (its route harness), as RouteDecisionCreateParityTests is.</remarks>
public sealed partial class SecureChildOwnershipAiToolTests
{
    /// <summary>The route's create over a stamp world this test controls (the harness's own is empty).</summary>
    private Task<IResult> CreateChildOverStamps(string table, Dictionary<string, object?> payload, StampWorld stamps) =>
        ChildRecordEndpoints.CreateAsync(
            table, JsonSerializer.SerializeToElement(payload), _user,
            RouteRecordCreation.Over(
                _world.Resolver(), _appOnly.Object, stamps.Restamper, Shares(), SecureRootFilingGateFixtures.NothingSecure(),
                null, _routeMembership.Object),
            HttpContextOfCaller(), NullLogger<Program>.Instance, CancellationToken.None);

    private Dictionary<string, object?> AnEventUnderTheEvent() => new()
    {
        ["sprk_eventname"] = "Follow-up",
        ["sprk_RegardingEvent@odata.bind"] = $"/sprk_events({Event:D})",
    };

    private void TheEventIsFiledUnderTheOrdinaryMatter()
    {
        _world.WithRecord("sprk_event", Event, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        ShareWorld.OrdinaryChild("sprk_event", Event, ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));
    }

    [Fact]
    public async Task RouteCreate_AnEventUnderAnEvent_CarriesItsMattersStampInTheCreatePayload()
    {
        TheEventIsFiledUnderTheOrdinaryMatter();
        var stamps = new StampWorld().Row("sprk_matter", OrdinaryMatter)
            .Row("sprk_event", Event, [("sprk_regardingmatter", "sprk_matter", OrdinaryMatter)]);

        var result = await CreateChildOverStamps("sprk_event", AnEventUnderTheEvent(), stamps);

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        _appCreates.Should().ContainSingle().Which.Fields["sprk_RegardingMatter@odata.bind"]
            .Should().Be($"/sprk_matters({OrdinaryMatter:D})", "step 10 derives the stamp into the one create");
    }

    // =====================================================================================================================
    // field-mapping-server-write-path-r1 task 042a characterized the #2483 / DI-009 window (design.md section 15.1, 15.6);
    // task 042b derives the stamp before step 7 (step 6½) and INVERTS those tests (A1, A2 below), with its must-fire and
    // must-not-fire tests (design.md section 15.6).
    // =====================================================================================================================

    /// <summary>
    /// The team-owned Event a new event is filed under, itself filed under the SECURE matter but still owned by an ordinary
    /// team: the Make Secure pass has not reached it yet, or ended incomplete. The stamp world derives its root, the Secure
    /// matter, for the I-1 stamp.
    /// </summary>
    private StampWorld TheTeamOwnedEventIsFiledUnderTheSecureMatter()
    {
        _world.WithRecord("sprk_event", Event, Directory.ChildBu, owningTeam: Directory.ChildTeam,
            extra: new() { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureMatter) });
        ShareWorld.OrdinaryChild("sprk_event", Event, ("sprk_regardingmatter", "sprk_matter", SecureMatter));
        return new StampWorld().Row("sprk_matter", SecureMatter)
            .Row("sprk_event", Event, [("sprk_regardingmatter", "sprk_matter", SecureMatter)]);
    }

    /// <summary>The row as Dataverse stores it after the create: every lookup bind in the create body as the column it sets.</summary>
    private static Entity TheWrittenRow(string table, Guid id, IReadOnlyDictionary<string, object?> body)
    {
        var row = new Entity(table, id);
        foreach (var (key, value) in body)
        {
            if (!key.EndsWith("@odata.bind", StringComparison.Ordinal) || key == "ownerid@odata.bind")
                continue;
            var path = value is JsonElement { ValueKind: JsonValueKind.String } element ? element.GetString() : value as string;
            if (path is null)
                continue;
            var set = path.TrimStart('/')[..path.TrimStart('/').IndexOf('(')];
            var target = Guid.Parse(path[(path.IndexOf('(') + 1)..path.IndexOf(')')]);
            if (set == "systemusers")
                continue;
            row[key[..^"@odata.bind".Length].ToLowerInvariant()] = new EntityReference(set.TrimEnd('s'), target);
        }

        return row;
    }

    /// <summary>
    /// Task 042b (FR-41, #2483 / DI-009 closed) INVERTS task 042a's A1, formerly
    /// <c>RouteCreate_AnEventUnderATeamOwnedEventOfASecureMatter_IsOwnedByTheEventsOrdinaryTeam_DI009Window</c>, which pinned
    /// the window: the owner was the event's ordinary business-unit team because step 7 decided over the item's own lookups and
    /// step 10 derived the stamp into the create BODY only. Step 6½ now derives the stamp BEFORE the owner and step 7 asks over
    /// the item's lookups plus the derived root, so the create is owned by <see cref="Directory.SecureNamedTeam"/> and the
    /// Secure matter's sharee mirror is written at create. The caller (not a sharee of the Secure matter, AC7) gets the normal
    /// 201, whose body names only the new row; the intermediate is read once (no second derivation, AC1); and step 5's AppendTo
    /// probes name only the record the caller sent: the derived root is never asked as the caller (AC12). Step 8 is NoOp since
    /// task 041, so there is no index assertion (AC13).
    /// </summary>
    [Fact]
    public async Task RouteCreate_AnEventUnderATeamOwnedEventOfASecureMatter_IsOwnedByTheSecureTeam_WithTheShareeMirrorAtCreate()
    {
        var stamps = TheTeamOwnedEventIsFiledUnderTheSecureMatter();
        var intermediateReadsBeforeTheCreate = -1;
        _onAppCreate = (_, _, _) => intermediateReadsBeforeTheCreate = IntermediateReads(stamps);
        var probes = AppendToProbes();

        var result = await CreateChildOverStamps("sprk_event", AnEventUnderTheEvent(), stamps);

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        var (_, id, fields) = _appCreates.Should().ContainSingle().Subject;
        fields["ownerid@odata.bind"].Should().Be($"/teams({Directory.SecureNamedTeam:D})",
            "#2483 closed: step 7 asks over the event and the derived Secure matter, as the reconciler does");
        fields["sprk_RegardingMatter@odata.bind"].Should().Be($"/sprk_matters({SecureMatter:D})",
            "step 10 binds the stamp step 6½ derived");
        _shareTable.MaskOf("sprk_event", id, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask,
            "the Secure matter's sharee sees the new event at create, not 2-4 minutes later");

        intermediateReadsBeforeTheCreate.Should().Be(1, "step 6½ derives once; step 10 binds that result with no second derivation (AC1)");

        probes.Should().NotBeEmpty("step 5 asks AppendTo on the record the caller sent");
        probes.Should().OnlyContain(p => p.Contains(Event.ToString("D"), StringComparison.OrdinalIgnoreCase),
            "step 5's AppendTo set is unchanged: only the event the caller filed under");
        probes.Should().NotContain(p => p.Contains(SecureMatter.ToString("D"), StringComparison.OrdinalIgnoreCase),
            "the derived root is never asked as the caller (AC12)");

        // AC7: the caller is not a sharee of the Secure matter; the 201 body names only the new row.
        var body = JsonSerializer.SerializeToElement(((IValueHttpResult)result).Value);
        body.EnumerateObject().Select(p => p.Name).Should().Equal(["id"], "no warnings, nothing else");
        body.GetProperty("id").GetGuid().Should().Be(id);
        var wire = body.GetRawText() + (result.GetType().GetProperty("Location")?.GetValue(result) as string);
        foreach (var secure in new[] { SecureMatter, Directory.SecureNamedTeam, Directory.SecureBu, Directory.SecureDefaultTeam })
            wire.Should().NotContainEquivalentOf(secure.ToString("D"), "the response carries no secure-family id the caller did not send");
    }

    /// <summary>
    /// Task 042b INVERTS task 042a's A2, formerly
    /// <c>RouteCreate_TheReconcilersQuestionOverTheWrittenEvent_OwnsItToTheSecureTeam_WhereTheCreateDidNot_DI009Window</c>, which
    /// pinned the disagreement: the reconciler's question over the written row owned it to the Secure team, the create did not.
    /// Now they agree: the create asks the question the reconciler asks of the written row.
    /// </summary>
    [Fact]
    public async Task RouteCreate_TheReconcilersQuestionOverTheWrittenEvent_AgreesWithTheCreatesOwner()
    {
        var stamps = TheTeamOwnedEventIsFiledUnderTheSecureMatter();
        await CreateChildOverStamps("sprk_event", AnEventUnderTheEvent(), stamps);
        var (_, id, fields) = _appCreates.Should().ContainSingle().Subject;
        var written = TheWrittenRow("sprk_event", id, fields);
        var reads = new ExistingRowReads(Mock.Of<IGenericEntityService>(), null, null);

        var question = await RecordOwnershipContext.ForExistingRowAsync(written, reads, CancellationToken.None);
        var answer = await _world.Resolver().ResolveOwnerAsync(question.Context, CancellationToken.None);

        question.Undetermined.Should().BeNull();
        answer.OwningTeamId.Should().Be(Directory.SecureNamedTeam, "the reconciler's answer for the written row");
        answer.IsSecureOwner.Should().BeTrue();
        Owner(fields).Should().Be(answer.OwningTeamId!.Value, "the create and the reconciler agree (#2483 closed)");
    }

    // ── 042b fix round 1 (F1, owner decision A' 2026-11-03): a stamp the caller SENT that disagrees with the root ────────
    // Step 10's bind stays fill-only, so the caller's stamp is created as sent and step 12's restamp replaces it with the
    // intermediate's root. Step 7 therefore asks over the stamp as the row ENDS (the planned value, not the sent one).

    /// <summary>
    /// The restamp's world holds the created row as the create stored it, and every stamp PATCH it gets lands on the share
    /// world's copy too (in Dataverse they are the same row), so step 12's sharee mirror reads the row as the restamp left it.
    /// </summary>
    private void TheCreatedRowIsRestampedInTheShareWorldToo(StampWorld stamps)
    {
        _onAppCreate = (table, id, fields) => stamps.Row(table, id, TheWrittenRow(table, id, fields).Attributes
            .Select(a => (a.Key, ((EntityReference)a.Value).LogicalName, ((EntityReference)a.Value).Id))
            .ToArray());
        stamps.AfterUpdate = (table, id, fields) =>
        {
            var stampsOnly = fields
                .Where(f => CoreAncestorResolver.CoreAncestorLookups.Any(l =>
                    string.Equals(l.LookupAttribute, f.Key, StringComparison.OrdinalIgnoreCase)))
                .ToDictionary(f => f.Key, f => f.Value);
            if (stampsOnly.Count > 0)
                ShareWorld.ApplyStampWrite(table, id, stampsOnly);
        };
    }

    /// <summary>
    /// F1 (a): the caller sends an ORDINARY matter as the event's stamp, under the team-owned event of the Secure matter. The
    /// restamp will replace it with the Secure matter, so the owner is <see cref="Directory.SecureNamedTeam"/> and the Secure
    /// matter's sharee mirror is written at create (before the fix: the event's ordinary team, the #2483 state). The bind is
    /// fill-only (the caller's value is created as sent); step 5 asks AppendTo on what the caller sent and never on the
    /// derived root (AC12, amended).
    /// </summary>
    [Fact]
    public async Task RouteCreate_ASentOrdinaryStampUnderATeamOwnedEventOfASecureMatter_IsOwnedByTheSecureTeam_WithTheShareeMirrorAtCreate()
    {
        var stamps = TheTeamOwnedEventIsFiledUnderTheSecureMatter();
        TheCreatedRowIsRestampedInTheShareWorldToo(stamps);
        var probes = AppendToProbes();
        var payload = AnEventUnderTheEvent();
        payload["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})";

        var result = await CreateChildOverStamps("sprk_event", payload, stamps);

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        var (_, id, fields) = _appCreates.Should().ContainSingle().Subject;
        Owner(fields).Should().Be(Directory.SecureNamedTeam,
            "step 7 asks over the stamp the row ends with (the Secure matter), not the one the caller sent (042b F1)");
        Bind(fields, "sprk_RegardingMatter@odata.bind").Should().Be($"/sprk_matters({OrdinaryMatter:D})",
            "step 10's bind is fill-only: the caller's value is created as sent");
        stamps.RowOf("sprk_event", id).GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(SecureMatter,
            "step 12's restamp replaces the disagreeing stamp with the intermediate's root");
        _shareTable.MaskOf("sprk_event", id, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask,
            "the Secure matter's sharee sees the new event at create");
        probes.Should().Contain(p => p.Contains(OrdinaryMatter.ToString("D"), StringComparison.OrdinalIgnoreCase),
            "a stamp the caller sent is a record the caller names: step 5 asks AppendTo on it, as before");
        probes.Should().NotContain(p => p.Contains(SecureMatter.ToString("D"), StringComparison.OrdinalIgnoreCase),
            "the derived root is never asked as the caller (AC12)");
    }

    /// <summary>
    /// F1 (b): the caller sends the SECURE matter as the stamp of an event under an event whose root is the ORDINARY matter.
    /// The restamp replaces it with the ordinary matter, so the owner is the ordinary owner (the event's team), never the
    /// Secure team, and no sharee of the Secure matter is mirrored onto the row.
    /// </summary>
    [Fact]
    public async Task RouteCreate_ASentSecureMatterStampUnderAnEventOfAnOrdinaryMatter_IsOwnedByTheOrdinaryTeam_NeverTheSecureTeam()
    {
        TheEventIsFiledUnderTheOrdinaryMatter();
        var stamps = new StampWorld().Row("sprk_matter", OrdinaryMatter)
            .Row("sprk_event", Event, [("sprk_regardingmatter", "sprk_matter", OrdinaryMatter)]);
        TheCreatedRowIsRestampedInTheShareWorldToo(stamps);
        var payload = AnEventUnderTheEvent();
        payload["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({SecureMatter:D})";

        var result = await CreateChildOverStamps("sprk_event", payload, stamps);

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        var (_, id, fields) = _appCreates.Should().ContainSingle().Subject;
        Owner(fields).Should().Be(Directory.ChildTeam, "the row ends under the ordinary matter: the event's team, record-first");
        Owner(fields).Should().NotBe(Directory.SecureNamedTeam, "never the Secure team of a matter the row will not be filed under");
        stamps.RowOf("sprk_event", id).GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(OrdinaryMatter,
            "step 12's restamp replaces the sent Secure matter with the intermediate's root");
        _shareTable.MaskOf("sprk_event", id, DataversePrincipalRef.User(Sharee)).Should().BeNull(
            "no sharee of the Secure matter is mirrored onto a row filed under the ordinary matter");
    }

    // ── 042b row 5 (UAC-r2 conditions (a) and (b)): the derived root is flagged secure but not isolated ──────────────────

    private static readonly Guid FlaggedMatter = Guid.Parse("a2470000-0000-4000-8000-0000000004b5");
    private static readonly Guid PreCallOwner = Guid.Parse("a2470000-0000-4000-8000-0000000004b6");

    /// <summary>The event, team-owned by <see cref="Directory.ChildTeam"/>, filed under <see cref="FlaggedMatter"/>.</summary>
    private StampWorld TheTeamOwnedEventIsFiledUnderTheFlaggedMatter()
    {
        _world.WithRecord("sprk_event", Event, Directory.ChildBu, owningTeam: Directory.ChildTeam,
            extra: new() { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", FlaggedMatter) });
        ShareWorld.OrdinaryChild("sprk_event", Event, ("sprk_regardingmatter", "sprk_matter", FlaggedMatter));
        return new StampWorld().Row("sprk_matter", FlaggedMatter)
            .Row("sprk_event", Event, [("sprk_regardingmatter", "sprk_matter", FlaggedMatter)]);
    }

    /// <summary>
    /// The refusal a DIRECT child of <see cref="FlaggedMatter"/> already gets (the existing C11 refusal), and the refusal the
    /// event under the team-owned event gets: both 409 <c>SecureParentNotIsolated</c> with the same words, and nothing created.
    /// </summary>
    private async Task TheEventUnderTheEventIsRefusedAsADirectChildIs(StampWorld stamps)
    {
        var direct = await CreateChildOverStamps("sprk_event", new()
        {
            ["sprk_eventname"] = "Follow-up",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({FlaggedMatter:D})",
        }, stamps);
        var underTheEvent = await CreateChildOverStamps("sprk_event", AnEventUnderTheEvent(), stamps);

        Status(direct).Should().Be(StatusCodes.Status409Conflict, Detail(direct));
        ReasonCode(direct).Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        Detail(direct).Should().Contain("re-run Make Secure on it, then retry");
        Status(underTheEvent).Should().Be(Status(direct), Detail(underTheEvent));
        ReasonCode(underTheEvent).Should().Be(ReasonCode(direct));
        Detail(underTheEvent).Should().Be(Detail(direct), "the same code, status and words as the direct child (UAC-r2 condition (b))");
        _appCreates.Should().BeEmpty("fail closed: nothing is created");
        _user.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task RouteCreate_UnderATeamOwnedEventOfAMatterInTheTransientMakeSecureInterval_IsRefused409_AsADirectChildIs()
    {
        // Make Secure step 4.1 has set sprk_issecure; step 5 has not yet moved the matter to the Secure team.
        var stamps = TheTeamOwnedEventIsFiledUnderTheFlaggedMatter();
        _world.WithRecord("sprk_matter", FlaggedMatter, Directory.ChildBu, isSecure: true, owningTeam: Directory.ChildTeam);

        await TheEventUnderTheEventIsRefusedAsADirectChildIs(stamps);
    }

    [Fact]
    public async Task RouteCreate_UnderATeamOwnedEventOfAMatterLeftFlaggedByACompensatedMakeSecure_IsRefused409_AsADirectChildIs()
    {
        // The lasting compensated state: the flag stays set and the owner was moved back to the pre-call owner (a user).
        var stamps = TheTeamOwnedEventIsFiledUnderTheFlaggedMatter();
        _world.WithRecord("sprk_matter", FlaggedMatter, Directory.ChildBu, isSecure: true, extra: new()
        {
            ["owninguser"] = new EntityReference("systemuser", PreCallOwner),
            ["ownerid"] = new EntityReference("systemuser", PreCallOwner),
        });

        await TheEventUnderTheEventIsRefusedAsADirectChildIs(stamps);
    }

    // ── 042b row 2 (AC6): a root read fault with an owner world that refuses ────────────────────────────────────────────

    [Fact]
    public async Task RouteCreate_WhenTheRootReadFaults_AndTheOwnerWouldBeRefused_Is503StampUnavailable()
    {
        // The owner world does not hold the event (step 7 would refuse 409 ParentUnresolved); the stamp world's read of it faults.
        ShareWorld.OrdinaryChild("sprk_event", Event, ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));
        var stamps = new StampWorld().Row("sprk_event", Event, [("sprk_regardingmatter", "sprk_matter", OrdinaryMatter)])
            .FailRead("sprk_event", Event);

        var result = await CreateChildOverStamps("sprk_event", AnEventUnderTheEvent(), stamps);

        Status(result).Should().Be(StatusCodes.Status503ServiceUnavailable, Detail(result));
        ReasonCode(result).Should().Be("record_creation.stamp_unavailable", "step 6½ runs before the owner (design 15.4 row 2)");
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    // ── 042b change 1 (AC3): a to-do under a team-owned report card in X whose matter is in Y ──────────────────────────────

    private static readonly Guid ReportCard = Guid.Parse("a2470000-0000-4000-8000-0000000004c1");
    private static readonly Guid MatterInY = Guid.Parse("a2470000-0000-4000-8000-0000000004c2");

    private StampWorld TheReportCardInXIsFiledUnderTheMatterInY()
    {
        _world.WithRecord("sprk_matter", MatterInY, Directory.CustomerChildBu, isSecure: false, owningTeam: Directory.CustomerChildTeam)
            .WithRecord("sprk_reportcard", ReportCard, Directory.ChildBu, owningTeam: Directory.ChildTeam,
                extra: new() { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterInY) });
        return new StampWorld().Row("sprk_matter", MatterInY)
            .Row("sprk_reportcard", ReportCard, [("sprk_regardingmatter", "sprk_matter", MatterInY)]);
    }

    [Fact]
    public async Task RouteCreate_AToDoWithNoPairUnderATeamOwnedReportCardInX_WhoseMatterIsInY_IsOwnedByYsTeam()
    {
        var stamps = TheReportCardInXIsFiledUnderTheMatterInY();

        var result = await CreateChildOverStamps("sprk_todo", new()
        {
            ["sprk_name"] = "Score the panel",
            ["sprk_RegardingReportCard@odata.bind"] = $"/sprk_reportcards({ReportCard:D})",
        }, stamps);

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        var fields = _appCreates.Should().ContainSingle().Subject.Fields;
        Owner(fields).Should().Be(Directory.CustomerChildTeam,
            "change 1 (owner 2026-11-02): the derived matter ranks above the report card in the to-do's lineage order");
        fields["sprk_RegardingMatter@odata.bind"].Should().Be($"/sprk_matters({MatterInY:D})");
    }

    /// <summary>
    /// AC3's second half (design 15.6 "the same report card with the to-do's pair set is owned by X") does NOT hold on this
    /// code, and this test pins why, as parity: a pair is the to-do's SUBJECT only when it names one of the server regarding
    /// builder's targets (<c>TodoRegardingBuilder.RegardingLookupByEntity</c>, 11 tables), and <c>sprk_reportcard</c> is not
    /// one of them (the client builder, TodoRegardingUpdateBuilder.ts, has it as its 12th). So the pair names no subject, the
    /// lineage order decides, and the derived matter (Y) ranks first, for the create and for the reconciler alike. Task 042b
    /// notes: open question and a proposed DI entry (found in passing); the map is outside OwnerQuestionOf / ForRow.
    /// </summary>
    [Fact]
    public async Task RouteCreate_AToDoWithItsPairNamingATeamOwnedReportCardInX_IsOwnedAsTheReconcilerOwnsTheWrittenRow()
    {
        var stamps = TheReportCardInXIsFiledUnderTheMatterInY();

        var result = await CreateChildOverStamps("sprk_todo", new()
        {
            ["sprk_name"] = "Score the panel",
            ["sprk_RegardingReportCard@odata.bind"] = $"/sprk_reportcards({ReportCard:D})",
            ["sprk_regardingrecordid"] = ReportCard.ToString("D"),
        }, stamps);

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        var (_, id, fields) = _appCreates.Should().ContainSingle().Subject;
        var written = TheWrittenRow("sprk_todo", id, fields);
        written["sprk_regardingrecordid"] = ReportCard.ToString("D");
        var question = await RecordOwnershipContext.ForExistingRowAsync(
            written, new ExistingRowReads(Mock.Of<IGenericEntityService>(), null, null), CancellationToken.None);
        var reconciler = await _world.Resolver().ResolveOwnerAsync(question.Context, CancellationToken.None);

        RecordOwnershipContext.SubjectColumnOf(written).Should().BeNull("a report card is not a to-do regarding-builder target");
        Owner(fields).Should().Be(reconciler.OwningTeamId!.Value, "the create asks the reconciler's question");
        Owner(fields).Should().Be(Directory.CustomerChildTeam, "Y: the pair names no subject, so the lineage order decides");
    }

    // ── 042b must not fire (AC12): a browser-sent stamp ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RouteCreate_ABrowserSentStamp_DerivesNothing_AndIsAskedAsTheCallerAsBefore()
    {
        var stamps = TheTeamOwnedEventIsFiledUnderTheSecureMatter();
        var probes = AppendToProbes();
        var intermediateReadsBeforeTheCreate = -1;
        _onAppCreate = (_, _, _) => intermediateReadsBeforeTheCreate = IntermediateReads(stamps);
        var payload = AnEventUnderTheEvent();
        payload["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({SecureMatter:D})";

        var result = await CreateChildOverStamps("sprk_event", payload, stamps);

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        var (_, _, fields) = _appCreates.Should().ContainSingle().Subject;
        Owner(fields).Should().Be(Directory.SecureNamedTeam, "the stamp the caller sent was already in the owner question");

        // Fix round 1 (AC1 and AC12 amended): step 6½ plans even when a stamp is sent (one read of the intermediate), and an
        // AGREEING stamp plans nothing, so step 7's question is the item's own lookups, unchanged.
        intermediateReadsBeforeTheCreate.Should().Be(1, "step 6½ plans once, a stamp sent or not (AC1 amended)");
        var merged = new Entity("sprk_event")
        {
            ["sprk_regardingevent"] = new EntityReference("sprk_event", Event),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureMatter),
        };
        (await stamps.Restamper.PlanStampOfRowAsync("sprk_event", merged, CancellationToken.None)).Should().BeNull(
            "a sent stamp that agrees with the intermediate's root changes nothing");
        var lookups = new[]
        {
            KeyValuePair.Create("sprk_regardingevent", (RecordOwnershipParent?)new RecordOwnershipParent("sprk_event", Event)),
            KeyValuePair.Create("sprk_regardingmatter", (RecordOwnershipParent?)new RecordOwnershipParent("sprk_matter", SecureMatter)),
        };
        RecordCreationService.AsTheRowEnds(lookups, null).Should().Equal(lookups, "no plan: the question is the item's lookups");
        Bind(fields, "sprk_RegardingMatter@odata.bind").Should().Be($"/sprk_matters({SecureMatter:D})", "the caller's own bind");
        probes.Should().Contain(p => p.Contains(SecureMatter.ToString("D"), StringComparison.OrdinalIgnoreCase),
            "a stamp the caller SENT is a record the caller names, so step 5 asks AppendTo on it, as before");
        fields["sprk_RegardingMatter@odata.bind"].Should().BeOfType<JsonElement>(
            "the caller's own mapped value, not a server-derived bind: fill-only, nothing is derived over a stamp the item holds");
    }

    private static readonly Guid RootProject = Guid.Parse("a2470000-0000-4000-8000-0000000004d1");
    private static readonly Guid OtherProject = Guid.Parse("a2470000-0000-4000-8000-0000000004d2");

    /// <summary>
    /// Task 042b fix round 2 (item 3): a NON-null plan where the sent row agrees on one stamp column and the plan names another.
    /// The intermediate event's root is the Secure matter AND a project; the item sends the Secure matter (agrees) and either
    /// another project (disagrees: replaced) or no project (missing: added). The owner question keeps the agreeing column as
    /// sent, with its value, and takes the planned value for the other; every non-stamp lookup is unchanged.
    /// </summary>
    [Theory]
    [InlineData(true)]  // the item sends a project that disagrees
    [InlineData(false)] // the item sends no project
    public async Task TheOwnerQuestion_KeepsAnAgreeingStampColumn_AndTakesThePlannedValueForAnother(bool sendsAnotherProject)
    {
        var stamps = new StampWorld().Row("sprk_matter", SecureMatter).Row("sprk_project", RootProject)
            .Row("sprk_event", Event,
                [("sprk_regardingmatter", "sprk_matter", SecureMatter), ("sprk_regardingproject", "sprk_project", RootProject)]);
        var lookups = new List<KeyValuePair<string, RecordOwnershipParent?>>
        {
            KeyValuePair.Create("sprk_regardingevent", (RecordOwnershipParent?)new RecordOwnershipParent("sprk_event", Event)),
            KeyValuePair.Create("sprk_regardingmatter", (RecordOwnershipParent?)new RecordOwnershipParent("sprk_matter", SecureMatter)),
        };
        if (sendsAnotherProject)
        {
            lookups.Add(KeyValuePair.Create("sprk_regardingproject",
                (RecordOwnershipParent?)new RecordOwnershipParent("sprk_project", OtherProject)));
        }

        var merged = new Entity("sprk_event");
        foreach (var (column, parent) in lookups)
            merged[column] = new EntityReference(parent!.EntityLogicalName, parent.RecordId);

        var plan = await stamps.Restamper.PlanStampOfRowAsync("sprk_event", merged, CancellationToken.None);
        var question = RecordCreationService.AsTheRowEnds(lookups, plan);

        plan.Should().NotBeNull().And.ContainKey("sprk_regardingproject").And.NotContainKey("sprk_regardingmatter",
            "the plan names only the column that changes; the matter agrees");
        question.Should().ContainSingle(l => l.Key == "sprk_regardingmatter")
            .Which.Value.Should().Be(new RecordOwnershipParent("sprk_matter", SecureMatter), "the agreeing column is kept as sent");
        question.Should().ContainSingle(l => l.Key == "sprk_regardingproject")
            .Which.Value.Should().Be(new RecordOwnershipParent("sprk_project", RootProject),
                sendsAnotherProject ? "a disagreeing column is replaced by the planned root" : "a missing column is added");
        question.Should().ContainSingle(l => l.Key == "sprk_regardingevent")
            .Which.Value.Should().Be(new RecordOwnershipParent("sprk_event", Event), "a non-stamp lookup is unchanged");
        question.Should().HaveCount(3);
        CoreAncestorRestamper.MissingOf(merged, plan).Should().Match<Dictionary<string, object>?>(m =>
                sendsAnotherProject ? m == null : m!.Count == 1 && m.ContainsKey("sprk_regardingproject"),
            "the bind stays fill-only: a held project is never overwritten, a missing one is bound");
    }

    /// <summary>The stamp world's reads of <see cref="Event"/> (the intermediate) so far.</summary>
    private static int IntermediateReads(StampWorld stamps) =>
        Mock.Get(stamps.Service).Invocations.Count(i =>
            i.Method.Name == nameof(IGenericEntityService.RetrieveAsync)
            && i.Arguments[0] is "sprk_event" && i.Arguments[1] is Guid id && id == Event);

    /// <summary>Every AppendTo probe the caller's Dataverse is asked from now on (RetrievePrincipalAccess paths).</summary>
    private List<string> AppendToProbes()
    {
        var probes = new List<string>();
        _user.GetOverride = path =>
        {
            if (path.Contains("RetrievePrincipalAccess", StringComparison.Ordinal))
                probes.Add(path);
            return null;
        };
        return probes;
    }

    [Fact]
    public async Task RouteCreate_WhenTheParentsRootCannotBeDetermined_Is400StampUnderivable_ThroughTheSharedMap_AndCreatesNothing()
    {
        TheEventIsFiledUnderTheOrdinaryMatter();
        var stamps = new StampWorld(); // the event is gone by the time its root is read: NotFound, a data condition

        var result = await CreateChildOverStamps("sprk_event", AnEventUnderTheEvent(), stamps);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be("record_creation.stamp_underivable");
        problem.ProblemDetails.Extensions["errorCode"].Should().Be("record_creation.stamp_underivable");
        problem.ProblemDetails.Detail.Should().StartWith("The event was not saved: the core record it belongs to cannot be determined");
        _appCreates.Should().BeEmpty("nothing is created");
        _user.Posts.Should().BeEmpty("and there is no caller-side fallback");
    }

    [Fact]
    public async Task RouteCreate_WhenTheParentsRootCannotBeReadRightNow_Is503StampUnavailable_ThroughTheSharedMap_AndCreatesNothing()
    {
        TheEventIsFiledUnderTheOrdinaryMatter();
        var stamps = new StampWorld().Row("sprk_event", Event, [("sprk_regardingmatter", "sprk_matter", OrdinaryMatter)])
            .FailRead("sprk_event", Event);

        var result = await CreateChildOverStamps("sprk_event", AnEventUnderTheEvent(), stamps);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be("record_creation.stamp_unavailable");
        problem.ProblemDetails.Detail.Should().Be(
            "The event could not be saved right now, so nothing was created. Try again in a few minutes.");
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }
}
