using System.Globalization;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// THE playbook-parameter policy: which caller-supplied <see cref="PlaybookRunRequest.Parameters"/> a run accepts, and
/// in what shape (unified-access-control-r2 task 164, owner round 16 item 3).
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> A caller's <c>Parameters</c> are rendered into EVERY node's ConfigJson (Layer 1,
/// <see cref="PlaybookOrchestrationService"/>), and the nodes run app-only. Before this policy a caller could name the
/// <c>userId</c> a run's <c>eq-userid</c> queries resolve to, point a record parameter at any matter (including an
/// app-only UpdateRecord write onto it), and put FetchXML into a public notification playbook's query through
/// <c>timeWindowHours</c>. The ROOT-CAUSE fix is at the substitution point: every value rendered into a query-text
/// position is escaped for that language (<see cref="PlaybookTemplateContextBuilder.EscapeForQueryText"/>) and the typed
/// keys below are type-checked there too (<see cref="EnsureTypedParametersValid"/>) — so no parameter can inject,
/// whatever its key. This class is the second half: the ONE shared policy every HTTP entry that takes caller parameters
/// applies before a run starts (<c>POST /api/ai/playbooks/{id}/execute</c> and <c>POST /api/agent/run-playbook</c> via
/// <c>PlaybookAuthorizationFilter</c>; <c>POST /api/insights/ask</c> is task 163's consumer).</para>
/// <para><b>The rules, in order</b> (<see cref="Evaluate"/>):</para>
/// <list type="number">
///   <item>A <see cref="ServerOwnedKeys">server-owned key</see> (any letter case, or a <c>run.</c> / <c>start.</c> /
///   <c>userPreferences.</c> path) is refused: the server or the scheduler binds it, never an HTTP caller. On the HTTP
///   path the run's user is the authenticated caller, set server-side (<see cref="PlaybookRunRequest.RunUserId"/>).</item>
///   <item>A <see cref="RecordIdentityParameters">record-identity key</see> must be a GUID; the HTTP entry then authorizes
///   it AS THE CALLER — Read, plus Write when a node that can write references it.</item>
///   <item>A <see cref="TypedParameters">typed tuning key</see> must parse as its declared type and range.</item>
///   <item>A <see cref="TextParameters">declared text key</see> is accepted (it is escaped wherever it lands in query
///   text, and otherwise only reaches prompt text) — unless its value is a GUID: a record id is accepted only on a
///   record-identity key (rule 2), so it cannot slip past rule 2 under another name.</item>
///   <item>Any other key is refused: the list is a declared ALLOW-list (round 16: "a declared allow-list of tuning keys
///   with types"). An undeclared key could carry a record id inside a larger value (<c>matter:{id}</c>) or shadow a node
///   output that a later node uses in an id position, and no rule could see either.</item>
/// </list>
/// <para>The lists are CLOSED and pinned by <c>PlaybookParameterPolicyTests</c>; the inventory they came from (in-repo
/// Insights playbooks, the live <c>sprk_playbooknode</c> rows, the scheduler and the code-level parameters) is recorded in
/// <c>projects/unified-access-control-r2/notes/task-164-ai-route-authorization.md</c> §12.</para>
/// </remarks>
public static class PlaybookParameterPolicy
{
    /// <summary>The value type of a <see cref="TypedParameters">typed tuning key</see>.</summary>
    public enum ParameterValueType
    {
        /// <summary>A whole number within the declared inclusive range.</summary>
        Integer,

        /// <summary>An ISO-8601 date or date-time (<c>yyyy-MM-dd</c> or <c>yyyy-MM-ddTHH:mm[:ss[.fff]][Z|±hh:mm]</c>).</summary>
        IsoDateTime,
    }

    /// <summary>One typed tuning key's declared type and (for integers) inclusive range.</summary>
    public sealed record TypedParameter(ParameterValueType Type, int Min = 0, int Max = 0);

    /// <summary>A record-identity parameter to authorize as the caller: its key, the entity it names and the record id.</summary>
    public sealed record RecordParameter(string Name, string EntityLogicalName, Guid RecordId);

    /// <summary>The outcome of <see cref="Evaluate"/>: either the record parameters to authorize, or the refused key and why.</summary>
    public sealed record Evaluation(bool IsValid, string? RejectedKey, string? Reason, IReadOnlyList<RecordParameter> RecordParameters)
    {
        internal static Evaluation Reject(string key, string reason) => new(false, key, reason, []);
    }

    /// <summary>Reason text of a refused server-owned key.</summary>
    public const string ServerOwnedReason = "it is set by the server, not by the caller";

    /// <summary>Reason text of a record-identity key whose value is not a record id.</summary>
    public const string NotARecordIdReason = "it must be a record id (GUID)";

    /// <summary>Reason text of a typed key whose value fails its type or range.</summary>
    public const string WrongTypeReason = "its value is not of the declared type";

    /// <summary>Reason text of a declared text key carrying a GUID.</summary>
    public const string UndeclaredRecordIdReason = "a record id is accepted only on a declared record parameter";

    /// <summary>Reason text of a key that is on none of the declared lists.</summary>
    public const string UndeclaredKeyReason = "it is not a declared playbook parameter";

    /// <summary>Reason text of a blank key.</summary>
    public const string BlankKeyReason = "a parameter name is required";

    /// <summary>
    /// Keys the SERVER or the scheduler binds, never an HTTP caller (rule 1). <c>userId</c> feeds the <c>eq-userid</c>
    /// FetchXML substitution and notification recipients; <c>tenantId</c> scopes index and Dataverse queries;
    /// <c>userName</c> is the scheduler's display name for the recipient (the former <c>PlaybookSchedulerJob</c>, removed D-100); <c>run</c>,
    /// <c>start</c> and <c>userPreferences</c> are server-built bags (the run metadata, the Start node's bound payload, the
    /// user's stored preferences).
    /// </summary>
    public static readonly IReadOnlyCollection<string> ServerOwnedKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "userId", "tenantId", "userName", "run", "start", "userPreferences" };

    /// <summary>Dotted paths under a server-owned bag are refused too (rule 1).</summary>
    public static readonly IReadOnlyList<string> ServerOwnedPrefixes = ["run.", "start.", "userPreferences."];

    /// <summary>
    /// Record-identity keys (rule 2): parameter name → the Dataverse LOGICAL name of the record it names. The entity SET
    /// is resolved by the existing allow-list (<c>SemanticSearchAuthorizationFilter.TryResolveAuthorizableEntitySet</c>),
    /// never by pluralizing. <c>matterId</c>: the Insights playbooks' subject (FetchXML, IndexRetrieve filter, LiveFact
    /// subject, UpdateRecord recordId); <c>projectId</c> / <c>invoiceId</c>: the other subjects the Insights orchestrator
    /// derives from <c>project:</c> / <c>invoice:</c>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> RecordIdentityParameters =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["matterId"] = "sprk_matter",
            ["projectId"] = "sprk_project",
            ["invoiceId"] = "sprk_invoice",
        };

    /// <summary>
    /// Typed tuning keys (rule 3), each with its type: the four the scheduler supplies to the notification playbooks,
    /// whose live Query Dataverse nodes put them into FetchXML (<c>last-x-hours value</c>, date comparisons).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, TypedParameter> TypedParameters =
        new Dictionary<string, TypedParameter>(StringComparer.OrdinalIgnoreCase)
        {
            ["timeWindowHours"] = new(ParameterValueType.Integer, 1, 8760),
            ["dueWithinDays"] = new(ParameterValueType.Integer, 0, 3660),
            ["todayUtc"] = new(ParameterValueType.IsoDateTime),
            ["dueSoonWindowUtc"] = new(ParameterValueType.IsoDateTime),
        };

    /// <summary>
    /// Declared text keys (rule 4; the "reasoned non-record list"): prompt content the Insights playbooks and the
    /// orchestrator bind (<c>matterDescription</c>, <c>matterContext</c>, <c>assessments</c>, <c>currentGrade</c>,
    /// <c>observations</c>, <c>cohortObservations</c>, <c>liveFacts</c>, <c>precedents</c>), the AI Completion node's
    /// <c>focus</c>, and the classifier hints <c>practiceAreaHint</c> / <c>documentTypeHint</c>. None is a record id; none
    /// fills a write target. Adding a key here is a policy change: it must be text-only wherever a playbook uses it.
    /// </summary>
    public static readonly IReadOnlyCollection<string> TextParameters =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "matterDescription", "matterContext", "assessments", "currentGrade", "observations",
            "cohortObservations", "liveFacts", "precedents", "focus", "practiceAreaHint", "documentTypeHint",
        };

    /// <summary>
    /// The ISO 8601 shapes an <see cref="ParameterValueType.IsoDateTime"/> value may take: a date, or a date and time
    /// (minutes, seconds, up to 7 fractional digits) with an optional <c>Z</c> / offset. An exact parse, not a regular
    /// expression: it cannot time out, so the answer never depends on load (a timed-out match would escape as a 500).
    /// </summary>
    private static readonly string[] IsoDateTimeFormats =
    [
        "yyyy-MM-dd",
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd'T'HH:mmK",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
    ];

    /// <summary>
    /// Applies rules 1-5 to a run's caller parameters (a closed allow-list). Syntax only — no rights query — so a refusal is a 400 that never
    /// depends on whether a record exists. A valid result lists the record-identity parameters the caller must be
    /// authorized for. <c>null</c> or empty parameters are valid.
    /// </summary>
    public static Evaluation Evaluate(IReadOnlyDictionary<string, string>? parameters)
    {
        if (parameters is not { Count: > 0 })
        {
            return new Evaluation(true, null, null, []);
        }

        var records = new List<RecordParameter>();
        foreach (var (rawKey, rawValue) in parameters)
        {
            var key = rawKey?.Trim() ?? string.Empty;
            var value = rawValue?.Trim() ?? string.Empty;

            if (key.Length == 0)
            {
                return Evaluation.Reject(string.Empty, BlankKeyReason);
            }

            if (IsServerOwned(key))
            {
                return Evaluation.Reject(key, ServerOwnedReason);
            }

            if (RecordIdentityParameters.TryGetValue(key, out var logicalName))
            {
                if (!Guid.TryParse(value, out var recordId) || recordId == Guid.Empty)
                {
                    return Evaluation.Reject(key, NotARecordIdReason);
                }

                records.Add(new RecordParameter(key, logicalName, recordId));
                continue;
            }

            if (TypedParameters.TryGetValue(key, out var typed))
            {
                if (!IsValidTypedValue(typed, value))
                {
                    return Evaluation.Reject(key, WrongTypeReason);
                }

                continue;
            }

            if (!TextParameters.Contains(key))
            {
                return Evaluation.Reject(key, UndeclaredKeyReason);
            }

            if (Guid.TryParse(value, out _))
            {
                return Evaluation.Reject(key, UndeclaredRecordIdReason);
            }
        }

        return new Evaluation(true, null, null, records);
    }

    /// <summary>True when <paramref name="key"/> is server-owned (rule 1), in any letter case.</summary>
    public static bool IsServerOwned(string key) =>
        ServerOwnedKeys.Contains(key)
        || ServerOwnedPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when <paramref name="value"/> parses as <paramref name="typed"/>'s type and lies in its range.</summary>
    public static bool IsValidTypedValue(TypedParameter typed, string? value)
    {
        ArgumentNullException.ThrowIfNull(typed);
        var trimmed = value?.Trim() ?? string.Empty;

        return typed.Type switch
        {
            ParameterValueType.Integer =>
                int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                && number >= typed.Min && number <= typed.Max,
            ParameterValueType.IsoDateTime =>
                DateTimeOffset.TryParseExact(
                    trimmed, IsoDateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _),
            _ => false,
        };
    }

    /// <summary>
    /// The substitution-point half of the type check: throws when a typed key in <paramref name="parameters"/> fails its
    /// type, whatever the entry path (HTTP, scheduler, Insights). The message names the key, never the value.
    /// </summary>
    /// <exception cref="InvalidOperationException">A typed parameter's value is not of its declared type.</exception>
    public static void EnsureTypedParametersValid(IReadOnlyDictionary<string, string>? parameters)
    {
        if (parameters is not { Count: > 0 })
        {
            return;
        }

        foreach (var (key, value) in parameters)
        {
            if (key is not null && TypedParameters.TryGetValue(key, out var typed) && !IsValidTypedValue(typed, value))
            {
                throw new InvalidOperationException(
                    $"Playbook parameter '{key}' is not a valid {typed.Type} value; the node was not run.");
            }
        }
    }

    /// <summary>
    /// True when <paramref name="text"/> (a node's ConfigJson) references the parameter <paramref name="key"/> inside any
    /// <c>{{ … }}</c> expression — directly (<c>{{matterId}}</c>), through a helper, or through a bag path such as
    /// <c>{{start.matterId}}</c>. Deliberately broad (fail closed): a match is the signal "this node may use the value".
    /// A plain scan (an expression is <c>{{</c>, then no brace, then <c>}}</c>; the key matches case-insensitively as a
    /// whole word), not a regular expression: it cannot time out, so the answer never depends on load.
    /// </summary>
    public static bool ReferencesParameter(string? text, string key)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(key) || !text.Contains("{{", StringComparison.Ordinal))
        {
            return false;
        }

        for (var open = text.IndexOf("{{", StringComparison.Ordinal); open >= 0; open = text.IndexOf("{{", open + 1, StringComparison.Ordinal))
        {
            var start = open + 2;
            var end = start;
            while (end < text.Length && text[end] != '{' && text[end] != '}')
            {
                end++;
            }

            if (end + 1 >= text.Length || text[end] != '}' || text[end + 1] != '}')
            {
                continue;
            }

            var expression = text.AsSpan(start, end - start);
            for (var at = expression.IndexOf(key, StringComparison.OrdinalIgnoreCase); at >= 0;)
            {
                var before = at == 0 ? '{' : expression[at - 1];
                var afterIndex = at + key.Length;
                var after = afterIndex < expression.Length ? expression[afterIndex] : '}';
                if (!IsWordChar(before) && !IsWordChar(after))
                {
                    return true;
                }

                var next = expression[(at + 1)..].IndexOf(key, StringComparison.OrdinalIgnoreCase);
                at = next < 0 ? -1 : at + 1 + next;
            }
        }

        return false;
    }

    private static bool IsWordChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';
}
