// -----------------------------------------------------------------------------
// ExchangeAdminTokenSource.cs
//
// Task 251 (owner D24): the Exchange Online token the Worker hands the sidecar. The Worker signs
// in as the 'Spaarke Exchange Admin' app registration through the federated identity credential
// whose subject is the Worker's managed identity (ADR-028 A4 -- the same mechanism task 248 uses
// for the SPE owning app). The sidecar receives the token per request and runs
// Connect-ExchangeOnline -AccessToken; it holds no credential of its own.
//
// Justification (CLAUDE.md §11): Existing -- SpeConfidentialClientGraphFactory builds the SPE
// owning-app credential and a GRAPH client; this needs an Exchange Online token for a different app.
// Extension -- both use WorkerDataverseCredentialFactory.CreateManagedIdentityFederatedCredential,
// the single place the Worker mints a federated assertion; only the app and scope differ.
// Cost of doing nothing -- the sidecar has no way to authenticate to Exchange, so H14a and H13 T4
// can never run.
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.Credentials;

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <summary>
/// Exchange Online access tokens for the <c>Spaarke Exchange Admin</c> app, obtained through the
/// Worker managed identity's federated credential. Singleton; no network I/O until a token is requested.
/// </summary>
public sealed class ExchangeAdminTokenSource
{
    /// <summary>The app-only Exchange Online scope Connect-ExchangeOnline -AccessToken needs.</summary>
    public static readonly string[] ExchangeOnlineScope = { "https://outlook.office365.com/.default" };

    private readonly Func<string, string, TokenCredential> _createCredential;
    private readonly IntegrationWiringOptions _options;
    private readonly ConcurrentDictionary<(string TenantId, string AppId), TokenCredential> _credentials = new();

    /// <summary>Production constructor — credentials come from the Worker UAMI (MI-FIC).</summary>
    public ExchangeAdminTokenSource(WorkerDataverseCredentialFactory credentials, IOptions<IntegrationWiringOptions> options)
        : this(ResolveFrom(credentials), options)
    {
    }

    /// <summary>Test seam: <paramref name="createCredential"/> replaces the MI-FIC credential.</summary>
    internal ExchangeAdminTokenSource(Func<string, string, TokenCredential> createCredential, IOptions<IntegrationWiringOptions> options)
    {
        ArgumentNullException.ThrowIfNull(createCredential);
        ArgumentNullException.ThrowIfNull(options);
        _createCredential = createCredential;
        _options = options.Value;
    }

    /// <summary>
    /// An Exchange Online token for <c>Spaarke Exchange Admin</c> in <paramref name="tenantId"/>.
    /// Never throws for configuration or sign-in failures — returns <see cref="ExchangeTokenResult.Failure"/>
    /// with a diagnostic naming what to check.
    /// </summary>
    public async Task<ExchangeTokenResult> GetTokenAsync(string tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var appId = _options.ExchangeAdminAppId?.Trim() ?? string.Empty;
        if (appId.Length == 0)
        {
            return new ExchangeTokenResult.Failure(
                $"'{IntegrationWiringModule.ConfigSection}:ExchangeAdminAppId' is not configured — the Worker cannot sign in to " +
                "Exchange Online. Set it to the client id of the 'Spaarke Exchange Admin' app registration (platform-controlplane " +
                "parameter exchangeAdminAppId).");
        }

        var credential = _credentials.GetOrAdd(
            (tenantId.Trim().ToLowerInvariant(), appId.ToLowerInvariant()),
            key => _createCredential(key.TenantId, key.AppId));
        try
        {
            var token = await credential.GetTokenAsync(new TokenRequestContext(ExchangeOnlineScope), cancellationToken).ConfigureAwait(false);
            return new ExchangeTokenResult.Success(token.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AuthenticationFailedException ex)
        {
            return new ExchangeTokenResult.Failure(
                $"Could not sign in to Exchange Online as app {appId} in tenant {tenantId}: {ex.Message} — check that the app's " +
                "federated identity credential names this Worker's managed identity (issuer = the managed identity's tenant, " +
                "audience api://AzureADTokenExchange) and that the app holds Exchange.ManageAsApp. Transient sign-in faults " +
                "surface the same way; a retry may succeed.");
        }
        catch (Exception ex)
        {
            // Callers promise not to throw for sign-in problems (code review S6).
            return new ExchangeTokenResult.Failure(
                $"Unexpected {ex.GetType().Name} signing in to Exchange Online as app {appId} in tenant {tenantId}: {ex.Message}");
        }
    }

    private static Func<string, string, TokenCredential> ResolveFrom(WorkerDataverseCredentialFactory credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        return credentials.CreateManagedIdentityFederatedCredential;
    }
}

/// <summary>Outcome of <see cref="ExchangeAdminTokenSource.GetTokenAsync"/>.</summary>
public abstract record ExchangeTokenResult
{
    private ExchangeTokenResult() { }

    /// <summary>The token. Never log it.</summary>
    public sealed record Success(string AccessToken) : ExchangeTokenResult
    {
        /// <inheritdoc/>
        public override string ToString() => "Success { AccessToken = *** }";
    }

    /// <summary>No token; <paramref name="Diagnostic"/> says why.</summary>
    public sealed record Failure(string Diagnostic) : ExchangeTokenResult;
}
