using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Tests.AccessControl.Ontology;

/// <summary>
/// A small Dataverse for the Ontology admin Rules routes (ontology platform R1 task 100), standing in for the caller-identity client
/// (<see cref="IDataverseUserClient"/>). It answers the queries the service sends and ENFORCES what they ask for, so a missing filter
/// shows up as a wrong answer instead of passing: the Ontology-only domain condition is applied only if the request carries it
/// (<see cref="DomainCondition"/>), the By-id filter only if the request names the id. A write is refused with the production error code
/// (<see cref="DataverseUserClientErrorCodes.AccessDenied"/>) for a user without the Ontology Administrator role, as Dataverse does.
/// Every request is recorded, in order, in <see cref="Requests"/>, together with the closure calls, in <see cref="Order"/>.
/// </summary>
public sealed class OntologyAdminRulesWorld : IDataverseUserClient
{
    /// <summary>The exact condition the service must send on every <c>sprk_policy</c> query (ebilling domain allow-list stand-in).</summary>
    public const string DomainCondition = "(sprk_domain eq null or sprk_domain eq 100000001)";

    public const int Billing = 100000000;
    public const int Ontology = 100000001;
    public const int Docketing = 100000002;

    public sealed class Policy
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string Code { get; init; } = "POL-X";
        public string Name { get; set; } = "Rule";
        public int? Domain { get; set; }
        public bool Enabled { get; set; } = true;
        public int StateCode { get; set; }
        public int StatusCode { get; set; } = 1;
        public int Lane { get; set; } = 100000001;
        public int SubjectType { get; set; } = 100000006;
        public int Severity { get; set; } = 100000001;
        public int? WorkType { get; set; } = 100000003;
        public int Priority { get; set; } = 500;
        public string? Tenant { get; set; }
        public Guid? Matter { get; set; }
        public bool CountsTowardSuppression { get; set; } = true;
        public Guid? CurrentVersion { get; set; }
        public string? RetiredReason { get; set; }
        public DateTime ModifiedOn { get; set; } = new(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);
        public Guid? ModifiedBy { get; set; }

