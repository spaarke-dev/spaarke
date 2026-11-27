using System.Reflection;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PlaybookRuns;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// spaarkeai-mcp-bff-api-r2 task 037a (owner decision D-037-engine), the security rule: no HTTP route or MCP tool argument can set a
/// run's <see cref="PlaybookRunRequest.AnalysisId"/>. The engine saves a run's output to that analysis app-only (the Analysis wrapper's
/// save), so the id must be the one the server has just created for this caller, and nothing else:
/// <list type="bullet">
/// <item>only <c>DetachedPlaybookRunner.StartReservedAsync</c> sets it, with the id <c>IAnalysisStartAi.StartAsCallerAsync</c> returned
/// for the caller (the route's own create, as the caller);</item>
/// <item>only <c>DetachedPlaybookRunner.RunDetachedAsync</c> links a run record store to an analysis, from that request's id;</item>
/// <item>no route handler binds <see cref="PlaybookRunRequest"/> (the REST routes build it in their handlers), and no type in
/// <c>Sprk.Bff.Api.Api</c> carries it as a property (so no bound body can carry it either).</item>
/// </list>
/// MCP tool arguments are closed sets read by hand (RunPlaybookTool refuses an <c>analysisId</c> argument:
/// <c>PlaybookRunToolsContractTests.Start_AnalysisIdArgument_IsRefused</c>); the IL pin below is what makes "no other code sets it" true.
/// </summary>
/// <remarks>
/// <para><b>Why IL.</b> <see cref="IlCallScan"/> reads compiled method references, so an object initializer inside an async state machine
/// or a lambda counts, and a comment, string or <c>nameof</c> does not. Setting the property is a call of its <c>init</c> accessor
/// (<c>set_AnalysisId</c>).</para>
/// <para><b>Controls.</b> <see cref="Detector_MustFireAndMustNotFire"/> runs the same detector over the <c>AnalysisIdControl*</c>
/// fixtures: a direct set and a set inside a lambda are found; a request that sets other members only, and one that only names the
/// property, are not.</para>
/// </remarks>
public class PlaybookRunAnalysisIdGuardTests
{
    private const string Setter = "set_" + nameof(PlaybookRunRequest.AnalysisId);

    private static readonly Lazy<IReadOnlyList<Type>> BffTypes = new(() => typeof(Program).Assembly.GetTypes());

    [Fact(DisplayName = "037a security: only DetachedPlaybookRunner.StartReservedAsync sets PlaybookRunRequest.AnalysisId")]
    public void OnlyTheCopilotStartSetsTheAnalysisId()
    {
        var setters = Callers(BffTypes.Value, typeof(PlaybookRunRequest), Setter).Distinct().ToArray();

        Assert.Equal(new[] { (typeof(DetachedPlaybookRunner), "StartReservedAsync") }, setters);
    }

    [Fact(DisplayName = "037a security: only DetachedPlaybookRunner.RunDetachedAsync links a run record store to an analysis")]
    public void OnlyTheDetachedRunLinksTheRunRecords()
    {
        var linkers = Callers(BffTypes.Value, typeof(PlaybookRunRecordStore), nameof(PlaybookRunRecordStore.LinkRunsToAnalysis)).Distinct().ToArray();

        Assert.Equal(new[] { (typeof(DetachedPlaybookRunner), "RunDetachedAsync") }, linkers);
    }

    [Fact(DisplayName = "037a security: no route handler takes a PlaybookRunRequest and no Api type carries one, so no request body can bind AnalysisId")]
    public void NoApiCodeBindsThePlaybookRunRequest()
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var apiTypes = BffTypes.Value.Where(t => t.Namespace?.StartsWith("Sprk.Bff.Api.Api", StringComparison.Ordinal) == true).ToArray();
        Assert.NotEmpty(apiTypes);

        var takers = apiTypes
            .SelectMany(t => t.GetMethods(All).Select(m => (Type: t, Method: (MethodBase)m)).Concat(t.GetConstructors(All).Select(c => (Type: t, Method: (MethodBase)c))))
            .Where(m => m.Method.GetParameters().Any(p => Carries(p.ParameterType)))
            .Select(m => $"{m.Type.FullName}.{m.Method.Name}")
            .ToArray();
        Assert.True(takers.Length == 0, "A method in Sprk.Bff.Api.Api takes a PlaybookRunRequest (a route could bind its AnalysisId from a body):\n" + string.Join("\n", takers));

