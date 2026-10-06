// -----------------------------------------------------------------------------
// DataverseEnvironmentRegistryClientUpdateColumnsTests.cs
//
// Wave 2 pre-dispatch remediation punchlist REG-01 (2026-08-27).
//
// Pure-function shape-assertion tests over
// DataverseEnvironmentRegistryClient.BuildColumnsPatchBody — the internal
// static that renders the arbitrary-columns PATCH body consumed by the new
// UpdateColumnsAsync method. No HttpMessageHandler mocks (ADR-038 §5 forbids)
// — we test the wire shape as a pure function and cover live behavior via
// the seam-test approach the sibling PATCH methods use.
//
// COVERAGE:
//   - Empty dict is guarded by UpdateColumnsAsync (returns Success without
//     calling the builder); this file covers the shape function directly.
//   - String, bool, DateTimeOffset (ISO 8601 UTC), int/long, Guid (bare-lower),
//     null (Dataverse clear-column semantics) all render to correct JSON shape.
//   - REG-06 alignment — column names in the dict are used verbatim (caller is
//     responsible for lowercase Dataverse logical names).
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Registry;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Registry;

public class DataverseEnvironmentRegistryClientUpdateColumnsTests
{
    [Fact]
    public void BuildColumnsPatchBody_Emits_Strings_As_JsonStrings()
    {
        var body = DataverseEnvironmentRegistryClient.BuildColumnsPatchBody(
            new Dictionary<string, object?>
            {
                ["sprk_bffversion"] = "1.4.2",
                ["sprk_solutionversion"] = "2.1.0",
            });

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("sprk_bffversion").GetString().Should().Be("1.4.2");
        doc.RootElement.GetProperty("sprk_solutionversion").GetString().Should().Be("2.1.0");
    }

    [Fact]
    public void BuildColumnsPatchBody_Emits_Null_As_JsonNull_ClearColumn()
    {
        var body = DataverseEnvironmentRegistryClient.BuildColumnsPatchBody(
            new Dictionary<string, object?>
            {
                ["sprk_clientcachebusttoken"] = null,
            });

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("sprk_clientcachebusttoken").ValueKind
            .Should().Be(JsonValueKind.Null,
                because: "REG-01 clear-column semantics — null → Dataverse clears the column.");
    }

    [Fact]
    public void BuildColumnsPatchBody_Emits_DateTimeOffset_As_Iso8601_UTC()
    {
        var stamp = new DateTimeOffset(2026, 8, 27, 15, 30, 45, TimeSpan.FromHours(-4));
        var body = DataverseEnvironmentRegistryClient.BuildColumnsPatchBody(
            new Dictionary<string, object?>
            {
                ["sprk_provisionedon"] = stamp,
            });

        using var doc = JsonDocument.Parse(body);
        var value = doc.RootElement.GetProperty("sprk_provisionedon").GetString()!;
        // Round-trip to UTC ISO 8601 (O format ends with Z-like offset).
        DateTimeOffset.Parse(value).ToUniversalTime()
            .Should().Be(stamp.ToUniversalTime(),
                because: "REG-01 must serialize DateTimeOffset to a Dataverse-parseable ISO 8601 UTC string.");
    }

    [Fact]
    public void BuildColumnsPatchBody_Preserves_Column_Name_Casing_Verbatim()
    {
        // REG-06 rule enforced by CALLER — this test locks that the builder
        // does NOT normalize casing itself. A caller supplying a PascalCase
        // column name will send a PascalCase key on the wire (which Dataverse
        // will reject — the fail-loud path). Keeps the builder honest.
        var body = DataverseEnvironmentRegistryClient.BuildColumnsPatchBody(
            new Dictionary<string, object?>
            {
                ["sprk_clientcachebusttoken"] = "abc",   // correct lowercase
                ["SprkPascalName"] = "def",             // wrong casing — passes through
            });

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("sprk_clientcachebusttoken", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("SprkPascalName", out _).Should().BeTrue(
            because: "The builder MUST NOT normalize column-name casing — Dataverse contract enforcement is the caller's job (REG-06).");
    }

    [Fact]
    public void BuildColumnsPatchBody_Emits_Guid_As_Bare_Lowercase_String()
    {
        var g = Guid.Parse("AABBCCDD-1122-3344-5566-778899AABBCC");
        var body = DataverseEnvironmentRegistryClient.BuildColumnsPatchBody(
            new Dictionary<string, object?>
            {
                ["sprk_azuresubscriptionid"] = g,
            });

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("sprk_azuresubscriptionid").GetString()
            .Should().Be("aabbccdd-1122-3344-5566-778899aabbcc",
                because: "ADR-044 — GUIDs canonicalize to bare-lowercase at every boundary.");
    }

    [Fact]
    public void BuildColumnsPatchBody_Emits_Numeric_Types_As_JsonNumbers()
    {
        var body = DataverseEnvironmentRegistryClient.BuildColumnsPatchBody(
            new Dictionary<string, object?>
            {
                ["sprk_intcol"] = 42,
                ["sprk_longcol"] = 9_999_999_999L,
            });

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("sprk_intcol").GetInt32().Should().Be(42);
        doc.RootElement.GetProperty("sprk_longcol").GetInt64().Should().Be(9_999_999_999L);
    }

    [Fact]
    public void BuildColumnsPatchBody_Throws_On_Empty_Column_Name()
    {
        var act = () => DataverseEnvironmentRegistryClient.BuildColumnsPatchBody(
            new Dictionary<string, object?>
            {
                [" "] = "x",
            });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*empty column name*",
                because: "REG-01 fail-loud — an empty column name is a caller bug, not a silent-drop.");
    }

