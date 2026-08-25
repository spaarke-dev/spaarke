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
    public void Validate_WhenAzureClientIdIsTheManagedIdentityAndManagedIdentityIsDisabled_ReportsButNoLongerFailsFast()
    {
        // AMENDED at task 022 — the trap was REMOVED rather than guarded, so the guard stopped being
        // fatal.
        //
        // Task 023 could only fail fast here: GraphClientFactory resolved the app-only clientId as
        // AZURE_CLIENT_ID ?? API_APP_ID, so with the flag off it would build a client credential from a
        // MANAGED IDENTITY paired with the app registration's secret, and the resulting AADSTS error
        // named neither identity. Changing that fallback was out of task 023's scope. Task 022 owns the
        // app-only branch and deleted the fallback: AZURE_CLIENT_ID now has ZERO consumers in src/, so
        // no code path can conflate the two identities regardless of the flag.
        //
        // The rule is kept because the SETTING is still wrong — it signals that someone believed this
        // key meant the app registration — but failing startup over a setting nothing reads would be a
        // false positive, and this project's own AP-7 rule forbids turning an inert condition into an
        // outage. Task 031 clears it.
        var logger = new CapturingLogger<IdentityConfigurationValidator>();

        var result = Validate(
            logger,
            ("Graph:ManagedIdentity:ClientId", Uami),
            ("API_APP_ID", AppRegistration),
            ("AZURE_CLIENT_ID", Uami),
            ("Graph:ManagedIdentity:Enabled", "false"));

        result.Succeeded.Should().BeTrue(
            "since task 022 nothing reads AZURE_CLIENT_ID, so this configuration is inert rather than fatal");

        logger.Entries.Should().Contain(
            e => e.Level == LogLevel.Error && e.Message.Contains("CONFLATION") && e.Message.Contains(Uami),
            "the setting is still wrong for what it appears to mean and must be reported so it gets cleared");
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

    // =============================================================================================
    // Rule 6 (task 062, FR-F3) — no secret-backed BFF identity outside Development, once enabled
    // =============================================================================================

    [Fact]
    public void Rule6_WhenEnabledOutsideDevelopment_AndTheSecretIsStillListed_FailsFast()
    {
        // The window this closes: ordered selection falls through by design, so after the migration a
        // broken MI-FIC would resolve to the secret, serve every request and pass every health check.
        // That failure mode is not an outage — it is an outage that never appears.
        var result = ValidateRule6(
            requireSecretFree: true,
            environment: "Production",
            order: new[] { CredentialKind.ManagedIdentityFederated, CredentialKind.ClientSecret });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("RequireSecretFreeIdentity")
            .And.Contain("ClientSecret")
            .And.Contain("Production",
                "the message must name the environment and the offending credential, not just say no");
    }

    [Fact]
    public void Rule6_WhenEnabledOutsideDevelopment_AndTheSecretIsGone_Succeeds()
    {
        // The task-033 end state. This is what the whole project is for.
        var result = ValidateRule6(
            requireSecretFree: true,
            environment: "Production",
            order: new[] { CredentialKind.ManagedIdentityFederated });

        result.Succeeded.Should().BeTrue(
            "with the secret absent there is nothing beneath MI-FIC to fall through to, so a broken "
            + "MI-FIC fails loudly by construction — which is the property FR-F3 actually wants");
    }

    [Fact]
    public void Rule6_InDevelopment_IsExempt_EvenWithTheSecretListed()
    {
        // A developer workstation has no route to IMDS, so MI-FIC cannot be minted there at all. The
        // user-secret fallback is the legitimate — and only — way to run OBO locally, and failing here
        // would make the guard's first effect "nobody can run the BFF".
        var result = ValidateRule6(
            requireSecretFree: true,
            environment: "Development",
            order: new[] { CredentialKind.ManagedIdentityFederated, CredentialKind.ClientSecret });

        result.Succeeded.Should().BeTrue("Development legitimately uses the user-secret fallback for OBO");
    }

    [Fact]
    public void Rule6_WhileTheSecretIsStillTheIntentionalFallback_IsInert()
    {
        // THE negative control this task turns on, and the reason the flag defaults to false.
        //
        // Today — pre-033 — ClientSecret is the intentional lowest-priority fallback AND the rollback
        // mechanism NFR-06 depends on. A guard that fired now would block the very rollout it exists to
        // protect: task 031 deploys with the secret still listed, and 032 swaps with it still listed.
        // Gated on configuration rather than on a date, and OFF by default, so forgetting to enable it
        // at 033 leaves the guard silent rather than breaking a deployment.
        var result = ValidateRule6(
            requireSecretFree: false,
            environment: "Production",
            order: new[] { CredentialKind.ManagedIdentityFederated, CredentialKind.ClientSecret });

        result.Succeeded.Should().BeTrue(
            "pre-033 the secret is the intentional fallback; the guard must be inert until task 033 "
            + "sets RequireSecretFreeIdentity=true in the same change that removes it");
    }

    [Fact]
    public void Rule6_WithNoHostEnvironment_TreatsTheEnvironmentAsNonDevelopment()
    {
        // Conservative default. Defaulting an UNKNOWN environment to "exempt" would make a fail-fast
        // guard silently inert exactly where its absence matters most — which is how the false premise
        // this whole project exists to correct survived three audits.
        var result = new IdentityConfigurationValidator(
                Config(("Graph:ManagedIdentity:ClientId", Uami), ("API_APP_ID", AppRegistration)),
                NullLogger<IdentityConfigurationValidator>.Instance,
                environment: null)
            .Validate(null, new CredentialSelectionOptions
            {
                Order = new List<string>
                {
                    nameof(CredentialKind.ManagedIdentityFederated),
                    nameof(CredentialKind.ClientSecret),
                },
                RequireSecretFreeIdentity = true,
            });

        result.Failed.Should().BeTrue("an unknown environment must not be treated as Development");
    }

    private static ValidateOptionsResult ValidateRule6(
        bool requireSecretFree,
        string environment,
        IEnumerable<CredentialKind> order)
        => new IdentityConfigurationValidator(
                Config(("Graph:ManagedIdentity:ClientId", Uami), ("API_APP_ID", AppRegistration)),
                NullLogger<IdentityConfigurationValidator>.Instance,
                new StubHostEnvironment(environment))
            .Validate(null, new CredentialSelectionOptions
            {
                Order = order.Select(k => k.ToString()).ToList(),
                RequireSecretFreeIdentity = requireSecretFree,
            });

    private sealed class StubHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public StubHostEnvironment(string environmentName) => EnvironmentName = environmentName;

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Sprk.Bff.Api";
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

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
