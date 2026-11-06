using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// spaarke-ontology-platform-r1 task 046 (D-21; uac-r2's answer D-113): the ONE server create of a work assignment —
/// <c>POST /api/v1/child-records/sprk_workassignment</c>, the REAL <see cref="ChildRecordEndpoints.CreateAsync"/> over the
/// REAL <c>OwnedChildWrite</c> core with the secure-create plan, the REAL resolver over <see cref="Directory"/> and the
/// caller's scripted Dataverse. The payload is the one the Create Work Assignment wizard builds
/// (<c>workAssignmentService.ts</c> <c>createWorkAssignment</c>). The isolated create under a SECURE matter is driven through
/// the same route in <c>SecureRootInheritanceWriterTests.ChildRecordRoute.cs</c> (provisioning's fixture).
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid WaRecordTypeMatter = Guid.Parse("a0460000-0000-4000-8000-000000000001");
    private static readonly Guid WaLawFirm = Guid.Parse("a0460000-0000-4000-8000-000000000002");

    /// <summary>
    /// The wizard's create payload for a work assignment regarding <paramref name="matter"/>. Quoted from the wizard:
    /// <c>applyResolverFields</c> (PolymorphicResolverService.ts) binds the regarding lookup, sets
    /// <c>entity['sprk_regardingrecordid']</c>, then binds the pair TYPE —
    /// <c>entity[`${rtNavProp}@odata.bind`] = `/sprk_recordtype_refs(${recordType.id})`</c>, the catalog row that
    /// <c>resolveRecordType</c> finds (<c>sprk_recordlogicalname eq '&lt;table&gt;' and statecode eq 0</c>). The live catalog has an
    /// active row for matter, project, event and invoice (spaarkedev1, read 2026-10-10), so the wizard sends a TYPED pair for
    /// all four. The caller reads that catalog row (the core's pair rule), so the scripted catalog knows it.
    /// </summary>
    private Dictionary<string, object?> WizardWorkAssignment(Guid matter)
    {
        _user.RecordTypes[WaRecordTypeMatter] = "sprk_matter";
        return WizardWorkAssignmentPayload(matter);
    }

    private static Dictionary<string, object?> WizardWorkAssignmentPayload(Guid matter) => new()
    {
        ["sprk_name"] = "Review the lease",
        ["sprk_priority"] = 100000001,
        ["sprk_description"] = "Second draft",
        ["sprk_responseduedate"] = "2026-10-20",
        ["sprk_searchindexname"] = "spaarke-files-index",
        ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({matter:D})",
        ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({WaRecordTypeMatter:D})",
        ["sprk_regardingrecordid"] = matter.ToString("D"),
        ["sprk_regardingrecordname"] = "Lease dispute",
        ["sprk_regardingrecordurl"] = $"https://org/main.aspx?etn=sprk_matter&id={matter:D}",
        ["sprk_AssignedLawFirm1@odata.bind"] = $"/sprk_organizations({WaLawFirm:D})",
    };

    [Fact]
    public async Task WorkAssignmentCreate_TheWizardsPayloadUnderAnOrdinaryMatter_IsCreatedByTheApp_OwnedByTheMattersTeam_ForTheCaller()
    {
        var result = await CreateChild("sprk_workassignment", WizardWorkAssignment(OrdinaryMatter));

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        _user.Posts.Should().BeEmpty("a work assignment is never created as, or owned by, the user (WP-3)");
        var (table, id, fields) = _appCreates.Should().ContainSingle().Subject;
        table.Should().Be("sprk_workassignment");
        Owner(fields).Should().Be(Directory.ChildTeam, "record-first (I-6): the regarding matter's business-unit team");
        fields.Should().ContainKey("sprk_CreatedByPerson@odata.bind")
            .WhoseValue?.ToString().Should().Be($"/systemusers({Caller:D})",
            "createdby is the application; the person who asked is recorded");
        Bind(fields, "sprk_RegardingMatter@odata.bind").Should().Be($"/sprk_matters({OrdinaryMatter:D})");
        Bind(fields, "sprk_RegardingRecordType@odata.bind").Should().Be($"/sprk_recordtype_refs({WaRecordTypeMatter:D})");
        Bind(fields, "sprk_AssignedLawFirm1@odata.bind").Should().Be($"/sprk_organizations({WaLawFirm:D})");
        foreach (var column in new[]
                 {
                     "sprk_name", "sprk_priority", "sprk_description", "sprk_responseduedate",
                     "sprk_regardingrecordid", "sprk_regardingrecordname", "sprk_regardingrecordurl",
                 })
        {
            fields.Should().ContainKey(column, "every field the wizard sets is written as given (parity table)");
        }

        // Task 039 (FR-38; was: the wizard's index name is written as given): the server owns both index columns, so the
        // wizard's value is dropped and the owning unit (which has no index in this world) supplies none.
        fields.Should().NotContainKey("sprk_searchindexname", "the search index is the server's, never the client's (FR-38)");
        fields.Keys.Should().NotContain(k => k.StartsWith("sprk_ai_search_index", StringComparison.OrdinalIgnoreCase));

        fields.Keys.Should().NotContain(k => k.StartsWith("sprk_AssignedToInternal", StringComparison.OrdinalIgnoreCase),
            "wizard parity: the chat tool's 'for person' default is not added by this route");
        fields.Should().NotContainKey("sprk_issecure", "filed under an ordinary matter: an ordinary row");
        Created(result).Should().Be(id);
        CreatedBody(result).TryGetProperty("warnings", out _).Should().BeFalse("nothing was left undone");
    }

    [Fact]
    public async Task WorkAssignmentCreate_WithoutAppendToOnTheRegardingMatter_IsTheSame404AsAMissingMatter_AndNothingIsCreated()
    {
        var missingMatter = Guid.Parse("a0460000-0000-4000-8000-0000000000f2");
        _user.NoAppendTo.Add(OrdinaryMatter);
        _user.Missing.Add(missingMatter);

        var denied = await CreateChild("sprk_workassignment", WizardWorkAssignment(OrdinaryMatter));
        var missing = await CreateChild("sprk_workassignment", WizardWorkAssignment(missingMatter));

        Status(denied).Should().Be(StatusCodes.Status404NotFound, "AppendTo on the regarding record is asked as the caller");
        ReasonCode(denied).Should().Be(ChildRecordEndpoints.NotFoundCode);
        (Status(denied), ReasonCode(denied), Detail(denied)).Should().Be((Status(missing), ReasonCode(missing), Detail(missing)),
            "a record the caller cannot append to is not disclosed");
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task WorkAssignmentCreate_WithoutTheCreatePrivilege_Is403_AndNothingIsCreated()
    {
        _user.Held.Remove("prvCreatesprk_workassignment");

        var result = await CreateChild("sprk_workassignment", WizardWorkAssignment(OrdinaryMatter));

        Status(result).Should().Be(StatusCodes.Status403Forbidden);
        ReasonCode(result).Should().Be(ChildRecordEndpoints.DeniedCode);
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    /// <summary>
    /// Provisioning's own column (the container) is not field-secured, so the route refuses it for a work assignment; the
    /// secure flag and the access record are refused by the secure-create plan (task 175) — D-113: neither the wizard nor a
    /// decision may send them. Every refusal writes nothing.
    /// </summary>
    [Theory]
    [InlineData("sprk_containerid", "b!container-of-someone-else", ChildRecordEndpoints.DeniedCode)]
    [InlineData("sprk_issecure", true, AccessFollowsParent.SecureFlagReasonCode)]
    [InlineData("sprk_accessinheritance", "{}", AccessInheritance.ServerOnlyReasonCode)]
    [InlineData("sprk_accesspermission", 100000000, ChildRecordEndpoints.DeniedCode)]
    public async Task WorkAssignmentCreate_NamingAColumnSpaarkeOwnsOnAWorkAssignment_Is403_AndNothingIsCreated(
        string column, object value, string reasonCode)
    {
        var payload = WizardWorkAssignment(OrdinaryMatter);
        payload[column] = value;

        var result = await CreateChild("sprk_workassignment", payload);

        Status(result).Should().Be(StatusCodes.Status403Forbidden, Detail(result));
        ReasonCode(result).Should().Be(reasonCode);
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task WorkAssignmentCreate_UnderAMatterWhoseSecureFlagCannotBeRead_Is409_AndNothingIsCreated()
    {
        // Task 158 (owner round 17 item 3): an EMPTY parent flag is never "not secure" — the plan refuses before any write.
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Microsoft.Xrm.Sdk.Query.QueryExpression q, CancellationToken _) =>
                q.EntityName == "sprk_matter"
                    ? new EntityCollection(new List<Entity> { new("sprk_matter", OrdinaryMatter) })
                    : new EntityCollection());

        var result = await CreateChild("sprk_workassignment", WizardWorkAssignment(OrdinaryMatter),
            SecureRootFilingGateFixtures.Over(entities.Object));

        Status(result).Should().Be(StatusCodes.Status409Conflict);
        ReasonCode(result).Should().Be(RecordOwnerRefusal.ParentUndetermined);
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    // ── The ADR-024 pair on this route is decided by the core alone (uac-r2 #1612, CallerWriteAuthorization.PairTargetAsync) ──────────

    private static Dictionary<string, object?> PairOnly(Dictionary<string, object?> payload)
    {
        payload.Remove("sprk_RegardingMatter@odata.bind");
        return payload;
    }

    private static Dictionary<string, object?> Untyped(Dictionary<string, object?> payload)
    {
        payload.Remove("sprk_RegardingRecordType@odata.bind");
        return payload;
    }

    /// <summary>
    /// A TYPED pair naming a matter counts on a work assignment only when its typed lookup (<c>sprk_RegardingMatter</c>)
    /// repeats it (uac-r2 #1628, <c>CallerWriteAuthorization.PairTargetAsync</c>). A pair-only payload naming a secure matter the caller
    /// cannot append to, a missing one, and a typed lookup to one matter beside a pair naming another all answer the SAME
    /// uniform 400 <c>child_record.invalid_payload</c> — no answer depends on whether the record exists or is secure. (The
    /// AppendTo 404 on a repeated pair is <see cref="WorkAssignmentCreate_WithoutAppendToOnTheRegardingMatter_IsTheSame404AsAMissingMatter_AndNothingIsCreated"/>.)
    /// </summary>
    [Fact]
    public async Task WorkAssignmentCreate_ATypedPairNoLookupRepeats_IsTheSameUniform400_WhateverThatRecordIs()
    {
        var missing = Guid.Parse("a0460000-0000-4000-8000-0000000000f3");
        _user.NoAppendTo.Add(SecureMatter);
        _user.Missing.Add(missing);

        var answers = new List<IResult>
        {
            await CreateChild("sprk_workassignment", PairOnly(WizardWorkAssignment(SecureMatter))),
            await CreateChild("sprk_workassignment", PairOnly(WizardWorkAssignment(missing))),
        };
        // A typed lookup to an ordinary matter and a pair naming a secure one the caller cannot append to.
        var mismatched = WizardWorkAssignment(OrdinaryMatter);
        mismatched["sprk_regardingrecordid"] = SecureMatter.ToString("D");
        answers.Add(await CreateChild("sprk_workassignment", mismatched));

        foreach (var answer in answers)
        {
            Status(answer).Should().Be(StatusCodes.Status400BadRequest, Detail(answer));
            ReasonCode(answer).Should().Be(ChildRecordEndpoints.InvalidPayloadCode);
            ProblemJson(answer).Should().Be(ProblemJson(answers[0]), "no answer depends on what the pair names");
        }

        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    /// <summary>
    /// Since uac-r2 #1628 a pair-only work assignment (typed pair, no typed matter lookup) is refused even when the caller CAN
    /// append to the matter: a work assignment is filed under a matter only through <c>sprk_RegardingMatter</c>, and the pair
    /// must repeat it. Refused by the core (the route adds no rule), nothing created. The wizard always binds the lookup
    /// (<see cref="WorkAssignmentCreate_APairAgreeingWithTheTypedMatterLookup_IsStillAccepted"/>).
    /// </summary>
    [Fact]
    public async Task WorkAssignmentCreate_ATypedPairOnlyNamingAMatterTheCallerCanAppendTo_IsRefused400_TheLookupMustRepeatIt()
    {
        // The secure-create plan would accept (the pair's type is a matter; the matter is ordinary), so the refusal below is the
        // core's #1628 rule, not the plan.
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Microsoft.Xrm.Sdk.Query.QueryExpression q, CancellationToken _) => q.EntityName switch
            {
                "sprk_recordtype_ref" => new EntityCollection(new List<Entity>
                    { new("sprk_recordtype_ref", WaRecordTypeMatter) { ["sprk_recordlogicalname"] = "sprk_matter" } }),
                "sprk_matter" => new EntityCollection(new List<Entity>
                    { new("sprk_matter", OrdinaryMatter) { ["sprk_issecure"] = false } }),
                _ => new EntityCollection(),
            });

        var result = await CreateChild("sprk_workassignment", PairOnly(WizardWorkAssignment(OrdinaryMatter)),
            SecureRootFilingGateFixtures.Over(entities.Object));

        Status(result).Should().Be(StatusCodes.Status400BadRequest, Detail(result));
        ReasonCode(result).Should().Be(ChildRecordEndpoints.InvalidPayloadCode);
        _appCreates.Should().BeEmpty();
    }

    /// <summary>
    /// An UNTYPED pair is read only by agreement with a lookup in the row's agreement columns; one that agrees with none is the
    /// core's 400 <c>child_record.invalid_payload</c> — the SAME body whatever the record is (secure, missing, ordinary, or
    /// named by a typed matter lookup that disagrees), mapped by the route (<c>{ Invalid: ... }</c>), nothing created.
    /// </summary>
    [Fact]
    public async Task WorkAssignmentCreate_AnUntypedPairAgreeingWithNoLookup_IsTheSame400_WhateverThatRecordIs()
    {
        var missing = Guid.Parse("a0460000-0000-4000-8000-0000000000f5");
        _user.NoAppendTo.Add(SecureMatter);
        _user.Missing.Add(missing);

        var answers = new List<IResult>
        {
            await CreateChild("sprk_workassignment", Untyped(PairOnly(WizardWorkAssignment(SecureMatter)))),
            await CreateChild("sprk_workassignment", Untyped(PairOnly(WizardWorkAssignment(missing)))),
            await CreateChild("sprk_workassignment", Untyped(PairOnly(WizardWorkAssignment(OrdinaryMatter)))),
        };
        var mismatched = Untyped(WizardWorkAssignment(OrdinaryMatter));
        mismatched["sprk_regardingrecordid"] = SecureMatter.ToString("D");
        answers.Add(await CreateChild("sprk_workassignment", mismatched));

        foreach (var answer in answers)
        {
            Status(answer).Should().Be(StatusCodes.Status400BadRequest, Detail(answer));
            ReasonCode(answer).Should().Be(ChildRecordEndpoints.InvalidPayloadCode);
            ProblemJson(answer).Should().Be(ProblemJson(answers[0]), "no answer depends on what the pair names");
        }

        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    /// <summary>
    /// uac-r2 ask (b), the same-id contact exploit: a contact whose id equals a secure matter's, bound in a NON-regarding
    /// lookup (<c>sprk_AssignedAttorney1</c>), and the matter named only by the pair. The caller may append to the contact but
    /// not to the matter. Refused by the core alone (the route has no pair rule): TYPED as a matter, no matter lookup repeats
    /// the pair (uac-r2 #1628); UNTYPED, the contact is not an agreement column. Both are 400
    /// <c>child_record.invalid_payload</c>; nothing is created either way.
    /// </summary>
    [Theory]
    [InlineData(true, StatusCodes.Status400BadRequest, ChildRecordEndpoints.InvalidPayloadCode)]
    [InlineData(false, StatusCodes.Status400BadRequest, ChildRecordEndpoints.InvalidPayloadCode)]
    public async Task WorkAssignmentCreate_APairWhoseIdOnlyMatchesANonRegardingLookup_IsRefusedByTheCoreAlone(
        bool typed, int status, string reasonCode)
    {
        _user.NoAppendToIn.Add(("sprk_matters", SecureMatter)); // may append to the CONTACT, not to the matter
        var payload = PairOnly(WizardWorkAssignment(SecureMatter));
        if (!typed)
            Untyped(payload);
        payload["sprk_AssignedAttorney1@odata.bind"] = $"/contacts({SecureMatter:D})"; // same GUID, a different table

        var result = await CreateChild("sprk_workassignment", payload);

        Status(result).Should().Be(status, Detail(result));
        ReasonCode(result).Should().Be(reasonCode);
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    /// <summary>
    /// The wizard's matter shape stays accepted — even when another (contact) lookup happens to carry the same id: the typed
    /// pair's record (the matter) is AppendTo-checked, and so is the contact.
    /// </summary>
    [Fact]
    public async Task WorkAssignmentCreate_APairAgreeingWithTheTypedMatterLookup_IsStillAccepted()
    {
        var payload = WizardWorkAssignment(OrdinaryMatter);
        payload["sprk_AssignedTo@odata.bind"] = $"/contacts({OrdinaryMatter:D})";

        var result = await CreateChild("sprk_workassignment", payload);

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        _appCreates.Should().ContainSingle();
    }

    /// <summary>
    /// uac-r2 ask (a): the wizard also offers an invoice and an event. For those <c>applyResolverFields</c> binds the work
    /// assignment's <c>sprk_RegardingInvoice</c> / <c>sprk_RegardingEvent</c>, sets the pair id to the same id, and binds the
    /// pair TYPE to the invoice's / event's catalog row — exactly this payload (no matter lookup). The pair is TYPED, so the
    /// core AppendTo-checks the record in the type's table as the caller and the create is accepted (201), with no route rule.
    /// </summary>
    [Theory]
    [InlineData("sprk_RegardingInvoice", "sprk_invoices", "sprk_invoice")]
    [InlineData("sprk_RegardingEvent", "sprk_events", "sprk_event")]
    public async Task WorkAssignmentCreate_TheWizardsInvoiceOrEventShape_TypedPair_IsCreatedThroughTheCore_201(
        string navigation, string entitySet, string table)
    {
        var record = Guid.Parse("a0460000-0000-4000-8000-0000000000a1");
        var recordType = Guid.Parse("a0460000-0000-4000-8000-0000000000a3");
        _world.WithRecord(table, record, Directory.ChildBu, owningTeam: Directory.ChildTeam); // record-first owner (I-6)
        _user.RecordTypes[recordType] = table; // the caller reads the catalog row (the core's pair rule)
        var payload = PairOnly(WizardWorkAssignment(OrdinaryMatter));
        payload["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({recordType:D})";
        payload[$"{navigation}@odata.bind"] = $"/{entitySet}({record:D})";
        payload["sprk_regardingrecordid"] = record.ToString("D");
        payload["sprk_regardingrecordurl"] = $"https://org/main.aspx?etn={table}&id={record:D}";

        // The plan reads the pair's type (the wizard binds the invoice's / event's catalog row) and, finding a table that is
        // not an access parent, names nothing to check.
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Microsoft.Xrm.Sdk.Query.QueryExpression q, CancellationToken _) =>
                q.EntityName == "sprk_recordtype_ref"
                    ? new EntityCollection(new List<Entity> { new("sprk_recordtype_ref", recordType) { ["sprk_recordlogicalname"] = table } })
                    : new EntityCollection());

        var result = await CreateChild("sprk_workassignment", payload, SecureRootFilingGateFixtures.Over(entities.Object));

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        var fields = _appCreates.Should().ContainSingle().Subject.Fields;
        Bind(fields, $"{navigation}@odata.bind").Should().Be($"/{entitySet}({record:D})");
        Owner(fields).Should().Be(Directory.ChildTeam, "record-first (I-6): the regarding record's business-unit team");
        fields.Should().NotContainKey("sprk_RegardingMatter@odata.bind");
        fields.Should().ContainKey("sprk_regardingrecordid");
    }

    /// <summary>
    /// The same typed invoice shape, but the caller holds no AppendTo on the invoice: the core's check on the typed pair (and on
    /// the invoice lookup) is the uniform 404 — byte-identical to the shape naming an invoice that does not exist.
    /// </summary>
    [Fact]
    public async Task WorkAssignmentCreate_TheWizardsInvoiceShape_WithoutAppendToOnTheInvoice_IsTheUniform404()
    {
        var invoice = Guid.Parse("a0460000-0000-4000-8000-0000000000a2");
        var absent = Guid.Parse("a0460000-0000-4000-8000-0000000000a4");
        var recordType = Guid.Parse("a0460000-0000-4000-8000-0000000000a3");
        _user.RecordTypes[recordType] = "sprk_invoice";
        _user.NoAppendTo.Add(invoice);
        _user.Missing.Add(absent);

        async Task<IResult> InvoiceShape(Guid id)
        {
            var payload = PairOnly(WizardWorkAssignment(OrdinaryMatter));
            payload["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({recordType:D})";
            payload["sprk_RegardingInvoice@odata.bind"] = $"/sprk_invoices({id:D})";
            payload["sprk_regardingrecordid"] = id.ToString("D");
            return await CreateChild("sprk_workassignment", payload);
        }

        var denied = await InvoiceShape(invoice);
        var missing = await InvoiceShape(absent);

        Status(denied).Should().Be(StatusCodes.Status404NotFound, Detail(denied));
        ProblemJson(denied).Should().Be(ProblemJson(missing));
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    /// <summary>
    /// Fallback shape, documented: when the catalog has no ACTIVE row for the invoice, <c>resolveRecordType</c> returns null and
    /// the wizard logs a warning and sends the pair UNTYPED (invoice lookup + pair id, no type). The core accepts it (the invoice
    /// lookup carries the pair's id), and since uac-r2 #1643 the secure-create plan files the work assignment under the invoice
    /// lookup itself: an invoice that does not resolve is the plan's 409 <c>record_owner_parent_unresolved</c>, nothing written;
    /// an existing invoice is created, owned record-first (I-6) by the invoice's business-unit team.
    /// </summary>
    [Fact]
    public async Task WorkAssignmentCreate_TheWizardsInvoiceShape_UntypedBecauseTheCatalogRowIsMissing_IsFiledUnderTheInvoice()
    {
        var invoice = Guid.Parse("a0460000-0000-4000-8000-0000000000a2");
        var absent = Guid.Parse("a0460000-0000-4000-8000-0000000000a4");
        _world.WithRecord("sprk_invoice", invoice, Directory.ChildBu, owningTeam: Directory.ChildTeam);

        var unresolved = await CreateChild("sprk_workassignment", UntypedInvoiceShape(absent));
        Status(unresolved).Should().Be(StatusCodes.Status409Conflict, Detail(unresolved));
        ReasonCode(unresolved).Should().Be(RecordOwnerRefusal.ParentUnresolved);
        _appCreates.Should().BeEmpty();

        var created = await CreateChild("sprk_workassignment", UntypedInvoiceShape(invoice));
        Status(created).Should().Be(StatusCodes.Status201Created, Detail(created));
        Owner(_appCreates.Should().ContainSingle().Subject.Fields).Should().Be(Directory.ChildTeam,
            "record-first (I-6): the invoice's business-unit team");

        Dictionary<string, object?> UntypedInvoiceShape(Guid id)
        {
            var payload = Untyped(PairOnly(WizardWorkAssignment(OrdinaryMatter)));
            payload["sprk_RegardingInvoice@odata.bind"] = $"/sprk_invoices({id:D})";
            payload["sprk_regardingrecordid"] = id.ToString("D");
            return payload;
        }
    }

    /// <summary>
    /// Defence in depth: the plan's own AppendTo refusal on a secure parent (<c>AccessDenied</c>) is answered with the
    /// route's uniform not-found — byte-identical to a create filed under a record that does not exist.
    /// </summary>
    [Fact]
    public async Task PlanRefusal_AccessDenied_IsByteIdenticalToTheMissingParent404()
    {
        var missing = Guid.Parse("a0460000-0000-4000-8000-0000000000f4");
        _user.Missing.Add(missing);
        var missingParent = await CreateChild("sprk_workassignment", WizardWorkAssignment(missing));

        var planRefusal = ChildRecordEndpoints.PlanRefused(
            RecordOwnerResolution.Refused(DataverseUserClientErrorCodes.AccessDenied, "no AppendTo on the secure matter"),
            "sprk_workassignment", "trace-1");

        Status(missingParent).Should().Be(StatusCodes.Status404NotFound);
        ProblemJson(planRefusal).Should().Be(ProblemJson(missingParent),
            "a caller without AppendTo on a secure parent learns nothing a missing parent would not tell them");
        _appCreates.Should().BeEmpty();
    }

    /// <summary>
    /// NFR-10 / D-29: a caller Dataverse cannot name is #1312's single 403, before the payload or any record it binds is
    /// asked about. A to-do on the same route gets the same answer (one create route, one rule).
    /// </summary>
    [Theory]
    [InlineData("sprk_workassignment", 403, DataverseUserClientErrorCodes.AccessDenied)]
    [InlineData("sprk_workassignment", 401, DataverseUserClientErrorCodes.AccessDenied)]
    [InlineData("sprk_workassignment", 0, DataverseUserClientErrorCodes.UserContextRequired)]
    [InlineData("sprk_workassignment", 0, DataverseUserClientErrorCodes.OboExchangeFailed)]
    [InlineData("sprk_todo", 403, DataverseUserClientErrorCodes.AccessDenied)]
    public async Task Create_ForACallerWithNoDataverseIdentity_IsTheSingle403_BeforeAnythingElseIsAsked(
        string table, int whoAmIStatus, string errorCode)
    {
        var unresolved = UserClientAnsweringWhoAmI(DataverseUserResponse.Fail(whoAmIStatus, errorCode, "who?"));

        var result = await CreateChild(table, WizardWorkAssignment(OrdinaryMatter), user: unresolved.Object);

        Status(result).Should().Be(StatusCodes.Status403Forbidden);
        ReasonCode(result).Should().Be("sdap.access.deny.caller_unresolved");
        unresolved.Verify(u => u.GetAsync(It.Is<string>(p => p != "WhoAmI()"), It.IsAny<CancellationToken>()), Times.Never(),
            "nothing about the payload or its records is asked for a caller who cannot be named");
        _appCreates.Should().BeEmpty();
    }

    /// <summary>Control: throttling or a Dataverse fault says nothing about WHO the caller is — their own error, not the 403.</summary>
    [Theory]
    [InlineData(429, DataverseUserClientErrorCodes.RateLimited)]
    [InlineData(503, DataverseUserClientErrorCodes.ServiceError)]
    public async Task Create_WhenWhoAmIIsThrottledOrFaults_IsNotTheUnresolvedCaller403(int whoAmIStatus, string errorCode)
    {
        var faulting = UserClientAnsweringWhoAmI(DataverseUserResponse.Fail(whoAmIStatus, errorCode, "busy"));

        var result = await CreateChild("sprk_workassignment", WizardWorkAssignment(OrdinaryMatter), user: faulting.Object);

        Status(result).Should().Be(whoAmIStatus);
        ReasonCode(result).Should().NotBe("sdap.access.deny.caller_unresolved");
        _appCreates.Should().BeEmpty();
    }

    /// <summary>
    /// AC 3 (task 043): a decision's Assign Work follow-on calls the same handler IN-PROCESS through
    /// <see cref="DecisionRouteCores.CreateChildAsync"/> with the commit route's own request, and gets the new work
    /// assignment's id back for <c>sprk_followons</c>.
    /// </summary>
    [Fact]
    public async Task WorkAssignmentCreate_InProcessThroughDecisionRouteCores_ReturnsTheCreatedId()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDataverseUserClient>(_user);
        // Task 017: the route's create takes the ONE server create, built over this harness's collaborators.
        services.AddSingleton(RouteRecordCreation.Over(
            _world.Resolver(), _appOnly.Object, Restamper(), Shares(), SecureRootFilingGateFixtures.NothingSecure()));
        var http = HttpContextOfCaller();
        http.RequestServices = services.BuildServiceProvider();

        var reply = await new DecisionRouteCores().CreateChildAsync(
            http, "sprk_workassignment", JsonSerializer.SerializeToElement(WizardWorkAssignment(OrdinaryMatter)),
            CancellationToken.None);

        reply.Status.Should().Be(StatusCodes.Status201Created, reply.Detail);
        var created = _appCreates.Should().ContainSingle().Subject;
        created.Table.Should().Be("sprk_workassignment");
        reply.CreatedId.Should().Be(created.Id, "the decision records the new work assignment as its follow-on");
    }

    /// <summary>
    /// I-12 L1 on this route: after a work assignment is created, its "Assigned *" contacts' access is materialized INLINE
    /// (the wizard no longer calls <c>/assigned-access/sync</c>) — the REAL materializer, over uac-r2's harness, is asked
    /// about the new row; its root read is made to fail so the outcome is observable in its log. A to-do (not a root) is not
    /// materialized.
    /// </summary>
    [Fact]
    public async Task WorkAssignmentCreate_RunsTheAssignedToMaterializerInline_ForTheNewRow_AndNotForAChild()
    {
        var log = new CapturedMaterializerLog();
        var harness = new Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles.Harness { Logger = log };
        harness.Store.FailRootRead = true;
        var services = new ServiceCollection();
        services.AddScoped(_ => harness.Materializer);
        var scopes = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var workAssignment = await CreateChild("sprk_workassignment", WizardWorkAssignment(OrdinaryMatter), scopes: scopes);
        var todo = await CreateChild("sprk_todo", new() { ["sprk_name"] = "Call back" }, scopes: scopes);

        Status(workAssignment).Should().Be(StatusCodes.Status201Created, Detail(workAssignment));
        Status(todo).Should().Be(StatusCodes.Status201Created, Detail(todo));
        var waId = Created(workAssignment);
        log.Lines.Should().Contain(l => l.Contains(waId.ToString(), StringComparison.OrdinalIgnoreCase),
            "the materializer was asked about the new work assignment in the request");
        log.Lines.Should().NotContain(l => l.Contains(Created(todo).ToString(), StringComparison.OrdinalIgnoreCase),
            "a child is not an Assigned-To root");
    }

    // ── Harness ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A caller's Dataverse whose WhoAmI answers <paramref name="whoAmI"/>; any other question is recorded.</summary>
    private static Mock<IDataverseUserClient> UserClientAnsweringWhoAmI(DataverseUserResponse whoAmI)
    {
        var client = new Mock<IDataverseUserClient>(MockBehavior.Loose);
        client.Setup(u => u.GetAsync("WhoAmI()", It.IsAny<CancellationToken>())).ReturnsAsync(whoAmI);
        return client;
    }

    /// <summary>The materializer's log lines (formatted).</summary>
    private sealed class CapturedMaterializerLog : Microsoft.Extensions.Logging.ILogger<Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer>
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Lines.Enqueue(formatter(state, exception));
    }

    /// <summary>The ProblemDetails as the client receives it (status, title, detail, reasonCode).</summary>
    private static string ProblemJson(IResult result) =>
        JsonSerializer.Serialize(((Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult)result).ProblemDetails);

    private static JsonElement CreatedBody(IResult result) =>
        JsonSerializer.SerializeToElement(((IValueHttpResult)result).Value);

    private static Guid Created(IResult result) => CreatedBody(result).GetProperty("id").GetGuid();
}
