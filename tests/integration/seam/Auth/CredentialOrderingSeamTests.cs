using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Identity.Client;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.DI;
using Xunit;

// Microsoft.Identity.Client also defines a LogLevel (MSAL's own diagnostic level). This file means the
// logging-abstractions one throughout.
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Sprk.Bff.Api.Tests.Seam.Auth;

/// <summary>
/// FR-B2 (auth-v4 task 021) — ordered credential selection, the mechanism that makes rollback a
/// configuration edit instead of a redeploy (NFR-06).
///
/// <para><b>Why these assertions are behavioural rather than structural.</b> The two properties worth
/// protecting here are both observable without touching private state: <i>which credential won</i>
/// (<c>SelectedKindFor</c>) and <i>how many clients were built</i> (<c>BuildCountFor</c>). Both are
/// deliberate, non-contractual observability surface on the provider, added for exactly this reason —
/// reflection would be ADR-038 ban <b>B8</b> and resolving from a container would be ban <b>B3</b>, and
/// this project has already hit both walls (tasks 011 and 020).</para>
///
/// <para><b>The MI-FIC branch is driven by a stub, not by a live identity, and that is the point.</b>
/// A real mint cannot be made deterministic across hosts: a developer workstation cannot route to IMDS
/// at all, whereas GitHub-hosted runners are Azure VMs where IMDS <i>is</i> reachable but carries no
/// matching identity — which is the fail-loud case. Pinning either would make this suite pass in one
/// environment and fail in the other. The stub lets each row of the fall-through table be exercised
/// exactly once, deterministically, which is the whole reason the decision was consolidated into a
/// single predicate.</para>
///
/// <para><b>Not covered here, stated rather than implied:</b> the Key Vault certificate branch is
/// exercised only as far as configuration validation. Loading a real PFX needs a live vault, and a
/// faked <c>SecretClient</c> would assert the Azure SDK's behaviour rather than ours. The extracted
/// loader is behaviour-preserving by construction (it is <c>CiamGraphClientFactory</c>'s own method
/// body, moved), and the certificate path is not on any live deployment — ADR-028 A4 records that the
/// certificate alternative was explicitly not taken.</para>
/// </summary>
public class CredentialOrderingSeamTests
{
    private const string Tenant = "a221a95e-6abc-4434-aecc-e48338a1b2f2";
    private const string AppId = "1e40baad-e065-4aea-a8d4-4b7ab273458c";
    private const string Uami = "5967251e-171c-46fe-a6c2-ef843c90309d";
    private const string Secret = "transitional-secret-value";

    // ---------------------------------------------------------------------------------------------
    // Criterion 1 — reordering the configured list changes the selected credential, no code change.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetClientAsync_WithManagedIdentityFirst_SelectsManagedIdentityFederated()
    {
        var provider = Build(
            order: new[] { CredentialKind.ManagedIdentityFederated, CredentialKind.ClientSecret },
            assertion: StubAssertion.Working());

        await provider.GetClientAsync(Tenant, AppId);

        provider.SelectedKindFor(Tenant, AppId)
            .Should().Be(CredentialKind.ManagedIdentityFederated,
                "the first credential in the configured order that can be obtained must win");
    }

    [Fact]
    public async Task GetClientAsync_WhenOrderIsReversed_SelectsClientSecret_WithNoCodeChange()
    {
        // THE rollback. Identical code, identical environment, identical working MI-FIC — the only
        // difference from the test above is the configured order. If this ever stops holding, design §6's
        // "rollback is a credential reorder" is false and every phase of this project loses its exit.
        var provider = Build(
            order: new[] { CredentialKind.ClientSecret, CredentialKind.ManagedIdentityFederated },
            assertion: StubAssertion.Working());

        await provider.GetClientAsync(Tenant, AppId);

        provider.SelectedKindFor(Tenant, AppId)
            .Should().Be(CredentialKind.ClientSecret,
                "reordering the configured list is what makes rollback a configuration edit (NFR-06)");
    }

