using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ISS-010 part D1 (#3184), uac-r2 Q3 (binding): the child-records route keeps the business-unit and team CONFIGURATION answers
/// for one request (<c>RecordOwnerLookupCache.ForRequest</c>: the Secure Record business unit, the Secure Record owner team,
/// whether a team can be it, each unit's default owner team, the acting user's unit). That is sound only because nothing the
/// decision commit runs creates, changes or deletes a <c>team</c> or <c>businessunit</c> row, or moves a user to another unit.
/// This guard fails any such write on the commit path.
/// </summary>
/// <remarks>
/// <para><b>Scope.</b> The files part C's A2 scan reads (<see cref="DecisionAccessWindowGuardTests.ScannedFiles"/>: every file under
/// <c>Services/Signals</c> and <c>Services/RecordCreation</c>, the step-12 services, the child-record route, the caller's write
/// authorization, the ownership resolver and the external team-share synchronizer), parsed with Roslyn.</para>
/// <para><b>What fails.</b></para>
/// <list type="bullet">
/// <item>A call whose name starts with <c>Create</c>, <c>Update</c>, <c>Delete</c>, <c>Upsert</c>, <c>Post</c>, <c>Patch</c> or
/// <c>Put</c> with a direct argument that names <c>team</c>, <c>teams</c>, <c>businessunit</c> or <c>businessunits</c> (a string,
/// an interpolated string, or a <c>const string</c> of the same file holding one), as a table, an entity set or a Web API path
/// (<c>teams(…)</c>, <c>/businessunits(…)</c>).</item>
/// <item>A <c>Delete…</c> call or a <c>DeleteRequest</c> whose arguments hold <c>new EntityReference("team" | "businessunit", …)</c>.</item>
/// <item><c>new Entity("team" | "businessunit", …)</c> anywhere (such a row is built only to write it), and a
/// <c>CreateRequest</c>, <c>UpdateRequest</c>, <c>UpsertRequest</c> or <c>DeleteRequest</c> that names either table.</item>
/// <item><c>SetBusinessSystemUser</c> in an identifier or a string.</item>
/// <item>A <c>systemuser</c> business-unit write: the text <c>businessunitid@odata.bind</c>, an assignment to
/// <c>x["businessunitid"]</c>, a dictionary initializer <c>["businessunitid"] = …</c> or <c>{ "businessunitid", … }</c>.</item>
/// </list>
/// <para><b>What does not.</b> Reads (<c>RetrieveMultipleAsync</c> over a <c>team</c> query, <c>GetAsync("teams(…)")</c>), an owner
/// assignment of a CHILD row (<c>["ownerid"] = new EntityReference("team", t)</c>, <c>"ownerid@odata.bind": "/teams(…)"</c>), the
/// <c>businessunitid</c> column read or filtered on, and comments or XML docs (trivia). The controls below pin both sides.</para>
/// <para><b>Limits.</b> A name match: a write through a variable whose table name is not a literal or a same-file constant, or
/// through a type outside the scanned files, is not seen (Known limit K of <see cref="DecisionAccessWindowGuardTests"/> applies:
/// the non-child-create executors' services are not scanned). When this guard was added it found no violation (uac-r2's grep of
/// master agreed: the only team writes are <c>RegistrationDataverseService</c> and <c>ExternalCollaboratorPlacement</c>, neither
/// on the commit path). Enforced by: this guard (ArchTests in CI and the Stop hook).</para>
/// </remarks>
public class DecisionCommitTeamWriteGuardTests
{
    private static readonly HashSet<string> Tables = new(StringComparer.OrdinalIgnoreCase) { "team", "teams", "businessunit", "businessunits" };

    private static readonly HashSet<string> EntityTables = new(StringComparer.OrdinalIgnoreCase) { "team", "businessunit" };

    private static readonly string[] WriteVerbs = ["Create", "Update", "Delete", "Upsert", "Post", "Patch", "Put"];

    private static readonly HashSet<string> WriteRequests = new(StringComparer.Ordinal) { "CreateRequest", "UpdateRequest", "UpsertRequest", "DeleteRequest" };

    private const string BusinessUnitColumn = "businessunitid";

    [Fact(DisplayName = "ISS-010 D1 Q3: nothing on the decision commit's path writes a team, a business unit or a user's unit")]
    public void CommitPath_WritesNoTeamOrBusinessUnit()
    {
        var files = DecisionAccessWindowGuardTests.ScannedFiles().ToList();
        Assert.Contains(files, f => f.EndsWith(Path.Combine("Services", "Dataverse", "RecordOwnershipResolver.cs"), StringComparison.Ordinal));
        Assert.Contains(files, f => f.EndsWith(Path.Combine("Services", "Access", "SecureChildShareSynchronizer.cs"), StringComparison.Ordinal));
        Assert.Contains(files, f => f.EndsWith(Path.Combine("Services", "RecordCreation", "RecordCreationService.cs"), StringComparison.Ordinal));

        var problems = files.SelectMany(f => Scan(SourceScan.Relative(f).Replace('\\', '/'), File.ReadAllText(f))).ToList();

        Assert.True(problems.Count == 0,
            "The child-records route keeps business-unit and team answers for one request (ISS-010 D1), so nothing on the decision "
            + "commit's path may write a team or business unit row or a user's business unit (uac-r2 Q3):\n  " + string.Join("\n  ", problems));
    }

    [Fact(DisplayName = "ISS-010 D1 Q3 guard (must fire): each team, business-unit and user-unit write shape")]
    public void Scan_MustFire()
    {
        string[] shapes =
        [
            "await _dataverse.CreateAsync(new Entity(\"team\") { [\"name\"] = \"x\" }, ct);",
            "var e = new Entity(\"businessunit\", id);",
            "await _dataverse.UpdateAsync(\"team\", id, fields, ct);",
            "await _dataverse.UpdateAsync(\"businessunit\", id, null!, ct);",
            "await _dataverse.UpsertAsync(\"teams\", fields, ct);",
            "await _dataverse.DeleteAsync(\"businessunit\", id, ct);",
            "await _org.DeleteAsync(new EntityReference(\"team\", id), ct);",
            "await _webApi.PostAsync(\"teams\", body, ct);",
            "await _webApi.PatchAsync($\"teams({id})\", body, ct);",
            "await _webApi.DeleteAsync($\"/businessunits({id:D})\", ct);",
            "await _webApi.PutAsync($\"teams({id})/name\", body, ct);",
            "await _org.ExecuteAsync(new DeleteRequest { Target = new EntityReference(\"team\", id) }, ct);",
            "await _org.ExecuteAsync(new UpdateRequest { Target = new Entity(\"businessunit\", id) }, ct);",
            "await _org.ExecuteAsync(new SetBusinessSystemUserRequest { BusinessId = bu, UserId = u }, ct);",
            "var name = \"SetBusinessSystemUser\";",
            "await _dataverse.UpdateAsync(\"systemuser\", u, new Dictionary<string, object> { [\"businessunitid\"] = new EntityReference(\"businessunit\", b) }, ct);",
            "var fields = new Dictionary<string, object> { { \"businessunitid\", unit } };",
            "row[\"businessunitid\"] = new EntityReference(\"businessunit\", b);",
            "var body = new Dictionary<string, object> { [\"businessunitid@odata.bind\"] = $\"/businessunits({b})\" };",
            "await _webApi.PatchAsync($\"systemusers({u})\", \"{\\\"businessunitid@odata.bind\\\":\\\"/businessunits(1)\\\"}\", ct);",
        ];
        foreach (var shape in shapes)
            Assert.True(Scan("fixture.cs", Method(shape)).Count > 0, $"not seen: {shape}");

        // A same-file constant that holds the table name counts as the name.
        Assert.NotEmpty(Scan("fixture.cs", "class S { private const string TeamEntity = \"team\"; async Task M() { await _d.UpdateAsync(TeamEntity, id, f, ct); } }"));
        Assert.NotEmpty(Scan("fixture.cs", "class S { private const string Units = \"businessunits\"; async Task M() { await _w.PostAsync(Units, b, ct); } }"));
    }

    [Fact(DisplayName = "ISS-010 D1 Q3 guard (must not fire): reads, a child's owner assignment, the column read, comments")]
    public void Scan_MustNotFire()
    {
        string[] shapes =
        [
            "var teams = await _dataverse.RetrieveMultipleAsync(new QueryExpression(\"team\") { ColumnSet = new ColumnSet(\"teamid\") }, ct);",
            "var q = new QueryExpression(\"businessunit\"); q.Criteria.AddCondition(\"businessunitid\", ConditionOperator.Equal, id);",
            "var r = await _webApi.GetAsync($\"teams({id})?$select=name\", ct);",
            "await _dataverse.UpdateAsync(\"sprk_todo\", id, new Dictionary<string, object> { [\"ownerid\"] = new EntityReference(\"team\", t) }, ct);",
            "await _app.UpdateRecordFieldsAsync(\"sprk_todo\", id, new Dictionary<string, object?> { [\"ownerid@odata.bind\"] = $\"/teams({t})\" }, ct);",
            "await _org.CreateAsync(new Entity(\"sprk_decisionrecord\") { [\"ownerid\"] = new EntityReference(\"team\", t) }, ct);",
            "await _org.ExecuteAsync(new AssignRequest { Target = new EntityReference(\"sprk_todo\", id), Assignee = new EntityReference(\"team\", t) }, ct);",
            "await _org.ExecuteAsync(new UpdateRequest { Target = new Entity(\"sprk_todo\", id) { [\"ownerid\"] = new EntityReference(\"team\", t) } }, ct);",
            "var bu = row.GetAttributeValue<EntityReference>(\"businessunitid\")?.Id;",
            "var columns = new ColumnSet(\"businessunitid\", \"sprk_isexternal\");",
            "await _dataverse.DeleteAsync(\"sprk_todo\", id, ct);",
            "await _org.DeleteAsync(new EntityReference(\"sprk_todo\", id), ct);",
            "var path = $\"teams({id})\"; var answer = await _webApi.GetAsync(path, ct);",
        ];
        foreach (var shape in shapes)
            Assert.True(Scan("fixture.cs", Method(shape)).Count == 0, $"seen, but is no team or unit write: {shape}: {string.Join("; ", Scan("fixture.cs", Method(shape)))}");

        const string comments = """
            /// <summary>Never <c>UpdateAsync("team", id)</c>; never <c>SetBusinessSystemUser</c>; no businessunitid@odata.bind.</summary>
            class S
            {
                // await _dataverse.UpdateAsync("team", id, f, ct); new Entity("businessunit")
                /* row["businessunitid"] = x; DeleteAsync($"teams({id})") */
                void M() { }
            }
            """;
        Assert.Empty(Scan("fixture.cs", comments));
    }

    /// <summary>The problems in <paramref name="source"/> (Roslyn: comments and XML docs are trivia, never seen).</summary>
    internal static IReadOnlyList<string> Scan(string file, string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var constants = root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Where(v => v.Parent?.Parent is FieldDeclarationSyntax field && field.Modifiers.Any(SyntaxKind.ConstKeyword)
                        && v.Initializer?.Value is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression })
            .GroupBy(v => v.Identifier.ValueText, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => ((LiteralExpressionSyntax)g.First().Initializer!.Value).Token.ValueText, StringComparer.Ordinal);
        var problems = new List<string>();
        string At(SyntaxNode node) => $"{file}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}";

        foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = NameOf(call);
            if (name is null || !WriteVerbs.Any(v => name.StartsWith(v, StringComparison.Ordinal)))
                continue;
            foreach (var argument in call.ArgumentList.Arguments)
            {
                if (TextOf(argument.Expression, constants) is { } text && NamesTeamOrUnit(text))
                    problems.Add($"{At(call)} {name}(…) names \"{text}\"");
            }

            if (name.StartsWith("Delete", StringComparison.Ordinal) && EntityReferenceToTeamOrUnit(call.ArgumentList, constants) is { } target)
                problems.Add($"{At(call)} {name}(…) deletes a {target}");
        }

        foreach (var creation in root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
        {
            var type = TypeNameOf(creation);
            if (type == "Entity" && creation.ArgumentList?.Arguments.FirstOrDefault() is { } first
                && TextOf(first.Expression, constants) is { } table && EntityTables.Contains(table))
                problems.Add($"{At(creation)} new Entity(\"{table}\") (a {table} row is built only to write it)");

            if (type is not null && WriteRequests.Contains(type) && RequestTargetTable(creation, constants) is { } named)
                problems.Add($"{At(creation)} new {type} whose Target is a {named}");
        }

        foreach (var token in root.DescendantTokens())
        {
            var text = token.Kind() switch
            {
                SyntaxKind.IdentifierToken => token.ValueText,
                SyntaxKind.StringLiteralToken or SyntaxKind.InterpolatedStringTextToken or SyntaxKind.SingleLineRawStringLiteralToken
                    or SyntaxKind.MultiLineRawStringLiteralToken or SyntaxKind.Utf8StringLiteralToken => token.Text,
                _ => null,
            };
            if (text is null)
                continue;
            if (text.Contains("SetBusinessSystemUser", StringComparison.Ordinal))
                problems.Add($"{At(token.Parent!)} names SetBusinessSystemUser");
            if (text.Contains(BusinessUnitColumn + "@odata.bind", StringComparison.OrdinalIgnoreCase))
                problems.Add($"{At(token.Parent!)} binds a user's businessunitid");
        }

        // x["businessunitid"] = …, and the initializers ["businessunitid"] = … and { "businessunitid", … }.
        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            var index = assignment.Left switch
            {
                ElementAccessExpressionSyntax e => e.ArgumentList.Arguments.FirstOrDefault()?.Expression,
                ImplicitElementAccessSyntax i => i.ArgumentList.Arguments.FirstOrDefault()?.Expression,
                _ => null,
            };
            if (index is not null && string.Equals(TextOf(index, constants), BusinessUnitColumn, StringComparison.OrdinalIgnoreCase))
                problems.Add($"{At(assignment)} writes businessunitid");
        }

        foreach (var pair in root.DescendantNodes().OfType<InitializerExpressionSyntax>().Where(i => i.IsKind(SyntaxKind.ComplexElementInitializerExpression)))
        {
            if (pair.Expressions.FirstOrDefault() is { } key && string.Equals(TextOf(key, constants), BusinessUnitColumn, StringComparison.OrdinalIgnoreCase))
                problems.Add($"{At(pair)} writes businessunitid");
        }

        return problems;
    }

    /// <summary>A table or entity-set name, or a Web API path that addresses one of the two tables.</summary>
    private static bool NamesTeamOrUnit(string text)
    {
        var path = text.Trim().TrimStart('/');
        if (Tables.Contains(path))
            return true;
        var open = path.IndexOf('(', StringComparison.Ordinal);
        return open > 0 && Tables.Contains(path[..open]);
    }

    /// <summary>The table a write request's <c>Target = new Entity(…)</c> or <c>new EntityReference(…)</c> names, when it is a team or
    /// business unit (a child row's owner team set inside the target is not the target).</summary>
    private static string? RequestTargetTable(BaseObjectCreationExpressionSyntax request, IReadOnlyDictionary<string, string> constants) =>
        request.Initializer?.Expressions.OfType<AssignmentExpressionSyntax>()
            .Where(a => a.Left is IdentifierNameSyntax { Identifier.ValueText: "Target" })
            .Select(a => a.Right is BaseObjectCreationExpressionSyntax target && TypeNameOf(target) is "Entity" or "EntityReference"
                         && target.ArgumentList?.Arguments.FirstOrDefault() is { } first
                ? TextOf(first.Expression, constants)
                : null)
            .FirstOrDefault(t => t is not null && EntityTables.Contains(t));

    /// <summary>The table a <c>new EntityReference("team" | "businessunit", …)</c> among <paramref name="arguments"/> names, if any.</summary>
    private static string? EntityReferenceToTeamOrUnit(ArgumentListSyntax arguments, IReadOnlyDictionary<string, string> constants) =>
        arguments.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>()
            .Where(c => TypeNameOf(c) == "EntityReference")
            .Select(c => c.ArgumentList?.Arguments.FirstOrDefault() is { } a ? TextOf(a.Expression, constants) : null)
            .FirstOrDefault(t => t is not null && EntityTables.Contains(t));

    /// <summary>The text of a string literal, the literal parts of an interpolated string, or a same-file <c>const string</c>.</summary>
    private static string? TextOf(ExpressionSyntax expression, IReadOnlyDictionary<string, string> constants) => expression switch
    {
        LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } literal => literal.Token.ValueText,
        InterpolatedStringExpressionSyntax interpolated => string.Concat(interpolated.Contents.Select(c =>
            c is InterpolatedStringTextSyntax t ? t.TextToken.ValueText : "{}")),
        IdentifierNameSyntax id when constants.TryGetValue(id.Identifier.ValueText, out var value) => value,
        _ => null,
    };

    private static string? TypeNameOf(BaseObjectCreationExpressionSyntax creation) => creation switch
    {
        ObjectCreationExpressionSyntax { Type: IdentifierNameSyntax id } => id.Identifier.ValueText,
        ObjectCreationExpressionSyntax { Type: QualifiedNameSyntax q } => q.Right.Identifier.ValueText,
        _ => null,
    };

    private static string? NameOf(InvocationExpressionSyntax call) => call.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        IdentifierNameSyntax n => n.Identifier.ValueText,
        GenericNameSyntax g => g.Identifier.ValueText,
        _ => null,
    };

    private static string Method(string statement) => $$"""
        class S
        {
            async Task M(Guid id, Guid t, Guid u, Guid b, Guid bu, object body, object fields, Entity row, CancellationToken ct)
            {
                {{statement}}
            }
        }
        """;
}
