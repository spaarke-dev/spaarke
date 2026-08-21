using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Xunit;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Sprk.Bff.Api.Tests.Seam.Auth;

/// <summary>
/// FR-B4 (auth-v4 task 023) — the UAMI / app-registration conflation guard.
///
/// <para><b>Why this failure mode gets its own test file.</b> MI-FIC requires holding two identities
/// simultaneously: the <b>user-assigned managed identity</b> mints the assertion, and the <b>app
/// registration</b> is what that assertion authenticates as. Swap them and everything looks right —
/// the credential is created cleanly, the deployment succeeds, health checks pass — and the only symptom
/// is a token exchange that fails later, on the OBO path, for every user at once. The dev subscription
/// sharpens it further: five UAMIs exist and one is named <c>spaarke-bff-identity</c> as though it were
/// the BFF's, without being attached to it.</para>
///
/// <para><b>The live environment already contains the trap.</b> Verified against <c>spaarke-bff-dev</c>
/// on 2026-08-21: <c>AZURE_CLIENT_ID</c> holds the <i>UAMI's</i> clientId while <c>API_APP_ID</c> holds
/// the app registration's, and <c>GraphClientFactory.cs:54</c> reads <c>AZURE_CLIENT_ID ?? API_APP_ID</c>
/// as the app-only clientId. It is inert only because managed identity is enabled, which makes that
/// branch dead code. The tests below pin both halves of that: fatal when it would fire, tolerated and
/// reported when it would not.</para>
/// </summary>
public class IdentityConflationSeamTests
{
    private const string Uami = "5967251e-171c-46fe-a6c2-ef843c90309d";
    private const string AppRegistration = "1e40baad-e065-4aea-a8d4-4b7ab273458c";
    private const string Tenant = "a221a95e-6abc-4434-aecc-e48338a1b2f2";

    // ---------------------------------------------------------------------------------------------
    // Criterion 3 — the two identities set to the same value fails fast.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Validate_WhenTheAssertionIdentityEqualsTheAppRegistration_FailsFastAndExplainsTheDifference()
    {
        var result = Validate(
            ("Graph:ManagedIdentity:ClientId", AppRegistration),   // the conflation: app-reg id in the UAMI slot
            ("API_APP_ID", AppRegistration));

        result.Failed.Should().BeTrue(
            "an app registration cannot mint its own managed-identity assertion, so this configuration " +
            "could never work — it can only fail later, at token exchange");

        result.FailureMessage.Should().Contain("MINTS")
            .And.Contain("Graph:ManagedIdentity:ClientId",
                "the message has to say which key to change; naming the problem without the remedy is " +
                "what makes an error message useless at 3am");
    }