    // ---------------------------------------------------------------------------------------------
    // Criteria 4 + 6 — fall-through is NOT uniform. This table IS the FR-B4 protection.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    // Eligible: no managed identity in this environment at all — the ordinary local-dev shape.
    [InlineData(MsalError.ManagedIdentityUnreachableNetwork, true)]
    [InlineData(MsalError.ManagedIdentityAllSourcesUnavailable, true)]
    // NOT eligible: IMDS answered and the identity was absent or wrong. This is the FR-B4 signature —
    // five UAMIs exist in the dev subscription and one is named like the BFF's without being attached
    // to it. Falling through here would run production on the transitional secret while every health
    // signal stayed green.
    [InlineData(MsalError.ManagedIdentityRequestFailed, false)]
    // NOT eligible: a deployment-shape error, not an environment one.
    [InlineData(MsalError.UserAssignedManagedIdentityNotSupported, false)]
    public async Task GetClientAsync_WhenManagedIdentityFails_FallsThroughOnlyForEnvironmentErrors(
        string errorCode,
        bool expectFallThrough)
    {
        var provider = Build(
            order: new[] { CredentialKind.ManagedIdentityFederated, CredentialKind.ClientSecret },
            assertion: StubAssertion.Failing(errorCode));

        if (expectFallThrough)
        {
            await provider.GetClientAsync(Tenant, AppId);

            provider.SelectedKindFor(Tenant, AppId)
                .Should().Be(CredentialKind.ClientSecret,
                    "{0} means no managed identity exists in this environment, which is exactly what " +
                    "ordered selection is for", errorCode);
        }
        else
        {
            var act = async () => await provider.GetClientAsync(Tenant, AppId);

            (await act.Should().ThrowAsync<MsalServiceException>(
                    "{0} means the identity is misconfigured, not absent — silently downgrading to the " +
                    "secret would hide a broken deployment behind a healthy-looking service", errorCode))
                .Which.ErrorCode.Should().Be(errorCode);

            provider.SelectedKindFor(Tenant, AppId)
                .Should().BeNull("no credential may be selected when the preferred one is misconfigured");
        }
    }

    [Fact]
    public async Task GetClientAsync_WhenTheHostHasNoImdsAtAll_FallsThroughEvenThoughMsalThrowsAClientException()
    {
        // REGRESSION. The theory above drives the stub with MsalServiceException, and that assumption
        // hid a real defect: MSAL throws MsalClientException — a DIFFERENT branch of the hierarchy — for
        // managed_identity_all_sources_unavailable, which is the "no IMDS on this host" case. That is
        // the commonest fall-through of all and the entire reason ordered selection exists, and the
        // original predicate and catch clause were both typed to MsalServiceException, so it never
        // matched. Every developer workstation would have received a failed request instead of a
        // fall-through to the secret.
        //
        // Found on 2026-08-21 from a real MSAL stack trace surfaced by an intermittent failure in
        // ClientAssertionProviderSeamTests, not by reasoning about types. This test pins the shape MSAL
        // actually throws so the narrowing cannot come back.
        var provider = Build(
            order: new[] { CredentialKind.ManagedIdentityFederated, CredentialKind.ClientSecret },
            assertion: StubAssertion.FailingWithClientException(MsalError.ManagedIdentityAllSourcesUnavailable));

        await provider.GetClientAsync(Tenant, AppId);

        provider.SelectedKindFor(Tenant, AppId)
            .Should().Be(CredentialKind.ClientSecret,
                "a host with no IMDS must fall through — this is local development, not a fault");
    }

    [Fact]
    public void IsFallThroughEligible_AcceptsBothMsalExceptionBranches_ForTheSameErrorCode()
    {
        // The same error code must decide the same way regardless of which branch of MSAL's exception
        // hierarchy carries it. Meaning belongs to the code; the type only records where the failure
        // happened.
        OrderedCredentialClientProvider.IsFallThroughEligible(
            new MsalClientException(MsalError.ManagedIdentityAllSourcesUnavailable, "no IMDS"))
            .Should().BeTrue();

        OrderedCredentialClientProvider.IsFallThroughEligible(
            new MsalServiceException(MsalError.ManagedIdentityAllSourcesUnavailable, "no IMDS"))
            .Should().BeTrue();

        // And the fail-loud code stays fail-loud on both branches.
        OrderedCredentialClientProvider.IsFallThroughEligible(
            new MsalClientException(MsalError.ManagedIdentityRequestFailed, "wrong identity"))
            .Should().BeFalse();

        OrderedCredentialClientProvider.IsFallThroughEligible(
            new MsalServiceException(MsalError.ManagedIdentityRequestFailed, "wrong identity"))
            .Should().BeFalse();
    }

