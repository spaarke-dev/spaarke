using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;
using StampWorld = Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/RecordOwnership/**`
//   - Justification: field-mapping-server-write-path-r1 task 042c fix round 2 (B', owner 2026-11-04) — a Signal or Decision
//     Record created through the ONE server create naming its core record TYPE with no id is never written as a half pair and
//     never refused: the service fills, replaces or clears the pair from the row as it ends, with a warning. Driven through the
//     REAL chat dataverse.create_record handler and the REAL service over the caller's scripted Dataverse, both tables.

/// <summary>
/// Task 042c fix round 2 (B'): step 6¾ of <see cref="RecordCreationService"/>. Rulings 1-9 and UAC-r2's conditions (a)-(f), each
/// through the service (status, ProblemDetails, warnings with codes) and through chat (the tool result and its warnings).
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid ProjectRecordType = Guid.Parse("a2561000-0000-4000-8000-0000000000a5");
    private static readonly Guid SecondMatterRecordType = Guid.Parse("a2561000-0000-4000-8000-0000000000a6");
    private static readonly Guid HalfPairProject = Guid.Parse("a2561000-0000-4000-8000-0000000000c1");
    private static readonly Guid InvoiceUnderY = Guid.Parse("a2561000-0000-4000-8000-0000000000c2");

    /// <summary>B': the service's own app-only reads (null: <see cref="RouteRecordCreation.Entities"/>), here answering the
    /// record-type catalog by table.</summary>
    private Mock<IGenericEntityService>? _serviceEntities;

    /// <summary>B': the service's restamper (null: an empty stamp world), here a world holding an invoice under the matter in Y.</summary>
    private Sprk.Bff.Api.Services.Dataverse.CoreAncestorRestamper? _serviceRestamper;

    private StampWorld? _halfPairStamps;

    /// <summary>The active catalog rows by table, as <see cref="CoreAncestorResolver.RecordTypeOfCoreTableAsync"/> reads them.</summary>
    private readonly Dictionary<string, List<Guid>> _catalogByTable = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The tables the service asked the catalog about, in order.</summary>
    private readonly List<string> _catalogTablesAsked = new();

    private Exception? _catalogFault;

    /// <summary>B': what the service logged in <see cref="ServiceCreate"/> (step 6¾'s outcome, and that no record id is logged).</summary>
    private readonly CapturingLog<RecordCreationService> _halfPairLog = new();

    /// <summary>
    /// <see cref="CorePairWorld"/>, plus: the catalog by table (matter, project); an ordinary project in X; a team-owned invoice in
    /// X filed under the ordinary matter in Y (directory and stamp world: step 6½ plans the matter stamp from it).
    /// </summary>
    private void HalfPairWorld()
    {
        CorePairWorld();
        _user.RecordTypes[ProjectRecordType] = "sprk_project";
        _user.RecordTypes[SecondMatterRecordType] = "sprk_matter";
        _catalogByTable["sprk_matter"] = [MatterRecordType];
        _catalogByTable["sprk_project"] = [ProjectRecordType];
        _world.WithRecord("sprk_project", HalfPairProject, Directory.ChildBu, isSecure: false, owningTeam: Directory.ChildTeam);
        _world.WithRecord("sprk_invoice", InvoiceUnderY, Directory.ChildBu, owningTeam: Directory.ChildTeam,
            extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", CoreMatterInY) });

        _halfPairStamps = new StampWorld();
        foreach (var matter in new[] { CoreMatterInY, OrdinaryMatter, SecureMatter })
            _halfPairStamps.Row("sprk_matter", matter);
        _halfPairStamps.Row("sprk_project", HalfPairProject);
        _halfPairStamps.Row("sprk_invoice", InvoiceUnderY, [("sprk_matter", "sprk_matter", CoreMatterInY)]);
        _serviceRestamper = _halfPairStamps.Restamper;

        var entities = RouteRecordCreation.Entities();
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_recordtype_ref"), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) =>
            {
                var table = (string)query.Criteria.Conditions.Single(c => c.AttributeName == "sprk_recordlogicalname").Values.Single();
                query.Criteria.Conditions.Should().Contain(c => c.AttributeName == "statecode", "only an active catalog row counts");
                _catalogTablesAsked.Add(table);
                if (_catalogFault is { } fault)
                    return Task.FromException<EntityCollection>(fault);
                var rows = _catalogByTable.TryGetValue(table, out var ids) ? ids : [];
                return Task.FromResult(new EntityCollection(rows.Select(id => new Entity("sprk_recordtype_ref", id)).ToList()));
            });
        _serviceEntities = entities;
    }

    private static (string Column, JsonElement Value) CoreType(Guid type) => Lookup("sprk_corerecordtype", "sprk_recordtype_ref", type);

    /// <summary>A core id in one of the half-pair shapes: absent, JSON null, empty, whitespace.</summary>
    private static (string Column, JsonElement Value)[] HalfId(string shape) => shape switch
    {
        "missing" => [],
        "null" => [("sprk_corerecordid", JsonSerializer.SerializeToElement<object?>(null))],
        "empty" => [("sprk_corerecordid", JsonSerializer.SerializeToElement(string.Empty))],
        _ => [("sprk_corerecordid", JsonSerializer.SerializeToElement(" \t "))],
    };

    /// <summary>The same item through the service directly, as the caller.</summary>
    private async Task<RecordCreationOutcome> ServiceCreate(string table, params (string Column, JsonElement Value)[] item)
    {
        var mapped = await DataverseWriteItemMapper.MapAsync(
            _user, table, JsonSerializer.SerializeToElement(item.ToDictionary(i => i.Column, i => i.Value)), CancellationToken.None);
        mapped.Item.Should().NotBeNull(mapped.ValidationError);
        return await RouteRecordCreation.Over(_world.Resolver(), _appOnly.Object, _serviceRestamper ?? Restamper(), Shares(),
                SecureRootFilingGateFixtures.NothingSecure(), entities: _serviceEntities?.Object, logger: _halfPairLog)
            .CreateAsync(new RecordCreationRequest(table, new RecordCreationPayload.Mapped(mapped.Item!)),
                RecordCreationPrincipal.ForCaller(_user, CallerOid), CancellationToken.None);
    }

    /// <summary>Step 6¾'s outcomes the service logged (the warning codes), in order.</summary>
    private IReadOnlyList<string> HalfPairOutcomesLogged() =>
        [.. _halfPairLog.Messages.Where(m => m.Contains("named its core record type without the core record", StringComparison.Ordinal))];

    /// <summary>Runs <paramref name="item"/> through the service and then through chat.</summary>
    private async Task<(RecordCreationOutcome Service, ToolResult Chat)> BothPaths(string table, params (string Column, JsonElement Value)[] item) =>
        (await ServiceCreate(table, item), await CreateRecord(table, item));

    private static string? CoreTypeWritten(IReadOnlyDictionary<string, object?> fields) =>
        Bind(fields, "sprk_CoreRecordType@odata.bind");

    /// <summary>Ruling 8 / UAC-r2 (f): the words name no record id (in any form) and nothing about security.</summary>
    private static void NamesNoRecordAndNoSecureState(string words, params Guid[] records)
    {
        words.Should().NotContainEquivalentOf("secure", "the words never say whether the core record is Secure");
        foreach (var id in records)
        {
            foreach (var form in new[] { "D", "N", "B", "P" })
                words.Should().NotContainEquivalentOf(id.ToString(form), "the words never name the core record");
        }
    }

    /// <summary>The created rows (service, then chat) and the warning each path carried, checked alike.</summary>
    private void BothCreatedWithTheWarning(
        RecordCreationOutcome viaService, ToolResult viaChat, string code, string words, params Guid[] records)
    {
        viaService.Succeeded.Should().BeTrue(viaService.Refusal?.Detail);
        viaChat.Success.Should().BeTrue(viaChat.ErrorMessage);
        _appCreates.Should().HaveCount(2);
        viaService.MappingWarnings.Should().ContainSingle(w => w.Code == code).Which.Detail.Should().Be(words);
        viaService.Warnings.Should().Contain(words, "the existing warning channel carries it");
        viaChat.Warnings.Should().Contain(words, "ruling 8: the warning reaches the chat tool result (DataverseCreateRecordHandler)");
        NamesNoRecordAndNoSecureState(words, records);
    }

    // ── Rulings 1 and 2: filled from the record the row is filed under ─────────────────────────────────────────────────

    [Theory(DisplayName = "042c B' rulings 1-2 (the converted AC5 case): a core TYPE with a missing, null, empty or whitespace id, filed under a record of that type, is FILLED with that record (the 'D' form, lower case), the type kept, with the core_record_filled warning; service and chat")]
    [InlineData("sprk_signal", "missing")]
    [InlineData("sprk_signal", "null")]
    [InlineData("sprk_signal", "empty")]
    [InlineData("sprk_signal", "whitespace")]
    [InlineData("sprk_decisionrecord", "missing")]
    [InlineData("sprk_decisionrecord", "null")]
    [InlineData("sprk_decisionrecord", "empty")]
    [InlineData("sprk_decisionrecord", "whitespace")]
    public async Task ACreateWithACoreTypeButNoId_IsFilledFromTheRecordItIsFiledUnder(string table, string idShape)
    {
        HalfPairWorld();

        var (viaService, viaChat) = await BothPaths(table,
            [Lookup("sprk_matter", "sprk_matter", OrdinaryMatter), CoreType(MatterRecordType), .. HalfId(idShape)]);

        var noun = RecordCreationService.NounOf(table);
        BothCreatedWithTheWarning(viaService, viaChat, RecordCreationWarning.CoreRecordFilledCode,
            RecordCreationService.CoreRecordFilled(noun), OrdinaryMatter, MatterRecordType);
        foreach (var (_, _, fields) in _appCreates)
        {
            CoreIdWritten(fields).Should().Be(OrdinaryMatter.ToString("D").ToLowerInvariant(), "never a half pair: the id is filled");
            CoreTypeWritten(fields).Should().Be($"/sprk_recordtype_refs({MatterRecordType:D})", "the type as sent");
            Owner(fields).Should().Be(Directory.ChildTeam, "the filled core is the matter the row is filed under");
        }
    }

    [Theory(DisplayName = "042c B' ruling 1, UAC-r2 (a): the fill comes from step 6½'s plan (the invoice's matter in Y), never from a SENT stamp that disagrees (the matter in X) and never from a probe: no AppendTo is asked on the filled record; owned by Y's team (the core asked first); service and chat")]
    [MemberData(nameof(CorePairTables))]
    public async Task AHalfPair_IsFilledFromThePlannedStamp_NeverFromADisagreeingSentStamp_NorAProbe(string table)
    {
        HalfPairWorld();
        var asked = new List<string>();
        _user.GetOverride = path =>
        {
            asked.Add(path);
            return null;
        };

        var (viaService, viaChat) = await BothPaths(table,
            Lookup("sprk_regardinginvoice", "sprk_invoice", InvoiceUnderY),
            Lookup("sprk_regardingmatter", "sprk_matter", OrdinaryMatter), // sent, and disagreeing with the invoice's matter
            CoreType(MatterRecordType));

        BothCreatedWithTheWarning(viaService, viaChat, RecordCreationWarning.CoreRecordFilledCode,
            RecordCreationService.CoreRecordFilled(RecordCreationService.NounOf(table)), CoreMatterInY, OrdinaryMatter);
        foreach (var (_, _, fields) in _appCreates)
        {
            CoreIdWritten(fields).Should().Be(CoreMatterInY.ToString("D"), "the planned root, as the row ends");
            Owner(fields).Should().Be(Directory.CustomerChildTeam, "the filled core (Y) is asked first, as the reconciler asks the written row");
        }

        asked.Should().NotContain(p => p.Contains("RetrievePrincipalAccess", StringComparison.Ordinal)
                                       && p.Contains($"sprk_matters({CoreMatterInY:D})", StringComparison.Ordinal),
            "the derived root is never probed as the caller (UAC-r2 (a))");
    }

    // ── Ruling 3: one root of another type — REPLACED whole, or CLEARED ─────────────────────────────────────────────────

    [Theory(DisplayName = "042c B' ruling 3, UAC-r2 (b): a core type that is not the type of the ONE record the row is filed under is REPLACED: type and id both from that record (the type from the catalog row of the id's table, so never a mixed pair), with the core_record_replaced warning; service and chat")]
    [MemberData(nameof(CorePairTables))]
    public async Task AHalfPairOfAnotherType_IsReplacedWholeFromTheOneRoot(string table)
    {
        HalfPairWorld();

        var (viaService, viaChat) = await BothPaths(table,
            Lookup("sprk_matter", "sprk_matter", OrdinaryMatter), CoreType(ProjectRecordType));

        BothCreatedWithTheWarning(viaService, viaChat, RecordCreationWarning.CoreRecordReplacedCode,
            RecordCreationService.CoreRecordReplaced(RecordCreationService.NounOf(table)), OrdinaryMatter, MatterRecordType, ProjectRecordType);
        foreach (var (_, _, fields) in _appCreates)
        {
            CoreTypeWritten(fields).Should().Be($"/sprk_recordtype_refs({MatterRecordType:D})",
                "the type is the catalog row of the id's table (the sent project type is never kept beside a matter id)");
            CoreIdWritten(fields).Should().Be(OrdinaryMatter.ToString("D"));
        }

        _catalogTablesAsked.Should().OnlyContain(t => t == "sprk_matter", "the catalog is read by the root's table only (app-only)");
    }

    [Theory(DisplayName = "042c B' ruling 3: when the one root's table has no active catalog row, or two, the type is CLEARED (neither column written), with the core_record_cleared warning; the row is created with no core; service and chat")]
    [InlineData("sprk_signal", "no catalog row")]
    [InlineData("sprk_signal", "two catalog rows")]
    [InlineData("sprk_decisionrecord", "no catalog row")]
    [InlineData("sprk_decisionrecord", "two catalog rows")]
    public async Task AHalfPairWhoseRootsCatalogRowCannotBeTold_IsCleared(string table, string catalog)
    {
        HalfPairWorld();
        _catalogByTable["sprk_matter"] = catalog == "no catalog row" ? [] : [MatterRecordType, SecondMatterRecordType];

        var (viaService, viaChat) = await BothPaths(table,
            Lookup("sprk_matter", "sprk_matter", OrdinaryMatter), CoreType(MatterRecordType));

        BothCreatedWithTheWarning(viaService, viaChat, RecordCreationWarning.CoreRecordClearedCode,
            RecordCreationService.CoreRecordCleared(RecordCreationService.NounOf(table)), OrdinaryMatter, MatterRecordType);
        foreach (var (_, _, fields) in _appCreates)
        {
            fields.Keys.Should().NotContain(k => k.Contains("corerecord", StringComparison.OrdinalIgnoreCase), "neither column is written");
            Owner(fields).Should().Be(Directory.ChildTeam, "a no-core row keeps the owner of what it is filed under");
        }
    }

    [Theory(DisplayName = "042c B' rulings 3 and 6, UAC-r2 (d): a FAULT reading the catalog is the existing 503 stamp_unavailable (the step-6½ words), never a clear; nothing is written; service and chat")]
    [MemberData(nameof(CorePairTables))]
    public async Task ACatalogFault_IsTheExisting503_NothingClearedOrWritten(string table)
    {
        HalfPairWorld();
        // The baseline: the existing step-6½ planning fault, through both paths (chat words are chat's own for a 503).
        _halfPairStamps!.FailRead("sprk_invoice", InvoiceUnderY);
        var (planService, planChat) = await BothPaths(table,
            Lookup("sprk_regardinginvoice", "sprk_invoice", InvoiceUnderY), CoreType(MatterRecordType));
        _catalogFault = new InvalidOperationException("Scripted catalog fault.");

        var (viaService, viaChat) = await BothPaths(table,
            Lookup("sprk_matter", "sprk_matter", OrdinaryMatter), CoreType(MatterRecordType));

        AssertTheStampUnavailable503(table, viaService, viaChat);
        AssertTheStampUnavailable503(table, planService, planChat);
        viaChat.ErrorCode.Should().Be(planChat.ErrorCode, "chat gives the catalog fault the planning fault's code");
        viaChat.ErrorMessage.Should().Be(planChat.ErrorMessage, "and its words");
        _catalogTablesAsked.Should().NotBeEmpty("the fault was on the catalog read");
    }

    [Theory(DisplayName = "042c B' ruling 6, UAC-r2 (d): a FAULT in step 6½'s plan (the invoice cannot be read) is the existing 503 stamp_unavailable before step 6¾ runs: no catalog read, nothing cleared or written; service and chat")]
    [MemberData(nameof(CorePairTables))]
    public async Task APlanningFault_IsTheExisting503_NothingClearedOrWritten(string table)
    {
        HalfPairWorld();
        _halfPairStamps!.FailRead("sprk_invoice", InvoiceUnderY);

        var (viaService, viaChat) = await BothPaths(table,
            Lookup("sprk_regardinginvoice", "sprk_invoice", InvoiceUnderY), CoreType(MatterRecordType));

        AssertTheStampUnavailable503(table, viaService, viaChat);
        _catalogTablesAsked.Should().BeEmpty("a planning fault refuses before step 6¾");
    }

    private void AssertTheStampUnavailable503(string table, RecordCreationOutcome viaService, ToolResult viaChat)
    {
        viaService.Refusal.Should().NotBeNull();
        viaService.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Unavailable);
        viaService.Refusal.Code.Should().Be(RecordCreationRefusal.StampUnavailableCode);
        viaService.Refusal.Detail.Should().Be(
            $"The core record the {RecordCreationService.NounOf(table)} belongs to could not be determined right now, so it was not created.",
            "the existing step-6½ words");
        RecordCreationProblemMap.StatusOf(viaService.Refusal).Should().Be(503);
        viaChat.Success.Should().BeFalse();
        viaChat.ErrorCode.Should().NotBeNullOrEmpty();
        viaChat.Warnings.Should().BeEmpty("a refusal carries no warning: nothing was cleared");
        _appCreates.Should().BeEmpty("nothing is cleared or written on a fault");
    }

    // ── Ruling 4: two roots ───────────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "042c B' ruling 4: filed under a matter AND a project, a type matching one is FILLED with that one; a type matching neither is CLEARED; each with its warning; service and chat")]
    [InlineData("sprk_signal", "matter")]
    [InlineData("sprk_signal", "project")]
    [InlineData("sprk_signal", "neither")]
    [InlineData("sprk_decisionrecord", "matter")]
    [InlineData("sprk_decisionrecord", "project")]
    [InlineData("sprk_decisionrecord", "neither")]
    public async Task AHalfPairUnderTwoRoots_IsFilledWithTheOneOfItsType_OrCleared(string table, string type)
    {
        HalfPairWorld();
        var sentType = type switch { "matter" => MatterRecordType, "project" => ProjectRecordType, _ => DocumentRecordType };

        var (viaService, viaChat) = await BothPaths(table,
            Lookup("sprk_matter", "sprk_matter", OrdinaryMatter),
            Lookup("sprk_regardingproject", "sprk_project", HalfPairProject),
            CoreType(sentType));

        var noun = RecordCreationService.NounOf(table);
        if (type == "neither")
        {
            BothCreatedWithTheWarning(viaService, viaChat, RecordCreationWarning.CoreRecordClearedCode,
                RecordCreationService.CoreRecordCleared(noun), OrdinaryMatter, HalfPairProject);
            _appCreates.Should().OnlyContain(c => !c.Fields.Keys.Any(k => k.Contains("corerecord", StringComparison.OrdinalIgnoreCase)));
            return;
        }

        var filled = type == "matter" ? OrdinaryMatter : HalfPairProject;
        BothCreatedWithTheWarning(viaService, viaChat, RecordCreationWarning.CoreRecordFilledCode,
            RecordCreationService.CoreRecordFilled(noun), OrdinaryMatter, HalfPairProject);
        foreach (var (_, _, fields) in _appCreates)
        {
            CoreIdWritten(fields).Should().Be(filled.ToString("D"));
            CoreTypeWritten(fields).Should().Be($"/sprk_recordtype_refs({sentType:D})");
        }
    }

    // ── Ruling 5: no root ─────────────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "042c B' ruling 5, UAC-r2 (c): a core type on a row filed under no core record is CLEARED (neither column written) with the core_record_cleared warning, and the row is created with no core; no catalog read; service and chat")]
    [MemberData(nameof(CorePairTables))]
    public async Task AHalfPairWithNoRoot_IsCleared_AndTheRowIsCreatedWithNoCore(string table)
    {
        HalfPairWorld();

        var (viaService, viaChat) = await BothPaths(table, CoreType(MatterRecordType));

        BothCreatedWithTheWarning(viaService, viaChat, RecordCreationWarning.CoreRecordClearedCode,
            RecordCreationService.CoreRecordCleared(RecordCreationService.NounOf(table)), MatterRecordType);
        _appCreates.Should().OnlyContain(c => !c.Fields.Keys.Any(k => k.Contains("corerecord", StringComparison.OrdinalIgnoreCase)));
        _catalogTablesAsked.Should().BeEmpty("there is no root to read the catalog for");
    }

    // ── Ruling 7: a Secure root is filled and refused as row 8 ─────────────────────────────────────────────────────────

    [Theory(DisplayName = "042c B' ruling 7, UAC-r2 (e): a half pair filed under a SECURE matter is filled and takes the normal path to the owner: the existing row-8 409 NoOwnerSource, byte-identical (service ProblemDetails and chat code and words); nothing is written and no warning leaks")]
    [MemberData(nameof(CorePairTables))]
    public async Task AHalfPairFilledWithASecureRoot_GetsTheRow8Refusal(string table)
    {
        HalfPairWorld();
        var row8Service = await ServiceCreate(table, Lookup("sprk_matter", "sprk_matter", SecureMatter));
        var row8Chat = await CreateRecord(table, Lookup("sprk_matter", "sprk_matter", SecureMatter));

        var (viaService, viaChat) = await BothPaths(table, Lookup("sprk_matter", "sprk_matter", SecureMatter), CoreType(MatterRecordType));

        row8Service.Refusal.Should().NotBeNull("the baseline is design section 15.4 row 8");
        viaService.Refusal.Should().NotBeNull();
        viaService.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.OwnerUnresolved);
        viaService.Refusal.Code.Should().Be(RecordOwnerRefusal.NoOwnerSource);
        viaService.Refusal.OwnerDecision!.IsSecureOwner.Should().BeTrue();
        RecordCreationProblemMap.ToProblemDetails(viaService.Refusal, table, "c").Should().BeEquivalentTo(
            RecordCreationProblemMap.ToProblemDetails(row8Service.Refusal!, table, "c"), "status, code and words are row 8's");
        viaService.MappingWarnings.Should().BeEmpty("a refusal carries no warning");
        viaChat.Success.Should().BeFalse();
        viaChat.ErrorCode.Should().Be(row8Chat.ErrorCode).And.Be(ToolErrorCodes.ValidationFailed);
        viaChat.ErrorMessage.Should().Be(row8Chat.ErrorMessage);
        _appCreates.Should().BeEmpty();
        _catalogTablesAsked.Should().Contain("sprk_matter", "the Secure root was filled (step 6¾), then refused at step 7");

        // The refusal is row 8's whether or not the pair is filled (the Secure matter is also the filing parent), so the fill
        // itself is proved by step 6¾'s logged outcome: FILLED, never cleared. And no record id is logged at Information or above.
        HalfPairOutcomesLogged().Should().ContainSingle().Which.Should().EndWith(RecordCreationWarning.CoreRecordFilledCode);
        _halfPairLog.Entries.Where(e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Information)
            .Should().NotContain(e => e.Message.Contains(SecureMatter.ToString("D"), StringComparison.OrdinalIgnoreCase)
                                      || e.Message.Contains(SecureMatter.ToString("N"), StringComparison.OrdinalIgnoreCase),
                "UAC-r2: the root id is never logged at Information or above");
    }

    // ── Ruling 9: no pair, a full pair, an id with no type — unchanged ────────────────────────────────────────────────

    [Theory(DisplayName = "042c B' ruling 9 (must not fire): a row with NO core pair and one with a FULL pair are untouched by step 6¾ — no catalog read by table, no B' warning — and keep today's pair and owner (an id with no type stays refused: ACoreIdThatIsNotARecordIdOrHasNoType_IsRefusedInTheCorePairsOwnWords); service and chat")]
    [InlineData("sprk_signal", "no pair")]
    [InlineData("sprk_signal", "full pair")]
    [InlineData("sprk_decisionrecord", "no pair")]
    [InlineData("sprk_decisionrecord", "full pair")]
    public async Task ARowWithNoPairOrAFullPair_IsUntouchedByStep6AndThreeQuarters(string table, string pair)
    {
        HalfPairWorld();
        (string, JsonElement)[] core = pair == "no pair"
            ? []
            : [CoreType(MatterRecordType), ("sprk_corerecordid", JsonSerializer.SerializeToElement(CoreMatterInY.ToString("D")))];

        var (viaService, viaChat) = await BothPaths(table, [Lookup("sprk_matter", "sprk_matter", OrdinaryMatter), .. core]);

        viaService.Succeeded.Should().BeTrue(viaService.Refusal?.Detail);
        viaChat.Success.Should().BeTrue(viaChat.ErrorMessage);
        _catalogTablesAsked.Should().BeEmpty("step 6¾ reads nothing for a row that is not a half pair");
        viaService.MappingWarnings.Should().BeEmpty();
        viaChat.Warnings.Should().BeEmpty();
        foreach (var (_, _, fields) in _appCreates)
        {
            CoreIdWritten(fields).Should().Be(pair == "no pair" ? null : CoreMatterInY.ToString("D"));
            Owner(fields).Should().Be(pair == "no pair" ? Directory.ChildTeam : Directory.CustomerChildTeam, "today's owner");
        }
    }
}
