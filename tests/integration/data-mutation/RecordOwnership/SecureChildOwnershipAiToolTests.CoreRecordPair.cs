using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk.Query;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/RecordOwnership/**`
//   - Justification: field-mapping-server-write-path-r1 task 042c (#2561, DI-019) — a Signal or Decision Record created through
//     the ONE server create with a CORE pair (sprk_corerecordtype + sprk_corerecordid) costs the caller's AppendTo on the core
//     record (step 5), and the core record is asked first by the owner question (step 7), through the REAL chat
//     dataverse.create_record handler (the reachable path, AC6) and the REAL service over the caller's scripted Dataverse.

/// <summary>
/// Task 042c: the chat create of a Signal or Decision Record that names a core pair. Step 5 resolves the pair's TYPE through the
/// record-type catalog AS THE CALLER (<see cref="CallerWriteAuthorization.PairTargetAsync"/>, the regarding pair's rule) and asks
/// AppendTo on the core record before anything reads it app-only; a caller who may not append to it gets the uniform not-found an
/// unappendable lookup gets, whether the core is ordinary, Secure, unreadable or missing (no existence or secure oracle). Step 7
/// asks the core record first, as the reconciler does (<see cref="RecordOwnershipContext.ForExistingRowAsync"/>).
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid MatterRecordType = Guid.Parse("a2561000-0000-4000-8000-0000000000a1");
    private static readonly Guid DocumentRecordType = Guid.Parse("a2561000-0000-4000-8000-0000000000a2");
    private static readonly Guid NoTableRecordType = Guid.Parse("a2561000-0000-4000-8000-0000000000a3");
    private static readonly Guid UnknownRecordType = Guid.Parse("a2561000-0000-4000-8000-0000000000a4");
    private static readonly Guid CoreMatterInY = Guid.Parse("a2561000-0000-4000-8000-0000000000b1");
    private static readonly Guid UnreadableCoreMatter = Guid.Parse("a2561000-0000-4000-8000-0000000000b2");
    private static readonly Guid MissingCoreMatter = Guid.Parse("a2561000-0000-4000-8000-0000000000b3");

    public static readonly TheoryData<string> CorePairTables = new() { "sprk_signal", "sprk_decisionrecord" };

    /// <summary>The caller may create both tables; the catalog as the caller reads it; an ordinary matter in another unit (Y).</summary>
    private void CorePairWorld()
    {
        _user.Held.UnionWith(["prvCreatesprk_signal", "prvAppendsprk_signal", "prvCreatesprk_decisionrecord", "prvAppendsprk_decisionrecord"]);
        _user.RecordTypes[MatterRecordType] = "sprk_matter";
        _user.RecordTypes[DocumentRecordType] = "sprk_document";
        _user.RecordTypes[NoTableRecordType] = string.Empty;
        _world.WithRecord("sprk_matter", CoreMatterInY, Directory.CustomerChildBu, isSecure: false, owningTeam: Directory.CustomerChildTeam);
        _world.WithRecord("sprk_matter", UnreadableCoreMatter, Directory.CustomerChildBu, isSecure: false, owningTeam: Directory.CustomerChildTeam);
    }

    private static (string Column, JsonElement Value)[] CorePair(Guid? type, object? id)
    {
        var pair = new List<(string, JsonElement)>();
        if (type is { } t)
            pair.Add(Lookup("sprk_corerecordtype", "sprk_recordtype_ref", t));
        if (id is not null)
            pair.Add(("sprk_corerecordid", JsonSerializer.SerializeToElement(id)));
        return [.. pair];
    }

    /// <summary>A row filed under the ordinary matter (X, ChildTeam) naming the core pair.</summary>
    private Task<ToolResult> CreateWithCore(string table, Guid? type, object? id) =>
        CreateRecord(table, [Lookup("sprk_matter", "sprk_matter", OrdinaryMatter), .. CorePair(type, id)]);

    /// <summary>The same create through the service directly (the route's surface: status and ProblemDetails).</summary>
    private async Task<RecordCreationOutcome> ServiceCreateWithCore(
        string table, Guid? type, object? id, Spaarke.Dataverse.IGenericEntityService? appOnlyEntities = null)
    {
        var item = JsonSerializer.SerializeToElement(
            new[] { Lookup("sprk_matter", "sprk_matter", OrdinaryMatter) }.Concat(CorePair(type, id)).ToDictionary(i => i.Column, i => i.Value));
        var mapped = await DataverseWriteItemMapper.MapAsync(_user, table, item, CancellationToken.None);
        mapped.Item.Should().NotBeNull(mapped.ValidationError);
        return await RouteRecordCreation.Over(_world.Resolver(), _appOnly.Object, Restamper(), Shares(),
                SecureRootFilingGateFixtures.NothingSecure(), entities: appOnlyEntities)
            .CreateAsync(new RecordCreationRequest(table, new RecordCreationPayload.Mapped(mapped.Item!)),
                RecordCreationPrincipal.ForCaller(_user, CallerOid), CancellationToken.None);
    }

    private static string UniformNotFound(string table) =>
        RecordCreationProblemMap.ToProblemDetails(
            new RecordCreationRefusal(RecordCreationRefusalKind.NotFoundOrDenied, RecordCreationRefusal.NotFoundCode, "any"), table, "c")
        .Detail!;

    // ── AC2 / AC6: the owner asks the core record first ────────────────────────────────────────────────────────────

    [Theory(DisplayName = "042c AC2 (b) / AC6: a chat Signal or Decision Record filed under an ordinary matter in X whose ORDINARY core is in Y is owned by Y's team, and the core pair is written")]
    [MemberData(nameof(CorePairTables))]
    public async Task Chat_ACreateWithAnOrdinaryCoreInY_IsOwnedByYsTeam(string table)
    {
        CorePairWorld();

        var result = await CreateWithCore(table, MatterRecordType, CoreMatterInY.ToString("D"));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var (created, _, fields) = _appCreates.Should().ContainSingle().Subject;
        created.Should().Be(table);
        Owner(fields).Should().Be(Directory.CustomerChildTeam, "the core record (Y) is asked first (before 042c: the matter in X, ChildTeam)");
        fields.Should().ContainKey("sprk_corerecordid");
    }

    [Theory(DisplayName = "042c AC2 (a) / AC6, UAC-r2 condition 2: a chat caller holding AppendTo on a SECURE core gets the existing row-8 refusal, byte-identical (code and words) to a Signal or Decision Record filed under a Secure matter; nothing is created")]
    [MemberData(nameof(CorePairTables))]
    public async Task Chat_ACreateWithASecureCore_TheCallerHoldingAppendTo_GetsTheRow8Refusal(string table)
    {
        CorePairWorld();
        var row8 = await CreateRecord(table, Lookup("sprk_matter", "sprk_matter", SecureMatter));

        var result = await CreateWithCore(table, MatterRecordType, SecureMatter.ToString("D"));

        row8.Success.Should().BeFalse("the baseline: a Secure owner is refused for a table that is not a reparentable child");
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed).And.Be(row8.ErrorCode);
        result.ErrorMessage.Should().Be(row8.ErrorMessage, "the words are row 8's");
        _appCreates.Should().BeEmpty();
    }

    // ── AC3 / UAC-r2 conditions 1 and 2: no existence or secure oracle ────────────────────────────────────────────────

    [Theory(DisplayName = "042c AC3 (negative) / AC6, UAC-r2 condition 1: no AppendTo on a Secure core, on an ordinary core, on an existing core the caller cannot read, a core that does not exist and a type the catalog does not hold give byte-identical refusals — the uniform not-found of an unappendable lookup — before the owner question; nothing is created")]
    [InlineData("sprk_signal", "secure core, no AppendTo")]
    [InlineData("sprk_signal", "ordinary core, no AppendTo")]
    [InlineData("sprk_signal", "existing core the caller cannot read")]
    [InlineData("sprk_signal", "core that does not exist")]
    [InlineData("sprk_signal", "unknown record type")]
    [InlineData("sprk_signal", "baseline: an unappendable lookup")]
    [InlineData("sprk_decisionrecord", "secure core, no AppendTo")]
    [InlineData("sprk_decisionrecord", "ordinary core, no AppendTo")]
    [InlineData("sprk_decisionrecord", "existing core the caller cannot read")]
    [InlineData("sprk_decisionrecord", "core that does not exist")]
    [InlineData("sprk_decisionrecord", "unknown record type")]
    [InlineData("sprk_decisionrecord", "baseline: an unappendable lookup")]
    public async Task ACallerWhoMayNotFileUnderTheCore_GetsTheUniformNotFound_WhateverTheCore(string table, string condition)
    {
        CorePairWorld();
        Guid? type = MatterRecordType;
        var core = CoreMatterInY;
        switch (condition)
        {
            case "secure core, no AppendTo":
                core = SecureMatter;
                _user.NoAppendTo.Add(SecureMatter);
                break;
            case "ordinary core, no AppendTo":
                _user.NoAppendTo.Add(CoreMatterInY);
                break;
            case "existing core the caller cannot read":
                core = UnreadableCoreMatter;
                _user.UnreadableRows.Add(UnreadableCoreMatter);
                _user.NoAppendTo.Add(UnreadableCoreMatter);
                break;
            case "core that does not exist":
                core = MissingCoreMatter;
                _user.Missing.Add(MissingCoreMatter);
                break;
            case "unknown record type":
                type = UnknownRecordType;
                _user.MissingRows.Add(UnknownRecordType);
                break;
            default:
                _user.NoAppendTo.Add(OrdinaryMatter); // the row's own lookup, with an appendable core
                break;
        }

        var viaService = await ServiceCreateWithCore(table, type, core.ToString("D"));
        var viaChat = await CreateWithCore(table, type, core.ToString("D"));

        viaService.Refusal.Should().NotBeNull();
        viaService.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied,
            "refused at step 5, before step 7 could answer 409 for a Secure core");
        var problem = RecordCreationProblemMap.ToProblemDetails(viaService.Refusal, table, "c");
        problem.Status.Should().Be(404);
        problem.Extensions["errorCode"].Should().Be(RecordCreationRefusal.NotFoundCode);
        problem.Detail.Should().Be(UniformNotFound(table));
        viaChat.Success.Should().BeFalse();
        viaChat.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        viaChat.ErrorMessage.Should().Be(UniformNotFound(table));
        _appCreates.Should().BeEmpty();
    }

    [Theory(DisplayName = "042c UAC-r2 condition 1: a fault on the core pair's own catalog read (its type's sprk_recordlogicalname, as the caller) is the existing upstream 503, never a 4xx; nothing is created")]
    [MemberData(nameof(CorePairTables))]
    public async Task AFaultOnTheCorePairsOwnCatalogRead_IsTheExisting503(string table)
    {
        CorePairWorld();
        _user.GetOverride = path => path.StartsWith($"sprk_recordtype_refs({MatterRecordType:D})?$select=sprk_recordlogicalname", StringComparison.Ordinal)
            ? DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.ServiceError, "Scripted fault.")
            : null;

        var viaService = await ServiceCreateWithCore(table, MatterRecordType, CoreMatterInY.ToString("D"));
        var viaChat = await CreateWithCore(table, MatterRecordType, CoreMatterInY.ToString("D"));

        viaService.Refusal.Should().NotBeNull();
        viaService.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Upstream);
        RecordCreationProblemMap.StatusOf(viaService.Refusal).Should().Be(503);
        viaChat.Success.Should().BeFalse();
        viaChat.ErrorCode.Should().Be(DataverseUserClientErrorCodes.ServiceError, "the failure's own code, as any upstream failure");
        _appCreates.Should().BeEmpty();
    }

    [Theory(DisplayName = "042c fix round 1 (DI-P3, K1): a fault at the TYPE lookup's organization-owned AppendTo read (sprk_recordtype_refs(type)?$select=sprk_recordtype_refid, as the caller) is today's uniform 404, as for every organization-owned lookup; fails closed, nothing is created")]
    [MemberData(nameof(CorePairTables))]
    public async Task AFaultOnTheTypeLookupsAppendToRead_IsTodaysUniformNotFound(string table)
    {
        CorePairWorld();
        var faulted = 0;
        _user.GetOverride = path =>
        {
            if (!path.StartsWith($"sprk_recordtype_refs({MatterRecordType:D})?$select=sprk_recordtype_refid", StringComparison.Ordinal))
                return null;
            faulted++;
            return DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.ServiceError, "Scripted fault.");
        };

        var viaService = await ServiceCreateWithCore(table, MatterRecordType, CoreMatterInY.ToString("D"));
        var viaChat = await CreateWithCore(table, MatterRecordType, CoreMatterInY.ToString("D"));

        faulted.Should().Be(2, "the type lookup's AppendTo read is asked (and faults) on both paths");
        viaService.Refusal.Should().NotBeNull();
        viaService.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied,
            "CallerWriteAuthorization reads a failed organization-owned row read as no AppendTo (return row.IsSuccess)");
        var problem = RecordCreationProblemMap.ToProblemDetails(viaService.Refusal, table, "c");
        problem.Status.Should().Be(404);
        problem.Extensions["errorCode"].Should().Be(RecordCreationRefusal.NotFoundCode);
        problem.Detail.Should().Be(UniformNotFound(table));
        viaChat.Success.Should().BeFalse();
        viaChat.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        viaChat.ErrorMessage.Should().Be(UniformNotFound(table));
        _appCreates.Should().BeEmpty();
    }

    // ── AC4: a core pair that cannot be told fails closed with an existing code ─────────────────────────────────────

    [Theory(DisplayName = "042c AC4 (negative) / AC6: a type naming a table that is no core record table, a type naming no table, an unreadable catalog row, a non-guid id, an id with no type and a non-string id each refuse with an existing code; nothing is created")]
    [InlineData("sprk_signal", "type names a table that is no core record table")]
    [InlineData("sprk_signal", "type names no table")]
    [InlineData("sprk_signal", "catalog row the caller cannot read")]
    [InlineData("sprk_signal", "id is not a guid")]
    [InlineData("sprk_signal", "id with no type")]
    [InlineData("sprk_signal", "id is not a string")]
    [InlineData("sprk_decisionrecord", "type names a table that is no core record table")]
    [InlineData("sprk_decisionrecord", "type names no table")]
    [InlineData("sprk_decisionrecord", "catalog row the caller cannot read")]
    [InlineData("sprk_decisionrecord", "id is not a guid")]
    [InlineData("sprk_decisionrecord", "id with no type")]
    [InlineData("sprk_decisionrecord", "id is not a string")]
    public async Task ACorePairThatCannotBeTold_IsRefusedFailClosed(string table, string condition)
    {
        CorePairWorld();
        Guid? type = MatterRecordType;
        object id = CoreMatterInY.ToString("D");
        var (kind, chatCode) = (RecordCreationRefusalKind.Invalid, ToolErrorCodes.ValidationFailed);
        switch (condition)
        {
            case "type names a table that is no core record table":
                type = DocumentRecordType;
                break;
            case "type names no table":
                type = NoTableRecordType;
                break;
            case "catalog row the caller cannot read":
                _user.UnreadableRows.Add(MatterRecordType);
                (kind, chatCode) = (RecordCreationRefusalKind.NotFoundOrDenied, DataverseUserClientErrorCodes.AccessDenied);
                break;
            case "id is not a guid":
                id = "not-a-guid";
                break;
            case "id with no type":
                type = null;
                break;
            default:
                id = 42;
                break;
        }

        var viaService = await ServiceCreateWithCore(table, type, id);
        var viaChat = await CreateWithCore(table, type, id);

        viaService.Refusal.Should().NotBeNull();
        viaService.Refusal!.Kind.Should().Be(kind);
        viaChat.Success.Should().BeFalse();
        viaChat.ErrorCode.Should().Be(chatCode);
        if (kind == RecordCreationRefusalKind.Invalid)
        {
            viaService.Refusal.Code.Should().Be(RecordCreationRefusal.InvalidPayloadCode);
            viaChat.ErrorMessage.Should().Contain("sprk_corerecord", "the words name the core pair, never the regarding pair");
        }

        _appCreates.Should().BeEmpty();
    }

    [Theory(DisplayName = "042c fix round 1 (C): a core id that is not a record id (not a string, not a guid, the empty guid) and a core id with no type are refused 400 in the core pair's own words, before the catalog is read; nothing is created")]
    [InlineData("sprk_signal", "id is not a string")]
    [InlineData("sprk_signal", "id is not a guid")]
    [InlineData("sprk_signal", "id is the empty guid")]
    [InlineData("sprk_signal", "id with no type")]
    [InlineData("sprk_decisionrecord", "id is not a string")]
    [InlineData("sprk_decisionrecord", "id is not a guid")]
    [InlineData("sprk_decisionrecord", "id is the empty guid")]
    [InlineData("sprk_decisionrecord", "id with no type")]
    public async Task ACoreIdThatIsNotARecordIdOrHasNoType_IsRefusedInTheCorePairsOwnWords(string table, string condition)
    {
        CorePairWorld();
        var asked = new List<string>();
        _user.GetOverride = path =>
        {
            asked.Add(path);
            return null;
        };
        Guid? type = MatterRecordType;
        object id = condition switch
        {
            "id is not a string" => 42,
            "id is not a guid" => "not-a-guid",
            "id is the empty guid" => Guid.Empty.ToString("D"),
            _ => CoreMatterInY.ToString("D"),
        };
        var words = RecordCreationService.CoreIdNotARecordId;
        if (condition == "id with no type")
            (type, words) = (null, RecordCreationService.CoreIdWithNoType);

        var viaService = await ServiceCreateWithCore(table, type, id);
        var viaChat = await CreateWithCore(table, type, id);

        viaService.Refusal.Should().NotBeNull();
        viaService.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Invalid);
        viaService.Refusal.Code.Should().Be(RecordCreationRefusal.InvalidPayloadCode);
        viaService.Refusal.Detail.Should().Be(words);
        var problem = RecordCreationProblemMap.ToProblemDetails(viaService.Refusal, table, "c");
        problem.Status.Should().Be(400);
        problem.Detail.Should().Be(words);
        viaChat.Success.Should().BeFalse();
        viaChat.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        viaChat.ErrorMessage.Should().Be(words);
        asked.Should().NotContain(p => p.Contains("$select=sprk_recordlogicalname", StringComparison.Ordinal), "refused before the catalog is read");
        _appCreates.Should().BeEmpty();
    }

    [Theory(DisplayName = "042c fix round 1 (D): a core id sent with braces, in upper case or padded is written as the writers store it (the 'D' form, lower case), through the service and through chat; one already in that form is written as sent")]
    [InlineData("sprk_signal", "braces")]
    [InlineData("sprk_signal", "upper case")]
    [InlineData("sprk_signal", "padded")]
    [InlineData("sprk_signal", "braces, upper case and padded")]
    [InlineData("sprk_signal", "as stored")]
    [InlineData("sprk_decisionrecord", "braces")]
    [InlineData("sprk_decisionrecord", "upper case")]
    [InlineData("sprk_decisionrecord", "padded")]
    [InlineData("sprk_decisionrecord", "braces, upper case and padded")]
    [InlineData("sprk_decisionrecord", "as stored")]
    public async Task ACoreIdIsWrittenAsTheWritersStoreIt(string table, string shape)
    {
        CorePairWorld();
        var stored = CoreMatterInY.ToString("D", System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant();
        var sent = shape switch
        {
            "braces" => CoreMatterInY.ToString("B"),
            "upper case" => stored.ToUpperInvariant(),
            "padded" => $"  {stored}\t",
            "braces, upper case and padded" => $" {CoreMatterInY.ToString("B").ToUpperInvariant()} ",
            _ => stored,
        };

        var viaService = await ServiceCreateWithCore(table, MatterRecordType, sent);
        var viaChat = await CreateWithCore(table, MatterRecordType, sent);

        viaService.Succeeded.Should().BeTrue(viaService.Refusal?.Detail);
        viaChat.Success.Should().BeTrue(viaChat.ErrorMessage);
        _appCreates.Should().HaveCount(2);
        _appCreates.Select(c => CoreIdWritten(c.Fields)).Should().AllBe(stored, "SignalWriter and DecisionRecordWriter store it so");
        _appCreates.Select(c => Owner(c.Fields)).Should().OnlyContain(t => t == Directory.CustomerChildTeam, "the core record (Y) is still asked first");
    }

    private static string? CoreIdWritten(IReadOnlyDictionary<string, object?> fields) =>
        fields.TryGetValue("sprk_corerecordid", out var value)
            ? value switch
            {
                string text => text,
                JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
                _ => null,
            }
            : null;

    // ── AC1: the probe list and the call order ──────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "042c AC1: step 5 reads the catalog row as the caller, then asks AppendTo on the core record; nothing reads the core record app-only before that check (the resolver reads it only after)")]
    [MemberData(nameof(CorePairTables))]
    public async Task Step5_AsksTheCatalogThenTheCoresAppendTo_BeforeAnyAppOnlyReadOfTheCore(string table)
    {
        CorePairWorld();
        var asked = new List<string>();
        // Fix round 1 (review item F): every app-only read is snapshotted at the caller's check — the resolver's queries AND its
        // single-row reads (RetrieveAsync), and the service's own app-only reads (its IGenericEntityService: RetrieveAsync and
        // RetrieveMultipleAsync).
        var serviceEntities = RouteRecordCreation.Entities();
        List<QueryExpression>? queriesBeforeTheCheck = null;
        List<(string Entity, Guid Id)>? rowReadsBeforeTheCheck = null;
        List<Moq.IInvocation>? serviceReadsBeforeTheCheck = null;
        _user.GetOverride = path =>
        {
            asked.Add(path);
            if (path.Contains("RetrievePrincipalAccess", StringComparison.Ordinal) && path.Contains($"sprk_matters({CoreMatterInY:D})", StringComparison.Ordinal))
            {
                queriesBeforeTheCheck ??= [.. _world.RecordQueries];
                rowReadsBeforeTheCheck ??= [.. _world.RowReads];
                serviceReadsBeforeTheCheck ??= [.. serviceEntities.Invocations];
            }

            return null;
        };

        var outcome = await ServiceCreateWithCore(table, MatterRecordType, CoreMatterInY.ToString("D"), serviceEntities.Object);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var catalog = asked.FindIndex(p => p.StartsWith($"sprk_recordtype_refs({MatterRecordType:D})?$select=sprk_recordlogicalname", StringComparison.Ordinal));
        var appendTo = asked.FindIndex(p => p.Contains("RetrievePrincipalAccess", StringComparison.Ordinal)
                                            && p.Contains($"sprk_matters({CoreMatterInY:D})", StringComparison.Ordinal));
        catalog.Should().BeGreaterThanOrEqualTo(0, "the core pair's type is read through the catalog as the caller");
        appendTo.Should().BeGreaterThan(catalog, "AppendTo is asked on the record the catalog names");
        queriesBeforeTheCheck.Should().NotBeNull();
        queriesBeforeTheCheck!.Should().NotContain(q => Names(q, CoreMatterInY), "no app-only query names the core record before the caller's check");
        rowReadsBeforeTheCheck!.Should().NotContain(r => r.Id == CoreMatterInY, "no app-only RetrieveAsync of the core record before the caller's check");
        serviceReadsBeforeTheCheck!.Where(ReadsTheCore).Should().BeEmpty("the service reads nothing app-only about the core before the caller's check");
        _world.RecordQueries.Should().Contain(q => Names(q, CoreMatterInY), "the owner question reads it after the check");
    }

    // ── AC5: must not fire ─────────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "042c AC5 (must not fire), UAC-r2 condition 3: a Signal or Decision Record with NO core pair asks no catalog row and no AppendTo beyond its own lookups, and keeps today's owner")]
    [MemberData(nameof(CorePairTables))]
    public async Task ACreateWithNoCorePair_KeepsTodaysProbesAndOwner(string table)
    {
        CorePairWorld();
        var asked = new List<string>();
        _user.GetOverride = path =>
        {
            asked.Add(path);
            return null;
        };

        var result = await CreateRecord(table, Lookup("sprk_matter", "sprk_matter", OrdinaryMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        Owner(_appCreates.Should().ContainSingle().Subject.Fields).Should().Be(Directory.ChildTeam, "the matter's team, as before 042c");
        asked.Should().NotContain(p => p.StartsWith("sprk_recordtype_refs(", StringComparison.Ordinal), "no catalog row is read");
        asked.Where(p => p.Contains("RetrievePrincipalAccess", StringComparison.Ordinal)).Should()
            .ContainSingle().Which.Should().Contain($"sprk_matters({OrdinaryMatter:D})", "AppendTo on its own lookup only");
    }

    [Theory(DisplayName = "042c AC5 (must not fire): a Signal or Decision Record naming only the core pair's TYPE (no id) names no core record, as PairTargetAsync reads a pair with no id: today's owner")]
    [MemberData(nameof(CorePairTables))]
    public async Task ACreateWithACoreTypeButNoId_KeepsTodaysOwner(string table)
    {
        CorePairWorld();

        var result = await CreateWithCore(table, MatterRecordType, id: null);

        result.Success.Should().BeTrue(result.ErrorMessage);
        Owner(_appCreates.Should().ContainSingle().Subject.Fields).Should().Be(Directory.ChildTeam);
    }

    /// <summary>Whether an app-only read of the service (its IGenericEntityService) reads the core record.</summary>
    private static bool ReadsTheCore(Moq.IInvocation invocation) => invocation.Method.Name switch
    {
        "RetrieveAsync" => invocation.Arguments[1] is Guid id && id == CoreMatterInY,
        "RetrieveMultipleAsync" => invocation.Arguments[0] is QueryExpression query && Names(query, CoreMatterInY),
        _ => false,
    };

    /// <summary>Whether <paramref name="query"/> names <paramref name="id"/> in any condition, at any depth.</summary>
    private static bool Names(QueryExpression query, Guid id)
    {
        static bool InFilter(FilterExpression filter, Guid id) =>
            filter.Conditions.Any(c => c.Values.Any(v => v is Guid g ? g == id : string.Equals(v?.ToString(), id.ToString("D"), StringComparison.OrdinalIgnoreCase)))
            || filter.Filters.Any(f => InFilter(f, id));

        return InFilter(query.Criteria, id) || query.LinkEntities.Any(l => InFilter(l.LinkCriteria, id));
    }
}
