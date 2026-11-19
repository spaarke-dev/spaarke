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
/// a relationship the allow-list does not accept; an accepted relationship is never read per row; every activity relationship is
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
        RecordCreationService.AcceptedAssignCascades.Should().OnlyContain(e => e.Value.Contains("Never read per row"));
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
        World.CascadeChildQueries.Should().OnlyContain(q => q.TopCount == 1);
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
    // AC3 (must fire, owner Q2): an event and an analysis with a note are re-owned; the note is never read per row
    // =====================================================================================

    [Fact]
    public async Task AC3_AnEventWithANote_IsReOwned_AndTheNoteRelationshipIsNeverRead()
    {
        Inventory("sprk_event", EventInventory);
        var id = World.EventUnderTheMatter(clientName: null, practiceArea: null, priority: null, indexed: true);
        World.AddCascadeChild("annotation", "objectid", id);

        var outcome = await World.RepairAsync("sprk_event", id);

        outcome.Status.Should().Be(RecordRepairStatus.Repaired);
        World.Writes.Should().ContainSingle().Which.Fields["ownerid"]
            .Should().Be(new EntityReference("team", Directory.ChildTeam), "the owner write is issued; Dataverse moves the note with it");
        World.CascadeChildQueries.Should().BeEmpty("sprk_event->annotation is accepted (owner Q2)");
        outcome.Fields.Should().NotContain(f => f.ReasonCode == RecordRepairReason.OwnerAssignCascades);
    }

    [Fact]
    public async Task AC3_AnAnalysisWithANote_IsReOwned_AndTheNoteRelationshipIsNeverRead()
    {
        Inventory("sprk_analysis", AnalysisInventory);
        var id = AnalysisUnderTheMatter();
        World.AddCascadeChild("annotation", "objectid", id);

        var outcome = await World.RepairAsync("sprk_analysis", id);

        outcome.Status.Should().Be(RecordRepairStatus.Repaired);
        World.Writes.Should().ContainSingle().Which.Fields.Should().ContainKey("ownerid");
        ChildTablesNamed(World).Should().Equal(["activitypointer", "activitypointer"],
            "the note and the analysis content are accepted; only the activities are read (the check, then again after the write)");
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

    /// <summary>The (table, column) pairs this world answers as a child read (registered by <see cref="CascadesTo"/>).</summary>
    public HashSet<(string Table, string Column)> CascadeChildColumns { get; } = [];

    /// <summary>Per child table, the fault a child read throws.</summary>
    public Dictionary<string, Exception> CascadeChildFaults { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Child tables whose read comes back with no answer.</summary>
    public HashSet<string> CascadeChildNoAnswer { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every child read Repair sent, in order.</summary>
    public List<QueryExpression> CascadeChildQueries { get; } = [];

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

    /// <summary>The answer to a child read, or <c>null</c> when <paramref name="query"/> is not one (the directory answers it).</summary>
    private Task<EntityCollection>? CascadeChildAnswer(QueryExpression query)
    {
        if (query.Criteria.Conditions.Count != 1)
            return null;

        var condition = query.Criteria.Conditions[0];
        if (condition.Operator != ConditionOperator.Equal
            || string.Equals(condition.AttributeName, query.EntityName + "id", StringComparison.OrdinalIgnoreCase)
            || !CascadeChildColumns.Contains((query.EntityName, condition.AttributeName)))
        {
            return null;
        }

        CascadeChildQueries.Add(query);
        if (CascadeChildFaults.TryGetValue(query.EntityName, out var fault))
            return Task.FromException<EntityCollection>(fault);
        if (CascadeChildNoAnswer.Contains(query.EntityName))
            return Task.FromResult<EntityCollection>(null!);

        var parent = (Guid)condition.Values.Single();
        var rows = CascadeChildren
            .Where(c => c.Table == query.EntityName && c.Column == condition.AttributeName && c.Parent == parent)
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
}