    // ---------------------------------------------------------------------------------------------
    // Criterion 2 — every fall-through is traceable.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetClientAsync_WhenFallingThrough_WarnsNamingTheSkippedCredentialAndTheReason()
    {
        var logger = new RecordingLogger<OrderedCredentialClientProvider>();
        var provider = Build(
            order: new[] { CredentialKind.ManagedIdentityFederated, CredentialKind.ClientSecret },
            assertion: StubAssertion.Failing(MsalError.ManagedIdentityUnreachableNetwork),
            logger: logger);

        await provider.GetClientAsync(Tenant, AppId);

        var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToList();

        warnings.Should().Contain(m =>
                m.Contains(nameof(CredentialKind.ManagedIdentityFederated)) &&
                m.Contains(MsalError.ManagedIdentityUnreachableNetwork),
            "a credential that was skipped must be named alongside why — a silent skip is how a process " +
            "ends up on the fallback credential with nothing in the logs to say so");

        warnings.Should().Contain(m => m.Contains(nameof(CredentialKind.ClientSecret)),
            "the credential that actually won after a fall-through is the one an operator needs to see");
    }

    // ---------------------------------------------------------------------------------------------
    // Criterion 7 — ONE cache. Task 022 collapses three per-class caches onto it; without it,
    // task 011's time-boxed A4 exception becomes permanent.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetClientAsync_CalledRepeatedly_BuildsExactlyOneClientAndReturnsIt()
    {
        var provider = Build(
            order: new[] { CredentialKind.ManagedIdentityFederated },
            assertion: StubAssertion.Working());

        var first = await provider.GetClientAsync(Tenant, AppId);
        var second = await provider.GetClientAsync(Tenant, AppId);
        var third = await provider.GetClientAsync(Tenant, AppId);

        second.Should().BeSameAs(first);
        third.Should().BeSameAs(first);

        provider.BuildCountFor(Tenant, AppId, CredentialKind.ManagedIdentityFederated)
            .Should().Be(1,
                "MSAL's OBO token cache lives ON the confidential client, so rebuilding it discards " +
                "every cached user token and forces a network exchange per request");
    }

    [Fact]
    public async Task GetClientAsync_WhenTheSecretIsRotated_BuildsAFreshClientRatherThanReusingTheStaleOne()
    {
        // Task 011 finding W-1, preserved deliberately rather than rediscovered. MSAL binds the
        // credential at Build() and holds it for the client's lifetime, so a key that ignores the
        // credential material hands back a client built with the OLD secret after a rotation —
        // presenting as AADSTS7000215 on OBO while app-only keeps working, and "fixed" by a restart
        // nobody can explain.
        var config = new MutableConfiguration
        {
            ["API_CLIENT_SECRET"] = Secret,
        };
        var provider = Build(order: new[] { CredentialKind.ClientSecret }, configuration: config);

        var beforeRotation = await provider.GetClientAsync(Tenant, AppId);

        config["API_CLIENT_SECRET"] = "rotated-secret-value";
        var afterRotation = await provider.GetClientAsync(Tenant, AppId);

        afterRotation.Should().NotBeSameAs(beforeRotation,
            "the cache key fingerprints the credential material, so rotating it must miss the cache");
    }

