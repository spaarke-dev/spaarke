using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Insights.Routing;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Insights.Routing;

/// <summary>
/// Unit tests for <see cref="InsightsActionRouter"/> — Wave D4 / task 033 runtime
/// per-(area, type) routing for universal-ingest@v1.
/// </summary>
/// <remarks>
/// Covers:
/// <list type="bullet">
///   <item>Layer 1 routing — per-area hit, miss + fallback, missing hint pass-through</item>
///   <item>Layer 2 routing — per-pair hit, NULL action code gate-fail, no matrix row fallback,
///   missing inputs pass-through, missing ref row fallback, dangling action code fallback</item>
///   <item>Caching — repeat calls within TTL hit the cache</item>
/// </list>
/// </remarks>
public class InsightsActionRouterTests
{
    private const string GenericLayer1ActionCode = "INS-L1C@v1";
    private const string GenericLayer2ActionCode = "INS-L2X@v1";
    private const string PerAreaLayer1ActionCode = "INS-L1C-CTRNS@v1";
    private const string PerPairLayer2ActionCode = "INS-L2X-CTRNS-CLOSING@v1";

    // Post-FR-06 (task 023): the resolver normalizes "@v1"-suffixed inputs to the
    // clean form before the alternate-key lookup. Tests that exercise the lookup
    // path must mock against the normalized (clean) code.
    private const string PerAreaLayer1ActionCodeNormalized = "INS-L1C-CTRNS";
    private const string PerPairLayer2ActionCodeNormalized = "INS-L2X-CTRNS-CLOSING";

    private static readonly Guid GenericLayer1ActionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PerAreaLayer1ActionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PerPairLayer2ActionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid PracticeAreaRefId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid DocumentTypeRefId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid MatrixRowId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private static AnalysisAction DefaultLayer1Action() => new()
    {
        Id = GenericLayer1ActionId,
        Name = "Layer 1 Classify (Generic)",
        SystemPrompt = "generic-classification@v1",
        ActionType = ActionType.AiAnalysis
    };

