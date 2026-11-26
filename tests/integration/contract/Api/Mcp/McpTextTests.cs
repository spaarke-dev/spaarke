// CONTRACT — McpText.SingleLine, the one-line rule of every MCP tool's text content (spaarkeai-mcp-bff-api-r2 tasks 023-025 second-review
// fix; spec NFR-07). KEEP path: endpoint-contract.
//
// WHAT RUNS: the PRODUCTION McpText, with no host. NOT HERE: the tools' own text lines (ListPlaybooksToolTests, TaskReadToolsContractTests,
// DocumentToolsContractTests), which prove a forged row cannot be made through each tool.

using FluentAssertions;
using Sprk.Bff.Api.Api.Mcp;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Mcp;

public sealed class McpTextTests
{
    // Written as numbers: a literal U+2028, U+2029 or U+0085 in source is itself a line break to editors and to the C# lexer.
    private static readonly string LineSeparator = ((char)0x2028).ToString();
    private static readonly string ParagraphSeparator = ((char)0x2029).ToString();
    private static readonly string NextLine = ((char)0x0085).ToString();

    public static TheoryData<string, string> LineBreakingInputs => new()
    {
        { "a\nb", "a b" },
        { "a\r\nb", "a  b" },
        { "a\tb", "a b" },
        { "a" + LineSeparator + "b", "a b" },
        { "a" + ParagraphSeparator + "b", "a b" },
        { "a" + NextLine + "b", "a b" },
        { "a\rb\fc\vd\0e", "a b c d e" },
    };

    [Theory]
    [MemberData(nameof(LineBreakingInputs))]
    public void SingleLine_TurnsEveryLineBreakingCharacterIntoASpace(string value, string expected) =>
        McpText.SingleLine(value).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("plain name")]
    [InlineData("  spaced  and  untouched  ")]
    [InlineData("語 café")]
    public void SingleLine_LeavesAnyOtherTextExactlyAsItIs(string value) =>
        McpText.SingleLine(value).Should().Be(value);

    [Fact]
    public void SingleLine_OfNull_IsTheEmptyString() => McpText.SingleLine(null).Should().BeEmpty();

    [Fact]
    public void SingleLine_OfAnyInput_HasNoCharacterThatCouldStartALine()
    {
        var all = new string(Enumerable.Range(0, 0x3000).Select(i => (char)i).Where(c => !char.IsSurrogate(c)).ToArray());

        var one = McpText.SingleLine(all);

        one.Length.Should().Be(all.Length, "a character is replaced, never removed");
        one.Any(c => char.IsControl(c) || c == (char)0x2028 || c == (char)0x2029).Should().BeFalse();
    }
}
