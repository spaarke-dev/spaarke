using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 036 option C (owner 2026-10-20): no handler file of the <c>/api/v1/external</c> plane
/// (<c>src/server/api/Sprk.Bff.Api/Api/ExternalAccess/External*.cs</c>) calls a token-taking (OBO) question of
/// <c>CallerRecordAccessProbe</c> — <c>GetCallerRightsAsync</c>, <c>GetCallerRightsForRecordsAsync</c>,
/// <c>GetCallerRightsForAuthorizationGateAsync</c>, <c>CallerHoldsPrivilegeAsync</c>, <c>GetCallerSystemUserIdAsync</c> —
/// or any other OBO entry point the BFF has (the MSAL exchange itself, the Graph <c>ForUser*</c> clients, the
/// SpeFileStore <c>*AsUserAsync</c> methods, the OBO Dataverse client <c>IDataverseUserClient</c>).
/// </summary>
/// <remarks>
/// <para><b>Why.</b> ADR-028 A1–A3 (restated by A4, A5, A8 and A9) forbid exchanging the caller's token for a downstream
/// token on this plane. The plane's write confirmation asks Dataverse app-only, naming the resolved systemuser
/// (<c>GetPrincipalRightsAppOnlyAsync</c> / <c>PrincipalHoldsPrivilegeAppOnlyAsync</c>, A5's mechanism). The OBO questions
/// stay on the probe for the internal routes. Before option C the plane's readers did call the OBO questions for
/// Spaarke-tenant systemusers (<c>ExternalProjectDataEndpoints.CallerRightsReader</c> / <c>CallerPrivilegeReader</c>),
/// and every behaviour test still passed — so the rule lives here, where the edit that reintroduces it fails the build
/// (CLAUDE.md §16 enforcement ladder). Enforced by: this guard (ArchTests, CI and the Stop hook).</para>
/// <para><b>Scope, and what it does not prove.</b> A source scan of the plane's own handler files, with line comments
/// stripped (<see cref="SourceScan"/>); it does not follow calls into services those handlers use. Today none of those
/// services performs OBO (task 036 note <c>036-obo-resolution.md</c> §1–§2), and the readers above are the plane's only
/// route to the probe.</para>
/// <para>Each rule has a must-fire and a must-not-fire control below, and a vacuity check on the file set.</para>
/// </remarks>
public class ExternalPlaneNoOboGuardTests
{
    private const string ExternalAccessDir = "src/server/api/Sprk.Bff.Api/Api/ExternalAccess";

    /// <summary>The files that must be in the scanned set, so the guard can never pass by scanning nothing.</summary>
    private static readonly string[] MustBeScanned = { "ExternalProjectDataEndpoints.cs", "ExternalMessageEndpoints.cs" };

    private static readonly (Regex Pattern, string Why)[] Forbidden =
    {
        (new Regex(
                @"\b(GetCallerRightsAsync|GetCallerRightsForRecordsAsync|GetCallerRightsForAuthorizationGateAsync|CallerHoldsPrivilegeAsync|GetCallerSystemUserIdAsync)\b",
                RegexOptions.CultureInvariant),
            "a token-taking (OBO) CallerRecordAccessProbe question — use GetPrincipalRightsAppOnlyAsync / "
            + "PrincipalHoldsPrivilegeAppOnlyAsync with the resolved systemuser (ExternalProjectDataEndpoints.CallerRightsReader)"),
        (new Regex(@"\b(AcquireTokenOnBehalfOf|CreateOnBehalfOfClientAsync)\b", RegexOptions.CultureInvariant),
            "an on-behalf-of token exchange"),
        (new Regex(@"\b(ForUserAsync|ForUserBetaAsync)\b", RegexOptions.CultureInvariant),
            "a Graph client built on behalf of the caller (GraphClientFactory OBO)"),
        (new Regex(@"\b\w+AsUserAsync\b", RegexOptions.CultureInvariant),
            "an SPE / Graph call made as the user (SpeFileStore *AsUserAsync, OBO)"),
        (new Regex(@"\bI?DataverseUserClient\b", RegexOptions.CultureInvariant),
            "the OBO Dataverse client (IDataverseUserClient)"),
    };

    /// <summary>The rule over one file's code text (line comments stripped): each violation, with its line.</summary>
    internal static IReadOnlyList<string> Violations(string code)
    {
        var violations = new List<string>();
        foreach (var (pattern, why) in Forbidden)
        {
            foreach (Match match in pattern.Matches(code))
            {
                var line = code.AsSpan(0, match.Index).Count('\n') + 1;
                violations.Add($"line {line}: `{match.Value}` — {why}");
            }
        }

        return violations;
    }

