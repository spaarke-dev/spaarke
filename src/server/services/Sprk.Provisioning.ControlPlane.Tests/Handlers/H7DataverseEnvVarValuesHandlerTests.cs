// -----------------------------------------------------------------------------
// H7DataverseEnvVarValuesHandlerTests.cs
//
// Unit tests over H7DataverseEnvVarValuesHandler (task 050 — wave C4 Batch 3E).
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live HTTP / Dataverse / Azure.Identity
//   round-trips. A fake replaces the IEnvVarValuesWriter seam so the handler
//   orchestration logic (guard clauses, value resolution + defaults,
//   idempotency, gate/state writes) is exercised in isolation.
//
// COVERAGE:
//   T1  Happy path — all 7 required present, optional resolved with
//       defaults → Success + Cosmos state advances + gate Verified.
//   T2  Idempotent no-op — CompletedPhases already contains H7 with matching
//       key → Success (no writer call, no state mutation).
//   T3  Missing tenantId → Failure(Resumable, MissingUpstreamState) naming tenantId.
//   T4  Missing bffAppRegId → Failure(Resumable, MissingUpstreamState) naming bffAppRegId.
//   T5  Missing dataverseEnvUrl → Failure(Resumable, MissingUpstreamState) naming dataverseEnvUrl.
//   T6  Missing openAiEndpoint → Failure(Resumable, MissingUpstreamState) naming openAiEndpoint.
//   T7  Missing speContainerId → Failure(Resumable, MissingUpstreamState) naming speContainerId.
//   T8  Missing ClientSecret → Failure(Resumable, MissingClientSecret).
//   T9  All 7 canonical schema names present with exact spelling (enumeration).
//   T10 bffApiBaseUrl defaults to https://api.spaarke.com when parameter absent.
//   T11 msalClientId defaults to bffAppRegId when parameter absent.
//   T12 shareLinkBaseUrl resolves to empty string when parameter absent (no failure).
//   T13 Writer Failure DefinitionNotFound → Resumable EnvVarDefinitionNotFound.
//   T14 Writer Failure AuthFailure → Resumable DataverseAuthFailure.
//   T15 Writer Failure RateLimited → Resumable RateLimited.
//   T16 Writer Failure UnknownInvocationFailure → Resumable WriterInvocationFailed.
//   T17 Writer infrastructure exception → Resumable WriterInvocationFailed.
//   T18 HandlerId mismatch → throws InvalidOperationException.
//   T19 Idempotency key format — deterministic by (customerId, configVer);
//       different resolved value set → different key.
//   T20 Run not found → Failure(Resumable, RunNotFound).
//   T21 MapWriterFailure round-trip — every failure kind maps to (rejection, Resumable).
//   T22 Concurrent write conflict on success path → Failure(Resumable, ConcurrentWriteConflict).
//   T23 Client-startup-contract proxy: all 7 written values are non-null (no
//       nulls reach the writer — proxy for task 024's "no missing env var").
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.Credentials;
using Sprk.Provisioning.ControlPlane.Handlers.EnvVarValues;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H7DataverseEnvVarValuesHandlerTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h7-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string BffAppRegId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string EnvUrl = "https://acme.crm.dynamics.com/";
    private const string OpenAiEndpoint = "https://acme-openai.openai.azure.com/";
    private const string SpeContainerId = "b!acmeContainerIdBase64";
    private const string ClientSecret = "test-client-secret-placeholder";

    // ---------- T1 happy path ----------

    [Fact]
    public async Task HappyPath_AllRequiredPresent_AdvancesStateAndWritesGate()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-1");
        var writer = FakeEnvVarValuesWriter.Success();
        var handler = BuildHandler(repo, writer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().StartWith($"envvars-{CustomerId}-");

        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Running);
        repo.LastWrittenRun.CurrentPhase.Should().Be("H7");
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle().Which.Phase.Should().Be("H7");

        var gate = repo.LastWrittenRun.GateStates[H7DataverseEnvVarValuesHandler.EnvVarsSetGateId];
        gate.Status.Should().Be(GateState.Verified);
        gate.VerifierHandler.Should().Be("H7");
        gate.Evidence.Should().NotBeNull();

        writer.CallCount.Should().Be(1);
        writer.LastRequest!.TenantId.Should().Be(TenantId);
        writer.LastRequest.ClientId.Should().Be(BffAppRegId);
        writer.LastRequest.ClientSecret.Should().Be(ClientSecret);
        writer.LastRequest.TargetDataverseUrl.Should().Be(EnvUrl);
    }

    // ---------- T2 idempotency ----------

    [Fact]
    public async Task Idempotent_SecondInvocationWithMatchingCompletedPhase_IsNoOp()
    {
        var run = BuildRun();
        var values = ExpectedValues(run);
        var configVer = H7DataverseEnvVarValuesHandler.ComputeConfigVer(values);
        var expectedKey = H7DataverseEnvVarValuesHandler.BuildIdempotencyKey(CustomerId, configVer);
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H7",
            IdempotencyKey = expectedKey,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-4),
            JobId = "prior-run",
        });

        var repo = new FakeRepository(run, etag: "etag-2");
        var writer = FakeEnvVarValuesWriter.Success();
        var handler = BuildHandler(repo, writer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        ((HandlerResult.Success)result).IdempotencyKey.Should().Be(expectedKey);
        repo.LastWrittenRun.Should().BeNull("idempotent no-op does not mutate state");
        writer.CallCount.Should().Be(0, "no re-write");
    }

    // ---------- T3-T7 missing upstream state ----------

    [Fact]
    public async Task MissingTenantId_FailsResumable_NamesTenantId_NoWriterCall()
    {
        var run = BuildRun(includeTenantId: false);
        var repo = new FakeRepository(run, etag: "etag-3");
        var writer = FakeEnvVarValuesWriter.Success();
        var handler = BuildHandler(repo, writer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EnvVarValuesRejectionCodes.MissingUpstreamState);
        failure.Diagnostic.Should().Contain("tenantId");
        writer.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task MissingBffAppRegId_FailsResumable_NamesBffAppRegId_NoWriterCall()
    {
        var run = BuildRun();
        run.InterStepState.BffAppRegId = null;
        var repo = new FakeRepository(run, etag: "etag-4");
        var writer = FakeEnvVarValuesWriter.Success();
        var handler = BuildHandler(repo, writer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EnvVarValuesRejectionCodes.MissingUpstreamState);
        failure.Diagnostic.Should().Contain("bffAppRegId");
        writer.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task MissingDataverseEnvUrl_FailsResumable_NamesDataverseEnvUrl_NoWriterCall()
    {
        var run = BuildRun();
        run.InterStepState.DataverseEnvUrl = null;
        var repo = new FakeRepository(run, etag: "etag-5");
        var writer = FakeEnvVarValuesWriter.Success();
        var handler = BuildHandler(repo, writer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EnvVarValuesRejectionCodes.MissingUpstreamState);
        failure.Diagnostic.Should().Contain("dataverseEnvUrl");
        writer.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task MissingOpenAiEndpoint_FailsResumable_NamesOpenAiEndpoint_NoWriterCall()
    {
        var run = BuildRun();
        run.InterStepState.OpenAiEndpoint = null;
        var repo = new FakeRepository(run, etag: "etag-6");
        var writer = FakeEnvVarValuesWriter.Success();
        var handler = BuildHandler(repo, writer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EnvVarValuesRejectionCodes.MissingUpstreamState);
        failure.Diagnostic.Should().Contain("openAiEndpoint");
        writer.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task MissingSpeContainerId_FailsResumable_NamesSpeContainerId_NoWriterCall()
    {
        var run = BuildRun();
        run.InterStepState.SpeContainerId = null;
        var repo = new FakeRepository(run, etag: "etag-7");
        var writer = FakeEnvVarValuesWriter.Success();
        var handler = BuildHandler(repo, writer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EnvVarValuesRejectionCodes.MissingUpstreamState);
        failure.Diagnostic.Should().Contain("speContainerId");
        writer.CallCount.Should().Be(0);
    }

    // ---------- T8 missing client secret ----------

    [Fact]
    public async Task MissingClientSecret_FailsResumable_NoWriterCall()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-8");
        var writer = FakeEnvVarValuesWriter.Success();
        var handler = BuildHandler(repo, writer, clientSecret: null);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EnvVarValuesRejectionCodes.MissingClientSecret);
        writer.CallCount.Should().Be(0);
    }

    // ---------- NFR-05 (task 142): EnvVarValuesOptions.Validate() boot-time fail-fast ----------
    //
    // Parity with DataverseEnvironmentRegistryClientTests's "Options.Validate"
    // region (task 112/122) — these tests call the internal Validate() method
    // directly (InternalsVisibleTo covers this test project) rather than
    // spinning up a full WebApplicationFactory/Program.cs host, since the
    // Validate() logic itself is what AddOptions<T>().ValidateOnStart() invokes
    // at boot (see Program.cs's AddOptions<EnvVarValuesOptions>() registration).
    // These are DISTINCT from T8 above: T8 proves the handler's own RUNTIME
    // guard (defense-in-depth, still present); these prove the BOOT-TIME guard
    // this task adds on top of it.

    [Fact]
    public void OptionsValidate_Throws_When_ClientSecret_Null()
    {
        var options = new EnvVarValuesOptions { ClientSecret = null };

        FluentActions.Invoking(() => options.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*EnvVarValues:ClientSecret*required*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void OptionsValidate_Throws_When_ClientSecret_Empty_Or_Whitespace(string clientSecret)
    {
        var options = new EnvVarValuesOptions { ClientSecret = clientSecret };

        FluentActions.Invoking(() => options.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*EnvVarValues:ClientSecret*required*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void OptionsValidate_Throws_When_RequestTimeout_TooSmall(int seconds)
    {
        var options = new EnvVarValuesOptions
        {
            ClientSecret = ClientSecret,
            RequestTimeout = TimeSpan.FromSeconds(seconds),
        };

        FluentActions.Invoking(() => options.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*EnvVarValues:RequestTimeout*");
    }

    [Fact]
    public void OptionsValidate_Throws_When_RequestTimeout_TooLarge()
    {
        var options = new EnvVarValuesOptions
        {
            ClientSecret = ClientSecret,
            RequestTimeout = TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1),
        };

        FluentActions.Invoking(() => options.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*EnvVarValues:RequestTimeout*");
    }

    [Fact]
    public void OptionsValidate_Passes_With_ClientSecret_And_Default_Timeout()
    {
        var options = new EnvVarValuesOptions { ClientSecret = ClientSecret };

        FluentActions.Invoking(() => options.Validate()).Should().NotThrow();
    }

    [Fact]
    public void OptionsValidate_SectionName_Is_EnvVarValues()
    {
        // Ground-truths the literal Bicep app-setting key contract: the KV-ref
        // app setting in modules/controlplane-worker-app-service.bicep is
        // named "EnvVarValues__ClientSecret" (double-underscore hierarchical
        // delimiter over section "EnvVarValues" + property "ClientSecret").
        // A drift here would silently break the KV-ref binding in every
        // deployed environment without any build-time signal.
        EnvVarValuesOptions.SectionName.Should().Be("EnvVarValues");
    }

    // ---------- T9 canonical name enumeration ----------

    [Fact]
    public async Task AllSevenCanonicalSchemaNames_PresentWithExactSpelling()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-9");
        var writer = FakeEnvVarValuesWriter.Success();
        var handler = BuildHandler(repo, writer);

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        writer.LastRequest!.Values.Select(kv => kv.Key).Should().BeEquivalentTo(new[]
        {
            "sprk_BffApiBaseUrl",
            "sprk_BffApiAppId",
            "sprk_MsalClientId",
            "sprk_TenantId",
            "sprk_AzureOpenAiEndpoint",
            "sprk_ShareLinkBaseUrl",
            "sprk_SharePointEmbeddedContainerId",
        });
    }

    // ---------- T10-T12 default resolution ----------

    [Fact]
    public async Task BffApiBaseUrl_DefaultsToCanonicalUrl_WhenParameterAbsent()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-10");
        var writer = FakeEnvVarValuesWriter.Success();
        var handler = BuildHandler(repo, writer);

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        writer.LastRequest!.Values.Single(kv => kv.Key == "sprk_BffApiBaseUrl").Value
            .Should().Be("https://api.spaarke.com");
    }

    [Fact]
    public async Task MsalClientId_DefaultsToBffAppRegId_WhenParameterAbsent()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-11");
        var writer = FakeEnvVarValuesWriter.Success();
        var handler = BuildHandler(repo, writer);

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        writer.LastRequest!.Values.Single(kv => kv.Key == "sprk_MsalClientId").Value
            .Should().Be(BffAppRegId);
    }

    [Fact]
    public async Task ShareLinkBaseUrl_ResolvesToEmptyString_WhenParameterAbsent_DoesNotFail()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-12");
        var writer = FakeEnvVarValuesWriter.Success();
        var handler = BuildHandler(repo, writer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        writer.LastRequest!.Values.Single(kv => kv.Key == "sprk_ShareLinkBaseUrl").Value
            .Should().BeEmpty();
    }

    // ---------- T13-T16 writer failure mapping ----------

    [Fact]
    public async Task WriterDefinitionNotFound_FailsResumable_WithExpectedCode()
    {
        await AssertWriterFailureMapsTo(
            EnvVarValuesWriteFailureKind.DefinitionNotFound,
            EnvVarValuesRejectionCodes.EnvVarDefinitionNotFound);
    }

    [Fact]
    public async Task WriterAuthFailure_FailsResumable_WithExpectedCode()
    {
        await AssertWriterFailureMapsTo(
            EnvVarValuesWriteFailureKind.AuthFailure,
            EnvVarValuesRejectionCodes.DataverseAuthFailure);
    }

    [Fact]
    public async Task WriterRateLimited_FailsResumable_WithExpectedCode()
    {
        await AssertWriterFailureMapsTo(
            EnvVarValuesWriteFailureKind.RateLimited,
            EnvVarValuesRejectionCodes.RateLimited);
    }

    [Fact]
    public async Task WriterUnknownInvocationFailure_FailsResumable_WithExpectedCode()
    {
        await AssertWriterFailureMapsTo(
            EnvVarValuesWriteFailureKind.UnknownInvocationFailure,
            EnvVarValuesRejectionCodes.WriterInvocationFailed);
    }

    // ---------- T17 writer infrastructure exception ----------

    [Fact]
    public async Task WriterInfrastructureException_FailsResumable_WithWriterInvocationFailedCode()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-17");
        var writer = new ThrowingEnvVarValuesWriter(new InvalidOperationException("HTTP transport unavailable"));
        var handler = BuildHandler(repo, writer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EnvVarValuesRejectionCodes.WriterInvocationFailed);
        failure.Diagnostic.Should().Contain("InvalidOperationException");
    }

    // ---------- T18 handler-id mismatch ----------

    [Fact]
    public async Task HandlerIdMismatch_Throws()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-18");
        var handler = BuildHandler(repo, FakeEnvVarValuesWriter.Success());

        var wrongEnvelope = new HandlerEnvelope
        {
            HandlerId = "H0",
            RunId = RunId,
            CustomerId = CustomerId,
            ParametersJson = "{}",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        var act = async () => await handler.HandleAsync(wrongEnvelope, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*mismatched HandlerId*");
    }

    // ---------- T19 idempotency key determinism ----------

    [Fact]
    public void IdempotencyKey_IsDeterministicByCustomerIdAndConfigVer_DiffersOnValueChange()
    {
        var values1 = new List<KeyValuePair<string, string>>
        {
            new("sprk_BffApiBaseUrl", "https://api.spaarke.com"),
        };
        var values2 = new List<KeyValuePair<string, string>>
        {
            new("sprk_BffApiBaseUrl", "https://api.other.com"),
        };

        var k1a = H7DataverseEnvVarValuesHandler.BuildIdempotencyKey(
            "acme", H7DataverseEnvVarValuesHandler.ComputeConfigVer(values1));
        var k1b = H7DataverseEnvVarValuesHandler.BuildIdempotencyKey(
            "acme", H7DataverseEnvVarValuesHandler.ComputeConfigVer(values1));
        var k2 = H7DataverseEnvVarValuesHandler.BuildIdempotencyKey(
            "acme", H7DataverseEnvVarValuesHandler.ComputeConfigVer(values2));

        k1a.Should().Be(k1b, "same resolved values produce the same key");
        k1a.Should().NotBe(k2, "a changed resolved value produces a different key");
        k1a.Should().StartWith("envvars-acme-");
    }

    // ---------- T20 run not found ----------

    [Fact]
    public async Task RunNotFound_ReturnsResumableFailure()
    {
        var repo = new FakeRepository(run: null, etag: null);
        var writer = FakeEnvVarValuesWriter.Success();
        var handler = BuildHandler(repo, writer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EnvVarValuesRejectionCodes.RunNotFound);
        writer.CallCount.Should().Be(0);
    }

    // ---------- T21 mapping table ----------

    [Theory]
    [InlineData(EnvVarValuesWriteFailureKind.AuthFailure, EnvVarValuesRejectionCodes.DataverseAuthFailure)]
    [InlineData(EnvVarValuesWriteFailureKind.RateLimited, EnvVarValuesRejectionCodes.RateLimited)]
    [InlineData(EnvVarValuesWriteFailureKind.DefinitionNotFound, EnvVarValuesRejectionCodes.EnvVarDefinitionNotFound)]
    [InlineData(EnvVarValuesWriteFailureKind.UnknownInvocationFailure, EnvVarValuesRejectionCodes.WriterInvocationFailed)]
    public void MapWriterFailure_ProducesExpectedRejection_AllResumable(
        EnvVarValuesWriteFailureKind kind, string expectedRejection)
    {
        var (rejection, cls) = H7DataverseEnvVarValuesHandler.MapWriterFailure(kind);
        rejection.Should().Be(expectedRejection);
        cls.Should().Be(FailureClass.Resumable);
    }

    // ---------- T22 concurrent write conflict ----------

    [Fact]
    public async Task ConcurrentWriteConflictOnSuccessPath_ReturnsResumableFailure()
    {
        var run = BuildRun();
        var repo = new ConflictingRepository(run, etag: "etag-22");
        var writer = FakeEnvVarValuesWriter.Success();
        var handler = BuildHandler(repo, writer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EnvVarValuesRejectionCodes.ConcurrentWriteConflict);
    }

    // ---------- T23 client-startup-contract proxy ----------

    [Fact]
    public async Task AllWrittenValues_AreNonNull_ProxyForClientFailFastContract()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-23");
        var writer = FakeEnvVarValuesWriter.Success();
        var handler = BuildHandler(repo, writer);

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        writer.LastRequest!.Values.Should().HaveCount(7);
        writer.LastRequest.Values.Should().OnlyContain(kv => kv.Value != null);
    }

    // ---------- helpers ----------

    private async Task AssertWriterFailureMapsTo(
        EnvVarValuesWriteFailureKind kind,
        string expectedCode)
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-fail");
        var writer = FakeEnvVarValuesWriter.Failure(kind, $"canned failure: {kind}");
        var handler = BuildHandler(repo, writer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(expectedCode);
    }

    // ---------- A44.5 (task 205i): FR-39 ordered credential chain ----------
    // The H7/task-142 half of A30's sentinel contract. Secret-free envs (§6.5
    // resolution prong 1) run the Worker with EnvVarValues__ClientSecret
    // OMITTED (empty is the SIGNAL — auth-v4 §9.1); the chain
    // EnvVarValues:Credentials:Order:0=ManagedIdentityFederated selects MI-FIC.
    // Pre-migration (prong-3) envs keep task-142 semantics unchanged — those
    // are the pre-existing tests above (default legacy chain).

    /// <summary>
    /// Goal (a)+(d) proxy at the handler boundary: under the MI-FIC-first
    /// secret-free chain an EMPTY secret slot does NOT fail the run — the
    /// handler proceeds to the writer (which resolves MI-FIC via
    /// WorkerDataverseCredentialFactory). No boot-loop, no sentinel.
    /// </summary>
    [Fact]
    public async Task SecretFree_MiFicFirstChain_EmptySecret_ProceedsToWriterAndSucceeds()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-a44");
        var writer = FakeEnvVarValuesWriter.Success();
        var handler = BuildHandler(repo, writer, clientSecret: null, credentials: SecretFreeChain());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        writer.CallCount.Should().Be(1);
        // The empty slot flows through EMPTY — never a fabricated placeholder
        // (§9.1: the ordered selector cannot distinguish a sentinel from a
        // real secret; AADSTS7000215 otherwise).
        writer.LastRequest!.ClientSecret.Should().BeNull();
    }

    /// <summary>Legacy chain + empty secret keeps failing (task-142 semantics preserved — explicit Order variant of T8).</summary>
    [Fact]
    public async Task ExplicitSecretFirstChain_EmptySecret_StillFailsMissingClientSecret()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-a44b");
        var writer = FakeEnvVarValuesWriter.Success();
        var handler = BuildHandler(repo, writer, clientSecret: null,
            credentials: new WorkerCredentialSelectionOptions { Order = { nameof(CredentialKind.ClientSecret) } });

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(EnvVarValuesRejectionCodes.MissingClientSecret);
        writer.CallCount.Should().Be(0);
    }

    // ---------- A44.5: EnvVarValuesOptions.Validate() chain-aware boundary ----------

    /// <summary>Goal (c): the §10.2 secret-free contract boots with an EMPTY secret slot.</summary>
    [Fact]
    public void OptionsValidate_Accepts_EmptyClientSecret_When_MiFicFirst()
    {
        var options = new EnvVarValuesOptions { ClientSecret = null, Credentials = SecretFreeChain() };

        var act = () => options.Validate();

        act.Should().NotThrow(
            "on a secret-free environment the EnvVarValues__ClientSecret KV-ref is omitted and empty is " +
            "the signal (auth-v4 §9.1) — the MI-FIC-first chain authenticates without it");
    }

    /// <summary>MI-FIC-first with the transitional secret still present is also valid (rollback-capable shape).</summary>
    [Fact]
    public void OptionsValidate_Accepts_MiFicFirst_WithTransitionalSecretPresent()
    {
        var options = new EnvVarValuesOptions
        {
            ClientSecret = ClientSecret,
            Credentials = new WorkerCredentialSelectionOptions
            {
                Order =
                {
                    nameof(CredentialKind.ManagedIdentityFederated),
                    nameof(CredentialKind.ClientSecret),
                },
            },
        };

        options.Invoking(o => o.Validate()).Should().NotThrow();
    }

    /// <summary>Fail-fast preserved when a secret-based provider is REQUIRED (explicit secret-first chain, empty slot).</summary>
    [Fact]
    public void OptionsValidate_Throws_When_ExplicitSecretFirstChain_And_EmptySecret()
    {
        var options = new EnvVarValuesOptions
        {
            ClientSecret = "",
            Credentials = new WorkerCredentialSelectionOptions { Order = { nameof(CredentialKind.ClientSecret) } },
        };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*EnvVarValues:ClientSecret*required*");
    }

    /// <summary>Invalid provider-chain configuration fail-fasts: unknown kind name (incl. the unsupported KeyVaultCertificate).</summary>
    [Theory]
    [InlineData("NotARealKind")]
    [InlineData("KeyVaultCertificate")]
    public void OptionsValidate_Throws_On_UnknownCredentialKind(string kind)
    {
        var options = new EnvVarValuesOptions
        {
            ClientSecret = ClientSecret,
            Credentials = new WorkerCredentialSelectionOptions { Order = { kind } },
        };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*EnvVarValues:Credentials:Order*not a known credential kind*");
    }

    /// <summary>Invalid provider-chain configuration fail-fasts: duplicate kind.</summary>
    [Fact]
    public void OptionsValidate_Throws_On_DuplicateCredentialKind()
    {
        var options = new EnvVarValuesOptions
        {
            ClientSecret = ClientSecret,
            Credentials = new WorkerCredentialSelectionOptions
            {
                Order =
                {
                    nameof(CredentialKind.ClientSecret),
                    nameof(CredentialKind.ClientSecret),
                },
            },
        };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*more than once*");
    }

    /// <summary>§10.2 mirror of BFF IdentityConfigurationValidator rule 6: RequireSecretFreeIdentity + secret kind listed → fail-fast.</summary>
    [Fact]
    public void OptionsValidate_Throws_When_RequireSecretFreeIdentity_And_ClientSecretListed()
    {
        var options = new EnvVarValuesOptions
        {
            ClientSecret = ClientSecret,
            Credentials = new WorkerCredentialSelectionOptions
            {
                Order =
                {
                    nameof(CredentialKind.ManagedIdentityFederated),
                    nameof(CredentialKind.ClientSecret),
                },
                RequireSecretFreeIdentity = true,
            },
        };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*RequireSecretFreeIdentity*ClientSecret*");
    }

    /// <summary>RequireSecretFreeIdentity with NO order configured is contradictory (legacy default is secret-based) → fail-fast.</summary>
    [Fact]
    public void OptionsValidate_Throws_When_RequireSecretFreeIdentity_And_NoOrderConfigured()
    {
        var options = new EnvVarValuesOptions
        {
            ClientSecret = null,
            Credentials = new WorkerCredentialSelectionOptions { RequireSecretFreeIdentity = true },
        };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*RequireSecretFreeIdentity*Order*");
    }

    private static H7DataverseEnvVarValuesHandler BuildHandler(
        IProvisioningRunRepository repo,
        IEnvVarValuesWriter writer,
        string? clientSecret = ClientSecret,
        WorkerCredentialSelectionOptions? credentials = null)
    {
        var options = Options.Create(new EnvVarValuesOptions
        {
            ClientSecret = clientSecret,
            RequestTimeout = TimeSpan.FromSeconds(5),
            // A44.5: default (unconfigured) = legacy [ClientSecret] chain —
            // every pre-existing test in this file exercises task-142
            // semantics unchanged.
            Credentials = credentials ?? new WorkerCredentialSelectionOptions(),
        });
        return new H7DataverseEnvVarValuesHandler(
            repo, writer, options,
            TimeProvider.System,
            NullLogger<H7DataverseEnvVarValuesHandler>.Instance);
    }

    /// <summary>The §10.2 secret-free chain: MI-FIC as the ONLY entry + fail-fast assertion.</summary>
    private static WorkerCredentialSelectionOptions SecretFreeChain() => new()
    {
        Order = { nameof(CredentialKind.ManagedIdentityFederated) },
        RequireSecretFreeIdentity = true,
    };

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = H7DataverseEnvVarValuesHandler.HandlerIdentifier,
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = "{}",
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    private static ProvisioningRun BuildRun(bool includeTenantId = true)
    {
        var run = new ProvisioningRun
        {
            RunId = RunId,
            CustomerId = CustomerId,
            EnvironmentId = "env-guid",
            TenancyModel = "Model2Dedicated",
            Status = RunStatus.Running,
            Profile = "spaarke-hosted-model2",
        };
        if (includeTenantId)
        {
            run.Parameters.NonSecret[H7DataverseEnvVarValuesHandler.TenantIdParameterKey] = TenantId;
        }
        run.InterStepState.BffAppRegId = BffAppRegId;
        run.InterStepState.DataverseEnvUrl = EnvUrl;
        run.InterStepState.OpenAiEndpoint = OpenAiEndpoint;
        run.InterStepState.SpeContainerId = SpeContainerId;
        return run;
    }

    private static List<KeyValuePair<string, string>> ExpectedValues(ProvisioningRun run) => new()
    {
        new("sprk_BffApiBaseUrl", "https://api.spaarke.com"),
        new("sprk_BffApiAppId", run.InterStepState.BffAppRegId!),
        new("sprk_MsalClientId", run.InterStepState.BffAppRegId!),
        new("sprk_TenantId", TenantId),
        new("sprk_AzureOpenAiEndpoint", run.InterStepState.OpenAiEndpoint!),
        new("sprk_ShareLinkBaseUrl", string.Empty),
        new("sprk_SharePointEmbeddedContainerId", run.InterStepState.SpeContainerId!),
    };

    // ---------- fakes ----------

    /// <summary>Repository fake — records last written run + last write etag.</summary>
    private sealed class FakeRepository : IProvisioningRunRepository
    {
        private ProvisioningRun? _run;
        private string? _etag;
        public ProvisioningRun? LastWrittenRun { get; private set; }

        public FakeRepository(ProvisioningRun? run, string? etag)
        {
            _run = run;
            _etag = etag;
        }

        public Task<ProvisioningRunReadResult?> ReadRunAsync(string customerId, string runId, CancellationToken ct)
            => Task.FromResult(_run is null || _etag is null
                ? null
                : new ProvisioningRunReadResult(_run, _etag));

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

    /// <summary>Repository fake that always reports a concurrency Conflict on write.</summary>
    private sealed class ConflictingRepository : IProvisioningRunRepository
    {
        private readonly ProvisioningRun _run;
        private readonly string _etag;

        public ConflictingRepository(ProvisioningRun run, string etag)
        {
            _run = run;
            _etag = etag;
        }

        public Task<ProvisioningRunReadResult?> ReadRunAsync(string customerId, string runId, CancellationToken ct)
            => Task.FromResult<ProvisioningRunReadResult?>(new ProvisioningRunReadResult(_run, _etag));

        public Task<ProvisioningRunReadResult> CreateRunAsync(ProvisioningRun run, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<ReplaceRunResult> ReplaceRunAsync(ProvisioningRun run, string ifMatchEtag, CancellationToken ct)
            => Task.FromResult<ReplaceRunResult>(
                new ReplaceRunResult.Conflict(new ProvisioningRunReadResult(_run, _etag)));
    }

    /// <summary>Writer fake — returns a fixed outcome + records the last request.</summary>
    private sealed class FakeEnvVarValuesWriter : IEnvVarValuesWriter
    {
        private readonly EnvVarValuesWriteOutcome _outcome;
        public int CallCount { get; private set; }
        public EnvVarValuesWriteRequest? LastRequest { get; private set; }

        private FakeEnvVarValuesWriter(EnvVarValuesWriteOutcome outcome) => _outcome = outcome;

        public static FakeEnvVarValuesWriter Success()
            => new(new EnvVarValuesWriteOutcome.Success(new List<KeyValuePair<string, string>>()));

        public static FakeEnvVarValuesWriter Failure(EnvVarValuesWriteFailureKind kind, string diagnostic)
            => new(new EnvVarValuesWriteOutcome.Failure(kind, SchemaName: null, diagnostic));

        public Task<EnvVarValuesWriteOutcome> WriteAsync(EnvVarValuesWriteRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            var outcome = _outcome is EnvVarValuesWriteOutcome.Success
                ? new EnvVarValuesWriteOutcome.Success(request.Values)
                : _outcome;
            return Task.FromResult(outcome);
        }
    }

    /// <summary>Writer that always throws — models an HTTP transport / token-acquisition fault.</summary>
    private sealed class ThrowingEnvVarValuesWriter : IEnvVarValuesWriter
    {
        private readonly Exception _exception;
        public ThrowingEnvVarValuesWriter(Exception ex) => _exception = ex;

        public Task<EnvVarValuesWriteOutcome> WriteAsync(EnvVarValuesWriteRequest request, CancellationToken ct)
            => throw _exception;
    }
}
