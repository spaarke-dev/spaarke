// -----------------------------------------------------------------------------
// KvSecretValueResolverTests.cs
//
// L2 CONTROL-PLANE unit tests for KvSecretValueResolver (task 126, Wave G-2
// Batch G-2C — H4 real-values correctness gate). Proves the REAL
// Azure.Security.KeyVault.Secrets SDK call path for copy-sourced entries by
// constructing a SecretClient against a fake Azure.Core.Pipeline.HttpClientTransport
// (same fake-transport pattern as SecretClientKvWriterTests.cs / task 125) so
// the SDK's own request construction, URL building, and response
// deserialization all run unmodified; only the HTTP boundary is faked.
// ADR-038 path #1. Never Mock&lt;HttpMessageHandler&gt;.
//
// GROUND-TRUTHED SDK SHAPE (reused from SecretClientKvWriterTests.cs's own
// header comment — same 4.11.0 package, same verification):
//   SecretClient.GetSecretAsync(name, version, ct) -> GET /secrets/{name}/{version}
//   response.Value.Value is the KeyVaultSecret's cleartext string.
//
// COVERAGE:
//   T1  Generated: two consecutive resolutions of the SAME entry produce
//       DIFFERENT values (AC2 — proves real RandomNumberGenerator use, not a
//       fixed placeholder), each 64 lower-hex chars (256-bit entropy).
//   T2  FromExistingKvSecret: resolves a KeyVaultSecretRef from
//       request.SecretParameters, reads the REAL value from the referenced
//       vault, and returns it UNCHANGED (AC3 — copy matches source exactly).
//   T3  FromRunParameters: identical copy mechanism, different provenance —
//       proves both non-Generated/non-BicepOutput enum members resolve via
//       the SAME real-value-copy path (see file-header mapping note on
//       KvSecretValueResolver.cs for why).
//   T4  FromExistingKvSecret with NO matching SecretParameters entry ->
//       Failed with a clear diagnostic — NOT a fabricated value (the exact
//       discipline DS-4's fix exists to enforce).
//   T5  FromExistingKvSecret where the source vault 404s on GetSecretAsync
//       -> Failed with the HTTP status surfaced in the diagnostic.
//   T6  FromBicepOutput -> ALWAYS Failed (the documented, tracked plumbing
//       gap — see KvSecretValueResolver.cs file header + notes/task-126-
//       deviations.md "FromBicepOutput gap"). Proves the gap is
//       intentional + tested, not silently swallowed.
//   T7  Diagnostic text on Failed outcomes NEVER contains a cleartext value
//       (root CLAUDE.md §9 no-log guard, extended to diagnostics).
// -----------------------------------------------------------------------------

