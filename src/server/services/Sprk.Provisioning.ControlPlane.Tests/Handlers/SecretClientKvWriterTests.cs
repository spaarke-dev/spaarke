// -----------------------------------------------------------------------------
// SecretClientKvWriterTests.cs
//
// L2 CONTROL-PLANE unit tests for SecretClientKvWriter (task 125, Wave G-2).
// Proves the REAL Azure.Security.KeyVault.Secrets + Azure.ResourceManager SDK
// call paths — not a hard-coded outcome — by constructing a SecretClient
// against a fake Azure.Core.Pipeline.HttpClientTransport (same fake handler
// shared by the ArmClient preflight probe) so the SDK's own request
// construction, URL building, and response deserialization all run
// unmodified; only the HTTP boundary is faked. ADR-038 path #1.
//
// GROUND-TRUTHED SDK SHAPES (verified via a throwaway scratch console app
// against the installed Azure.Security.KeyVault.Secrets 4.11.0 +
// Azure.ResourceManager 1.14.0 packages BEFORE writing this file, not
// guessed):
//   - SecretClient.SetSecretAsync(name, value, ct) -> PUT /secrets/{name}
//   - SecretClient.GetSecretAsync(name, null, ct)  -> GET /secrets/{name}/
//     (trailing slash — matches KeyVaultCertBootstrapProbeTests.cs's own
//     ground-truthing note)
//   - SecretClient.StartDeleteSecretAsync(name, ct) -> DELETE /secrets/{name};
//     the returned DeleteSecretOperation.HasCompleted is FALSE on the initial
//     response (KV's own async purge-readiness semantics), but the HTTP call
//     itself already succeeded — H4's writer does NOT call
//     WaitForCompletionAsync (parity with the retired az CLI's
//     `az keyvault secret delete`, which also just issues one synchronous
//     DELETE and returns without polling further).
//   - ArmClient.GetDefaultSubscriptionAsync() (the writer's "account show"
//     equivalent) performs a SINGLE GET /subscriptions/{defaultSubscriptionId}
//     — reuses the SAME shape as ArmSubscriptionReadinessProbeTests.cs's
//     SubscriptionGetBody helper.
//
// COVERAGE:
//   T1  Happy path: preflight ArmClient call + one PUT per Upsert entry
//       (including BFF-API-ClientSecret — proving FR-39's "no special-casing"
//       acceptance criterion: it takes the SAME code path as every other
//       entry, not a distinct one).
//   T2  Never-delete guard fires for Dataverse-ClientSecret BEFORE any HTTP
//       DELETE call — carried verbatim from the retired writer.
//   T3  A non-guarded entry's Delete op DOES call SecretClient.StartDeleteSecretAsync.
//   T4  Upgrade-mode + rotate=false + secret already exists -> SkippedRotationSafe,
//       NO PUT call for that entry (rotation-safe default, spec.md FR-34).
//   T5  ARM preflight probe fails (403) -> Failure BEFORE any /secrets/ call —
//       proves the whole-writer prerequisite gate still exists post-port.
//   T6  A single entry's SetSecretAsync 500s -> that entry's result is Failed,
//       but the OVERALL outcome is still Success (per-entry isolation
//       preserved — matches KvSecretsWriteOutcome's contract).
//
// TASK 126 ADDITIONS (H4 real-values correctness gate — value resolution is
// now delegated to IKvSecretValueResolver; this file uses a canned FakeValueResolver
// test double so these tests stay focused on the WRITER's control flow —
// existence checks, rotation-safety, the FIC omit-list, and the
// FromBicepOutput skip-if-already-provisioned guard. IKvSecretValueResolver's
// OWN SDK-calling behavior — real GetSecretAsync copy-sourcing, generated
// crypto-random values — is ground-truthed separately in
// KvSecretValueResolverTests.cs):
//   T7  A FromBicepOutput entry that ALREADY EXISTS on the target vault is
//       skipped WITHOUT invoking the resolver and WITHOUT any PUT call
//       (satisfies task 126 AC4's "no value-copy call made" — the writer
//       never asks the resolver for a value at all for this case).
//   T8  An entry listed in OmitCanonicalNames (FR-39 FIC pluggability) is
//       omitted entirely — no GET, no PUT, no resolver call — regardless of
//       whether it is BFF-API-ClientSecret or any other name (manifest/run-
//       parameter driven, not a hardcoded identity check).
//   T9  The resolver returning Failed for an entry propagates as a per-entry
//       Failed result (no fabricated value written).
// -----------------------------------------------------------------------------

