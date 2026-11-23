using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Services.Ai.Jobs;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.DI;

/// <summary>
/// ebilling-r1 task 095d-2b-1: the re-run's registrations in the REAL <c>AddFinanceModule</c>: one each for the concrete processor, the re-run
/// and its job handler, all scoped and unconditional (registered with eBilling off, so a queued message never dead-letters NoHandler).
/// </summary>
public sealed class FinanceModuleRerunRegistrationTests
{
    [Fact(DisplayName = "095d-2b-1: AddFinanceModule registers the concrete processor, the re-run and its job handler exactly once each, scoped, with eBilling off")]
    public void TheRerun_IsRegisteredOnce_Unconditionally()
    {
        var services = new ServiceCollection();
        services.AddFinanceModule(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ebilling:Enabled"] = "false" }).Build());

        services.Where(d => d.ServiceType == typeof(InvoiceSubmissionProcessor)).Should().ContainSingle().Which.Lifetime.Should().Be(ServiceLifetime.Scoped);
        services.Where(d => d.ServiceType == typeof(InvoiceSubmissionRerun)).Should().ContainSingle().Which.Lifetime.Should().Be(ServiceLifetime.Scoped);
        services.Where(d => d.ServiceType == typeof(IJobHandler) && d.ImplementationType == typeof(InvoiceSubmissionRerunJobHandler))
            .Should().ContainSingle().Which.Lifetime.Should().Be(ServiceLifetime.Scoped);
        services.Where(d => d.ServiceType == typeof(IInvoiceSubmissionProcessor)).Should().ContainSingle("the 049 seam is unchanged");
    }
}
