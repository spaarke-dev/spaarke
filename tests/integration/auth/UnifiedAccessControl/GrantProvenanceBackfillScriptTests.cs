using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 140, session 27 round 50 item 2 — the BACKFILL of <c>sprk_grantedbycontactid</c> that
/// <c>scripts/Deploy-ExternalRecordAccessContactGrantor.ps1 -Apply</c> runs (step f), exercised by running its functions
/// (<c>scripts/common/GrantProvenanceBackfill.ps1</c>) in <c>pwsh</c> over rows shaped exactly as the Web API serves them,
/// with a recording PATCH in place of Dataverse.
/// </summary>
/// <remarks>
/// <para><b>Why behaviour, not text.</b> The backfill decides which rows a live <c>-Apply</c> writes and how; a source scan
/// could not tell a wrong text form, a missing <c>If-Match</c> or a swallowed fault from a correct one. What is NOT covered
/// here is the HTTP itself (<c>Invoke-DvWrite</c> over a real token) — that runs at the operator gate G-140-1, whose
/// <c>-Verify</c> FAILS while any row lacks its provenance.</para>
/// <para>Requires PowerShell 7 (<c>pwsh</c>) on the PATH — present on the CI runners (windows-latest) and every dev machine
/// this repo's scripts run on. Its absence fails the test with that message; it is never skipped.</para>
/// </remarks>
public class GrantProvenanceBackfillScriptTests
{
    private const string Lookup = "_sprk_grantedbycontact_value";
    private const string Provenance = ExternalGrantLifecycle.GrantedByContactIdAttribute;

    private static readonly Guid Row1 = Guid.Parse("e0500000-0000-0000-0000-000000000001");
    private static readonly Guid Row2 = Guid.Parse("e0500000-0000-0000-0000-000000000002");
    private static readonly Guid Row3 = Guid.Parse("e0500000-0000-0000-0000-000000000003");
    private static readonly Guid Row4 = Guid.Parse("e0500000-0000-0000-0000-000000000004");
    private static readonly Guid Row5 = Guid.Parse("e0500000-0000-0000-0000-000000000005");
    private static readonly Guid ContactA = Guid.Parse("c0500000-0000-0000-0000-0000000000aa");
    private static readonly Guid ContactB = Guid.Parse("c0500000-0000-0000-0000-0000000000bb");
    private static readonly Guid ContactC = Guid.Parse("c0500000-0000-0000-0000-0000000000cc");

    /// <summary>
    /// Which rows: every row whose LOOKUP names a contact and whose provenance is not that contact's id in the BFF's ONE text
    /// form (<see cref="ExternalGrantLifecycle.ContactIssuerProvenance"/>) — missing, different, or another casing. One input
    /// apart, a row already recording it is left alone, and a row whose lookup is EMPTY (a deleted issuer's — its record is the
    /// point) is never touched.
    /// </summary>
    [Fact]
    public void TheBackfill_SelectsEveryRowWhoseLookupNamesAContactItDoesNotRecord_InTheBffsTextForm()
    {
        var rows = new object[]
        {
            Row(Row1, "W/\"11\"", lookup: ContactA.ToString("D").ToUpperInvariant(), provenance: null), // missing (lookup read upper-case)
            Row(Row2, "W/\"12\"", lookup: ContactB.ToString("D"), provenance: ContactB.ToString("D")),  // already recorded
            Row(Row3, "W/\"13\"", lookup: ContactB.ToString("D"), provenance: ContactC.ToString("D")),  // records another contact
            Row(Row4, "W/\"14\"", lookup: null, provenance: ContactC.ToString("D")),                    // issuer deleted
            Row(Row5, "W/\"15\"", lookup: ContactC.ToString("D"), provenance: ContactC.ToString("D").ToUpperInvariant()), // casing
        };

        var output = RunPwsh($"""
            $rows = '{Json(rows)}' | ConvertFrom-Json
            $items = @(Get-GrantProvenanceBackfill -Rows $rows -LookupProperty '{Lookup}' -ProvenanceProperty '{Provenance}')
            ConvertTo-Json -InputObject $items -Depth 5 -Compress
            """);

        var items = JsonDocument.Parse(output).RootElement.EnumerateArray()
            .Select(i => (Id: i.GetProperty("Id").GetString(), ETag: i.GetProperty("ETag").GetString(), Value: i.GetProperty("Value").GetString()))
            .ToList();

        items.Should().Equal(
            (Row1.ToString("D"), "W/\"11\"", ExternalGrantLifecycle.ContactIssuerProvenance(ContactA)),
            (Row3.ToString("D"), "W/\"13\"", ExternalGrantLifecycle.ContactIssuerProvenance(ContactB)),
            (Row5.ToString("D"), "W/\"15\"", ExternalGrantLifecycle.ContactIssuerProvenance(ContactC)));
    }

