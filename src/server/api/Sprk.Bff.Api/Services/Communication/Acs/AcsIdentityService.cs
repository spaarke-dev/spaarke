using Azure;
using Azure.Communication;
using Azure.Communication.Identity;
using Azure.Core;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Acs;

/// <summary>
/// ACS trusted-service implementation of the identity plane (FR-03). Wraps
/// <see cref="CommunicationIdentityClient"/> (constructed from the DI-injected central
/// <see cref="TokenCredential"/> — ADR-028 / NFR-05, no inline credential) and persists the
/// <c>communicationUserId ↔ Dataverse</c> mapping via the canonical <see cref="IGenericEntityService"/>
/// (no direct Dataverse SDK). Keeps every <c>Azure.Communication.*</c> type inside this boundary (ADR-045).
/// </summary>
public sealed class AcsIdentityService : IAcsIdentityService
{
    /// <summary>The ACS identity-map column on both <c>systemuser</c> and <c>contact</c> (as-built, task 005).</summary>
    internal const string CommunicationUserIdColumn = "sprk_communicationuserid";

    private static readonly TimeSpan MinValidity = TimeSpan.FromHours(1);
    private static readonly TimeSpan MaxValidity = TimeSpan.FromHours(24);

    private readonly CommunicationIdentityClient _identityClient;
    private readonly IGenericEntityService _dataverse;
    private readonly TimeSpan _defaultValidity;
    private readonly ILogger<AcsIdentityService> _logger;

    public AcsIdentityService(
        CommunicationIdentityClient identityClient,
        IGenericEntityService dataverse,
        IOptions<AcsOptions> options,
        ILogger<AcsIdentityService> logger)
    {
        _identityClient = identityClient;
        _dataverse = dataverse;
        _logger = logger;

        var configuredHours = options.Value.DefaultTokenValidityHours;
        _defaultValidity = ClampValidity(TimeSpan.FromHours(configuredHours <= 0 ? 24 : configuredHours));
    }

    /// <inheritdoc />
    public async Task<AcsIdentityMapping> EnsureIdentityAsync(ParticipantReference participant, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(participant);

        // 1. Idempotency — reuse an existing mapping if present.
        var record = await _dataverse.RetrieveAsync(
            participant.EntityLogicalName,
            participant.RecordId,
            new[] { CommunicationUserIdColumn },
            ct);

        var existing = record?.GetAttributeValue<string>(CommunicationUserIdColumn);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            _logger.LogDebug(
                "ACS identity already mapped for {Entity} {RecordId}; reusing.",
                participant.EntityLogicalName, participant.RecordId);

            return new AcsIdentityMapping
            {
                CommunicationUserId = existing,
                Participant = participant,
                WasCreated = false
            };
        }

        // 2. Create the ACS identity (trusted service) and persist the mapping via the canonical
        //    Dataverse interface (no direct SDK).
        Response<CommunicationUserIdentifier> created = await _identityClient.CreateUserAsync(ct);
        var communicationUserId = created.Value.Id;

        await _dataverse.UpdateAsync(
            participant.EntityLogicalName,
            participant.RecordId,
            new Dictionary<string, object> { [CommunicationUserIdColumn] = communicationUserId },
            ct);

        _logger.LogInformation(
            "Created + mapped ACS identity for {Entity} {RecordId}.",
            participant.EntityLogicalName, participant.RecordId);

        return new AcsIdentityMapping
        {
            CommunicationUserId = communicationUserId,
            Participant = participant,
            WasCreated = true
        };
    }

    /// <inheritdoc />
    public async Task<AcsChatToken> MintChatTokenAsync(string communicationUserId, TimeSpan? validity = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(communicationUserId))
            throw new ArgumentException("communicationUserId is required.", nameof(communicationUserId));

        var user = new CommunicationUserIdentifier(communicationUserId);
        var scopes = new[] { CommunicationTokenScope.Chat }; // chat scope ONLY (NFR — uniform minting)
        var expiresIn = ClampValidity(validity ?? _defaultValidity);

        Response<AccessToken> token = await _identityClient.GetTokenAsync(user, scopes, expiresIn, ct);

        return new AcsChatToken
        {
            CommunicationUserId = communicationUserId,
            Token = token.Value.Token,
            ExpiresOn = token.Value.ExpiresOn
        };
    }

    private static TimeSpan ClampValidity(TimeSpan requested)
    {
        if (requested < MinValidity) return MinValidity;
        if (requested > MaxValidity) return MaxValidity;
        return requested;
    }
}
