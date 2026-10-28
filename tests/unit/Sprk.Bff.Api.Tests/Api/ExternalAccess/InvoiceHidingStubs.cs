using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// ebilling-r1 task 098: stand-ins for the (a′) invoice predicate's seams, for surface tests that are about something else
/// (the predicate itself is tested over the real reads in <c>ExternalInvoiceVisibilityTests</c>). Each records what it was
/// asked, so a test can prove a surface asked for the invoice it returned.
/// </summary>
internal sealed class InvoiceHidingStub
{
    private readonly HashSet<Guid> _hidden;
    private readonly bool _fault;
    private readonly List<Guid[]> _calls = new();

    private InvoiceHidingStub(IEnumerable<Guid> hidden, bool fault)
    {
        _hidden = hidden.ToHashSet();
        _fault = fault;
    }

    /// <summary>Hides nothing (every asked invoice is visible).</summary>
    public static InvoiceHidingStub NoneHidden() => new(Array.Empty<Guid>(), fault: false);

    /// <summary>Hides exactly <paramref name="invoices"/>.</summary>
    public static InvoiceHidingStub Hiding(params Guid[] invoices) => new(invoices, fault: false);

    /// <summary>The read failed: every asked invoice is hidden (<see cref="InvoiceVisibilityResult.Faulted"/>).</summary>
    public static InvoiceHidingStub Faulting() => new(Array.Empty<Guid>(), fault: true);

    /// <summary>Every call's asked invoices, in order.</summary>
    public IReadOnlyList<Guid[]> Calls => _calls;

    /// <summary>The caller-aware seam the external surfaces take.</summary>
    public ExternalInvoiceHiding Seam => (caller, invoiceIds, ct) =>
    {
        var asked = invoiceIds.ToArray();
        lock (_calls) _calls.Add(asked);
        if (_fault)
            return Task.FromResult(InvoiceVisibilityResult.ReadFailed(asked));
        var hidden = asked.Where(_hidden.Contains).Distinct().ToDictionary(id => id, _ => InvoiceHiddenReason.SourceHidden);
        return Task.FromResult(new InvoiceVisibilityResult(asked, hidden, faulted: false));
    };
}

/// <summary>Task 098: stand-ins for the assistant tools' hide-only invoice-family seam.</summary>
internal static class InvoiceFamilyHidingStubs
{
    /// <summary>For a handler a test never lets reach the seam (an internal caller, a non-family table): any call fails.</summary>
    public static AppOnlyInvoiceFamilyHiding NeverCalled { get; } = (_, _, _) =>
        throw new InvalidOperationException("The invoice-family hiding was not expected to run (test).");

    /// <summary>Hides exactly <paramref name="rows"/> (of any table); records each call as "table:count".</summary>
    public static AppOnlyInvoiceFamilyHiding Hiding(List<string>? calls, params Guid[] rows)
    {
        var hidden = rows.ToHashSet();
        return (table, rowIds, _) =>
        {
            lock (hidden) calls?.Add($"{table}:{rowIds.Count}");
            return Task.FromResult<IReadOnlySet<Guid>>(rowIds.Where(hidden.Contains).ToHashSet());
        };
    }

    /// <summary>
    /// Review round 5: the REAL seam, <see cref="ExternalDataService.HideInvoiceFamilyRowsAsync"/> over the app-only Web API double
    /// <paramref name="world"/> (as <c>AddExternalAccess</c> binds it), for tool tests that must prove the decision itself.
    /// </summary>
    public static AppOnlyInvoiceFamilyHiding Real(Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.InvoiceWorld world) =>
        (table, rowIds, ct) => world.NewService(identity: null).HideInvoiceFamilyRowsAsync(table, rowIds, world.RootFlags, ct);

    /// <summary>A read fault: the seam throws, so the tool call fails with no data.</summary>
    public static AppOnlyInvoiceFamilyHiding Throwing { get; } = (_, _, _) =>
        throw new ExternalDataService.DataverseUnavailableException("The invoice-family read failed (test).");
}