    /// <summary>
    /// The write: each row PATCHed with its provenance and <c>If-Match</c> = the version it was read at. A 412 (an internal
    /// take-over cleared the issuer in between) is counted and the row left as it is — never written over, never retried
    /// unconditionally; the other rows are still written.
    /// </summary>
    [Fact]
    public void TheBackfill_WritesEachRowConditionallyOnItsVersion_AndLeavesARowThatChangedInBetween()
    {
        var output = RunPwsh($$"""
            $calls = [System.Collections.Generic.List[object]]::new()
            $patch = {
                param($Path, $Body, $Extra)
                $calls.Add([pscustomobject]@{ Path = $Path; Body = $Body; IfMatch = $Extra['If-Match'] })
                if ($Path -like '*{{Row2:D}}*') {
                    throw [Microsoft.PowerShell.Commands.HttpResponseException]::new('Precondition Failed',
                        [System.Net.Http.HttpResponseMessage]::new([System.Net.HttpStatusCode]::PreconditionFailed))
                }
            }
            $items = @(
                [pscustomobject]@{ Id = '{{Row1:D}}'; ETag = 'W/"21"'; Value = '{{ContactA:D}}' },
                [pscustomobject]@{ Id = '{{Row2:D}}'; ETag = 'W/"22"'; Value = '{{ContactB:D}}' },
                [pscustomobject]@{ Id = '{{Row3:D}}'; ETag = 'W/"23"'; Value = '{{ContactC:D}}' })
            $result = Invoke-GrantProvenanceBackfill -Items $items -ProvenanceProperty '{{Provenance}}' -Patch $patch
            ConvertTo-Json -InputObject ([pscustomobject]@{ Result = $result; Calls = @($calls) }) -Depth 6 -Compress
            """);

        var root = JsonDocument.Parse(output).RootElement;
        root.GetProperty("Result").GetProperty("Written").GetInt32().Should().Be(2);
        root.GetProperty("Result").GetProperty("Changed").GetInt32().Should().Be(1, "the row that changed since it was read is counted, not written");

        var calls = root.GetProperty("Calls").EnumerateArray().ToList();
        calls.Should().HaveCount(3, "one conditional PATCH per row — no retry of the refused one");
        calls.Select(c => c.GetProperty("Path").GetString()).Should().Equal(
            $"sprk_externalrecordaccesses({Row1:D})", $"sprk_externalrecordaccesses({Row2:D})", $"sprk_externalrecordaccesses({Row3:D})");
        calls.Select(c => c.GetProperty("IfMatch").GetString()).Should().Equal("W/\"21\"", "W/\"22\"", "W/\"23\"");
        calls.Select(c => c.GetProperty("Body").GetProperty(Provenance).GetString()).Should().Equal(
            ContactA.ToString("D"), ContactB.ToString("D"), ContactC.ToString("D"));
        calls.Should().OnlyContain(c => c.GetProperty("Body").EnumerateObject().Count() == 1, "the backfill writes the provenance and nothing else");
    }