using System.Net;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class SecretClientKvWriterTests
{
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";
    private const string TenantId = "11111111-2222-3333-4444-555555555555";
    private const string VaultName = "sprk-acme-prod-kv";
    private const string CustomerId = "acme";

    private static KvSecretsPopulationOptions NewOptions() => new();

    private static KvSecretWriteRequest NewRequest(
        IReadOnlyList<KvSecretEntry> entries,
        bool upgradeMode = false,
        bool rotate = false,
        IReadOnlySet<string>? omitCanonicalNames = null) => new(
        CustomerId: CustomerId,
        TargetKeyVaultName: VaultName,
        SubscriptionId: SubscriptionId,
        Entries: entries,
        UpgradeMode: upgradeMode,
        RotateExisting: rotate,
        OmitCanonicalNames: omitCanonicalNames);

    private static SecretClientKvWriter NewWriter(FakeSecretsHandler handler, IKvSecretValueResolver? resolver = null)
    {
        // Both the ArmClient (preflight probe) and the SecretClient (KV ops)
        // are built against the SAME fake HttpMessageHandler instance so a
        // single test can assert the full call sequence across both SDKs.
        var httpClient = new HttpClient(handler);
        var armClient = new Azure.ResourceManager.ArmClient(
            new FakeCredential(),
            Guid.NewGuid().ToString(),
            new Azure.ResourceManager.ArmClientOptions { Transport = new HttpClientTransport(httpClient) });
        var secretClientOptions = new SecretClientOptions { Transport = new HttpClientTransport(httpClient) };

        return new SecretClientKvWriter(
            new FakeCredential(),
            armClient,
            secretClientOptions,
            resolver ?? new FakeValueResolver(),
            Options.Create(NewOptions()),
            NullLogger<SecretClientKvWriter>.Instance);
    }

    // ---------- T1 happy path (incl. FR-39 no-special-casing) ----------

    [Fact]
    public async Task WriteAsync_HappyPath_WritesAllEntriesIncludingBffApiClientSecretIdentically()
    {
        var handler = new FakeSecretsHandler(SubscriptionId, TenantId);
        var writer = NewWriter(handler);
        var entries = new List<KvSecretEntry>
        {
            new("AiSearch--AdminKey", KvSecretOperation.Upsert, KvSecretValueSource.FromBicepOutput),
            new("BFF-API-ClientSecret", KvSecretOperation.Upsert, KvSecretValueSource.FromExistingKvSecret),
        };

        var outcome = await writer.WriteAsync(NewRequest(entries), CancellationToken.None);

        var success = outcome.Should().BeOfType<KvSecretsWriteOutcome.Success>().Subject;
        success.Results.Should().HaveCount(2);
        success.Results.Should().OnlyContain(r => r.Action == KvSecretWriteAction.Wrote);

        handler.RequestedUris.Should().Contain(u => u.AbsolutePath.StartsWith("/subscriptions/"),
            "the whole-writer preflight probe must actually call ARM, not skip straight to KV");
        handler.RequestedUris.Should().Contain(u => u.AbsolutePath == "/secrets/AiSearch--AdminKey");
        handler.RequestedUris.Should().Contain(u => u.AbsolutePath == "/secrets/BFF-API-ClientSecret",
            "FR-39: BFF-API-ClientSecret MUST flow through the SAME PUT call path as every other entry — no special-casing");
        handler.PutMethods.Should().HaveCount(2);
    }

    // ---------- T2 never-delete guard ----------

    [Fact]
    public async Task WriteAsync_DeleteOnNeverDeleteSecret_ReturnsFailedWithoutCallingKv()
    {
        var handler = new FakeSecretsHandler(SubscriptionId, TenantId);
        var writer = NewWriter(handler);
        var entries = new List<KvSecretEntry>
        {
            new("Dataverse-ClientSecret", KvSecretOperation.Delete, KvSecretValueSource.FromExistingKvSecret),
        };

        var outcome = await writer.WriteAsync(NewRequest(entries), CancellationToken.None);

        var success = outcome.Should().BeOfType<KvSecretsWriteOutcome.Success>().Subject;
        var result = success.Results.Single();
        result.Action.Should().Be(KvSecretWriteAction.Failed);
        result.ErrorMessage.Should().Contain("BINDING never-delete guard");

        handler.RequestedUris.Should().NotContain(u => u.AbsolutePath.Contains("Dataverse-ClientSecret"),
            "the guard MUST fire before any HTTP DELETE call reaches the KV data plane");
    }

    // ---------- T3 allowed delete ----------

    [Fact]
    public async Task WriteAsync_DeleteOnNonGuardedSecret_CallsDeleteAndReturnsDeleted()
    {
        var handler = new FakeSecretsHandler(SubscriptionId, TenantId);
        var writer = NewWriter(handler);
        var entries = new List<KvSecretEntry>
        {
            new("SPE-ContainerTypeId", KvSecretOperation.Delete, KvSecretValueSource.FromBicepOutput),
        };

        var outcome = await writer.WriteAsync(NewRequest(entries), CancellationToken.None);

        var success = outcome.Should().BeOfType<KvSecretsWriteOutcome.Success>().Subject;
        success.Results.Single().Action.Should().Be(KvSecretWriteAction.Deleted);
        handler.DeleteMethods.Should().ContainSingle();
    }

    // ---------- T4 rotation-safe skip ----------

    [Fact]
    public async Task WriteAsync_UpgradeModeRotateFalseExistingSecret_SkipsWithoutPut()
    {
        var handler = new FakeSecretsHandler(SubscriptionId, TenantId) { SecretExists = true };
        var writer = NewWriter(handler);
        var entries = new List<KvSecretEntry>
        {
            new("AiSearch--AdminKey", KvSecretOperation.Upsert, KvSecretValueSource.FromBicepOutput),
        };

        var outcome = await writer.WriteAsync(NewRequest(entries, upgradeMode: true, rotate: false), CancellationToken.None);

        var success = outcome.Should().BeOfType<KvSecretsWriteOutcome.Success>().Subject;
        success.Results.Single().Action.Should().Be(KvSecretWriteAction.SkippedRotationSafe);
        handler.PutMethods.Should().BeEmpty("rotation-safe default must never overwrite an existing live secret");
    }

    // ---------- T5 preflight failure gates before any KV call ----------

    [Fact]
    public async Task WriteAsync_ArmPreflightProbeForbidden_ReturnsFailureBeforeAnyKvCall()
    {
        var handler = new FakeSecretsHandler(SubscriptionId, TenantId) { PreflightForbidden = true };
        var writer = NewWriter(handler);
        var entries = new List<KvSecretEntry>
        {
            new("AiSearch--AdminKey", KvSecretOperation.Upsert, KvSecretValueSource.FromBicepOutput),
        };

        var outcome = await writer.WriteAsync(NewRequest(entries), CancellationToken.None);

        outcome.Should().BeOfType<KvSecretsWriteOutcome.Failure>();
        handler.RequestedUris.Should().NotContain(u => u.AbsolutePath.StartsWith("/secrets/"),
            "the preflight gate must fail BEFORE any per-entry KV work — no partial state");
    }

    // ---------- T6 per-entry isolation ----------

    [Fact]
    public async Task WriteAsync_OneEntryPutFails_OtherEntriesStillSucceedAndOutcomeIsSuccess()
    {
        var handler = new FakeSecretsHandler(SubscriptionId, TenantId) { FailPutForName = "AiSearch--AdminKey" };
        var writer = NewWriter(handler);
        var entries = new List<KvSecretEntry>
        {
            new("AiSearch--AdminKey", KvSecretOperation.Upsert, KvSecretValueSource.FromBicepOutput),
            new("Dataverse-ServiceUrl", KvSecretOperation.Upsert, KvSecretValueSource.FromBicepOutput),
        };

        var outcome = await writer.WriteAsync(NewRequest(entries), CancellationToken.None);

        var success = outcome.Should().BeOfType<KvSecretsWriteOutcome.Success>().Subject;
        success.Results.Should().HaveCount(2);
        success.Results.Single(r => r.CanonicalName == "AiSearch--AdminKey").Action.Should().Be(KvSecretWriteAction.Failed);
        success.Results.Single(r => r.CanonicalName == "Dataverse-ServiceUrl").Action.Should().Be(KvSecretWriteAction.Wrote);
    }

    // ---------- T7 FromBicepOutput already-exists skip (no resolver call) ----------

    [Fact]
    public async Task WriteAsync_FromBicepOutputEntryAlreadyExists_SkipsWithoutInvokingResolverOrPut()
    {
        var handler = new FakeSecretsHandler(SubscriptionId, TenantId) { SecretExists = true };
        var resolver = new FakeValueResolver();
        var writer = NewWriter(handler, resolver);
        var entries = new List<KvSecretEntry>
        {
            new("AiSearch--AdminKey", KvSecretOperation.Upsert, KvSecretValueSource.FromBicepOutput),
        };

        var outcome = await writer.WriteAsync(NewRequest(entries), CancellationToken.None);

        var success = outcome.Should().BeOfType<KvSecretsWriteOutcome.Success>().Subject;
        success.Results.Single().Action.Should().Be(KvSecretWriteAction.SkippedRotationSafe);
        handler.PutMethods.Should().BeEmpty(
            "FromBicepOutput entries that already exist must never be overwritten with a resolver guess");
        resolver.ResolvedNames.Should().BeEmpty(
            "task 126 AC4: no value-copy/resolution call should be made when the ARM-deployed value is already present");
    }

    // ---------- T8 FIC omit-list (FR-39 pluggability, manifest/run-parameter driven) ----------

    [Theory]
    [InlineData("BFF-API-ClientSecret")]
    [InlineData("AiSearch--AdminKey")]
    public async Task WriteAsync_EntryInOmitCanonicalNames_OmitsEntirelyWithoutAnyKvOrResolverCall(string canonicalName)
    {
        var handler = new FakeSecretsHandler(SubscriptionId, TenantId);
        var resolver = new FakeValueResolver();
        var writer = NewWriter(handler, resolver);
        var entries = new List<KvSecretEntry>
        {
            new(canonicalName, KvSecretOperation.Upsert, KvSecretValueSource.FromExistingKvSecret),
        };

        var outcome = await writer.WriteAsync(
            NewRequest(entries, omitCanonicalNames: new HashSet<string>(StringComparer.Ordinal) { canonicalName }),
            CancellationToken.None);

        var success = outcome.Should().BeOfType<KvSecretsWriteOutcome.Success>().Subject;
        success.Results.Single().Action.Should().Be(KvSecretWriteAction.Omitted);
        handler.RequestedUris.Should().NotContain(u => u.AbsolutePath.Contains(canonicalName),
            "an omitted entry must generate ZERO KV calls — not even an existence probe");
        resolver.ResolvedNames.Should().BeEmpty("an omitted entry must never reach value resolution");
    }

    // ---------- T9 resolver failure propagates as per-entry Failed ----------

    [Fact]
    public async Task WriteAsync_ResolverFailsForEntry_ReturnsFailedWithoutPut()
    {
        var handler = new FakeSecretsHandler(SubscriptionId, TenantId);
        var resolver = new FakeValueResolver { FailForName = "AiSearch--AdminKey" };
        var writer = NewWriter(handler, resolver);
        var entries = new List<KvSecretEntry>
        {
            new("AiSearch--AdminKey", KvSecretOperation.Upsert, KvSecretValueSource.FromBicepOutput),
        };

        var outcome = await writer.WriteAsync(NewRequest(entries), CancellationToken.None);

        var success = outcome.Should().BeOfType<KvSecretsWriteOutcome.Success>().Subject;
        var result = success.Results.Single();
        result.Action.Should().Be(KvSecretWriteAction.Failed);
        result.ErrorMessage.Should().Contain("fake resolver failure");
        handler.PutMethods.Should().BeEmpty("a resolution failure must never fall through to a fabricated write");
    }

    /// <summary>
    /// Canned-outcome <see cref="IKvSecretValueResolver"/> test double. Real
    /// SDK-calling resolution behavior (GetSecretAsync copy-sourcing,
    /// RandomNumberGenerator generation) is ground-truthed separately in
    /// KvSecretValueResolverTests.cs — this file's tests focus on the
    /// WRITER's control flow, not the resolver's internals.
    /// </summary>
    private sealed class FakeValueResolver : IKvSecretValueResolver
    {
        public string? FailForName { get; init; }
        public List<string> ResolvedNames { get; } = new();

        public Task<KvSecretValueResolution> ResolveAsync(
            KvSecretEntry entry, KvSecretWriteRequest request, CancellationToken cancellationToken)
        {
            ResolvedNames.Add(entry.CanonicalName);
            if (string.Equals(entry.CanonicalName, FailForName, StringComparison.Ordinal))
            {
                return Task.FromResult<KvSecretValueResolution>(
                    new KvSecretValueResolution.Failed($"fake resolver failure for '{entry.CanonicalName}'"));
            }
            return Task.FromResult<KvSecretValueResolution>(
                new KvSecretValueResolution.Resolved($"{entry.CanonicalName}-fake-resolved-value"));
        }
    }

    /// <summary>Minimal fake <see cref="TokenCredential"/> — this file's own copy per KeyVaultCertBootstrapProbeTests.cs's convention.</summary>
    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-kv-test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    /// <summary>
    /// Routes BOTH the ArmClient preflight call (GET /subscriptions/{id}) and
    /// the SecretClient KV calls (PUT/GET/DELETE /secrets/{name}) through one
    /// fake transport so a single test can assert the full call sequence.
    /// </summary>
    private sealed class FakeSecretsHandler : HttpMessageHandler
    {
        private readonly string _tenantId;

        public FakeSecretsHandler(string subscriptionId, string tenantId)
        {
            _tenantId = tenantId;
        }

        public bool SecretExists { get; init; }
        public bool PreflightForbidden { get; init; }
        public string? FailPutForName { get; init; }

        public List<Uri> RequestedUris { get; } = new();
        public List<Uri> PutMethods { get; } = new();
        public List<Uri> DeleteMethods { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUris.Add(request.RequestUri!);
            var path = request.RequestUri!.AbsolutePath;

            if (path.StartsWith("/subscriptions/", StringComparison.Ordinal))
            {
                if (PreflightForbidden)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
                    {
                        Content = new StringContent(
                            """{"error":{"code":"AuthorizationFailed","message":"denied"}}""",
                            System.Text.Encoding.UTF8, "application/json"),
                    });
                }
                var subId = path.Split('/')[2];
                var body = $$"""
                {
                  "id": "/subscriptions/{{subId}}",
                  "subscriptionId": "{{subId}}",
                  "displayName": "Test Sub",
                  "tenantId": "{{_tenantId}}",
                  "state": "Enabled",
                  "authorizationSource": "RoleBased"
                }
                """;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
                });
            }

            if (request.Method == HttpMethod.Put)
            {
                PutMethods.Add(request.RequestUri!);
                var name = path.Trim('/').Split('/').Last();
                if (string.Equals(name, FailPutForName, StringComparison.Ordinal))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("""{"error":{"code":"InternalError","message":"boom"}}""",
                            System.Text.Encoding.UTF8, "application/json"),
                    });
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$$"""{"value":"redacted","id":"https://{{{VaultName}}}.vault.azure.net/secrets/{{{name}}}/v1","attributes":{"enabled":true}}""",
                        System.Text.Encoding.UTF8, "application/json"),
                });
            }

            if (request.Method == HttpMethod.Delete)
            {
                DeleteMethods.Add(request.RequestUri!);
                var name = path.Trim('/').Split('/').Last();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$$"""{"recoveryId":"https://{{{VaultName}}}.vault.azure.net/deletedsecrets/{{{name}}}","deletedDate":1700000000,"scheduledPurgeDate":1700000000,"id":"https://{{{VaultName}}}.vault.azure.net/secrets/{{{name}}}/v1","attributes":{"enabled":false}}""",
                        System.Text.Encoding.UTF8, "application/json"),
                });
            }

            // GET (secret-existence probe)
            if (SecretExists)
            {
                var name = path.Trim('/').Split('/').First();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$$"""{"value":"redacted","id":"https://{{{VaultName}}}.vault.azure.net/secrets/{{{name}}}/v1","attributes":{"enabled":true}}""",
                        System.Text.Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"error":{"code":"SecretNotFound","message":"not found"}}""",
                    System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
