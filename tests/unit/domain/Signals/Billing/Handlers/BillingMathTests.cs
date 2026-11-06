using System.Globalization;
using FluentAssertions;
using Sprk.Bff.Api.Services.Signals.Billing.Handlers;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers;

/// <summary>
/// ebilling-r1 task 071, the one rounding rule and the invariant text of the handlers. Maintain-class (ADR-038): pure logic.
/// Money rounds to the cent, a midpoint away from zero.
/// </summary>
public class BillingMathTests
{
    [Theory]
    [InlineData("2.345", "2.35")]
    [InlineData("-2.345", "-2.35")]
    [InlineData("2.344", "2.34")]
    [InlineData("2.355", "2.36")]
    [InlineData("0.005", "0.01")]
    [InlineData("0.004", "0.00")]
    [InlineData("2.5", "2.50")]
    [InlineData("0", "0.00")]
    public void ToCent_RoundsAMidpointAwayFromZero(string value, string expected)
    {
        BillingMath.ToCent(decimal.Parse(value, CultureInfo.InvariantCulture)).Should().Be(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ToCent_IsNotBankersRounding()
    {
        // Banker's rounding would give 2.34 for 2.345 and 2.36 for 2.355; the rule is half away from zero.
        BillingMath.ToCent(2.345m).Should().Be(2.35m);
        BillingMath.ToCent(2.365m).Should().Be(2.37m);
    }

    // ── D-105: the one definition of a credit line ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("-0.1", "575.00")] // negative hours
    [InlineData("-8", null)]
    [InlineData("1.0", "-575.00")] // negative amount
    [InlineData(null, "-50.00")]
    [InlineData("-1.0", "-575.00")] // both
    [InlineData("-0.0001", "0")] // the smallest negative still counts
    [InlineData("1.0", "-0.01")]
    public void IsCreditLine_MustFire_ForNegativeHoursOrANegativeAmount(string? hours, string? amount)
    {
        var line = Fee() with { Hours = hours is null ? null : D(hours), Amount = amount is null ? null : D(amount) };

        BillingMath.IsCreditLine(line).Should().BeTrue();
    }

    [Theory]
    [InlineData("1.0", "575.00")]
    [InlineData("0", "0")] // zero is not negative
    [InlineData("0.0", null)]
    [InlineData(null, "0.00")]
    [InlineData(null, null)] // no hours and no amount: nothing says it is a credit
    [InlineData("0.0001", "0.01")]
    public void IsCreditLine_MustNotFire_ForPositiveZeroOrMissingHoursAndAmount(string? hours, string? amount)
    {
        var line = Fee() with { Hours = hours is null ? null : D(hours), Amount = amount is null ? null : D(amount) };

        BillingMath.IsCreditLine(line).Should().BeFalse();
    }

    [Fact]
    public void IsCreditLine_JudgesAnExpenseLineByItsAmount()
    {
        BillingMath.IsCreditLine(Expense() with { Amount = -50m }).Should().BeTrue();
        BillingMath.IsCreditLine(Expense() with { Amount = 50m }).Should().BeFalse();
    }

    [Theory]
    [InlineData("0.10", "0.1")]
    [InlineData("5.0", "5")]
    [InlineData("13.25", "13.25")]
    [InlineData("0.000", "0")]
    [InlineData("-1.50", "-1.5")]
    public void Number_HasNoTrailingZeros(string value, string expected)
    {
        BillingText.Number(decimal.Parse(value, CultureInfo.InvariantCulture)).Should().Be(expected);
    }

    [Fact]
    public void Money_IsTwoDecimals_AndNullStaysNull()
    {
        BillingText.Money(2.5m).Should().Be("2.50");
        BillingText.Money(2.345m).Should().Be("2.35");
        BillingText.Money(null).Should().BeNull();
        BillingText.Number(null).Should().BeNull();
        BillingText.Date(null).Should().BeNull();
    }

    [Fact]
    public void Text_IsTrimmed_AndBlankIsNull()
    {
        BillingText.Text("  Jane Doe ").Should().Be("Jane Doe");
        BillingText.Text("   ").Should().BeNull();
        BillingText.Text(null).Should().BeNull();
    }

    [Theory]
    [InlineData("  jane   doe ", "JANE DOE")]
    [InlineData("Jane\tDoe", "JANE DOE")]
    [InlineData("i", "I")] // invariant, not the Turkish dotted capital
    public void Key_IsTrimmedWhitespaceCollapsedAndUpperCasedInvariantly(string value, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            BillingText.Key(value).Should().Be(expected);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        BillingText.Key("   ").Should().BeNull();
        BillingText.Key(null).Should().BeNull();
    }

    [Fact]
    public void TheTextOfAValue_DoesNotDependOnTheMachinesCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            BillingText.Number(0.1m).Should().Be("0.1");
            BillingText.Money(1234.5m).Should().Be("1234.50");
            BillingText.Date(new DateOnly(2026, 3, 2)).Should().Be("2026-03-02");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
