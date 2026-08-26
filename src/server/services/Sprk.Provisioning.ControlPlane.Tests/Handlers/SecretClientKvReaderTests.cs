// -----------------------------------------------------------------------------
// SecretClientKvReaderTests.cs
//
// L2 CONTROL-PLANE unit tests for SecretClientKvReader (task 160, Wave G-6 —
// H14 KV-reader swap, AzCliKvSecretReader -> SecretClient). Proves the REAL
// Azure.Security.KeyVault.Secrets SDK call path — not a hard-coded outcome —
// by constructing a SecretClient against a fake
// Azure.Core.Pipeline.HttpClientTransport so the SDK's own request
// construction, URL building, and response deserialization all run
// unmodified; only the HTTP boundary is faked. ADR-038 path #1 (never
// Mock<HttpMessageHandler>).
//
// GROUND-TRUTHED SDK SHAPE (reused from SecretClientKvWriterTests.cs's own
// ground-truthing, NOT re-guessed): SecretClient.GetSecretAsync(name, null,
// ct) -> GET /secrets/{name}/ (trailing slash); throws RequestFailedException
// with Status==404 for a missing secret; KeyVaultSecret.Value is the
// cleartext string.
//
// PARITY-SHAPE COVERAGE (acceptance criterion: "A regression test confirms
// the reader's return contract is unchanged from AzCliKvSecretReader's
// shape" — Success(value) | NotFound() | Failure(diagnostic), same 3-case
// discriminated union AzCliKvSecretReader.ReadSecretAsync returned):
//   T1  Happy path (200 + value) -> Success(trimmedValue).
//   T2  404 (SecretNotFound)     -> NotFound() — never a Failure, matching
//       AzCliKvSecretReader's own stderr.Contains("SecretNotFound") branch
//       mapping to the SAME NotFound() case.
//   T3  403 (Forbidden)          -> Failure() with an access-denied-specific
//       diagnostic (silent-fail-adjacent distinction task 143/144 established
//       matters — a permanent RBAC misconfiguration must not read like a
//       transient fault).
//   T4  200 + empty value        -> Failure() (parity with AzCliKvSecretReader's
//       own "exited 0 but returned an empty value" branch).
//   T5  Generic 500               -> Failure() (parity with the retired
//       reader's "az keyvault secret show exit {code}" branch).
//   T6  Read timeout               -> Failure() carrying a TimeoutException
//       message (parity with the retired reader's process-timeout ->
//       TimeoutException -> generic-catch -> Failure path).
//   T7  No 'az'/ProcessStartInfo reference remains in the new production
//       file (acceptance criterion #1 — a source-text grep, not SDK
//       behavior, but co-located here so the whole task's acceptance
//       surface lives in one place).
// -----------------------------------------------------------------------------

