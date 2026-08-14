using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace Sprk.Bff.Api.Tests;

/// <summary>
/// Architectural guard (dotnet-10-upgrade task 020, H2 / spec FR-08): boots the FULL BFF DI graph
/// in the Development environment with <c>ValidateOnBuild=true</c> + <c>ValidateScopes=true</c> and
/// fails if the container has any captive dependency (singleton→scoped) or unresolvable ctor dep.
///
/// <para>Since .NET 9, those two validations default to ON in Development — so on net10 a Development
/// boot crashes on such defects (Production is unaffected). CI never boots the app in Development, so
/// without this test those defects regress silently — exactly how 45 captive-dependency defects had
/// accumulated (see <c>CustomWebAppFactory.cs</c> comment "known singleton→scoped DI lifetime issues",
/// remediated in task 020). This test is the CI forcing-function that keeps them fixed.</para>
///
/// <para>Runs network-free: it reuses <see cref="CustomWebAppFactory"/>'s fake config + mocked
/// <c>IDataverseService</c> + fake <c>IGraphClientFactory</c> + removed hosted services, so
/// eager-connecting infra ctors are neutralized and only the DI-graph structure is validated.
/// If a future change adds a new infra service that connects in its constructor, mock it in
/// <see cref="CustomWebAppFactory"/> the same way (do NOT relax the validations here).</para>
///
/// <para>Disposition flagged for task 021 (adversarial verify) / 030 (test suite) review: keep as the
/// permanent H2 regression guard vs. rely on the Development-boot default alone.</para>
/// </summary>
public sealed class DiGraphValidationTests
{
    private sealed class ValidatingWebAppFactory : CustomWebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            // Re-enable both validations (base disables them for the other fixtures' sake). Last call wins.
            builder.UseDefaultServiceProvider(options =>
            {
                options.ValidateScopes = true;
                options.ValidateOnBuild = true;
            });
        }
    }

    [Fact]
    public void BffDiGraph_InDevelopment_HasNoCaptiveDependenciesOrUnresolvableServices()
    {
        using var factory = new ValidatingWebAppFactory();

        // Accessing Services forces the host build → triggers ValidateOnBuild + ValidateScopes.
        var ex = Record.Exception(() => _ = factory.Services);

        if (ex is null)
        {
            return; // Graph is valid — no captive dependency, everything resolvable.
        }

        var errors = (ex as AggregateException)?.Flatten().InnerExceptions
                     ?? (IReadOnlyList<Exception>)new[] { ex };
        var detail = string.Join(
            Environment.NewLine,
            errors.Select((e, i) => $"[{i + 1}] {e.GetType().Name}: {e.Message}"));

        Assert.Fail(
            $"BFF DI graph failed ValidateOnBuild/ValidateScopes with {errors.Count} error(s) — " +
            $"a captive-dependency (singleton→scoped) or unresolvable-service regression (H2, task 020). " +
            $"Fix the offending registration (scope-per-unit-of-work via IServiceScopeFactory, or demote " +
            $"to Scoped where consumed only by scoped services); do NOT disable the validations:" +
            $"{Environment.NewLine}{detail}");
    }
}
