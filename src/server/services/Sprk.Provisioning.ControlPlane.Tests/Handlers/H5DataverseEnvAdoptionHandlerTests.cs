// -----------------------------------------------------------------------------
// H5DataverseEnvAdoptionHandlerTests.cs
//
// T228: H5 adopts the Dataverse environment the operator created (intake `dataverseEnvUrl`) and never creates one.
//
//   A1  happy path: canonical URL written to InterStepState.DataverseEnvUrl, verified gate, completed phase.
//   A2  idempotent: a completed H5 is a no-op (no probe call).
//   A3  missing tenantId → Resumable, no probe call (§4D I1).
//   A4  a URL that is missing / malformed / another customer's → Resumable env-url-invalid, no probe call, nothing adopted.
//   A5  WhoAmI 401/403 → Resumable worker-not-app-user naming the prerequisite.
//   A6  WhoAmI terminal failure / probe throws → Resumable env-health-check-failed.
//   A7  InProgress → Reachable within the window → adopted; InProgress to the end → env-health-check-failed.
//   A8  the stamp environment segment selects the allowed domain (spaarke-{id}-{env}).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.DataverseEnvCreation;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H5DataverseEnvAdoptionHandlerTests
{
    private const string CustomerId = "acme";
    private const string RunId = "run-h5-adopt";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string EnvUrl = "https://spaarke-acme.crm.dynamics.com/";

    [Fact]
    public async Task A1_TheCustomersEnvironment_IsAdopted_InCanonicalForm()
    {
        var run = BuildRun(url: "https://SPAARKE-acme.crm.dynamics.com");
        var repo = new FakeRepository(run, "etag-1");
        var probe = new SequencedProbe(new DataverseHealthProbeResult.Reachable());

        var result = await BuildHandler(repo, probe).HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>()
            .Which.IdempotencyKey.Should().Be(H5DataverseEnvAdoptionHandler.BuildIdempotencyKey(CustomerId));
        repo.LastWrittenRun!.InterStepState.DataverseEnvUrl.Should().Be(EnvUrl);
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle(cp => cp.Phase == "H5");
        var gate = repo.LastWrittenRun.GateStates[H5DataverseEnvAdoptionHandler.EnvProvisionedGateId];
        gate.Status.Should().Be(GateState.Verified);
        gate.Evidence!.Value.GetProperty("adopted").GetBoolean().Should().BeTrue();
        probe.Urls.Should().Equal(EnvUrl);
    }

    [Fact]
    public async Task A2_ACompletedH5_IsANoOp()
    {
        var run = BuildRun();
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H5",
            IdempotencyKey = H5DataverseEnvAdoptionHandler.BuildIdempotencyKey(CustomerId),
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            JobId = RunId,
        });
        var repo = new FakeRepository(run, "etag-1");
        var probe = new SequencedProbe(new DataverseHealthProbeResult.Reachable());

        (await BuildHandler(repo, probe).HandleAsync(BuildEnvelope(), CancellationToken.None))
            .Should().BeOfType<HandlerResult.Success>();
        probe.Urls.Should().BeEmpty();
        repo.LastWrittenRun.Should().BeNull();
    }

    [Fact]
    public async Task A3_MissingTenant_IsRefused_BeforeAnyCall()
    {
        var repo = new FakeRepository(BuildRun(includeTenant: false), "etag-1");
        var probe = new SequencedProbe(new DataverseHealthProbeResult.Reachable());

        var failure = (await BuildHandler(repo, probe).HandleAsync(BuildEnvelope(), CancellationToken.None))
            .Should().BeOfType<HandlerResult.Failure>().Subject;

        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(DataverseEnvAdoptionRejectionCodes.MissingTenantId);
        probe.Urls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://spaarke-other.crm.dynamics.com/")]     // another customer's environment
    [InlineData("https://spaarke-acmex.crm.dynamics.com/")]     // a longer id is another customer
    [InlineData("https://acme.crm.dynamics.com/")]              // not the naming rule
    [InlineData("https://spaarke-acme.crm.dynamics.com/main.aspx")]
    [InlineData("http://spaarke-acme.crm.dynamics.com/")]
    public async Task A4_AnEnvironmentThatIsNotThisCustomers_IsRefused_NothingAdopted(string? url)
    {
        var repo = new FakeRepository(BuildRun(url: url), "etag-1");
        var probe = new SequencedProbe(new DataverseHealthProbeResult.Reachable());

        var failure = (await BuildHandler(repo, probe).HandleAsync(BuildEnvelope(), CancellationToken.None))
            .Should().BeOfType<HandlerResult.Failure>().Subject;

        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(DataverseEnvAdoptionRejectionCodes.EnvUrlInvalid);
        probe.Urls.Should().BeEmpty("H5 never touches an environment that is not this customer's");
        repo.LastWrittenRun!.InterStepState.DataverseEnvUrl.Should().BeNull();
    }

    [Fact]
    public async Task A5_AWorkerThatIsNotAnApplicationUser_NamesThePrerequisite()
    {
        var repo = new FakeRepository(BuildRun(), "etag-1");
        var probe = new SequencedProbe(new DataverseHealthProbeResult.AccessDenied("WhoAmI returned 403 Forbidden"));

        var failure = (await BuildHandler(repo, probe).HandleAsync(BuildEnvelope(), CancellationToken.None))
            .Should().BeOfType<HandlerResult.Failure>().Subject;

        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(DataverseEnvAdoptionRejectionCodes.WorkerNotAppUser);
        failure.Diagnostic.Should().Contain("PRQ-C-09").And.Contain("System Administrator");
        repo.LastWrittenRun!.InterStepState.DataverseEnvUrl.Should().BeNull();
    }

    [Fact]
    public async Task A6_ATerminalProbeFailureOrFault_IsAHealthCheckFailure()
    {
        var unreachable = await BuildHandler(
                new FakeRepository(BuildRun(), "etag-1"),
                new SequencedProbe(new DataverseHealthProbeResult.Unreachable("WhoAmI returned 500")))
            .HandleAsync(BuildEnvelope(), CancellationToken.None);
        unreachable.Should().BeOfType<HandlerResult.Failure>()
            .Which.RejectionCode.Should().Be(DataverseEnvAdoptionRejectionCodes.EnvHealthCheckFailed);

        var thrown = await BuildHandler(new FakeRepository(BuildRun(), "etag-1"), new ThrowingProbe())
            .HandleAsync(BuildEnvelope(), CancellationToken.None);
        thrown.Should().BeOfType<HandlerResult.Failure>()
            .Which.RejectionCode.Should().Be(DataverseEnvAdoptionRejectionCodes.EnvHealthCheckFailed);
    }

    [Fact]
    public async Task A7_AnEnvironmentStillBeingPrepared_IsPolled()
    {
        var ready = new SequencedProbe(
            new DataverseHealthProbeResult.InProgress("503"), new DataverseHealthProbeResult.Reachable());
        (await BuildHandler(new FakeRepository(BuildRun(), "etag-1"), ready)
                .HandleAsync(BuildEnvelope(), CancellationToken.None))
            .Should().BeOfType<HandlerResult.Success>();
        ready.Urls.Should().HaveCount(2);

        var never = new SequencedProbe(new DataverseHealthProbeResult.InProgress("503"));
        (await BuildHandler(new FakeRepository(BuildRun(), "etag-1"), never, totalTimeout: TimeSpan.FromMilliseconds(30))
                .HandleAsync(BuildEnvelope(), CancellationToken.None))
            .Should().BeOfType<HandlerResult.Failure>()
            .Which.RejectionCode.Should().Be(DataverseEnvAdoptionRejectionCodes.EnvHealthCheckFailed);
    }

    [Fact]
    public async Task A8_TheStampEnvironmentSegment_SelectsTheAllowedDomain()
    {
        var run = BuildRun(url: "https://spaarke-acme-staging.crm.dynamics.com/");
        run.Parameters.NonSecret[IntakeParameterCatalog.EnvironmentName] = "staging";

        (await BuildHandler(new FakeRepository(run, "etag-1"), new SequencedProbe(new DataverseHealthProbeResult.Reachable()))
                .HandleAsync(BuildEnvelope(), CancellationToken.None))
            .Should().BeOfType<HandlerResult.Success>();
    }

    // ---------- helpers ----------

    private static H5DataverseEnvAdoptionHandler BuildHandler(
        FakeRepository repo, IDataverseHealthProbe probe, TimeSpan? totalTimeout = null)
        => new(
            repo,
            probe,
            Options.Create(new DataverseEnvAdoptionOptions
            {
                HealthProbeInterval = TimeSpan.FromMilliseconds(1),
                HealthProbeTotalTimeout = totalTimeout ?? TimeSpan.FromSeconds(5),
            }),
            TimeProvider.System,
            NullLogger<H5DataverseEnvAdoptionHandler>.Instance);

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = H5DataverseEnvAdoptionHandler.HandlerIdentifier,
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = "{}",
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    private static ProvisioningRun BuildRun(bool includeTenant = true, string? url = EnvUrl)
    {
        var run = new ProvisioningRun
        {
            RunId = RunId,
            CustomerId = CustomerId,
            EnvironmentId = "env-guid",
            TenancyModel = "Model1",
            Status = RunStatus.Running,
            Profile = "spaarke-hosted-model2",
        };
        if (includeTenant)
        {
            run.Parameters.NonSecret[IntakeParameterCatalog.TenantId] = TenantId;
        }
        if (url is not null)
        {
            run.Parameters.NonSecret[IntakeParameterCatalog.DataverseEnvUrl] = url;
        }
        return run;
    }

    private sealed class FakeRepository(ProvisioningRun? run, string? etag) : IProvisioningRunRepository
    {
        private ProvisioningRun? _run = run;
        private string? _etag = etag;
        public ProvisioningRun? LastWrittenRun { get; private set; }

        public Task<ProvisioningRunReadResult?> ReadRunAsync(string customerId, string runId, CancellationToken ct)
            => Task.FromResult(_run is null || _etag is null ? null : new ProvisioningRunReadResult(_run, _etag));

        public Task<ProvisioningRunReadResult> CreateRunAsync(ProvisioningRun run, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<ReplaceRunResult> ReplaceRunAsync(ProvisioningRun run, string ifMatchEtag, CancellationToken ct)
        {
            LastWrittenRun = run;
            _run = run;
            _etag = ifMatchEtag + "-next";
            return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.Success(run, _etag));
        }
    }

    /// <summary>Answers each call with the next result (the last repeats); records the URLs probed.</summary>
    private sealed class SequencedProbe(params DataverseHealthProbeResult[] sequence) : IDataverseHealthProbe
    {
        public List<string> Urls { get; } = [];

        public Task<DataverseHealthProbeResult> CheckHealthAsync(string environmentUrl, string tenantId, CancellationToken ct)
        {
            Urls.Add(environmentUrl);
            return Task.FromResult(sequence[Math.Min(Urls.Count - 1, sequence.Length - 1)]);
        }
    }

    private sealed class ThrowingProbe : IDataverseHealthProbe
    {
        public Task<DataverseHealthProbeResult> CheckHealthAsync(string environmentUrl, string tenantId, CancellationToken ct)
            => throw new HttpRequestException("connection reset");
    }
}
