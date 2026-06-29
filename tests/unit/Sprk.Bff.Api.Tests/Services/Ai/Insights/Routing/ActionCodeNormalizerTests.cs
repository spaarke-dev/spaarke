using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Insights.Routing;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Insights.Routing;

/// <summary>
/// Unit tests for <see cref="ActionCodeNormalizer"/> + the backward-compat path
/// inside <see cref="InsightsActionRouter"/>.
/// </summary>
/// <remarks>
/// Project: spaarke-ai-platform-chat-routing-redesign-r1, task 023.
/// FR-06: new sprk_actioncode values are kebab-case without "@v1"; legacy
/// "@v1"-suffixed values remain valid in the same column during the stabilization
/// window. Both forms MUST resolve to the same Dataverse row at the lookup
/// boundary, and an <c>actionCodeFormat</c> telemetry tag MUST report the input
/// form so the deprecation-window decay can be measured.
/// </remarks>
public class ActionCodeNormalizerTests
{
    // ─── Pure normalizer ──────────────────────────────────────────────────────────

    [Fact]
    public void NormalizeActionCode_StripsV1Suffix()
    {
        ActionCodeNormalizer.Normalize("foo-bar@v1").Should().Be("foo-bar");
    }

    [Fact]
    public void NormalizeActionCode_StripsV1Suffix_LegacyUpperCaseCode()
    {
        // Existing dataset includes codes like SUM-CHAT@v1, INS-L1C-CTRNS@v1, etc.
        ActionCodeNormalizer.Normalize("SUM-CHAT@v1").Should().Be("SUM-CHAT");
        ActionCodeNormalizer.Normalize("INS-L1C-CTRNS@v1").Should().Be("INS-L1C-CTRNS");
    }

    [Fact]
    public void NormalizeActionCode_LeavesCleanSlugUnchanged()
    {
        ActionCodeNormalizer.Normalize("foo-bar").Should().Be("foo-bar");
        ActionCodeNormalizer.Normalize("summarize-nda").Should().Be("summarize-nda");
        ActionCodeNormalizer.Normalize("INS-FETCH-KPI").Should().Be("INS-FETCH-KPI");
    }

    [Fact]
    public void NormalizeActionCode_HandlesNull()
    {
        ActionCodeNormalizer.Normalize(null).Should().BeNull();
    }

    [Fact]
    public void NormalizeActionCode_HandlesEmpty()
    {
        ActionCodeNormalizer.Normalize(string.Empty).Should().Be(string.Empty);
    }

    [Fact]
    public void NormalizeActionCode_DoesNotStripMidStringV1()
    {
        // Only a TRAILING "@v1" should be stripped — a code like "foo@v1-bar"
        // is not a recognized convention and must pass through verbatim.
        ActionCodeNormalizer.Normalize("foo@v1-bar").Should().Be("foo@v1-bar");
    }

    [Fact]
    public void NormalizeActionCode_IsCaseSensitive_OnlyExactAtV1Stripped()
    {
        // "@V1" upper-case is NOT the convention (legacy data is lowercase "@v1").
        ActionCodeNormalizer.Normalize("FOO@V1").Should().Be("FOO@V1");
    }

    // ─── Format (telemetry tag) ──────────────────────────────────────────────────

    [Fact]
    public void FormatActionCode_ReturnsV1Suffix_ForSuffixedInput()
    {
        ActionCodeNormalizer.Format("foo-bar@v1").Should().Be("v1Suffix");
        ActionCodeNormalizer.Format("SUM-CHAT@v1").Should().Be("v1Suffix");
    }

    [Fact]
    public void FormatActionCode_ReturnsClean_ForCleanInput()
    {
        ActionCodeNormalizer.Format("foo-bar").Should().Be("clean");
        ActionCodeNormalizer.Format("summarize-nda").Should().Be("clean");
    }

    [Fact]
    public void FormatActionCode_ReturnsClean_ForNullOrEmpty()
    {
        ActionCodeNormalizer.Format(null).Should().Be("clean");
        ActionCodeNormalizer.Format(string.Empty).Should().Be("clean");
    }

    // ─── End-to-end resolution via InsightsActionRouter ──────────────────────────

    private const string PerPairActionCodeSuffixed = "INS-L2X-CTRNS-CLOSING@v1";
    private const string PerPairActionCodeClean = "INS-L2X-CTRNS-CLOSING";

    private static readonly Guid PerPairActionId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
    private static readonly Guid PracticeAreaRefId = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");
    private static readonly Guid DocumentTypeRefId = Guid.Parse("cccccccc-1111-2222-3333-444444444444");
    private static readonly Guid MatrixRowId = Guid.Parse("dddddddd-1111-2222-3333-444444444444");

