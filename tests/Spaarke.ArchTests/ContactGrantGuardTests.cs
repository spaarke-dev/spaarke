using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 140 (#1063) — the contact-side Grant Access routes stay inside the owner's rules, and
/// the issuer column the code binds is the column the schema script creates.
/// </summary>
/// <remarks>
/// <para><b>What these pin, and why structurally.</b> The behavioural suite
/// (<c>tests/integration/auth/UnifiedAccessControl/ContactGrantAuthorizationTests.cs</c>) proves each refusal on a request
/// that reaches it. These guards cover the parts a request cannot reach — what the route files are able to DO at all:</para>
/// <list type="number">
/// <item>No onboarding, contact creation or membership write on the contact-side surface (owner decision G1 (a) /
/// escalation trigger 6): a contact never brings in a new person and never alters an organization membership, on either
/// sign-in plane. A future "invite a new colleague" shortcut would have to delete this guard to compile its way in.</item>
/// <item>The contact-typed issuer column: the navigation property the BFF binds, the column it selects and the target
/// entity are exactly what <c>scripts/Deploy-ExternalRecordAccessContactGrantor.ps1</c> creates. A mismatch is a 400 on
/// every grant read in production (the deploy-order hazard the script documents).</item>
/// </list>
/// <para>Crude by design (<see cref="SourceScan"/>): text, not syntax. Each rule was seeded with its violation and seen to
/// go red before this file was committed (task 140 notes).</para>
/// </remarks>
public class ContactGrantGuardTests
{
    private const string EndpointsFile = "src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ContactGrantEndpoints.cs";
    private const string FilterFile = "src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ContactGrantorAuthorizationFilter.cs";
    private const string LifecycleFile = "src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalGrantLifecycle.cs";
    private const string SchemaScript = "scripts/Deploy-ExternalRecordAccessContactGrantor.ps1";

    private static string CodeOf(string relativePath) =>
        SourceScan.CodeText(File.ReadAllLines(Path.Combine(SourceScan.RepoRoot, relativePath)));

    private static string TextOf(string relativePath) =>
        File.ReadAllText(Path.Combine(SourceScan.RepoRoot, relativePath));

    /// <summary>Calls and entity sets the contact-side surface must never reach.</summary>
    private static readonly (string Pattern, string Why)[] Forbidden =
    {
        (@"\bProvisionAsync\s*\(", "CIAM onboarding (InviteExternalUserEndpoint.ProvisionAsync)"),
        (@"\bCreateCiamUserAsync\s*\(", "creating a CIAM account"),
        (@"\bCreateContactForOidAsync\s*\(", "creating a contact"),
        (@"\bBindOidAsync\s*\(|\bBindInvitedContactAsync\s*\(", "binding a sign-in to a contact"),
        (@"sprk_contactorganization", "reading or writing an organization membership row directly"),
        (@"\bCreateAsync\s*\(\s*""contacts""", "creating a contact through the Dataverse client"),
    };

    [Theory(DisplayName = "Task 140 (owner G1 a): the contact-side surface never onboards, creates a contact or writes a membership")]
    [InlineData(EndpointsFile)]
    [InlineData(FilterFile)]
    public void TheContactSideSurfaceHasNoOnboardingOrMembershipPath(string relativePath)
    {
        var code = CodeOf(relativePath);

        var hits = Forbidden
            .Where(f => Regex.IsMatch(code, f.Pattern, RegexOptions.CultureInvariant))
            .Select(f => f.Why)
            .ToList();

        Assert.True(
            hits.Count == 0,
            $"{relativePath} reaches {string.Join("; ", hits)}. A contact grants only to EXISTING active colleagues of its own " +
            "organization (owner decisions Q2 and G1 (a), session 27): it never creates a person, provisions a sign-in, or " +
            "creates or alters a sprk_contactorganization membership — a membership silently widens every organization-wide " +
            "grant to that organization on every record.");
    }

    [Fact(DisplayName = "Task 140: the issuer column the BFF binds and selects is the one the schema script creates")]
    public void TheIssuerColumnAgreesWithTheSchemaScript()
    {
        var script = TextOf(SchemaScript);
        string ScriptValue(string variable)
        {
            var m = Regex.Match(script, $@"^\${variable}\s*=\s*'([^']+)'", RegexOptions.Multiline);
            Assert.True(m.Success, $"{SchemaScript} no longer declares ${variable}.");
            return m.Groups[1].Value;
        }

        var column = ScriptValue("Column");
        var navigation = ScriptValue("ExpectedNavigationProperty");
        var schemaName = ScriptValue("ColumnSchemaName");
        var target = ScriptValue("TargetEntity");

        var lifecycle = TextOf(LifecycleFile);
        var navConst = Regex.Match(lifecycle, @"GrantedByContactNavigationProperty\s*=\s*""([^""]+)""");
        Assert.True(navConst.Success, "ExternalGrantLifecycle.GrantedByContactNavigationProperty was not found.");

        Assert.Equal("sprk_grantedbycontact", column);
        Assert.Equal(navigation, navConst.Groups[1].Value);
        Assert.Equal(schemaName, navigation);
        Assert.Equal("contact", target);
        Assert.Contains($"_{column}_value", lifecycle, StringComparison.Ordinal);
        Assert.StartsWith("sprk_", column, StringComparison.Ordinal);
    }
}
