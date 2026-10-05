// -----------------------------------------------------------------------------
// TenancyModelParserTests.cs
//
// TASK 223 (2026-09-29) introduced these tests to pin the D-12 parse-or-reject
// contract + the H12c idempotency-key byte-for-byte preservation invariant.
// TASK 224 (2026-09-29 EOD; INCOMING §5 Item 3) RETIRED that preservation
// invariant per owner-decision Q2 (greenfield state: no completed H12c phases
// in production Cosmos) and renamed enum members Model1Shared→Model1 +
// Model2Dedicated→Model2. Tests are re-pinned to the NEW string values.
//
// TEST SCOPE:
//   - Exact-match parse for both defined enum members returns true + typed value
//   - Null / empty / whitespace input returns false + default enum
//   - Case-sensitive: wrong-case input returns false
//   - Unknown non-blank value returns false — INCLUDES the retired pre-T224
//     names "Model1Shared" / "Model2Dedicated" (they are now unknown values)
//   - Parse (throwing overload) throws TenancyModelParseException on failure
//     and returns the enum on success
//   - Enum.ToString() round-trips to the post-T224 string literals ("Model1" /
//     "Model2") — H12c embeds this in its idempotency key
//   - Numeric strings ("0", "1") do NOT parse — Enum.TryParse alone would accept
//     them, but the parser scans Enum.GetNames + string.Equals(Ordinal) so it
//     admits only defined enum-member NAMES (never their numeric values)
//
// NOT COVERED (out of scope for T223/T224):
//   - JSON serialization behaviour (no [JsonConverter] on the enum by design —
//     ProvisioningRun.TenancyModel stays a string field for Cosmos wire-format
//     preservation; adding a converter would risk breaking existing rows)
//   - FormatExpectedValues formatting stability (a smoke test would risk
//     freezing the diagnostic wording; the enum's name list is the ground truth)
// -----------------------------------------------------------------------------

using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Core.Models;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Models;

public sealed class TenancyModelParserTests
{
    // ---------- TryParse: happy path ----------

    [Fact]
    public void TryParse_ExactMatch_Model1_ReturnsTrue()
    {
        var ok = TenancyModelParser.TryParse("Model1", out var parsed);

        ok.Should().BeTrue();
        parsed.Should().Be(TenancyModel.Model1);
    }

    [Fact]
    public void TryParse_ExactMatch_Model2_ReturnsTrue()
    {
        var ok = TenancyModelParser.TryParse("Model2", out var parsed);

        ok.Should().BeTrue();
        parsed.Should().Be(TenancyModel.Model2);
    }

    // ---------- TryParse: rejection paths ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void TryParse_NullOrWhitespace_ReturnsFalse(string? input)
    {
        var ok = TenancyModelParser.TryParse(input, out var parsed);

        ok.Should().BeFalse();
        parsed.Should().Be(default(TenancyModel));
    }

    [Theory]
    [InlineData("model1")]  // wrong case
    [InlineData("MODEL1")]  // wrong case
    [InlineData("model2")]
    [InlineData("MODEL2")]
    [InlineData("Model1 ")]  // trailing whitespace
    [InlineData(" Model2")]  // leading whitespace
    public void TryParse_WrongCaseOrPadded_ReturnsFalse(string input)
    {
        // Case-sensitivity is load-bearing: H12c's idempotency-key format embeds this
        // string byte-for-byte; permitting "model2" downstream would either silently
        // duplicate H12c work under a new key, or (with normalization) break replay.
        var ok = TenancyModelParser.TryParse(input, out var parsed);

        ok.Should().BeFalse();
        parsed.Should().Be(default(TenancyModel));
    }

