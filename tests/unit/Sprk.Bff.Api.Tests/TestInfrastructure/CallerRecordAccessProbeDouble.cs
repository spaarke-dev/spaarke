using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// unified-access-control-r2 task 036 (FR-20): <see cref="CallerRecordAccessProbe"/> substituted at its module boundary —
/// the caller's own Dataverse rights per (entity set, record id), as the write routes ask for them. Unanswered pairs are
/// <see cref="AccessRights.None"/>, the probe's own fail-closed answer. Every call is recorded, so a test can assert that a
/// caller whose access Dataverse did NOT decide is never asked about (flag off is byte-equivalent).
/// </summary>
/// <remarks>Not a transport mock (ADR-038 §7 B1): it overrides the probe's virtual question, never its HTTP.</remarks>
public sealed class CallerRecordAccessProbeDouble : CallerRecordAccessProbe
{
    public CallerRecordAccessProbeDouble()
        : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
    {
    }

    /// <summary>Dataverse's answer per (entity set, record id).</summary>
    public ConcurrentDictionary<(string EntitySet, Guid RecordId), AccessRights> Answers { get; } = new();

    /// <summary>When set, the question throws (an unexpected fault; the real probe answers None instead).</summary>
    public Exception? Fault { get; set; }

    /// <summary>Every question asked, in order.</summary>
    public ConcurrentQueue<(string? Token, string EntitySet, Guid RecordId)> Calls { get; } = new();

    public CallerRecordAccessProbeDouble Answer(string entitySet, Guid recordId, AccessRights rights)
    {
        Answers[(entitySet, recordId)] = rights;
        return this;
    }

    public void Reset()
    {
        Answers.Clear();
        Fault = null;
        Calls.Clear();
    }

    public override Task<AccessRights> GetCallerRightsAsync(
        string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
    {
        Calls.Enqueue((callerBearerToken, entitySet, recordId));
        if (Fault is not null)
        {
            throw Fault;
        }

        return Task.FromResult(Answers.TryGetValue((entitySet, recordId), out var rights) ? rights : AccessRights.None);
    }
}