    // ---------------------------------------------------------------------------------------------
    // The negative cache, and the TTL bound task 030 measured.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetClientAsync_AfterASingleFailure_RetriesThePreferredCredentialOnTheNextCall()
    {
        // A single failure must NOT demote. Entra flaps for ~2 minutes after a federated credential is
        // created or changed — successes and failures interleaved as replicas converge (task 030 §11).
        // Treating one failure as proof would pin the process to the fallback secret on nothing more
        // than a replica that had not caught up yet.
        var assertion = StubAssertion.Failing(MsalError.ManagedIdentityUnreachableNetwork);
        var provider = Build(
            order: new[] { CredentialKind.ManagedIdentityFederated, CredentialKind.ClientSecret },
            assertion: assertion);

        await provider.GetClientAsync(Tenant, AppId);
        provider.SelectedKindFor(Tenant, AppId).Should().Be(CredentialKind.ClientSecret);

        assertion.Recover();
        await provider.GetClientAsync(Tenant, AppId);

        provider.SelectedKindFor(Tenant, AppId)
            .Should().Be(CredentialKind.ManagedIdentityFederated,
                "one failure is below the suppression threshold, so the preferred credential is retried " +
                "immediately rather than written off");
    }

    [Fact]
    public async Task GetClientAsync_AfterConsecutiveFailures_SuppressesBriefly_ThenRecoversToThePreferredCredential()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-21T12:00:00Z"));
        var assertion = StubAssertion.Failing(MsalError.ManagedIdentityUnreachableNetwork);
        var provider = Build(
            order: new[] { CredentialKind.ManagedIdentityFederated, CredentialKind.ClientSecret },
            assertion: assertion,
            time: time,
            negativeCacheSeconds: 10);

        // Two consecutive failures reach the suppression threshold.
        await provider.GetClientAsync(Tenant, AppId);
        await provider.GetClientAsync(Tenant, AppId);
        provider.SelectedKindFor(Tenant, AppId).Should().Be(CredentialKind.ClientSecret);

        // MI-FIC is healthy again, but is still inside its suppression window.
        assertion.Recover();
        time.Advance(TimeSpan.FromSeconds(5));
        await provider.GetClientAsync(Tenant, AppId);

        provider.SelectedKindFor(Tenant, AppId)
            .Should().Be(CredentialKind.ClientSecret,
                "suppression is what stops a failing credential being retried on every single request " +
                "(~80 ms each, measured at task 020)");

        // Past the window, the preferred credential is reconsidered WITHOUT a restart. This is the
        // property that keeps a transient flap from becoming a permanent silent downgrade to the secret.
        time.Advance(TimeSpan.FromSeconds(6));
        await provider.GetClientAsync(Tenant, AppId);

