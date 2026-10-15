// spaarke-ontology-platform-r1 task 066 (2026-10-09) — sprk_event.sprk_eventstatus is DEPRECATED; statuscode is the one
// authoritative event status (D-28, spec FR-62).
//
// What was wrong: two status columns on sprk_event that disagree on live rows (spaarkedev1 2026-10-08: a row Completed in
// sprk_eventstatus but Draft in statuscode; most Draft rows have sprk_eventstatus null). The BFF's own paths (complete,
// create, To Do, briefing, external SPA) already read and write statuscode (task 097); the create-task reconcile apply, the
// reconcile undo, the ribbon commands, the Events grid bulk handlers, the calendar status filter and the side pane's
// fetch/enum still used the other column and its 0-7 vocabulary — and the reconcile tab put those 0-7 values on the wire.
//
// ADR-038 project rule 1: an assertion on an option-set value is paired with the real schema. The schema is
// docs/data-model/sprk_event-related-tables.md's statuscode row, re-verified against the LIVE describe of sprk_event
// (spaarkedev1, 2026-10-09: statuscode = No Further Action 2 [1], Draft 1 [0], Open 659490001 [0], Completed 659490002 [0],
// Closed 659490003 [0], Cancelled 659490004 [1], Transferred 659490005 [1], On Hold 659490006 [0], Reassigned 659490007 [0]).
// The parity tests below fail if a client's status constants and that row disagree.

using System.Text.RegularExpressions;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Events;

[Trait("status", "task-066-ontology-r1")]
public class EventStatusDeprecationTests
{
    // ── The BFF write: statuscode + the statecode it belongs to ────────────────────────────────────────────────

    [Fact]
    public void StatusMappings_EveryLiveStatus_WritesStatuscodeAndItsDocumentedStatecode()
    {
        var documented = DocumentedStatuses();
        documented.Should().NotBeEmpty();

        foreach (var (value, label, state) in documented)
        {
            var mappings = CommunicationCreateTaskApplyService.StatusMappings(value);

            mappings.Should().HaveCount(2, label);
            mappings.Should().ContainSingle(m => m.Field == "statuscode" && m.Value == value.ToString());
            mappings.Should().ContainSingle(m => m.Field == "statecode" && m.Value == state.ToString(),
                $"{label} must be written with statecode {state} or Dataverse rejects it");
            mappings.Should().NotContain(m => m.Field == "sprk_eventstatus");
        }
    }

    [Fact]
    public void StatusMappings_Completed_IsActive_AndCancelled_IsInactive()
    {
        var completed = CommunicationCreateTaskApplyService.StatusMappings(EventStatusCode.Completed);
        var cancelled = CommunicationCreateTaskApplyService.StatusMappings(EventStatusCode.Cancelled);

        completed.Single(m => m.Field == "statecode").Value.Should().Be("0");
        cancelled.Single(m => m.Field == "statecode").Value.Should().Be("1");
    }

    [Theory]
    [InlineData(0)]   // the deprecated column's Draft
    [InlineData(5)]   // the deprecated column's Cancelled
    [InlineData(7)]   // the deprecated column's Archived
    [InlineData(99)]
    public void StatusMappings_AValueOutsideTheLiveSet_IsLeftToTheFailLoudCoercion_NeverGuessedIntoAWrite(int notALiveStatus)
    {
        var mappings = CommunicationCreateTaskApplyService.StatusMappings(notALiveStatus);

        mappings.Should().ContainSingle("no statecode is invented for a value Dataverse does not know")
            .Which.Should().Match<ActionFieldMapping>(m => m.Field == "statuscode" && m.Type == ActionFieldType.String);
    }

    // ── Every client's status constants ARE the live statuscode values ─────────────────────────────────────────

    public static IEnumerable<object[]> ClientStatusMaps() => new[]
    {
        new object[] { "src/solutions/EventCommands/sprk_event_ribbon_commands.js", @"Spaarke\.Event\.EventStatus\s*=\s*\{(?<body>[^}]*)\}" },
        new object[] { "src/solutions/EventsPage/src/registerEventHandlers.ts", @"export const EventStatus\s*=\s*\{(?<body>[^}]*)\}" },
        new object[] { "src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx", @"const EventStatus\s*=\s*\{(?<body>[^}]*)\}" },
        new object[] { "src/client/shared/Spaarke.Communication.Components/src/components/ReconcileTabs/TaskReconcileTab.tsx", @"export const EVENT_STATUS\s*=\s*\{(?<body>[^}]*)\}" },
        new object[] { "src/solutions/EventDetailSidePane/src/types/EventRecord.ts", @"export enum EventStatus\s*\{(?<body>[^}]*)\}" },
    };

