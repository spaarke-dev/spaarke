using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Sprk.Bff.Api.Api;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Finding C1 (unified-access-control-r2 task 022) — <c>POST /api/documents/bulk-download</c> was
/// gated in FORM ONLY.
///
/// <para><c>BulkDownloadAuthorizationFilter</c> read the tenant claim, logged
/// <c>"Bulk download authorization granted"</c>, and called <c>next()</c>. There was no per-document
/// decision at any point. One request naming up to 500 arbitrary GUIDs streamed every one of them
/// app-only — so a single call both exfiltrated documents the caller could not otherwise reach AND,
/// via the <c>_FAILED.txt</c> manifest's distinct per-document reasons, told the caller which GUIDs
/// were real.</para>
///
/// <para><b>Two halves, and closing one without the other makes things worse.</b> Adding per-document
/// authorization closes the exfiltration. But it also introduces a NEW distinguishable outcome
/// ("denied") which, next to the existing "not found in Dataverse", turns the manifest into an
/// enumeration oracle amplified 500× per request — and there was no such oracle before the fix,
/// because previously every document was simply returned. So the denial reason and the
/// not-found reason must be the same string. Both halves are asserted here.</para>
/// </summary>
public class BulkDownloadAuthorizationTests
    : IClassFixture<DocumentDestroyAuthorizationTestFixture>
{
    private readonly DocumentDestroyAuthorizationTestFixture _fixture;

    // The fixture's IDocumentDataverseService answers GetDocumentAsync for ANY id, so every id the
    // filter authorizes resolves. That is what makes "was it in the zip?" a clean signal for the
    // authorization decision rather than for data setup.
    private static readonly string DocA = "11111111-1111-1111-1111-111111111111";
    private static readonly string DocB = "22222222-2222-2222-2222-222222222222";

    public BulkDownloadAuthorizationTests(DocumentDestroyAuthorizationTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private static Task<HttpResponseMessage> PostIds(HttpClient client, params string[] ids) =>
        client.PostAsJsonAsync("/api/documents/bulk-download", new { documentIds = ids });

    // ─────────────────────────────────────────────────────────────────────────────
    // Half one — no unauthorized bytes
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The core of C1. A caller with no rights on anything asks for two documents and must receive
    /// no zip at all — not a zip containing them, and not a zip containing one of them.
    /// </summary>
    [Fact]
    public async Task BulkDownload_ForCallerWithNoRights_ReturnsNoZipAndNoDocuments()
    {
        using var client = _fixture.CreateClientWithRights("");

        var response = await PostIds(client, DocA, DocB);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "with nothing authorized the handler's total-failure branch is reached BEFORE the zip " +
            "headers are committed");

        response.Content.Headers.ContentType?.MediaType.Should().NotBe("application/zip",
            "no archive may be produced at all — C1's exfiltration was that bytes flowed regardless " +
            "of the caller's rights");
    }

    /// <summary>
    /// The mixed case is the one that would survive a sloppy fix: authorize the request as a whole,
    /// and one accessible document licenses the rest. The zip must contain exactly the authorized
    /// document and the other must appear only in the manifest.
    /// </summary>
    [Fact]
    public async Task BulkDownload_ForCallerAuthorizedOnSomeDocuments_ZipsOnlyThose()
    {
        // The fixture's access data source answers from the token, so rights are uniform across
        // documents. To get a MIXED verdict, ask for one real id and one that cannot be authorized
        // because it is not a GUID at all — the filter never authorizes an unparseable id.
        using var client = _fixture.CreateClientWithRights("ReadAccess");

        var response = await PostIds(client, DocA, "not-a-guid");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/zip");

        var entries = await ReadZipEntryNamesAsync(response);

        entries.Should().Contain("_FAILED.txt",
            "the unauthorized id must be reported, not silently dropped");
        entries.Should().Contain($"{DocA}.pdf",
            "the authorized document must still be delivered — one unauthorized id does not void the " +
            "whole archive");

        // The load-bearing assertion: whose BYTES were fetched. Entry names alone would not
        // distinguish "excluded by authorization" from "included, but the fetch failed".
        _fixture.DownloadedItemIds.Should().ContainSingle()
            .Which.Should().Be($"item-{DocA}",
                "exactly the authorized document's content may be read from SPE");
    }

    /// <summary>
    /// The inverse, and the sharpest single assertion in this class: when the caller is authorized
    /// for nothing, SPE must not be touched at all. Before task 022 every requested document was
    /// fetched app-only regardless of rights, so this count was the request size.
    /// </summary>
    [Fact]
    public async Task BulkDownload_ForCallerWithNoRights_NeverReadsAnyBytesFromSpe()
    {
        using var client = _fixture.CreateClientWithRights("");

        await PostIds(client, DocA, DocB);

        _fixture.DownloadedItemIds.Should().BeEmpty(
            "C1's exfiltration was that bytes flowed before any per-document decision existed");
    }

    [Fact]
    public async Task BulkDownload_ForUnauthenticatedCaller_IsRejected()
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await PostIds(client, DocA);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// ADR-003 fail-closed on the authorization path itself. When the access check THROWS — RPA
    /// unavailable, Dataverse unreachable, a transient fault — the document must be excluded, not
    /// waved through. The filter catches broadly and simply does not add the id, which is easy to
    /// write and easy to silently invert later into <c>authorized.Add(rawId)</c> in the catch.
    ///
    /// <para>This test exists because that inversion initially broke NOTHING: no case made the check
    /// throw, so the catch was unreachable and the perturbation failed zero tests. The fixture's
    /// <see cref="DocumentDestroyAuthorizationTestFixture.ThrowingDocumentId"/> sentinel is what makes
    /// it bite. A guard with no test that can reach it is not a guard.</para>
    /// </summary>
    [Fact]
    public async Task BulkDownload_WhenTheAccessCheckThrows_ExcludesTheDocumentAndReadsNoBytes()
    {
        // The caller holds Read on everything the check can answer for — so if this document is
        // excluded, it is because the ERROR denied it, not because the caller lacks rights.
        using var client = _fixture.CreateClientWithRights("ReadAccess");

        var response = await PostIds(
            client, DocumentDestroyAuthorizationTestFixture.ThrowingDocumentId);

        _fixture.DownloadedItemIds.Should().BeEmpty(
            "an errored access check must deny (ADR-003); failing open here would mean a transient " +
            "Dataverse fault silently grants bulk access to every requested document");

        response.IsSuccessStatusCode.Should().BeFalse();
        response.Content.Headers.ContentType?.MediaType.Should().NotBe("application/zip");
    }

    /// <summary>
    /// The mixed version of the above, which is the shape that matters in production: one document
    /// resolves normally and one errors. The good one must still be delivered — an errored check is a
    /// per-document denial, not a reason to fail the whole request — and the errored one must not be.
    /// </summary>
    [Fact]
    public async Task BulkDownload_WhenOneAccessCheckThrows_DeliversTheOthersAndExcludesThatOne()
    {
        using var client = _fixture.CreateClientWithRights("ReadAccess");

        var response = await PostIds(
            client, DocA, DocumentDestroyAuthorizationTestFixture.ThrowingDocumentId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        _fixture.DownloadedItemIds.Should().ContainSingle()
            .Which.Should().Be($"item-{DocA}",
                "the errored document is excluded; the healthy one is unaffected");

        var entries = await ReadZipEntryNamesAsync(response);
        entries.Should().Contain("_FAILED.txt");
        entries.Should().NotContain(
            $"{DocumentDestroyAuthorizationTestFixture.ThrowingDocumentId}.pdf");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Half two — the manifest must not be an enumeration oracle
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The reason string for a document the caller may not read must not distinguish "denied" from
    /// "does not exist". Asserted on the constant itself as well as on a live response, because the
    /// constant is what a future edit would change: renaming it to "Access denied" would be a
    /// one-word change that silently re-opens the oracle with every test still passing except this one.
    /// </summary>
    [Fact]
    public void NotAccessibleReason_DoesNotDistinguishDeniedFromNonexistent()
    {
        var reason = DocumentsBulkEndpoints.NotAccessibleReason;

        reason.ToLowerInvariant().Should().NotContain("denied",
            "a caller who may not read a document must not learn whether it exists");
        reason.ToLowerInvariant().Should().NotContain("forbidden");
        reason.ToLowerInvariant().Should().NotContain("permission");
        reason.ToLowerInvariant().Should().NotContain("access denied");

        reason.Should().Contain("not found",
            "the reason must be the same one a nonexistent document gets, so the two are " +
            "indistinguishable in the manifest");
    }

    /// <summary>
    /// The live counterpart: a total denial must not describe itself as a rights failure. If this
    /// response said "forbidden" or "denied", a caller could partition arbitrary GUIDs into real and
    /// unreal by sending them one at a time.
    /// </summary>
    [Fact]
    public async Task BulkDownload_WhenEverythingIsDenied_DoesNotRevealThatItWasARightsDecision()
    {
        using var client = _fixture.CreateClientWithRights("");

        var response = await PostIds(client, DocA, DocB);
        var body = (await response.Content.ReadAsStringAsync()).ToLowerInvariant();

        body.Should().NotContain("insufficient_rights",
            "the aggregate response must not disclose that these ids exist and were refused");
        body.Should().NotContain("access denied");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Consistency with the single-document route — the invariant whose absence WAS C1
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bulk download and single-document download must reach the same decision for the same caller
    /// and document. Their disagreement was the whole finding: <c>/download</c> was gated by task 002
    /// while the bulk route accepted the same document from the same caller and streamed it.
    /// </summary>
    [Fact]
    public async Task BulkAndSingleDownload_AgreeThatACallerWithNoRightsGetsNothing()
    {
        using var client = _fixture.CreateClientWithRights("");

        var single = await client.GetAsync($"/api/documents/{DocA}/download");
        var bulk = await PostIds(client, DocA);

        single.IsSuccessStatusCode.Should().BeFalse();
        bulk.IsSuccessStatusCode.Should().BeFalse(
            "the bulk route must not be a way around the gate the single route applies. The status " +
            "codes differ deliberately — single says 403, bulk says 404 so that a one-id bulk request " +
            "is not a cheaper oracle than the single route — but neither yields bytes");

        bulk.Content.Headers.ContentType?.MediaType.Should().NotBe("application/zip");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private static async Task<List<string>> ReadZipEntryNamesAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        return zip.Entries.Select(e => e.FullName).ToList();
    }
}