    // -------------------------------------------------------------------------
    // Bucket B MED#3 SESSION 18 (customer-provisioning-orchestration-r1 adversarial
    // e2e verify workflow wepdcb8we): I1 immutability allow-list guard.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("sprk_tenantid")]
    [InlineData("sprk_customerid")]
    [InlineData("sprk_dataverseenvironmentid")]
    [InlineData("SPRK_TENANTID")]                 // case-insensitive guard (OrdinalIgnoreCase)
    [InlineData("SPRK_CustomerId")]
    public void BuildColumnsPatchBody_Refuses_I1_Immutable_Columns_BucketB_MED3(string forbiddenColumn)
    {
        // BucketB MED#3 SESSION 18 baseline: prior behavior silently accepted any
        // column name — a future H14 wiring step that added
        // columns["sprk_tenantid"] = tenantId to a promoted-columns dict would
        // silently violate I1 with no audit trail. The guard bakes I1 into code
        // so a violation fails LOUDLY here + at runtime, not by discipline alone.
        var act = () => DataverseEnvironmentRegistryClient.BuildColumnsPatchBody(
            new Dictionary<string, object?>
            {
                [forbiddenColumn] = "x",
            });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*I1-immutable*",
                because: "Bucket B MED#3 SESSION 18: sprk_tenantid / sprk_customerid / " +
                         "sprk_dataverseenvironmentid are set once at placeholder-create and NEVER " +
                         "re-writable per design.md §4D I1. Any caller that supplies one of these " +
                         "MUST fail LOUDLY — the immutability invariant lives in code, not just docs.");
    }

    [Fact]
    public void BuildColumnsPatchBody_Permits_Mutable_Columns_Alongside_Immutable_Refusal_BucketB_MED3()
    {
        // Belt-and-suspenders: even a mixed dict (mutable + immutable columns)
        // MUST refuse — the guard fires on the immutable entry as soon as the
        // loop visits it. Dictionary iteration order is stable in .NET for
        // OrderedDictionary; for plain Dictionary<string,object?> insertion
        // order is preserved but not guaranteed by contract, so this test only
        // asserts the throw, not which mutable columns got serialized before it.
        var act = () => DataverseEnvironmentRegistryClient.BuildColumnsPatchBody(
            new Dictionary<string, object?>
            {
                ["sprk_bffversion"] = "1.5.0",   // mutable
                ["sprk_tenantid"] = "would-violate-I1",  // I1-immutable
            });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*sprk_tenantid*I1-immutable*",
                because: "Bucket B MED#3: an I1-immutable entry ANYWHERE in the dict aborts the entire " +
                         "PATCH body build — partial writes would leave the runrecord half-updated.");
    }

    [Fact]
    public void ImmutableColumnsBlockList_Contains_ExpectedSetOnly_BucketB_MED3()
    {
        // Change-detector: locks the exact block-list contents so a future
        // well-intentioned addition/removal shows up in code review.
        DataverseEnvironmentRegistryClient.ImmutableColumnsBlockList
            .Should().BeEquivalentTo(new[]
            {
                "sprk_dataverseenvironmentid",
                "sprk_customerid",
                "sprk_tenantid",
            }, because: "Bucket B MED#3 SESSION 18: exactly these three columns are I1-immutable per " +
                        "design.md §4D. Adding a new column here means adding a new immutability invariant " +
                        "to the design doc; removing one means the invariant no longer holds and must be " +
                        "explicitly repealed in a §6.5 resolution.");
    }
}