    [Theory]
    [MemberData(nameof(ClientStatusMaps))]
    public void ClientStatusConstants_AreTheLiveStatuscodeValues(string relativePath, string mapPattern)
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var map = Regex.Match(source, mapPattern);
        map.Success.Should().BeTrue($"{relativePath}: the status map must be found to be checked");

        var entries = Regex.Matches(map.Groups["body"].Value, @"(?<name>[A-Za-z_]+)\s*[:=]\s*(?<value>\d+)")
            .Select(m => (Name: Normalize(m.Groups["name"].Value), Value: int.Parse(m.Groups["value"].Value)))
            .ToList();
        entries.Should().NotBeEmpty();

        var documented = DocumentedStatuses().ToDictionary(d => Normalize(d.Label), d => d.Value);
        foreach (var (name, value) in entries)
        {
            // The ribbon / grid "Archive" command sets the status the data model calls "No Further Action".
            var key = name == "archived" ? "nofurtheraction" : name;
            documented.Should().ContainKey(key, $"{relativePath}: '{name}' is not a live sprk_event status");
            value.Should().Be(documented[key], $"{relativePath}: '{name}' must carry the live statuscode value");
        }
    }

    [Fact]
    public void TheSidePaneOpenWorkSet_IsEventStatusCodeIsOpenWork()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "solutions", "EventDetailSidePane", "src", "types", "EventRecord.ts"));
        var block = Regex.Match(source, @"ACTIVE_EVENT_STATUSES\s*=\s*\[(?<body>[^\]]*)\]").Groups["body"].Value;
        var names = Regex.Matches(block, @"EventStatus\.(?<n>\w+)").Select(m => Normalize(m.Groups["n"].Value)).ToHashSet();

        var expected = EventStatusCode.All.Where(s => EventStatusCode.IsOpenWork(s.Value)).Select(s => Normalize(s.Label)).ToHashSet();

        names.Should().BeEquivalentTo(expected, "the side pane must not decide 'open' differently from the BFF predicate");
    }

    // ── The deprecation guard: no source file touches the column ───────────────────────────────────────────────

    [Fact]
    public void NoSourceFile_ReadsOrWrites_TheDeprecatedColumn()
    {
        var root = RepoRoot();
        var offenders = new List<string>();
        foreach (var top in new[] { "src", "scripts" })
        {
            foreach (var file in Walk(Path.Combine(root, top)))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not (".cs" or ".ts" or ".tsx" or ".js" or ".mjs" or ".jsx" or ".ps1" or ".json" or ".xml" or ".html" or ".md"))
                    continue;
                if (new FileInfo(file).Length > 2_000_000)
                    continue; // generated bundles: they change when their source rebuilds
                if (File.ReadAllText(file).Contains("sprk_eventstatus", StringComparison.OrdinalIgnoreCase))
                    offenders.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
            }
        }

        offenders.Should().BeEmpty(
            "D-28: statuscode is authoritative. The column, its form placement, its views and the exported solution " +
            "snapshot (src/dataverse/solutions) are removed only by the owner after the task-066 inventory");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────────────────

    // The generated / exported / dependency trees are not hand-edited source: the Dataverse solution export (it carries
    // the column's definition, form placement and views, which stay until the owner approves removal) and build output.
    private static IEnumerable<string> Walk(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir))
            yield return file;

        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            var name = Path.GetFileName(sub);
            if (name is "node_modules" or "bin" or "obj" or "dist" or ".git" or "out" or "storybook-static")
                continue;
            if (sub.Replace('\\', '/').EndsWith("src/dataverse/solutions", StringComparison.Ordinal))
                continue;
            foreach (var f in Walk(sub))
                yield return f;
        }
    }

    private static string Normalize(string name) =>
        Regex.Replace(name, "[^A-Za-z]", "").ToLowerInvariant();

    /// <summary>(value, label, state) from the statuscode row of the live-verified sprk_event data-model doc.</summary>
    private static IReadOnlyList<(int Value, string Label, int State)> DocumentedStatuses()
    {
        var row = File.ReadLines(Path.Combine(RepoRoot(), "docs", "data-model", "sprk_event-related-tables.md"))
            .Single(l => Regex.IsMatch(l, @"\|\s*sprk_event\s*\|\s*statuscode\s*\|"));

        return Regex.Matches(row, @"(\d+): ([A-Za-z ]+?) \[(\d) (?:Active|Inactive)\]")
            .Select(m => (int.Parse(m.Groups[1].Value), m.Groups[2].Value, int.Parse(m.Groups[3].Value)))
            .ToList();
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            // `.git` is a FILE in a git worktree, which is how this repo is normally developed.
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir;

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root (.git) not found above " + AppContext.BaseDirectory);
    }
}
