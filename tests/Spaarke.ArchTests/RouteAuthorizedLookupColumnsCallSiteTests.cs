using System.Reflection;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Field-mapping-server-write-path-r1 task 031 fix round 1 (F1; UAC-r2 binding conditions (a) to (c), 2026-11-04):
/// <see cref="RecordCreationRequest.RouteAuthorizedLookupColumns"/> skips the caller's AppendTo on a lookup whose target the
/// ROUTE already authorised. Only route code may set it, at a CLOSED list of call sites, ratcheted at their names:
/// <list type="bullet">
/// <item><c>AnalysisEndpoints.CreateAnalysis</c> — POST /api/ai/analysis/create, the playbook the create filter authorised.</item>
/// </list>
/// The event create is NOT on the list: its route makes no route-level decision on the event type (review K1; a roles task).
/// The filter's decision is recorded by <c>AnalysisAuthorizationFilter.RecordPlaybookUseAuthorized</c>, itself a closed list of
/// one caller: <c>AnalysisAuthorizationFilter.AuthorizeCreateAsync</c> (the create filter, after every check passed). Which
/// (table, column) the service accepts is the service's own closed list (<see cref="RecordCreationService.RouteAuthorizedLookups"/>,
/// pinned here too).
/// </summary>
/// <remarks>
/// <para><b>Why IL.</b> <see cref="IlCallScan"/> reads compiled method references, so an initializer inside an async state
/// machine or a lambda counts, and a comment, string or <c>nameof</c> does not. Setting the property is a call of its
/// <c>init</c> accessor (<c>set_RouteAuthorizedLookupColumns</c>).</para>
/// <para><b>Controls.</b> <see cref="Detector_MustFireAndMustNotFire"/> runs the same detector over the
/// <c>RouteAuthorizedControl*</c> fixtures: a direct set and a set inside a lambda are found; a request that sets only
/// <see cref="RecordCreationRequest.ServerSetLookupColumns"/>, and one that only names the property, are not.</para>
/// </remarks>
public class RouteAuthorizedLookupColumnsCallSiteTests
{
    private const string Setter = "set_" + nameof(RecordCreationRequest.RouteAuthorizedLookupColumns);

    /// <summary>The closed list of call sites that may set the option, each with its route decision.</summary>
    private static readonly IReadOnlyList<(Type Type, string Method, string Reason)> SetterAllowList = new[]
    {
        (typeof(AnalysisEndpoints), nameof(AnalysisEndpoints.CreateAnalysis),
            "POST /api/ai/analysis/create: the playbook AnalysisAuthorizationFilter (AnalysisCreate) authorised for this caller and id."),
    };

    private static readonly Lazy<IReadOnlyList<Type>> BffTypes = new(() => typeof(Program).Assembly.GetTypes());

    [Fact(DisplayName = "031 fix round 1 (b): only AnalysisEndpoints.CreateAnalysis sets RecordCreationRequest.RouteAuthorizedLookupColumns")]
    public void OnlyTheClosedListSetsRouteAuthorizedLookupColumns()
    {
        var setters = Callers(BffTypes.Value, typeof(RecordCreationRequest), Setter);

        var outside = setters
            .Where(c => !SetterAllowList.Any(a => a.Type == c.Type && string.Equals(a.Method, c.Method, StringComparison.Ordinal)))
            .Select(c => $"{c.Type.FullName}.{c.Method}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(outside.Length == 0,
            "RecordCreationRequest.RouteAuthorizedLookupColumns skips the caller's AppendTo; only a route whose filter authorised " +
            "that record for this caller may set it (UAC-r2 conditions (a)-(c), owner decision 2026-11-04). Not on the list:\n" +
            string.Join("\n", outside));

        var stale = SetterAllowList
            .Where(a => !setters.Any(c => c.Type == a.Type && string.Equals(c.Method, a.Method, StringComparison.Ordinal)))
            .Select(a => $"{a.Type.Name}.{a.Method}")
            .ToArray();
        Assert.True(stale.Length == 0, "An allow-list entry no longer sets the option — remove it: " + string.Join(", ", stale));
    }

    [Fact(DisplayName = "031 fix round 1 (c): only the analysis create filter records the playbook-use decision the handler reads")]
    public void OnlyTheCreateFilterRecordsThePlaybookUseDecision()
    {
        var recorders = Callers(BffTypes.Value, typeof(AnalysisAuthorizationFilter), nameof(AnalysisAuthorizationFilter.RecordPlaybookUseAuthorized));

        // The record is made inside the delegate the create filter hands the evaluator (its "next"), so the body is a closure
        // of AuthorizeCreateAsync: pinned by its type and by the compiler's name for it, which carries the source method.
        var outside = recorders
            .Where(c => c.Type != typeof(AnalysisAuthorizationFilter) || !c.Method.Contains("AuthorizeCreateAsync", StringComparison.Ordinal))
            .Select(c => $"{c.Type.FullName}.{c.Method}")
            .ToArray();
        Assert.True(outside.Length == 0, "Only the /create filter may record that it authorised a playbook:\n" + string.Join("\n", outside));
        Assert.NotEmpty(recorders);
    }

