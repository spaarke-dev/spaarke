using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Workspace;
using Sprk.Bff.Api.Tests.Domain.Signals;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// Schema-agreement guard for the ADR-024 pair type on <c>sprk_signal</c> and <c>sprk_decisionrecord</c>
/// (spaarke-ontology-platform-r1 task 141; owner request 2026-10-12, enforcement ladder). The code that writes
/// <c>sprk_regardingrecordtype</c> must emit the CLR shape the repo's solution source declares for that column: a lookup
/// (<c>&lt;Type&gt;lookup&lt;/Type&gt;</c> in <c>Entity.xml</c>, target from the relationship whose referencing attribute
/// it is) takes an <see cref="EntityReference"/> to that target; a text column takes a <see cref="string"/>.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> Task 140 changed <c>sprk_signal.sprk_regardingrecordtype</c> from text to a Lookup to
/// <c>sprk_recordtype_ref</c> on spaarkedev1, while the branch writer still wrote the subject's logical name as a string.
/// Dataverse refuses a string in a lookup column, so a BFF built from that code could not create a Signal. Nothing failed
/// the build. This guard does: the Signal value comes from the REAL <see cref="SignalWriter.WriteAsync"/> build path, the
/// Decision Record value from the shared regarding builder on its host table (the path task 142's writer uses), and both are
/// compared with <c>src/dataverse/solutions/SpaarkeMaster</c>.</para>
/// <para><b>Controls.</b> <see cref="Disagreement"/> is exercised on seeded shapes: it fires on a string against a lookup
/// and on a reference to the wrong table (must-fire) and passes the declared shapes (must-not-fire). The declaration parser
/// is exercised on a seeded text declaration. The writer-side mutation (the writer emits the logical name as a string) was
/// run by hand for task 141 and failed <see cref="SignalWriter_WritesThePairTypeInTheShapeTheSolutionDeclares"/>.</para>
/// <para><b>Maintenance.</b> If the column's declared type changes in the solution source, this guard fails until the writer
/// changes with it (or the reverse). Change both in one PR. Do not edit the expected shape here to make it pass.</para>
/// </remarks>
public class SignalPairTypeSchemaAgreementTests
{
    private const string PairTypeColumn = "sprk_regardingrecordtype";

