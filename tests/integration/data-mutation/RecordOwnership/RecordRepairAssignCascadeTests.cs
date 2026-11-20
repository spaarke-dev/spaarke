using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 052f (spec FR-25 as amended; design 052e §3.1, §3.2; owner 2026-11-18 Q2, Q5; UAC-r2
/// U2, U4): the Assign-cascade allow-list and the per-row child check. A row is kept with its owner only when it has a child along
/// a relationship the allow-list does not accept, or (an event's or an analysis's accepted notes) a note owned by another
/// principal; any other accepted relationship is never read per row; the per-row reads need organization-level Read confirmed
/// once per pass; every activity relationship is
/// one <c>activitypointer</c> read; a failed or refused child read is unknown (retried), never "no children"; a child added between
/// the check and the write is logged and counted. Same harness as <see cref="RecordRepairPassJobTests"/>: the real job and Repair
/// mode over a doubled Dataverse boundary (<see cref="RepairWorld"/>'s cascade children).
/// </summary>
public sealed partial class RecordRepairPassJobTests
{
    // ── The live inventory (design 052e §1.4): relationships that cascade on Assign, per pass table ─────────────────────

    private static readonly (string Schema, string Child, string Column)[] MatterInventory =
    [
        ("sprk_matter_Teams", "team", "regardingobjectid"),
        ("sprk_matter_SharePointDocumentLocations", "sharepointdocumentlocation", "regardingobjectid"),
        ("sprk_matter_SharePointDocuments", "sharepointdocument", "regardingobjectid"),
    ];

    private static readonly (string Schema, string Child, string Column)[] ProjectInventory =
    [
        ("sprk_project_Teams", "team", "regardingobjectid"),
        ("sprk_project_SharePointDocumentLocations", "sharepointdocumentlocation", "regardingobjectid"),
        ("sprk_project_SharePointDocuments", "sharepointdocument", "regardingobjectid"),
    ];

    private static readonly (string Schema, string Child, string Column)[] EventInventory =
    [
        ("sprk_event_Teams", "team", "regardingobjectid"),
        ("sprk_event_SharePointDocumentLocations", "sharepointdocumentlocation", "regardingobjectid"),
        ("sprk_event_SharePointDocuments", "sharepointdocument", "regardingobjectid"),
        ("sprk_event_Annotations", "annotation", "objectid"),
    ];

    private static readonly (string Schema, string Child, string Column)[] DocumentInventory =
        [("sprk_document_Teams", "team", "regardingobjectid")];

    private static readonly (string Schema, string Child, string Column)[] InvoiceInventory =
        [("sprk_invoice_Teams", "team", "regardingobjectid")];

    private static readonly (string Schema, string Child, string Column)[] AnalysisInventory =
    [
        ("sprk_analysis_chatmessages", "sprk_analysischatmessage", "sprk_analysisid"),
        ("sprk_analysis_emailmetadata", "sprk_analysisemailmetadata", "sprk_analysisid"),
        ("sprk_analysis_workingversions", "sprk_analysisworkingversion", "sprk_analysisid"),
        ("sprk_analysis_Annotations", "annotation", "objectid"),
        ("sprk_analysis_Appointments", "appointment", "regardingobjectid"),
        ("sprk_analysis_Emails", "email", "regardingobjectid"),
        ("sprk_analysis_Tasks", "task", "regardingobjectid"),
        ("sprk_analysis_PhoneCalls", "phonecall", "regardingobjectid"),
        ("sprk_analysis_adx_portalcomments", "adx_portalcomment", "regardingobjectid"),
    ];

    private static readonly (string Schema, string Child, string Column)[] ContactInventory =
    [
        ("contact_Teams", "team", "regardingobjectid"),
        ("Contact_Annotation", "annotation", "objectid"),
        ("contact_customer_contacts", "contact", "parentcustomerid"),
        ("Contact_Emails", "email", "regardingobjectid"),
        ("Contact_Tasks", "task", "regardingobjectid"),
        ("Contact_Appointments", "appointment", "regardingobjectid"),
        ("Contact_Faxes", "fax", "regardingobjectid"),
        ("Contact_Letters", "letter", "regardingobjectid"),
        ("Contact_Phonecalls", "phonecall", "regardingobjectid"),
        ("contact_actioncard", "actioncard", "regardingobjectid"),
        ("contact_socialprofiles", "socialprofile", "customerid"),
        ("contact_MailboxTrackingFolder", "mailboxtrackingfolder", "regardingobjectid"),
        ("Contact_SyncErrors", "syncerror", "regardingobjectid"),
        ("contact_SharePointDocumentLocations", "sharepointdocumentlocation", "regardingobjectid"),
        ("contact_SharePointDocuments", "sharepointdocument", "regardingobjectid"),
        ("adx_contact_externalidentity", "adx_externalidentity", "adx_contactid"),
    ];

    private void Inventory(string table, IEnumerable<(string Schema, string Child, string Column)> relationships)
    {
        foreach (var (schema, child, column) in relationships)
            World.CascadesTo(table, schema, child, column);
    }

    /// <summary>An analysis filed under the matter, owned by the person in the child unit (natively created).</summary>
    private Guid AnalysisUnderTheMatter() =>
        World.RowOf("sprk_analysis", Directory.ChildBu, new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("systemuser", RepairWorld.PersonInChild),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", RepairWorld.Matter),
        });

    /// <summary>A row of <paramref name="table"/> filed under nothing, owned by the person in the child unit.</summary>
    private Guid UnfiledRowOf(string table) =>
        World.RowOf(table, Directory.ChildBu,
            new Dictionary<string, object> { ["ownerid"] = new EntityReference("systemuser", RepairWorld.PersonInChild) });

    private static IEnumerable<string> ChildTablesNamed(RepairWorld world) => world.CascadeChildQueries.Select(q => q.EntityName);

    // =====================================================================================
    // AC1: the allow-list holds exactly the design's entries, each with its reason
    // =====================================================================================

    private static readonly string[] DesignAllowList =
    [
        "sprk_matter->team", "sprk_project->team", "sprk_event->team", "sprk_document->team", "sprk_invoice->team", "contact->team",
        "contact->adx_externalidentity",
        "sprk_matter->sharepointdocument", "sprk_project->sharepointdocument", "sprk_event->sharepointdocument",
        "contact->sharepointdocument",
        "sprk_matter->sharepointdocumentlocation", "sprk_project->sharepointdocumentlocation",
        "sprk_event->sharepointdocumentlocation", "contact->sharepointdocumentlocation",
        "sprk_analysis->sprk_analysischatmessage", "sprk_analysis->sprk_analysisemailmetadata",
        "sprk_analysis->sprk_analysisworkingversion",
        "sprk_event->annotation", "sprk_analysis->annotation",
    ];

    [Fact]
    public void AC1_TheAllowList_HoldsExactlyTheDesignEntries_EachWithItsReason()
    {
        RecordCreationService.AcceptedAssignCascades.Keys.Should().BeEquivalentTo(DesignAllowList);
        RecordCreationService.AcceptedAssignCascades.Should().OnlyContain(e => e.Value.Length > 20, "each entry says why");
        RecordCreationService.AcceptedOnlyWithTheOwnersNotes.Should().BeEquivalentTo(["sprk_event->annotation", "sprk_analysis->annotation"]);
        RecordCreationService.AcceptedAssignCascades
            .Where(e => !RecordCreationService.AcceptedOnlyWithTheOwnersNotes.Contains(e.Key))
            .Should().OnlyContain(e => e.Value.Contains("Never read per row"));
        RecordCreationService.AcceptedAssignCascades
            .Where(e => RecordCreationService.AcceptedOnlyWithTheOwnersNotes.Contains(e.Key))
            .Should().OnlyContain(e => e.Value
                == "Assign carries every note on the row; Repair re-owns the row only when every note on it is the row owner's.");
    }

    [Fact]
    public void AC1_AC10_NoNeverAcceptedRelationship_IsOnTheAllowList_AndTheNeverListsHoldTheDesignEntries()
    {
        RecordCreationService.NeverAcceptedAssignCascadeChildren.Should().Contain(
        [
            "activitypointer", "appointment", "chat", "email", "fax", "letter", "phonecall", "recurringappointmentmaster",
            "socialactivity", "task", "adx_inviteredemption", "adx_portalcomment",
            "actioncard", "socialprofile", "mailboxtrackingfolder", "syncerror",
            "sprk_signal", "sprk_decisionrecord", "sprk_budgetrevision",
        ]);
        RecordCreationService.NeverAcceptedAssignCascades.Should().BeEquivalentTo(["contact->contact", "contact->annotation"]);

        foreach (var key in RecordCreationService.AcceptedAssignCascades.Keys)
        {
            var (table, child) = (key.Split("->")[0], key.Split("->")[1]);
            RecordCreationService.NeverAcceptedAssignCascadeChildren.Should().NotContain(child, $"{key} is never accepted");
            RecordCreationService.NeverAcceptedAssignCascades.Should().NotContain(key);
            RecordCreationService.IsAcceptedAssignCascade(table, child).Should().BeTrue(key);
        }
    }

    [Theory]
    [InlineData("contact", "annotation")]
    [InlineData("contact", "contact")]
    [InlineData("contact", "task")]
    [InlineData("sprk_analysis", "email")]
    [InlineData("sprk_analysis", "activitypointer")]
    [InlineData("contact", "actioncard")]
    [InlineData("sprk_matter", "sprk_signal")]
    [InlineData("sprk_event", "sprk_decisionrecord")]
    [InlineData("sprk_project", "sprk_budgetrevision")]
    public void AC10_ANeverAcceptedRelationship_IsNotAccepted(string table, string child) =>
        RecordCreationService.IsAcceptedAssignCascade(table, child).Should().BeFalse();

    [Fact]
    public void AC1_TheChildReads_SkipEveryAcceptedRelationship_AndFoldEveryActivityIntoOneActivityPointerRead()
    {
        var cascades = ContactInventory.Select(r => new AssignCascadeRelationship(r.Schema, r.Child, "Cascade", r.Column));

        var (reads, unreadable) = RecordCreationService.ChildReadsOf("contact", cascades);

        unreadable.Should().BeEmpty();
        reads.Select(r => (r.Table, r.Column)).Should().BeEquivalentTo(new[]
        {
            ("activitypointer", "regardingobjectid"), ("annotation", "objectid"), ("contact", "parentcustomerid"),
            ("actioncard", "regardingobjectid"), ("socialprofile", "customerid"), ("mailboxtrackingfolder", "regardingobjectid"),
            ("syncerror", "regardingobjectid"),
        });
        reads.Single(r => r.Table == "activitypointer").Relationships.Should().BeEquivalentTo(
            ["Contact_Emails", "Contact_Tasks", "Contact_Appointments", "Contact_Faxes", "Contact_Letters", "Contact_Phonecalls"]);
        reads.Should().OnlyContain(r => r.Kind == AssignCascadeChildReadKind.AnyChild, "a contact's notes are never accepted");
    }

    [Theory]
    [InlineData("sprk_event", "sprk_event_Annotations")]
    [InlineData("sprk_analysis", "sprk_analysis_Annotations")]
    public void AC3_AnAcceptedNoteRelationship_IsReadPerRow_ForANoteOfAnotherOwner(string table, string schema)
    {
        var inventory = table == "sprk_event" ? EventInventory : AnalysisInventory;
        var cascades = inventory.Select(r => new AssignCascadeRelationship(r.Schema, r.Child, "Cascade", r.Column));

        var (reads, unreadable) = RecordCreationService.ChildReadsOf(table, cascades);

        unreadable.Should().BeEmpty();
        var note = reads.Should().ContainSingle(r => r.Table == "annotation").Which;
        note.Column.Should().Be("objectid");
        note.Kind.Should().Be(AssignCascadeChildReadKind.NoteOfAnotherOwner);
        note.Relationships.Should().Equal(schema);
        reads.Select(r => r.Table).Should().NotContain(
            ["team", "sharepointdocument", "sharepointdocumentlocation", "sprk_analysischatmessage", "sprk_analysisemailmetadata",
                "sprk_analysisworkingversion"],
            "the team, document, location and analysis content are never read");
    }

    [Fact]
    public void AC6_ARelationshipThatDoesNotStateItsReferencingColumn_IsUnreadable_NeverSkipped()
    {
        var (reads, unreadable) = RecordCreationService.ChildReadsOf("contact",
            [new AssignCascadeRelationship("contact_nocolumn", "sprk_memo", "Cascade", null)]);

        reads.Should().BeEmpty();
        unreadable.Should().ContainSingle().Which.SchemaName.Should().Be("contact_nocolumn");
    }

    // =====================================================================================
    // AC2: a row with no unaccepted child is re-owned; accepted relationships are never read per row
    // =====================================================================================

    [Theory]
    [InlineData("sprk_matter")]
    [InlineData("sprk_project")]
    [InlineData("sprk_document")]
    public async Task AC2_ARowWithOnlyAcceptedCascades_IsReOwned_WithNoChildRead(string table)
    {
        Inventory(table, table switch
        {
            "sprk_matter" => MatterInventory,
            "sprk_project" => ProjectInventory,
            _ => DocumentInventory,
        });
        var id = UnfiledRowOf(table);

        var outcome = await World.RepairAsync(table, id);

        outcome.Status.Should().Be(RecordRepairStatus.Repaired);
        World.Writes.Should().ContainSingle().Which.Fields.Should().ContainKey("ownerid");
        World.CascadeChildQueries.Should().BeEmpty("every relationship of the table is accepted: none is read per row");
    }

    [Fact]
    public async Task AC2_AnInvoiceWithOnlyAcceptedCascades_IsNotRefusedForThem()
    {
        // In this build every invoice-family write is held by the invoice guard (Repair report: that guard's code), so the
        // owner is not written; the cascade check itself refuses nothing and reads nothing.
        Inventory("sprk_invoice", InvoiceInventory);
        var id = World.InvoiceUnderTheMatter();

        var outcome = await World.RepairAsync("sprk_invoice", id);

        outcome.Fields.Should().NotContain(f => f.ReasonCode == RecordRepairReason.OwnerAssignCascades
                                                || f.ReasonCode == RecordRepairReason.OwnerAssignCascadeUnknown);
        World.CascadeChildQueries.Should().BeEmpty();
    }

    [Fact]
    public async Task AC2_AContactWithNoUnacceptedChild_IsReOwned_AfterOneTopOneReadPerUnacceptedRelationship()
    {
        Inventory("contact", ContactInventory);
        var id = _pass.NativeContact();

        var outcome = await World.RepairAsync("contact", id, _pass.Context());

        outcome.Status.Should().Be(RecordRepairStatus.Repaired);
        World.Writes.Should().ContainSingle().Which.Fields["ownerid"]
            .Should().Be(new EntityReference("team", CustomerContactOwnerTests.CustomerTeam));
        string[] unaccepted =
            ["activitypointer", "annotation", "contact", "actioncard", "socialprofile", "mailboxtrackingfolder", "syncerror"];
        ChildTablesNamed(World).Take(unaccepted.Length).Should().BeEquivalentTo(unaccepted,
            "one read per unaccepted (table, column); the accepted team, external identity, document and location are never read");
        ChildTablesNamed(World).Skip(unaccepted.Length).Should().BeEquivalentTo(unaccepted,
            "each is read once more after the owner write (the race)");
        World.CascadeChildQueries.Take(unaccepted.Length).Should().OnlyContain(q => q.TopCount == 1, "the check reads top 1");
        World.CascadeChildQueries.Skip(unaccepted.Length).Should().OnlyContain(q => q.TopCount == RecordCreationService.RaceReadTopCount,
            "the race re-read asks for more, to name each child that moved");
        World.CascadeChildQueries.Should().OnlyContain(q => q.Criteria.Conditions.Single().Values.Single().Equals(id));
    }

    [Fact]
    public async Task AC2_TheMetadataIsReadOncePerTablePerPass_TheChildrenOncePerRow()
    {
        Inventory("contact", ContactInventory);
        _pass.NativeContact();
        _pass.NativeContact();

        var result = await _pass.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        World.AssignCascadeReads.Where(t => t == "contact").Should().ContainSingle();
        World.CascadeChildQueries.Count(q => q.EntityName == "activitypointer")
            .Should().Be(4, "per row: once for the check and once again after the owner write");
        PassWorld.Count(result, "repaired").Should().Be(2);
    }

    // =====================================================================================
    // AC3 (owner "owner's notes only"): an event or an analysis is re-owned only when every note on it is the row owner's
    // =====================================================================================

    private static readonly EntityReference RowOwner = new("systemuser", RepairWorld.PersonInChild);

    private Guid EventWithNotes() =>
        World.EventUnderTheMatter(clientName: null, practiceArea: null, priority: null, indexed: true);

    [Fact]
    public async Task AC3_AnEventWhoseNotesAreAllTheOwners_IsReOwned_AfterOneNoteReadForAnotherOwner()
    {
        Inventory("sprk_event", EventInventory);
        var id = EventWithNotes();
        World.AddNote(id, RowOwner);
        World.AddNote(id, RowOwner, createdBy: Directory.CallerUserId);

        var outcome = await World.RepairAsync("sprk_event", id);

        outcome.Status.Should().Be(RecordRepairStatus.Repaired);
        World.Writes.Should().ContainSingle().Which.Fields["ownerid"]
            .Should().Be(new EntityReference("team", Directory.ChildTeam), "the owner write is issued; Dataverse moves the notes with it");
        ChildTablesNamed(World).Should().Equal(["annotation", "annotation"], "the note check, then the race re-read");
        var check = World.CascadeChildQueries[0];
        check.TopCount.Should().Be(1);
        check.Criteria.Filters.Should().ContainSingle().Which.Conditions.Select(c => (c.AttributeName, c.Operator))
            .Should().BeEquivalentTo(new[] { ("owninguser", ConditionOperator.NotEqual), ("owninguser", ConditionOperator.Null) },
                "compared with the row's current owner, a user: by owning user (id and type)");
        outcome.Fields.Should().NotContain(f => f.ReasonCode == RecordRepairReason.OwnerAssignCascades);
        outcome.RacedChildren.Should().BeEmpty("both notes were there before the check began");
    }

    [Fact]
    public async Task AC3_AnAnalysisWhoseNotesAreAllTheOwners_IsReOwned()
    {
        Inventory("sprk_analysis", AnalysisInventory);
        var id = AnalysisUnderTheMatter();
        World.AddNote(id, RowOwner);

        var outcome = await World.RepairAsync("sprk_analysis", id);

        outcome.Status.Should().Be(RecordRepairStatus.Repaired);
        World.Writes.Should().ContainSingle().Which.Fields.Should().ContainKey("ownerid");
        ChildTablesNamed(World).Should().Equal(["activitypointer", "annotation", "activitypointer", "annotation"],
            "the activities and the note check (the check, then again after the write); the analysis content is never read");
    }

    public static TheoryData<string> OtherNoteOwners => new() { "another user", "a team" };

    private static EntityReference OtherNoteOwner(string who) => who == "a team"
        ? new EntityReference("team", Directory.ChildTeam)
        : new EntityReference("systemuser", Directory.CallerUserId);

    [Theory]
    [MemberData(nameof(OtherNoteOwners))]
    public async Task AC3_AnEventWithANoteOwnedByAnotherPrincipal_IsNotReOwned_AndIsCounted(string who)
    {
        Inventory("sprk_event", EventInventory);
        var id = _pass.NativeEvent();
        World.AddNote(id, RowOwner);
        World.AddNote(id, OtherNoteOwner(who));

        var result = await _pass.RunAsync();

        World.Writes.Should().NotContain(w => w.Id == id && w.Fields.ContainsKey("ownerid"), "the Assign would carry that note");
        PassWorld.Count(result, "ownerAssignCascades").Should().Be(1);
        PassWorld.Count(result, "pendingRetry").Should().Be(0, "an answer, not a fault");
        using var json = JsonDocument.Parse(result.ResultJson!);
        json.RootElement.GetProperty("ownerAssignCascadesByTable").GetProperty("sprk_event").GetInt32().Should().Be(1);
        World.Log.Entries.Should().ContainSingle(e => e.Message.Contains("sprk_event_Annotations")
                                                      && e.Message.Contains("note owned by another principal")
                                                      && e.Message.Contains(id.ToString()));
    }

    [Theory]
    [MemberData(nameof(OtherNoteOwners))]
    public async Task AC3_AnAnalysisWithANoteOwnedByAnotherPrincipal_IsNotReOwned(string who)
    {
        Inventory("sprk_analysis", AnalysisInventory);
        var id = AnalysisUnderTheMatter();
        World.AddNote(id, OtherNoteOwner(who));

        var outcome = await World.RepairAsync("sprk_analysis", id);

        outcome.Fields.Should().ContainSingle(f => f.Part == RecordRepairPart.Owner)
            .Which.ReasonCode.Should().Be(RecordRepairReason.OwnerAssignCascades);
        outcome.NeedsRetry.Should().BeFalse();
        World.Writes.Should().NotContain(w => w.Fields.ContainsKey("ownerid"));
    }

    [Theory]
    [MemberData(nameof(ChildReadFailures))]
    public async Task AC3_ANoteReadThatFails_IsUnknown_NeverNoOtherOwnersNote(string how)
    {
        Inventory("sprk_event", EventInventory);
        var id = EventWithNotes();
        World.AddNote(id, RowOwner);
        ChildReadFails("annotation", how);

        var outcome = await World.RepairAsync("sprk_event", id);

        outcome.Fields.Should().ContainSingle(f => f.Part == RecordRepairPart.Owner)
            .Which.ReasonCode.Should().Be(RecordRepairReason.OwnerAssignCascadeUnknown);
        outcome.NeedsRetry.Should().BeTrue();
        World.Writes.Should().NotContain(w => w.Fields.ContainsKey("ownerid"));
    }

    [Fact]
    public async Task AC3a_AContactWithANote_IsNotReOwned_AndIsCounted()
    {
        Inventory("contact", ContactInventory);
        var id = _pass.NativeContact();
        World.AddCascadeChild("annotation", "objectid", id);

        var result = await _pass.RunAsync();

        World.Writes.Should().NotContain(w => w.Fields.ContainsKey("ownerid"));
        PassWorld.Count(result, "ownerAssignCascades").Should().Be(1);
        PassWorld.Count(result, "pendingRetry").Should().Be(0, "an answer, not a fault");
        World.Log.Entries.Should().ContainSingle(e => e.Message.Contains("Contact_Annotation")
                                                      && e.Message.Contains(RecordRepairReason.OwnerAssignCascades));
    }

    // =====================================================================================
    // AC4, AC5 (negative): activities and child contacts never move
    // =====================================================================================

    [Fact]
    public async Task AC4_AnAnalysisWithAnEmail_IsNotReOwned_WithTheRelationshipNamed()
    {
        Inventory("sprk_analysis", AnalysisInventory);
        var id = AnalysisUnderTheMatter();
        World.AddCascadeChild("activitypointer", "regardingobjectid", id, activityType: "email");

        var outcome = await World.RepairAsync("sprk_analysis", id);

        outcome.Fields.Should().ContainSingle(f => f.Part == RecordRepairPart.Owner)
            .Which.ReasonCode.Should().Be(RecordRepairReason.OwnerAssignCascades);
        outcome.NeedsRetry.Should().BeFalse();
        World.Writes.Should().NotContain(w => w.Fields.ContainsKey("ownerid"));
        World.Log.Entries.Should().ContainSingle(e => e.Message.Contains("sprk_analysis_Emails")
                                                      && e.Message.Contains(id.ToString()));
    }

    [Fact]
    public async Task AC5_AContactWithAChildContact_IsNotReOwned()
    {
        Inventory("contact", ContactInventory);
        var id = _pass.NativeContact();
        World.AddCascadeChild("contact", "parentcustomerid", id);

        var outcome = await World.RepairAsync("contact", id, _pass.Context());

        outcome.Fields.Should().ContainSingle(f => f.Part == RecordRepairPart.Owner)
            .Which.ReasonCode.Should().Be(RecordRepairReason.OwnerAssignCascades);
        World.Writes.Should().NotContain(w => w.Fields.ContainsKey("ownerid"));
        World.Log.Entries.Should().Contain(e => e.Message.Contains("contact_customer_contacts"));
    }

    // =====================================================================================
    // AC6, AC7 (negative): a child read that fails, is refused or gives no answer is unknown, never "no children"
    // =====================================================================================

    public static TheoryData<string> ChildReadFailures => new() { "timeout", "privilege refused", "http refused", "no answer" };

    private void ChildReadFails(string table, string how)
    {
        switch (how)
        {
            case "timeout":
                World.CascadeChildFaults[table] = new TimeoutException("Test: the read timed out.");
                break;
            case "privilege refused":
                // The form Dataverse refuses a read in for a principal whose role does not reach the table.
                World.CascadeChildFaults[table] = new System.ServiceModel.FaultException<OrganizationServiceFault>(
                    new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040220), Message = "Test: missing prvRead privilege." },
                    new System.ServiceModel.FaultReason("Test: missing prvRead privilege."));
                break;
            case "http refused":
                World.CascadeChildFaults[table] = new HttpRequestException("Test: refused.", null, System.Net.HttpStatusCode.Forbidden);
                break;
            default:
                World.CascadeChildNoAnswer.Add(table);
                break;
        }
    }

    [Theory]
    [MemberData(nameof(ChildReadFailures))]
    public async Task AC6_AChildReadThatFails_IsUnknown_NeverNoChildren(string how)
    {
        Inventory("contact", ContactInventory);
        var id = _pass.NativeContact();
        ChildReadFails("annotation", how);

        var outcome = await World.RepairAsync("contact", id, _pass.Context());

        outcome.Fields.Should().ContainSingle(f => f.Part == RecordRepairPart.Owner)
            .Which.ReasonCode.Should().Be(RecordRepairReason.OwnerAssignCascadeUnknown);
        outcome.NeedsRetry.Should().BeTrue("unknown is a fault: asked again");
        World.Writes.Should().NotContain(w => w.Fields.ContainsKey("ownerid"));
    }

    [Fact]
    public async Task AC6_ARelationshipWithoutItsReferencingColumn_IsUnknown_NeverNoChildren()
    {
        World.AssignCascades["contact"] = [new AssignCascadeRelationship("contact_nocolumn", "sprk_memo", "Cascade")];
        var id = _pass.NativeContact();

        var outcome = await World.RepairAsync("contact", id, _pass.Context());

        outcome.Fields.Should().ContainSingle(f => f.Part == RecordRepairPart.Owner)
            .Which.ReasonCode.Should().Be(RecordRepairReason.OwnerAssignCascadeUnknown);
        World.Writes.Should().NotContain(w => w.Fields.ContainsKey("ownerid"));
    }

    [Fact]
    public async Task AC7_ARowStillUnknownAfterItsAttempts_IsAbandoned_AndListedInTheReport()
    {
        Inventory("contact", ContactInventory);
        var id = _pass.NativeContact();
        ChildReadFails("activitypointer", "timeout");

        JobRunResult last = await _pass.RunAsync();
        PassWorld.Count(last, "pendingRetry").Should().Be(1);
        for (var run = 2; run <= RecordRepairPassJob.MaxAttempts; run++)
            last = await _pass.RunAsync();

        PassWorld.Count(last, "abandoned").Should().Be(1);
        PassWorld.RetrySet(last, "contact").Should().BeEquivalentTo((0, new[] { id.ToString("D") }));
        World.Writes.Should().NotContain(w => w.Fields.ContainsKey("ownerid"));
        World.Directory.Row("contact", id).GetAttributeValue<EntityReference>("ownerid")
            .Should().Be(new EntityReference("systemuser", RepairWorld.PersonInChild));
    }

    // =====================================================================================
    // AC8 (negative): an unstated Assign value counts as cascading
    // =====================================================================================

    [Fact]
    public async Task AC8_ARelationshipWithAnUnstatedAssignValue_IsReadAndRefusesARowWithAChild()
    {
        World.CascadesTo("contact", "contact_unstated", "sprk_memo", "sprk_regardingcontact", assign: null);
        var withChild = _pass.NativeContact();
        var withoutChild = _pass.NativeContact();
        World.AddCascadeChild("sprk_memo", "sprk_regardingcontact", withChild);

        var refused = await World.RepairAsync("contact", withChild, _pass.Context());
        var reOwned = await World.RepairAsync("contact", withoutChild, _pass.Context());

        refused.Fields.Should().ContainSingle(f => f.Part == RecordRepairPart.Owner)
            .Which.ReasonCode.Should().Be(RecordRepairReason.OwnerAssignCascades);
        reOwned.Status.Should().Be(RecordRepairStatus.Repaired);
        World.CascadeChildQueries.Should().HaveCount(3,
            "read for each row (and once more after the write of the re-owned one): an unstated Assign value is treated as cascading");
    }

    // =====================================================================================
    // AC9: a child added between the check and the write is logged and counted (not undone, K1)
    // =====================================================================================

    [Fact]
    public async Task AC9_AChildAddedAfterTheCheck_IsLoggedWithItsTableAndId_AndCounted()
    {
        Inventory("contact", ContactInventory);
        var id = _pass.NativeContact();
        var raced = Guid.Empty;
        World.BeforeWrite = () => raced = World.AddCascadeChild("activitypointer", "regardingobjectid", id, activityType: "email");

        var result = await _pass.RunAsync();

        World.Writes.Should().ContainSingle().Which.Fields.Should().ContainKey("ownerid", "the check found no child: the owner is written");
        PassWorld.Count(result, "ownerAssignCascadeRaced").Should().Be(1);
        World.Log.Entries.Should().ContainSingle(e => e.Message.Contains(RecordRepairReason.OwnerAssignCascadeRaced)
                                                      && e.Message.Contains("email") && e.Message.Contains(raced.ToString())
                                                      && e.Message.Contains("Contact_Emails"));
        PassWorld.Count(result, "pendingRetry").Should().Be(0, "the move is not undone (K1); the row is repaired");
    }

    [Fact]
    public async Task AC9_TheRaceReRead_ReturnsTheRacedChild_AndNothingWhenNoneWasAdded()
    {
        Inventory("contact", ContactInventory);
        var quiet = _pass.NativeContact();
        var racedRow = _pass.NativeContact();

        var none = await World.RepairAsync("contact", quiet, _pass.Context());
        World.BeforeWrite = () => World.AddCascadeChild("contact", "parentcustomerid", racedRow);
        var hit = await World.RepairAsync("contact", racedRow, _pass.Context());

        none.RacedChildren.Should().BeEmpty();
        none.RaceUnchecked.Should().BeFalse();
        hit.RacedChildren.Should().ContainSingle().Which.Table.Should().Be("contact");
        hit.RacedChildren.Single().Relationships.Should().Equal("contact_customer_contacts");
    }

    [Fact]
    public async Task AC9_ARaceReReadThatFails_IsReportedAsUnchecked_AndCounted()
    {
        Inventory("contact", ContactInventory);
        _pass.NativeContact();
        World.BeforeWrite = () => World.CascadeChildFaults["annotation"] = new TimeoutException("Test: the re-read timed out.");

        var result = await _pass.RunAsync();

        World.Writes.Should().ContainSingle().Which.Fields.Should().ContainKey("ownerid");
        PassWorld.Count(result, "ownerAssignCascadeRaceUnchecked").Should().Be(1);
        World.Log.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("could not be read again"));
    }

    [Fact]
    public async Task AC9_TwoChildrenAddedAfterTheCheck_AreEachLoggedWithTheirTableAndId_AndCounted()
    {
        Inventory("contact", ContactInventory);
        var id = _pass.NativeContact();
        var (email, task) = (Guid.Empty, Guid.Empty);
        World.BeforeWrite = () =>
        {
            email = World.AddCascadeChild("activitypointer", "regardingobjectid", id, activityType: "email");
            task = World.AddCascadeChild("activitypointer", "regardingobjectid", id, activityType: "task");
        };

        var result = await _pass.RunAsync();

        PassWorld.Count(result, "ownerAssignCascadeRaced").Should().Be(2);
        World.Log.Entries.Should().ContainSingle(e => e.Message.Contains(RecordRepairReason.OwnerAssignCascadeRaced)
                                                      && e.Message.Contains("email") && e.Message.Contains(email.ToString()));
        World.Log.Entries.Should().ContainSingle(e => e.Message.Contains(RecordRepairReason.OwnerAssignCascadeRaced)
                                                      && e.Message.Contains("task") && e.Message.Contains(task.ToString()));
        World.Log.Entries.Should().ContainSingle(e => e.Message.Contains("2 child(ren) found again")
                                                      && e.Message.Contains("truncated: False"));
    }

    [Fact]
    public async Task AC9_ARaceReReadThatFillsItsPage_IsLoggedAsTruncated()
    {
        Inventory("contact", ContactInventory);
        var id = _pass.NativeContact();
        World.BeforeWrite = () =>
        {
            for (var i = 0; i <= RecordCreationService.RaceReadTopCount; i++)
                World.AddCascadeChild("contact", "parentcustomerid", id);
        };

        var outcome = await World.RepairAsync("contact", id, _pass.Context());

        outcome.RacedChildren.Should().HaveCount(RecordCreationService.RaceReadTopCount);
        World.Log.Entries.Should().ContainSingle(e => e.Message.Contains($"{RecordCreationService.RaceReadTopCount} child(ren) found again")
                                                      && e.Message.Contains("truncated: True"));
    }

    [Fact]
    public async Task AC9_ANoteAddedByAnotherPrincipalAfterTheCheck_IsRaced_ANoteByThePreviousOwnerIsNot()
    {
        Inventory("sprk_event", EventInventory);
        var id = EventWithNotes();
        World.AddNote(id, RowOwner);
        var raced = Guid.Empty;
        World.BeforeWrite = () =>
        {
            raced = World.AddNote(id, OtherNoteOwner("another user"), createdOn: DateTime.UtcNow.AddMinutes(1));
            World.AddNote(id, RowOwner, createdOn: DateTime.UtcNow.AddMinutes(1));
        };

        var outcome = await World.RepairAsync("sprk_event", id);

        outcome.Status.Should().Be(RecordRepairStatus.Repaired);
        outcome.RacedChildren.Should().ContainSingle().Which.Should().Match<RecordRepairRacedChild>(
            c => c.Table == "annotation" && c.Id == raced);
        var race = World.CascadeChildQueries[^1];
        race.TopCount.Should().Be(RecordCreationService.RaceReadTopCount);
        race.Criteria.Conditions.Select(c => (c.AttributeName, c.Operator)).Should().Contain(("createdon", ConditionOperator.GreaterEqual));
        World.Log.Entries.Should().ContainSingle(e => e.Message.Contains(RecordRepairReason.OwnerAssignCascadeRaced)
                                                      && e.Message.Contains("annotation") && e.Message.Contains(raced.ToString()));
    }

    // =====================================================================================
    // Fix round (C): a child found is an answer, whatever another read gave
    // =====================================================================================

    [Theory]
    [InlineData("syncerror", "the read after the one that found the email faults")]
    [InlineData("actioncard", "a read between faults")]
    public async Task AC6_AContactWithAnEmail_IsAnAnswer_EvenWhenAnotherReadFaults(string faulting, string because)
    {
        Inventory("contact", ContactInventory);
        var id = _pass.NativeContact();
        World.AddCascadeChild("activitypointer", "regardingobjectid", id, activityType: "email");
        World.CascadeChildFaults[faulting] = new TimeoutException("Test: the read timed out.");

        var outcome = await World.RepairAsync("contact", id, _pass.Context());

        outcome.Fields.Should().ContainSingle(f => f.Part == RecordRepairPart.Owner)
            .Which.ReasonCode.Should().Be(RecordRepairReason.OwnerAssignCascades, because);
        outcome.NeedsRetry.Should().BeFalse("an answer: the email keeps the row whatever the other read gave");
        ChildTablesNamed(World).Should().Contain(faulting, "every read is still made after one finds a child or faults");
    }

    [Fact]
    public async Task AC6_AChildFoundAfterAReadThatFaulted_IsAnAnswer()
    {
        Inventory("contact", ContactInventory);
        var id = _pass.NativeContact();
        World.CascadeChildFaults["activitypointer"] = new TimeoutException("Test: the first read timed out.");
        World.AddCascadeChild("syncerror", "regardingobjectid", id);

        var outcome = await World.RepairAsync("contact", id, _pass.Context());

        outcome.Fields.Should().ContainSingle(f => f.Part == RecordRepairPart.Owner)
            .Which.ReasonCode.Should().Be(RecordRepairReason.OwnerAssignCascades);
    }

    // =====================================================================================
    // Fix round (B): the per-row child reads need organization-level Read, confirmed once per pass (tri-state)
    // =====================================================================================

    private const string DepthRule = "per-row child checks need organization-level Read; held";

    [Fact]
    public async Task B_Confirmed_ThePerRowReadsAreMade_TheNoteReadIncluded()
    {
        Inventory("contact", ContactInventory);
        Inventory("sprk_event", EventInventory);
        var contact = _pass.NativeContact();
        var anEvent = _pass.NativeEvent();
        World.AddNote(anEvent, RowOwner);
        var pass = _pass.Context();

        var contactOutcome = await World.RepairAsync("contact", contact, pass);
        var eventOutcome = await World.RepairAsync("sprk_event", anEvent, pass);

        contactOutcome.Status.Should().Be(RecordRepairStatus.Repaired);
        eventOutcome.Status.Should().Be(RecordRepairStatus.Repaired);
        ChildTablesNamed(World).Should().Contain(["activitypointer", "annotation"]);
        World.RoleQueries.Should().ContainSingle("asked once for the pass");
        World.Log.Entries.Should().NotContain(e => e.Message.Contains(DepthRule));
    }

    public static TheoryData<string> DepthNotConfirmed => new()
    {
        "not held", "roles read faults", "identity read faults", "roles read gives no answer", "identity is empty",
    };

    private void DepthIs(string how)
    {
        switch (how)
        {
            case "not held":
                World.HoldsSystemAdministrator = false;
                break;
            case "roles read faults":
                World.RoleReadFault = new TimeoutException("Test: the roles read timed out.");
                break;
            case "roles read gives no answer":
                World.RoleReadNoAnswer = true;
                break;
            case "identity is empty":
                World.IdentityAnswered = Guid.Empty;
                break;
            default:
                World.IdentityFault = new TimeoutException("Test: the identity read timed out.");
                break;
        }
    }

    [Theory]
    [MemberData(nameof(DepthNotConfirmed))]
    public async Task B_NotConfirmedOrFaulted_HoldsEveryRowThatNeedsAPerRowRead_OthersProceed(string how)
    {
        Inventory("contact", ContactInventory);
        Inventory("sprk_event", EventInventory);
        Inventory("sprk_analysis", AnalysisInventory);
        Inventory("sprk_matter", MatterInventory);
        DepthIs(how);
        var contact = _pass.NativeContact();
        var anEvent = _pass.NativeEvent();
        World.AddNote(anEvent, RowOwner);
        var analysis = _pass.Native("sprk_analysis", AnalysisUnderTheMatter());
        var matter = _pass.Native("sprk_matter", UnfiledRowOf("sprk_matter"));
        var pass = _pass.Context();

        var held = new[]
        {
            await World.RepairAsync("contact", contact, pass),
            await World.RepairAsync("sprk_event", anEvent, pass),
            await World.RepairAsync("sprk_analysis", analysis, pass),
        };
        var matterOutcome = await World.RepairAsync("sprk_matter", matter, pass);

        held.Should().OnlyContain(o => o.Fields.Single(f => f.Part == RecordRepairPart.Owner).ReasonCode
                                       == RecordRepairReason.OwnerAssignCascadeUnknown);
        held.Should().OnlyContain(o => o.NeedsRetry);
        World.Writes.Should().NotContain(w => w.Id != matter && w.Fields.ContainsKey("ownerid"));
        World.CascadeChildQueries.Should().BeEmpty("no per-row child read is made without confirmed organization-level Read");
        matterOutcome.Status.Should().Be(RecordRepairStatus.Repaired, "a row that needs no per-row read is unaffected");
        World.Writes.Should().Contain(w => w.Id == matter && w.Fields.ContainsKey("ownerid"));
        World.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.Message.Contains(DepthRule),
            "one error line per pass states the rule");
    }

    [Theory]
    [InlineData("roles read faults")]
    [InlineData("identity read faults")]
    public async Task B_Faulted_LogsTheRuleOnceWithTheFault_AndIsNeverConfirmed(string how)
    {
        Inventory("contact", ContactInventory);
        DepthIs(how);
        var pass = _pass.Context();
        var first = _pass.NativeContact();
        var second = _pass.NativeContact();

        await World.RepairAsync("contact", first, pass);
        var later = await World.RepairAsync("contact", second, pass);

        later.Fields.Single(f => f.Part == RecordRepairPart.Owner).ReasonCode.Should().Be(RecordRepairReason.OwnerAssignCascadeUnknown);
        World.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.Message.Contains(DepthRule)
                                                      && e.Exception is TimeoutException);
        World.CascadeChildQueries.Should().BeEmpty();
    }

    [Theory]
    [InlineData("roles read gives no answer", "returned no answer", 1)]
    [InlineData("identity is empty", "could not be read", 0)]
    public async Task B_ARolesReadWithNoAnswer_OrAnEmptyIdentity_IsFaulted_NeverConfirmed(string how, string logged, int roleReads)
    {
        Inventory("contact", ContactInventory);
        DepthIs(how);
        var id = _pass.NativeContact();

        var outcome = await World.RepairAsync("contact", id, _pass.Context());

        outcome.Fields.Single(f => f.Part == RecordRepairPart.Owner).ReasonCode.Should().Be(RecordRepairReason.OwnerAssignCascadeUnknown);
        World.Writes.Should().NotContain(w => w.Fields.ContainsKey("ownerid"));
        World.CascadeChildQueries.Should().BeEmpty();
        World.RoleQueries.Should().HaveCount(roleReads, "an empty identity is not asked about");
        World.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.Message.Contains(DepthRule)
                                                      && e.Message.Contains(logged) && !e.Message.Contains("does not hold"),
            "Faulted, not NotConfirmed");
    }

    // =====================================================================================
    // F2: the note check's owner branches Repair does not reach (it re-owns only a user-owned row), asked directly
    // =====================================================================================

    [Theory]
    [InlineData("sprk_event")]
    [InlineData("sprk_analysis")]
    public async Task AC3_ATeamOwnedRow_TheNoteCheckFiltersOnOwningTeam_AUserOwnedNoteKeepsIt_TheTeamsOwnNoteLetsItThrough(string table)
    {
        Inventory(table, table == "sprk_event" ? EventInventory : AnalysisInventory);
        var team = new EntityReference("team", Directory.ChildTeam);
        var teamsOnly = Guid.NewGuid();
        var withAUsersNote = Guid.NewGuid();
        World.AddNote(teamsOnly, team);
        World.AddNote(withAUsersNote, team);
        World.AddNote(withAUsersNote, RowOwner);

        var through = await World.AssignCascadeCheckAsync(table, teamsOnly, team);
        var kept = await World.AssignCascadeCheckAsync(table, withAUsersNote, team);

        through.Refusal.Should().BeNull("every note on the row is the owning team's");
        kept.Refusal.Should().Be(RecordRepairReason.OwnerAssignCascades, "a user's note would move with the Assign");
        var noteChecks = World.CascadeChildQueries.Where(q => q.EntityName == "annotation").ToList();
        noteChecks.Should().HaveCount(2).And.OnlyContain(q => q.TopCount == 1);
        noteChecks.Should().OnlyContain(q => q.Criteria.Filters.Single().FilterOperator == LogicalOperator.Or);
        noteChecks.SelectMany(q => q.Criteria.Filters.Single().Conditions).Select(c => (c.AttributeName, c.Operator)).Distinct()
            .Should().BeEquivalentTo(new[] { ("owningteam", ConditionOperator.NotEqual), ("owningteam", ConditionOperator.Null) },
                "a team owner is compared by owning team, id and type, never by owning user");
        World.Log.Entries.Should().ContainSingle(e => e.Message.Contains("note owned by another principal")
                                                      && e.Message.Contains(withAUsersNote.ToString()));
        World.Writes.Should().BeEmpty("the check writes nothing");
    }

    public static TheoryData<string> UnreadableOwners => new() { "none", "empty id", "not a user or a team" };

    [Theory]
    [MemberData(nameof(UnreadableOwners))]
    public async Task AC3_TheNoteCheck_WithNoReadableUserOrTeamOwner_IsUnknown_AndReadsNoNote(string how)
    {
        Inventory("sprk_event", EventInventory);
        var id = Guid.NewGuid();
        World.AddNote(id, RowOwner);
        EntityReference? owner = how switch
        {
            "none" => null,
            "empty id" => new EntityReference("systemuser", Guid.Empty),
            _ => new EntityReference("businessunit", Directory.ChildBu),
        };

        var check = await World.AssignCascadeCheckAsync("sprk_event", id, owner);

        check.Refusal.Should().Be(RecordRepairReason.OwnerAssignCascadeUnknown, "whose notes are the owner's cannot be told");
        World.CascadeChildQueries.Should().BeEmpty("no note read is made against an owner it cannot compare with");
        World.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task B_TheDepthIsReadOncePerPass_HoweverManyRowsNeedIt()
    {
        Inventory("contact", ContactInventory);
        Inventory("sprk_event", EventInventory);
        _pass.NativeContact();
        _pass.NativeContact();
        _pass.NativeEvent();

        var result = await _pass.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        PassWorld.Count(result, "repaired").Should().Be(3, "each row's per-row reads were made");
        World.RoleQueries.Should().ContainSingle("the read depth is asked once for the pass, not per row");
    }

    private sealed partial class PassWorld
    {
        /// <summary>Gives <paramref name="table"/> <paramref name="id"/> the creation stamps of a native row inside the pass.</summary>
        public Guid Native(string table, Guid id) => Seed(table, id, createdOn: null, createdBy: null, withoutCreatedOn: false);
    }

    // =====================================================================================
    // AC10 (negative): no write reaches an activity, a child contact, a Signal, a Decision Record or a Budget Revision
    // =====================================================================================

    [Theory]
    [InlineData("activitypointer", "regardingobjectid", "Contact_Tasks", "task")]
    [InlineData("contact", "parentcustomerid", "contact_customer_contacts", "contact")]
    [InlineData("sprk_signal", "sprk_contact", "contact_sprk_signal", "sprk_signal")]
    [InlineData("sprk_decisionrecord", "sprk_contact", "contact_sprk_decisionrecord", "sprk_decisionrecord")]
    [InlineData("sprk_budgetrevision", "sprk_contact", "contact_sprk_budgetrevision", "sprk_budgetrevision")]
    public async Task AC10_ARowWithANeverAcceptedChild_IsNotReOwned_AndNoWriteReachesTheChild(
        string readTable, string column, string schema, string child)
    {
        World.CascadesTo("contact", schema, child, column);
        var id = _pass.NativeContact();
        var childId = World.AddCascadeChild(readTable, column, id, activityType: readTable == "activitypointer" ? child : null);

        var result = await _pass.RunAsync();

        World.Writes.Should().NotContain(w => w.Fields.ContainsKey("ownerid"), "the Assign would move the child");
        World.Writes.Should().NotContain(w => w.Table != "contact" || w.Id != id || w.Id == childId,
            "Repair writes the row it repairs only, never a child");
        PassWorld.Count(result, "ownerAssignCascades").Should().Be(1);
    }

    // =====================================================================================
    // Per-table count
    // =====================================================================================

    [Fact]
    public async Task TheRunCountsRowsKeptForAnUnacceptedChild_PerTable()
    {
        Inventory("contact", ContactInventory);
        World.CascadesTo("sprk_event", "sprk_event_Memos", "sprk_memo", "sprk_regardingevent");
        var first = _pass.NativeContact();
        var second = _pass.NativeContact();
        var anEvent = _pass.NativeEvent();
        World.AddCascadeChild("annotation", "objectid", first);
        World.AddCascadeChild("contact", "parentcustomerid", second);
        World.AddCascadeChild("sprk_memo", "sprk_regardingevent", anEvent);

        var result = await _pass.RunAsync();

        using var json = JsonDocument.Parse(result.ResultJson!);
        var byTable = json.RootElement.GetProperty("ownerAssignCascadesByTable");
        byTable.GetProperty("contact").GetInt32().Should().Be(2);
        byTable.GetProperty("sprk_event").GetInt32().Should().Be(1);
        PassWorld.Count(result, "ownerAssignCascades").Should().Be(3);
        _pass.Log.Entries.Last(e => e.Message.Contains("heartbeat")).Message
            .Should().Contain("ownerAssignCascadesByTable=contact=2,sprk_event=1 ");
    }
}