    [Fact]
    public void Validate_WithTwoDistinctIdentities_Succeeds()
    {
        // Negative control. Without it, the assertion above would also pass against a validator that
        // rejected every configuration.
        Validate(
            ("Graph:ManagedIdentity:ClientId", Uami),
            ("API_APP_ID", AppRegistration))
            .Succeeded.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------------------------
    // The live AZURE_CLIENT_ID trap — fatal exactly when it fires, reported when it does not.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Validate_WhenAzureClientIdIsTheManagedIdentityAndManagedIdentityIsDisabled_FailsFast()
    {
        // This is the moment the live trap springs: with the flag off, GraphClientFactory resolves the
        // app-only clientId as AZURE_CLIENT_ID ?? API_APP_ID and builds a ClientSecretCredential from a
        // MANAGED IDENTITY paired with the app registration's secret. The resulting AADSTS error names
        // neither identity, which is why this belongs at startup instead.
        var result = Validate(
            ("Graph:ManagedIdentity:ClientId", Uami),
            ("API_APP_ID", AppRegistration),
            ("AZURE_CLIENT_ID", Uami),
            ("Graph:ManagedIdentity:Enabled", "false"));

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("AZURE_CLIENT_ID")
            .And.Contain("ClientSecretCredential");
    }

    [Fact]
    public void Validate_WhenAzureClientIdIsTheManagedIdentityButManagedIdentityIsEnabled_StillBootsAndReportsTheHazard()
    {
        // THE LIVE spaarke-bff-dev SHAPE. It must keep booting — failing here would take dev down to fix
        // a defect that is not firing, which is precisely the #3b failure mode. So the guard reports
        // instead, at error level, naming both identities.
        var logger = new CapturingLogger<IdentityConfigurationValidator>();

        var result = Validate(
            logger,
            ("Graph:ManagedIdentity:ClientId", Uami),
            ("API_APP_ID", AppRegistration),
            ("AZURE_CLIENT_ID", Uami),
            ("Graph:ManagedIdentity:Enabled", "true"));

        result.Succeeded.Should().BeTrue("the live dev environment is in this exact state and must boot");

        logger.Entries.Should().Contain(
            e => e.Level == LogLevel.Error && e.Message.Contains("CONFLATION") && e.Message.Contains(Uami),
            "an inert trap that nobody is told about is just a trap with a longer fuse");
    }

    // ---------------------------------------------------------------------------------------------
    // Criterion 5 — MI-FIC with no identity fails fast, but ONLY where there is nothing to fall through to.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Validate_WhenManagedIdentityIsTheOnlyCredentialAndNoIdentityIsSet_FailsFast()
    {
        // The end state this project is heading for: once task 033 removes ClientSecret from the order,
        // this rule becomes strict automatically. With nothing beneath it, an absent identity can only
        // surface at the first token exchange — on the OBO path, for every user simultaneously.
        var result = Validate(
            order: new[] { CredentialKind.ManagedIdentityFederated },
            settings: ("API_APP_ID", AppRegistration));

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("only configured credential")
            .And.Contain("Graph:ManagedIdentity:ClientId");
    }

    [Fact]
    public void Validate_WhenManagedIdentityHasAFallbackAndNoIdentityIsSet_Succeeds()
    {
        // The bound on the rule above, and it is load-bearing rather than lenient: NO developer
        // workstation and NO test fixture in this repo sets Graph:ManagedIdentity:ClientId. With a
        // fallback configured, an absent managed identity is a DESIGNED fall-through (task 021), not an
        // error. Making it fatal would break every one of them to guard a case that is not the hazard.
        Validate(
            order: new[] { CredentialKind.ManagedIdentityFederated, CredentialKind.ClientSecret },
            settings: ("API_APP_ID", AppRegistration))
            .Succeeded.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------------------------
    // Task 024 / FR-B5 — relaxing the three [Required] secrets is safe because THIS backstops them.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Validate_WhenNoConfiguredCredentialCanBeObtained_FailsFastNamingWhatIsMissing()
    {
        // Task 024 removed three [Required] attributes that mandated a SECRET. This asserts the weaker,
        // correct replacement: that SOME credential is configured — checked at startup rather than
        // discovered at the first token exchange, which on the OBO path means every user at once.
        var result = Validate(
            order: new[] { CredentialKind.ClientSecret },
            settings: ("API_APP_ID", AppRegistration));   // no secret anywhere

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("API_CLIENT_SECRET")
            .And.Contain("fail closed",
                "the message has to say what breaks, not just which key is unset");
    }

    [Fact]
    public void Validate_WithNoSecretButManagedIdentityInTheOrder_Succeeds()
    {
        // THE POINT OF FR-B5, and the negative control for the test above: with a secret-free credential
        // configured, the absence of a secret is no longer a startup failure. Before task 024 this
        // configuration could not boot — which is what made a secret-free deployment impossible.
        Validate(
            order: new[] { CredentialKind.ManagedIdentityFederated, CredentialKind.ClientSecret },
            settings: new (string, string?)[] { ("Graph:ManagedIdentity:ClientId", Uami), ("API_APP_ID", AppRegistration) })
            .Succeeded.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------------------------
    // Criterion 2 — the assertion is minted for the UAMI while the client targets the app registration.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetClientAsync_MintsTheAssertionForTheUami_ButBuildsTheClientForTheAppRegistration()
    {
        // The one assertion that actually proves the two identities stay separate end to end, rather
        // than proving that configuration keys have different names. The stub records that it was asked
        // to mint (the UAMI's job); MSAL's own AppConfig reports who the built client authenticates as.
        var assertion = new RecordingAssertionProvider();

        var provider = new OrderedCredentialClientProvider(
            Options.Create(new CredentialSelectionOptions
            {
                Order = new List<string> { nameof(CredentialKind.ManagedIdentityFederated) },
            }),
            Config(
                ("Graph:ManagedIdentity:ClientId", Uami),
                ("API_APP_ID", AppRegistration)),
            NullLogger<OrderedCredentialClientProvider>.Instance,
            assertion);

        var client = await provider.GetClientAsync(Tenant, AppRegistration);

        assertion.MintCount.Should().BeGreaterThan(0,
            "the assertion is minted BY the managed identity — that is the half the UAMI performs");

        client.AppConfig.ClientId.Should().Be(AppRegistration,
            "the client authenticates AS the app registration — the UAMI's clientId must never end up here");

        client.AppConfig.ClientId.Should().NotBe(Uami,
            "stated as its own assertion because this is the silent failure: a client built on the " +
            "UAMI's clientId is created cleanly and fails only at token exchange");
    }

    // ---------------------------------------------------------------------------------------------
    // Criterion 4 — the runtime resolves identities by configured id, never by name.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ResolveUamiClientId_ReturnsNullWhenUnset_RatherThanFallingBackToAnyOtherValue()
    {
        // The decoy in the dev subscription is spaarke-bff-identity — named as though it were the BFF's,
        // not attached to it. Name-based resolution would pick it. The runtime cannot: it resolves only
        // from a configured clientId, and when none is set it returns null rather than searching.
        //
        // The complementary half is structural — no name-based managed-identity lookup exists anywhere
        // in src/ (verified by grep at task 023; the only occurrence of the decoy name in the codebase
        // is a doc comment warning about it). That guard is source analysis and belongs in the task 060
        // ArchTests, where this project's other structural guards live.
        ManagedIdentityCredentialFactory.ResolveUamiClientId(Config())
            .Should().BeNull("an unset identity must resolve to nothing, never to a discovered one");

        ManagedIdentityCredentialFactory.ResolveUamiClientId(Config(("Graph:ManagedIdentity:ClientId", Uami)))
            .Should().Be(Uami, "and when set, to exactly the configured id");
    }

    // ---------------------------------------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------------------------------------

    private static IConfiguration Config(params (string Key, string? Value)[] settings)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (k, v) in settings) dict[k] = v;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static ValidateOptionsResult Validate(params (string Key, string? Value)[] settings)
        => Validate(new CapturingLogger<IdentityConfigurationValidator>(), settings);

    private static ValidateOptionsResult Validate(
        ILogger<IdentityConfigurationValidator> logger,
        params (string Key, string? Value)[] settings)
        => Validate(
            new[] { CredentialKind.ManagedIdentityFederated, CredentialKind.ClientSecret },
            logger,
            settings);

    private static ValidateOptionsResult Validate(
        IEnumerable<CredentialKind> order,
        params (string Key, string? Value)[] settings)
        => Validate(order, new CapturingLogger<IdentityConfigurationValidator>(), settings);

    private static ValidateOptionsResult Validate(
        IEnumerable<CredentialKind> order,
        ILogger<IdentityConfigurationValidator> logger,
        params (string Key, string? Value)[] settings)
        => new IdentityConfigurationValidator(Config(settings), logger)
            .Validate(null, new CredentialSelectionOptions
            {
                Order = order.Select(k => k.ToString()).ToList(),
            });

    private sealed class RecordingAssertionProvider : IClientAssertionProvider
    {
        private int _mintCount;

        public int MintCount => _mintCount;

        public Task<string> GetAssertionAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _mintCount);
            return Task.FromResult("stub.signed.assertion");
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLogger<T> : ILogger<T>
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