    [Fact]
    public async Task SignalWriter_WritesThePairTypeInTheShapeTheSolutionDeclares()
    {
        var declared = DeclaredType("sprk_Signal");
        var writerOrg = new SignalWriterTests.FakeOrganizationService();
        var writer = SignalWriterTests.Build(
            writerOrg,
            new SignalWriterTests.FakeSysadminClient().WithMatterBusinessUnit(SignalWriterTests.MatterId, SignalWriterTests.BusinessUnitId),
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero)));

        // A typed subject (the matter has a catalog row), so the writer emits a value to check.
        var result = await writer.WriteAsync(SignalWriterTests.MatterSubjectRequest());
        var row = writerOrg.Rows[result.SignalId];

        row.Attributes.Should().ContainKey(PairTypeColumn);
        Disagreement(declared, row[PairTypeColumn]).Should().BeNull();
    }

    [Fact]
    public async Task SharedRegardingBuilder_WritesTheDecisionRecordPairTypeInTheShapeTheSolutionDeclares()
    {
        var declared = DeclaredType("sprk_DecisionRecord");
        var record = new Entity("sprk_decisionrecord");
        var builder = new TodoRegardingBuilder(
            new RecordTypeCatalogStub(RecordTypeCatalogStub.SpaarkeDev1), CoreAncestorResolverFixtures.Inert(),
            NullLogger<TodoRegardingBuilder>.Instance);

        await builder.ApplyResolverFieldsAsync(record, "sprk_matter", SignalWriterTests.MatterId, "REAL-2026-123456.01");

        record.Attributes.Should().ContainKey(PairTypeColumn);
        Disagreement(declared, record[PairTypeColumn]).Should().BeNull();
    }

    [Fact]
    public void TheSolution_DeclaresThePairTypeAsALookupToTheRecordTypeCatalog_OnBothTables()
    {
        // Pins the premise the two guards above check against; if the solution changes, read the class remarks first.
        DeclaredType("sprk_Signal").Should().Be(new ColumnType("lookup", "sprk_recordtype_ref"));
        DeclaredType("sprk_DecisionRecord").Should().Be(new ColumnType("lookup", "sprk_recordtype_ref"));
    }

    /// <summary>
    /// ebilling-r1 task 026 (FR-15, D-94): every subject lookup the shared builder writes on a Signal or Decision Record (the
    /// invoice line's <c>sprk_regardinginvoiceline</c> among them) is declared in the solution source as a lookup to that
    /// subject's table. A subject added to the builder's map before its column is in the solution source fails here.
    /// </summary>
    [Theory]
    [InlineData("sprk_signal", "sprk_Signal")]
    [InlineData("sprk_decisionrecord", "sprk_DecisionRecord")]
    public void EverySubjectLookupTheBuilderWrites_IsDeclaredAsALookupToTheSubjectsTable(string host, string entityFolder)
    {
        var lookups = TodoRegardingBuilder.RegardingLookupsByHostTable[host];
        lookups.Should().Contain("sprk_billingevent", "sprk_regardinginvoiceline");

        foreach (var (subject, column) in lookups)
        {
            DeclaredType(entityFolder, column).Should().Be(new ColumnType("lookup", subject), $"{host}.{column}");
        }
    }

    [Fact]
    public void Disagreement_FiresOnAStringOrAWrongTarget_AndPassesTheDeclaredShapes()
    {
        var lookup = new ColumnType("lookup", "sprk_recordtype_ref");
        var text = new ColumnType("nvarchar", null);

        // Must fire: the pre-141 writer's value against the lookup, and a reference to the wrong table.
        Disagreement(lookup, "sprk_matter").Should().NotBeNull();
        Disagreement(lookup, new EntityReference("sprk_matter", Guid.NewGuid())).Should().NotBeNull();
        Disagreement(text, new EntityReference("sprk_recordtype_ref", Guid.NewGuid())).Should().NotBeNull();

        // Must not fire: each declared shape.
        Disagreement(lookup, new EntityReference("sprk_recordtype_ref", Guid.NewGuid())).Should().BeNull();
        Disagreement(text, "sprk_matter").Should().BeNull();
    }

    [Fact]
    public void DeclaredType_ReadsATextDeclarationAsText_SoAColumnChangedBackToTextWouldBeSeen()
    {
        var entityXml = XDocument.Parse(
            "<Entity><attributes><attribute PhysicalName=\"sprk_RegardingRecordType\"><Type>nvarchar</Type>" +
            "<LogicalName>sprk_regardingrecordtype</LogicalName></attribute></attributes></Entity>");

        DeclaredType(entityXml, "sprk_Signal", Array.Empty<XDocument>()).Should().Be(new ColumnType("nvarchar", null));
    }

    // ── The checker ──────────────────────────────────────────────────────────────────────────────────────────────

    internal sealed record ColumnType(string Type, string? LookupTarget);

    /// <summary>Null when <paramref name="written"/> has the CLR shape <paramref name="declared"/> needs, else why not.</summary>
    internal static string? Disagreement(ColumnType declared, object? written) =>
        declared.Type switch
        {
            "lookup" => written is EntityReference reference
                && string.Equals(reference.LogicalName, declared.LookupTarget, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : $"declared lookup to {declared.LookupTarget}, written {Describe(written)}",
            "nvarchar" or "ntext" => written is string ? null : $"declared text, written {Describe(written)}",
            _ => $"declared type '{declared.Type}' is not one this guard knows; extend it deliberately",
        };

    private static string Describe(object? value) => value switch
    {
        null => "null",
        EntityReference r => $"EntityReference({r.LogicalName})",
        _ => $"{value.GetType().Name} '{value}'",
    };

    private static ColumnType DeclaredType(string entityFolder) => DeclaredType(entityFolder, PairTypeColumn);

    private static ColumnType DeclaredType(string entityFolder, string column)
    {
        var solution = Path.Combine(RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster");
        var entityXml = XDocument.Load(Path.Combine(solution, "Entities", entityFolder, "Entity.xml"));
        var relationships = Directory.GetFiles(Path.Combine(solution, "Other", "Relationships"), "*.xml")
            .Select(XDocument.Load).ToArray();
        return DeclaredType(entityXml, entityFolder, relationships, column);
    }

    private static ColumnType DeclaredType(
        XDocument entityXml, string entitySchemaName, IReadOnlyList<XDocument> relationships, string column = PairTypeColumn)
    {
        var attribute = entityXml.Descendants("attribute")
            .Single(a => string.Equals((string?)a.Element("LogicalName"), column, StringComparison.Ordinal));
        var type = ((string?)attribute.Element("Type"))?.Trim().ToLowerInvariant() ?? string.Empty;
        if (type != "lookup")
        {
            return new ColumnType(type, null);
        }

        var target = relationships
            .SelectMany(r => r.Descendants("EntityRelationship"))
            .Where(r => string.Equals((string?)r.Element("ReferencingEntityName"), entitySchemaName, StringComparison.OrdinalIgnoreCase)
                && string.Equals((string?)r.Element("ReferencingAttributeName"), column, StringComparison.OrdinalIgnoreCase))
            .Select(r => ((string?)r.Element("ReferencedEntityName"))?.Trim().ToLowerInvariant())
            .Should().ContainSingle($"{entitySchemaName}.{column} is a lookup, so exactly one relationship names it").Subject;
        return new ColumnType(type, target);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
