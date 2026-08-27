// -----------------------------------------------------------------------------
// CustomerRunGuardModulePostConfigureTests.cs
//
// Wave 2 pre-dispatch remediation punchlist REG-02 + REG-05 (2026-08-27).
//
// Tests the CustomerRunGuardModule.AddCustomerRunGuard() composition-time
// contract:
//   - REG-05 URL collapse: TargetDataverseUrl falls back to
//     DataverseEnvironmentRegistry:AdminEnvironmentUrl when unset explicitly.
//   - REG-05 cross-check: when BOTH URLs are set, they MUST resolve to the
//     same host (case-insensitive); mismatch throws InvalidOperationException
//     naming both settings.
//   - REG-02 ManagedIdentityClientId fallback: unset in CustomerRunGuard
//     section falls back to ManagedIdentity:ClientId.
//   - Enabled=true is safe with no ClientSecret (REG-02 Path X invariant).
//
// PATTERN PARITY: these are boot-time IConfiguration-driven tests. They use
// the real Options.Validate() codepath via services.PostConfigure — the
// pattern lines up with existing DataverseEnvironmentRegistryClientTests
// options-shape tests.
//
// ADR-038 KEEP category: unit-test — pure DI composition, no HTTP, no mocks.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Concurrency;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Concurrency;

public class CustomerRunGuardModulePostConfigureTests
{
    private const string SpaarkeAdminUrl = "https://spaarkedev1.crm.dynamics.com";
    private const string RegistrySection = "DataverseEnvironmentRegistry:AdminEnvironmentUrl";
    private const string ManagedIdentityClientIdKey = "ManagedIdentity:ClientId";
    private const string UamiId = "11111111-1111-1111-1111-111111111111";

    /// <summary>
    /// REG-05 URL collapse: only the registry-side setting is populated;
    /// PostConfigure falls back TargetDataverseUrl onto that value so
    /// Validate() passes without an explicit CustomerRunGuard:TargetDataverseUrl.
    /// </summary>
    [Fact]
    public void PostConfigure_FallsBack_TargetDataverseUrl_To_RegistryAdminUrl()
    {
        var config = Build(
            (RegistrySection, SpaarkeAdminUrl),
            (ManagedIdentityClientIdKey, UamiId),
            ("CustomerRunGuard:Enabled", "true"));

        var options = ResolveOptions(config);

        options.TargetDataverseUrl.Should().Be(SpaarkeAdminUrl,
            because: "REG-05 URL collapse — one setting drives both admin-env clients.");
        options.Enabled.Should().BeTrue();
        options.ManagedIdentityClientId.Should().Be(UamiId,
            because: "REG-02 fallback to ManagedIdentity:ClientId.");
    }

    /// <summary>
    /// REG-02 default: Enabled=true even when no CustomerRunGuard:Enabled key
    /// is bound (options POCO default). This is the load-bearing behavior
    /// change of the Path X migration.
    /// </summary>
    [Fact]
    public void PostConfigure_DefaultEnabled_IsTrue_WithNoCustomerRunGuardSection()
    {
        var config = Build(
            (RegistrySection, SpaarkeAdminUrl),
            (ManagedIdentityClientIdKey, UamiId));

        var options = ResolveOptions(config);

        options.Enabled.Should().BeTrue(
            because: "REG-02 flipped the default to true — Path X removes the last credential-missing failure mode.");
    }

    /// <summary>
    /// REG-05 cross-check: BOTH URLs set to DIFFERENT hosts → PostConfigure
    /// throws. Prevents the silent lock-forever bug where the guard writes
    /// sprk_currentrunid to env A while the registry status-updater clears
    /// it from env B.
    /// </summary>
    [Fact]
    public void PostConfigure_Throws_When_TwoAdminUrls_Point_To_Different_Hosts()
    {
        var config = Build(
            (RegistrySection, "https://envA.crm.dynamics.com"),
            ("CustomerRunGuard:TargetDataverseUrl", "https://envB.crm.dynamics.com"),
            (ManagedIdentityClientIdKey, UamiId),
            ("CustomerRunGuard:Enabled", "true"));

        var act = () => ResolveOptions(config);

        // Uri.Host returns the lowercased host, so the diagnostic will contain
        // lowercase host names regardless of the input casing. Match on both
        // (order-independent) — asserting only that BOTH hosts appear.
        act.Should().Throw<InvalidOperationException>()
            .Where(ex => ex.Message.Contains("enva.crm.dynamics.com", StringComparison.OrdinalIgnoreCase)
                      && ex.Message.Contains("envb.crm.dynamics.com", StringComparison.OrdinalIgnoreCase),
                because: "REG-05 cross-check must name BOTH hosts so the operator can reconcile.");
    }

