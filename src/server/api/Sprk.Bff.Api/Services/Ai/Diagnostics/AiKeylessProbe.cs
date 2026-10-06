using System.Net.Http.Json;
using Azure.AI.DocumentIntelligence;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Diagnostics;
using Spaarke.Contracts.Provisioning;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Ai.Safety;
using Sprk.Bff.Api.Services.Ai.Sessions;

namespace Sprk.Bff.Api.Services.Ai.Diagnostics;

/// <summary>
/// The AI-owned half of the keyless proof (task 230b, owner D13) — see <see cref="IAiKeylessProbe"/>.
/// </summary>
/// <remarks>
/// <para><b>Same credential and endpoints as production, new clients.</b> Each probe builds its SDK client from the
/// DI <see cref="TokenCredential"/> (the stamp's user-assigned managed identity) and the configuration keys the
/// production services read. It does not inject <c>IOpenAiClient</c> or <c>TextExtractorService</c>: those are
/// registered only under <c>DocumentIntelligence:Enabled</c>, and this probe sits behind an endpoint that is mapped
/// unconditionally (ADR-032 / bff-extensions §F.1). Where production would use a key, the probe reports
/// <see cref="KeylessProofContract.Outcomes.KeyCredential"/> instead of calling — the key-selection rules mirrored here are
/// those of <c>AiModule.BuildInnerClient</c>, <c>OpenAiClient</c>, <c>TextExtractorService</c>,
/// <see cref="SearchClientFactory"/> and <see cref="ContentSafetyAuthHandler"/>.</para>
/// <para><b>Side effects and cost.</b> Every call is read-only. Chat is capped at 16 output tokens and the
/// embedding input is three words; Prompt Shield and groundedness are one text record each; Document
/// Intelligence's resource-details read is free. Together a fraction of a cent per H13 run.</para>
/// <para>Content Safety goes through <see cref="ContentSafetyProbeHttpClientName"/>: the production client's timeout
/// is the Prompt Shield budget (~550 ms), short enough that a probe through it would report a timeout instead of the
/// identity's answer. The probe client carries the same base address and the same <see cref="ContentSafetyAuthHandler"/>.</para>
/// </remarks>
public sealed class AiKeylessProbe : IAiKeylessProbe
{
    /// <summary>Named HttpClient for the Content Safety probes (registered by <c>AiSafetyModule</c>).</summary>
    public const string ContentSafetyProbeHttpClientName = "ContentSafetyKeylessProbe";

    /// <summary>The Cosmos container the probe reads the metadata of — declared by <c>cosmos-db.bicep</c>.</summary>
    internal const string CosmosProbeContainer = "sessions";

    private const string ProbeText = "Spaarke provisioning keyless proof.";

    private readonly IConfiguration _configuration;
    private readonly DocumentIntelligenceOptions _docIntel;
    private readonly TokenCredential _credential;
    private readonly CosmosClient _cosmosClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AiKeylessProbe> _logger;