    private static AnalysisAction MakeDefaultLayer2Action() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Layer 2 Extract (Generic)",
        SystemPrompt = "generic-outcome-extraction",
        ExecutorType = ExecutorType.AiAnalysis
    };

    private static AnalysisAction MakePerPairAction() => new()
    {
        Id = PerPairActionId,
        Name = "Layer 2 Extract (CTRNS x Closing)",
        SystemPrompt = "ctrns-closing-extraction",
        ExecutorType = ExecutorType.AiAnalysis
    };

    /// <summary>
    /// End-to-end resolution test: a matrix row carrying the LEGACY "@v1"-suffixed
    /// action code resolves to the same per-pair action as a matrix row carrying
    /// the new clean form would — because the resolver normalizes at the lookup
    /// boundary. This is the core FR-06 backward-compat invariant.
    /// </summary>
    [Fact]
    public async Task LookupByCode_Resolves_BothCleanAndV1SuffixedToSameAction()
    {
        var (router1, entity1, scope1, _) = CreateRouter();
        var (router2, entity2, scope2, _) = CreateRouter();
        var defaultAction = MakeDefaultLayer2Action();

        // Resolver 1 — matrix carries clean code "INS-L2X-CTRNS-CLOSING"
        SetupRefRows(entity1);
        SetupMatrixRow(entity1, PerPairActionCodeClean);
        // After normalization (no-op for clean form), the lookup uses the clean code.
        SetupAlternateKeyAction(entity1, PerPairActionCodeClean, PerPairActionId);
        scope1.Setup(s => s.GetActionAsync(PerPairActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePerPairAction());

        // Resolver 2 — matrix carries legacy code "INS-L2X-CTRNS-CLOSING@v1"
        SetupRefRows(entity2);
        SetupMatrixRow(entity2, PerPairActionCodeSuffixed);
        // After normalization (strips "@v1"), the lookup MUST use the clean code
        // — even though the matrix carried the suffixed form.
        SetupAlternateKeyAction(entity2, PerPairActionCodeClean, PerPairActionId);
        scope2.Setup(s => s.GetActionAsync(PerPairActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePerPairAction());

        var result1 = await router1.ResolveLayer2ActionAsync("CTRNS", "CTRNS_CLOSING", defaultAction, CancellationToken.None);
        var result2 = await router2.ResolveLayer2ActionAsync("CTRNS", "CTRNS_CLOSING", defaultAction, CancellationToken.None);

        result1.Decision.Should().Be(InsightsLayer2RoutingDecision.UsePerPairAction);
        result2.Decision.Should().Be(InsightsLayer2RoutingDecision.UsePerPairAction);
        result1.Action.Id.Should().Be(PerPairActionId);
        result2.Action.Id.Should().Be(PerPairActionId);

        // Both routers MUST have done exactly one alternate-key lookup against
        // the CLEAN code form — proving normalization happened at the boundary.
        entity1.Verify(e => e.RetrieveByAlternateKeyAsync(
            "sprk_analysisaction",
            It.Is<KeyAttributeCollection>(k => k.Contains("sprk_actioncode") && (string)k["sprk_actioncode"] == PerPairActionCodeClean),
            It.IsAny<string[]>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
        entity2.Verify(e => e.RetrieveByAlternateKeyAsync(
            "sprk_analysisaction",
            It.Is<KeyAttributeCollection>(k => k.Contains("sprk_actioncode") && (string)k["sprk_actioncode"] == PerPairActionCodeClean),
            It.IsAny<string[]>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Telemetry test: the <c>actionCodeFormat</c> log property reflects the
    /// INPUT form so the deprecation window's call-rate decay is measurable
    /// from log aggregation. Uses a capturing logger to inspect property values.
    /// </summary>
    [Fact]
    public async Task LookupByCode_LogsActionCodeFormatTag_ReflectingInputForm()
    {
        // Suffixed input → tag "v1Suffix"
        var suffixedLogger = new CapturingLogger<InsightsActionRouter>();
        var (router1, entity1, scope1, _) = CreateRouterWithLogger(suffixedLogger);
        SetupRefRows(entity1);
        SetupMatrixRow(entity1, PerPairActionCodeSuffixed);
        SetupAlternateKeyAction(entity1, PerPairActionCodeClean, PerPairActionId);
        scope1.Setup(s => s.GetActionAsync(PerPairActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePerPairAction());

        await router1.ResolveLayer2ActionAsync("CTRNS", "CTRNS_CLOSING", MakeDefaultLayer2Action(), CancellationToken.None);

        suffixedLogger.CapturedFormatTags.Should().Contain("v1Suffix");

        // Clean input → tag "clean"
        var cleanLogger = new CapturingLogger<InsightsActionRouter>();
        var (router2, entity2, scope2, _) = CreateRouterWithLogger(cleanLogger);
        SetupRefRows(entity2);
        SetupMatrixRow(entity2, PerPairActionCodeClean);
        SetupAlternateKeyAction(entity2, PerPairActionCodeClean, PerPairActionId);
        scope2.Setup(s => s.GetActionAsync(PerPairActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePerPairAction());

        await router2.ResolveLayer2ActionAsync("CTRNS", "CTRNS_CLOSING", MakeDefaultLayer2Action(), CancellationToken.None);

        cleanLogger.CapturedFormatTags.Should().Contain("clean");
        cleanLogger.CapturedFormatTags.Should().NotContain("v1Suffix");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private static (InsightsActionRouter Router, Mock<IGenericEntityService> Entity, Mock<IScopeResolverService> Scope, IMemoryCache Cache) CreateRouter()
        => CreateRouterWithLogger(NullLogger<InsightsActionRouter>.Instance);

    private static (InsightsActionRouter Router, Mock<IGenericEntityService> Entity, Mock<IScopeResolverService> Scope, IMemoryCache Cache) CreateRouterWithLogger(
        ILogger<InsightsActionRouter> logger)
    {
        var entity = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var scope = new Mock<IScopeResolverService>(MockBehavior.Strict);
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1024 });
        var router = new InsightsActionRouter(entity.Object, scope.Object, cache, logger);
        return (router, entity, scope, cache);
    }

    private static void SetupRefRows(Mock<IGenericEntityService> entity)
    {
        SetupRefRow(entity, "sprk_practicearea_ref", "sprk_practiceareacode", "sprk_practicearea_refid", "CTRNS", PracticeAreaRefId);
        SetupRefRow(entity, "sprk_documenttype_ref", "sprk_documenttypecode", "sprk_documenttype_refid", "CTRNS_CLOSING", DocumentTypeRefId);
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
                It.Is<Microsoft.Xrm.Sdk.Query.QueryExpression>(q =>
                    q.EntityName == refEntityName
                    && HasCondition(q.Criteria, codeColumn, code.ToUpperInvariant())),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ec);
    }

    private static void SetupMatrixRow(Mock<IGenericEntityService> entity, string layer2ActionCode)
    {
        var m = new Entity("sprk_practicearea_documenttype") { Id = MatrixRowId };
        m["sprk_practicearea_documenttypeid"] = MatrixRowId;
        m["sprk_layer2actioncode"] = layer2ActionCode;
        var ec = new EntityCollection(new List<Entity> { m });

        entity.Setup(s => s.RetrieveMultipleAsync(
                It.Is<Microsoft.Xrm.Sdk.Query.QueryExpression>(q =>
                    q.EntityName == "sprk_practicearea_documenttype"
                    && HasCondition(q.Criteria, "sprk_practicearea", PracticeAreaRefId)
                    && HasCondition(q.Criteria, "sprk_documenttype", DocumentTypeRefId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ec);
    }

    private static void SetupAlternateKeyAction(Mock<IGenericEntityService> entity, string actionCode, Guid actionId)
    {
        var e = new Entity("sprk_analysisaction") { Id = actionId };
        e["sprk_analysisactionid"] = actionId;
        e["sprk_actioncode"] = actionCode;
        e["sprk_name"] = "Action " + actionCode;

        entity.Setup(s => s.RetrieveByAlternateKeyAsync(
                "sprk_analysisaction",
                It.Is<KeyAttributeCollection>(k => k.Contains("sprk_actioncode") && (string)k["sprk_actioncode"] == actionCode),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(e);
    }

    private static bool HasCondition(Microsoft.Xrm.Sdk.Query.FilterExpression filter, string attributeName, object expectedValue)
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
        foreach (var sub in filter.Filters)
        {
            if (HasCondition(sub, attributeName, expectedValue)) return true;
        }
        return false;
    }

    /// <summary>
    /// Minimal capturing logger that extracts the <c>ActionCodeFormat</c>
    /// structured-log property value from each log call, so tests can assert
    /// the telemetry tag was emitted with the expected enum value.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> CapturedFormatTags { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (state is IReadOnlyList<KeyValuePair<string, object?>> kvps)
            {
                foreach (var kv in kvps)
                {
                    if (kv.Key == "ActionCodeFormat" && kv.Value is string s)
                    {
                        CapturedFormatTags.Add(s);
                    }
                }
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