using System.IO;
using System.Net;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class SecretClientKvReaderTests
{
    private const string VaultName = "sprk-acme-prod-kv";
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";
    private const string SecretName = "Communication-Webhook-SigningKey";

    private static SecretClientKvReader NewReader(FakeSecretsHandler handler, IntegrationWiringOptions? options = null)
    {
        var httpClient = new HttpClient(handler);
        var secretClientOptions = new SecretClientOptions { Transport = new HttpClientTransport(httpClient) };

        return new SecretClientKvReader(
            new FakeCredential(),
            secretClientOptions,
            Options.Create(options ?? new IntegrationWiringOptions()),
            NullLogger<SecretClientKvReader>.Instance);
    }

    // ---------- T1 happy path ----------

    [Fact]
    public async Task ReadSecretAsync_HappyPath_ReturnsSuccessWithTrimmedValue()
    {
        var handler = new FakeSecretsHandler { Value = "  the-signing-key-value  " };
        var reader = NewReader(handler);

        var result = await reader.ReadSecretAsync(VaultName, SubscriptionId, SecretName, CancellationToken.None);

        var success = result.Should().BeOfType<KvSecretReadResult.Success>().Subject;
        success.Value.Should().Be("the-signing-key-value");
        handler.RequestedUris.Should().ContainSingle(u => u.AbsolutePath.Contains(SecretName));
    }

    // ---------- T2 not-found ----------

    [Fact]
    public async Task ReadSecretAsync_SecretNotFound_ReturnsNotFound_NeverFailure()
    {
        var handler = new FakeSecretsHandler { StatusCode = HttpStatusCode.NotFound, ErrorCode = "SecretNotFound" };
        var reader = NewReader(handler);

        var result = await reader.ReadSecretAsync(VaultName, SubscriptionId, SecretName, CancellationToken.None);

        result.Should().BeOfType<KvSecretReadResult.NotFound>(
            "parity with AzCliKvSecretReader's own SecretNotFound -> NotFound() mapping");
    }

    // ---------- T3 access-denied distinction ----------

    [Fact]
    public async Task ReadSecretAsync_Forbidden_ReturnsFailureWithAccessDeniedDiagnostic()
    {
        var handler = new FakeSecretsHandler { StatusCode = HttpStatusCode.Forbidden, ErrorCode = "Forbidden" };
        var reader = NewReader(handler);

        var result = await reader.ReadSecretAsync(VaultName, SubscriptionId, SecretName, CancellationToken.None);

        var failure = result.Should().BeOfType<KvSecretReadResult.Failure>().Subject;
        failure.Diagnostic.Should().Contain("Access denied").And.Contain("403").And.Contain("Key Vault Secrets User",
            "a 403 must be distinguishable from a generic/transient failure — this is a permanent RBAC gap, not a retry-worthy fault");
    }

    // ---------- T4 empty-value-on-200 ----------

    [Fact]
    public async Task ReadSecretAsync_EmptyValueOn200_ReturnsFailure()
    {
        var handler = new FakeSecretsHandler { Value = "" };
        var reader = NewReader(handler);

        var result = await reader.ReadSecretAsync(VaultName, SubscriptionId, SecretName, CancellationToken.None);

        result.Should().BeOfType<KvSecretReadResult.Failure>(
            "parity with AzCliKvSecretReader's own 'exited 0 but returned an empty value' branch");
    }

    // ---------- T5 generic infra fault ----------

    [Fact]
    public async Task ReadSecretAsync_GenericServerError_ReturnsFailure()
    {
        var handler = new FakeSecretsHandler { StatusCode = HttpStatusCode.InternalServerError, ErrorCode = "InternalError" };
        var reader = NewReader(handler);

        var result = await reader.ReadSecretAsync(VaultName, SubscriptionId, SecretName, CancellationToken.None);

        result.Should().BeOfType<KvSecretReadResult.Failure>();
    }

    // ---------- T6 timeout ----------

    [Fact]
    public async Task ReadSecretAsync_Timeout_ReturnsFailureCarryingTimeoutDiagnostic()
    {
        var handler = new FakeSecretsHandler { Delay = TimeSpan.FromSeconds(2) };
        var options = new IntegrationWiringOptions { KvReadTimeout = TimeSpan.FromMilliseconds(50) };
        var reader = NewReader(handler, options);

        var result = await reader.ReadSecretAsync(VaultName, SubscriptionId, SecretName, CancellationToken.None);

        var failure = result.Should().BeOfType<KvSecretReadResult.Failure>().Subject;
        failure.Diagnostic.Should().Contain("TimeoutException",
            "parity with AzCliKvSecretReader's own process-timeout -> TimeoutException -> generic-catch -> Failure path");
    }

    // ---------- T7 no shell-out reference remains ----------

    [Fact]
    public void SecretClientKvReader_SourceFile_ContainsNoAzOrProcessStartInfoReference()
    {
        // [CallerFilePath] resolves at COMPILE time to this test file's own
        // absolute path on the build machine — robust across bin/obj output
        // layout changes (net10.0 / linux-x64 / Debug-vs-Release), unlike
        // walking up from AppContext.BaseDirectory.
        var thisFileDir = Path.GetDirectoryName(ThisFilePath())!;
        // .../Sprk.Provisioning.ControlPlane.Tests/Handlers -> up to .../services
        var servicesDir = Directory.GetParent(Directory.GetParent(thisFileDir)!.FullName)!.FullName;
        var candidate = Path.Combine(
            servicesDir, "Sprk.Provisioning.ControlPlane.Core", "Handlers", "IntegrationWiring", "SecretClientKvReader.cs");
        File.Exists(candidate).Should().BeTrue($"expected to find {candidate}");

        var text = File.ReadAllText(candidate);

        text.Should().NotContain("ProcessStartInfo");
        text.Should().NotMatch("*FileName*=*\"az\"*",
            "acceptance criterion #1: grep 'FileName.*az|ProcessStartInfo' must return 0 matches in the new file");
    }

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    /// <summary>Minimal fake <see cref="TokenCredential"/> — this file's own copy per SecretClientKvWriterTests.cs's convention.</summary>
    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-kv-test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    /// <summary>Fake transport for a single SecretClient.GetSecretAsync call.</summary>
    private sealed class FakeSecretsHandler : HttpMessageHandler
    {
        public string Value { get; init; } = "default-value";
        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;
        public string ErrorCode { get; init; } = string.Empty;
        public TimeSpan? Delay { get; init; }

        public List<Uri> RequestedUris { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUris.Add(request.RequestUri!);

            if (Delay is { } delay)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            if (StatusCode != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(StatusCode)
                {
                    Content = new StringContent(
                        $$$"""{"error":{"code":"{{{ErrorCode}}}","message":"fake error"}}""",
                        System.Text.Encoding.UTF8, "application/json"),
                };
            }

            var path = request.RequestUri!.AbsolutePath;
            var name = path.Trim('/').Split('/').First();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$$"""{"value":"{{{Value}}}","id":"https://sprk-acme-prod-kv.vault.azure.net/secrets/{{{name}}}/v1","attributes":{"enabled":true}}""",
                    System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}