    /// <summary>
    /// REG-05 cross-check: BOTH URLs set to the SAME host (case-insensitive)
    /// → no throw. Verifies the cross-check is tolerant of casing.
    /// </summary>
    [Fact]
    public void PostConfigure_Passes_When_TwoAdminUrls_Match_CaseInsensitive()
    {
        var config = Build(
            (RegistrySection, "https://SpaarkeDev1.crm.dynamics.com"),
            ("CustomerRunGuard:TargetDataverseUrl", "https://spaarkedev1.crm.dynamics.com"),
            (ManagedIdentityClientIdKey, UamiId),
            ("CustomerRunGuard:Enabled", "true"));

        var options = ResolveOptions(config);

        options.Enabled.Should().BeTrue();
        options.TargetDataverseUrl.Should().Be("https://spaarkedev1.crm.dynamics.com",
            because: "The explicit CustomerRunGuard setting wins when both are set.");
    }

    /// <summary>
    /// REG-02 Path X: Enabled=true is safe with NO ClientSecret / TenantId /
    /// ClientId configuration keys — those fields were removed. Confirms
    /// Validate() no longer demands them.
    /// </summary>
    [Fact]
    public void PostConfigure_EnabledTrue_Succeeds_Without_ClientSecret_Or_TenantId_Or_ClientId()
    {
        var config = Build(
            ("CustomerRunGuard:TargetDataverseUrl", SpaarkeAdminUrl),
            (ManagedIdentityClientIdKey, UamiId),
            ("CustomerRunGuard:Enabled", "true"));

        var options = ResolveOptions(config);

        options.Enabled.Should().BeTrue();
        options.TargetDataverseUrl.Should().Be(SpaarkeAdminUrl);
        options.ManagedIdentityClientId.Should().Be(UamiId);
    }

    /// <summary>
    /// REG-02: Enabled=true + neither URL set → Validate() throws citing BOTH
    /// candidate keys (CustomerRunGuard:TargetDataverseUrl and the registry
    /// AdminEnvironmentUrl fallback) so the operator sees an unambiguous fix.
    /// </summary>
    [Fact]
    public void PostConfigure_EnabledTrue_Throws_When_Both_Urls_Are_Absent()
    {
        var config = Build(
            (ManagedIdentityClientIdKey, UamiId),
            ("CustomerRunGuard:Enabled", "true"));

        var act = () => ResolveOptions(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TargetDataverseUrl*AdminEnvironmentUrl*",
                because: "REG-02 error message must cite BOTH candidate keys.");
    }

    /// <summary>
    /// REG-02: Enabled=false skips ALL validation — the ADR-032 kill-switch
    /// path is preserved for explicit test-host opt-out.
    /// </summary>
    [Fact]
    public void PostConfigure_EnabledFalse_Skips_All_Validation()
    {
        var config = Build(
            ("CustomerRunGuard:Enabled", "false"));
        // No URL, no UAMI — Validate() must return early.

        var options = ResolveOptions(config);

        options.Enabled.Should().BeFalse();
        options.TargetDataverseUrl.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds an IConfiguration from an in-memory key/value dictionary.
    /// Uses a params approach so tests read as tables.
    /// </summary>
    private static IConfiguration Build(params (string Key, string? Value)[] entries)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in entries)
        {
            dict[k] = v;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    /// <summary>
    /// Composes the module + resolves the fully-configured options — the
    /// same PostConfigure pipeline the Worker Program.cs boots against.
    /// </summary>
    private static CustomerRunGuardOptions ResolveOptions(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCustomerRunGuard(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<CustomerRunGuardOptions>>().Value;
    }
}
