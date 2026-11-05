using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.FinanceSubmission;

/// <summary>
/// ebilling-r1 082c-2 (uac-r2 (a), (b)): <c>scripts/ebilling/Deploy-EbillingInvoiceReviewDetailColumn.ps1</c> creates the column the BFF writes
/// (<see cref="InvoiceReviewDetail.Column"/>) as a LOCAL choice with exactly the option values and labels pinned in code
/// (<see cref="InvoiceReviewDetail.Options"/>), and its <c>-Verify</c> checks the invariant (non-blank only while In Review). Read as text (no
/// PowerShell host, no network), as <see cref="TrackingNumberPreviousSequenceColumnAgreementTests"/>; each pin is checked against a seeded drift.
/// </summary>
public class InvoiceReviewDetailScriptAgreementTests
{
    private const string Script = "Deploy-EbillingInvoiceReviewDetailColumn.ps1";

    [Fact(DisplayName = "082c-2: the script's table, column and options are the BFF's (InvoiceReviewDetail), value by value")]
    public void TheScriptsColumnAndOptions_AreTheBffs()
    {
        var script = ReadScript();

        Single(script, @"^\s*\$Table\s*=\s*'([^']+)'").Should().Be(InvoiceReviewService.InvoiceEntity);
        Single(script, @"^\s*\$Column\s*=\s*'([^']+)'").Should().Be(InvoiceReviewDetail.Column);
        Single(script, @"^\s*\$InReviewStatusCode\s*=\s*(\d+)").Should().Be(InvoiceStatusCodes.InReview.ToString());
        OptionsOf(script).Should().Equal(InvoiceReviewDetail.Options, "the script creates exactly the pinned values and labels, in order");
    }

    [Fact(DisplayName = "082c-2: the options parser sees a drifted value, label or extra option")]
    public void TheOptionsParser_SeesEachDrift()
    {
        var script = ReadScript().ReplaceLineEndings("\n");

        OptionsOf(script.Replace("659490001 = 'Billing terms could not be checked'", "659490003 = 'Billing terms could not be checked'"))
            .Should().NotEqual(InvoiceReviewDetail.Options);
        OptionsOf(script.Replace("'Waiting for billing terms'", "'Waiting for terms'")).Should().NotEqual(InvoiceReviewDetail.Options);
        OptionsOf(script.Replace("    659490002 = 'Waiting for the firm to be confirmed'\n", "    659490002 = 'Waiting for the firm to be confirmed'\n    659490004 = 'On hold'\n"))
            .Should().NotEqual(InvoiceReviewDetail.Options);
    }

    // Behaviours no host runs in a unit test, pinned as source facts, each against a seeded drift.
    [Theory(DisplayName = "082c-2: the script keeps a local choice, its -Verify gaps and invariant check, its entity-scoped publish and its refusal to run without a mode")]
    [InlineData(@"IsGlobal\s*=\s*\$false", "IsGlobal      = $false", "IsGlobal      = $true")]
    [InlineData(@"OptionSet\.IsGlobal -eq \$true", "OptionSet.IsGlobal -eq $true", "OptionSet.IsGlobal -eq $null")]
    [InlineData(@"\$attr\.IsSecured -eq \$true", "$attr.IsSecured -eq $true", "$attr.IsSecured -eq $null")]
    [InlineData(@"\$Column ne null and statuscode ne \$InReviewStatusCode", "$Column ne null and statuscode ne $InReviewStatusCode", "$Column ne null")]
    [InlineData(@"has an extra option", "has an extra option", "has another option")]
    [InlineData(@"<entities><entity>\$Table</entity></entities>", "<entities><entity>$Table</entity></entities>", "<entities></entities>")]
    [InlineData(@"if \(-not \$Apply -and -not \$Verify -and -not \$DryRun\) \{ throw", "-not $Apply -and -not $Verify -and -not $DryRun", "-not $Apply -and -not $Verify")]
    public void TheScript_KeepsItsBehaviours(string pattern, string from, string to)
    {
        var script = ReadScript().ReplaceLineEndings("\n");

        Regex.IsMatch(script, pattern).Should().BeTrue("the shipped script has this behaviour");
        script.Should().Contain(from);
        Regex.IsMatch(script.Replace(from, to), pattern).Should().BeFalse("a seeded drift of it must fail the pin");
        script.Should().NotContain("PublishAllXml", "the publish is entity-scoped only");
    }

    private static List<(int Value, string Label)> OptionsOf(string script) =>
        Regex.Matches(script, @"^\s*(\d{9})\s*=\s*'([^']+)'", RegexOptions.Multiline)
            .Select(m => (int.Parse(m.Groups[1].Value), m.Groups[2].Value))
            .ToList();

    private static string? Single(string text, string pattern) =>
        Regex.Match(text, pattern, RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;

    private static string ReadScript() => File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", Script));

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
