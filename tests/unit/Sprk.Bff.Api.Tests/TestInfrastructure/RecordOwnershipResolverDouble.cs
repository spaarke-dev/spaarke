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
/// Set <see cref="TeamId"/> to <see langword="null"/> to exercise a writer's refusal (with
/// <see cref="RefusalCode"/>), or set <see cref="Fault"/> to make every question throw — a Dataverse fault, which
/// writers must propagate rather than report as a refusal (task 146).
/// </para>
/// <para>
/// <b>Unchanged.</b> A context that opted into <see cref="UnfiledOwnership.KeepCreator"/> and names no parent is
/// answered <see cref="RecordOwnerOutcome.Unchanged"/>, exactly as the real resolver does — so writer tests see the
/// E1 branch without a second knob.
/// </para>
/// </remarks>
public sealed class RecordOwnershipResolverDouble : IRecordOwnershipResolver
{
    /// <summary>The team every create is owned by unless a test says otherwise.</summary>
    public static readonly Guid DefaultTeamId = Guid.Parse("0f0f0f0f-0080-4080-8080-000000000080");

    /// <summary>The answer to every question; <see langword="null"/> makes every writer refuse.</summary>
    public Guid? TeamId { get; set; } = DefaultTeamId;

    /// <summary>The refusal code reported when <see cref="TeamId"/> is null.</summary>
    public string RefusalCode { get; set; } = RecordOwnerRefusal.SecureParentNotIsolated;

    /// <summary>When set, every question throws it (a Dataverse fault).</summary>
    public Exception? Fault { get; set; }

    /// <summary>
    /// Task 146 c1-r1: the person a requester known only by Entra object id resolves to (a requester named by
    /// systemuserid is echoed as given). <see langword="null"/>: such a requester resolves to nobody.
    /// </summary>
    public Guid? ObjectIdRequesterPerson { get; set; } = DefaultRequesterPersonId;

    /// <summary>The person an object-id requester resolves to unless a test says otherwise.</summary>
    public static readonly Guid DefaultRequesterPersonId = Guid.Parse("0f0f0f0f-0146-4146-8146-000000000146");

    /// <summary>Every context the resolver was asked about, in order.</summary>
    public ConcurrentQueue<RecordOwnershipContext> Requests { get; } = new();

    /// <summary>Every reparent the resolver was asked about, in order.</summary>
    public ConcurrentQueue<RecordReparent> Reparents { get; } = new();

    public Task<Guid?> ResolveOwningTeamAsync(RecordOwnershipContext context, CancellationToken ct)
    {
        Requests.Enqueue(context);
        if (Fault is not null)
            return Task.FromException<Guid?>(Fault);
        return Task.FromResult(TeamId);
    }

    public Task<RecordOwnerResolution> ResolveOwnerAsync(RecordOwnershipContext context, CancellationToken ct)
    {
        Requests.Enqueue(context);
        var answer = Answer(context.HasParent, context.WhenUnfiled);
        return Task.FromResult(answer.IsRefused ? answer : answer with { CreatedByPerson = PersonOf(context.RequestedBy) });
    }

    /// <summary>The person who asked, as the real resolver answers it: the systemuserid given, else the object id's user.</summary>
    private Guid? PersonOf(RecordRequester? requester) =>
        requester?.SystemUserId is { } systemUserId && systemUserId != Guid.Empty
            ? systemUserId
            : requester?.ObjectId is { } objectId && objectId != Guid.Empty
                ? ObjectIdRequesterPerson
                : null;

    public async Task<RecordOwnerResolution> ReparentAsync(
        RecordReparent request, Func<CancellationToken, Task> applyChange, CancellationToken ct)
    {
        Reparents.Enqueue(request);
        var hasParent = request.ParentChanges.Values.Any(v => v is not null)
            || request.InheritedParents.Any(p => p.IsSpecified);
        var resolution = Answer(hasParent, request.WhenUnfiled);
        if (resolution.IsRefused)
            return resolution;

        await applyChange(ct);
        return resolution;
    }

    private RecordOwnerResolution Answer(bool hasParent, UnfiledOwnership whenUnfiled)
    {
        if (Fault is not null)
            throw Fault;

        if (!hasParent && whenUnfiled == UnfiledOwnership.KeepCreator)
            return RecordOwnerResolution.Unchanged("double: unfiled row keeps its creator");

        return TeamId is { } team
            ? RecordOwnerResolution.Owned(team)
            : RecordOwnerResolution.Refused(RefusalCode, "double: refused");
    }
}
