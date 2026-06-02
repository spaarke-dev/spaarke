using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai.Insights;

/// <summary>
/// Stable, deterministic cache-key composer for Insights-mode playbook execution results
/// (D-P13, SPEC §3.1). The same <c>(playbookId, subject, parameters, accessibleScopeHash)</c>
/// tuple MUST produce the same key across processes, restarts, and Redis nodes so the
/// distributed Inference cache is coherent under scale-out per ADR-009.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a separate composer?</b> The cache key is part of the cache contract — changes
/// to its format invalidate every cached entry. Isolating it from the cache implementation
/// makes the contract explicit and unit-testable in isolation, and prevents the cache
/// implementation from accidentally drifting (e.g., by serialising parameters with
/// different <see cref="JsonSerializerOptions"/>).
/// </para>
/// <para>
/// <b>Key format</b>: <c>insights:playbook:{base64-sha256-of-canonical-tuple}</c>.
/// The "insights:playbook:" prefix matches the SDAP cache-naming convention used by
/// <see cref="EmbeddingCache"/> (<c>sdap:embedding:</c>) and the chat session manager
/// (<c>chat:session:</c>) — see ADR-014 namespacing.
/// </para>
/// <para>
/// <b>Stability requirements</b>:
/// <list type="bullet">
/// <item>Parameters are serialised in <b>sorted key order</b> so semantically-identical
/// dictionaries with different insertion orders produce the same key.</item>
/// <item>Parameter values are serialised via <see cref="JsonSerializer"/> with
/// <see cref="JsonSerializerOptions.Default"/> — invariant culture, no indentation,
/// no extra whitespace.</item>
/// <item>Subject and accessibleScopeHash are normalised with
/// <see cref="string.Trim()"/> only — case is preserved because subjects like
/// <c>matter:M-1234</c> are case-sensitive identifiers per SPEC §3.4.</item>
/// </list>
/// </para>
/// <para>
/// <b>Why include accessibleScopeHash?</b> Per DEP-3, a user's accessible-scope set can
/// change within-tenant (matter assignment changes, role removed). Two otherwise-identical
/// invocations from users with different access surfaces MUST NOT share a cached Inference
/// — otherwise we'd leak grounding data the second user can't see. The hash gives us
/// per-access-surface partitioning without putting the full scope list in the key.
/// </para>
/// </remarks>
public static class InsightsPlaybookCacheKey
{
    /// <summary>Cache-key prefix per SDAP naming convention (ADR-014).</summary>
    public const string Prefix = "insights:playbook:";

    /// <summary>
    /// Compose a stable cache key for a playbook invocation.
    /// </summary>
    /// <param name="playbookId">Insights-mode playbook identifier.</param>
    /// <param name="subject">The subject the playbook is being asked about
    /// (e.g., <c>matter:M-1234</c>). Required, trimmed before hashing.</param>
    /// <param name="parameters">Playbook parameters (template substitution values).
    /// May be null or empty — both produce the same key. Keys are sorted for stability.</param>
    /// <param name="accessibleScopeHash">Hash of the caller's accessible-scope set
    /// (per DEP-3). Required so within-tenant access changes invalidate the cache.</param>
    /// <returns>Stable cache key string, e.g.
    /// <c>insights:playbook:abc123...=</c>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="subject"/>
    /// or <paramref name="accessibleScopeHash"/> is null or whitespace.</exception>
    public static string Compose(
        Guid playbookId,
        string subject,
        IReadOnlyDictionary<string, string>? parameters,
        string accessibleScopeHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessibleScopeHash);

        // Canonicalise parameters: sort by key, JSON-serialise the resulting ordered dictionary.
        // We use SortedDictionary<,> rather than .OrderBy().ToDictionary() to be explicit
        // about ordinal-ordering semantics (avoids culture-sensitive sort surprises).
        var sortedParameters = parameters is not null && parameters.Count > 0
            ? new SortedDictionary<string, string>(parameters.ToDictionary(kvp => kvp.Key, kvp => kvp.Value), StringComparer.Ordinal)
            : new SortedDictionary<string, string>(StringComparer.Ordinal);

        var parametersJson = JsonSerializer.Serialize(sortedParameters);

        // Canonical tuple form. The "|" delimiter is unambiguous because:
        //   - playbookId is a Guid (no pipes)
        //   - subject is trimmed (no leading/trailing whitespace)
        //   - parametersJson is JSON ({ ... }) — pipes inside JSON strings would be escaped,
        //     but the tuple as a whole is hashed so even if a value contained a pipe the
        //     hash would differ.
        //   - accessibleScopeHash is a hex/base64 hash string (no pipes)
        var canonical = $"{playbookId:N}|{subject.Trim()}|{parametersJson}|{accessibleScopeHash.Trim()}";

        // SHA256 + Base64 — same approach as EmbeddingCache.ComputeContentHash.
        // 32 bytes -> 44-char Base64 string -> 62-char total key (well under Redis 512MB key limit).
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        var hashB64 = Convert.ToBase64String(hashBytes);

        return $"{Prefix}{hashB64}";
    }
}