    public AiKeylessProbe(
        IConfiguration configuration,
        IOptions<DocumentIntelligenceOptions> docIntelOptions,
        TokenCredential credential,
        CosmosClient cosmosClient,
        IHttpClientFactory httpClientFactory,
        ILogger<AiKeylessProbe> logger)
    {
        _configuration = configuration;
        _docIntel = docIntelOptions.Value;
        _credential = credential;
        _cosmosClient = cosmosClient;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<KeylessProbeResult>> ProbeAsync(CancellationToken cancellationToken)
    {
        var probes = new[]
        {
            OpenAiChatAsync(cancellationToken),
            OpenAiEmbeddingsAsync(cancellationToken),
            DocumentIntelligenceAsync(cancellationToken),
            AiSearchAsync(cancellationToken),
            CosmosAsync(cancellationToken),
            BlobStorageAsync(cancellationToken),
            ContentSafetyAsync(KeylessProofContract.Services.ContentSafetyPromptShield,
                $"{PromptShieldService.ApiPath}?api-version={PromptShieldService.ApiVersion}",
                new { userPrompt = ProbeText, documents = Array.Empty<string>() }, cancellationToken),
            ContentSafetyAsync(KeylessProofContract.Services.ContentSafetyGroundedness,
                GroundednessCheckService.GroundednessPath,
                new { domain = "Generic", task = "Summarization", text = ProbeText, groundingSources = new[] { ProbeText }, reasoning = false },
                cancellationToken),
        };
        return await Task.WhenAll(probes).ConfigureAwait(false);
    }

    private Task<KeylessProbeResult> OpenAiChatAsync(CancellationToken ct)
    {
        const string service = KeylessProofContract.Services.OpenAiChat;
        if (!string.IsNullOrWhiteSpace(_configuration["AzureOpenAI:ApiKey"]))
            return Task.FromResult(KeylessProbeRunner.KeyCredential(service, "AzureOpenAI:ApiKey"));
        var endpoint = _configuration["AzureOpenAI:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
            return Task.FromResult(KeylessProbeRunner.NotConfigured(service, "AzureOpenAI:Endpoint"));
        var model = _configuration["AzureOpenAI:ChatModelName"];
        if (string.IsNullOrWhiteSpace(model))
            return Task.FromResult(KeylessProbeRunner.NotConfigured(service, "AzureOpenAI:ChatModelName"));

        return KeylessProbeRunner.RunAsync(service, async token =>
        {
            var chat = new AzureOpenAIClient(new Uri(endpoint), _credential).GetChatClient(model);
            var result = await chat.CompleteChatAsync(
                [new UserChatMessage("Reply with the single word OK.")],
                new ChatCompletionOptions { MaxOutputTokenCount = 16 },
                token).ConfigureAwait(false);
            return result.GetRawResponse().Status;
        }, _logger, ct);
    }

    private Task<KeylessProbeResult> OpenAiEmbeddingsAsync(CancellationToken ct)
    {
        const string service = KeylessProofContract.Services.OpenAiEmbeddings;
        if (!string.IsNullOrWhiteSpace(_docIntel.OpenAiKey))
            return Task.FromResult(KeylessProbeRunner.KeyCredential(service, "DocumentIntelligence:OpenAiKey"));
        if (string.IsNullOrWhiteSpace(_docIntel.OpenAiEndpoint))
            return Task.FromResult(KeylessProbeRunner.NotConfigured(service, "DocumentIntelligence:OpenAiEndpoint"));
        if (string.IsNullOrWhiteSpace(_docIntel.EmbeddingModel))
            return Task.FromResult(KeylessProbeRunner.NotConfigured(service, "DocumentIntelligence:EmbeddingModel"));

        return KeylessProbeRunner.RunAsync(service, async token =>
        {
            var embeddings = new AzureOpenAIClient(new Uri(_docIntel.OpenAiEndpoint), _credential)
                .GetEmbeddingClient(_docIntel.EmbeddingModel);
            var result = await embeddings.GenerateEmbeddingAsync(ProbeText, cancellationToken: token).ConfigureAwait(false);
            return result.GetRawResponse().Status;
        }, _logger, ct);
    }

    private Task<KeylessProbeResult> DocumentIntelligenceAsync(CancellationToken ct)
    {
        const string service = KeylessProofContract.Services.DocumentIntelligence;
        if (!string.IsNullOrWhiteSpace(_docIntel.DocIntelKey))
            return Task.FromResult(KeylessProbeRunner.KeyCredential(service, "DocumentIntelligence:DocIntelKey"));
        if (string.IsNullOrWhiteSpace(_docIntel.DocIntelEndpoint))
            return Task.FromResult(KeylessProbeRunner.NotConfigured(service, "DocumentIntelligence:DocIntelEndpoint"));

        return KeylessProbeRunner.RunAsync(service, async token =>
        {
            // Resource details: a free metadata read that still requires the data-plane role.
            var admin = new DocumentIntelligenceAdministrationClient(new Uri(_docIntel.DocIntelEndpoint), _credential);
            var details = await admin.GetResourceDetailsAsync(token).ConfigureAwait(false);
            return details.GetRawResponse().Status;
        }, _logger, ct);
    }

    private Task<KeylessProbeResult> AiSearchAsync(CancellationToken ct)
    {
        const string service = KeylessProofContract.Services.AiSearch;
        foreach (var keySetting in new[] { "DocumentIntelligence:AiSearchKey", "AiSearch:ReferencesApiKey", "RecordSync:AiSearchApiKey" })
        {
            if (!SearchClientFactory.UseManagedIdentity(_configuration, _configuration[keySetting]))
                return Task.FromResult(KeylessProbeRunner.KeyCredential(service, keySetting));
        }
        if (string.IsNullOrWhiteSpace(_docIntel.AiSearchEndpoint))
            return Task.FromResult(KeylessProbeRunner.NotConfigured(service, "DocumentIntelligence:AiSearchEndpoint"));
        var index = _configuration["AiSearch:KnowledgeIndexName"];
        if (string.IsNullOrWhiteSpace(index))
            index = new AiSearchOptions().KnowledgeIndexName;

        return KeylessProbeRunner.RunAsync(service, async token =>
        {
            // A document COUNT — no content leaves the service, and no query reaches the index.
            var client = SearchClientFactory.CreateSearchClient(
                new Uri(_docIntel.AiSearchEndpoint), index, apiKey: null, _configuration, _credential);
            var count = await client.GetDocumentCountAsync(token).ConfigureAwait(false);
            return count.GetRawResponse().Status;
        }, _logger, ct);
    }

    private Task<KeylessProbeResult> CosmosAsync(CancellationToken ct)
    {
        const string service = KeylessProofContract.Services.Cosmos;
        if (string.IsNullOrWhiteSpace(_configuration["CosmosPersistence:Endpoint"]))
            return Task.FromResult(KeylessProbeRunner.NotConfigured(service, "CosmosPersistence:Endpoint"));
        var database = _configuration["CosmosPersistence:DatabaseName"] ?? "spaarke-ai";

        return KeylessProbeRunner.RunAsync(service, async token =>
        {
            // Container metadata (readMetadata) — reads no item.
            var response = await _cosmosClient.GetContainer(database, CosmosProbeContainer)
                .ReadContainerAsync(cancellationToken: token).ConfigureAwait(false);
            return (int)response.StatusCode;
        }, _logger, ct);
    }

    private Task<KeylessProbeResult> BlobStorageAsync(CancellationToken ct)
    {
        const string service = KeylessProofContract.Services.BlobStorage;
        var endpoint = _configuration[SessionFileBlobStore.BlobEndpointConfigKey];
        if (string.IsNullOrWhiteSpace(endpoint))
            return Task.FromResult(KeylessProbeRunner.NotConfigured(service, SessionFileBlobStore.BlobEndpointConfigKey));

        return KeylessProbeRunner.RunAsync(service, async token =>
        {
            var endpointUri = SessionFileBlobStore.ValidateConfiguration(
                endpoint, _configuration[SessionFileBlobStore.ContainerNameConfigKey], out var containerName);
            var container = new BlobServiceClient(endpointUri, _credential).GetBlobContainerClient(containerName);
            // One listing page of at most one name: proves blob read access; the name is discarded.
            await foreach (var page in container.GetBlobsAsync(cancellationToken: token).AsPages(pageSizeHint: 1).ConfigureAwait(false))
            {
                return page.GetRawResponse().Status;
            }
            return null;
        }, _logger, ct);
    }

    private Task<KeylessProbeResult> ContentSafetyAsync(string service, string relativeUri, object body, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_configuration[ContentSafetyAuthHandler.ApiKeyConfigKey])
            && !_configuration.GetValue<bool>(ContentSafetyAuthHandler.ManagedIdentityEnabledConfigKey))
        {
            return Task.FromResult(KeylessProbeRunner.KeyCredential(service, ContentSafetyAuthHandler.ApiKeyConfigKey));
        }

        var client = _httpClientFactory.CreateClient(ContentSafetyProbeHttpClientName);
        if (client.BaseAddress is null)
            return Task.FromResult(KeylessProbeRunner.NotConfigured(service, "AiSafety:ContentSafety:Endpoint"));

        return KeylessProbeRunner.RunAsync(service, async token =>
        {
            using var response = await client.PostAsJsonAsync(relativeUri, body, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return (int)response.StatusCode;
        }, _logger, ct);
    }
}
