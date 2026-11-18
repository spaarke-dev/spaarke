using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Registration;

/// <summary>
/// UAC task 193 (F-final): the CIAM provisioner writes no log line naming the invitee's email or UPN. The real
/// <c>CiamUserProvisioningService</c> cannot run in a test (its Graph client needs a Key Vault secret and a token), so this
/// scans its log calls: no email, recipient or UPN placeholder in a template, and no email, recipient or UPN value among the
/// arguments. (The mailer is checked with the real class: <c>RegistrationEmailInviteTests.NoLogLine_NamesTheInviteeEmail</c>.)
/// MUTATION: putting <c>{Email}</c> and <c>email</c> back into the provisioner's "Created CIAM user" line fails this test.
/// </summary>
public class CiamInviteLogTemplateTests
{
    private static readonly Regex LogCall = new(@"\b_?logger\.Log\w*\((?<args>.*?)\);", RegexOptions.Singleline);
    private static readonly Regex StringLiteral = new("\\$?@?\"(?:[^\"\\\\]|\\\\.)*\"", RegexOptions.Singleline);
    private static readonly Regex PersonPlaceholder = new(@"\{(Email|Recipient|Upn|UserPrincipalName)\b[^}]*\}", RegexOptions.IgnoreCase);
    private static readonly Regex PersonArgument = new(@"\b\w*(email|recipient|upn|userprincipalname)\w*\b", RegexOptions.IgnoreCase);

    [Fact]
    public void TheCiamProvisioner_LogsNoInviteeEmail()
    {
        var path = Path.Combine(RepoRoot(), "src", "server", "api", "Sprk.Bff.Api", "Services", "Registration",
            "CiamUserProvisioningService.cs");

        var hits = PersonLogCalls(File.ReadAllText(path));

        LogCall.Matches(File.ReadAllText(path)).Should().NotBeEmpty("the provisioner logs its create");
        hits.Should().BeEmpty();
    }

    /// <summary>The scanner's must-fire and must-not-fire controls.</summary>
    [Fact]
    public void TheScanner_FlagsAnEmailInATemplateOrAnArgument_AndNothingElse()
    {
        PersonLogCalls("""_logger.LogInformation("Created {Oid} for {Email}", oid, email);""").Should().HaveCount(1);
        PersonLogCalls("""_logger.LogWarning("Created {Oid} for {Who}", oid, request.Email);""").Should().HaveCount(1);
        PersonLogCalls("""logger.LogInformation("Sending | Recipient: {Recipient}", x);""").Should().HaveCount(1);
        PersonLogCalls("""_logger.LogInformation("Created {Oid}. Trace {TraceId}.", oid, traceId);""").Should().BeEmpty();
        PersonLogCalls("""_logger.LogInformation("The email was sent for {Oid}.", oid);""").Should().BeEmpty(
            "the word in the template's text is not a value");
    }

    private static List<string> PersonLogCalls(string source) =>
        LogCall.Matches(source)
            .Select(m => m.Groups["args"].Value)
            .Where(args => StringLiteral.Matches(args).Any(lit => PersonPlaceholder.IsMatch(lit.Value))
                           || PersonArgument.IsMatch(StringLiteral.Replace(args, "\"\"")))
            .ToList();

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            // `.git` is a FILE in a git worktree.
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("The repository root was not found above " + AppContext.BaseDirectory);
    }
}