    /// <summary>
    /// Fail closed, two ways. Any failure other than 412 STOPS the backfill (it is not a "changed row"); and a row read without
    /// a version is refused before anything is sent — the backfill never writes unconditionally.
    /// </summary>
    [Theory]
    [InlineData("server-error")]
    [InlineData("no-version")]
    public void TheBackfill_StopsOnAnyOtherFailure_AndNeverWritesWithoutAVersion(string shape)
    {
        var etag = shape == "no-version" ? "$null" : "'W/\"31\"'";
        var output = RunPwsh($$"""
            $calls = [System.Collections.Generic.List[object]]::new()
            $patch = {
                param($Path, $Body, $Extra)
                $calls.Add($Path)
                throw [Microsoft.PowerShell.Commands.HttpResponseException]::new('Service Unavailable',
                    [System.Net.Http.HttpResponseMessage]::new([System.Net.HttpStatusCode]::ServiceUnavailable))
            }
            $items = @([pscustomobject]@{ Id = '{{Row1:D}}'; ETag = {{etag}}; Value = '{{ContactA:D}}' })
            $outcome = try { Invoke-GrantProvenanceBackfill -Items $items -ProvenanceProperty '{{Provenance}}' -Patch $patch; 'RETURNED' }
                       catch { "THREW: $($_.Exception.Message)" }
            ConvertTo-Json -InputObject ([pscustomobject]@{ Outcome = "$outcome"; Calls = $calls.Count }) -Compress
            """);

        var root = JsonDocument.Parse(output).RootElement;
        root.GetProperty("Outcome").GetString().Should().StartWith("THREW:");
        if (shape == "no-version")
        {
            root.GetProperty("Outcome").GetString().Should().Contain("without a version");
            root.GetProperty("Calls").GetInt32().Should().Be(0, "nothing is sent for a row read without a version");
        }
        else
        {
            root.GetProperty("Outcome").GetString().Should().Contain("Service Unavailable");
            root.GetProperty("Calls").GetInt32().Should().Be(1);
        }
    }

    private static Dictionary<string, object?> Row(Guid id, string etag, string? lookup, string? provenance) => new()
    {
        ["@odata.etag"] = etag,
        ["sprk_externalrecordaccessid"] = id.ToString("D"),
        [Lookup] = lookup,
        [Provenance] = provenance,
    };

    // Single quotes are the only character a PowerShell single-quoted string needs escaped.
    private static string Json(object value) => JsonSerializer.Serialize(value).Replace("'", "''", StringComparison.Ordinal);

    /// <summary>Runs <paramref name="body"/> in pwsh after dot-sourcing the helper; returns stdout (the script's JSON).</summary>
    private static string RunPwsh(string body)
    {
        var helper = Path.Combine(RepoRoot(), "scripts", "common", "GrantProvenanceBackfill.ps1");
        File.Exists(helper).Should().BeTrue($"{helper} is the backfill the schema script dot-sources");

        // The stubs throw [Microsoft.PowerShell.Commands.HttpResponseException], whose assembly loads only with the Utility
        // module; module auto-loading made that intermittent on CI ("Unable to find type"), so load it before the stubs run.
        var script = "$ErrorActionPreference = 'Stop'\nImport-Module Microsoft.PowerShell.Utility\n" + $". '{helper.Replace("'", "''", StringComparison.Ordinal)}'\n" + body;
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(script);

        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException("PowerShell 7 (pwsh) must be on the PATH to exercise the schema script's backfill.", ex);
        }

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit(120_000).Should().BeTrue("pwsh must finish");
            process.ExitCode.Should().Be(0, $"the backfill script failed: {stderr.Result}");
            return stdout.Result.Trim();
        }
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            // `.git` is a FILE in a git worktree, which is how this repo is normally developed.
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
