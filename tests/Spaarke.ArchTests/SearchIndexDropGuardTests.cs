using System.Text.RegularExpressions;
using System.Xml.Linq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 039, review R6 (FR-38, #2399): on a search-indexed table both index columns
/// (<c>sprk_ai_search_index</c>, <c>sprk_searchindexname</c>) are the SERVER's, written only by step 8 of the unified create. So
/// every BFF code path that turns a client-, model- or playbook-shaped body into a Dataverse write must either drop the index
/// from that body (<c>RecordTableRules.OwnsSearchIndex</c> / <c>WithoutSearchIndex</c> / <c>RemoveSearchIndex</c> / <c>DropSearchIndexFromUpdate</c> / the
/// registry's <c>SearchIndexed</c> flag) or be listed here with a one-line reason it cannot carry one.
/// </summary>
/// <remarks>
/// <para><b>Signal and limits.</b> A source scan of <c>src/server/**</c> code text (line comments stripped). A file is a writer of
/// a client-shaped body when it calls the write-item mapper (<c>DataverseWriteItemMapper.MapAsync</c> /
/// <c>MapWebApiPayloadAsync</c>, the one place a client JSON body becomes a Dataverse body, so a raw <c>PatchAsync</c> of that
/// body is reached through it) or the field-dictionary updaters (<c>UpdateRecordFieldsAsync</c> /
/// <c>UpdateExistingRecordFieldsAsync</c>). Per FILE, not per method: it says the file has the drop, not that every path through
/// it does (the behaviour tests in <c>SearchIndexDefaultTests</c>, <c>SecureChildOwnershipAiToolTests.SearchIndexUpdatePaths</c>
/// and <c>SearchIndexGenericUpdatePathTests</c> pin the paths). A write that builds its body without either entry point (a
/// hand-written <c>HttpClient</c> PATCH) is not seen. Enforced by: this guard (ArchTests in CI and the Stop hook).</para>
/// <para><b>No baseline of violations</b>: the exceptions below are files that never carry a client body, each with its reason,
/// and a stale entry (it no longer matches the signal, or it now has the drop) fails the test, so the list only shrinks.</para>
/// <para><b>Controls.</b> <see cref="Control_AWriterWithoutTheDrop_IsFound"/> (must fire) and
/// <see cref="Control_AWriterWithTheDrop_OrNoWriter_IsNotFound"/> (must not fire) run the same detector over synthetic source.</para>
/// </remarks>
public class SearchIndexDropGuardTests
{
    /// <summary>Files that call a signal entry point without a client body to drop the index from, each with its reason.</summary>
    private static readonly IReadOnlyDictionary<string, string> NotClientBodyWriters = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["src/server/api/Sprk.Bff.Api/Services/Dataverse/DataverseWriteItemMapper.cs"] =
            "The mapper itself; it declares the entry points and drops nothing. Its callers carry the drop.",
        ["src/server/api/Sprk.Bff.Api/Services/Dataverse/OwnedChildWrite.cs"] =
            "Maps server-built columns (the pair, the \"for\" lookup, the re-file's filing columns); the client body reached the route, handler or service first.",
        ["src/server/api/Sprk.Bff.Api/Services/RecordCreation/RecordCreationService.Mapping.cs"] =
            "Re-maps the merged item after RecordCreationService (the same type) has stripped the index at steps 2 and 3.5.",
        ["src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/EmailDraftToolHandler.cs"] =
            "Writes a sprk_communication draft; that table is not search-indexed.",
        ["src/server/api/Sprk.Bff.Api/Services/Dataverse/InvoiceFamilyCoreRecordGuard.cs"] =
            "Every caller drops first (FieldMappingEndpoints push, UpdateRecordActionCore, DataverseUpdateHandler).",
        ["src/server/api/Sprk.Bff.Api/Services/Finance/FinanceRollupService.cs"] =
            "Writes server-computed roll-up columns on the parent; a fixed column set.",
        ["src/server/api/Sprk.Bff.Api/Services/Finance/InvoiceTotalsService.cs"] =
            "Writes server-computed adjustment amounts on invoice lines (not a search-indexed table); a fixed column set.",
        ["src/server/api/Sprk.Bff.Api/Services/Finance/SignalEvaluationService.cs"] =
            "Writes the server-decided owner of a Signal's record; a fixed column set.",
        ["src/server/api/Sprk.Bff.Api/Services/ScorecardCalculatorService.cs"] =
            "Writes server-computed scorecard grades; a fixed column set.",
        ["src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs"] =
            "The Dataverse client that implements the field updaters; every caller carries the drop (or is listed).",
        ["src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs"] =
            "The Web API client that implements the field updaters; every caller carries the drop (or is listed).",
        ["src/server/shared/Spaarke.Dataverse/IFieldMappingDataverseService.cs"] =
            "Declares the field updaters; no caller code.",
    };

    private static readonly Regex WritesAClientShapedBody = new(
        @"DataverseWriteItemMapper\s*\.\s*(?:MapAsync|MapWebApiPayloadAsync)\b|\bMapWebApiPayloadAsync\s*\(|\b(?:UpdateRecordFieldsAsync|UpdateExistingRecordFieldsAsync)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DropsTheSearchIndex = new(
        @"\b(?:OwnsSearchIndex|WithoutSearchIndex|RemoveSearchIndex|DropSearchIndexFromUpdate)\s*\(|\brule\s*\.\s*SearchIndexed\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Whether <paramref name="source"/> calls a signal entry point, and whether it also has the drop.</summary>
    internal static (bool Writes, bool Drops) Classify(string source)
    {
        var code = SourceScan.CodeText(source.Split('\n'));
        return (WritesAClientShapedBody.IsMatch(code), DropsTheSearchIndex.IsMatch(code));
    }

    [Fact(DisplayName = "Task 039 R6: every writer of a client-shaped body drops the search index, or is listed with its reason")]
    public void EveryClientBodyWriter_DropsTheSearchIndex_OrIsListed()
    {
        var problems = new List<string>();
        var writers = new List<string>();
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var relative = SourceScan.Relative(file).Replace('\\', '/');
            var (writes, drops) = Classify(File.ReadAllText(file));
            if (!writes)
                continue;

            writers.Add(relative);
            var listed = NotClientBodyWriters.ContainsKey(relative);
            if (!drops && !listed)
                problems.Add($"{relative}: writes a client-shaped body to Dataverse without dropping the search index (OwnsSearchIndex / WithoutSearchIndex / RemoveSearchIndex)");
            if (drops && listed)
                problems.Add($"STALE entry {relative}: it now drops the search index — remove it from the list (the ratchet).");
        }

        // Non-vacuity: the writers task 039 wired are seen, with the drop.
        foreach (var wired in new[]
                 {
                     "src/server/api/Sprk.Bff.Api/Api/ChildRecordEndpoints.cs",
                     "src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/DataverseCreateRecordHandler.cs",
                     "src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/DataverseUpdateRecordHandler.cs",
                     "src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/ActionCore/UpdateRecordActionCore.cs",
                     "src/server/api/Sprk.Bff.Api/Services/Dataverse/DataverseUpdateHandler.cs",
                     "src/server/api/Sprk.Bff.Api/Services/RecordCreation/RecordCreationService.cs",
                 })
        {
            if (!writers.Contains(wired))
                problems.Add($"{wired}: expected to be seen writing a client-shaped body — the scan lost its signal.");
        }

        foreach (var listed in NotClientBodyWriters.Keys)
        {
            if (!writers.Contains(listed))
                problems.Add($"STALE entry {listed}: it no longer calls a signal entry point — remove it.");
        }

        Assert.True(problems.Count == 0,
            "On a search-indexed table both index columns are the server's (FR-38, #2399): a path that writes a client-, model- or " +
            "playbook-shaped body must drop them first (RecordTableRules.OwnsSearchIndex + WithoutSearchIndex / RemoveSearchIndex), " +
            "or be listed in NotClientBodyWriters with its reason:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "Task 039 R6 control (must fire): a writer of a client body without the drop is found")]
    public void Control_AWriterWithoutTheDrop_IsFound()
    {
        var (mapper, mapperDrops) = Classify(
            "var mapped = await DataverseWriteItemMapper.MapWebApiPayloadAsync(user, entity, body, ct);\nawait user.PatchAsync(path, mapped.Item!.JsonBody, ct);");
        Assert.True(mapper);
        Assert.False(mapperDrops);

        var (fields, fieldsDrops) = Classify("await _records.UpdateExistingRecordFieldsAsync(table, id, fields, ct);");
        Assert.True(fields);
        Assert.False(fieldsDrops);

        // Round 3 (G3): the shared update-path helper only named in a comment, or a lookalike name, is not the drop.
        var (_, helperInComment) = Classify("// DropSearchIndexFromUpdate(table, id, fields, origin, logger);\nawait _records.UpdateRecordFieldsAsync(t, id, f, ct);");
        Assert.False(helperInComment);
        var (_, lookalike) = Classify("await DropSearchIndexFromUpdateLater(x);\nawait DataverseWriteItemMapper.MapAsync(d, t, item, ct);");
        Assert.False(lookalike);

        // A drop that only appears in a comment is not a drop.
        var (_, commentOnly) = Classify("// OwnsSearchIndex(table) is handled elsewhere\nawait DataverseWriteItemMapper.MapAsync(d, t, item, ct);");
        Assert.False(commentOnly);
    }

    [Fact(DisplayName = "Task 039 R6 control (must not fire): a writer with the drop, or no writer at all, is not flagged")]
    public void Control_AWriterWithTheDrop_OrNoWriter_IsNotFound()
    {
        var (writes, drops) = Classify(
            "if (RecordTableRules.OwnsSearchIndex(entity)) { body = RecordTableRules.WithoutSearchIndex(body, origin, dropped); }\n"
            + "var mapped = await DataverseWriteItemMapper.MapWebApiPayloadAsync(user, entity, body, ct);");
        Assert.True(writes);
        Assert.True(drops);

        // Round 3 (G3): the shared update-path helper is a drop.
        var (helperWrites, helperDrops) = Classify(
            "var d = RecordTableRules.DropSearchIndexFromUpdate(table, id, fields, origin, logger);\nawait _records.UpdateRecordFieldsAsync(table, id, fields, ct);");
        Assert.True(helperWrites);
        Assert.True(helperDrops);

        var (registry, registryDrops) = Classify("if (rule.SearchIndexed) { /* fill */ }\nawait _records.UpdateRecordFieldsAsync(t, id, f, ct);");
        Assert.True(registry);
        Assert.True(registryDrops);

        var (other, _) = Classify("// DataverseWriteItemMapper.MapAsync(x)\nvar n = await _service.UpdateRecordAsync(request, ct);");
        Assert.False(other);
    }

    // -- Pass 2 hardening: the navigation property the drop matches by name ---------------------------------------------

    /// <summary>The solution's relationship file for the <c>sprk_aisearchindex</c> lookup.</summary>
    private const string SearchIndexRelationshipsXml = "src/dataverse/solutions/SpaarkeMaster/Other/Relationships/sprk_AISearchIndex.xml";

    /// <summary>
    /// The drop matches body keys BY NAME: <c>RecordTableRules.SearchIndexColumnOf</c> strips <c>@odata.bind</c> and compares what
    /// is left, case-insensitively, with <c>SearchIndexNameResolver.LookupColumn</c>. A bind written under any other navigation
    /// property name would survive the drop, so for every table the registry marks <c>SearchIndexed</c> the lookup's navigation
    /// property (the referencing side of its relationship) must be that name. Every table's is <c>sprk_AI_Search_Index</c> today.
    /// Returns one problem per table that breaks it; empty when the XML agrees with the drop.
    /// </summary>
    internal static IReadOnlyList<string> NavigationPropertyProblems(string relationshipsXml, IEnumerable<string> searchIndexedTables)
    {
        var relationships = XDocument.Parse(relationshipsXml).Descendants("EntityRelationship").ToList();
        var problems = new List<string>();
        foreach (var table in searchIndexedTables)
        {
            var navs = relationships
                .Where(r => string.Equals((string?)r.Element("ReferencingEntityName"), table, StringComparison.OrdinalIgnoreCase)
                            && string.Equals((string?)r.Element("ReferencedEntityName"), "sprk_aisearchindex", StringComparison.OrdinalIgnoreCase)
                            && string.Equals((string?)r.Element("ReferencingAttributeName"), SearchIndexNameResolver.LookupColumn, StringComparison.OrdinalIgnoreCase))
                .SelectMany(r => r.Descendants("EntityRelationshipRole")
                    .Where(role => (string?)role.Element("RelationshipRoleType") == "1")
                    .Select(role => (string?)role.Element("NavigationPropertyName")))
                .ToList();

            if (navs.Count == 0)
            {
                problems.Add($"{table}: no relationship from {table}.{SearchIndexNameResolver.LookupColumn} to sprk_aisearchindex in the solution XML; the registry marks it SearchIndexed.");
                continue;
            }

            foreach (var nav in navs)
            {
                var dropped = RecordTableRules.SearchIndexColumnOf(nav + "@odata.bind");
                if (dropped != SearchIndexNameResolver.LookupColumn)
                    problems.Add($"{table}: the lookup's navigation property is '{nav}', which the search-index drop does not match "
                                 + $"(it matches '{SearchIndexNameResolver.LookupColumn}'); a bind written as '{nav}@odata.bind' would survive the drop.");
            }
        }

        return problems;
    }

    [Fact(DisplayName = "Task 019 pass 2: every SearchIndexed table's lookup navigation property is the name the drop matches")]
    public void EverySearchIndexedTable_NavigationProperty_IsTheNameTheDropMatches()
    {
        var tables = RecordTableRules.All.Where(r => r.SearchIndexed).Select(r => r.Table).ToList();
        Assert.True(tables.Count >= 6, "non-vacuity: the registry marks at least matter, project, document, work assignment, event and invoice");

        var xml = File.ReadAllText(Path.Combine(SourceScan.RepoRoot, SearchIndexRelationshipsXml.Replace('/', Path.DirectorySeparatorChar)));
        var problems = NavigationPropertyProblems(xml, tables);

        Assert.True(problems.Count == 0,
            "The search-index drop matches body keys by name (RecordTableRules.SearchIndexColumnOf); a navigation property under another " +
            "name would let a client's bind through:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "Task 019 pass 2 control (must fire): a relationship under a different navigation property name is found")]
    public void Control_ADifferentNavigationPropertyName_IsFound()
    {
        var fake = """
            <EntityRelationships>
              <EntityRelationship Name="fake">
                <ReferencingEntityName>sprk_Matter</ReferencingEntityName>
                <ReferencedEntityName>sprk_AISearchIndex</ReferencedEntityName>
                <ReferencingAttributeName>sprk_AI_Search_Index</ReferencingAttributeName>
                <EntityRelationshipRoles>
                  <EntityRelationshipRole><NavigationPropertyName>sprk_SearchIndexLookup</NavigationPropertyName><RelationshipRoleType>1</RelationshipRoleType></EntityRelationshipRole>
                  <EntityRelationshipRole><NavigationPropertyName>fake</NavigationPropertyName><RelationshipRoleType>0</RelationshipRoleType></EntityRelationshipRole>
                </EntityRelationshipRoles>
              </EntityRelationship>
            </EntityRelationships>
            """;

        var problems = NavigationPropertyProblems(fake, ["sprk_matter"]);

        Assert.Single(problems);
        Assert.Contains("sprk_SearchIndexLookup", problems[0]);

        // A table with no relationship at all is also a problem, not a silent pass.
        Assert.Single(NavigationPropertyProblems(fake, ["sprk_project"]));
    }

    [Fact(DisplayName = "Task 019 pass 2 control (must not fire): the matching navigation property, in any case, passes")]
    public void Control_TheMatchingNavigationPropertyName_IsNotFound()
    {
        var ok = """
            <EntityRelationships>
              <EntityRelationship Name="ok">
                <ReferencingEntityName>sprk_Matter</ReferencingEntityName>
                <ReferencedEntityName>sprk_AISearchIndex</ReferencedEntityName>
                <ReferencingAttributeName>sprk_AI_Search_Index</ReferencingAttributeName>
                <EntityRelationshipRoles>
                  <EntityRelationshipRole><NavigationPropertyName>sprk_AI_Search_Index</NavigationPropertyName><RelationshipRoleType>1</RelationshipRoleType></EntityRelationshipRole>
                  <EntityRelationshipRole><NavigationPropertyName>other</NavigationPropertyName><RelationshipRoleType>0</RelationshipRoleType></EntityRelationshipRole>
                </EntityRelationshipRoles>
              </EntityRelationship>
            </EntityRelationships>
            """;

        Assert.Empty(NavigationPropertyProblems(ok, ["sprk_matter"]));
    }
}
