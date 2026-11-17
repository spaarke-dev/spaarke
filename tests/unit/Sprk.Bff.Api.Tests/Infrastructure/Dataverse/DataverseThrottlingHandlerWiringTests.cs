// GitHub #2845 round 2 (F1 follow-up) — which typed Dataverse clients send through the process-wide read gate.
//
// The gate only bounds the reads whose HttpClient pipeline carries DataverseThrottlingHandler, so the registration is the
// contract: the participation reader, the deny-list reader and the external data service (r4 014b's event reads and writes)
// must carry it. Read from the real AddExternalAccess() registration (HttpClientFactoryOptions), with a must-not-fire control
// on a client registered without it.

using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.Dataverse;

public sealed class DataverseThrottlingHandlerWiringTests
{
    private const string UngatedControlClient = "ungated-control";

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddExternalAccess();
        services.AddHttpClient(UngatedControlClient); // must-not-fire control: no handler added
        return services.BuildServiceProvider();
    }

    private static IReadOnlyList<DelegatingHandler> HandlersOf(ServiceProvider provider, string clientName)
    {
        var options = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(clientName);
        var builder = new RecordingBuilder(provider) { Name = clientName };
        foreach (var action in options.HttpMessageHandlerBuilderActions)
        {
            action(builder);
        }

        return builder.AdditionalHandlers.ToList();
    }

    [Theory]
    [InlineData(nameof(ExternalParticipationService))]
    [InlineData(nameof(NoAccessListReader))]
    [InlineData(nameof(ExternalDataService))]
    public void TheEvaluatorAndExternalDataClients_SendThroughTheGate(string typedClientName)
    {
        using var provider = Provider();

        HandlersOf(provider, typedClientName).Should().ContainSingle(h => h is DataverseThrottlingHandler,
            $"{typedClientName}'s Dataverse sends must hold a permit of the process-wide gate (#2845 F1)");
    }

    [Fact]
    public void AClientRegisteredWithoutTheHandler_HasNone()
    {
        using var provider = Provider();

        HandlersOf(provider, UngatedControlClient).Should().NotContain(h => h is DataverseThrottlingHandler);
    }

    [Fact]
    public void TheGate_IsOneSingletonForTheProcess()
    {
        using var provider = Provider();

        provider.GetRequiredService<DataverseConcurrencyGate>()
            .Should().BeSameAs(provider.GetRequiredService<DataverseConcurrencyGate>());
    }

    private sealed class RecordingBuilder(IServiceProvider services) : HttpMessageHandlerBuilder
    {
        public override string? Name { get; set; }

        public override HttpMessageHandler PrimaryHandler { get; set; } = new HttpClientHandler();

        public override IList<DelegatingHandler> AdditionalHandlers { get; } = new List<DelegatingHandler>();

        public override IServiceProvider Services { get; } = services;

        public override HttpMessageHandler Build() => CreateHandlerPipeline(PrimaryHandler, AdditionalHandlers);
    }
}