    [Fact(DisplayName = "031 fix round 1 (b): the service's closed list is exactly (sprk_analysis, sprk_playbook, sprk_analysisplaybook)")]
    public void TheServicesClosedListIsTheAnalysisPlaybookOnly()
    {
        Assert.Equal(
            new[] { ("sprk_analysis", "sprk_playbook", "sprk_analysisplaybook") },
            RecordCreationService.RouteAuthorizedLookups.ToArray());
    }

    [Fact(DisplayName = "031 fix round 1 (a): the option is on no type a request body binds - only on the internal RecordCreationRequest")]
    public void NoBoundTypeCarriesTheOption()
    {
        var carriers = BffTypes.Value
            .Where(t => t != typeof(RecordCreationRequest))
            .Where(t => t.GetProperty(nameof(RecordCreationRequest.RouteAuthorizedLookupColumns),
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase) is not null)
            .Select(t => t.FullName)
            .ToArray();
        Assert.True(carriers.Length == 0, "Only RecordCreationRequest carries RouteAuthorizedLookupColumns:\n" + string.Join("\n", carriers));
        Assert.False(typeof(RecordCreationRequest).IsPublic, "RecordCreationRequest is internal: no endpoint can bind it from a body");
    }

    [Fact(DisplayName = "031 fix round 1 (b): control — a direct or lambda set is found; ServerSetLookupColumns or a nameof is not")]
    public void Detector_MustFireAndMustNotFire()
    {
        var fixtures = new[]
        {
            typeof(RouteAuthorizedControlDirectSetter), typeof(RouteAuthorizedControlLambdaSetter),
            typeof(RouteAuthorizedControlServerSetOnly), typeof(RouteAuthorizedControlNameOnly),
        };
        var found = Callers(fixtures.SelectMany(IlCallScan.WithNested), typeof(RecordCreationRequest), Setter)
            .Select(c => c.Type.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { nameof(RouteAuthorizedControlDirectSetter), nameof(RouteAuthorizedControlLambdaSetter) }, found);

        // A lambda's body is not the method it sits in: even in a method named like the allowed one it would not match the pin.
        var lambda = Callers(IlCallScan.WithNested(typeof(RouteAuthorizedControlLambdaSetter)), typeof(RecordCreationRequest), Setter).Single();
        Assert.NotEqual(nameof(RouteAuthorizedControlLambdaSetter.CreateAnalysis), lambda.Method);
    }

    /// <summary>The detector: each (outermost type, source method) whose compiled body references <paramref name="declaring"/>.<paramref name="name"/>.</summary>
    private static IReadOnlyList<(Type Type, string Method)> Callers(IEnumerable<Type> types, Type declaring, string name) =>
        IlCallScan.MethodReferences(types)
            .Where(r => r.Target.DeclaringType == declaring && r.Target.Name == name)
            .Select(r => (IlCallScan.Outermost(r.Caller), IlCallScan.SourceMethodName(r.Caller, r.CallerMethod)))
            .ToArray();
}

// Compiled control fixtures for RouteAuthorizedLookupColumnsCallSiteTests.Detector_MustFireAndMustNotFire. Never run; only
// their IL is read.

/// <summary>MUST FIRE: sets the option directly.</summary>
internal static class RouteAuthorizedControlDirectSetter
{
    internal static RecordCreationRequest Build(RecordCreationPayload payload) =>
        new("sprk_analysis", payload) { RouteAuthorizedLookupColumns = new HashSet<string> { "sprk_playbook" } };
}

/// <summary>MUST FIRE: sets the option inside a lambda — in a method named like the allowed one.</summary>
internal static class RouteAuthorizedControlLambdaSetter
{
    internal static RecordCreationRequest CreateAnalysis(RecordCreationPayload payload)
    {
        Func<RecordCreationRequest> build = () =>
            new("sprk_analysis", payload) { RouteAuthorizedLookupColumns = new HashSet<string> { "sprk_playbook" } };
        return build();
    }
}

/// <summary>MUST NOT FIRE: sets only the server-set lookups.</summary>
internal static class RouteAuthorizedControlServerSetOnly
{
    internal static RecordCreationRequest Build(RecordCreationPayload payload) =>
        new("sprk_event", payload) { ServerSetLookupColumns = new HashSet<string> { "sprk_assignedto" } };
}

/// <summary>MUST NOT FIRE: only names the option (this comment sets RouteAuthorizedLookupColumns; the code does not).</summary>
internal static class RouteAuthorizedControlNameOnly
{
    internal static string Name() => nameof(RecordCreationRequest.RouteAuthorizedLookupColumns);
}