    private static AnalysisAction DefaultLayer2Action() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Layer 2 Extract (Generic)",
        SystemPrompt = "generic-outcome-extraction@v1",
        ActionType = ActionType.AiAnalysis
    };

    private static AnalysisAction PerAreaLayer1Action() => new()
    {
        Id = PerAreaLayer1ActionId,
        Name = "Layer 1 Classify (CTRNS)",
        SystemPrompt = "ctrns-classification@v1",
        ActionType = ActionType.AiAnalysis
    };

    private static AnalysisAction PerPairLayer2Action() => new()
    {
        Id = PerPairLayer2ActionId,
        Name = "Layer 2 Extract (CTRNS × Closing)",
        SystemPrompt = "ctrns-closing-extraction@v1",
        ActionType = ActionType.AiAnalysis
    };

    private static (InsightsActionRouter Router, Mock<IGenericEntityService> Entity, Mock<IScopeResolverService> Scope, IMemoryCache Cache) CreateRouter()
    {
        var entity = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var scope = new Mock<IScopeResolverService>(MockBehavior.Strict);
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1024 });
        var router = new InsightsActionRouter(entity.Object, scope.Object, cache, NullLogger<InsightsActionRouter>.Instance);
        return (router, entity, scope, cache);
    }

    // ─── Layer 1 routing ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveLayer1_PassesThrough_WhenPracticeAreaHintIsNull()
    {
        var (router, entity, scope, _) = CreateRouter();
        var defaultAction = DefaultLayer1Action();

        var result = await router.ResolveLayer1ActionAsync(null, defaultAction, CancellationToken.None);

        result.Should().BeSameAs(defaultAction);
        entity.VerifyNoOtherCalls();
        scope.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResolveLayer1_PassesThrough_WhenPracticeAreaHintIsWhitespace()
    {
        var (router, entity, scope, _) = CreateRouter();
        var defaultAction = DefaultLayer1Action();

        var result = await router.ResolveLayer1ActionAsync("   ", defaultAction, CancellationToken.None);

        result.Should().BeSameAs(defaultAction);
        entity.VerifyNoOtherCalls();
        scope.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResolveLayer1_ReturnsPerAreaAction_WhenRowExists()
    {
        var (router, entity, scope, _) = CreateRouter();
        var defaultAction = DefaultLayer1Action();

        // Per-area row exists: alternate-key lookup returns an entity, scope resolver returns AnalysisAction.
        // Per FR-06 task 023: the resolver normalizes "@v1" suffix off the code at the lookup
        // boundary, so the alternate-key call uses the normalized (clean) form.
        entity.Setup(e => e.RetrieveByAlternateKeyAsync(
                "sprk_analysisaction",
                It.Is<KeyAttributeCollection>(k => k.Contains("sprk_actioncode") && (string)k["sprk_actioncode"] == PerAreaLayer1ActionCodeNormalized),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeActionEntity(PerAreaLayer1ActionId, PerAreaLayer1ActionCodeNormalized));

        scope.Setup(s => s.GetActionAsync(PerAreaLayer1ActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PerAreaLayer1Action());

        var result = await router.ResolveLayer1ActionAsync("CTRNS", defaultAction, CancellationToken.None);

        result.Id.Should().Be(PerAreaLayer1ActionId);
        result.Name.Should().Be("Layer 1 Classify (CTRNS)");
        entity.Verify(e => e.RetrieveByAlternateKeyAsync(
            "sprk_analysisaction", It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResolveLayer1_FallsBackToDefault_WhenPerAreaRowMissing()
    {
        var (router, entity, scope, _) = CreateRouter();
        var defaultAction = DefaultLayer1Action();

        entity.Setup(e => e.RetrieveByAlternateKeyAsync(
                "sprk_analysisaction", It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Entity sprk_analysisaction not found with provided alternate key values"));

        var result = await router.ResolveLayer1ActionAsync("MA", defaultAction, CancellationToken.None);

        result.Should().BeSameAs(defaultAction);
        scope.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResolveLayer1_CachesPerAreaActionAcrossCalls()
    {
        var (router, entity, scope, _) = CreateRouter();
        var defaultAction = DefaultLayer1Action();

        entity.Setup(e => e.RetrieveByAlternateKeyAsync(
                "sprk_analysisaction", It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeActionEntity(PerAreaLayer1ActionId, PerAreaLayer1ActionCode));

        scope.Setup(s => s.GetActionAsync(PerAreaLayer1ActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PerAreaLayer1Action());

        var first = await router.ResolveLayer1ActionAsync("CTRNS", defaultAction, CancellationToken.None);
        var second = await router.ResolveLayer1ActionAsync("ctrns", defaultAction, CancellationToken.None);

        first.Id.Should().Be(PerAreaLayer1ActionId);
        second.Id.Should().Be(PerAreaLayer1ActionId);
        // Should have hit Dataverse only ONCE — second call served from cache.
        entity.Verify(e => e.RetrieveByAlternateKeyAsync(
            "sprk_analysisaction", It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
        scope.Verify(s => s.GetActionAsync(PerAreaLayer1ActionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Layer 2 routing ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveLayer2_PassesThrough_WhenPracticeAreaHintIsMissing()
    {
        var (router, entity, scope, _) = CreateRouter();
        var defaultAction = DefaultLayer2Action();

        var result = await router.ResolveLayer2ActionAsync(null, "CTRNS_CLOSING_STATEMENT", defaultAction, CancellationToken.None);

        result.Decision.Should().Be(InsightsLayer2RoutingDecision.PassThrough);
        result.Action.Should().BeSameAs(defaultAction);
        entity.VerifyNoOtherCalls();
        scope.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResolveLayer2_PassesThrough_WhenDocumentTypeIsMissing()
    {
        var (router, entity, scope, _) = CreateRouter();
        var defaultAction = DefaultLayer2Action();

        var result = await router.ResolveLayer2ActionAsync("CTRNS", null, defaultAction, CancellationToken.None);

        result.Decision.Should().Be(InsightsLayer2RoutingDecision.PassThrough);
        result.Action.Should().BeSameAs(defaultAction);
        entity.VerifyNoOtherCalls();
        scope.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResolveLayer2_UsesPerPairAction_WhenMatrixCarriesNonNullActionCode()
    {
        var (router, entity, scope, _) = CreateRouter();
        var defaultAction = DefaultLayer2Action();

        SetupRefRow(entity, "sprk_practicearea_ref", "sprk_practiceareacode", "sprk_practicearea_refid", "CTRNS", PracticeAreaRefId);
        SetupRefRow(entity, "sprk_documenttype_ref", "sprk_documenttypecode", "sprk_documenttype_refid", "CTRNS_CLOSING_STATEMENT", DocumentTypeRefId);
        SetupMatrixRow(entity, PracticeAreaRefId, DocumentTypeRefId, MatrixRowId, layer2ActionCode: PerPairLayer2ActionCode);
        // Per FR-06 task 023: the resolver normalizes "@v1" suffix off the code at the lookup
        // boundary, so the alternate-key call uses the normalized (clean) form even when the
        // matrix carries the legacy "@v1"-suffixed code.
        SetupAlternateKeyAction(entity, PerPairLayer2ActionCodeNormalized, PerPairLayer2ActionId);

        scope.Setup(s => s.GetActionAsync(PerPairLayer2ActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PerPairLayer2Action());

        var result = await router.ResolveLayer2ActionAsync("CTRNS", "CTRNS_CLOSING_STATEMENT", defaultAction, CancellationToken.None);

        result.Decision.Should().Be(InsightsLayer2RoutingDecision.UsePerPairAction);
        result.Action.Id.Should().Be(PerPairLayer2ActionId);
        result.MatrixRowId.Should().Be(MatrixRowId);
        // ResolvedActionCode reflects the matrix-carried code (pre-normalization);
        // normalization happens only at the lookup boundary, not in the result payload.
        result.ResolvedActionCode.Should().Be(PerPairLayer2ActionCode);
    }

    [Fact]
    public async Task ResolveLayer2_GateFailsWithNullActionCode_WhenMatrixActionCodeIsNull()
    {
        // CTRNS × NDA pattern: matrix row exists but sprk_layer2actioncode is NULL by design.
        var (router, entity, scope, _) = CreateRouter();
        var defaultAction = DefaultLayer2Action();

        SetupRefRow(entity, "sprk_practicearea_ref", "sprk_practiceareacode", "sprk_practicearea_refid", "CTRNS", PracticeAreaRefId);
        SetupRefRow(entity, "sprk_documenttype_ref", "sprk_documenttypecode", "sprk_documenttype_refid", "CTRNS_NDA", DocumentTypeRefId);
        SetupMatrixRow(entity, PracticeAreaRefId, DocumentTypeRefId, MatrixRowId, layer2ActionCode: null);

        var result = await router.ResolveLayer2ActionAsync("CTRNS", "CTRNS_NDA", defaultAction, CancellationToken.None);

        result.Decision.Should().Be(InsightsLayer2RoutingDecision.GateFailNullActionCode);
        result.Action.Should().BeSameAs(defaultAction);
        result.MatrixRowId.Should().Be(MatrixRowId);
        // No action lookup should occur — gate-fail short-circuits.
        scope.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResolveLayer2_FallsBackToGeneric_WhenNoMatrixRowForPair()
    {
        // Unmapped (area, type) pair — e.g., IPPAT × CTRNS_NDA — no matrix row exists.
        var (router, entity, scope, _) = CreateRouter();
        var defaultAction = DefaultLayer2Action();

        SetupRefRow(entity, "sprk_practicearea_ref", "sprk_practiceareacode", "sprk_practicearea_refid", "IPPAT", PracticeAreaRefId);
        SetupRefRow(entity, "sprk_documenttype_ref", "sprk_documenttypecode", "sprk_documenttype_refid", "IPPAT_UNKNOWN_TYPE", DocumentTypeRefId);
        SetupMatrixEmpty(entity, PracticeAreaRefId, DocumentTypeRefId);

        var result = await router.ResolveLayer2ActionAsync("IPPAT", "IPPAT_UNKNOWN_TYPE", defaultAction, CancellationToken.None);

        result.Decision.Should().Be(InsightsLayer2RoutingDecision.FallbackToGeneric);
        result.Action.Should().BeSameAs(defaultAction);
        result.MatrixRowId.Should().BeNull();
        scope.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResolveLayer2_FallsBackToGeneric_WhenPracticeAreaRefRowMissing()
    {
        var (router, entity, scope, _) = CreateRouter();
        var defaultAction = DefaultLayer2Action();

        SetupRefRowMissing(entity, "sprk_practicearea_ref");

        var result = await router.ResolveLayer2ActionAsync("BOGUS", "BOGUS_TYPE", defaultAction, CancellationToken.None);

        result.Decision.Should().Be(InsightsLayer2RoutingDecision.FallbackToGeneric);
        result.Action.Should().BeSameAs(defaultAction);
        scope.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResolveLayer2_FallsBackToGeneric_WhenMatrixReferencesNonExistentAction()
    {
        // Defense-in-depth: matrix carries an action code, but the referenced action row
        // doesn't exist (SME authoring error or stale matrix).
        var (router, entity, scope, _) = CreateRouter();
        var defaultAction = DefaultLayer2Action();

        SetupRefRow(entity, "sprk_practicearea_ref", "sprk_practiceareacode", "sprk_practicearea_refid", "CTRNS", PracticeAreaRefId);
        SetupRefRow(entity, "sprk_documenttype_ref", "sprk_documenttypecode", "sprk_documenttype_refid", "CTRNS_CLOSING_STATEMENT", DocumentTypeRefId);
        SetupMatrixRow(entity, PracticeAreaRefId, DocumentTypeRefId, MatrixRowId, layer2ActionCode: "INS-L2X-DANGLING@v1");
        // Alternate-key lookup for the dangling action throws "not found".
        entity.Setup(e => e.RetrieveByAlternateKeyAsync(
                "sprk_analysisaction",
                It.Is<KeyAttributeCollection>(k => k.Contains("sprk_actioncode") && (string)k["sprk_actioncode"] == "INS-L2X-DANGLING@v1"),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Entity sprk_analysisaction not found"));

        var result = await router.ResolveLayer2ActionAsync("CTRNS", "CTRNS_CLOSING_STATEMENT", defaultAction, CancellationToken.None);

        result.Decision.Should().Be(InsightsLayer2RoutingDecision.FallbackToGeneric);
        result.Action.Should().BeSameAs(defaultAction);
        result.MatrixRowId.Should().Be(MatrixRowId);
    }

    [Fact]
    public async Task ResolveLayer2_CachesMatrixLookupAcrossCalls()
    {
        var (router, entity, scope, _) = CreateRouter();
        var defaultAction = DefaultLayer2Action();

        SetupRefRow(entity, "sprk_practicearea_ref", "sprk_practiceareacode", "sprk_practicearea_refid", "CTRNS", PracticeAreaRefId);
        SetupRefRow(entity, "sprk_documenttype_ref", "sprk_documenttypecode", "sprk_documenttype_refid", "CTRNS_CLOSING_STATEMENT", DocumentTypeRefId);
        SetupMatrixRow(entity, PracticeAreaRefId, DocumentTypeRefId, MatrixRowId, layer2ActionCode: PerPairLayer2ActionCode);
        // Per FR-06 task 023: alternate-key lookup uses the normalized (clean) form.
        SetupAlternateKeyAction(entity, PerPairLayer2ActionCodeNormalized, PerPairLayer2ActionId);

        scope.Setup(s => s.GetActionAsync(PerPairLayer2ActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PerPairLayer2Action());

        var first = await router.ResolveLayer2ActionAsync("CTRNS", "CTRNS_CLOSING_STATEMENT", defaultAction, CancellationToken.None);
        var second = await router.ResolveLayer2ActionAsync("ctrns", "ctrns_closing_statement", defaultAction, CancellationToken.None);

        first.Decision.Should().Be(InsightsLayer2RoutingDecision.UsePerPairAction);
        second.Decision.Should().Be(InsightsLayer2RoutingDecision.UsePerPairAction);

        // Matrix lookup runs only ONCE — second call serves from cache.
        entity.Verify(e => e.RetrieveMultipleAsync(
            It.Is<QueryExpression>(q => q.EntityName == "sprk_practicearea_documenttype"),
            It.IsAny<CancellationToken>()),
            Times.Once);
        // Per-pair action lookup runs only ONCE (cached on its own key, on the matrix-carried
        // code form — normalization is applied inside LoadActionByCodeAsync after the cache).
        entity.Verify(e => e.RetrieveByAlternateKeyAsync(
            "sprk_analysisaction",
            It.Is<KeyAttributeCollection>(k => k.Contains("sprk_actioncode") && (string)k["sprk_actioncode"] == PerPairLayer2ActionCodeNormalized),
            It.IsAny<string[]>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── Test helpers ─────────────────────────────────────────────────────────────

    private static Entity MakeActionEntity(Guid actionId, string actionCode)
    {
        var e = new Entity("sprk_analysisaction") { Id = actionId };
        e["sprk_analysisactionid"] = actionId;
        e["sprk_actioncode"] = actionCode;
        e["sprk_name"] = "Action " + actionCode;
        return e;
    }

    private static void SetupAlternateKeyAction(Mock<IGenericEntityService> entity, string actionCode, Guid actionId)
    {
        entity.Setup(e => e.RetrieveByAlternateKeyAsync(
                "sprk_analysisaction",
                It.Is<KeyAttributeCollection>(k => k.Contains("sprk_actioncode") && (string)k["sprk_actioncode"] == actionCode),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeActionEntity(actionId, actionCode));
    }

    private static void SetupRefRow(
        Mock<IGenericEntityService> entity,
        string refEntityName, string codeColumn, string idColumn,
        string code, Guid refId)
    {
        var e = new Entity(refEntityName) { Id = refId };
        e[idColumn] = refId;

        var ec = new EntityCollection(new List<Entity> { e });

        entity.Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q =>
                    q.EntityName == refEntityName
                    && HasCondition(q.Criteria, codeColumn, code.ToUpperInvariant())),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ec);
    }

    private static void SetupRefRowMissing(Mock<IGenericEntityService> entity, string refEntityName)
    {
        entity.Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == refEntityName),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>()));
    }

    private static void SetupMatrixRow(
        Mock<IGenericEntityService> entity,
        Guid practiceAreaRefId, Guid documentTypeRefId,
        Guid matrixRowId, string? layer2ActionCode)
    {
        var m = new Entity("sprk_practicearea_documenttype") { Id = matrixRowId };
        m["sprk_practicearea_documenttypeid"] = matrixRowId;
        m["sprk_layer2actioncode"] = layer2ActionCode;
        var ec = new EntityCollection(new List<Entity> { m });

        entity.Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q =>
                    q.EntityName == "sprk_practicearea_documenttype"
                    && HasCondition(q.Criteria, "sprk_practicearea", practiceAreaRefId)
                    && HasCondition(q.Criteria, "sprk_documenttype", documentTypeRefId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ec);
    }

    private static void SetupMatrixEmpty(
        Mock<IGenericEntityService> entity,
        Guid practiceAreaRefId, Guid documentTypeRefId)
    {
        entity.Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q =>
                    q.EntityName == "sprk_practicearea_documenttype"
                    && HasCondition(q.Criteria, "sprk_practicearea", practiceAreaRefId)
                    && HasCondition(q.Criteria, "sprk_documenttype", documentTypeRefId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>()));
    }

    private static bool HasCondition(FilterExpression filter, string attributeName, object expectedValue)
    {
        foreach (var c in filter.Conditions)
        {
            if (string.Equals(c.AttributeName, attributeName, StringComparison.OrdinalIgnoreCase)
                && c.Values.Count > 0
                && Equals(c.Values[0], expectedValue))
            {
                return true;
            }
        }
        foreach (var subFilter in filter.Filters)
        {
            if (HasCondition(subFilter, attributeName, expectedValue)) return true;
        }
        return false;
    }
}
