using System.Reflection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 084d-1 (design <c>notes/084d-dry-run-design.md</c> §1; ontology-r1 review F1): the dry run's own types, the write-refusing
/// planning service and the row overlay, reference no write member of <see cref="IGenericEntityService"/> and nothing of the Signal writer,
/// the Decision Record writer, the owner question or Signal closure. A dry run plans through these types, so a write reached from them would
/// be a write made by a preview.
/// </summary>
/// <remarks>
/// <para><b>How.</b> Every method reference in the compiled IL of the scanned types and their nested types (<see cref="IlCallScan.MethodReferences"/>:
/// <c>call</c>, <c>callvirt</c>, <c>newobj</c>, <c>ldftn</c>, <c>ldvirtftn</c>, an <c>ldtoken</c> of a method), whatever lambda, local function or
/// state machine wraps it. A write member is matched by its name (an explicit implementation's name is cut at its last dot) on a type assignable
/// to <see cref="IGenericEntityService"/>, so a forward through a concrete implementation is caught as well as one through the interface.</para>
/// <para><b>Must-fire and must-not-fire controls</b> are the fixtures below: a forwarded create, a row-version update, a transaction sent inside an async lambda, an
/// owner question and a Signal close fire; a type that only reads does not. A non-vacuity check proves the scan reads the real bodies.</para>
/// <para><b>Not covered</b>: the <c>Plan*Async</c> methods of <c>ProcedureAssignmentService</c> share their type with the write path, so an IL
/// scan of that type cannot tell them apart; they take the planning service as a <see cref="WriteRefusingEntityService"/> parameter, and
/// <c>ProcedureDryRunPlanningContractTests</c> asserts that planning writes nothing, sends no transaction, asks no owner and closes nothing. A
/// call chosen at run time (reflection, a delegate built elsewhere) is beyond a reference scan.</para>
/// </remarks>
public sealed class DryRunNoWriteGuardTests
{
    /// <summary>The write members of the seam (kept in step with the contract test's classification of every member).</summary>
    private static readonly IReadOnlySet<string> WriteMembers = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(IGenericEntityService.CreateAsync), nameof(IGenericEntityService.UpsertAsync), nameof(IGenericEntityService.UpdateAsync),
        nameof(IGenericEntityService.UpdateIfRowVersionMatchesAsync), nameof(IGenericEntityService.BulkUpdateAsync),
        nameof(IGenericEntityService.BulkUpdateIfRowVersionsMatchAsync), nameof(IGenericEntityService.ExecuteTransactionAsync),
        nameof(IGenericEntityService.DeleteAsync), nameof(IGenericEntityService.AssociateAsync),
    };

    /// <summary>Types a dry-run type never references at all: they write Signals or the Decision Record, ask an owner, or close Signals.</summary>
    private static readonly Type[] ForbiddenTypes =
    [
        typeof(SignalWriter), typeof(DecisionRecordWriter), typeof(IRecordOwnershipResolver), typeof(SignalClosureService),
    ];

    /// <summary>The row source, the planning service, and (084d-2a fix round 1, K-5) the 2a entry point itself: the whole read service, so DryRunAsync, PlanAsync, SyntheticRowsAsync and their state machines are scanned.</summary>
    private static readonly Type[] DryRunTypes = [typeof(WriteRefusingEntityService), typeof(ProcedureRowOverlay), typeof(RecordProcedureReadService)];

    private static IEnumerable<string> Violations(IEnumerable<Type> types) =>
        IlCallScan.MethodReferences(types.SelectMany(IlCallScan.WithNested))
            .Where(r => IsForbidden(r.Target))
            .Select(r => $"{IlCallScan.Outermost(r.Caller).Name}::{IlCallScan.SourceMethodName(r.Caller, r.CallerMethod)} calls "
                         + $"{r.Target.DeclaringType?.FullName}::{r.Target.Name}")
            .Distinct();

    private static bool IsForbidden(MethodBase target)
    {
        var declaring = target.DeclaringType;
        if (declaring is null)
        {
            return false;
        }

        var name = target.Name[(target.Name.LastIndexOf('.') + 1)..];
        if (WriteMembers.Contains(name) && typeof(IGenericEntityService).IsAssignableFrom(declaring))
        {
            return true;
        }

        return ForbiddenTypes.Any(f => f.IsAssignableFrom(declaring));
    }

    [Fact(DisplayName = "084d-1 F1: the dry-run types reference no write, no Signal or Decision Record writer, no owner question and no Signal close")]
    public void TheDryRunTypes_ReferenceNoWrite()
    {
        var violations = Violations(DryRunTypes).ToList();

        Assert.True(violations.Count == 0,
            "A dry run never writes. The planning service and the overlay pass reads through and refuse every write; "
            + "a reference to a write, an owner question or a Signal close from them would let a preview write. " + string.Join("; ", violations));
    }

    [Fact(DisplayName = "084d-1 F1: the no-write scan is not vacuous (it reads the real bodies and sees their reads)")]
    public void TheScan_ReadsTheRealBodies()
    {
        var references = IlCallScan.MethodReferences(DryRunTypes.SelectMany(IlCallScan.WithNested)).ToList();

        Assert.Contains(references, r => r.Caller == typeof(ProcedureRowOverlay) || IlCallScan.Outermost(r.Caller) == typeof(ProcedureRowOverlay));
        foreach (var entryPoint in new[] { "DryRunAsync", "PlanAsync", "SyntheticRowsAsync" })
        {
            // K-5: the 2a entry point's own async bodies (the compiler-generated state machines) are read, not just its outer type.
            Assert.Contains(references, r => IlCallScan.Outermost(r.Caller) == typeof(RecordProcedureReadService)
                                             && IlCallScan.SourceMethodName(r.Caller, r.CallerMethod) == entryPoint);
        }

        Assert.Contains(references, r => r.Target.Name == nameof(IGenericEntityService.RetrieveMultipleAsync)
                                         && r.Target.DeclaringType == typeof(IGenericEntityService));
        Assert.Contains(references, r => r.Target.Name == nameof(IGenericEntityService.RetrieveAsync)
                                         && r.Target.DeclaringType == typeof(IGenericEntityService));
    }

    [Theory(DisplayName = "084d-1 F1: the no-write guard fires on each forbidden shape (must-fire controls)")]
    [InlineData(typeof(DryRunNoWriteControls.ForwardsACreate), "Spaarke.Dataverse.IGenericEntityService::CreateAsync")]
    [InlineData(typeof(DryRunNoWriteControls.UpdatesIfRowVersionMatches), "Spaarke.Dataverse.IGenericEntityService::UpdateIfRowVersionMatchesAsync")]
    [InlineData(typeof(DryRunNoWriteControls.SendsATransactionInAnAsyncLambda), "Spaarke.Dataverse.IGenericEntityService::ExecuteTransactionAsync")]
    [InlineData(typeof(DryRunNoWriteControls.AsksAnOwner), "IRecordOwnershipResolver::ResolveOwnerAsync")]
    [InlineData(typeof(DryRunNoWriteControls.ClosesSignals), "SignalClosureService::")]
    public void TheGuard_FiresOnItsControl(Type control, string expected)
    {
        var violations = Violations([control]).ToList();

        Assert.Contains(violations, v => v.Contains(expected, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "084d-1 F1: the no-write guard does not fire on a type that only reads (must-not-fire control)")]
    public void TheGuard_DoesNotFireOnReads()
    {
        Assert.Empty(Violations([typeof(DryRunNoWriteControls.ReadsOnly)]));
        Assert.NotEmpty(IlCallScan.MethodReferences(IlCallScan.WithNested(typeof(DryRunNoWriteControls.ReadsOnly)))
            .Where(r => r.Target.Name == nameof(IGenericEntityService.RetrieveMultipleAsync)));
    }
}

