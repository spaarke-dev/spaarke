using System.Collections.Concurrent;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// A module-boundary double for <see cref="IRecordOwnershipResolver"/> (task 080, write-path invariant I-6):
/// answers every question with one fixed team and records what it was asked.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a double and not the real resolver.</b> The real one reads <c>systemuser</c> / <c>team</c> /
/// the target's <c>owningbusinessunit</c> through <c>IDataverseService</c>, which the Office test hosts double
/// with a loose mock — so it would answer "no team" and correctly REFUSE every create. The resolution order and
/// both refuse branches are pinned by <c>RecordOwnershipResolverTests</c>; the writer tests only need to see
/// that each writer ASKS (with the right target) and WRITES what it is told.
/// </para>
/// <para>
/// Set <see cref="TeamId"/> to <see langword="null"/> to exercise a writer's refusal.
/// </para>
/// </remarks>
public sealed class RecordOwnershipResolverDouble : IRecordOwnershipResolver
{
    /// <summary>The team every create is owned by unless a test says otherwise.</summary>
    public static readonly Guid DefaultTeamId = Guid.Parse("0f0f0f0f-0080-4080-8080-000000000080");

    /// <summary>The answer to every question; <see langword="null"/> makes every writer refuse.</summary>
    public Guid? TeamId { get; set; } = DefaultTeamId;

    /// <summary>Every context the resolver was asked about, in order.</summary>
    public ConcurrentQueue<RecordOwnershipContext> Requests { get; } = new();

    public Task<Guid?> ResolveOwningTeamAsync(RecordOwnershipContext context, CancellationToken ct)
    {
        Requests.Enqueue(context);
        return Task.FromResult(TeamId);
    }
}
