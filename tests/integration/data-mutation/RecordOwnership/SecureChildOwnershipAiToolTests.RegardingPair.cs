using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// The ADR-024 regarding pair (<c>sprk_regardingrecordid</c> + <c>sprk_RegardingRecordType@odata.bind</c>) on the shared
/// write core (<c>OwnedChildWrite</c>; defect reported by spaarke-ontology-platform-r1 task 046's review). The pair files a
/// row under a record exactly as a lookup does (CoreAncestorResolver stamps from it), but it is TEXT, so neither the mapper
/// nor Dataverse checks it. Before the fix, the core asked AppendTo on the record it names only when that record was SECURE
/// (the secure-create plan's own guard): a pair-only payload naming a missing record, or an ordinary record the caller cannot
/// see, was created, and only a SECURE unreadable one was refused — an existence-and-secure oracle. Now the pair's record is
/// checked AS THE CALLER like any lookup: missing, unreadable and unappendable all answer the same not-found.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid AbsentMatter = Guid.Parse("a2470000-0000-4000-8000-0000000000d1");

    private void CatalogKnowsTheMatterType() => _user.RecordTypes[RecordTypeMatter] = "sprk_matter";

    /// <summary>A to-do filed ONLY by the pair (no typed regarding lookup), under <paramref name="matter"/>.</summary>
    private static Dictionary<string, object?> PairOnlyToDo(Guid matter) => new()
    {
        ["sprk_name"] = "Call back",
        ["sprk_regardingrecordid"] = matter.ToString("D"),
        ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({RecordTypeMatter:D})",
    };

    /// <summary>The response as the client receives it: status and the serialized problem body.</summary>
    private static async Task<(int Status, string Body)> WireOf(IResult result)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
            TraceIdentifier = "trace",
        };
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    // ── (a) (b) (c): the oracle — missing, ordinary-unreadable and secure-unreadable answer byte-identically ──────────

    [Fact]
    public async Task ChildCreate_PairOnly_ToAMissingAnUnreadableOrASecureUnreadableMatter_IsOneByteIdentical404_AndNothingIsCreated()
    {
        CatalogKnowsTheMatterType();
        _user.Missing.Add(AbsentMatter);           // (a) does not exist
        _user.NoAppendTo.Add(OrdinaryMatter);      // (b) an ordinary matter the caller cannot see
        _user.NoAppendTo.Add(SecureMatter);        // (c) a secure matter the caller cannot see

        var missing = await WireOf(await CreateChild("sprk_todo", PairOnlyToDo(AbsentMatter)));
        var ordinary = await WireOf(await CreateChild("sprk_todo", PairOnlyToDo(OrdinaryMatter)));
        var secure = await WireOf(await CreateChild("sprk_todo", PairOnlyToDo(SecureMatter)));

        missing.Status.Should().Be(StatusCodes.Status404NotFound, missing.Body);
        ordinary.Should().Be(missing, "an ordinary record the caller cannot append to answers as a missing one");
        secure.Should().Be(missing, "a secure record the caller cannot append to answers as a missing one — no oracle");
        missing.Body.Should().Contain(ChildRecordEndpoints.NotFoundCode)
            .And.NotContain(AbsentMatter.ToString("D")).And.NotContain(SecureMatter.ToString("D"));
        _appCreates.Should().BeEmpty("nothing is filed under a record the caller may not append to");
        _user.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildCreate_PairOnly_IsTheSame404_AsAnUnappendableTypedLookup()
    {
        CatalogKnowsTheMatterType();
        _user.NoAppendTo.Add(OrdinaryMatter);

        var byPair = await WireOf(await CreateChild("sprk_todo", PairOnlyToDo(OrdinaryMatter)));
        var byLookup = await WireOf(await CreateChild("sprk_todo", new()
        {
            ["sprk_name"] = "Call back",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
        }));

        byPair.Should().Be(byLookup, "the pair is checked like any lookup — the same uniform not-found");
        _appCreates.Should().BeEmpty();
    }

    // ── (d) an appendable target is created ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChildCreate_PairOnly_ToAnAppendableMatter_IsCreated_WithThePairAsWritten()
    {
        CatalogKnowsTheMatterType();

        var result = await CreateChild("sprk_todo", PairOnlyToDo(OrdinaryMatter));

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        var fields = _appCreates.Should().ContainSingle().Subject.Fields;
        ((JsonElement)fields["sprk_regardingrecordid"]!).GetString().Should().Be(OrdinaryMatter.ToString("D"));
    }

    [Fact]
    public async Task ChildCreate_ThePairRepeatingTheTypedRegardingLookup_IsCreated_AndThePairIsNotAskedTwice()
    {
        // The regarding builders' shape (applyResolverFields): the typed lookup and the pair name the same record.
        CatalogKnowsTheMatterType();

        var result = await CreateChild("sprk_todo", new()
        {
            ["sprk_name"] = "Call back",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            ["sprk_regardingrecordid"] = OrdinaryMatter.ToString("D"),
            ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({RecordTypeMatter:D})",
        });

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        _appCreates.Should().ContainSingle();
    }

    [Fact]
    public async Task ChildCreate_APairIdWithNoType_RepeatingTheTypedLookup_IsCreated()
    {
        // The email draft's shape, and a resolver payload whose type is missing from the catalog: no type, the id repeats
        // the typed lookup (which was checked).
        var result = await CreateChild("sprk_todo", new()
        {
            ["sprk_name"] = "Call back",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            ["sprk_regardingrecordid"] = OrdinaryMatter.ToString("D"),
        });

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
    }

    // ── (e) a pair that disagrees with the typed lookup: the loser's record is checked too ────────────────────────

    /// <summary>
    /// The typed lookup names an appendable matter and the pair a DIFFERENT one. Both readers count both (secure-if-any), so
    /// both cost AppendTo: a pair naming a missing, unreadable or secure-unreadable matter is the same uniform 404 even with
    /// an appendable typed lookup beside it, and a pair naming an appendable one is created.
    /// </summary>
    [Fact]
    public async Task ChildCreate_APairNamingADifferentMatterThanTheTypedLookup_IsCheckedToo_OneByteIdentical404()
    {
        CatalogKnowsTheMatterType();
        var appendable = Guid.Parse("a2470000-0000-4000-8000-0000000000d2");
        _world.WithOrdinaryRoot("sprk_matter", appendable);
        _user.Missing.Add(AbsentMatter);
        _user.NoAppendTo.Add(SecureMatter);

        Dictionary<string, object?> Mismatched(Guid pairMatter) => new()
        {
            ["sprk_name"] = "Call back",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})", // appendable
            ["sprk_regardingrecordid"] = pairMatter.ToString("D"),
            ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({RecordTypeMatter:D})",
        };

        var toSecure = await WireOf(await CreateChild("sprk_todo", Mismatched(SecureMatter)));
        var toMissing = await WireOf(await CreateChild("sprk_todo", Mismatched(AbsentMatter)));
        _appCreates.Should().BeEmpty("the typed lookup being appendable does not excuse the pair's record");
        var toAppendable = await CreateChild("sprk_todo", Mismatched(appendable));

        toSecure.Status.Should().Be(StatusCodes.Status404NotFound, toSecure.Body);
        toSecure.Body.Should().Contain(ChildRecordEndpoints.NotFoundCode);
        toMissing.Should().Be(toSecure);
        Status(toAppendable).Should().Be(StatusCodes.Status201Created, Detail(toAppendable));
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task ChildCreate_APairIdThatIsNotARecordId_Is400_AndNothingIsCreated(string pairId)
    {
        CatalogKnowsTheMatterType();

        var result = await CreateChild("sprk_todo", new()
        {
            ["sprk_name"] = "Call back",
            ["sprk_regardingrecordid"] = pairId,
            ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({RecordTypeMatter:D})",
        });

        Status(result).Should().Be(StatusCodes.Status400BadRequest);
        ReasonCode(result).Should().Be(ChildRecordEndpoints.InvalidPayloadCode);
        _appCreates.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildCreate_APairIdWithNoTypeThatRepeatsNoLookup_Is400_AndNothingIsCreated()
    {
        var result = await CreateChild("sprk_todo", new()
        {
            ["sprk_name"] = "Call back",
            ["sprk_regardingrecordid"] = SecureMatter.ToString("D"),
        });

        Status(result).Should().Be(StatusCodes.Status400BadRequest, "what an untyped pair names cannot be told, so it is never 'nothing'");
        ReasonCode(result).Should().Be(ChildRecordEndpoints.InvalidPayloadCode);
        _appCreates.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildCreate_ATypeThatNamesNoTable_Is400_AndNothingIsCreated()
    {
        // The catalog row is readable but names no table (RecordTypes has no entry for it).
        var result = await CreateChild("sprk_todo", PairOnlyToDo(OrdinaryMatter));

        Status(result).Should().Be(StatusCodes.Status400BadRequest);
        ReasonCode(result).Should().Be(ChildRecordEndpoints.InvalidPayloadCode);
        _appCreates.Should().BeEmpty();
    }

    // ── (f) the chat tool: dataverse.create_record ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateRecord_PairOnly_ToAMissingAnUnreadableOrASecureUnreadableMatter_AnswersIdentically_AndCreatesNothing()
    {
        CatalogKnowsTheMatterType();
        _user.Missing.Add(AbsentMatter);
        _user.NoAppendTo.Add(OrdinaryMatter);
        _user.NoAppendTo.Add(SecureMatter);

        (string? Code, string? Message, bool Success) Of(ToolResult r) => (r.ErrorCode, r.ErrorMessage, r.Success);
        Task<ToolResult> PairOnly(Guid matter) => CreateRecord("sprk_todo",
            ("sprk_name", JsonSerializer.SerializeToElement("Call back")),
            ("sprk_regardingrecordid", JsonSerializer.SerializeToElement(matter.ToString("D"))),
            Lookup("sprk_regardingrecordtype", "sprk_recordtype_ref", RecordTypeMatter));

        var missing = Of(await PairOnly(AbsentMatter));
        var ordinary = Of(await PairOnly(OrdinaryMatter));
        var secure = Of(await PairOnly(SecureMatter));

        missing.Success.Should().BeFalse();
        missing.Code.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        ordinary.Should().Be(missing);
        secure.Should().Be(missing);
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateRecord_PairOnly_ToAnAppendableMatter_IsCreated()
    {
        CatalogKnowsTheMatterType();

        var result = await CreateRecord("sprk_todo",
            ("sprk_name", JsonSerializer.SerializeToElement("Call back")),
            ("sprk_regardingrecordid", JsonSerializer.SerializeToElement(OrdinaryMatter.ToString("D"))),
            Lookup("sprk_regardingrecordtype", "sprk_recordtype_ref", RecordTypeMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _appCreates.Should().ContainSingle();
    }

    // ── (g) re-file: an update that moves the pair ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChildRefile_MovingThePairToAMissingAnUnreadableOrASecureUnreadableMatter_IsOneByteIdentical404_AndNothingIsWritten()
    {
        _world.WithRecord("sprk_todo", Todo, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        CatalogKnowsTheMatterType();
        _user.Missing.Add(AbsentMatter);
        _user.NoAppendTo.Add(OrdinaryMatter);
        _user.NoAppendTo.Add(SecureMatter);

        Dictionary<string, object?> MovePair(Guid matter) => new()
        {
            ["sprk_regardingrecordid"] = matter.ToString("D"),
            ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({RecordTypeMatter:D})",
        };

        var missing = await WireOf(await RefileChild("sprk_todo", Todo, MovePair(AbsentMatter)));
        var ordinary = await WireOf(await RefileChild("sprk_todo", Todo, MovePair(OrdinaryMatter)));
        var secure = await WireOf(await RefileChild("sprk_todo", Todo, MovePair(SecureMatter)));

        missing.Status.Should().Be(StatusCodes.Status404NotFound, missing.Body);
        ordinary.Should().Be(missing);
        secure.Should().Be(missing);
        _user.Patches.Should().BeEmpty("the caller's PATCH is never sent");
        _world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildRefile_MovingThePairToAnAppendableMatter_IsTheCallersOwnPatch()
    {
        _world.WithRecord("sprk_todo", Todo, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        CatalogKnowsTheMatterType();

        var result = await RefileChild("sprk_todo", Todo, new()
        {
            ["sprk_regardingrecordid"] = OrdinaryMatter.ToString("D"),
            ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({RecordTypeMatter:D})",
        });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        _user.Patches.Should().ContainSingle();
    }

    [Fact]
    public async Task ChildRefile_ChangingOnlyThePairId_IsCheckedAgainstTheRowsOwnType()
    {
        // The update sets one half; the other is the row's own, read as the caller.
        _world.WithRecord("sprk_todo", Todo, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        CatalogKnowsTheMatterType();
        _user.RowValues[Todo] = new() { ["_sprk_regardingrecordtype_value"] = RecordTypeMatter.ToString("D") };
        _user.NoAppendTo.Add(SecureMatter);

        var result = await RefileChild("sprk_todo", Todo, new() { ["sprk_regardingrecordid"] = SecureMatter.ToString("D") });

        Status(result).Should().Be(StatusCodes.Status404NotFound, Detail(result));
        ReasonCode(result).Should().Be(ChildRecordEndpoints.NotFoundCode);
        _user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildRefile_ReTypingThePair_IsCheckedAgainstTheRowsOwnId()
    {
        // Re-typing the pair re-points it: (project, X) → (matter, X) names another record.
        _world.WithRecord("sprk_todo", Todo, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        CatalogKnowsTheMatterType();
        _user.RowValues[Todo] = new() { ["sprk_regardingrecordid"] = SecureMatter.ToString("D") };
        _user.NoAppendTo.Add(SecureMatter);

        var result = await RefileChild("sprk_todo", Todo, new()
        {
            ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({RecordTypeMatter:D})",
        });

        Status(result).Should().Be(StatusCodes.Status404NotFound, Detail(result));
        _user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildRefile_ClearingThePair_IsTheCallersOwnPatch()
    {
        _world.WithRecord("sprk_todo", Todo, Directory.ChildBu, owningTeam: Directory.ChildTeam);

        var result = await RefileChild("sprk_todo", Todo, new()
        {
            ["sprk_regardingrecordid"] = null,
            ["sprk_RegardingRecordType@odata.bind"] = null,
        });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        _user.Patches.Should().ContainSingle();
    }

    [Fact]
    public async Task EventFiling_MovingThePairToASecureMatterTheCallerCannotAppendTo_IsTheUniform404()
    {
        _world.WithRecord("sprk_event", Event, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        CatalogKnowsTheMatterType();
        _user.NoAppendTo.Add(SecureMatter);

        var result = await RefileEvent(new()
        {
            ["sprk_regardingrecordid"] = SecureMatter.ToString("D"),
            ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({RecordTypeMatter:D})",
        });

        Status(result).Should().Be(StatusCodes.Status404NotFound, Detail(result));
        ReasonCode(result).Should().Be(ChildRecordEndpoints.NotFoundCode);
        _user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateRecord_MovingThePairToASecureMatterTheCallerCannotAppendTo_IsDenied_LikeAMissingOne()
    {
        _world.WithRecord("sprk_todo", Todo, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        CatalogKnowsTheMatterType();
        _user.Missing.Add(AbsentMatter);
        _user.NoAppendTo.Add(SecureMatter);

        Task<ToolResult> MovePair(Guid matter) => UpdateRecord("sprk_todo", Todo,
            ("sprk_regardingrecordid", JsonSerializer.SerializeToElement(matter.ToString("D"))),
            Lookup("sprk_regardingrecordtype", "sprk_recordtype_ref", RecordTypeMatter));

        var secure = await MovePair(SecureMatter);
        var missing = await MovePair(AbsentMatter);

        secure.Success.Should().BeFalse();
        secure.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        (secure.ErrorCode, secure.ErrorMessage).Should().Be((missing.ErrorCode, missing.ErrorMessage));
        _user.Patches.Should().BeEmpty();
    }

    // ── Verifier round on #1612 (F1/F2): a lookup with the SAME id in another table says nothing about the pair's record ──

    /// <summary>
    /// A contact whose id the caller chose equal to <paramref name="matter"/>'s (a client may pick a new row's id), assigned
    /// on the row, and the pair naming that matter by its type. Before the fix the pair's check was skipped because its id
    /// equalled a lookup's — any lookup, in any table — so the row was filed under the matter with no AppendTo on it.
    /// </summary>
    private static Dictionary<string, object?> AssignedToAContactWithTheMattersId(Guid matter, bool typed = true)
    {
        var item = new Dictionary<string, object?>
        {
            ["sprk_name"] = "Hearing",
            ["sprk_AssignedTo@odata.bind"] = $"/contacts({matter:D})",
            ["sprk_regardingrecordid"] = matter.ToString("D"),
        };
        if (typed)
            item["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({RecordTypeMatter:D})";
        return item;
    }

    [Fact]
    public async Task ChildCreate_AnEventAssignedToAContactWithASecureMattersId_AndThePairNamingThatMatter_IsTheUniform404_LikeAMissingMatter()
    {
        CatalogKnowsTheMatterType();
        _user.NoAppendToIn.Add(("sprk_matters", SecureMatter)); // the caller may append to the CONTACT, not to the matter
        _user.MissingIn.Add(("sprk_matters", AbsentMatter));     // a contact with this id exists; no matter does

        var secure = await WireOf(await CreateChild("sprk_event", AssignedToAContactWithTheMattersId(SecureMatter)));
        var missing = await WireOf(await CreateChild("sprk_event", AssignedToAContactWithTheMattersId(AbsentMatter)));

        secure.Status.Should().Be(StatusCodes.Status404NotFound, secure.Body);
        secure.Should().Be(missing, "the matter's existence and security are not told by a contact sharing its id");
        secure.Body.Should().Contain(ChildRecordEndpoints.NotFoundCode).And.NotContain(SecureMatter.ToString("D"));
        _appCreates.Should().BeEmpty("no event is filed under the matter, so no upload to it can be routed to its container");
        _user.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildCreate_AnEventAssignedToAContact_WithThePairNamingAnAppendableMatterOfTheSameId_IsCreated()
    {
        // The control: the matter's own AppendTo is what decides, not the contact's.
        CatalogKnowsTheMatterType();

        var result = await CreateChild("sprk_event", AssignedToAContactWithTheMattersId(OrdinaryMatter));

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        _appCreates.Should().ContainSingle();
    }

    [Fact]
    public async Task ChildCreate_AnUntypedPairIdEqualToOnlyAContactTheEventIsAssignedTo_Is400_AndNothingIsCreated()
    {
        // The event's assignee is not a column the pair's readers match the pair against, so an untyped pair repeating it
        // names nothing they can tell (the container resolver would refuse the row): refused before anything is written.
        var result = await CreateChild("sprk_event", AssignedToAContactWithTheMattersId(SecureMatter, typed: false));

        Status(result).Should().Be(StatusCodes.Status400BadRequest, Detail(result));
        ReasonCode(result).Should().Be(ChildRecordEndpoints.InvalidPayloadCode);
        _appCreates.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildCreate_ATypedRegardingLookupAndTheSamePair_IsStillCreated_UnderASecureMatter()
    {
        // The regarding builders' legitimate shape on the event: the typed lookup and the pair name the same matter.
        CatalogKnowsTheMatterType();

        var result = await CreateChild("sprk_event", new()
        {
            ["sprk_name"] = "Hearing",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({SecureMatter:D})",
            ["sprk_regardingrecordid"] = SecureMatter.ToString("D"),
            ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({RecordTypeMatter:D})",
        });

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        Owner(_appCreates.Should().ContainSingle().Subject.Fields).Should().Be(Directory.SecureNamedTeam);
    }

    [Fact]
    public async Task CreateRecord_AToDoAssignedToAContactWithASecureMattersId_AndThePairNamingThatMatter_IsDenied_LikeAMissingMatter()
    {
        CatalogKnowsTheMatterType();
        _user.NoAppendToIn.Add(("sprk_matters", SecureMatter));
        _user.MissingIn.Add(("sprk_matters", AbsentMatter));

        Task<ToolResult> Create(Guid matter) => CreateRecord("sprk_todo",
            ("sprk_name", JsonSerializer.SerializeToElement("Call back")),
            Lookup("sprk_assignedto", "contact", matter),
            ("sprk_regardingrecordid", JsonSerializer.SerializeToElement(matter.ToString("D"))),
            Lookup("sprk_regardingrecordtype", "sprk_recordtype_ref", RecordTypeMatter));

        var secure = await Create(SecureMatter);
        var missing = await Create(AbsentMatter);

        secure.Success.Should().BeFalse();
        secure.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        (secure.ErrorCode, secure.ErrorMessage).Should().Be((missing.ErrorCode, missing.ErrorMessage));
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateRecord_AssigningAContactWithASecureMattersId_AndMovingThePairOntoThatMatter_IsDenied_LikeAMissingMatter()
    {
        _world.WithRecord("sprk_todo", Todo, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        CatalogKnowsTheMatterType();
        _user.NoAppendToIn.Add(("sprk_matters", SecureMatter));
        _user.MissingIn.Add(("sprk_matters", AbsentMatter));

        Task<ToolResult> Move(Guid matter) => UpdateRecord("sprk_todo", Todo,
            Lookup("sprk_assignedto", "contact", matter),
            ("sprk_regardingrecordid", JsonSerializer.SerializeToElement(matter.ToString("D"))),
            Lookup("sprk_regardingrecordtype", "sprk_recordtype_ref", RecordTypeMatter));

        var secure = await Move(SecureMatter);
        var missing = await Move(AbsentMatter);

        secure.Success.Should().BeFalse();
        secure.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        (secure.ErrorCode, secure.ErrorMessage).Should().Be((missing.ErrorCode, missing.ErrorMessage));
        _user.Patches.Should().BeEmpty("the caller's PATCH is never sent");
        _world.Assignments.Should().BeEmpty();
    }

    // ── Verifier round on #1612 (F3): email.draft asks AppendTo on its regarding BEFORE deriving its ancestry app-only ──

    [Fact]
    public async Task EmailDraft_RegardingAMissingAnUnreadableOrAnUnappendableInvoice_AnswersIdentically_BeforeAnyAppOnlyRead()
    {
        var absent = Guid.Parse("a2470000-0000-4000-8000-0000000000e1");
        var unreadable = Guid.Parse("a2470000-0000-4000-8000-0000000000e2");
        var unappendable = Guid.Parse("a2470000-0000-4000-8000-0000000000e3");
        _user.Missing.Add(absent);
        _user.NoAppendTo.Add(unreadable);
        _user.UnreadableRows.Add(unreadable);
        _user.NoAppendTo.Add(unappendable);
        var appOnlyReads = new Moq.Mock<IGenericEntityService>(Moq.MockBehavior.Strict); // any app-only read fails the test
        var (visibility, visibilityAccessor) = new Sprk.Bff.Api.Tests.TestInfrastructure.CommunicationVisibilityFixtures().Build();
        var handler = new EmailDraftToolHandler(
            _user, Moq.Mock.Of<Sprk.Bff.Api.Services.Ai.PublicContracts.IEmailDraftAi>(),
            new Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver(
                appOnlyReads.Object,
                Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.ProbeReturning(
                    Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.AllRootColumns),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver>.Instance),
            CreateLogger<EmailDraftToolHandler>(), DraftCreation(), visibility, visibilityAccessor);

        Task<ToolResult> Draft(Guid invoice) => handler.ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: JsonSerializer.Serialize(new
            {
                subject = "Invoice query",
                body = "Draft.",
                to = new[] { "a@b.example" },
                regarding = new { table = "sprk_invoice", recordId = invoice },
            })) with { UserId = Guid.NewGuid().ToString() },
            BuildAnalysisTool(nameof(EmailDraftToolHandler)), CancellationToken.None);

        var missing = await Draft(absent);
        var cannotSee = await Draft(unreadable);
        var cannotAppend = await Draft(unappendable);

        missing.Success.Should().BeFalse();
        missing.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied,
            "a missing invoice is the same denial as one the caller may not file under — never 'ancestry could not be resolved'");
        (cannotSee.ErrorCode, cannotSee.ErrorMessage).Should().Be((missing.ErrorCode, missing.ErrorMessage));
        (cannotAppend.ErrorCode, cannotAppend.ErrorMessage).Should().Be((missing.ErrorCode, missing.ErrorMessage));
        appOnlyReads.Invocations.Should().BeEmpty("the regarding record is never read app-only before the caller's AppendTo");
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task EmailDraft_RegardingAnAppendableInvoice_DerivesItsAncestry_AndIsCreated()
    {
        // The control: the caller may append to the invoice — its ancestry is derived (here: none) and the draft is created.
        var invoice = Guid.Parse("a2470000-0000-4000-8000-0000000000e4");
        _world.WithRecord("sprk_invoice", invoice, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        var (visibility, visibilityAccessor) = new Sprk.Bff.Api.Tests.TestInfrastructure.CommunicationVisibilityFixtures().Build();
        var handler = new EmailDraftToolHandler(
            _user, Moq.Mock.Of<Sprk.Bff.Api.Services.Ai.PublicContracts.IEmailDraftAi>(),
            Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
            CreateLogger<EmailDraftToolHandler>(),
            // Task 017 (2d-4): the server create's step 10 reads the same invoice (filed under nothing: no stamp) that the
            // handler's derivation reads, as one Dataverse answers both in production.
            DraftCreation(new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld()
                .Row("sprk_invoice", invoice).Restamper),
            visibility, visibilityAccessor);

        var result = await handler.ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: JsonSerializer.Serialize(new
            {
                subject = "Invoice query",
                body = "Draft.",
                to = new[] { "a@b.example" },
                regarding = new { table = "sprk_invoice", recordId = invoice },
            })) with { UserId = Guid.NewGuid().ToString() },
            BuildAnalysisTool(nameof(EmailDraftToolHandler)), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
    }
}