        /// <summary>The row's <c>versionnumber</c>; moves on every write, as Dataverse does.</summary>
        public long Version { get; set; } = 1_000_000_001;
    }

    public sealed class VersionRow
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public Guid PolicyId { get; init; }
        public int Number { get; init; } = 1;
        public string Headline { get; init; } = "Open task past its due date";
        public int RuleType { get; init; } = 100000002;
        public string RuleBody { get; init; } = ExistenceBody;
        public string MessageTemplate { get; init; } = "This task is open and its due date has passed.";
        public string ProposedAction { get; init; } = "mark-complete: finish the task, or reschedule or reassign it.";
        public string? DecisionPlan { get; init; } = "{\"actions\":[\"mark-complete\",\"reschedule\",\"reassign\"],\"nextSteps\":[]}";
        public DateTime? InForceFrom { get; init; } = new(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc);
        public DateTime? InForceTo { get; init; }
        public Guid? AuthoredBy { get; init; }
        public DateTime CreatedOn { get; init; } = new(2026, 10, 14, 17, 30, 0, DateTimeKind.Utc);
    }

    /// <summary>The seeded Do-lane shape (no lookup GUIDs, so the describer needs no reference-table read).</summary>
    public const string ExistenceBody =
        "{\"type\":\"Existence\",\"subject\":\"sprk_event\",\"when\":{\"statuscode\":659490001,\"sprk_duedate\":{\"<=\":\"now-1d\"}},\"all\":[]}";

    public List<Policy> Policies { get; } = new();
    public List<VersionRow> Versions { get; } = new();
    public Dictionary<Guid, string> Users { get; } = new();
    public Dictionary<Guid, string> Matters { get; } = new();

    /// <summary>Open (Open or Acknowledged) Signals per policy id; the open-items number.</summary>
    public Dictionary<Guid, int> OpenSignals { get; } = new();

    public Dictionary<Guid, DateTime> LastFired { get; } = new();

    /// <summary>Acted / Dismissed Signals in the last 90 days per (policy code, version id), as the rate query groups them.</summary>
    public List<(string Code, Guid Version, int Lane, int Resolution, int Count)> Resolved { get; } = new();

    /// <summary>Every request, in order: <c>GET path</c>, <c>PATCH path body</c>.</summary>
    public List<string> Requests { get; } = new();

    /// <summary>Requests and closure calls, in order (<c>CLOSE id</c> is appended by <see cref="RecordClosure"/>).</summary>
    public List<string> Order { get; } = new();

    public List<(string Path, string Body)> Patches { get; } = new();

    /// <summary>The error code a PATCH is refused with (the caller is not an Ontology Administrator), or null to allow it.</summary>
    public string? WriteRefusal { get; set; }

    /// <summary>The error code every GET is refused with (an unresolved caller), or null.</summary>
    public string? ReadError { get; set; }

    /// <summary>Refuses (500) every Signal read: the numbers are unavailable but the rules must still be listed.</summary>
    public bool SignalReadsFail { get; set; }

    /// <summary>Refuses (500, <c>AggregateQueryRecordLimit</c>) only these Signal numbers: <c>open</c>, <c>last</c> or <c>rate</c>.</summary>
    public HashSet<string> FailedSignalNumbers { get; } = new(StringComparer.Ordinal);

    /// <summary>Dataverse's aggregate limit: a FetchXML aggregate that has to scan more rows than this fails with
    /// <c>AggregateQueryRecordLimit</c>.</summary>
    public const int AggregateRecordLimit = 50_000;

    /// <summary>Every Signal ever written (what an aggregate with no date window scans).</summary>
    public int TotalSignals { get; set; }

    /// <summary>Signals created in the last 90 days (what an aggregate bounded by <c>createdon last-x-days</c> scans).</summary>
    public int RecentSignals { get; set; }

    /// <summary>Leaves <c>versionnumber</c> out of every policy row (a read that did not return it).</summary>
    public bool OmitVersionNumber { get; set; }

    /// <summary>Called with the policy at write time, before the version check: another admin writing in between.</summary>
    public Action<Policy>? BeforePatch { get; set; }

    /// <summary>The FetchXML of every last-fired aggregate the service sent (decoded).</summary>
    public List<string> LastFiredFetches { get; } = new();

    /// <summary>Makes the policy list return this many rows (a page limit probe).</summary>
    public int? PadPolicyListTo { get; set; }

    public void RecordClosure(Guid policyId) => Order.Add($"CLOSE {policyId:D}");

    public Policy AddPolicy(string code, int? domain = null, bool enabled = true, Action<Policy>? configure = null)
    {
        var policy = new Policy { Code = code, Name = $"Rule {code}", Domain = domain, Enabled = enabled };
        configure?.Invoke(policy);
        Policies.Add(policy);
        return policy;
    }

    public VersionRow AddVersion(Policy policy, int number = 1, string? ruleBody = null, string? decisionPlan = null,
        DateTime? inForceTo = null, Guid? authoredBy = null, bool current = true)
    {
        var version = new VersionRow
        {
            PolicyId = policy.Id,
            Number = number,
            RuleBody = ruleBody ?? ExistenceBody,
            DecisionPlan = decisionPlan ?? "{\"actions\":[\"mark-complete\",\"reschedule\",\"reassign\"],\"nextSteps\":[]}",
            InForceTo = inForceTo,
            AuthoredBy = authoredBy,
        };
        Versions.Add(version);
        if (current)
        {
            policy.CurrentVersion = version.Id;
        }

        return version;
    }

    public int WriteCount => Patches.Count;

    // ---- IDataverseUserClient ---------------------------------------------------------------------------------------------

    public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
    {
        Requests.Add("GET " + relativePath);
        Order.Add("GET " + relativePath);
        if (ReadError is { } error)
        {
            return Task.FromResult(DataverseUserResponse.Fail(error == DataverseUserClientErrorCodes.AccessDenied ? 403 : 500, error, error));
        }

        var (path, query) = Split(relativePath);
        return Task.FromResult(path switch
        {
            "sprk_policies" => Ok(PolicyRows(query)),
            "sprk_policyversions" => Ok(VersionRows(query)),
            "systemusers" => Ok(UserRows(query)),
            "sprk_matters" => Ok(MatterRows(query)),
            "sprk_signals" => SignalsAnswer(query),
            _ => throw new InvalidOperationException($"The service asked something unexpected: {relativePath}"),
        });
    }

    public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken) =>
        Patch(relativePath, jsonBody, expectedVersion: null);

    public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, long expectedVersion, CancellationToken cancellationToken) =>
        Patch(relativePath, jsonBody, expectedVersion);

    private Task<DataverseUserResponse> Patch(string relativePath, string jsonBody, long? expectedVersion)
    {
        Requests.Add($"PATCH {relativePath} {jsonBody}");
        ExpectedVersions.Add(expectedVersion);
        Order.Add($"PATCH {relativePath}");
        Patches.Add((relativePath, jsonBody));
        if (WriteRefusal is { } refusal)
        {
            return Task.FromResult(DataverseUserResponse.Fail(403, refusal, refusal));
        }

        var match = Regex.Match(relativePath, @"^sprk_policies\(([0-9a-f-]{36})\)$");
        var policy = match.Success ? Policies.FirstOrDefault(p => p.Id == Guid.Parse(match.Groups[1].Value)) : null;
        if (policy is null)
        {
            return Task.FromResult(DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "not found"));
        }

        BeforePatch?.Invoke(policy);
        if (expectedVersion is { } expected && expected != policy.Version)
        {
            // Dataverse: If-Match names a version the row has moved past. Nothing is written.
            Patches.RemoveAt(Patches.Count - 1);
            PreconditionFailures.Add(relativePath);
            return Task.FromResult(DataverseUserResponse.Fail(412, DataverseUserClientErrorCodes.BadRequest, "The version of the record does not match."));
        }

        using var doc = JsonDocument.Parse(jsonBody);
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            switch (property.Name)
            {
                case "sprk_enabled":
                    policy.Enabled = property.Value.GetBoolean();
                    break;
                case "statecode":
                    policy.StateCode = property.Value.GetInt32();
                    break;
                case "statuscode":
                    policy.StatusCode = property.Value.GetInt32();
                    break;
                case "sprk_retiredreason":
                    policy.RetiredReason = property.Value.GetString();
                    break;
                default:
                    throw new InvalidOperationException($"The service wrote an unexpected column: {property.Name}");
            }
        }

        policy.Version++;
        return Task.FromResult(DataverseUserResponse.Ok(204, null));
    }

    /// <summary>The conditional PATCHes Dataverse refused with 412 (they are not counted in <see cref="Patches"/>: nothing was written).</summary>
    public List<string> PreconditionFailures { get; } = new();

    /// <summary>The expected versions the service sent, in order (null for an unconditional PATCH).</summary>
    public List<long?> ExpectedVersions { get; } = new();

    public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The Rules routes never create a row (no Decision Record is written).");

    public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The Rules routes never create a row (no Decision Record is written).");

    public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The Rules routes never delete a row.");

    // ---- the query emulation ----------------------------------------------------------------------------------------------

    private static (string Path, string Query) Split(string relativePath)
    {
        var i = relativePath.IndexOf('?', StringComparison.Ordinal);
        return i < 0 ? (relativePath, string.Empty) : (relativePath[..i], relativePath[(i + 1)..]);
    }

    private static DataverseUserResponse Ok(IEnumerable<object> rows) =>
        DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { value = rows.ToList() }));

    private static IEnumerable<Guid> Guids(string text) =>
        Regex.Matches(text, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}").Select(m => Guid.Parse(m.Value));

    private IEnumerable<object> PolicyRows(string query)
    {
        IEnumerable<Policy> rows = Policies;
        if (query.Contains(DomainCondition, StringComparison.Ordinal))
        {
            rows = rows.Where(p => p.Domain is null || p.Domain == Ontology);
        }

        var byId = Regex.Match(query, @"sprk_policyid eq ([0-9a-fA-F-]{36})");
        if (byId.Success)
        {
            var id = Guid.Parse(byId.Groups[1].Value);
            rows = rows.Where(p => p.Id == id);
        }

        var list = rows.Select(ToJson).ToList();
        if (PadPolicyListTo is { } pad)
        {
            while (list.Count < pad)
            {
                list.Add(ToJson(new Policy { Code = "PAD-" + list.Count }));
            }
        }

        return list;
    }

    private object ToJson(Policy p)
    {
        var row = ToRow(p);
        if (OmitVersionNumber)
        {
            row.Remove("versionnumber");
        }

        return row;
    }

    private static Dictionary<string, object?> ToRow(Policy p) => new()
    {
        ["sprk_policyid"] = p.Id,
        ["sprk_name"] = p.Name,
        ["sprk_policycode"] = p.Code,
        ["sprk_enabled"] = p.Enabled,
        ["statecode"] = p.StateCode,
        ["statuscode"] = p.StatusCode,
        ["sprk_lane"] = p.Lane,
        ["sprk_subjecttype"] = p.SubjectType,
        ["sprk_severity"] = p.Severity,
        ["sprk_worktype"] = p.WorkType,
        ["sprk_priority"] = p.Priority,
        ["sprk_tenant"] = p.Tenant,
        ["_sprk_matter_value"] = p.Matter,
        ["sprk_countstowardsuppression"] = p.CountsTowardSuppression,
        ["_sprk_currentversion_value"] = p.CurrentVersion,
        ["sprk_retiredreason"] = p.RetiredReason,
        ["modifiedon"] = p.ModifiedOn.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        ["_modifiedby_value"] = p.ModifiedBy,
        ["versionnumber"] = p.Version,
    };

    private IEnumerable<object> VersionRows(string query)
    {
        IEnumerable<VersionRow> rows = Versions;
        var byPolicy = Regex.Match(query, @"_sprk_policy_value eq ([0-9a-fA-F-]{36})");
        if (byPolicy.Success)
        {
            var id = Guid.Parse(byPolicy.Groups[1].Value);
            rows = rows.Where(v => v.PolicyId == id).OrderByDescending(v => v.Number);
        }
        else if (query.Contains("PropertyName='sprk_policyversionid'", StringComparison.Ordinal))
        {
            var ids = Guids(query[query.IndexOf("PropertyValues", StringComparison.Ordinal)..]).ToHashSet();
            rows = rows.Where(v => ids.Contains(v.Id));
        }
        else
        {
            throw new InvalidOperationException($"The service asked for versions in an unexpected way: {query}");
        }

        return rows.Select(v => (object)new Dictionary<string, object?>
        {
            ["sprk_policyversionid"] = v.Id,
            ["_sprk_policy_value"] = v.PolicyId,
            ["sprk_versionnumber"] = v.Number,
            ["sprk_shortheadline"] = v.Headline,
            ["sprk_ruletype"] = v.RuleType,
            ["sprk_rulebody"] = v.RuleBody,
            ["sprk_messagetemplate"] = v.MessageTemplate,
            ["sprk_proposedaction"] = v.ProposedAction,
            ["sprk_decisionplan"] = v.DecisionPlan,
            ["sprk_inforcefrom"] = v.InForceFrom?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["sprk_inforceto"] = v.InForceTo?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["_sprk_authoredby_value"] = v.AuthoredBy,
            ["createdon"] = v.CreatedOn.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        });
    }

    private IEnumerable<object> UserRows(string query)
    {
        var ids = Guids(query[(query.IndexOf("PropertyValues", StringComparison.Ordinal))..]).ToHashSet();
        return Users.Where(u => ids.Contains(u.Key))
            .Select(u => (object)new Dictionary<string, object?> { ["systemuserid"] = u.Key, ["fullname"] = u.Value });
    }

    private IEnumerable<object> MatterRows(string query)
    {
        var id = Guids(query).First();
        return Matters.Where(m => m.Key == id)
            .Select(m => (object)new Dictionary<string, object?> { ["sprk_matterid"] = m.Key, ["sprk_mattername"] = m.Value });
    }

    private DataverseUserResponse SignalsAnswer(string query)
    {
        if (SignalReadsFail)
        {
            return DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.ServiceError, "signals unavailable");
        }

        var fetch = Uri.UnescapeDataString(query["fetchXml=".Length..]);
        var number = fetch.Contains("sprk_resolutiontype", StringComparison.Ordinal) ? "rate"
            : fetch.Contains("aggregate=\"max\"", StringComparison.Ordinal) ? "last"
            : "open";
        if (FailedSignalNumbers.Contains(number))
        {
            return DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.ServiceError, "AggregateQueryRecordLimit exceeded");
        }

        if (number == "last")
        {
            LastFiredFetches.Add(fetch);
            var windowed = fetch.Contains("operator=\"last-x-days\" value=\"90\"", StringComparison.Ordinal);
            if ((windowed ? RecentSignals : TotalSignals) > AggregateRecordLimit)
            {
                return DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.ServiceError, "AggregateQueryRecordLimit exceeded");
            }
        }

        if (number == "rate")
        {
            return Ok(Resolved.Select(r => (object)new Dictionary<string, object?>
            {
                ["n"] = r.Count,
                ["policy"] = r.Code,
                ["version"] = r.Version,
                ["lane"] = r.Lane,
                ["resolution"] = r.Resolution,
            }));
        }

        if (number == "last")
        {
            return Ok(LastFired.Select(l => (object)new Dictionary<string, object?>
            {
                ["policy"] = l.Key,
                ["last"] = l.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            }));
        }

        return Ok(OpenSignals.Select(o => (object)new Dictionary<string, object?> { ["policy"] = o.Key, ["n"] = o.Value }));
    }
}
