using System.Net;
using System.Text;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Files;
using Xunit;
using Host = Sprk.Bff.Api.Tests.Api.Finance.StoredFileEndpointsTests.Host;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// An AP export file (purpose Export file, parent the <c>sprk_billingexport</c>) through 090's internal download route (ebilling-r1 task 150;
/// EB-D-148, FR-41): internal-only. A flagged-external caller is refused before any read, a caller with Read on the BATCH downloads it, and Read on
/// anything else does not. The rows the export writes are never Firm-visible (asserted in <c>ApExportBatchTests</c>), so the firm route cannot list them.
/// </summary>
public sealed class ApExportFileDownloadTests
{
    private static readonly Guid BatchId = Guid.Parse("15000000-0000-4000-8000-0000000000b1");
    private static readonly StoredFileParent Batch = new(StoredFileParentKind.BillingExport, BatchId);
    private static readonly byte[] Csv = Encoding.UTF8.GetBytes("InvoiceNumber\r\nINV-1\r\n");

    private static string Url(Guid id) => $"/api/finance/files/{id}/content";

    public static IEnumerable<object[]> RefusedKinds => StatedCaller.RefusedKinds.Select(k => new object[] { k });

    private static Guid SeedExportFile(Host host)
    {
        var file = Guid.NewGuid();
        host.Rig.SeedFile(file, Batch, Csv, purpose: StoredFilePurpose.ExportFile, contentType: "text/csv");
        return file;
    }

    [Theory(DisplayName = "150: a flagged-external, row-less or unreadable caller is 403 on an export file before any row read, even holding Read on the batch")]
    [MemberData(nameof(RefusedKinds))]
    public async Task FlaggedExternalCaller_IsRefused_BeforeAnyRead(StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        var file = SeedExportFile(host);
        host.Rig.Probe.Grant(ApExportAuthorization.BatchEntitySet, BatchId, AccessRights.Read);
        host.StepZero.Kind = kind;

        var response = await host.SendAsync(Url(file));

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: false);
        host.AssertNothingRead();
    }

    [Fact(DisplayName = "150: a caller with Read on the batch downloads the export file - its content type and bytes, with one audit event")]
    public async Task ReadOnTheBatch_Downloads()
    {
        await using var host = await Host.StartAsync();
        var file = SeedExportFile(host);
        host.Rig.Probe.Grant(ApExportAuthorization.BatchEntitySet, BatchId, AccessRights.Read);

        var response = await host.SendAsync(Url(file));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(Csv);
        host.AuditEvents.Should().ContainSingle().Which.Message.Should().Contain("sprk_billingexport").And.Contain(BatchId.ToString());
    }

    [Fact(DisplayName = "150: Read on a different batch, or Write without Read on this one, is the uniform 404 and no bytes")]
    public async Task NoReadOnTheBatch_Is404()
    {
        await using var host = await Host.StartAsync();
        var file = SeedExportFile(host);
        host.Rig.Probe.Grant(ApExportAuthorization.BatchEntitySet, Guid.NewGuid(), AccessRights.Read);
        host.Rig.Probe.Grant(ApExportAuthorization.BatchEntitySet, BatchId, AccessRights.Write | AccessRights.Append);

        var response = await host.SendAsync(Url(file));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        host.Rig.Gateway.StorageCalls.Should().Be(0);
        host.AuditEvents.Should().BeEmpty();
    }
}