    [Theory]
    [InlineData("Model3Future")]
    [InlineData("SomeOtherValue")]
    [InlineData("Model1Shared")]     // T224 retired name — now unknown
    [InlineData("Model2Dedicated")]  // T224 retired name — now unknown
    public void TryParse_UnknownValue_ReturnsFalse(string input)
    {
        // Post-T224 the enum members are Model1/Model2 — the pre-T224 names
        // (Model1Shared / Model2Dedicated) are UNKNOWN to the parser. This pins
        // that regression state: if the parser ever silently re-accepts the old
        // labels (e.g. via a case-insensitive fallback or a legacy alias table),
        // owner Q2's H12c invalidation reasoning breaks.
        var ok = TenancyModelParser.TryParse(input, out var parsed);

        ok.Should().BeFalse();
        parsed.Should().Be(default(TenancyModel));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2")]
    public void TryParse_NumericString_ReturnsFalse(string input)
    {
        // Enum.TryParse alone accepts numeric strings ("0" → Model1, "1" → Model2)
        // which would silently accept a legacy integer-serialised tenancyModel value.
        // The parser scans Enum.GetNames + string.Equals(Ordinal) instead, so it
        // matches only defined enum-member NAMES (never numeric values).
        var ok = TenancyModelParser.TryParse(input, out var parsed);

        ok.Should().BeFalse();
        parsed.Should().Be(default(TenancyModel));
    }

    // ---------- Parse (throwing overload) ----------

    [Fact]
    public void Parse_ExactMatch_Model1_ReturnsEnum()
    {
        TenancyModelParser.Parse("Model1").Should().Be(TenancyModel.Model1);
    }

    [Fact]
    public void Parse_ExactMatch_Model2_ReturnsEnum()
    {
        TenancyModelParser.Parse("Model2").Should().Be(TenancyModel.Model2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("model1")]
    [InlineData("Unknown")]
    [InlineData("Model1Shared")]  // T224-retired name
    public void Parse_InvalidInput_ThrowsTenancyModelParseException(string? input)
    {
        var act = () => TenancyModelParser.Parse(input);

        var ex = act.Should().Throw<TenancyModelParseException>().Which;
        ex.OffendingValue.Should().Be(input);
        ex.Message.Should().Contain("Model1").And.Contain("Model2");
    }

    // ---------- BINDING invariant: H12c idempotency-key format preservation (T224 restated) ----------
    //
    // These two tests are the tripwire for the H12c idempotency-key format:
    // the enum's .ToString() output MUST equal the CURRENT-post-T224 string literal
    // byte-for-byte. T223's tripwire pinned "Model1Shared"/"Model2Dedicated"; T224
    // re-pins to "Model1"/"Model2" per owner-decision Q1. Owner-decision Q2 explicitly
    // retired T223's "preserve historical strings" clause (greenfield state — no
    // completed H12c phases to preserve), but the byte-for-byte equality between
    // enum-name and idempotency-key-string embed still matters for future consistency.

    [Fact]
    public void EnumToString_Model1_EqualsPostT224Literal()
    {
        TenancyModel.Model1.ToString().Should().Be(
            "Model1",
            because: "H12c idempotency-key format h12c-{customerId}-{tenancyModel}-{endpointHash} " +
                     "embeds this string verbatim. Post-T224 keys use 'Model1' as the literal. " +
                     "T223's 'preserve historical Model1Shared' invariant was retired by owner-decision Q2 " +
                     "(greenfield state — no completed H12c phases exist to preserve).");
    }

    [Fact]
    public void EnumToString_Model2_EqualsPostT224Literal()
    {
        TenancyModel.Model2.ToString().Should().Be(
            "Model2",
            because: "H12c idempotency-key format h12c-{customerId}-{tenancyModel}-{endpointHash} " +
                     "embeds this string verbatim. Post-T224 keys use 'Model2' as the literal. " +
                     "T223's 'preserve historical Model2Dedicated' invariant was retired by owner-decision Q2 " +
                     "(greenfield state — no completed H12c phases exist to preserve).");
    }

    // ---------- Round-trip: enum → string → enum ----------

    [Theory]
    [InlineData(TenancyModel.Model1)]
    [InlineData(TenancyModel.Model2)]
    public void RoundTrip_EnumToStringToEnum_Preserves(TenancyModel original)
    {
        // Belt-and-braces: what handlers pass through Cosmos as string MUST land back
        // as the same enum member. If a future enum addition breaks this, add a case
        // to the parser's TryParse (do NOT relax case-sensitivity).
        var asString = original.ToString();
        TenancyModelParser.TryParse(asString, out var parsed).Should().BeTrue();
        parsed.Should().Be(original);
    }
}