    /// <summary>The plane's handler files: <c>Api/ExternalAccess/External*.cs</c>.</summary>
    internal static IReadOnlyList<string> ScannedFiles() =>
        Directory.EnumerateFiles(Path.Combine(SourceScan.RepoRoot, ExternalAccessDir), "External*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

    [Fact(DisplayName = "Task 036 C: no /api/v1/external handler file reaches an OBO question or exchange (ADR-028 A1-A3)")]
    public void ExternalPlaneHandlers_CallNoOboEntryPoint()
    {
        var files = ScannedFiles();

        var violations = files
            .SelectMany(file => Violations(SourceScan.CodeText(File.ReadAllLines(file)))
                .Select(v => $"{Path.GetRelativePath(SourceScan.RepoRoot, file).Replace('\\', '/')} {v}"))
            .ToList();

        Assert.True(violations.Count == 0,
            "The /api/v1/external plane must never exchange the caller's token (ADR-028 A1-A3; task 036 option C). "
            + "Confirm a systemuser's write app-only, naming the resolved systemuser:\n  " + string.Join("\n  ", violations));
    }

    [Fact(DisplayName = "Task 036 C guard, vacuity control: the scanned set holds the plane's handler files")]
    public void Control_TheScannedSet_IsNotEmpty_AndHoldsTheKnownHandlers()
    {
        var names = ScannedFiles().Select(Path.GetFileName).ToList();

        Assert.NotEmpty(names);
        foreach (var required in MustBeScanned)
        {
            Assert.Contains(required, names);
        }
    }

    // ── Controls ──────────────────────────────────────────────────────────────────────────────────────

    private const string Sanctioned = """
        internal static Func<string, Guid, CancellationToken, Task<AccessRights>> CallerRightsReader(
            CallerRecordAccessProbe probe, HttpContext httpContext)
        {
            var systemUserId = AppOnlyConfirmationPrincipal(GetCallerPrincipal(httpContext));
            return (entitySet, recordId, ct) => probe.GetPrincipalRightsAppOnlyAsync(systemUserId, entitySet, recordId, ct);
        }

        internal static Func<string, CancellationToken, Task<bool>> CallerPrivilegeReader(
            CallerRecordAccessProbe probe, HttpContext httpContext)
        {
            var systemUserId = AppOnlyConfirmationPrincipal(GetCallerPrincipal(httpContext));
            return (privilege, ct) => probe.PrincipalHoldsPrivilegeAppOnlyAsync(systemUserId, privilege, ct);
        }

        var stream = await speFileStore.DownloadFileAsync(driveId, itemId, ct);
        """;

    private static IReadOnlyList<string> ViolationsOf(string source) => Violations(SourceScan.CodeText(source.Split('\n')));

    [Fact(DisplayName = "Task 036 C guard, must-not-fire control: the sanctioned app-only readers pass")]
    public void Control_TheSanctionedAppOnlyShape_HasNoViolation()
        => Assert.Empty(ViolationsOf(Sanctioned));

    [Fact(DisplayName = "Task 036 C guard, must-not-fire control: a comment naming an OBO question is ignored")]
    public void Control_ACommentNamingAnOboQuestion_HasNoViolation()
        => Assert.Empty(ViolationsOf(Sanctioned
            + "\n// never probe.GetCallerRightsAsync(token, ...) or CallerHoldsPrivilegeAsync here"
            + "\n/// <see cref=\"CallerRecordAccessProbe.GetCallerSystemUserIdAsync\"/> serves internal routes only\n"));

    [Theory(DisplayName = "Task 036 C guard, must-fire control: each OBO question or exchange is caught")]
    [InlineData("return (entitySet, recordId, ct) => probe.GetCallerRightsAsync(token, entitySet, recordId, ct);")]
    [InlineData("return (privilege, ct) => probe.CallerHoldsPrivilegeAsync(token, privilege, ct);")]
    [InlineData("var rights = await probe.GetCallerRightsForRecordsAsync(token, \"sprk_projects\", ids, ct);")]
    [InlineData("var rights = await probe.GetCallerRightsForAuthorizationGateAsync(token, \"sprk_projects\", id, ct);")]
    [InlineData("var me = await probe.GetCallerSystemUserIdAsync(token, ct);")]
    [InlineData("Func<string?, string, Guid, CancellationToken, Task<AccessRights>> ask = probe.GetCallerRightsAsync;")]
    [InlineData("var result = await app.AcquireTokenOnBehalfOf(scopes, new UserAssertion(token)).ExecuteAsync(ct);")]
    [InlineData("var graph = await graphFactory.ForUserAsync(httpContext, ct);")]
    [InlineData("var stream = await speFileStore.DownloadFileAsUserAsync(httpContext, driveId, itemId, ct);")]
    [InlineData("IDataverseUserClient userClient,")]
    public void Control_AnOboEntryPoint_IsCaught(string statement)
        => Assert.NotEmpty(ViolationsOf(Sanctioned + "\n" + statement + "\n"));

    [Fact(DisplayName = "Task 036 C guard, must-fire control: the pre-option-C readers (OBO for a Spaarke-tenant systemuser) are caught")]
    public void Control_ThePreOptionCReaders_AreCaught()
    {
        const string preOptionC = """
            if (AppOnlyConfirmationPrincipal(GetCallerPrincipal(httpContext)) is { } systemUserId)
            {
                return (entitySet, recordId, ct) => probe.GetPrincipalRightsAppOnlyAsync(systemUserId, entitySet, recordId, ct);
            }

            var token = Sprk.Bff.Api.Infrastructure.Auth.TokenHelper.ExtractBearerTokenOrNull(httpContext);
            return (entitySet, recordId, ct) => probe.GetCallerRightsAsync(token, entitySet, recordId, ct);
            """;

        Assert.Contains(ViolationsOf(preOptionC), v => v.Contains("GetCallerRightsAsync", StringComparison.Ordinal));
    }
}
