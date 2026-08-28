// -----------------------------------------------------------------------------
// ExchangePolicySidecarClientContractTests.cs
//
// L2 CONTROL-PLANE contract-and-envelope tests for ExchangePolicySidecarClient
// (task 161, Wave G-6 — H14a sidecar client wiring). Proves the wire contract
// between the C# client and task 114's Listener.ps1 in BOTH directions:
//
//   (1) The client's request serialization emits EVERY field Listener.ps1's
//       body-parsing block reads (tenantId, expectedAppIds, policyScopeGroupId,
//       correlationId + optional descriptionPrefix, timeoutSeconds) at the
//       expected JSON path with the expected type.
//
//   (2) The client's response deserialization correctly maps EVERY outcome
//       Listener.ps1 emits (Success, AlreadyCompliant, Drift, Failure) onto
//       the 3-case IExchangePolicyApplier seam per the mapping documented in
//       ExchangePolicySidecarClient.cs's file header:
//
//         wire Success            + createdCount>0  -> Applied(createdCount, observedAppIds)
//         wire AlreadyCompliant   + createdCount=0  -> Applied(0, observedAppIds)
//         wire Drift                                -> Drift(expected, observed)
//         wire Failure                              -> RetryEligible (retries once, then surfaces Failure)
//
// AUTHORITATIVE CONTRACT: Listener.ps1's own .DESCRIPTION block (lines 14-54)
// is the single source of truth for the wire shape — where the POML's own
// prose disagrees, Listener.ps1 wins because it is the deployable artifact
// (POML §escalation-trigger). This test suite is authored against Listener.ps1's
// documented shape, not the POML's abbreviation.
//
// ADR-038 CATEGORY: Path #1 — pure C# unit test with a hand-rolled
// HttpMessageHandler as the transport fake. Never Mock<HttpMessageHandler>.
// No live sidecar / no live pwsh — end-to-end verification against a live
// sidecar is task 162's separate scope.
//
// COVERAGE (task 161 acceptance criteria):
//   AC-1  Request envelope shape — every Listener.ps1-parsed field present at
//         expected JSON path with expected type.
//   AC-2  X-Sidecar-Auth header present on EVERY outbound request
//         (criterion #2 verbatim).
//   AC-3  correlationId in outbound request equals RunId of invoking handler
//         (criterion #3 verbatim).
//   AC-4  Response deserialization — wire Success + createdCount>0 -> Applied.
//   AC-5  Response deserialization — wire AlreadyCompliant + createdCount=0 -> Applied.
//   AC-6  Response deserialization — wire Drift -> Drift(expected, observed).
//   AC-7  Response deserialization — wire Failure -> retry-once, then Failure.
//   AC-8  Connection-refused (HttpRequestException) -> Failure (H14a
//         classifies Resumable, per criterion #5).
//   AC-9  HttpClient timeout -> Failure (same InfraFault semantics).
//   AC-10 HTTP 401 auth failure -> terminal Failure naming the KV secret + vault to check.
//   AC-11 HTTP 400 bad-request -> terminal Failure with sidecar's own diagnostic.
//   AC-12 HTTP 5xx -> retry once (transient), then surface Failure on 2nd attempt.
//   AC-13 Malformed JSON 200 body -> terminal Failure (never silent-null).
//   AC-14 Unknown outcome value -> terminal Failure naming the expected outcome set.
//   AC-15 Empty CorrelationId in request -> terminal Failure (contract violation guarded before HTTP).
//   AC-16 Empty shared-secret config -> terminal Failure BEFORE any HTTP call
//         (silent-fail-audit trap closed: never proceed with empty header).
//   AC-17 KV read NotFound -> terminal Failure with explicit "secret not found on vault" diagnostic.
//   AC-18 KV read Failure -> terminal Failure passing through KV diagnostic.
//   AC-19 Source-text: no ProcessStartInfo / no Set-ExchangeApplicationAccessPolicy
//         reference in the new production file (criterion #1 verbatim).
// -----------------------------------------------------------------------------

