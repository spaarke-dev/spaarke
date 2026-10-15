// -----------------------------------------------------------------------------
// PropagationWait.cs
//
// T186 audit (first live end-to-end provisioning run): a handler creates or
// changes an Entra / Dataverse object and its very NEXT call fails because the
// platform has not replicated yet; the handler then treats that transient as
// fatal. H3 creates the stamp app registration + its federated identity
// credentials (FICs); H10 creates the Dataverse application user; H6 / H7 are
// the first to USE them. These two helpers make the consumers wait the
// propagation out — bounded, on a narrow predicate, options-driven — and
// otherwise leave every failure mapping exactly as it was.
//
// Reference shape (copied, not shared — different layers): the bounded retry in
// GraphAppRegistrationProvisioner.RetryWhileReplicatingAsync and the single
// shared time budget of SecretClientKvWriter.ExecuteEntryWithRbacRetryAsync.
//
//   EntraPropagationRetry.GetTokenAsync
//     The FIRST client-assertion / client-credential token exchange for a
//     just-created app can fail with AADSTS70025 (no federated identity
//     credentials configured yet), AADSTS700211/700212/700213 (no matching
//     federated identity record yet) or AADSTS700016 (application not found in
//     the directory yet). Exactly those codes (matched on the error-code text of
//     the AuthenticationFailedException / MsalException chain) are retried for
//     WorkerCredentialSelectionOptions.EntraPropagationTimeout; any other auth
//     error — and an exhausted budget — rethrows the last exception unchanged,
//     so the callers' existing "Token acquisition failed" -> AuthFailure mapping
//     is untouched.
//
//   DataverseAppUserPropagationGate
//     A just-created application user (H10) and its security role take time to
//     become effective: the first Dataverse read can answer 401/403. The gate
//     retries ONLY 401/403, ONLY until the first non-401/403 answer (which
//     proves the identity is effective — a later 403 is real), against ONE
//     time budget for the whole operation. An exhausted budget returns the
//     401/403 response itself, so the caller's existing classification stands.
//
// §11: existing — none of the Core handlers waits on identity propagation
// (grep: AADSTS / 401 / 403 retry); extension — the bounded-retry pattern lives
// in Handlers/EntraAppReg (Graph, a different layer and predicate) and
// Handlers/KvSecretsPopulation (ARM RBAC), neither reachable from H6/H7 without
// a cross-handler dependency; cost of doing nothing — the T186 run fails H6/H7
// on a race that resolves itself within minutes, stranding the run Resumable.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using Microsoft.Identity.Client;

namespace Sprk.Provisioning.ControlPlane.Handlers.Credentials;

/// <summary>Bounded wait for Entra to propagate a just-created app / FIC before the first token exchange.</summary>
internal static partial class EntraPropagationRetry
{
    /// <summary>
    /// The Entra error codes that mean "not replicated yet": AADSTS70025 (no federated identity credentials),
    /// AADSTS700211/700212/700213 (no matching federated identity record), AADSTS700016 (application not found).
    /// </summary>
    [GeneratedRegex(@"\bAADSTS(?:70025|700211|700212|700213|700016)\b", RegexOptions.CultureInvariant)]
    private static partial Regex TransientCodePattern();

    /// <summary>
    /// True for an <see cref="AuthenticationFailedException"/> / <see cref="MsalException"/> whose message (or an inner
    /// exception's) carries one of the propagation error codes. Anything else — including other AADSTS codes — is false.
    /// </summary>
    internal static bool IsPropagationFailure(Exception exception)
    {
        if (exception is not (AuthenticationFailedException or MsalException))
        {
            return false;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (TransientCodePattern().IsMatch(current.Message))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// <c>credential.GetTokenAsync</c> that waits out Entra propagation. Throws the last exception when it is not a
    /// propagation failure or the budget (<see cref="WorkerCredentialSelectionOptions.EntraPropagationTimeout"/>) is spent.
    /// </summary>
    internal static async Task<AccessToken> GetTokenAsync(
        TokenCredential credential,
        TokenRequestContext requestContext,
        WorkerCredentialSelectionOptions options,
        TimeProvider timeProvider,
        ILogger logger,
        string operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        var started = timeProvider.GetTimestamp();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await credential.GetTokenAsync(requestContext, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && IsPropagationFailure(ex))
            {
                var elapsed = timeProvider.GetElapsedTime(started);
                if (elapsed + options.EntraPropagationRetryInterval > options.EntraPropagationTimeout)
                {
                    logger.LogWarning(
                        "{Operation}: Entra propagation budget {Budget} spent after {Attempts} attempt(s); giving up: {Message}",
                        operation, options.EntraPropagationTimeout, attempt, ex.Message);
                    throw;
                }

                logger.LogInformation(
                    "{Operation}: token exchange hit an Entra propagation error (attempt {Attempt}, {Elapsed} of " +
                    "{Budget}); retrying in {Interval}: {Message}",
                    operation, attempt, elapsed, options.EntraPropagationTimeout,
                    options.EntraPropagationRetryInterval, ex.Message);
                await Task.Delay(options.EntraPropagationRetryInterval, timeProvider, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}

/// <summary>
/// One time budget, shared by every call of one operation, that retries a Dataverse 401/403 until the first answer that
/// is neither (after which the identity is proven effective and a later 401/403 is genuine).
/// </summary>
internal sealed class DataverseAppUserPropagationGate
{
    private readonly WorkerCredentialSelectionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly string _operation;
    private long? _started;
    private bool _established;

    internal DataverseAppUserPropagationGate(
        WorkerCredentialSelectionOptions options, TimeProvider timeProvider, ILogger logger, string operation)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
        _operation = operation;
    }

    /// <summary>
    /// Sends the (idempotent) request built by <paramref name="buildRequest"/>, rebuilding it for each retry. The caller
    /// owns — and disposes — the returned response. Transport faults propagate exactly as <c>SendAsync</c> raises them.
    /// </summary>
    internal async Task<HttpResponseMessage> SendAsync(
        HttpClient httpClient, Func<HttpRequestMessage> buildRequest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(buildRequest);

        for (var attempt = 1; ; attempt++)
        {
            using var request = buildRequest();
            var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (_established || response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
            {
                _established = true;
                return response;
            }

            _started ??= _timeProvider.GetTimestamp();
            var elapsed = _timeProvider.GetElapsedTime(_started.Value);
            if (elapsed + _options.DataverseAppUserPropagationRetryInterval > _options.DataverseAppUserPropagationTimeout)
            {
                _logger.LogWarning(
                    "{Operation}: Dataverse still answers {Status} after the {Budget} application-user propagation " +
                    "budget ({Attempts} attempt(s)); surfacing it.",
                    _operation, (int)response.StatusCode, _options.DataverseAppUserPropagationTimeout, attempt);
                return response;
            }

            _logger.LogInformation(
                "{Operation}: Dataverse answered {Status} on the first call (attempt {Attempt}, {Elapsed} of {Budget}); the " +
                "application user / security role may not be effective yet — retrying in {Interval}.",
                _operation, (int)response.StatusCode, attempt, elapsed, _options.DataverseAppUserPropagationTimeout,
                _options.DataverseAppUserPropagationRetryInterval);
            response.Dispose();
            await Task.Delay(_options.DataverseAppUserPropagationRetryInterval, _timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