/// <summary>Control fixtures for <see cref="DryRunNoWriteGuardTests"/>. Never run; only their IL is read.</summary>
internal static class DryRunNoWriteControls
{
    internal sealed class ForwardsACreate(IGenericEntityService inner)
    {
        public Task<Guid> Forward(Entity row, CancellationToken ct) => inner.CreateAsync(row, ct);
    }

    internal sealed class UpdatesIfRowVersionMatches(IGenericEntityService inner)
    {
        public Task Update(Guid id, CancellationToken ct) =>
            inner.UpdateIfRowVersionMatchesAsync("sprk_policy", id, "1", new Dictionary<string, object> { ["sprk_enabled"] = true }, ct);
    }

    internal sealed class SendsATransactionInAnAsyncLambda(IGenericEntityService inner)
    {
        public Func<Task> Later(IReadOnlyList<OrganizationRequest> requests, CancellationToken ct) =>
            async () => await inner.ExecuteTransactionAsync(requests, ct).ConfigureAwait(false);
    }

    internal sealed class AsksAnOwner(IRecordOwnershipResolver owners)
    {
        public async Task<object> Ask(CancellationToken ct) => await owners.ResolveOwnerAsync(null!, ct).ConfigureAwait(false);
    }

    internal sealed class ClosesSignals(SignalClosureService closure)
    {
        public async Task Close(Guid policy, CancellationToken ct) =>
            await closure.ClosePolicySignalsRetiredAsync(policy, "RULE", withBillingSubject: false, ct).ConfigureAwait(false);
    }

    internal sealed class ReadsOnly(IGenericEntityService inner)
    {
        public Task<EntityCollection> Read(CancellationToken ct) => inner.RetrieveMultipleAsync(new QueryExpression("sprk_policy"), ct);
    }
}
