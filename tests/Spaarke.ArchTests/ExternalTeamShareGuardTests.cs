using System.Reflection;
using System.Text.Json.Nodes;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 191c (#2179 design v2 §3.4, §3.5, §3.8). Two rules the External Collaborators team share rests on.
/// <list type="number">
/// <item><b>Every code path that GRANTS or WIDENS a POA share is known.</b> Once flagged users sit in the External Collaborators
/// unit, a per-user share is the only way a flagged user reaches a row the predicate hides. Every type whose compiled IL calls
/// <see cref="IDataverseRecordShareService.GrantAccessAsync"/> or <see cref="IDataverseRecordShareService.ModifyAccessAsync"/>
/// is listed below with the reason it cannot hand a flagged user a hidden row (or the known gap it is). A new caller fails here
/// until someone decides; an entry with no caller left fails too (the list only shrinks with the code).</item>
/// <item><b>The engine's tables are the role's tables.</b> <see cref="ExternalTeamShareSynchronizer.FilingTables"/> are exactly the
/// lineage entries of <c>config/external-collaborator-role.json</c> that carry AppendTo; every registry table the file lists as
/// "never" gets no team share; every other registry table gets Read; the unit's name is the file's.</item>
/// </list>
/// Enforced by: this guard (IL references, so a lambda, local function, state machine or method group is seen; controls below).
/// </summary>
public class ExternalTeamShareGuardTests
{
    /// <summary>
    /// The types allowed to grant or widen a POA share, with why that cannot reach a flagged user on a row the predicate hides.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> GrantCallers = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Sprk.Bff.Api.Services.Access.ExternalTeamShareSynchronizer"] =
            "Task 191c's engine: grants and widens the External Collaborators TEAM only, and only on a row the predicate shows " +
            "(never a Secure family, never a never-shared table).",
        ["Sprk.Bff.Api.Services.Access.SecureChildShareSynchronizer"] =
            "The secure mirror (task 149): its flagged-principal filter (task 191c, FilterFlaggedAsync) gives a user flagged " +
            "external, or one whose flag cannot be read, nothing on a child the predicate does not show.",
        ["Sprk.Bff.Api.Api.ExternalAccess.InternalUserShareCore"] =
            "Manage Access shares a ROOT with a person; a flagged user is barred on a Restricted root (task 114, " +
            "InternalShareEndpoints.IsBarredOnRestricted). Root per-user shares are the Restricted remover's domain (I-14).",
        ["Sprk.Bff.Api.Api.ExternalAccess.ProvisionProjectEndpoint"] =
            "Make Secure shares the secure ROOT with named colleagues and the creator, never with a user flagged external " +
            "(task 114); a secure family is never team-shared.",
        ["Sprk.Bff.Api.Services.Access.SecureRootInheritance"] =
            "A secure ROOT filed under a secure parent: passes the parent's sharees on, never a user flagged external on a " +
            "Restricted record (task 114, RestrictedExternalPrincipalsOrThrowAsync); the modify restores a prior mask on rollback.",
        ["Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer"] =
            "Assigned-To shares a ROOT with its assignee; on a Restricted root a user flagged external gets none (task 114) " +
            "and the Restricted remover runs first.",
        ["Sprk.Bff.Api.Services.Ai.PlaybookSharingService"] =
            "Shares a playbook (sprk_analysisplaybook), not a lineage table: outside the visibility predicate.",
        ["Sprk.Bff.Api.Services.Signals.GateDecisionRecordSharing"] =
            "A gate Decision Record is shared with the users who read its communication, never with a user flagged external " +
            "(its own check: the stored sprk_isexternal only, task 197b; an unreadable flag is never shared).",
        ["Sprk.Bff.Api.Services.Access.DirectMessageParticipantShares"] =
            "Task 197b: a Direct-team-owned message's participants flagged external are granted a row only when the predicate " +
            "allows it (DecideForDirectParticipantsAsync, participant-scoped NoRoot exemption); a hidden row's share is revoked.",
        ["Sprk.Bff.Api.Services.Communication.Access.DirectThreadAccessService"] =
            "Task 197b: the Direct-team branch and the other Direct branch ask the predicate for participants flagged external " +
            "(DecideForDirectParticipantsAsync; the NoRoot exemption only on the Direct-team branch). KNOWN LIMIT: the thread " +
            "create share and the record-anchored branch do not (task notes).",
    };

    private static readonly string[] GrantMethods =
        [nameof(IDataverseRecordShareService.GrantAccessAsync), nameof(IDataverseRecordShareService.ModifyAccessAsync)];

    /// <summary>The outermost types whose IL references the seam's grant or modify (through the interface).</summary>
    internal static IReadOnlyCollection<string> GrantCallersIn(IEnumerable<Type> types) =>
        IlCallScan.MethodReferences(types)
            .Where(r => r.Target.DeclaringType == typeof(IDataverseRecordShareService) && GrantMethods.Contains(r.Target.Name))
            .Select(r => IlCallScan.Outermost(r.Caller).FullName ?? r.Caller.Name)
            .ToHashSet(StringComparer.Ordinal);

    private static readonly Lazy<IReadOnlyCollection<string>> BffGrantCallers = new(() =>
        GrantCallersIn(typeof(ExternalTeamShareSynchronizer).Assembly.GetTypes()));

    [Fact]
    public void EveryTypeThatGrantsOrWidensAShare_IsOnTheAllowList()
    {
        var unlisted = BffGrantCallers.Value.Where(c => !GrantCallers.ContainsKey(c)).Order(StringComparer.Ordinal).ToList();

        Assert.True(unlisted.Count == 0,
            "These types grant or widen a POA share and are not on ExternalTeamShareGuardTests.GrantCallers. Add each with the " +
            "reason it cannot give a user flagged sprk_isexternal = true a right on a row the visibility predicate hides " +
            "(design §3.5), or route it through the External-team engine:\n" + string.Join("\n", unlisted));
    }

    [Fact]
    public void EveryAllowListEntry_StillGrants()
    {
        var stale = GrantCallers.Keys.Where(c => !BffGrantCallers.Value.Contains(c)).Order(StringComparer.Ordinal).ToList();

        Assert.True(stale.Count == 0, "These allow-list entries no longer grant a share; remove them:\n" + string.Join("\n", stale));
    }

    [Fact]
    public void EveryAllowListEntry_SaysWhy()
    {
        Assert.All(GrantCallers, entry => Assert.True(entry.Value.Length >= 20, $"{entry.Key}: give the reason"));
    }

    [Fact]
    public void Controls_AGrantInAnAsyncLambdaAndAModifyInADelegateAreSeen_ARevokeIsNot()
    {
        Assert.Contains(typeof(ExternalTeamShareGuardControl_Granting).FullName!, GrantCallersIn(IlCallScan.WithNested(typeof(ExternalTeamShareGuardControl_Granting))));
        Assert.Contains(typeof(ExternalTeamShareGuardControl_Widening).FullName!, GrantCallersIn(IlCallScan.WithNested(typeof(ExternalTeamShareGuardControl_Widening))));
        Assert.Empty(GrantCallersIn(IlCallScan.WithNested(typeof(ExternalTeamShareGuardControl_Revoking))));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The engine's tables are config/external-collaborator-role.json's
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    private static JsonObject RoleFile() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(SourceScan.RepoRoot, "config", ExternalCollaboratorRoleGuardTests.FileName)))!
            .AsObject();

    private static IEnumerable<(string Table, JsonObject Entry)> Entries(JsonObject file, string name) =>
        (file[name] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(e => ((string)e["logicalName"]!, e));

    [Fact]
    public void TheFilingTables_AreTheRolesLineageTablesWithAppendTo()
    {
        var withAppendTo = Entries(RoleFile(), "lineage")
            .Where(e => (e.Entry["access"] as JsonArray ?? []).Any(a => (string?)a == "AppendTo"))
            .Select(e => e.Table)
            .Order(StringComparer.Ordinal);

        Assert.Equal(withAppendTo, ExternalTeamShareSynchronizer.FilingTables.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryRegistryTable_GetsTheTeamShareTheRoleFileImplies()
    {
        var file = RoleFile();
        var never = Entries(file, "never").Select(e => e.Table).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lineage = Entries(file, "lineage").ToDictionary(e => e.Table, e => e.Entry, StringComparer.OrdinalIgnoreCase);
        var findings = new List<string>();

        foreach (var table in ExternalCollaboratorRoleGuardTests.RegistryTables().Order(StringComparer.Ordinal))
        {
            var rights = ExternalTeamShareSynchronizer.TeamRightsFor(table);
            if (never.Contains(table))
            {
                if (rights is not null)
                    findings.Add($"{table} is 'never' in the role file but the engine shares it ({rights.Value.AccessRightsCsv})");
                if (!ExternalTeamShareSynchronizer.NeverSharedTables.Contains(table))
                    findings.Add($"{table} is 'never' in the role file but not in ExternalTeamShareSynchronizer.NeverSharedTables");
            }
            else if (lineage.TryGetValue(table, out var entry))
            {
                var appendTo = (entry["access"] as JsonArray ?? []).Any(a => (string?)a == "AppendTo");
                var expected = appendTo ? 17 : 1;
                if (rights?.AccessRightsMask != expected)
                    findings.Add($"{table}: the engine shares {rights?.AccessRightsMask.ToString() ?? "nothing"}, the role file implies {expected}");
            }
            else
            {
                findings.Add($"{table} is in the lineage registry but in neither 'lineage' nor 'never' of the role file");
            }
        }

        foreach (var table in ExternalTeamShareSynchronizer.NeverSharedTables.Where(t => !never.Contains(t)))
            findings.Add($"{table} is in NeverSharedTables but not 'never' in the role file");

        Assert.True(findings.Count == 0, string.Join("\n", findings));
    }

    [Fact]
    public void ATableOutsideTheRegistry_GetsNoTeamShare()
    {
        Assert.Null(ExternalTeamShareSynchronizer.TeamRightsFor("contact"));
        Assert.Null(ExternalTeamShareSynchronizer.TeamRightsFor("sprk_procedure"));
    }

    /// <summary>uac-r2 task 197b: the External Collaborators unit has ONE source. The C# constant every reader uses (191c's engine,
    /// 191e's placement job and census, the ownership resolver) is the role file's name, and the role's.</summary>
    [Fact]
    public void TheUnitName_AndTheRoleName_AreTheRoleFiles()
    {
        var file = RoleFile();
        Assert.Equal(Sprk.Bff.Api.Services.ExternalAccess.ExternalCollaboratorPlacement.BusinessUnitName,
            (string?)file["businessUnit"]?["name"]);
        Assert.Equal(Sprk.Bff.Api.Services.ExternalAccess.ExternalCollaboratorPlacement.RoleName, (string?)file["roleName"]);
    }

    /// <summary>uac-r2 task 197b: the retired setting is in no checked-in configuration (appsettings, Bicep, parameter files,
    /// workflows, scripts). Its only mention outside C# and docs would revive a second source of the unit's name.</summary>
    [Fact]
    public void TheRetiredUnitNameSetting_IsInNoCheckedInConfiguration()
    {
        Assert.Equal("ExternalAccess:ExternalTeamShare:BusinessUnitName", ExternalTeamShareSynchronizer.RetiredBusinessUnitNameConfigKey);
        var offenders = RetiredSettingMentions(SourceScan.RepoRoot).ToList();
        Assert.True(offenders.Count == 0, "The retired setting is still configured in:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void Control_TheRetiredSettingScan_FindsTheKeyInEitherForm()
    {
        var root = Directory.CreateTempSubdirectory("uac197b-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "appsettings.Test.json"), "{ \"ExternalAccess\": { \"ExternalTeamShare\": { \"BusinessUnitName\": \"x\" } } }");
            File.WriteAllText(Path.Combine(root, "main.bicep"), "name: 'ExternalAccess__ExternalTeamShare__BusinessUnitName'");
            File.WriteAllText(Path.Combine(root, "clean.json"), "{ \"ExternalAccess\": { \"ExternalTeamShare\": { \"WritesEnabled\": false } } }");

            Assert.Equal(["appsettings.Test.json", "main.bicep"], RetiredSettingMentions(root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static readonly string[] ConfigExtensions = [".json", ".bicep", ".bicepparam", ".yml", ".yaml", ".ps1", ".psm1", ".env"];

    private static readonly System.Text.RegularExpressions.Regex RetiredFlat = new(
        @"ExternalTeamShare(:|__)BusinessUnitName", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly System.Text.RegularExpressions.Regex RetiredNested = new(
        "\"ExternalTeamShare\"\\s*:\\s*\\{[^}]*\"BusinessUnitName\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Configuration files (not C#, not docs, not projects/ notes, not build output) that mention the retired key.</summary>
    private static IEnumerable<string> RetiredSettingMentions(string root)
    {
        var skipped = new[] { "node_modules", "bin", "obj", ".git", "projects", "docs", ".claude", "tests", "dist", ".vs" };
        var pending = new Stack<string>([root]);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                if (!skipped.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase))
                    pending.Push(sub);
            }

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (!ConfigExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    continue;
                var text = File.ReadAllText(file);
                if (RetiredFlat.IsMatch(text) || RetiredNested.IsMatch(text))
                    yield return file;
            }
        }
    }
}

// ── Controls (top-level: the scan attributes a nested type to its outermost type) ─────────────────────────────────────────

/// <summary>Must fire: a grant inside an async lambda.</summary>
internal sealed class ExternalTeamShareGuardControl_Granting(IDataverseRecordShareService shares)
{
    public Task Run(Guid id) => Task.Run(async () =>
        await shares.GrantAccessAsync("sprk_events", id, DataversePrincipalRef.User(id), "ReadAccess"));
}

/// <summary>Must fire: a modify inside a delegate.</summary>
internal sealed class ExternalTeamShareGuardControl_Widening(IDataverseRecordShareService shares)
{
    public Func<Guid, Task> Run => id => shares.ModifyAccessAsync("sprk_events", id, DataversePrincipalRef.User(id), "ReadAccess");
}

/// <summary>Must not fire: a revoke only.</summary>
internal sealed class ExternalTeamShareGuardControl_Revoking(IDataverseRecordShareService shares)
{
    public Task Run(Guid id) => shares.RevokeAccessAsync("sprk_events", id, DataversePrincipalRef.User(id));
}