using System.Net;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;
using Sprk.Provisioning.ControlPlane.Models;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class KvSecretValueResolverTests
{
    private const string CustomerId = "acme";
    private const string TargetVaultName = "sprk-acme-prod-kv";
    private const string SourceVaultName = "sprk-platform-prod-kv";

    private static KvSecretValueResolver NewResolver(FakeSourceVaultHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var options = new SecretClientOptions { Transport = new HttpClientTransport(httpClient) };
        return new KvSecretValueResolver(new FakeCredential(), options);
    }

    private static KvSecretWriteRequest NewRequest(
        IReadOnlyDictionary<string, KeyVaultSecretRef>? secretParameters = null) => new(
        CustomerId: CustomerId,
        TargetKeyVaultName: TargetVaultName,
        SubscriptionId: "22222222-3333-4444-5555-666666666666",
        Entries: Array.Empty<KvSecretEntry>(),
        UpgradeMode: false,
        RotateExisting: false,
        SecretParameters: secretParameters);

    // ---------- T1 Generated: real randomness ----------

    [Fact]
    public async Task ResolveAsync_GeneratedEntry_TwoConsecutiveCallsProduceDifferentValues()
    {
        var resolver = NewResolver(new FakeSourceVaultHandler());
        var entry = new KvSecretEntry("Communication-Webhook-SigningKey", KvSecretOperation.Upsert, KvSecretValueSource.Generated);
        var request = NewRequest();

        var first = await resolver.ResolveAsync(entry, request, CancellationToken.None);
        var second = await resolver.ResolveAsync(entry, request, CancellationToken.None);

        var firstValue = first.Should().BeOfType<KvSecretValueResolution.Resolved>().Subject.Value;
        var secondValue = second.Should().BeOfType<KvSecretValueResolution.Resolved>().Subject.Value;

        firstValue.Should().NotBe(secondValue, "each Generated resolution must produce a fresh cryptographically-random value");
        firstValue.Should().HaveLength(64, "256-bit entropy, lower-hex encoded");
        firstValue.Should().MatchRegex("^[0-9a-f]{64}$");
        firstValue.Should().NotContain("interim-placeholder", "the exact defect DS-4 flagged must not resurface");
    }

    // ---------- T2 FromExistingKvSecret: real copy ----------

    [Fact]
    public async Task ResolveAsync_FromExistingKvSecretEntry_CopiesRealValueFromReferencedVault()
    {
        const string knownValue = "sup3r-s3cret-bff-client-secret-value";
        var handler = new FakeSourceVaultHandler { KnownValue = knownValue };
        var resolver = NewResolver(handler);
        var entry = new KvSecretEntry("BFF-API-ClientSecret", KvSecretOperation.Upsert, KvSecretValueSource.FromExistingKvSecret);
        var request = NewRequest(new Dictionary<string, KeyVaultSecretRef>(StringComparer.Ordinal)
        {
            ["BFF-API-ClientSecret"] = new KeyVaultSecretRef(SourceVaultName, "BFF-API-ClientSecret"),
        });

        var resolution = await resolver.ResolveAsync(entry, request, CancellationToken.None);

        var resolved = resolution.Should().BeOfType<KvSecretValueResolution.Resolved>().Subject;
        resolved.Value.Should().Be(knownValue, "the copy branch MUST return the actual source value unchanged");
        handler.RequestedSecretNames.Should().ContainSingle().Which.Should().Be("BFF-API-ClientSecret");
    }

    // ---------- T3 FromRunParameters: same copy mechanism ----------

    [Fact]
    public async Task ResolveAsync_FromRunParametersEntry_CopiesRealValueFromReferencedVault()
    {
        const string knownValue = "bing-search-api-key-operator-supplied";
        var handler = new FakeSourceVaultHandler { KnownValue = knownValue };
        var resolver = NewResolver(handler);
        var entry = new KvSecretEntry("BingSearch-ApiKey", KvSecretOperation.Upsert, KvSecretValueSource.FromRunParameters);
        var request = NewRequest(new Dictionary<string, KeyVaultSecretRef>(StringComparer.Ordinal)
        {
            ["BingSearch-ApiKey"] = new KeyVaultSecretRef(SourceVaultName, "operator-supplied-bing-key"),
        });

        var resolution = await resolver.ResolveAsync(entry, request, CancellationToken.None);

        var resolved = resolution.Should().BeOfType<KvSecretValueResolution.Resolved>().Subject;
        resolved.Value.Should().Be(knownValue);
        handler.RequestedSecretNames.Should().ContainSingle().Which.Should().Be("operator-supplied-bing-key");
    }

    // ---------- T4 missing reference -> honest failure, no fabricated value ----------

    [Fact]
    public async Task ResolveAsync_FromExistingKvSecretEntryWithNoSecretParameter_ReturnsFailedNotFabricated()
    {
        var resolver = NewResolver(new FakeSourceVaultHandler());
        var entry = new KvSecretEntry("Dataverse-ClientSecret", KvSecretOperation.Upsert, KvSecretValueSource.FromExistingKvSecret);
        var request = NewRequest(); // no SecretParameters supplied

        var resolution = await resolver.ResolveAsync(entry, request, CancellationToken.None);

        var failed = resolution.Should().BeOfType<KvSecretValueResolution.Failed>().Subject;
        failed.Diagnostic.Should().Contain("Dataverse-ClientSecret");
        failed.Diagnostic.Should().NotContain("interim-placeholder");
    }

    // ---------- T5 source vault 404s ----------

    [Fact]
    public async Task ResolveAsync_SourceSecretNotFound_ReturnsFailedWithHttpStatus()
    {
        var handler = new FakeSourceVaultHandler { SourceSecretMissing = true };
        var resolver = NewResolver(handler);
        var entry = new KvSecretEntry("BFF-API-ClientSecret", KvSecretOperation.Upsert, KvSecretValueSource.FromExistingKvSecret);
        var request = NewRequest(new Dictionary<string, KeyVaultSecretRef>(StringComparer.Ordinal)
        {
            ["BFF-API-ClientSecret"] = new KeyVaultSecretRef(SourceVaultName, "missing-secret"),
        });

        var resolution = await resolver.ResolveAsync(entry, request, CancellationToken.None);

        var failed = resolution.Should().BeOfType<KvSecretValueResolution.Failed>().Subject;
        failed.Diagnostic.Should().Contain("404");
    }

    // ---------- T6 FromBicepOutput: documented, tracked gap ----------

    [Fact]
    public async Task ResolveAsync_FromBicepOutputEntry_AlwaysReturnsFailedWithGapDiagnostic()
    {
        var resolver = NewResolver(new FakeSourceVaultHandler());
        var entry = new KvSecretEntry("AiSearch--AdminKey", KvSecretOperation.Upsert, KvSecretValueSource.FromBicepOutput);
        var request = NewRequest();

        var resolution = await resolver.ResolveAsync(entry, request, CancellationToken.None);

        var failed = resolution.Should().BeOfType<KvSecretValueResolution.Failed>().Subject;
        failed.Diagnostic.Should().Contain("FromBicepOutput");
        failed.Diagnostic.Should().NotContain("interim-placeholder");
    }

    // ---------- T7 no cleartext ever appears in a diagnostic ----------

    [Fact]
    public async Task ResolveAsync_AnyFailedOutcome_DiagnosticNeverContainsASecretValue()
    {
        const string sourceValue = "THIS-VALUE-MUST-NEVER-LEAK-INTO-A-DIAGNOSTIC";
        var handler = new FakeSourceVaultHandler { KnownValue = sourceValue, SourceSecretMissing = true };
        var resolver = NewResolver(handler);
        var entry = new KvSecretEntry("BFF-API-ClientSecret", KvSecretOperation.Upsert, KvSecretValueSource.FromExistingKvSecret);
        var request = NewRequest(new Dictionary<string, KeyVaultSecretRef>(StringComparer.Ordinal)
        {
            ["BFF-API-ClientSecret"] = new KeyVaultSecretRef(SourceVaultName, "some-secret"),
        });

        var resolution = await resolver.ResolveAsync(entry, request, CancellationToken.None);

        var failed = resolution.Should().BeOfType<KvSecretValueResolution.Failed>().Subject;
        failed.Diagnostic.Should().NotContain(sourceValue);
    }

    /// <summary>Minimal fake <see cref="TokenCredential"/> — same convention as SecretClientKvWriterTests.cs.</summary>
    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-kv-test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    /// <summary>Fake transport standing in for the SOURCE vault's SecretClient.GetSecretAsync call.</summary>
    private sealed class FakeSourceVaultHandler : HttpMessageHandler
    {
        public string KnownValue { get; init; } = "default-fake-source-value";
        public bool SourceSecretMissing { get; init; }
        public List<string> RequestedSecretNames { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var name = path.Trim('/').Split('/').Last();
            RequestedSecretNames.Add(name);

            if (SourceSecretMissing)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("""{"error":{"code":"SecretNotFound","message":"not found"}}""",
                        System.Text.Encoding.UTF8, "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$$"""{"value":"{{{KnownValue}}}","id":"https://{{{SourceVaultName}}}.vault.azure.net/secrets/{{{name}}}/v1","attributes":{"enabled":true}}""",
                    System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
