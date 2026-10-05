using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Services.Registration;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Registration;

/// <summary>
/// Schema-shape ArchTests for <see cref="DataverseEnvironmentRecord"/>.
///
/// REG-06 (customer-provisioning-orchestration-r1 Wave 2 B24, 2026-08-27):
/// Dataverse logical names are ALWAYS lowercase. The SchemaName of a column can be
/// PascalCase (e.g. <c>sprk_ClientCacheBustToken</c> per the schema-creation script's
/// FR-35 grandfather clause), but the wire-form logical name used in $select, in the
/// PATCH body property key, and in <c>JsonElement.TryGetProperty</c> reads is the
/// lowercase form (<c>sprk_clientcachebusttoken</c>).
///
/// Ground truth for the ClientCacheBustToken column verified via Dataverse MCP describe
/// against admin env spaarkedev1 on 2026-08-27:
///   sprk_clientcachebusttoken NVARCHAR(100)
///
/// These tests prevent regressions where a maintainer paste-adds a PascalCase logical
/// name into AllColumns or MapFromJson — the read path would silently return null
/// (TryGetProperty case-sensitive) and future writes would 400.
/// </summary>
public class DataverseEnvironmentRecordSchemaTests
{
    // Pattern for a Dataverse logical name — publisher prefix + underscore + lowercase
    // alphanumerics/underscores. Chosen to accept the seven system columns
    // (createdon, statecode, etc.) while rejecting any PascalCase segment.
    private static readonly Regex LogicalNamePattern = new(
        "^[a-z][a-z0-9]*_?[a-z0-9_]+$",
        RegexOptions.Compiled);

    [Fact]
    public void AllColumns_AreLowerCaseLogicalNames()
    {
        // ArchTest: every entry in AllColumns matches the lowercase-only pattern.
        // Prevents the REG-06 regression class — a PascalCase column slug slipping
        // into $select would silently break the read path because Dataverse
        // projects lowercase names back regardless of the SchemaName casing.
        foreach (var column in DataverseEnvironmentRecord.AllColumns)
        {
            LogicalNamePattern.IsMatch(column).Should().BeTrue(
                because: $"AllColumns entry '{column}' must be a lowercase Dataverse " +
                         "logical name (schema SchemaName casing is IRRELEVANT on the OData wire — " +
                         "the projected property key is always lowercase). See REG-06.");
        }
    }

    [Fact]
    public void AllColumns_IncludesLowerCaseClientCacheBustToken()
    {
        // Anchor test — REG-06 was specifically the sprk_ClientCacheBustToken casing
        // regression. Pin the exact lowercase form so any future re-introduction of
        // the PascalCase form fails loud here (in addition to the pattern test above).
        DataverseEnvironmentRecord.AllColumns.Should().Contain(
            "sprk_clientcachebusttoken",
            because: "Verified via Dataverse MCP describe (2026-08-27) — the deployed " +
                     "logical name is lowercase, even though the schema SchemaName is PascalCase.");

        DataverseEnvironmentRecord.AllColumns.Should().NotContain(
            "sprk_ClientCacheBustToken",
            because: "PascalCase logical name is a REG-06 regression — Dataverse OData " +
                     "$select / projected keys are lowercase-only.");
    }

    [Fact]
    public void MapFromJson_ReadsClientCacheBustToken_FromLowerCaseKey()
    {
        // The JSON parser is case-sensitive; MapFromJson must look up the lowercase
        // logical-name key. Confirms the read wiring matches AllColumns.
        var payload = """
        {
          "sprk_dataverseenvironmentid": "00000000-0000-0000-0000-000000000001",
          "sprk_isactive": true,
          "sprk_isdefault": false,
          "sprk_clientcachebusttoken": "abc123"
        }
        """;
        using var doc = JsonDocument.Parse(payload);

        var record = DataverseEnvironmentRecord.MapFromJson(doc.RootElement);

        record.ClientCacheBustToken.Should().Be("abc123",
            because: "MapFromJson must read from the lowercase logical-name key (REG-06).");
    }

    [Fact]
    public void MapFromJson_DoesNotReadClientCacheBustToken_FromPascalCaseKey()
    {
        // Reverse-direction pin: a payload carrying the PascalCase key (as an old,
        // broken caller might send it) must be IGNORED — the record's field ends
        // up null. Protects against ambiguous test fixtures papering over the
        // real casing rule.
        var payload = """
        {
          "sprk_dataverseenvironmentid": "00000000-0000-0000-0000-000000000001",
          "sprk_isactive": true,
          "sprk_isdefault": false,
          "sprk_ClientCacheBustToken": "abc123"
        }
        """;
        using var doc = JsonDocument.Parse(payload);

        var record = DataverseEnvironmentRecord.MapFromJson(doc.RootElement);

        record.ClientCacheBustToken.Should().BeNull(
            because: "PascalCase key must NOT round-trip — the Dataverse wire form " +
                     "is lowercase and TryGetProperty is case-sensitive (REG-06).");
    }
}
