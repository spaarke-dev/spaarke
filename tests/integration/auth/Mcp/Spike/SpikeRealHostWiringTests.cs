// SPIKE BRANCH ONLY (specialist-legal-agents-r1 task 011; never merged). The REAL BFF host (Program, compound AI on) with MCP enabled:
// the spike tools are in the legal-operations catalogue and their facade resolves from a request scope to the real implementation, so
// the SDK binds it as a service, never as a tool argument (a static input schema would hide that).

using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.Mcp.Spike;

public sealed class SpikeRealHostWiringTests
{
    [Fact]
    public void RealBffHost_CataloguesBothSpikeTools_AndResolvesTheRealFacadeFromARequestScope()
    {
        using var factory = new CustomWebAppFactory().WithWebHostBuilder(builder => builder.UseSetting("Mcp:Enabled", "true"));

        var names = factory.Services.GetRequiredService<McpToolCatalog>().ToolsFor(McpEndpoints.LegalOperationsArea).Select(t => t.ProtocolTool.Name);
        names.Should().Contain(["spike_get_profile", "spike_review_nda"]);

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISpecialistProfileSpikeAi>().Should().BeOfType<SpecialistProfileSpikeAi>();
        factory.Services.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceProviderIsService>()
            .IsService(typeof(ISpecialistProfileSpikeAi)).Should().BeTrue();
    }
}
