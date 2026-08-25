using Azure;
using Azure.Core;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;

namespace Sprk.Bff.Api.Infrastructure.Auth;

/// <summary>
/// Single place where every Azure AI Search client in the BFF decides between Entra (managed
/// identity) and admin-key authentication.
/// </summary>
/// <remarks>
/// <para>
/// <b>Component Justification (CLAUDE.md §11)</b>:
/// (1) <i>Existing</i> — <see cref="ManagedIdentityCredentialFactory"/> builds the
/// <see cref="TokenCredential"/>, but it cannot help here: <c>SearchClient</c> exposes two
/// mutually exclusive constructor overloads (<see cref="TokenCredential"/> vs
/// <see cref="AzureKeyCredential"/>), so the choice has to be made where the client is built, not
/// where the credential is made.
/// (2) <i>Extension</i> — there is no natural existing host. The five call sites live in a DI
/// module, a citations provider, a record-matching service, an index-sync service and a background
/// job, which share no base type.
/// (3) <i>Cost-of-doing-nothing</i> — five independent copies of this branch. That is precisely the
/// shape that made <c>BFF-API-ClientSecret</c> unfixable in one place (ADR-028 A4: <i>"seven call
/// sites each rolling their own credential handling is what made the previous state
/// unfixable"</i>), and a partial migration would leave some AI Search consumers on the admin key
/// while the project reported the key retired.
/// </para>
/// <para>
/// <b>Selection rule</b>, deliberately identical to
/// <see cref="Services.Ai.Safety.ContentSafetyAuthHandler"/> so the platform has one shape:
/// <c>AiSearch:ManagedIdentity:Enabled = true</c> <i>OR</i> no key configured → Entra bearer;
/// otherwise the admin key. The flag exists so an environment can move to Entra <b>while the key is
/// still in place as a rollback</b> — the staged transition this project's NFR-06 requires. It
/// defaults to <c>false</c>, so behaviour is unchanged until an operator flips it.
/// </para>
/// <para>
/// <b>Service prerequisite.</b> Entra auth on an Azure AI Search service is OFF by default:
/// a service with <c>authOptions: apiKeyOnly</c> returns <b>HTTP 403 to every bearer token
/// regardless of role assignments</b>, so a <c>Search Index Data Reader/Contributor</c> grant on
/// such a service is inert. The service must be <c>aadOrApiKey</c> (or key-disabled) first.
/// <c>spaarke-search-dev</c> was switched to <c>aadOrApiKey</c> on 2026-08-22
/// (spaarke-auth-v4-dataverse-MI task 053); verified by an Entra-token probe returning 200 where it
/// had returned 403.
/// </para>
/// <para>
/// <b>Roles.</b> Reading index documents needs <c>Search Index Data Reader</c>; writing/indexing
/// needs <c>Search Index Data Contributor</c>. Subscription <c>Owner</c> does <b>not</b> grant
/// either — those are dataActions, and Owner carries none.
/// </para>
/// </remarks>
public static class SearchClientFactory
{
    /// <summary>Configuration key for the AI Search managed-identity opt-in flag.</summary>
    public const string ManagedIdentityEnabledConfigKey = "AiSearch:ManagedIdentity:Enabled";

    /// <summary>
    /// True when the Entra path should be used: the opt-in flag is set, or no admin key is
    /// configured at all (an absent key means Entra, never an unauthenticated call).
    /// </summary>
    public static bool UseManagedIdentity(IConfiguration configuration, string? apiKey)
        => configuration.GetValue<bool>(ManagedIdentityEnabledConfigKey)
           || string.IsNullOrWhiteSpace(apiKey);

    /// <summary>Builds a <see cref="SearchClient"/> for one index using the selected credential.</summary>
    public static SearchClient CreateSearchClient(
        Uri endpoint,
        string indexName,
        string? apiKey,
        IConfiguration configuration,
        TokenCredential credential)
        => UseManagedIdentity(configuration, apiKey)
            ? new SearchClient(endpoint, indexName, credential)
            : new SearchClient(endpoint, indexName, new AzureKeyCredential(apiKey!));

    /// <summary>Builds a <see cref="SearchIndexClient"/> using the selected credential.</summary>
    public static SearchIndexClient CreateIndexClient(
        Uri endpoint,
        string? apiKey,
        IConfiguration configuration,
        TokenCredential credential)
        => UseManagedIdentity(configuration, apiKey)
            ? new SearchIndexClient(endpoint, credential)
            : new SearchIndexClient(endpoint, new AzureKeyCredential(apiKey!));
}
