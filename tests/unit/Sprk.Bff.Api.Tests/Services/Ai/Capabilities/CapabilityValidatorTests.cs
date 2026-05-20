using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Ai.Capabilities;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Capabilities;

/// <summary>
/// Unit tests for <see cref="CapabilityValidator"/> (AIPU2-016).
///
/// Coverage:
///   1. Unknown capability (not in manifest) — not relevant to validator (manifest pre-filters).
///   2. Kill switch (per-capability) — excluded.
///   3. Global kill switch — all candidates excluded.
///   4. Tenant mismatch — excluded.
///   5. Tenant restriction empty — allowed for any tenant.
///   6. Missing required role — excluded.
///   7. Required role present — allowed.
///   8. Missing required context key — excluded.
///   9. Required context key present but "false" — excluded.
///  10. Required context key present and "true" — allowed.
///  11. All checks pass — capability included.
///  12. Multiple candidates — only valid ones pass through.
///  13. Empty candidate list — returns empty list without error.
///  14. No annotations on description — all checks pass (unrestricted).
/// </summary>
public sealed class CapabilityValidatorTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private const string AnyTenant = "https://spaarkedev1.crm.dynamics.com";

    /// <summary>
    /// Builds a <see cref="CapabilityManifestEntry"/> with sensible defaults.
    /// Use the description overload to embed [RequiredRole=...] or [RequiredContext=...] annotations.
    /// </summary>
    private static CapabilityManifestEntry MakeEntry(
        string name,
        IReadOnlyList<string>? tenantRestrictions = null,
        string? description = null)
    {
        return new CapabilityManifestEntry(
            CapabilityName: name,
            Description: description ?? $"Description for {name}",
            KeywordHints: ["hint"],
            PlaybookId: null,
            ToolNames: [],
            IsEnabled: true,
            TenantRestrictions: tenantRestrictions ?? []);
    }

    /// <summary>
    /// Builds a <see cref="CapabilityValidationContext"/> with no conversation context and
    /// a ClaimsPrincipal representing an authenticated user without any roles.
    /// </summary>
    private static CapabilityValidationContext MakeContext(
        string tenantUrl = AnyTenant,
        string[]? roles = null,
        Dictionary<string, string>? conversationContext = null,
        string userId = "user-oid-123")
    {
        var claims = new List<Claim>
        {
            new("oid", userId)
        };

        foreach (var role in roles ?? [])
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);

        return new CapabilityValidationContext(
            User: principal,
            TenantEnvironmentUrl: tenantUrl,
            ConversationContext: conversationContext
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Builds a <see cref="CapabilityValidator"/> backed by the given configuration values.
    /// </summary>
    private static CapabilityValidator BuildValidator(
        Dictionary<string, string?>? configValues = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues ?? [])
            .Build();

        return new CapabilityValidator(config, NullLogger<CapabilityValidator>.Instance);
    }

    // ── Test 1: Empty candidate list ──────────────────────────────────────────

    [Fact]
    public async Task FilterAsync_ReturnsEmpty_WhenCandidateListIsEmpty()
    {
        var validator = BuildValidator();
        var ctx = MakeContext();

        var result = await validator.FilterAsync([], ctx);

        result.Should().BeEmpty("empty input must produce empty output without errors");
    }

    // ── Test 2: Per-capability kill switch ────────────────────────────────────

    [Fact]
    public async Task FilterAsync_ExcludesCapability_WhenKillSwitchActive()
    {
        var config = new Dictionary<string, string?>
        {
            ["AiCapabilities:KillSwitch:legal_research"] = "true"
        };
        var validator = BuildValidator(config);
        var entry = MakeEntry("legal_research");
        var ctx = MakeContext();

        var result = await validator.FilterAsync([entry], ctx);

        result.Should().BeEmpty("kill switch = true must exclude the capability");
    }

    [Fact]
    public async Task FilterAsync_AllowsCapability_WhenKillSwitchIsFalse()
    {
        var config = new Dictionary<string, string?>
        {
            ["AiCapabilities:KillSwitch:legal_research"] = "false"
        };
        var validator = BuildValidator(config);
        var entry = MakeEntry("legal_research");
        var ctx = MakeContext();

        var result = await validator.FilterAsync([entry], ctx);

        result.Should().ContainSingle(e => e.CapabilityName == "legal_research");
    }

    [Fact]
    public async Task FilterAsync_AllowsCapability_WhenKillSwitchNotConfigured()
    {
        var validator = BuildValidator(); // no kill switch keys
        var entry = MakeEntry("web_search");
        var ctx = MakeContext();

        var result = await validator.FilterAsync([entry], ctx);

        result.Should().ContainSingle(e => e.CapabilityName == "web_search");
    }

    // ── Test 3: Global kill switch ────────────────────────────────────────────

    [Fact]
    public async Task FilterAsync_ExcludesAllCapabilities_WhenGlobalKillSwitchActive()
    {
        var config = new Dictionary<string, string?>
        {
            ["AiCapabilities:KillSwitch:All"] = "true"
        };
        var validator = BuildValidator(config);
        var entries = new[]
        {
            MakeEntry("legal_research"),
            MakeEntry("web_search"),
            MakeEntry("write_back")
        };
        var ctx = MakeContext();

        var result = await validator.FilterAsync(entries, ctx);

        result.Should().BeEmpty("global kill switch must exclude every capability");
    }

    // ── Test 4: Tenant restriction — mismatch ─────────────────────────────────

    [Fact]
    public async Task FilterAsync_ExcludesCapability_WhenTenantNotInRestrictionList()
    {
        var validator = BuildValidator();
        var entry = MakeEntry("legal_research",
            tenantRestrictions: ["https://othertenant.crm.dynamics.com"]);
        var ctx = MakeContext(tenantUrl: "https://spaarkedev1.crm.dynamics.com");

        var result = await validator.FilterAsync([entry], ctx);

        result.Should().BeEmpty("caller's tenant URL is not in the restriction list");
    }

    // ── Test 5: Tenant restriction — match ────────────────────────────────────

    [Fact]
    public async Task FilterAsync_AllowsCapability_WhenTenantInRestrictionList()
    {
        var validator = BuildValidator();
        var entry = MakeEntry("legal_research",
            tenantRestrictions: ["https://spaarkedev1.crm.dynamics.com"]);
        var ctx = MakeContext(tenantUrl: "https://spaarkedev1.crm.dynamics.com");

        var result = await validator.FilterAsync([entry], ctx);

        result.Should().ContainSingle(e => e.CapabilityName == "legal_research");
    }

    [Fact]
    public async Task FilterAsync_AllowsCapability_WhenTenantListIsEmpty()
    {
        var validator = BuildValidator();
        var entry = MakeEntry("web_search", tenantRestrictions: []); // unrestricted
        var ctx = MakeContext(tenantUrl: "https://any-tenant.crm.dynamics.com");

        var result = await validator.FilterAsync([entry], ctx);

        result.Should().ContainSingle(e => e.CapabilityName == "web_search");
    }

    [Fact]
    public async Task FilterAsync_AllowsCapability_WhenTenantUrlHasTrailingSlash()
    {
        var validator = BuildValidator();
        var entry = MakeEntry("legal_research",
            tenantRestrictions: ["https://spaarkedev1.crm.dynamics.com"]); // no trailing slash
        var ctx = MakeContext(tenantUrl: "https://spaarkedev1.crm.dynamics.com/"); // trailing slash

        var result = await validator.FilterAsync([entry], ctx);

        result.Should().ContainSingle("trailing slash difference must be normalised away");
    }

    // ── Test 6: Missing required role ─────────────────────────────────────────

    [Fact]
    public async Task FilterAsync_ExcludesCapability_WhenUserLacksRequiredRole()
    {
        var validator = BuildValidator();
        var entry = MakeEntry("legal_research",
            description: "Legal research. [RequiredRole=ai.capabilities.legalResearch]");
        var ctx = MakeContext(roles: []); // no roles

        var result = await validator.FilterAsync([entry], ctx);

        result.Should().BeEmpty("user without the required role must be excluded");
    }

    // ── Test 7: Required role present ─────────────────────────────────────────

    [Fact]
    public async Task FilterAsync_AllowsCapability_WhenUserHasRequiredRole()
    {
        var validator = BuildValidator();
        var entry = MakeEntry("legal_research",
            description: "Legal research. [RequiredRole=ai.capabilities.legalResearch]");
        var ctx = MakeContext(roles: ["ai.capabilities.legalResearch"]);

        var result = await validator.FilterAsync([entry], ctx);

        result.Should().ContainSingle(e => e.CapabilityName == "legal_research");
    }

    [Fact]
    public async Task FilterAsync_AllowsCapability_WhenNoRequiredRoleAnnotation()
    {
        var validator = BuildValidator();
        var entry = MakeEntry("web_search", description: "Searches the web. No role required.");
        var ctx = MakeContext(roles: []); // user has no roles

        var result = await validator.FilterAsync([entry], ctx);

        result.Should().ContainSingle(e => e.CapabilityName == "web_search",
            "no role annotation means unrestricted");
    }

    // ── Test 8: Missing required context key ──────────────────────────────────

    [Fact]
    public async Task FilterAsync_ExcludesCapability_WhenRequiredContextKeyAbsent()
    {
        var validator = BuildValidator();
        var entry = MakeEntry("matter_summary",
            description: "Summarises a matter. [RequiredContext=MatterLoaded]");
        var ctx = MakeContext(conversationContext: []); // empty context

        var result = await validator.FilterAsync([entry], ctx);

        result.Should().BeEmpty("MatterLoaded key is absent from conversation context");
    }

    // ── Test 9: Required context key present but false ────────────────────────

    [Fact]
    public async Task FilterAsync_ExcludesCapability_WhenRequiredContextKeyIsFalse()
    {
        var validator = BuildValidator();
        var entry = MakeEntry("matter_summary",
            description: "Summarises a matter. [RequiredContext=MatterLoaded]");
        var ctx = MakeContext(conversationContext: new Dictionary<string, string>
        {
            ["MatterLoaded"] = "false"
        });

        var result = await validator.FilterAsync([entry], ctx);

        result.Should().BeEmpty("MatterLoaded=false must be treated as context not satisfied");
    }

    // ── Test 10: Required context key present and true ────────────────────────

    [Fact]
    public async Task FilterAsync_AllowsCapability_WhenRequiredContextKeyIsTrue()
    {
        var validator = BuildValidator();
        var entry = MakeEntry("matter_summary",
            description: "Summarises a matter. [RequiredContext=MatterLoaded]");
        var ctx = MakeContext(conversationContext: new Dictionary<string, string>
        {
            ["MatterLoaded"] = "true"
        });

        var result = await validator.FilterAsync([entry], ctx);

        result.Should().ContainSingle(e => e.CapabilityName == "matter_summary");
    }

    [Fact]
    public async Task FilterAsync_AllowsCapability_WhenRequiredContextKeyIsTrueCaseInsensitive()
    {
        var validator = BuildValidator();
        var entry = MakeEntry("matter_summary",
            description: "Summarises a matter. [RequiredContext=MatterLoaded]");
        var ctx = MakeContext(conversationContext: new Dictionary<string, string>
        {
            ["MatterLoaded"] = "True" // different case
        });

        var result = await validator.FilterAsync([entry], ctx);

        result.Should().ContainSingle(e => e.CapabilityName == "matter_summary");
    }

    // ── Test 11: All checks pass ──────────────────────────────────────────────

    [Fact]
    public async Task FilterAsync_AllowsCapability_WhenAllChecksPass()
    {
        var config = new Dictionary<string, string?>
        {
            ["AiCapabilities:KillSwitch:legal_research"] = "false"
        };
        var validator = BuildValidator(config);

        var entry = MakeEntry("legal_research",
            tenantRestrictions: ["https://spaarkedev1.crm.dynamics.com"],
            description: "Legal research. [RequiredRole=ai.legalResearch] [RequiredContext=MatterLoaded]");

        var ctx = MakeContext(
            tenantUrl: "https://spaarkedev1.crm.dynamics.com",
            roles: ["ai.legalResearch"],
            conversationContext: new Dictionary<string, string>
            {
                ["MatterLoaded"] = "true"
            });

        var result = await validator.FilterAsync([entry], ctx);

        result.Should().ContainSingle(e => e.CapabilityName == "legal_research",
            "all four checks pass so the capability is included");
    }

    // ── Test 12: Multiple candidates — mixed outcomes ─────────────────────────

    [Fact]
    public async Task FilterAsync_ReturnsOnlyValidCandidates_WhenMixedInputProvided()
    {
        var config = new Dictionary<string, string?>
        {
            ["AiCapabilities:KillSwitch:disabled_cap"] = "true"
        };
        var validator = BuildValidator(config);

        var killSwitched = MakeEntry("disabled_cap");
        var tenantMismatch = MakeEntry("restricted_cap",
            tenantRestrictions: ["https://other-tenant.crm.dynamics.com"]);
        var missingRole = MakeEntry("role_cap",
            description: "Needs role. [RequiredRole=ai.specialRole]");
        var missingContext = MakeEntry("context_cap",
            description: "Needs context. [RequiredContext=DocumentPresent]");
        var valid = MakeEntry("web_search");

        var candidates = new[]
        {
            killSwitched, tenantMismatch, missingRole, missingContext, valid
        };

        var ctx = MakeContext(
            tenantUrl: "https://spaarkedev1.crm.dynamics.com",
            roles: [],
            conversationContext: []);

        var result = await validator.FilterAsync(candidates, ctx);

        result.Should().ContainSingle(e => e.CapabilityName == "web_search",
            "only the unrestricted capability should survive all four checks");
    }

    // ── Test 13: Cancellation ─────────────────────────────────────────────────

    [Fact]
    public async Task FilterAsync_ThrowsOperationCanceled_WhenTokenCancelled()
    {
        var validator = BuildValidator();
        var entries = Enumerable.Range(0, 10)
            .Select(i => MakeEntry($"cap_{i}"))
            .ToList();
        var ctx = MakeContext();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => validator.FilterAsync(entries, ctx, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Test 14: No annotations — unrestricted ────────────────────────────────

    [Fact]
    public async Task FilterAsync_AllowsCapability_WhenDescriptionHasNoAnnotations()
    {
        var validator = BuildValidator();
        var entry = MakeEntry("write_back", description: "Writes data back to Dataverse.");
        var ctx = MakeContext(roles: [], conversationContext: []);

        var result = await validator.FilterAsync([entry], ctx);

        result.Should().ContainSingle(e => e.CapabilityName == "write_back",
            "no annotations means no restrictions — capability is always allowed");
    }
}
