// -----------------------------------------------------------------------------
// OpenAiMonthlyLimitRuleTests.cs — task 254 (G37): the rule POST /api/runs applies to the OPTIONAL monthly OpenAI
// spend limit. Absent = no limit; present = a plain decimal in (0, 1,000,000]. The endpoint tests cover the wiring.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Core.Models;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class OpenAiMonthlyLimitRuleTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ValidateOpenAiMonthlyLimit_Absent_IsNoLimit(string? value)
    {
        OpenAiMonthlyLimitRule.Validate(value).Should().Be(new OpenAiMonthlyLimitOutcome.Valid(null));
    }

    [Theory]
    [InlineData("0.01", 0.01)]
    [InlineData("500", 500)]
    [InlineData("1000000", 1000000)]
    public void ValidateOpenAiMonthlyLimit_APlainPositiveDecimal_IsTheLimit(string value, double expected)
    {
        OpenAiMonthlyLimitRule.Validate(value).Should().Be(new OpenAiMonthlyLimitOutcome.Valid((decimal)expected));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.00")]
    [InlineData("-1")]
    [InlineData("1000000.01")]
    [InlineData(" 500")]
    [InlineData("500 ")]
    [InlineData(" ")]
    [InlineData(".5")]
    [InlineData("1e3")]
    [InlineData("1,000")]
    public void ValidateOpenAiMonthlyLimit_AnythingElse_IsRefused(string value)
    {
        OpenAiMonthlyLimitRule.Validate(value).Should().BeOfType<OpenAiMonthlyLimitOutcome.Invalid>()
            .Which.RejectionCode.Should().Be(OpenAiMonthlyLimitRule.InvalidRejectionCode);
    }
}
