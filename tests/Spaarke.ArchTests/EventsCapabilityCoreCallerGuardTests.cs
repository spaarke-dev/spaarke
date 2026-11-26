using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// THE EVENTS CAPABILITY'S UNGATED CORES HAVE TWO CALLERS (spaarkeai-mcp-bff-api-r2 task 006, ADR-008 / ADR-028).
///
/// <para><c>EventsCapability.ListCoreAsync</c>, <c>GetCoreAsync</c>, <c>CreateCoreAsync</c> and <c>CompleteCoreAsync</c> hold the
/// event rules but run NO gate of their own: get and complete read and write app-only, and create writes, on the assumption
/// that the caller's access was already decided. Only two callers may assume that: the REST handlers in
/// <c>Api/Events/EventEndpoints.cs</c>, whose endpoint filters decide it first, and <c>EventsCapability</c>'s own instance methods,
/// which decide it through the same methods. Anything else — an MCP tool above all — must call <c>IEventsCapability</c>, or it
/// gets the rule without the gate (adversarial review of task 006, finding K1, promoted to a guard).</para>
///
/// <para><b>Limits, stated exactly</b>: a source scan of <c>src/server/**</c>, allowing the two files by repo-relative path.
/// It sees a call written as <c>EventsCapability.XCoreAsync(</c>, a <c>using static</c> of the type and a <c>using</c> alias of
/// it; a call through reflection or a delegate captured inside an allowed file is not seen, and a new route added inside
/// <c>EventEndpoints.cs</c> itself is not told apart from the four filtered handlers (route filters there are held by
/// <c>RouteAuthorizationGuardTests</c>).</para>
/// </summary>
public class EventsCapabilityCoreCallerGuardTests
{
    // Repo-relative paths, '/'-separated: a file of the same name elsewhere is not allowed.
    private static readonly string[] AllowedFiles =
    [
        "src/server/api/Sprk.Bff.Api/Services/Events/EventsCapability.cs",
        "src/server/api/Sprk.Bff.Api/Api/Events/EventEndpoints.cs",
    ];

    private static readonly Regex UsingAlias = new(
        @"^\s*using\s+\w+\s*=\s*(?:global::)?Sprk\.Bff\.Api\.Services\.Events\.EventsCapability\s*;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex CoreCall = new(
        @"\bEventsCapability\s*\.\s*(?<core>ListCoreAsync|GetCoreAsync|CreateCoreAsync|CompleteCoreAsync)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UsingStatic = new(
        @"^\s*using\s+static\s+(?:global::)?Sprk\.Bff\.Api\.Services\.Events\.EventsCapability\s*;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    /// <summary>"what" for every core call or <c>using static</c> of the capability in <paramref name="code"/> (comments stripped).</summary>
    internal static List<string> ViolationsIn(string code)
    {
        var found = CoreCall.Matches(code).Select(m => m.Groups["core"].Value + "(").ToList();
        found.AddRange(UsingStatic.Matches(code).Select(_ => "using static EventsCapability"));
        found.AddRange(UsingAlias.Matches(code).Select(_ => "using alias = EventsCapability"));
        return found;
    }

    [Fact(DisplayName = "r2 task 006: only EventEndpoints and EventsCapability call the events capability's ungated cores")]
    public void OnlyTheRestHandlersAndTheCapabilityCallTheCores()
    {
        var violations = new List<string>();
        var allowedCalls = 0;
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var code = SourceScan.CodeText(File.ReadAllLines(file));
            var found = ViolationsIn(code);
            if (found.Count == 0)
            {
                continue;
            }

            if (AllowedFiles.Contains(SourceScan.Relative(file).Replace('\\', '/'), StringComparer.Ordinal))
            {
                allowedCalls += found.Count;
                continue;
            }

            violations.AddRange(found.Select(f => $"{SourceScan.Relative(file)}: {f}"));
        }

        Assert.True(
            violations.Count == 0,
            "These call an events capability core directly. The cores run no gate (the REST filters or the capability's instance "
            + "methods decide access first), so any other caller must use IEventsCapability (ListAsync / GetAsync / CreateAsync / "
            + "CompleteAsync), which fails closed and runs the same checks.\n\n  " + string.Join("\n  ", violations));

        // Non-vacuous: the four REST adapters' calls are seen (and allowed).
        Assert.True(allowedCalls >= 4, $"expected the four REST adapters' core calls in EventEndpoints.cs, saw {allowedCalls}");
    }

    [Fact(DisplayName = "r2 task 006 controls: a direct core call and a using static fire; a call through IEventsCapability does not")]
    public void CoreCallerGuard_Controls()
    {
        // Must fire.
        Assert.Equal(["GetCoreAsync("], ViolationsIn("var r = await EventsCapability.GetCoreAsync(id, events, logger, ct);"));
        Assert.Equal(["CompleteCoreAsync("], ViolationsIn("await Sprk.Bff.Api.Services.Events.EventsCapability . CompleteCoreAsync (id, http);"));
        Assert.Equal(["using static EventsCapability"], ViolationsIn("using static Sprk.Bff.Api.Services.Events.EventsCapability;\nclass X {}"));
        Assert.Equal(["using alias = EventsCapability"], ViolationsIn("using E = Sprk.Bff.Api.Services.Events.EventsCapability;\nclass X {}"));

        // Must not fire.
        Assert.Empty(ViolationsIn("var r = await _events.GetAsync(http, id, ct); var l = await capability.ListAsync(http, q, ct);"));
        Assert.Empty(ViolationsIn("internal static Task<int> GetCoreAsync(Guid id) => OtherService.GetCoreAsync(id);"));
    }
}
