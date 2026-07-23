using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// spaarkeai-assistant-enhancements-r1 task 031 (FR-E3 / FR-E4) — the mechanical, reflection-backed
/// proof of the ADR-039 "preference ≠ permission; ONE decider" invariant for the User Model shipped by
/// task 030 (<see cref="StatedProfileReader"/> / <c>ContextBinder.userFragment</c>). Three provable
/// assertions:
/// <list type="number">
///   <item><see cref="AgentToolFilterContext"/> — the grounding pre-filter's ONLY input — carries no
///   profile/memory-derived member. The deny-list guard is calibrated against the REAL
///   <see cref="StatedProfile"/> vocabulary (task 030's payload type) so it is PROVEN to fire on a
///   realistic leak, not just assumed to (red-before-green).</item>
///   <item>Because assertion 1 holds (and is independently re-proven via the ctor-shape assertion below),
///   a profile has no channel into <see cref="AgentToolProjection.PreFilter"/> — the grounded-tool set
///   for a profiled vs. an unprofiled caller under identical session facts is byte-identical.</item>
///   <item>The chip-order-producing C# surface that exists TODAY
///   (<see cref="ConsumerRoutingService"/> mapping <c>sprk_chiptransitions</c> JSON authored order onto
///   <see cref="Binding.ChipTransitions"/>) is deterministic and carries no model/completion dependency.
///   <b>Adaptation note</b>: the deterministic PREFERENCE-KEYED reorder-for-display described in the
///   project CLAUDE.md ("the profile may... deterministically reorder already-grounded chips for
///   display") is a Suggested-Next-Steps-cards concern that is explicitly future task 043
///   (client-side <c>SpaarkeAi</c> SNS cards per
///   <c>projects/spaarkeai-assistant-enhancements-r1/tasks/043-suggested-next-steps-cards.poml</c>) and
///   does not exist as a C# surface in this codebase yet. Per this task's instruction to "not fabricate
///   a test against a non-existent API," this file instead proves the invariant at the chip-order
///   surface that DOES exist today: order is authored-catalog-JSON order, not model- or profile-driven.
///   When task 043 lands a preference-keyed reorder, its own test should extend (not replace) this one.</item>
/// </list>
/// </summary>
public sealed class PreferenceNotPermissionInvariantTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Assertion 1: AgentToolFilterContext carries no profile/memory-derived member
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Deny-list of profile/memory concept substrings (case-insensitive), per this task's spec.
    /// Calibrated below against the REAL <see cref="StatedProfile"/> member vocabulary — see
    /// <see cref="ProfileMemoryDenyList_CatchesEveryStatedProfileMemberName_ProvingTheGuardWouldFireOnALeak"/>.
    /// </summary>
    private static readonly string[] ProfileMemoryDenyList =
    {
        "role", "profile", "practicearea", "focusarea", "office",
        "preference", "memory", "statedprofile", "userfragment",
    };

    [Fact]
    public void AgentToolFilterContext_Members_CarryNoProfileOrMemoryDerivedSignal()
    {
        var type = typeof(AgentToolFilterContext);
        var memberNames = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(c => c.GetParameters())
                .Select(p => p.Name!))
            .Distinct()
            .ToList();

        memberNames.Should().NotBeEmpty(
            "the reflection probe must actually enumerate real members to be a load-bearing guard");

        var leaked = memberNames
            .Where(name => ProfileMemoryDenyList.Any(d => name.Contains(d, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        leaked.Should().BeEmpty(
            "AgentToolFilterContext (ADR-039) is a structural session-fact record consumed by the " +
            "grounding pre-filter; it must NEVER carry a profile/memory-derived member — a stated profile " +
            "biases the one agent turn's PROMPT ONLY (ContextBinder.userFragment), never the closed " +
            "grounded-tool set. This assertion is the mechanical guard against the User Model quietly " +
            $"becoming a second decider. Leaked member(s): {string.Join(", ", leaked)}");
    }

    [Fact]
    public void ProfileMemoryDenyList_CatchesEveryStatedProfileMemberName_ProvingTheGuardWouldFireOnALeak()
    {
        // Calibration / red-before-green proof: every REAL stated-fact member of StatedProfile (task
        // 030's producer payload type) must be caught by the deny-list above. If a member shaped like
        // one of these (or named identically) were ever added to AgentToolFilterContext, the previous
        // test would fail. HasAny is a computed presence flag (not a stated-fact carrier) and is
        // intentionally excluded — its generic name would never leak profile CONTENT even if copied
        // verbatim.
        var statedProfileContentMembers = typeof(StatedProfile)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(name => name != nameof(StatedProfile.HasAny))
            .ToList();

        statedProfileContentMembers.Should().Contain(new[]
        {
            nameof(StatedProfile.RoleLabel),
            nameof(StatedProfile.PracticeAreaNames),
            nameof(StatedProfile.FocusAreas),
            nameof(StatedProfile.OfficeLocation),
            nameof(StatedProfile.AssistantPreferences),
        }, "the calibration set must reflect the actual task-030 StatedProfile shape, not a stale copy");

        foreach (var name in statedProfileContentMembers)
        {
            ProfileMemoryDenyList.Any(d => name.Contains(d, StringComparison.OrdinalIgnoreCase))
                .Should().BeTrue(
                    $"'{name}' is a real StatedProfile member; the deny-list guard on AgentToolFilterContext " +
                    "MUST be calibrated to catch it (or an equivalently-named leak) — this proves the guard " +
                    "is a live tripwire, not a vacuous pass.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Assertion 2: profile injection does not change the grounded-tool set
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AgentToolFilterContext_ConstructionSurface_ExposesNoProfileChannel()
    {
        var ctor = typeof(AgentToolFilterContext)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Should().ContainSingle(
                "the record has exactly one public construction path — no overload could smuggle in a " +
                "profile parameter").Subject;

        var parameterShapes = ctor.GetParameters()
            .Select(p => $"{p.ParameterType.Name} {p.Name}")
            .ToList();

        parameterShapes.Should().BeEquivalentTo(new[]
        {
            "String Surface",
            "Boolean HasSessionFiles",
            "Boolean HasActiveDocument",
            "Boolean HasAnalysisBinding",
            // Added by task 044 (FR-H1): a STRUCTURAL host-record fact (ChatHostContext.IsValid()), not a
            // profile/learned signal — grounding stays preference-independent.
            "Boolean HasAttachedRecord",
        }, "these structural session facts are the ONLY inputs the grounding pre-filter ever sees — " +
           "there is no parameter, profile or otherwise, through which a stated or learned user signal " +
           "could reach AgentToolProjection.PreFilter");
    }

    [Fact]
    public void PreFilter_SameSessionFactsForProfiledAndUnprofiledCaller_YieldsIdenticalGroundedToolSet()
    {
        // Two callers with materially different STATED profiles (task 030's payload type) but the SAME
        // structural session facts. Because AgentToolFilterContext accepts no profile parameter
        // (proven above), ANY code building a filter context for these two callers under the same
        // session state produces record-EQUAL contexts — there is no dimension along which a profile
        // could perturb it.
        var profiledCallerProfile = new StatedProfile
        {
            RoleLabel = "Partner",
            PracticeAreaNames = new[] { "Corporate", "Litigation" },
            OfficeLocation = "New York",
            AssistantPreferences = "Concise, cite sources",
        };
        StatedProfile? unprofiledCallerProfile = null;

        // The profile parameter is accepted here ONLY to make the "it is never consulted" point
        // explicit in this test — real AgentToolFilterContext construction (SprkChatAgentFactory) has
        // no profile parameter to ignore in the first place (see the ctor-shape assertion above).
        static AgentToolFilterContext BuildContextIgnoringProfile(StatedProfile? profile) =>
            new(AgentToolFilterContext.AssistantSurface,
                HasSessionFiles: true, HasActiveDocument: true, HasAnalysisBinding: false);

        var contextForProfiledCaller = BuildContextIgnoringProfile(profiledCallerProfile);
        var contextForUnprofiledCaller = BuildContextIgnoringProfile(unprofiledCallerProfile);

        contextForProfiledCaller.Should().Be(contextForUnprofiledCaller,
            "record equality proves the two contexts are structurally IDENTICAL for the profiled vs. " +
            "unprofiled caller — preference has no channel to bias the grounding decision");

        var assistantSurfaceTool = CreateCapabilityTool(CreateCapabilityBinding(
            "chat-summarize", surfaces: new[] { "assistant" }));
        var recordFormOnlyTool = CreateCapabilityTool(CreateCapabilityBinding(
            "chat-record-form-only", surfaces: new[] { "record-form" }));
        var tools = new AIFunction[] { assistantSurfaceTool, recordFormOnlyTool };

        var groundedForProfiledCaller = AgentToolProjection.PreFilter(tools, contextForProfiledCaller)
            .Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var groundedForUnprofiledCaller = AgentToolProjection.PreFilter(tools, contextForUnprofiledCaller)
            .Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

        groundedForProfiledCaller.Should().Equal(groundedForUnprofiledCaller,
            "the grounded-tool set is identical for a profiled and an unprofiled caller given the same " +
            "session facts — preference never grants or revokes a capability (ADR-039 preference≠permission)");
        groundedForProfiledCaller.Should().ContainSingle().Which.Should().Be(assistantSurfaceTool.Name,
            "grounding is decided ONLY by the sprk_surfaces catalog column against the session surface — " +
            "the record-form-only tool is excluded on BOTH sides identically, never admitted by preference");
    }

    private static Binding CreateCapabilityBinding(string consumerType, string[] surfaces) => new()
    {
        BindingId = Guid.NewGuid(),
        ConsumerType = consumerType,
        ToolDescription = $"{consumerType} capability (test fixture).",
        Surfaces = surfaces,
    };

    private static BindingCapabilityTool CreateCapabilityTool(Binding binding) =>
        new(binding, new ServiceCollection().BuildServiceProvider(), "tenant-test", "session-test",
            NullLogger.Instance);

    // ─────────────────────────────────────────────────────────────────────────
    // Assertion 3: chip order is deterministic + not model-driven
    // (no C# preference-keyed reorder-for-display surface exists yet — see class summary)
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly Guid PlaybookId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private static Entity BuildEntityWithChipTransitions(string chipTransitionsJson)
    {
        var entity = new Entity("sprk_playbookconsumer");
        entity["sprk_playbookconsumerid"] = Guid.NewGuid();
        entity["sprk_consumertype"] = "chat-summarize";
        entity["sprk_consumercode"] = "default";
        entity["sprk_environment"] = "*";
        entity["sprk_priority"] = 500;
        entity["sprk_enabled"] = true;
        entity["sprk_playbook"] = new EntityReference("sprk_analysisplaybook", PlaybookId);
        entity["sprk_chiptransitions"] = chipTransitionsJson;
        return entity;
    }

    private static ConsumerRoutingService CreateRoutingService(Entity entity)
    {
        var entityServiceMock = new Mock<IGenericEntityService>();
        entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { entity }));
        var envMock = new Mock<IHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns("dev");

        // Fresh IMemoryCache per instance — proves the observed determinism is NOT an artifact of the
        // 5-minute cache serving back the same cached object twice.
        return new ConsumerRoutingService(
            entityServiceMock.Object,
            new MemoryCache(new MemoryCacheOptions()),
            envMock.Object,
            NullLogger<ConsumerRoutingService>.Instance);
    }

    [Fact]
    public async Task ConsumerRoutingService_ChipTransitionsOrder_IsDeterministicAcrossIndependentResolves()
    {
        const string chipJson = """
            [
              {"target_binding_id":"11111111-1111-1111-1111-111111111111","chip_label":"Send welcome email"},
              {"target_binding_id":"22222222-2222-2222-2222-222222222222","chip_label":"Save to matter"},
              {"target_binding_id":"33333333-3333-3333-3333-333333333333","chip_label":"Schedule follow-up"}
            ]
            """;
        var authoredOrder = new[]
        {
            "11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222",
            "33333333-3333-3333-3333-333333333333",
        };

        // Two INDEPENDENT service instances (own cache, own mock) resolving the SAME input — same
        // input must yield the same output order, and that order must be the authored JSON order (no
        // sort, no rank, no model call reorders it — this IS the pure-function proof for the surface
        // that exists today).
        var firstResolve = await CreateRoutingService(BuildEntityWithChipTransitions(chipJson))
            .ResolveBindingAsync("chat-summarize");
        var secondResolve = await CreateRoutingService(BuildEntityWithChipTransitions(chipJson))
            .ResolveBindingAsync("chat-summarize");

        firstResolve.Should().NotBeNull();
        secondResolve.Should().NotBeNull();

        var firstOrder = firstResolve!.ChipTransitions.Select(c => c.TargetBindingId).ToList();
        var secondOrder = secondResolve!.ChipTransitions.Select(c => c.TargetBindingId).ToList();

        firstOrder.Should().Equal(authoredOrder,
            "the chip list order today IS the maker-authored sprk_chiptransitions JSON array order — " +
            "no reorder step exists between Dataverse and the Binding contract");
        secondOrder.Should().Equal(authoredOrder,
            "a fresh resolve of the identical input reproduces the identical order (determinism)");
        firstOrder.Should().Equal(secondOrder,
            "same input -> same output order across two wholly independent service instances");
    }

    [Fact]
    public void ConsumerRoutingService_ConstructorSurface_HasNoModelOrCompletionDependency()
    {
        // The chip-order-producing surface (ConsumerRoutingService.ResolveBindingAsync -> MapBinding ->
        // ParseJsonList<ChipTransition>) is proven here to have no AI completion dependency at all: its
        // ONE constructor's dependency graph is Dataverse + cache + host-env + logging only.
        var ctor = typeof(ConsumerRoutingService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Should().ContainSingle().Subject;
        var dependencyTypeNames = ctor.GetParameters()
            .Select(p => p.ParameterType.FullName ?? p.ParameterType.Name)
            .ToList();

        var modelDenyList = new[]
        {
            "openai", "chatclient", "completion", "aiagent", "iplaybookservice", "microsoft.extensions.ai",
        };

        var suspect = dependencyTypeNames
            .Where(t => modelDenyList.Any(d => t.Contains(d, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        suspect.Should().BeEmpty(
            "the chip-order-producing surface must have no model/completion dependency — chip order is " +
            "a deterministic mapping of catalog data, never an LLM ranking. Dependencies were: " +
            string.Join(", ", dependencyTypeNames));
    }
}
