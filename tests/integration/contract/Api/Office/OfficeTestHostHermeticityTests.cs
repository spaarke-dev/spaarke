using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// The Office test hosts never do a real on-behalf-of token exchange.
/// </summary>
/// <remarks>
/// <para><b>Why this is pinned.</b> The real <see cref="IGraphClientFactory"/> answers <c>ForUserAsync</c> with an MSAL
/// on-behalf-of exchange of the caller's bearer token. A test host's bearer is the literal <c>test-caller-token</c>, so the
/// exchange can only fail, and it fails SLOWLY: MSAL probes IMDS (169.254.169.254) for the managed-identity assertion, then
/// calls login.microsoftonline.com. Both escape <see cref="TestInfrastructure.TestOutboundNetworkGuard"/> (MSAL owns its
/// transport; the guard only sees <c>IHttpClientFactory</c> clients), so the guard cannot turn them into a fast, named failure.</para>
/// <para>Every EMAIL save reaches it: <c>OfficeEmailEnricher</c> reads the message's sent time from the caller's mailbox whenever
/// the request names an Exchange item id (task 132). Measured on a developer machine: ~22 s per save request. The save tests
/// of one class run one after another, so a class of 33 email saves took ~12 minutes by itself and the office-scope CI leg
/// (<c>rest</c>, 20-minute cap) was cancelled with every test still passing but one class unfinished. The suites under test
/// do not change: a test that is ABOUT the Graph read builds its own <c>OfficeEmailEnricher</c> over its own Graph double.</para>
/// <para>Failing here means a host in the Office fixture family would again exchange a token for real; fix the fixture, not this test.</para>
/// </remarks>
public class OfficeTestHostHermeticityTests : IClassFixture<OfficeTestWebAppFactory>
{
    private readonly OfficeTestWebAppFactory _factory;

    public OfficeTestHostHermeticityTests(OfficeTestWebAppFactory factory) => _factory = factory;

    [Fact]
    public void OfficeTestWebAppFactory_BindsTheFakeGraphClientFactory_NotTheRealOnBehalfOfExchange()
    {
        using var scope = _factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IGraphClientFactory>().Should().BeOfType<FakeGraphClientFactory>(
            "the real factory exchanges the test bearer through MSAL, which waits on IMDS and Azure AD for ~20 s per request");
    }

    [Fact]
    public void OfficeVersionSaveTestWebAppFactory_InheritsTheFakeGraphClientFactory()
    {
        // The factory every email-save suite builds its host on (OfficeVersionSaveWorld + WithWebHostBuilder).
        using var factory = new OfficeVersionSaveTestWebAppFactory(new OfficeVersionSaveWorld());
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IGraphClientFactory>().Should().BeOfType<FakeGraphClientFactory>(
            "an email save reads the sent time through this factory (OfficeEmailEnricher), once per save request");
    }

    [Fact]
    public void OfficeTestWebAppFactory_SubmitsMainQueueJobsWithoutOpeningAServiceBusConnection()
    {
        // An email save's association gate enqueues a GateDecisionRecord job through this submitter and waits up to
        // GateDecisionRecordEnqueuer.EnqueueTimeout (5 s) for it. A real ServiceBusClient to the fake namespace never connects,
        // so the wait is the whole timeout, on every email save. The double answers at once: the returned task is already complete.
        var job = new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = GateDecisionRecordEnqueuer.JobTypeName,
            SubjectId = Guid.NewGuid().ToString("D"),
        };

        var submitted = _factory.Services.GetRequiredService<JobSubmissionService>().SubmitJobAsync(job);

        submitted.IsCompleted.Should().BeTrue(
            "a submitter that is not complete yet is waiting on a Service Bus connection the test host can never open");
    }
}
