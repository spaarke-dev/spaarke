using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Test doubles for the write-time grant policy (unified-access-control-r2 task 138).
/// </summary>
internal static class GrantPolicyTestDoubles
{
    /// <summary>
    /// The production <see cref="ExternalParticipationService"/> with ONLY its flag read overridden — the same
    /// subclass-and-override seam every evaluator test uses (ADR-038: no <c>Mock&lt;HttpMessageHandler&gt;</c>).
    /// </summary>
    /// <remarks>
    /// <para>Records the entity type it was asked about, so a test can prove the policy addressed the flag
    /// reader by the root's LOGICAL name (an entity-set name would return an empty map).</para>
    /// <para>An id with no seeded flags answers <c>defaultFlags</c>. <see cref="Absent"/> drops an id from the
    /// returned map altogether — the shape the real reader produces for a non-flag-bearing entity type — and
    /// <see cref="ThrowOnRead"/> makes the read throw.</para>
    /// </remarks>
    internal sealed class FlagStubParticipationService : ExternalParticipationService
    {
        private readonly RootRecordFlags _defaultFlags;

        public FlagStubParticipationService(RootRecordFlags defaultFlags)
            : base(new HttpClient(), cache: null!, configuration: null!, credential: null!,
                   httpContextAccessor: null!, logger: NullLogger<ExternalParticipationService>.Instance)
        {
            _defaultFlags = defaultFlags;
        }

        /// <summary>Per-record flags; anything not listed answers the default.</summary>
        public ConcurrentDictionary<Guid, RootRecordFlags> Flags { get; } = new();

        /// <summary>Ids the read leaves OUT of its answer (the absent-key case).</summary>
        public ConcurrentDictionary<Guid, bool> Absent { get; } = new();

        /// <summary>When set, the read throws instead of answering.</summary>
        public bool ThrowOnRead { get; set; }

        /// <summary>Every (entityType, recordId) the read was asked about.</summary>
        public ConcurrentBag<(string EntityType, Guid RecordId)> Reads { get; } = new();

        public override Task<IReadOnlyDictionary<Guid, RootRecordFlags>> GetRootRecordFlagsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
        {
            foreach (var id in recordIds)
            {
                Reads.Add((entityType, id));
            }

            if (ThrowOnRead)
            {
                throw new InvalidOperationException("Simulated root-flag read failure.");
            }

            IReadOnlyDictionary<Guid, RootRecordFlags> result = recordIds
                .Distinct()
                .Where(id => !Absent.ContainsKey(id))
                .ToDictionary(id => id, id => Flags.TryGetValue(id, out var f) ? f : _defaultFlags);
            return Task.FromResult(result);
        }
    }
}