using System.IO;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ExchangePolicySidecarClientContractTests
{
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string BffAppRegId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string UamiClientId = "11111111-2222-3333-4444-555555555555";
    private const string PolicyScopeGroupId = "77777777-8888-9999-0000-111111111111";
    private const string RunId = "01j7q3zp-h14a-contract-test-run";
    private const string DescriptionPrefix = "Spaarke-Provisioning-AppAccessPolicy";
    private const string PlatformKvVault = "sprk-controlplane-dev-kv";
    private const string PlatformKvSubscription = "22222222-3333-4444-5555-666666666666";
    private const string SharedSecretName = "Sidecar-Shared-Secret";
    private const string SharedSecretValue = "per-boot-shared-secret-value-42";

    // ========== AC-1 REQUEST ENVELOPE ==========

    [Fact]
    public async Task AC1_RequestEnvelope_ContainsEveryFieldListenerPs1Parses()
    {
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => OkJson(WireSuccess(createdCount: 2)),
        };
        var client = NewClient(handler);

        var result = await client.ApplyAsync(BuildRequest(), CancellationToken.None);

        result.Should().BeOfType<ExchangePolicyApplyOutcome.Applied>();
        handler.CapturedRequests.Should().ContainSingle();
        var body = handler.CapturedRequests[0].BodyJson!;
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // Listener.ps1 lines 370-374 read these six fields.
        root.GetProperty("tenantId").GetString().Should().Be(TenantId);
        var expected = root.GetProperty("expectedAppIds").EnumerateArray().Select(e => e.GetString()).ToArray();
        expected.Should().BeEquivalentTo(new[] { BffAppRegId, UamiClientId });
        root.GetProperty("policyScopeGroupId").GetString().Should().Be(PolicyScopeGroupId);
        root.GetProperty("descriptionPrefix").GetString().Should().Be(DescriptionPrefix);
        root.GetProperty("correlationId").GetString().Should().Be(RunId);
        root.GetProperty("timeoutSeconds").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AC1b_RequestEnvelope_OmitsDescriptionPrefix_WhenEmpty_LettingListenerDefaultServerSide()
    {
        // Listener.ps1 line 373 defaults descriptionPrefix server-side when
        // the property is absent (`if body.PSObject.Properties['descriptionPrefix'] -and body.descriptionPrefix`).
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(WireSuccess(1)) };
        var client = NewClient(handler);
        var request = new ExchangePolicyApplyRequest(TenantId, new[] { BffAppRegId, UamiClientId }, PolicyScopeGroupId, string.Empty, RunId);

        await client.ApplyAsync(request, CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.CapturedRequests.Single().BodyJson!);
        doc.RootElement.TryGetProperty("descriptionPrefix", out var d).Should().BeFalse(
            "empty descriptionPrefix must be omitted so Listener.ps1's server-side default takes effect");
    }

    // ========== AC-2 X-SIDECAR-AUTH HEADER (criterion #2 verbatim) ==========

    [Fact]
    public async Task AC2_XSidecarAuthHeader_IsPresentOnEveryOutboundRequest()
    {
        // Two attempts (retry path) to prove the header is on BOTH.
        var responses = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(new Func<HttpRequestMessage, HttpResponseMessage>[]
        {
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("first attempt 503") },
            _ => OkJson(WireSuccess(2)),
        });
        var handler = new CapturingHandler { ResponseFactory = req => responses.Dequeue()(req) };
        var client = NewClient(handler);

        await client.ApplyAsync(BuildRequest(), CancellationToken.None);

        handler.CapturedRequests.Should().HaveCount(2, "5xx triggers one retry per DS-1b §3");
        foreach (var captured in handler.CapturedRequests)
        {
            captured.Headers.Should().ContainKey(ExchangePolicySidecarClient.SharedSecretHeaderName);
            captured.Headers[ExchangePolicySidecarClient.SharedSecretHeaderName]
                .Should().Be(SharedSecretValue, "the per-boot shared secret from platform KV MUST accompany every outbound request");
        }
    }

    // ========== AC-3 CORRELATION ID = RUN ID (criterion #3 verbatim) ==========

    [Fact]
    public async Task AC3_CorrelationId_EqualsInvokingHandlerRunId()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(WireSuccess(2)) };
        var client = NewClient(handler);

        await client.ApplyAsync(BuildRequest(), CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.CapturedRequests.Single().BodyJson!);
        doc.RootElement.GetProperty("correlationId").GetString().Should().Be(RunId,
            "sidecar's stdout log lines interleave with Worker logs by correlationId — MUST equal RunId");
    }

    // ========== AC-4..AC-6 RESPONSE ENVELOPE MAPPING (all 4 wire outcomes) ==========

    [Fact]
    public async Task AC4_WireSuccess_CreatedCount2_MapsToApplied()
    {
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => OkJson(WireSuccess(createdCount: 2)),
        };
        var client = NewClient(handler);

        var result = await client.ApplyAsync(BuildRequest(), CancellationToken.None);

        var applied = result.Should().BeOfType<ExchangePolicyApplyOutcome.Applied>().Subject;
        applied.CreatedCount.Should().Be(2);
        applied.ObservedAppIds.Should().BeEquivalentTo(new[] { BffAppRegId, UamiClientId });
    }

    [Fact]
    public async Task AC5_WireAlreadyCompliant_CreatedCount0_MapsToApplied()
    {
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => OkJson(WireAlreadyCompliant()),
        };
        var client = NewClient(handler);

        var result = await client.ApplyAsync(BuildRequest(), CancellationToken.None);

        var applied = result.Should().BeOfType<ExchangePolicyApplyOutcome.Applied>().Subject;
        applied.CreatedCount.Should().Be(0);
        applied.ObservedAppIds.Should().BeEquivalentTo(new[] { BffAppRegId, UamiClientId });
    }

    [Fact]
    public async Task AC6_WireDrift_MapsToDrift_WithExpectedAndObservedBothPreserved()
    {
        var driftObserved = new[] { BffAppRegId, "ffffffff-0000-0000-0000-000000000000" };
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => OkJson(WireDrift(new[] { BffAppRegId, UamiClientId }, driftObserved)),
        };
        var client = NewClient(handler);

        var result = await client.ApplyAsync(BuildRequest(), CancellationToken.None);

        var drift = result.Should().BeOfType<ExchangePolicyApplyOutcome.Drift>().Subject;
        drift.ExpectedAppIds.Should().BeEquivalentTo(new[] { BffAppRegId, UamiClientId });
        drift.ObservedAppIds.Should().BeEquivalentTo(driftObserved);
    }

    // ========== AC-7 WIRE FAILURE -> retry-once then Failure ==========

    [Fact]
    public async Task AC7_WireFailure_RetriesOnce_ThenSurfacesFailure_OnPersistentDiagnostic()
    {
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => OkJson(WireFailure("EXO throttled — TooManyRequests")),
        };
        var client = NewClient(handler);

        var result = await client.ApplyAsync(BuildRequest(), CancellationToken.None);

        handler.CapturedRequests.Should().HaveCount(2, "DS-1b §3 mandates one retry for transient failures");
        var failure = result.Should().BeOfType<ExchangePolicyApplyOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("EXO throttled").And.Contain("retry");
    }

    [Fact]
    public async Task AC7b_WireFailure_ThenSuccess_SurfacesApplied_WithoutFailurePropagation()
    {
        var responses = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(new Func<HttpRequestMessage, HttpResponseMessage>[]
        {
            _ => OkJson(WireFailure("transient throttle")),
            _ => OkJson(WireSuccess(1)),
        });
        var handler = new CapturingHandler { ResponseFactory = req => responses.Dequeue()(req) };
        var client = NewClient(handler);

        var result = await client.ApplyAsync(BuildRequest(), CancellationToken.None);

        handler.CapturedRequests.Should().HaveCount(2);
        result.Should().BeOfType<ExchangePolicyApplyOutcome.Applied>();
    }

    // ========== AC-8 CONNECTION REFUSED -> Failure (Resumable, criterion #5) ==========

    [Fact]
    public async Task AC8_ConnectionRefused_MapsToFailure_H14aClassifiesResumable()
    {
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => throw new HttpRequestException("Connection refused: sidecar not listening on 127.0.0.1:8091"),
        };
        var client = NewClient(handler);

        var result = await client.ApplyAsync(BuildRequest(), CancellationToken.None);

        // DS-1b §3: connection-refused -> InfraFault (Resumable). No in-client
        // retry per that rule (verified: exactly 1 attempt made).
        handler.CapturedRequests.Should().HaveCount(1,
            "DS-1b §3 mandates NO in-client retry for connection-refused — reconciler re-enqueues at run level");
        var failure = result.Should().BeOfType<ExchangePolicyApplyOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("Connection refused").And.Contain("InfraFault");
    }

    // ========== AC-9 TIMEOUT -> Failure ==========

    [Fact]
    public async Task AC9_HttpClientTimeout_MapsToFailure_NoRetry()
    {
        // Use a cancellation-respecting Task.Delay so HttpClient.Timeout's
        // internal CTS can actually observe the cancellation (Thread.Sleep
        // blocks the OS thread and never observes cancellation — HttpClient
        // couldn't interrupt it, so the "timeout" branch would never fire).
        var handler = new CapturingHandler
        {
            DelayBeforeResponse = TimeSpan.FromSeconds(5),
            ResponseFactory = _ => OkJson(WireSuccess(1)),
        };
        // Override HttpClient.Timeout directly on the test-injected instance
        // (bypasses the Validate() 30 s min bound, which is a boot-time gate
        // for production callers — not applicable to a pre-configured test seam).
        var client = NewClient(handler, configureHttpClient: c => c.Timeout = TimeSpan.FromMilliseconds(50));

        var result = await client.ApplyAsync(BuildRequest(), CancellationToken.None);

        handler.CapturedRequests.Should().HaveCount(1, "DS-1b §3: NO in-client retry for timeouts");
        var failure = result.Should().BeOfType<ExchangePolicyApplyOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("timed out").And.Contain("InfraFault");
    }

    // ========== AC-10 HTTP 401 -> terminal Failure naming secret + vault ==========

    [Fact]
    public async Task AC10_Http401_ReturnsTerminalFailure_NamingKvSecretAndVault_NoRetry()
    {
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"outcome":"Failure","diagnostic":"Missing or invalid X-Sidecar-Auth header."}"""),
            },
        };
        var client = NewClient(handler);

        var result = await client.ApplyAsync(BuildRequest(), CancellationToken.None);

        handler.CapturedRequests.Should().HaveCount(1, "HTTP 401 is permanent — no retry");
        var failure = result.Should().BeOfType<ExchangePolicyApplyOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("401")
            .And.Contain(SharedSecretName, "operator must know WHICH secret to check")
            .And.Contain(PlatformKvVault, "operator must know WHICH vault to check");
    }

    // ========== AC-11 HTTP 400 -> terminal Failure with sidecar diagnostic ==========

    [Fact]
    public async Task AC11_Http400_ReturnsTerminalFailure_WithSidecarDiagnostic_NoRetry()
    {
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"outcome":"Failure","diagnostic":"expectedAppIds must contain exactly 2 GUIDs (got 3)"}"""),
            },
        };
        var client = NewClient(handler);

        var result = await client.ApplyAsync(BuildRequest(), CancellationToken.None);

        handler.CapturedRequests.Should().HaveCount(1);
        var failure = result.Should().BeOfType<ExchangePolicyApplyOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("400").And.Contain("expectedAppIds must contain exactly 2");
    }

    // ========== AC-12 HTTP 5xx -> retry once, then Failure ==========

    [Fact]
    public async Task AC12_Http503_RetriesOnce_ThenFailureOnPersistentServerError()
    {
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("sidecar overloaded"),
            },
        };
        var client = NewClient(handler);

        var result = await client.ApplyAsync(BuildRequest(), CancellationToken.None);

        handler.CapturedRequests.Should().HaveCount(2, "5xx is retry-eligible per DS-1b §3");
        var failure = result.Should().BeOfType<ExchangePolicyApplyOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("503");
    }

    // ========== AC-13 MALFORMED JSON -> Failure (never silent-null) ==========

    [Fact]
    public async Task AC13_Http200_MalformedJson_ReturnsTerminalFailure_NeverSilentNull()
    {
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{not-valid-json"),
            },
        };
        var client = NewClient(handler);

        var result = await client.ApplyAsync(BuildRequest(), CancellationToken.None);

        var failure = result.Should().BeOfType<ExchangePolicyApplyOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("unparseable JSON",
            "silent-fail-audit: malformed sidecar response must fail loud, never silent-null-return");
    }

    // ========== AC-14 UNKNOWN OUTCOME -> Failure (never silent-continue) ==========

    [Fact]
    public async Task AC14_Http200_UnknownOutcomeValue_ReturnsTerminalFailure_NamingExpectedSet()
    {
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"outcome":"Nonsense","createdCount":0,"observedAppIds":[]}"""),
            },
        };
        var client = NewClient(handler);

        var result = await client.ApplyAsync(BuildRequest(), CancellationToken.None);

        var failure = result.Should().BeOfType<ExchangePolicyApplyOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("unknown outcome 'Nonsense'")
            .And.Contain(ExchangePolicySidecarClient.WireOutcomeSuccess)
            .And.Contain(ExchangePolicySidecarClient.WireOutcomeAlreadyCompliant)
            .And.Contain(ExchangePolicySidecarClient.WireOutcomeDrift)
            .And.Contain(ExchangePolicySidecarClient.WireOutcomeFailure);
    }

    // ========== AC-15 EMPTY CORRELATION ID -> Failure BEFORE HTTP ==========

    [Fact]
    public async Task AC15_EmptyCorrelationId_ReturnsFailure_BeforeAnyHttpCall()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(WireSuccess(2)) };
        var client = NewClient(handler);
        var badRequest = new ExchangePolicyApplyRequest(
            TenantId, new[] { BffAppRegId, UamiClientId }, PolicyScopeGroupId, DescriptionPrefix, CorrelationId: "");

        var result = await client.ApplyAsync(badRequest, CancellationToken.None);

        handler.CapturedRequests.Should().BeEmpty("empty CorrelationId is a contract violation — MUST NOT hit the wire");
        var failure = result.Should().BeOfType<ExchangePolicyApplyOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("CorrelationId is required");
    }

    // ========== AC-16 EMPTY SHARED-SECRET CONFIG -> Failure BEFORE HTTP ==========

    [Fact]
    public async Task AC16_EmptySharedSecretConfig_ReturnsFailure_BeforeAnyHttpCall_NeverEmptyHeader()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(WireSuccess(2)) };
        var options = NewOptions();
        options.SidecarSharedSecretVaultName = "";   // <-- deliberately missing
        var client = NewClient(handler, options: options);

        var result = await client.ApplyAsync(BuildRequest(), CancellationToken.None);

        handler.CapturedRequests.Should().BeEmpty(
            "silent-fail-audit: missing shared-secret config must fail BEFORE the wire, never proceed with empty X-Sidecar-Auth");
        var failure = result.Should().BeOfType<ExchangePolicyApplyOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("Sidecar shared secret config missing");
    }

    // ========== AC-17 KV NotFound -> Failure with explicit diagnostic ==========

    [Fact]
    public async Task AC17_SharedSecretKvNotFound_ReturnsFailure_NamingSecretAndVault()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(WireSuccess(2)) };
        var reader = new FakeKvSecretReader { Result = new KvSecretReadResult.NotFound() };
        var client = NewClient(handler, kvReader: reader);

        var result = await client.ApplyAsync(BuildRequest(), CancellationToken.None);

        handler.CapturedRequests.Should().BeEmpty();
        var failure = result.Should().BeOfType<ExchangePolicyApplyOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("not found").And.Contain(SharedSecretName).And.Contain(PlatformKvVault);
    }

    // ========== AC-18 KV Failure -> Failure passing through KV diagnostic ==========

    [Fact]
    public async Task AC18_SharedSecretKvFailure_ReturnsFailure_PassingThroughKvDiagnostic()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(WireSuccess(2)) };
        var reader = new FakeKvSecretReader
        {
            Result = new KvSecretReadResult.Failure("Access denied (403) reading 'X' on vault 'Y'."),
        };
        var client = NewClient(handler, kvReader: reader);

        var result = await client.ApplyAsync(BuildRequest(), CancellationToken.None);

        handler.CapturedRequests.Should().BeEmpty();
        var failure = result.Should().BeOfType<ExchangePolicyApplyOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("Access denied (403)");
    }

    // ========== AC-20 BOOT-TIME PARTIAL-CONFIG-DRIFT CHECK (code-review W1) ==========

    // Validate() is package-internal — reflected here per the same pattern
    // task 160's SecretClientKvReaderTests uses to reach Options.Validate.
    // The 3 shared-secret fields are all-or-none: leave ALL empty for CI/dev
    // (never dispatched) OR bind ALL for production. Partial configuration
    // is a T1 rotation-drift silent-fail trap that boot-time Validate()
    // must catch — otherwise a config rotation half-update would surface
    // only at first ApplyAsync with a confusing runtime diagnostic.

    [Fact]
    public void AC20a_Validate_AllThreeSharedSecretFieldsEmpty_Succeeds_CiDevPosture()
    {
        var options = new IntegrationWiringOptions
        {
            SidecarSharedSecretVaultName = "",
            SidecarSharedSecretSubscriptionId = "",
            SidecarSharedSecretName = "Sidecar-Shared-Secret",  // default
        };
        InvokeValidate(options);   // no throw
    }

    [Fact]
    public void AC20b_Validate_AllThreeSharedSecretFieldsSet_Succeeds_ProductionPosture()
    {
        var options = new IntegrationWiringOptions
        {
            SidecarSharedSecretVaultName = PlatformKvVault,
            SidecarSharedSecretSubscriptionId = PlatformKvSubscription,
            SidecarSharedSecretName = SharedSecretName,
        };
        InvokeValidate(options);   // no throw
    }

    [Fact]
    public void AC20c_Validate_OnlyVaultNameSet_ThrowsWithPartialConfigDiagnostic()
    {
        var options = new IntegrationWiringOptions
        {
            SidecarSharedSecretVaultName = PlatformKvVault,
            SidecarSharedSecretSubscriptionId = "",
            SidecarSharedSecretName = "Sidecar-Shared-Secret",
        };
        var act = () => InvokeValidate(options);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*partial configuration detected*")
            .WithMessage("*T1 silent-fail trap*");
    }

    [Fact]
    public void AC20d_Validate_OnlySubscriptionSet_ThrowsWithPartialConfigDiagnostic()
    {
        var options = new IntegrationWiringOptions
        {
            SidecarSharedSecretVaultName = "",
            SidecarSharedSecretSubscriptionId = PlatformKvSubscription,
            SidecarSharedSecretName = "Sidecar-Shared-Secret",
        };
        var act = () => InvokeValidate(options);
        act.Should().Throw<InvalidOperationException>().WithMessage("*partial configuration detected*");
    }

    [Fact]
    public void AC20e_Validate_OnlyCustomSecretNameSet_ThrowsWithPartialConfigDiagnostic()
    {
        var options = new IntegrationWiringOptions
        {
            SidecarSharedSecretVaultName = "",
            SidecarSharedSecretSubscriptionId = "",
            SidecarSharedSecretName = "Custom-Sidecar-Secret",   // explicitly non-default
        };
        var act = () => InvokeValidate(options);
        act.Should().Throw<InvalidOperationException>().WithMessage("*partial configuration detected*");
    }

    [Fact]
    public void AC20f_Validate_VaultAndSubscriptionSet_DefaultName_Succeeds()
    {
        // The default name "Sidecar-Shared-Secret" is a valid populated name
        // when the other two fields are also set — the "explicitly-set" gate
        // only fires on a NON-default custom name, so this production posture
        // that relies on the default name (still binding vault+sub from
        // platform config) is legitimate + doesn't false-trigger the check.
        var options = new IntegrationWiringOptions
        {
            SidecarSharedSecretVaultName = PlatformKvVault,
            SidecarSharedSecretSubscriptionId = PlatformKvSubscription,
            SidecarSharedSecretName = "Sidecar-Shared-Secret",   // the default
        };
        InvokeValidate(options);   // no throw
    }

    private static void InvokeValidate(IntegrationWiringOptions options)
    {
        var method = typeof(IntegrationWiringOptions).GetMethod("Validate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        try
        {
            method!.Invoke(options, null);
        }
        catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is not null)
        {
            throw tie.InnerException;
        }
    }

    // ========== AC-19 SOURCE-TEXT (criterion #1 verbatim) ==========

    [Fact]
    public void AC19_SourceFile_ContainsNoProcessStartInfo_NoSetExchangePolicyScriptReference()
    {
        // [CallerFilePath] resolves at COMPILE time — robust across
        // bin/obj/net10.0 layout changes. Same pattern as SecretClientKvReaderTests.T7.
        var thisFileDir = Path.GetDirectoryName(ThisFilePath())!;
        var servicesDir = Directory.GetParent(Directory.GetParent(thisFileDir)!.FullName)!.FullName;
        var candidate = Path.Combine(
            servicesDir, "Sprk.Provisioning.ControlPlane.Core", "Handlers", "IntegrationWiring", "ExchangePolicySidecarClient.cs");
        File.Exists(candidate).Should().BeTrue($"expected to find {candidate}");

        var text = File.ReadAllText(candidate);

        text.Should().NotContain("ProcessStartInfo",
            "acceptance criterion #1: grep 'ProcessStartInfo' must return 0 matches in the new production file");
        text.Should().NotContain("Set-ExchangeApplicationAccessPolicy",
            "acceptance criterion #1: grep 'Set-ExchangeApplicationAccessPolicy' must return 0 matches in the new production file");
    }

    // ---------- helpers ----------

    private static ExchangePolicyApplyRequest BuildRequest() => new(
        TenantId, new[] { BffAppRegId, UamiClientId }, PolicyScopeGroupId, DescriptionPrefix, RunId);

    private static IntegrationWiringOptions NewOptions() => new()
    {
        SidecarBaseUrl = "http://127.0.0.1:8091/",
        SidecarRequestTimeout = TimeSpan.FromMinutes(6),
        SidecarTransientRetryDelay = TimeSpan.FromMilliseconds(100),
        SidecarSharedSecretVaultName = PlatformKvVault,
        SidecarSharedSecretSubscriptionId = PlatformKvSubscription,
        SidecarSharedSecretName = SharedSecretName,
    };

    private static ExchangePolicySidecarClient NewClient(
        CapturingHandler handler,
        IntegrationWiringOptions? options = null,
        FakeKvSecretReader? kvReader = null,
        Action<HttpClient>? configureHttpClient = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:8091/"),
        };
        configureHttpClient?.Invoke(httpClient);
        return new ExchangePolicySidecarClient(
            httpClient,
            kvReader ?? new FakeKvSecretReader { Result = new KvSecretReadResult.Success(SharedSecretValue) },
            Options.Create(options ?? NewOptions()),
            NullLogger<ExchangePolicySidecarClient>.Instance);
    }

    private static HttpResponseMessage OkJson(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static string WireSuccess(int createdCount) => $$"""
      {
        "outcome": "Success",
        "createdCount": {{createdCount}},
        "expectedAppIds": ["{{BffAppRegId}}", "{{UamiClientId}}"],
        "observedAppIds": ["{{BffAppRegId}}", "{{UamiClientId}}"],
        "policiesApplied": ["{{BffAppRegId}}", "{{UamiClientId}}"],
        "diagnostic": "Created {{createdCount}} policies."
      }
      """;

    private static string WireAlreadyCompliant() => $$"""
      {
        "outcome": "AlreadyCompliant",
        "createdCount": 0,
        "expectedAppIds": ["{{BffAppRegId}}", "{{UamiClientId}}"],
        "observedAppIds": ["{{BffAppRegId}}", "{{UamiClientId}}"],
        "policiesApplied": [],
        "diagnostic": "Both policies already present, no create needed."
      }
      """;

    private static string WireDrift(string[] expected, string[] observed)
    {
        var expJson = string.Join(",", expected.Select(x => $"\"{x}\""));
        var obsJson = string.Join(",", observed.Select(x => $"\"{x}\""));
        return $$"""
        {
          "outcome": "Drift",
          "createdCount": 0,
          "expectedAppIds": [{{expJson}}],
          "observedAppIds": [{{obsJson}}],
          "policiesApplied": [],
          "diagnostic": "T4 drift detected: observed AppIds do not match expected set."
        }
        """;
    }

    private static string WireFailure(string diagnostic) => $$"""
      {
        "outcome": "Failure",
        "createdCount": 0,
        "expectedAppIds": [],
        "observedAppIds": [],
        "policiesApplied": [],
        "diagnostic": "{{diagnostic}}"
      }
      """;

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    /// <summary>Records every outbound request's headers + body so tests can assert against them.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public required Func<HttpRequestMessage, HttpResponseMessage> ResponseFactory { get; init; }
        public TimeSpan? DelayBeforeResponse { get; init; }
        public List<CapturedRequest> CapturedRequests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in request.Headers)
            {
                headers[h.Key] = string.Join(", ", h.Value);
            }
            string? body = null;
            if (request.Content is not null)
            {
                body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            CapturedRequests.Add(new CapturedRequest(request.RequestUri, request.Method, headers, body));

            if (DelayBeforeResponse is { } d)
            {
                // Cancellation-respecting delay so HttpClient.Timeout's own
                // internal CTS can actually observe cancellation (Thread.Sleep
                // would block the OS thread past the timeout deadline).
                await Task.Delay(d, cancellationToken).ConfigureAwait(false);
            }
            return ResponseFactory(request);
        }
    }

    private sealed record CapturedRequest(
        Uri? RequestUri,
        HttpMethod Method,
        IReadOnlyDictionary<string, string> Headers,
        string? BodyJson);

    /// <summary>Test-only IKvSecretReader returning a canned result.</summary>
    private sealed class FakeKvSecretReader : IKvSecretReader
    {
        public required KvSecretReadResult Result { get; init; }
        public int CallCount { get; private set; }
        public string? LastVaultName { get; private set; }
        public string? LastSecretName { get; private set; }

        public Task<KvSecretReadResult> ReadSecretAsync(string vaultName, string subscriptionId, string secretName, CancellationToken cancellationToken)
        {
            CallCount++;
            LastVaultName = vaultName;
            LastSecretName = secretName;
            return Task.FromResult(Result);
        }
    }
}
