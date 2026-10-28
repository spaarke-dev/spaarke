using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// The caller's <c>WhoAmI()</c> answer, remembered for ONE request and ONE Dataverse credential (ontology task 170, D-196):
/// recording one decision asked Dataverse <c>WhoAmI()</c> 35 times for the same person (App Insights operation
/// <c>855d8644…</c>, 2026-10-28: 3.0 s of a 24 s commit), because every caller-scoped question re-proves who the caller is.
/// </summary>
/// <remarks>
/// <para><b>What is remembered, and for how long.</b> Only a SUCCESSFUL answer that names a user (a non-empty <c>UserId</c>);
/// a failure is never remembered, so the next question asks again. The store is the request's own
/// <see cref="HttpContext.Items"/>: it is created with the request and dropped with it, so an answer is never shared
/// across requests, and outside a request (background work, or after the request has ended) nothing is remembered and every
/// question asks Dataverse as before. Nothing here is static data: the static members hold no answers.</para>
///
/// <para><b>Keyed by the exact credential the answer was obtained with.</b> The key is a SHA-256 of the Dataverse bearer
/// token <c>WhoAmI()</c> ran under, and <c>WhoAmI()</c> answers for that token's subject and nothing else. So an answer is
/// only ever returned for the same credential: a different caller, or the application's own (app-only) credential, has a
/// different token and misses. The token itself is not kept. The two OBO paths that ask, <see cref="ExternalAccess.CallerRecordAccessProbe"/>
/// and <see cref="DataverseUserClient"/>, exchange the same caller assertion through the same confidential client (one MSAL
/// user-token cache, <c>OrderedCredentialClientProvider</c>), so within a request they hold the same token and share one answer.
/// If they ever hold different tokens, each asks once: more calls, never a wrong answer.</para>
///
/// <para><b>Access results are NOT remembered.</b> Only who the caller is. <c>RetrievePrincipalAccess</c>, privilege questions
/// and record reads are still asked every time.</para>
///
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <c>DataverseWebApiService._entitySetNameCache</c>
/// (a cache of a different, caller-independent answer) and <see cref="ExternalAccess.CallerRecordAccessProbe.GetCallerRightsForRecordsAsync"/>,
/// which already asks <c>WhoAmI</c> once per set, but only within one call. Extension: the probe and the user client each read and
/// write this memo; it is a static helper with no DI registration and no state of its own, because the two classes that ask are
/// separate and must share one answer. Cost of doing nothing: every decision recorded spends about 3 s re-asking who the
/// caller is (owner UAT round 2 item 3).</para>
/// </remarks>
internal static class CallerWhoAmIMemo
{
    /// <summary>The <see cref="HttpContext.Items"/> key of this request's answers. A private object, so no other code can reach it.</summary>
    private static readonly object ItemsKey = new();

    /// <summary>
    /// Reads the framework's per-request context. <see cref="HttpContextAccessor"/> keeps the current context in a
    /// per-async-flow slot that every instance shares and that the host clears when the request ends, so a fresh instance
    /// sees the same request the injected one does. The probe uses this rather than a new constructor parameter, because its
    /// constructor is pinned by Moq-built test doubles that pass an exact argument list.
    /// </summary>
    private static readonly HttpContextAccessor Accessor = new();

    /// <summary>The current request, or <see langword="null"/> outside one (and once it has ended).</summary>
    internal static HttpContext? CurrentRequest => Accessor.HttpContext;

    /// <summary>
    /// The caller's <c>systemuserid</c> from an answer already obtained in <paramref name="request"/> under exactly
    /// <paramref name="credential"/>. False when there is no request, no credential, or no such answer.
    /// </summary>
    internal static bool TryGetUserId(HttpContext? request, string? credential, out Guid userId)
    {
        userId = Guid.Empty;
        if (!TryGetAnswer(request, credential, out var answer))
            return false;

        userId = answer.UserId;
        return true;
    }

    /// <summary>
    /// The full <c>WhoAmI()</c> body (<c>UserId</c>, <c>BusinessUnitId</c>, <c>OrganizationId</c>) already obtained in
    /// <paramref name="request"/> under exactly <paramref name="credential"/>. Each call gets its own freshly parsed copy:
    /// a parsed JSON document is not safe to read from two threads at once, and one request can ask in parallel.
    /// </summary>
    internal static bool TryGetBody(HttpContext? request, string? credential, out JsonElement body)
    {
        body = default;
        if (!TryGetAnswer(request, credential, out var answer))
            return false;

        using var document = JsonDocument.Parse(answer.RawJson);
        body = document.RootElement.Clone();
        return true;
    }

    /// <summary>
    /// Remembers a <c>WhoAmI()</c> answer for the rest of <paramref name="request"/>, under exactly <paramref name="credential"/>.
    /// Ignored unless the answer names a user (a non-empty <c>UserId</c>): a failed or empty answer is never remembered. A no-op
    /// outside a request.
    /// </summary>
    internal static void Remember(HttpContext? request, string? credential, JsonElement whoAmIBody)
    {
        if (request is null || string.IsNullOrEmpty(credential) || !TryReadUserId(whoAmIBody, out var userId))
            return;

        ConcurrentDictionary<string, Answer> answers;
        lock (request.Items)
        {
            if (request.Items.TryGetValue(ItemsKey, out var held) && held is ConcurrentDictionary<string, Answer> existing)
            {
                answers = existing;
            }
            else
            {
                answers = new ConcurrentDictionary<string, Answer>(StringComparer.Ordinal);
                request.Items[ItemsKey] = answers;
            }
        }

        answers.TryAdd(KeyOf(credential), new Answer(userId, whoAmIBody.GetRawText()));
    }

    /// <summary>Whether a <see cref="DataverseUserClient"/> request is the caller's <c>WhoAmI</c> (a GET of
    /// <c>/api/data/v9.2/WhoAmI</c> or <c>/api/data/v9.2/WhoAmI()</c>, with nothing after it).</summary>
    internal static bool IsWhoAmI(HttpMethod method, string apiPath) =>
        method == HttpMethod.Get
        && (string.Equals(apiPath, "/api/data/v9.2/WhoAmI()", StringComparison.Ordinal)
            || string.Equals(apiPath, "/api/data/v9.2/WhoAmI", StringComparison.Ordinal));

    private static bool TryGetAnswer(HttpContext? request, string? credential, out Answer answer)
    {
        answer = default!;
        if (request is null || string.IsNullOrEmpty(credential))
            return false;

        object? held;
        lock (request.Items) // Items is a plain dictionary; Remember may be adding to it on another thread of this request
        {
            request.Items.TryGetValue(ItemsKey, out held);
        }

        return held is ConcurrentDictionary<string, Answer> answers
            && answers.TryGetValue(KeyOf(credential), out answer!);
    }

    private static bool TryReadUserId(JsonElement body, out Guid userId)
    {
        userId = Guid.Empty;
        return body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("UserId", out var id)
            && id.ValueKind == JsonValueKind.String
            && Guid.TryParse(id.GetString(), out userId)
            && userId != Guid.Empty;
    }

    private static string KeyOf(string credential) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));

    /// <summary>One remembered answer: the id, read once, and the body as text (immutable, so safe to share across threads).</summary>
    private sealed record Answer(Guid UserId, string RawJson);
}