        provider.SelectedKindFor(Tenant, AppId)
            .Should().Be(CredentialKind.ManagedIdentityFederated,
                "recovery to the secret-free credential must be automatic and bounded in seconds");
    }

    // ---------------------------------------------------------------------------------------------
    // Criterion 3 — configuration failures are fatal and actionable.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Validate_WithAnEmptyOrder_FailsWithAMessageNamingTheKeyAndTheValidValues()
    {
        var result = new CredentialSelectionOptionsValidator()
            .Validate(null, new CredentialSelectionOptions { Order = new List<string>() });

        result.Failed.Should().BeTrue(
            "an operator who blanked the list did so to force a decision — choosing a credential for " +
            "them silently is the one response that cannot be right");
        result.FailureMessage.Should().Contain("Graph:Credentials:Order")
            .And.Contain(nameof(CredentialKind.ManagedIdentityFederated));
    }

    [Fact]
    public async Task Startup_WithAnInvalidCredentialOrder_FailsFastInsteadOfBootingOnAFallback()
    {
        // The criterion says "fails fast AT STARTUP", and the validator passing in isolation does not
        // establish that. What is actually being asserted here is the WIRING — that the validator is
        // reached by ValidateOnStart through the real registration path. A misconfigured credential
        // order that let the host boot would be the worst outcome available: the BFF would come up
        // healthy and authenticate as something nobody chose.
        using var host = new HostBuilder()
            .ConfigureServices(services => services.AddCredentialSelection(
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // A near-miss spelling — the realistic operator error, and the one a silent
                    // fall-through to the default order would hide completely.
                    ["Graph:Credentials:Order:0"] = "ManagedIdentity",
                }).Build()))
            .Build();

        var act = async () => await host.StartAsync();

        (await act.Should().ThrowAsync<OptionsValidationException>())
            .Which.Message.Should().Contain(nameof(CredentialKind.ManagedIdentityFederated),
                "the startup failure has to name the valid spellings, or the operator is left guessing");
    }

    [Fact]
    public async Task Startup_WithNoCredentialSectionAtAll_BootsOnTheCanonicalOrder()
    {
        // The bound on the fail-fast above (FAILURE-MODES AP-7). Every environment and test fixture in
        // this repo has no Graph:Credentials section — there is no appsettings.json in the BFF at all —
        // so if an absent section did not boot, this change would take down every one of them. Task 010
        // already shipped that exact regression once in this project.
        using var host = new HostBuilder()
            .ConfigureServices(services => services.AddCredentialSelection(
                new ConfigurationBuilder().Build()))
            .Build();

        var act = async () => await host.StartAsync();

        await act.Should().NotThrowAsync();

        host.Services.GetRequiredService<IOptions<CredentialSelectionOptions>>().Value.Order
            .Should().Equal(
                nameof(CredentialKind.ManagedIdentityFederated),
                nameof(CredentialKind.ClientSecret));

        await host.StopAsync();
    }

    [Fact]
    public void Validate_WithAnUnknownCredentialKind_FailsAndListsTheValidValues()
    {
        var result = new CredentialSelectionOptionsValidator()
            .Validate(null, new CredentialSelectionOptions { Order = new List<string> { "ManagedIdentity" } });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ManagedIdentity")
            .And.Contain(nameof(CredentialKind.ManagedIdentityFederated),
                "a near-miss name is the likeliest error, so the message has to show the exact spelling");
    }

    [Fact]
    public void Validate_WithCertificateOrderedButUnnamed_Fails()
    {
        var result = new CredentialSelectionOptionsValidator()
            .Validate(null, new CredentialSelectionOptions
            {
                Order = new List<string> { nameof(CredentialKind.KeyVaultCertificate) },
            });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("KeyVaultCertificateName");
    }

    [Fact]
    public void Validate_WithSuppressionAfterASingleFailure_Fails()
    {
        var result = new CredentialSelectionOptionsValidator()
            .Validate(null, new CredentialSelectionOptions
            {
                Order = new List<string> { nameof(CredentialKind.ManagedIdentityFederated) },
                FailuresBeforeSuppression = 1,
            });

        result.Failed.Should().BeTrue(
            "suppressing after one failure would demote to the fallback credential on a single " +
            "transient error inside Entra's post-change propagation window");
    }

    // ---------------------------------------------------------------------------------------------
    // Criterion 8 — a secret above a secret-free credential is an ADR-028 A4 deviation.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Constructor_WhenTheSecretIsOrderedAboveASecretFreeCredential_LogsAnA4DeviationAtError()
    {
        // Permitted rather than rejected, and the reason is not leniency: this ordering IS the rollback
        // (NFR-06). Refusing to start would disable the emergency exit at the one moment it is needed —
        // on the OBO path, which fails closed for every user at once. So it is allowed and made loud.
        var logger = new RecordingLogger<OrderedCredentialClientProvider>();

        Build(
            order: new[] { CredentialKind.ClientSecret, CredentialKind.ManagedIdentityFederated },
            assertion: StubAssertion.Working(),
            logger: logger);

        logger.Entries.Should().Contain(
            e => e.Level == LogLevel.Error && e.Message.Contains("A4") && e.Message.Contains("DEVIATION"),
            "a temporary rollback that becomes the permanent state is exactly how the secret survived " +
            "three prior audits");
    }

    [Fact]
    public void Constructor_WithTheCanonicalOrder_DoesNotLogAnA4Deviation()
    {
        // Negative control. Without it the assertion above would also pass on a provider that logged the
        // deviation unconditionally.
        var logger = new RecordingLogger<OrderedCredentialClientProvider>();

        Build(
            order: new[] { CredentialKind.ManagedIdentityFederated, CredentialKind.ClientSecret },
            assertion: StubAssertion.Working(),
            logger: logger);

        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
    }

    // ---------------------------------------------------------------------------------------------
    // Exhaustion is fail-closed.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetClientAsync_WhenNoConfiguredCredentialIsAvailable_ThrowsNamingEveryAttempt()
    {
        var provider = Build(
            order: new[] { CredentialKind.ManagedIdentityFederated, CredentialKind.ClientSecret },
            assertion: StubAssertion.Failing(MsalError.ManagedIdentityUnreachableNetwork),
            configuration: new MutableConfiguration());   // no secret configured either

        var act = async () => await provider.GetClientAsync(Tenant, AppId);

        (await act.Should().ThrowAsync<InvalidOperationException>(
                "an OBO path with no credential authenticates nobody — degrading quietly would produce " +
                "a service that looks healthy and authorises no one (NFR-03)"))
            .Which.Message.Should()
                .Contain(nameof(CredentialKind.ManagedIdentityFederated))
                .And.Contain(nameof(CredentialKind.ClientSecret));
    }

    // ---------------------------------------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------------------------------------

    private static OrderedCredentialClientProvider Build(
        IEnumerable<CredentialKind> order,
        IClientAssertionProvider? assertion = null,
        IConfiguration? configuration = null,
        ILogger<OrderedCredentialClientProvider>? logger = null,
        TimeProvider? time = null,
        int negativeCacheSeconds = 10)
    {
        var options = Options.Create(new CredentialSelectionOptions
        {
            Order = order.Select(k => k.ToString()).ToList(),
            NegativeCacheSeconds = negativeCacheSeconds,
        });

        return new OrderedCredentialClientProvider(
            options,
            configuration ?? new MutableConfiguration
            {
                ["API_CLIENT_SECRET"] = Secret,
                ["Graph:ManagedIdentity:ClientId"] = Uami,
            },
            logger ?? new RecordingLogger<OrderedCredentialClientProvider>(),
            assertion,
            secretClient: null,
            timeProvider: time);
    }

    /// <summary>
    /// A controllable <see cref="IClientAssertionProvider"/>. The real one cannot be made deterministic
    /// across hosts — see the class remarks — and every row of the fall-through table needs to be
    /// exercised exactly once.
    /// </summary>
    private sealed class StubAssertion : IClientAssertionProvider
    {
        private string? _errorCode;
        private readonly bool _asClientException;

        private StubAssertion(string? errorCode, bool asClientException = false)
        {
            _errorCode = errorCode;
            _asClientException = asClientException;
        }

        public static StubAssertion Working() => new(null);

        public static StubAssertion Failing(string errorCode) => new(errorCode);

        /// <summary>
        /// Fails as <see cref="MsalClientException"/> — the branch MSAL actually uses for
        /// <c>managed_identity_all_sources_unavailable</c>. Assuming the service branch is what hid a
        /// real fall-through defect until 2026-08-21.
        /// </summary>
        public static StubAssertion FailingWithClientException(string errorCode) => new(errorCode, true);

        public void Recover() => _errorCode = null;

        public Task<string> GetAssertionAsync(CancellationToken cancellationToken = default)
        {
            if (_errorCode is null)
            {
                return Task.FromResult("stub.signed.assertion");
            }

            var message = $"stubbed managed-identity failure: {_errorCode}";
            throw _asClientException
                ? new MsalClientException(_errorCode, message)
                : new MsalServiceException(_errorCode, message);
        }
    }

    /// <summary>
    /// Minimal mutable <see cref="IConfiguration"/> — the secret-rotation test needs a value to change
    /// between calls, which an immutable in-memory provider cannot express without rebuilding the whole
    /// configuration root (and therefore the provider under test, which would defeat the test).
    /// </summary>
    private sealed class MutableConfiguration : IConfiguration
    {
        private readonly ConcurrentDictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

        public string? this[string key]
        {
            get => _values.TryGetValue(key, out var v) ? v : null;
            set => _values[key] = value;
        }

        public IEnumerable<IConfigurationSection> GetChildren() => Array.Empty<IConfigurationSection>();

        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken()
            => new Microsoft.Extensions.Primitives.CancellationChangeToken(CancellationToken.None);

        public IConfigurationSection GetSection(string key)
            => new ConfigurationRoot(new List<IConfigurationProvider>()).GetSection(key);
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }
}