/// <summary>Task 052f: the repair world's Assign-cascade children — the per-row child reads Repair makes, answered here.</summary>
internal sealed partial class RepairWorld
{
    /// <summary>The children per (child table, referencing column): the parent id, the child id and, for an activity, its type.</summary>
    public List<(string Table, string Column, Guid Parent, Guid Child, string? ActivityType)> CascadeChildren { get; } = [];

    /// <summary>A child's other columns (a note's owning user or team, its creator and when it was created), by child id.</summary>
    public Dictionary<Guid, Dictionary<string, object>> CascadeChildColumnValues { get; } = [];

    /// <summary>The (table, column) pairs this world answers as a child read (registered by <see cref="CascadesTo"/>).</summary>
    public HashSet<(string Table, string Column)> CascadeChildColumns { get; } = [];

    /// <summary>Per child table, the fault a child read throws.</summary>
    public Dictionary<string, Exception> CascadeChildFaults { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Child tables whose read comes back with no answer.</summary>
    public HashSet<string> CascadeChildNoAnswer { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every child read Repair sent, in order.</summary>
    public List<QueryExpression> CascadeChildQueries { get; } = [];

    // ── Fix round (B): the app identity and its roles ──────────────────────────────────────────────────────────────

    /// <summary>The app identity's <c>systemuser</c> id.</summary>
    public static readonly Guid AppIdentity = Guid.Parse("a0160000-0000-4000-8000-0000000000a1");

    /// <summary>Whether the app identity holds the System Administrator role directly (the default: yes).</summary>
    public bool HoldsSystemAdministrator { get; set; } = true;

    /// <summary>The fault the read of the app identity's roles throws.</summary>
    public Exception? RoleReadFault { get; set; }

    /// <summary>The fault the read of the app identity throws.</summary>
    public Exception? IdentityFault { get; set; }

    /// <summary>Every read of the app identity's roles Repair sent.</summary>
    public List<QueryExpression> RoleQueries { get; } = [];

    /// <summary>When set, the read of the app identity's roles comes back with no answer.</summary>
    public bool RoleReadNoAnswer { get; set; }

    /// <summary>When set, the identity the read of the app identity answers (instead of <see cref="AppIdentity"/>).</summary>
    public Guid? IdentityAnswered { get; set; }

    /// <summary>Repair's Assign-cascade check of <paramref name="table"/> <paramref name="id"/>, asked directly against
    /// <paramref name="owner"/> (for the owner branches Repair itself does not reach: it re-owns only a user-owned row).</summary>
    public Task<RecordCreationService.AssignCascadeCheck> AssignCascadeCheckAsync(string table, Guid id, EntityReference? owner) =>
        Service().AssignCascadeCheckAsync(table, id, owner, pass: null, CancellationToken.None);

    /// <summary>
    /// Adds a relationship of <paramref name="table"/> that cascades on Assign to <paramref name="child"/> on
    /// <paramref name="column"/>, and lets the world answer a read of it (an activity on <c>regardingobjectid</c> is read on
    /// <c>activitypointer</c>).
    /// </summary>
    public void CascadesTo(string table, string schemaName, string child, string column, string? assign = "Cascade")
    {
        var existing = AssignCascades.GetValueOrDefault(table) ?? [];
        AssignCascades[table] = [.. existing, new AssignCascadeRelationship(schemaName, child, assign, column)];
        CascadeChildColumns.Add((child, column));
        if (column == "regardingobjectid")
            CascadeChildColumns.Add(("activitypointer", column));
    }

    /// <summary>A child row of <paramref name="parent"/>, as read on <paramref name="table"/>.<paramref name="column"/>.</summary>
    public Guid AddCascadeChild(string table, string column, Guid parent, string? activityType = null)
    {
        var id = Guid.NewGuid();
        CascadeChildren.Add((table, column, parent, id, activityType));
        CascadeChildColumns.Add((table, column));
        return id;
    }

    /// <summary>A note on <paramref name="parent"/> owned by <paramref name="owner"/> (a user or a team), created by
    /// <paramref name="createdBy"/> (default: the owner when a user, else the person in the child unit) a day ago unless
    /// <paramref name="createdOn"/> is given.</summary>
    public Guid AddNote(Guid parent, EntityReference owner, Guid? createdBy = null, DateTime? createdOn = null)
    {
        var id = AddCascadeChild("annotation", "objectid", parent);
        var columns = new Dictionary<string, object>
        {
            [owner.LogicalName == "team" ? "owningteam" : "owninguser"] = owner,
            ["createdby"] = new EntityReference("systemuser",
                createdBy ?? (owner.LogicalName == "systemuser" ? owner.Id : PersonInChild)),
            ["createdon"] = createdOn ?? DateTime.UtcNow.AddDays(-1),
        };
        CascadeChildColumnValues[id] = columns;
        return id;
    }

    /// <summary>The app identity, as <see cref="IGenericEntityService.GetCurrentIdentityIdAsync"/> answers it.</summary>
    private Task<Guid> AppIdentityAnswer() =>
        IdentityFault is { } fault ? Task.FromException<Guid>(fault) : Task.FromResult(IdentityAnswered ?? AppIdentity);

    /// <summary>The answer to a read of the app identity's roles, or <c>null</c> when <paramref name="query"/> is not one. Only
    /// the read Repair is meant to make is answered: the identity's <c>systemuserroles</c> joined to <c>role</c> on the System
    /// Administrator template id.</summary>
    private Task<EntityCollection>? RoleAnswer(QueryExpression query)
    {
        if (query.EntityName != "systemuserroles")
            return null;

        RoleQueries.Add(query);
        if (RoleReadFault is { } fault)
            return Task.FromException<EntityCollection>(fault);
        if (RoleReadNoAnswer)
            return Task.FromResult<EntityCollection>(null!);

        var forTheIdentity = query.Criteria.Conditions.Any(c => c.AttributeName == "systemuserid"
                                                                && c.Operator == ConditionOperator.Equal
                                                                && c.Values.Single() is Guid who && who == AppIdentity);
        var byTemplate = query.LinkEntities.Any(l => l.LinkToEntityName == "role"
                                                     && l.LinkCriteria.Conditions.Any(c => c.AttributeName == "roletemplateid"
                                                         && c.Operator == ConditionOperator.Equal
                                                         && c.Values.Single() is Guid template
                                                         && template == RecordCreationService.SystemAdministratorRoleTemplateId));
        var rows = forTheIdentity && byTemplate && HoldsSystemAdministrator
            ? new List<Entity> { new("systemuserroles", Guid.NewGuid()) }
            : [];
        return Task.FromResult(new EntityCollection(rows));
    }

    /// <summary>The columns a child read may filter on besides its parent column (a note read's owner, creator and time).</summary>
    private static readonly HashSet<string> ChildFilterColumns = ["owninguser", "owningteam", "createdby", "createdon"];

    /// <summary>The answer to a child read, or <c>null</c> when <paramref name="query"/> is not one (the directory answers it).
    /// A child read's first condition is its parent column; any other condition or filter is on
    /// <see cref="ChildFilterColumns"/>, and is applied to the child's <see cref="CascadeChildColumnValues"/>.</summary>
    private Task<EntityCollection>? CascadeChildAnswer(QueryExpression query)
    {
        if (query.Criteria.Conditions.Count == 0)
            return null;

        var condition = query.Criteria.Conditions[0];
        if (condition.Operator != ConditionOperator.Equal
            || string.Equals(condition.AttributeName, query.EntityName + "id", StringComparison.OrdinalIgnoreCase)
            || !CascadeChildColumns.Contains((query.EntityName, condition.AttributeName)))
        {
            return null;
        }

        var rest = new FilterExpression(LogicalOperator.And);
        foreach (var other in query.Criteria.Conditions.Skip(1))
            rest.Conditions.Add(other);
        foreach (var filter in query.Criteria.Filters)
            rest.Filters.Add(filter);
        if (!OnlyOn(rest))
            return null;

        CascadeChildQueries.Add(query);
        if (CascadeChildFaults.TryGetValue(query.EntityName, out var fault))
            return Task.FromException<EntityCollection>(fault);

        if (CascadeChildNoAnswer.Contains(query.EntityName))
            return Task.FromResult<EntityCollection>(null!);

        var parent = (Guid)condition.Values.Single();
        var rows = CascadeChildren
            .Where(c => c.Table == query.EntityName && c.Column == condition.AttributeName && c.Parent == parent)
            .Where(c => Matches(rest, CascadeChildColumnValues.GetValueOrDefault(c.Child) ?? []))
            .Select(c =>
            {
                var row = new Entity(c.Table, c.Child);
                if (c.ActivityType is { } type)
                    row["activitytypecode"] = type;
                return row;
            });
        if (query.TopCount is { } top)
            rows = rows.Take(top);
        return Task.FromResult(new EntityCollection(rows.ToList()));
    }

    private static bool OnlyOn(FilterExpression filter) =>
        filter.Conditions.All(c => ChildFilterColumns.Contains(c.AttributeName)) && filter.Filters.All(OnlyOn);

    /// <summary>Dataverse's reading of a filter over one row: <c>ne</c> never matches an empty column (<c>null</c> does).</summary>
    private static bool Matches(FilterExpression filter, IReadOnlyDictionary<string, object> columns)
    {
        var results = filter.Conditions.Select(c => Matches(c, columns)).Concat(filter.Filters.Select(f => Matches(f, columns)));
        return filter.FilterOperator == LogicalOperator.Or ? results.Any(r => r) : results.All(r => r);
    }

    private static bool Matches(ConditionExpression condition, IReadOnlyDictionary<string, object> columns)
    {
        var value = columns.GetValueOrDefault(condition.AttributeName) switch
        {
            EntityReference reference => reference.Id,
            var other => other,
        };
        return condition.Operator switch
        {
            ConditionOperator.Null => value is null,
            ConditionOperator.NotNull => value is not null,
            ConditionOperator.Equal => value is not null && value.Equals(condition.Values.Single()),
            ConditionOperator.NotEqual => value is not null && !value.Equals(condition.Values.Single()),
            ConditionOperator.GreaterEqual => value is DateTime when && when >= (DateTime)condition.Values.Single(),
            _ => throw new NotSupportedException($"Test: a child read filters with {condition.Operator}."),
        };
    }
}