        // Fields and properties of the code's own types: a handler's async state machine or closure holds the request it BUILT as a
        // compiler-generated field, which no body binds (a handler that TAKES one is caught above, lambdas included).
        var carriers = apiTypes
            .Where(t => !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false))
            .SelectMany(t => t.GetProperties(All).Select(p => (Type: t, Member: p.Name, MemberType: p.PropertyType))
                .Concat(t.GetFields(All).Select(f => (Type: t, Member: f.Name, MemberType: f.FieldType))))
            .Where(m => Carries(m.MemberType))
            .Select(m => $"{m.Type.FullName}.{m.Member}")
            .ToArray();
        Assert.True(carriers.Length == 0, "A type in Sprk.Bff.Api.Api carries a PlaybookRunRequest:\n" + string.Join("\n", carriers));
    }

    [Fact(DisplayName = "037a security: control - a direct or lambda set is found; other members or a nameof are not; the binding check sees a parameter and a property")]
    public void Detector_MustFireAndMustNotFire()
    {
        var fixtures = new[]
        {
            typeof(AnalysisIdControlDirectSetter), typeof(AnalysisIdControlLambdaSetter),
            typeof(AnalysisIdControlOtherMembersOnly), typeof(AnalysisIdControlNameOnly),
        };
        var found = Callers(fixtures.SelectMany(IlCallScan.WithNested), typeof(PlaybookRunRequest), Setter)
            .Select(c => c.Type.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { nameof(AnalysisIdControlDirectSetter), nameof(AnalysisIdControlLambdaSetter) }, found);

        // A lambda's body is not the method it sits in: even in a method named like the allowed one it would not match the pin.
        var lambda = Callers(IlCallScan.WithNested(typeof(AnalysisIdControlLambdaSetter)), typeof(PlaybookRunRequest), Setter).Single();
        Assert.NotEqual(nameof(AnalysisIdControlLambdaSetter.StartReservedAsync), lambda.Method);

        Assert.True(Carries(typeof(PlaybookRunRequest)));
        Assert.True(Carries(typeof(IReadOnlyList<PlaybookRunRequest>)));
        Assert.True(Carries(typeof(Task<PlaybookRunRequest>)));
        Assert.False(Carries(typeof(PlaybookExecuteRequest)));
        Assert.Contains(
            typeof(AnalysisIdControlBinder).GetMethods(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly),
            m => m.GetParameters().Any(p => Carries(p.ParameterType)));
        Assert.Contains(
            typeof(AnalysisIdControlCarrier).GetProperties(),
            p => Carries(p.PropertyType));
    }

    /// <summary>A type that is, or is built from, <see cref="PlaybookRunRequest"/> (an array, a list, a task, a nullable).</summary>
    private static bool Carries(Type type) =>
        type == typeof(PlaybookRunRequest)
        || (type.HasElementType && Carries(type.GetElementType()!))
        || (type.IsGenericType && type.GetGenericArguments().Any(Carries));

    /// <summary>The detector: each (outermost type, source method) whose compiled body references <paramref name="declaring"/>.<paramref name="name"/>.</summary>
    private static IReadOnlyList<(Type Type, string Method)> Callers(IEnumerable<Type> types, Type declaring, string name) =>
        IlCallScan.MethodReferences(types)
            .Where(r => r.Target.DeclaringType == declaring && r.Target.Name == name)
            .Select(r => (IlCallScan.Outermost(r.Caller), IlCallScan.SourceMethodName(r.Caller, r.CallerMethod)))
            .ToArray();
}

// Compiled control fixtures for PlaybookRunAnalysisIdGuardTests.Detector_MustFireAndMustNotFire. Never run; only their IL and
// signatures are read.

/// <summary>MUST FIRE: sets the id directly.</summary>
internal static class AnalysisIdControlDirectSetter
{
    internal static PlaybookRunRequest Build(Guid analysisId) => new() { PlaybookId = Guid.NewGuid(), DocumentIds = [], AnalysisId = analysisId };
}

/// <summary>MUST FIRE: sets the id inside a lambda, in a method named like the allowed one.</summary>
internal static class AnalysisIdControlLambdaSetter
{
    internal static PlaybookRunRequest StartReservedAsync(Guid analysisId)
    {
        Func<PlaybookRunRequest> build = () => new() { PlaybookId = Guid.NewGuid(), DocumentIds = [], AnalysisId = analysisId };
        return build();
    }
}

/// <summary>MUST NOT FIRE: builds a request without the id.</summary>
internal static class AnalysisIdControlOtherMembersOnly
{
    internal static PlaybookRunRequest Build() => new() { PlaybookId = Guid.NewGuid(), DocumentIds = [], UserContext = "x" };
}

/// <summary>MUST NOT FIRE: only names the property.</summary>
internal static class AnalysisIdControlNameOnly
{
    internal static string Name() => nameof(PlaybookRunRequest.AnalysisId);
}

/// <summary>MUST FIRE (binding check): a handler that takes the request.</summary>
internal static class AnalysisIdControlBinder
{
    internal static Guid? Handle(PlaybookRunRequest request) => request.AnalysisId;
}

/// <summary>MUST FIRE (binding check): a body type that carries the request.</summary>
internal sealed class AnalysisIdControlCarrier
{
    public PlaybookRunRequest? Run { get; init; }
}
