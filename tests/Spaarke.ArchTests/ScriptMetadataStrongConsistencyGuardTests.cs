using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// #2833: a script that reads Dataverse attribute metadata must ask for a strongly consistent read.
///
/// <para>Dataverse caches attribute metadata per server, so a plain Web API read of <c>EntityDefinitions(...)/Attributes(...)</c>
/// soon after a change can return a stale <c>IsSecured</c> (measured on dev 2026-11-14: 5 of 36 plain reads stale, 0 of 36 with
/// <c>Consistency: Strong</c>). A stale read makes <c>-Verify</c> report a secured column as MISSING, or lets a re-run
/// re-apply a change that already landed.</para>
///
/// <para><b>The rule.</b> Every <c>*.ps1</c> / <c>*.psm1</c> under <c>scripts/</c> whose text contains <c>IsSecured</c> and
/// <c>EntityDefinitions(</c> must also contain <c>Consistency</c> (the request header). Textual and file-level on purpose: a
/// script that mentions all three has at least decided about the header; one that does not has not.</para>
///
/// <para><b>Ratchet baseline.</b> <see cref="KnownOffenders"/> lists the files that violate the rule today and are fixed on
/// another branch (the <c>scripts/ebilling/</c> slice of #2833). A new violation outside the baseline fails; a baseline entry
/// that no longer violates also fails, so the list only shrinks. <b>The ebilling team's PR for #2833 must empty its entries.</b></para>
///
/// <para><b>Controls.</b> <see cref="Violates_MustFireAndMustNotFire"/> runs the predicate on in-memory text: it fires on
/// <c>IsSecured</c> + <c>EntityDefinitions(</c> without the header, and does not fire with the header or without <c>IsSecured</c>.</para>
/// </summary>
public class ScriptMetadataStrongConsistencyGuardTests
{
    /// <summary>
    /// Files that violate the rule on master today. Remove an entry when its script is fixed (the test demands it). The
    /// ebilling team's PR for #2833 must empty every <c>scripts/ebilling/</c> entry.
    /// </summary>
    private static readonly IReadOnlySet<string> KnownOffenders = new HashSet<string>(StringComparer.Ordinal)
    {
        "scripts/ebilling/Deploy-EbillingInvoiceReviewDetailColumn.ps1",
        "scripts/ebilling/Deploy-EbillingProcedureSchema.ps1",
        "scripts/ebilling/Deploy-EbillingProcedureVersionPublishedOn.ps1",
        "scripts/ebilling/Deploy-EbillingStoredFileSchema.ps1",
        "scripts/ebilling/Deploy-EbillingSubmissionPreviousSequenceColumn.ps1",
        "scripts/ebilling/Deploy-EbillingSubmissionSourceEmailSchema.ps1",
        "scripts/ebilling/Set-EbillingFindingsJsonFieldSecurity.ps1",
        "scripts/ebilling/Set-EbillingFirmReasonFieldSecurity.ps1",
        "scripts/ebilling/Set-EbillingInvoiceFirmHoldColumn.ps1",
        "scripts/ebilling/Set-EbillingInvoiceMoneyFieldSecurity.ps1",
        "scripts/ebilling/Set-EbillingResubmissionOfFieldSecurity.ps1",
        "scripts/ebilling/Set-EbillingSourceEmailFieldSecurity.ps1",
    };

    /// <summary>True when the script reads attribute security metadata without ever naming the Consistency header. Pure.</summary>
    internal static bool ViolatesStrongConsistencyRule(string scriptText)
        => scriptText.Contains("IsSecured", StringComparison.Ordinal)
           && scriptText.Contains("EntityDefinitions(", StringComparison.Ordinal)
           && !scriptText.Contains("Consistency", StringComparison.Ordinal);

    private static List<string> Violators()
    {
        var root = SourceScan.RepoRoot;
        var scriptsRoot = Path.Combine(root, "scripts");
        return Directory.EnumerateFiles(scriptsRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".psm1", StringComparison.OrdinalIgnoreCase))
            .Where(f => ViolatesStrongConsistencyRule(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    [Fact(DisplayName = "#2833: scripts that read attribute IsSecured via EntityDefinitions send Consistency: Strong (known offenders ratcheted)")]
    public void ScriptsReadingAttributeMetadataSendStrongConsistency()
    {
        var violators = Violators();

        // Non-vacuity: the shared engine is a script that does read IsSecured through EntityDefinitions and passes the rule.
        var engine = File.ReadAllText(Path.Combine(SourceScan.RepoRoot, "scripts", "Set-DocumentPointerFieldSecurity.ps1"));
        Assert.Contains("EntityDefinitions(", engine);
        Assert.Contains("IsSecured", engine);
        Assert.False(ViolatesStrongConsistencyRule(engine));

        var newViolations = violators.Where(v => !KnownOffenders.Contains(v)).ToList();
        Assert.True(newViolations.Count == 0,
            "These scripts read attribute metadata (IsSecured via EntityDefinitions(...)) without a Consistency header. " +
            "Dataverse caches attribute metadata per server, so a plain read can return a stale IsSecured (#2833). Add " +
            "Consistency = 'Strong' to the Dataverse request headers (see scripts/Set-DocumentPointerFieldSecurity.ps1):\n  " +
            string.Join("\n  ", newViolations));

        var fixedEntries = KnownOffenders.Where(k => !violators.Contains(k, StringComparer.Ordinal)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(fixedEntries.Count == 0,
            "These baseline entries no longer violate the rule (or no longer exist): remove them from KnownOffenders so the " +
            "ratchet only shrinks:\n  " + string.Join("\n  ", fixedEntries));
    }

    [Fact(DisplayName = "#2833 guard control: the predicate fires without the header and does not fire with it or without IsSecured")]
    public void Violates_MustFireAndMustNotFire()
    {
        const string Read = "Invoke-RestMethod -Uri \"$Api/EntityDefinitions(LogicalName='sprk_x')/Attributes(LogicalName='sprk_y')?$select=IsSecured\" -Headers $headers";

        // Must fire: reads IsSecured through EntityDefinitions( with no Consistency header anywhere.
        Assert.True(ViolatesStrongConsistencyRule(Read));

        // Must not fire: the same script with the header.
        Assert.False(ViolatesStrongConsistencyRule("$headers = @{ Consistency = 'Strong' }\n" + Read));

        // Must not fire: EntityDefinitions( without IsSecured (the rule is about the secured flag only).
        Assert.False(ViolatesStrongConsistencyRule("Invoke-RestMethod -Uri \"$Api/EntityDefinitions(LogicalName='sprk_x')?$select=LogicalName\""));

        // Must not fire: IsSecured without EntityDefinitions( (not a metadata read).
        Assert.False(ViolatesStrongConsistencyRule("$col.IsSecured = $true"));
    }
}
