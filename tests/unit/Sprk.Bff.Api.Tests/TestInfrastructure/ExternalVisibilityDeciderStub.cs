using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// uac-r2 task 191g: a scripted <see cref="ExternalVisibilityDecider"/> (191c's seam over the 191b predicate) for tests of its
/// callers. Every asked row is Visible unless <see cref="Set"/> names it; <see cref="Faulted"/> marks the whole result faulted;
/// <see cref="Throws"/> makes the call throw. The predicate's own rules are pinned in <c>ExternalVisibilityTests</c>.
/// </summary>
internal sealed class ExternalVisibilityDeciderStub
{
    private readonly Dictionary<(string Table, Guid Id), (ExternalVisibilityVerdict Verdict, ExternalVisibilityReason Reason)> _verdicts = [];

    /// <summary>The whole decision comes back faulted (rows keep the verdicts set here).</summary>
    public bool Faulted { get; set; }

    /// <summary>The call throws this instead of answering.</summary>
    public Exception? Throws { get; set; }

    /// <summary>The rows each call asked about, in order.</summary>
    public List<IReadOnlyCollection<(string Table, Guid Id)>> Calls { get; } = [];

    public ExternalVisibilityDeciderStub Set(string table, Guid id, ExternalVisibilityVerdict verdict, ExternalVisibilityReason reason)
    {
        _verdicts[(table, id)] = (verdict, reason);
        return this;
    }

    public ExternalVisibilityDecider Decider => (rows, ct) =>
    {
        Calls.Add(rows);
        if (Throws is not null)
            return Task.FromException<ExternalVisibilityResult>(Throws);

        var decided = rows.ToDictionary(
            r => new ExternalVisibility.Node(r.Table, r.Id),
            r => _verdicts.TryGetValue((r.Table, r.Id), out var v)
                ? new ExternalVisibilityDecision(v.Verdict, v.Reason, r.Table, r.Id)
                : new ExternalVisibilityDecision(ExternalVisibilityVerdict.Visible, ExternalVisibilityReason.Visible, r.Table, r.Id));
        return Task.FromResult(new ExternalVisibilityResult(decided, Faulted));
    };
}
