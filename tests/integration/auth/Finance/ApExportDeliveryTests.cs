using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Tests.Api.Finance;
using Sprk.Bff.Api.Tests.DataMutation.Ebilling;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.Finance;

/// <summary>
/// <c>POST /api/finance/export-batches/{batchId}/email</c> and <c>.../mark-sent-to-ap</c> (ebilling-r1 task 151c-1a; EB-D-181, EB-D-182, FR-41, ADR-008,
/// ADR-019). The REAL routes, filters, authorization stack, export service, Stored File service (scan verdict, own path), delivery service and kill switch
/// run over the in-memory world; the batch is created through the real POST route. The only stand-in is the mail seam (<see cref="RecordingApMailer"/>):
/// the real send path, owner and persistence are covered by <c>CommunicationServiceServerSendTests</c> and <see cref="ApExportEmailAccessTests"/>.
/// </summary>
public sealed class ApExportDeliveryTests
{
    private const string Csv = "text/csv";
    private const string Json = "application/json";
    private const string ApAddress = "ap@example.com";

    private static string EmailUrl(Guid batchId) => $"/api/finance/export-batches/{batchId}/email";

    private static string MarkUrl(Guid batchId) => $"/api/finance/export-batches/{batchId}/mark-sent-to-ap";

    /// <summary>Exports two Approved invoices, scans the files clean, sets the AP address and (unless told not to) grants the caller Read and Write on the batch.</summary>
    private static async Task<(Guid BatchId, ApExportRoutesHost Host)> ExportedAsync(
        string[]? formats = null, bool grantAccess = true, bool ebillingEnabled = true, string? apEmail = ApAddress, bool scanned = true)
    {
        var host = await ApExportRoutesHost.StartAsync();
        var a = host.World.AddInvoice("INV-A", approved: 900m);
        var b = host.World.AddInvoice("INV-B", approved: 600.5m);
        var batchId = await host.ExportAsync([a, b], formats);
        if (scanned)
        {
            host.MarkExportedFilesScannedClean();
        }

        if (apEmail is not null)
        {
            host.SetApEmail(apEmail);
        }

        host.EbillingEnabled = ebillingEnabled;
        if (grantAccess)
        {
            host.World.Access.Grant(ApExportAuthorization.BatchEntitySet, batchId, AccessRights.Read | AccessRights.Write);
        }

        return (batchId, host);
    }

    private static async Task<JsonNode> BodyOf(HttpResponseMessage response) => JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    private static async Task<string> CodeOf(HttpResponseMessage response) => (await BodyOf(response))["errorCode"]!.GetValue<string>();

    private static DateTime? SentToApOf(ApExportRoutesHost host, Guid batchId) =>
        host.World.Dataverse.Get("sprk_billingexport", batchId).GetAttributeValue<DateTime?>("sprk_senttoapdate");

    // =========================================================================================
    // Email to AP: success
    // =========================================================================================

    [Fact(DisplayName = "151c-1a: Email to AP sends both stored files as the user, summarises the batch, sets the date once and answers the shape 151c-1b calls")]
    public async Task Email_Succeeds_AndSetsTheDate()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        var csvText = host.World.TextOf(host.World.FileOf(batchId, Csv));
        var jsonText = host.World.TextOf(host.World.FileOf(batchId, Json));

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId), body: new { cc = new[] { "boss@example.com", "BOSS@example.com", "ops@example.com" }, message = "Please pay by Friday." });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyOf(response);
        body["sentTo"]!.GetValue<string>().Should().Be(ApAddress);
        body["cc"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal("boss@example.com", "ops@example.com");
        body["communicationId"]!.GetValue<Guid>().Should().NotBeEmpty();
        body["sentToApDateSet"]!.GetValue<bool>().Should().BeTrue();
        body["sentToApDate"]!.GetValue<DateTimeOffset>().Should().Be(host.Clock.Now);
        SentToApOf(host, batchId).Should().Be(host.Clock.Now.UtcDateTime);

        var sent = host.FakeMailer!.Sent.Should().ContainSingle().Subject;
        sent.To.Should().Equal(ApAddress);
        sent.Cc.Should().Equal("boss@example.com", "ops@example.com");
        sent.SendMode.Should().Be(SendMode.User);
        sent.ArchiveToSpe.Should().BeFalse();
        sent.IsInternalOnly.Should().BeTrue();
        sent.Associations.Should().BeNull("no regarding link to the batch yet: that is task 151c-2");
        sent.AttachmentDocumentIds.Should().BeNull("the files are not documents");
        sent.Subject.Should().Contain(batchId.ToString("D")).And.Contain("2 invoices").And.Contain("1,500.50").And.Contain("USD");
        sent.Body.Should().Contain(batchId.ToString("D")).And.Contain("Invoices: 2").And.Contain("Total: 1,500.50 USD").And.Contain("Please pay by Friday.");
        var server = sent.ServerSend!;
        server.SkipEnrichment.Should().BeTrue();
        host.World.Dataverse.Get("sprk_billingexport", batchId).GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", ApExportWorld.TeamId));
        server.OwnerFollows.Should().BeEquivalentTo(new ServerOwnerRecord("sprk_billingexport", batchId, ApExportWorld.TeamId), "the email's owner must equal the batch's owning team");
        server.Attachments.Select(a => (a.Name, a.ContentType)).Should().Equal(
            ($"ap-export-{batchId:D}.csv", "text/csv"), ($"ap-export-{batchId:D}.json", "application/json"));
        Encoding.UTF8.GetString(server.Attachments[0].Content).Should().Be(csvText).And.NotBeEmpty();
        Encoding.UTF8.GetString(server.Attachments[1].Content).Should().Be(jsonText).And.NotBeEmpty();
    }

    [Fact(DisplayName = "151c-1a: a batch exported as CSV only sends the one file")]
    public async Task Email_CsvOnlyBatch_SendsOneFile()
    {
        var (batchId, host) = await ExportedAsync(formats: ["csv"]);
        await using var _ = host;

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.FakeMailer!.Sent.Single().ServerSend!.Attachments.Select(a => a.Name).Should().Equal($"ap-export-{batchId:D}.csv");
    }

    [Fact(DisplayName = "151c-1a: mixed invoice currencies are said so in the subject and body")]
    public async Task Email_MixedCurrencies_AreStated()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        var one = host.World.Dataverse.RowsOf("sprk_invoice").First();
        host.World.Dataverse.Touch("sprk_invoice", one.Id, new Dictionary<string, object> { ["sprk_currency"] = "CAD" });

        await host.SendAsync(HttpMethod.Post, EmailUrl(batchId));

        host.FakeMailer!.Sent.Single().Subject.Should().Contain("mixed currencies: CAD, USD");
    }

    [Fact(DisplayName = "151c-1a: a re-send sends again but leaves the Sent to AP Date unchanged (set once, never overwritten)")]
    public async Task Email_ReSend_LeavesTheDateUnchanged()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        var first = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId));
        var firstDate = SentToApOf(host, batchId);
        host.Clock.Now = host.Clock.Now.AddDays(3);
        var writes = host.World.Dataverse.WritesTo("sprk_billingexport");

        var second = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId));

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyOf(second);
        body["sentToApDateSet"]!.GetValue<bool>().Should().BeFalse();
        body["sentToApDate"]!.GetValue<DateTimeOffset>().UtcDateTime.Should().Be(firstDate!.Value);
        SentToApOf(host, batchId).Should().Be(firstDate, "never overwritten");
        host.World.Dataverse.WritesTo("sprk_billingexport").Should().Be(writes, "the second send wrote nothing to the batch");
        host.FakeMailer!.Sent.Should().HaveCount(2, "the user asked for it again");
    }

    [Fact(DisplayName = "151c-1a: a batch Mark Sent to AP already dated is emailed without changing the date")]
    public async Task Email_AfterMarkSent_KeepsTheEarlierDate()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        await host.SendAsync(HttpMethod.Post, MarkUrl(batchId));
        var marked = SentToApOf(host, batchId);
        host.Clock.Now = host.Clock.Now.AddDays(1);

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId));

        (await BodyOf(response))["sentToApDateSet"]!.GetValue<bool>().Should().BeFalse();
        SentToApOf(host, batchId).Should().Be(marked);
    }

    // =========================================================================================
    // Email to AP: faults and refusals
    // =========================================================================================

    [Fact(DisplayName = "151c-1a: a send fault is 503 ebilling.export.email_send_failed and the date is NOT set")]
    public async Task Email_SendFault_Is503_AndNoDate()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        host.FakeMailer!.Fault = new SdapProblemException("GRAPH_SEND_FAILED", "Email Send Failed", "boom", 502);
        var writes = host.World.Dataverse.WritesTo("sprk_billingexport");

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await CodeOf(response)).Should().Be(ApExportCodes.EmailSendFailed);
        SentToApOf(host, batchId).Should().BeNull();
        host.World.Dataverse.WritesTo("sprk_billingexport").Should().Be(writes);
        host.DeliveryLog.Entries.Should().Contain(e => e.Level == LogLevel.Error);
    }

    [Fact(DisplayName = "151c-1a: the date write failing AFTER the send logs an Error and still answers 200 with sentToApDateSet false (the email went)")]
    public async Task Email_DateWriteFails_StillOk_AndLogsAnError()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        host.World.Dataverse.FaultOn = (op, table, _) => op == "update-if" && table == "sprk_billingexport" ? new InvalidOperationException("dataverse down") : null;

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyOf(response);
        body["sentToApDateSet"]!.GetValue<bool>().Should().BeFalse();
        body["sentToApDate"].Should().BeNull();
        body["communicationId"]!.GetValue<Guid>().Should().NotBeEmpty();
        host.FakeMailer!.Sent.Should().ContainSingle("the email went exactly once");
        host.DeliveryLog.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("do not send it again"));
        SentToApOf(host, batchId).Should().BeNull();
    }

    [Fact(DisplayName = "151c-1a: no AP address in the settings (no row, or an empty column) is 409 ebilling.export.ap_email_not_configured and nothing is read or sent")]
    public async Task Email_NoRecipient_Is409()
    {
        foreach (var noRow in new[] { true, false })
        {
            var (batchId, host) = await ExportedAsync(apEmail: noRow ? null : "ignored");
            await using var _ = host;
            if (!noRow)
            {
                host.SetApEmail(null);
            }

            var storageCalls = host.World.Gateway.StorageCalls;
            var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId));

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await CodeOf(response)).Should().Be(ApExportCodes.ApEmailNotConfigured);
            host.FakeMailer!.Sent.Should().BeEmpty();
            host.World.Gateway.StorageCalls.Should().Be(storageCalls, "no file is read for a batch with nowhere to go");
        }
    }

    [Theory(DisplayName = "151c-1a A5: a configured value that is not one valid address is 409 not_configured with its own detail and a Warning (not 'not set')")]
    [InlineData("not an address")]
    [InlineData("ap@example.com; ops@example.com")]
    [InlineData("AP <ap@example.com>")]
    public async Task Email_InvalidConfiguredAddress_Is409_WithItsOwnDetail(string configured)
    {
        var (batchId, host) = await ExportedAsync(apEmail: configured);
        await using var _ = host;

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await BodyOf(response);
        body["errorCode"]!.GetValue<string>().Should().Be(ApExportCodes.ApEmailNotConfigured);
        body["detail"]!.GetValue<string>().Should().Contain("not a single valid address").And.NotContain("No AP export email address is set");
        host.DeliveryLog.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("not a single valid address"));
        host.FakeMailer!.Sent.Should().BeEmpty();
    }

    [Fact(DisplayName = "151c-1a A5 control: an empty column still says the address is not set")]
    public async Task Email_EmptyConfiguredAddress_SaysNotSet()
    {
        var (batchId, host) = await ExportedAsync(apEmail: "   ");
        await using var _ = host;

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId));

        (await BodyOf(response))["detail"]!.GetValue<string>().Should().Contain("No AP export email address is set");
    }

    [Theory(DisplayName = "151c-1a: a batch that is not Completed is 409 ebilling.export.file_not_available and nothing is read or sent")]
    [InlineData(ApExportService.StatusFailed)]
    [InlineData(ApExportService.StatusGenerating)]
    public async Task Email_BatchNotCompleted_Is409(int status)
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        host.World.Dataverse.Touch("sprk_billingexport", batchId, new Dictionary<string, object> { ["sprk_exportstatus"] = new OptionSetValue(status) });
        var storageCalls = host.World.Gateway.StorageCalls;

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be(ApExportCodes.FileNotAvailable);
        host.FakeMailer!.Sent.Should().BeEmpty();
        host.World.Gateway.StorageCalls.Should().Be(storageCalls);
        SentToApOf(host, batchId).Should().BeNull();
    }

    [Fact(DisplayName = "151c-1a: a file with no scan verdict yet is 409 file_not_available and nothing is sent")]
    public async Task Email_ScanPending_Is409()
    {
        var (batchId, host) = await ExportedAsync(scanned: false);
        await using var _ = host;

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be(ApExportCodes.FileNotAvailable);
        host.FakeMailer!.Sent.Should().BeEmpty();
        SentToApOf(host, batchId).Should().BeNull();
    }

    [Fact(DisplayName = "151c-1a: a file that is no longer servable (the JSON was removed) is 409 file_not_available - no partial send of the CSV alone")]
    public async Task Email_FileMissing_Is409_NoPartialSend()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        var json = host.World.FileOf(batchId, Json);
        host.World.Dataverse.Touch(StoredFileSchema.Entity, json.Id, new Dictionary<string, object>
        {
            [StoredFileSchema.Status] = new OptionSetValue((int)StoredFileStatus.Removed),
        });

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be(ApExportCodes.FileNotAvailable);
        host.FakeMailer!.Sent.Should().BeEmpty();
    }

    [Fact(DisplayName = "151c-1a A3: files over the route's 3 MB budget are 409 ebilling.export.attachments_too_large up front and nothing is sent")]
    public async Task Email_FilesTooLarge_Is409()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        foreach (var (container, name) in host.World.Gateway.Names)
        {
            host.World.Gateway.Get(container, name)!.Content = new byte[(int)(ApExportDeliveryService.MaxEmailAttachmentBytes / 2) + 1];
        }

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be(ApExportCodes.AttachmentsTooLarge);
        host.FakeMailer!.Sent.Should().BeEmpty();
        SentToApOf(host, batchId).Should().BeNull();
    }

    [Fact(DisplayName = "151c-1a: the send path refusing the attachments (its own limit) is also 409 attachments_too_large")]
    public async Task Email_SendPathLimit_Is409()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        host.FakeMailer!.Fault = new SdapProblemException("ATTACHMENT_LIMIT_EXCEEDED", "Attachments Too Large", "too big", 400);

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be(ApExportCodes.AttachmentsTooLarge);
        SentToApOf(host, batchId).Should().BeNull();
    }

    [Fact(DisplayName = "151c-1a A3 must-not-fire: files exactly at the 3 MB budget are sent")]
    public async Task Email_FilesAtTheBudget_AreSent()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        foreach (var (container, name) in host.World.Gateway.Names)
        {
            host.World.Gateway.Get(container, name)!.Content = new byte[(int)(ApExportDeliveryService.MaxEmailAttachmentBytes / 2)];
        }

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.FakeMailer!.Sent.Should().ContainSingle().Which.ServerSend!.Attachments.Sum(a => (long)a.Content.Length)
            .Should().Be(ApExportDeliveryService.MaxEmailAttachmentBytes);
    }

    [Fact(DisplayName = "151c-1a A3: Graph refusing the request as too large (GRAPH_SEND_FAILED 413) is 409 attachments_too_large, not a retry")]
    public async Task Email_Graph413_Is409TooLarge()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        host.FakeMailer!.Fault = new SdapProblemException("GRAPH_SEND_FAILED", "Email Send Failed", "request entity too large", 413);

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be(ApExportCodes.AttachmentsTooLarge);
        SentToApOf(host, batchId).Should().BeNull();
    }

    [Theory(DisplayName = "151c-1a A4: a send-path refusal below 500 (no user email claim, Graph 403 for a user without a mailbox) is 409 email_send_refused telling the user to deliver another way")]
    [InlineData("USER_EMAIL_NOT_FOUND", 400)]
    [InlineData("GRAPH_SEND_FAILED", 403)]
    [InlineData("OBO_CONTEXT_REQUIRED", 400)]
    public async Task Email_RefusalBelow500_Is409_NoRetry(string code, int status)
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        host.FakeMailer!.Fault = new SdapProblemException(code, "Refused", "refused", status);

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await BodyOf(response);
        body["errorCode"]!.GetValue<string>().Should().Be(ApExportCodes.EmailSendRefused);
        body["detail"]!.GetValue<string>().Should().Contain("Mark Sent to AP").And.NotContain("try again;");
        SentToApOf(host, batchId).Should().BeNull();
    }

    [Theory(DisplayName = "151c-1a refusal code: an ownership refusal from the send path is 409 email_owner_refused (not 503 'try again')")]
    [InlineData("record_owner_no_source")]
    [InlineData("record_owner_parent_unresolved")]
    [InlineData(ServerOwnerRecord.OwnerMismatchCode)]
    public async Task Email_OwnerRefusal_Is409_NoRetry(string code)
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        host.FakeMailer!.Fault = new SdapProblemException(code, "Record owner unresolved", "The message was not sent", 409);

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await BodyOf(response);
        body["errorCode"]!.GetValue<string>().Should().Be(ApExportCodes.EmailOwnerRefused);
        body["detail"]!.GetValue<string>().Should().Contain("trying again will not help");
        SentToApOf(host, batchId).Should().BeNull();
    }

    [Fact(DisplayName = "151c-1a F4: a batch not owned by a team (a user owns it) is 409 email_owner_refused before any file is read or anything is sent")]
    public async Task Email_BatchNotTeamOwned_Is409_NothingSent()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        host.World.Dataverse.Touch("sprk_billingexport", batchId, new Dictionary<string, object> { ["ownerid"] = new EntityReference("systemuser", ApExportWorld.CallerSystemUser) });
        var storageCalls = host.World.Gateway.StorageCalls;

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be(ApExportCodes.EmailOwnerRefused);
        host.FakeMailer!.Sent.Should().BeEmpty();
        host.World.Gateway.StorageCalls.Should().Be(storageCalls);
    }

    [Theory(DisplayName = "151c-1a D2 a: the AP address or a CC (any case) that is a monitored mailbox is 409 ap_email_recipient_monitored and nothing is read or sent")]
    [InlineData("AP@Example.com", null)]
    [InlineData("intake@example.com", "INTAKE@example.com")]
    public async Task Email_MonitoredRecipient_Is409_NothingSent(string monitored, string? cc)
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        host.MonitoredMailboxes.Add(monitored);
        var storageCalls = host.World.Gateway.StorageCalls;

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId), body: new { cc = cc is null ? Array.Empty<string>() : new[] { "ops@example.com", cc } });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be(ApExportCodes.ApEmailRecipientMonitored);
        host.FakeMailer!.Sent.Should().BeEmpty();
        host.World.Gateway.StorageCalls.Should().Be(storageCalls, "no file is read for a refused recipient");
        SentToApOf(host, batchId).Should().BeNull();
    }

    [Fact(DisplayName = "151c-1a D2 a must-not-fire: monitored mailboxes that are not recipients do not stop the send")]
    public async Task Email_OtherMonitoredMailboxes_DoNotStopTheSend()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        host.MonitoredMailboxes.Add("intake@example.com");

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId), body: new { cc = new[] { "ops@example.com" } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.FakeMailer!.Sent.Should().ContainSingle();
    }

    [Fact(DisplayName = "151c-1a A6: when the Sent to AP Date column cannot be read the route answers 503 BEFORE sending")]
    public async Task Email_SentToApColumnMissing_Is503_BeforeTheSend()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        host.World.Dataverse.Mock
            .Setup(e => e.RetrieveAsync("sprk_billingexport", batchId, It.Is<string[]>(c => c.Contains("sprk_senttoapdate")), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("'sprk_billingexport' entity doesn't contain attribute with Name = 'sprk_senttoapdate'"));

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await CodeOf(response)).Should().Be(ApExportCodes.Unavailable);
        host.FakeMailer!.Sent.Should().BeEmpty("the route must not send an email whose date it cannot record");
    }

    [Fact(DisplayName = "151c-1a A2: a client that disconnects after the send still gets the Sent to AP Date written (the caller's token is not used after the send)")]
    public async Task Email_CancelledAfterTheSend_StillSetsTheDate()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        using var disconnect = new CancellationTokenSource();
        host.FakeMailer!.DuringSend = disconnect.Cancel;

        // The Dataverse seam honours the token, as the real client does: a cancelled token fails the call.
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var world = host.World.Dataverse.Service;
        entities.Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string t, Guid id, string[] c, CancellationToken ct) => ct.IsCancellationRequested ? Task.FromCanceled<Entity>(ct) : world.RetrieveAsync(t, id, c, ct));
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((Microsoft.Xrm.Sdk.Query.QueryExpression q, CancellationToken ct) => ct.IsCancellationRequested ? Task.FromCanceled<EntityCollection>(ct) : world.RetrieveMultipleAsync(q, ct));
        entities.Setup(e => e.UpdateIfRowVersionMatchesAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string t, Guid id, string v, Dictionary<string, object> f, CancellationToken ct) => ct.IsCancellationRequested ? Task.FromCanceled(ct) : world.UpdateIfRowVersionMatchesAsync(t, id, v, f, ct));
        var delivery = host.NewDelivery(entities.Object);

        var result = await delivery.EmailAsync(batchId, new ApExportEmailRequest(null, null), ApExportWorld.CallerSystemUser, new DefaultHttpContext(), disconnect.Token);

        disconnect.IsCancellationRequested.Should().BeTrue();
        host.FakeMailer.Sent.Should().ContainSingle();
        result.Outcome.Should().Be(ApExportDeliveryOutcome.Done);
        result.Email!.SentToApDateSet.Should().BeTrue();
        SentToApOf(host, batchId).Should().Be(host.Clock.Now.UtcDateTime);
    }

    // =========================================================================================
    // Email to AP: the body (400) - nothing is read or sent
    // =========================================================================================

    public static IEnumerable<object[]> BadBodies()
    {
        yield return new object[] { new { cc = new[] { "not-an-address" } } };
        yield return new object[] { new { cc = new[] { "Name <a@example.com>" } } };
        yield return new object[] { new { cc = new[] { "a@example.com, b@example.com" } } };
        yield return new object[] { new { cc = new[] { "a@example.com", "" } } };
        yield return new object[] { new { cc = Enumerable.Range(0, 11).Select(i => $"cc{i}@example.com").ToArray() } };
        yield return new object[] { new { message = new string('x', 2001) } };
    }

    [Theory(DisplayName = "151c-1a: a bad CC (invalid, with a display name, two in one, blank), more than 10 CCs, or a message over 2000 characters is 400 and nothing is read or sent")]
    [MemberData(nameof(BadBodies))]
    public async Task Email_BadBody_Is400(object body)
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        var storageCalls = host.World.Gateway.StorageCalls;

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId), body: body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be(ApExportCodes.InvalidRequest);
        host.FakeMailer!.Sent.Should().BeEmpty();
        host.World.Gateway.StorageCalls.Should().Be(storageCalls);
    }

    [Fact(DisplayName = "151c-1a: exactly 10 CCs and a 2000-character message are accepted")]
    public async Task Email_AtTheLimits_IsAccepted()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId), body: new
        {
            cc = Enumerable.Range(0, 10).Select(i => $"cc{i}@example.com").ToArray(),
            message = new string('x', 2000),
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.FakeMailer!.Sent.Single().Cc.Should().HaveCount(10);
    }

    // =========================================================================================
    // Email to AP: authorization (Read on the batch; uniform 404)
    // =========================================================================================

    [Fact(DisplayName = "151c-1a: unauthenticated is 401 and touches nothing")]
    public async Task Email_Unauthenticated_Is401()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId), authenticated: false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.FakeMailer!.Sent.Should().BeEmpty();
    }

    [Fact(DisplayName = "151c-1a SEEDED: no Read on the batch (Write alone, or nothing) is the uniform 404 - no settings read, no bytes, no send")]
    public async Task Email_NoReadOnTheBatch_Is404_AndNothingHappens()
    {
        var (batchId, host) = await ExportedAsync(grantAccess: false);
        await using var _ = host;
        host.World.Access.Grant(ApExportAuthorization.BatchEntitySet, batchId, AccessRights.Write | AccessRights.Append);
        host.World.Access.Grant(ApExportAuthorization.BatchEntitySet, Guid.NewGuid(), AccessRights.Read);
        var storageCalls = host.World.Gateway.StorageCalls;
        var log = host.World.Dataverse.Log.Count;

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BodyOf(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.RecordUnavailableReasonCode);
        host.FakeMailer!.Sent.Should().BeEmpty();
        host.World.Gateway.StorageCalls.Should().Be(storageCalls);
        host.World.Dataverse.Log.Count.Should().Be(log, "not one Dataverse call was made for a caller without Read");
        SentToApOf(host, batchId).Should().BeNull();
    }

    [Theory(DisplayName = "151c-1a D1 SEEDED: Read alone (no Write) on the batch is the uniform 404 on both routes - nothing read, written or sent")]
    [InlineData("email")]
    [InlineData("mark-sent-to-ap")]
    public async Task ReadOnlyCaller_IsTheUniform404_AndNothingHappens(string route)
    {
        var (batchId, host) = await ExportedAsync(grantAccess: false);
        await using var _ = host;
        host.World.Access.Grant(ApExportAuthorization.BatchEntitySet, batchId, AccessRights.Read);
        var storageCalls = host.World.Gateway.StorageCalls;
        var log = host.World.Dataverse.Log.Count;

        var response = await host.SendAsync(HttpMethod.Post, $"/api/finance/export-batches/{batchId}/{route}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BodyOf(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.RecordUnavailableReasonCode);
        host.FakeMailer!.Sent.Should().BeEmpty();
        host.World.Gateway.StorageCalls.Should().Be(storageCalls);
        host.World.Dataverse.Log.Count.Should().Be(log, "not one Dataverse call was made for a caller without Write");
        SentToApOf(host, batchId).Should().BeNull();
    }

    [Fact(DisplayName = "151c-1a: an unknown batch the caller holds Read and Write on is the same uniform 404")]
    public async Task Email_UnknownBatch_IsTheUniform404()
    {
        await using var host = await ApExportRoutesHost.StartAsync();
        host.SetApEmail(ApAddress);
        var unknown = Guid.NewGuid();
        host.World.Access.Grant(ApExportAuthorization.BatchEntitySet, unknown, AccessRights.Read | AccessRights.Write);

        var response = await host.SendAsync(HttpMethod.Post, EmailUrl(unknown));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BodyOf(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.RecordUnavailableReasonCode);
        host.FakeMailer!.Sent.Should().BeEmpty();
    }

    public static IEnumerable<object[]> RefusedKinds() => StatedCaller.RefusedKinds.Select(k => new object[] { k });

    [Theory(DisplayName = "151c-1a task 099 must-fire: a flagged-external, row-less or unreadable caller is the uniform 404 before any rights call, on both routes")]
    [MemberData(nameof(RefusedKinds))]
    public async Task StepZero_RefusesBeforeAnyRead(StepZeroCallerKind kind)
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        var accessCalls = host.World.Access.Calls.Count;
        host.StepZero.Kind = kind;

        var email = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId));
        var mark = await host.SendAsync(HttpMethod.Post, MarkUrl(batchId));

        await host.StepZero.AssertRefusedAsync(email, kind, uniformNotFound: true);
        await host.StepZero.AssertRefusedAsync(mark, kind, uniformNotFound: true);
        host.World.Access.Calls.Count.Should().Be(accessCalls);
        host.FakeMailer!.Sent.Should().BeEmpty();
        SentToApOf(host, batchId).Should().BeNull();
    }

    [Fact(DisplayName = "151c-1a ADR-018: the kill switch is 503 ebilling.disabled for an authorized caller on both routes; an unauthorized caller still gets the 404")]
    public async Task FlagOff_AuthorizedGets503_UnauthorizedStillGets404()
    {
        var (batchId, host) = await ExportedAsync(ebillingEnabled: false);
        await using var _ = host;
        var unreadable = Guid.NewGuid();

        var email = await host.SendAsync(HttpMethod.Post, EmailUrl(batchId));
        var mark = await host.SendAsync(HttpMethod.Post, MarkUrl(batchId));
        var unauthorized = await host.SendAsync(HttpMethod.Post, EmailUrl(unreadable));

        email.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await CodeOf(email)).Should().Be("ebilling.disabled");
        mark.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await CodeOf(mark)).Should().Be("ebilling.disabled");
        unauthorized.StatusCode.Should().Be(HttpStatusCode.NotFound);
        host.FakeMailer!.Sent.Should().BeEmpty();
    }

    // =========================================================================================
    // Mark Sent to AP
    // =========================================================================================

    [Fact(DisplayName = "151c-1a: Mark Sent to AP sets the date when it is empty - 200 { sentToApDate, alreadySet: false } - and sends nothing")]
    public async Task Mark_SetsTheDate()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;

        var response = await host.SendAsync(HttpMethod.Post, MarkUrl(batchId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyOf(response);
        body["alreadySet"]!.GetValue<bool>().Should().BeFalse();
        body["sentToApDate"]!.GetValue<DateTimeOffset>().Should().Be(host.Clock.Now);
        SentToApOf(host, batchId).Should().Be(host.Clock.Now.UtcDateTime);
        host.FakeMailer!.Sent.Should().BeEmpty();
    }

    [Fact(DisplayName = "151c-1a: Mark Sent to AP is idempotent - a second call is 200 alreadySet true with the same date and writes nothing")]
    public async Task Mark_IsIdempotent()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        await host.SendAsync(HttpMethod.Post, MarkUrl(batchId));
        var first = SentToApOf(host, batchId);
        host.Clock.Now = host.Clock.Now.AddDays(5);
        var writes = host.World.Dataverse.WritesTo("sprk_billingexport");

        var response = await host.SendAsync(HttpMethod.Post, MarkUrl(batchId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyOf(response);
        body["alreadySet"]!.GetValue<bool>().Should().BeTrue();
        body["sentToApDate"]!.GetValue<DateTimeOffset>().UtcDateTime.Should().Be(first!.Value);
        SentToApOf(host, batchId).Should().Be(first);
        host.World.Dataverse.WritesTo("sprk_billingexport").Should().Be(writes);
    }

    [Fact(DisplayName = "151c-1a: a rival that sets the date between the read and the write wins - Mark Sent to AP reports alreadySet with the rival's date and never overwrites it")]
    public async Task Mark_RivalWins_NeverOverwrites()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        var rival = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);
        var armed = true;
        host.World.Dataverse.BeforeConditionalUpdate = (table, id) =>
        {
            if (armed && table == "sprk_billingexport")
            {
                armed = false;
                host.World.Dataverse.Touch(table, id, new Dictionary<string, object> { ["sprk_senttoapdate"] = rival });
            }
        };

        var response = await host.SendAsync(HttpMethod.Post, MarkUrl(batchId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyOf(response);
        body["alreadySet"]!.GetValue<bool>().Should().BeTrue();
        body["sentToApDate"]!.GetValue<DateTimeOffset>().UtcDateTime.Should().Be(rival);
        SentToApOf(host, batchId).Should().Be(rival);
    }

    [Theory(DisplayName = "151c-1a: Mark Sent to AP on a batch that is not Completed is 409 ebilling.export.batch_not_completed and writes nothing")]
    [InlineData(ApExportService.StatusFailed)]
    [InlineData(ApExportService.StatusGenerating)]
    public async Task Mark_BatchNotCompleted_Is409(int status)
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        host.World.Dataverse.Touch("sprk_billingexport", batchId, new Dictionary<string, object> { ["sprk_exportstatus"] = new OptionSetValue(status) });

        var response = await host.SendAsync(HttpMethod.Post, MarkUrl(batchId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be(ApExportCodes.BatchNotCompleted);
        SentToApOf(host, batchId).Should().BeNull();
    }

    [Fact(DisplayName = "151c-1a: a Dataverse fault on Mark Sent to AP is 503 ebilling.export.unavailable")]
    public async Task Mark_Fault_Is503()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        host.World.Dataverse.FaultOn = (op, table, _) => op == "update-if" && table == "sprk_billingexport" ? new InvalidOperationException("dataverse down") : null;

        var response = await host.SendAsync(HttpMethod.Post, MarkUrl(batchId));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await CodeOf(response)).Should().Be(ApExportCodes.Unavailable);
        SentToApOf(host, batchId).Should().BeNull();
    }

    [Fact(DisplayName = "151c-1a: unauthenticated Mark Sent to AP is 401")]
    public async Task Mark_Unauthenticated_Is401()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;

        var response = await host.SendAsync(HttpMethod.Post, MarkUrl(batchId), authenticated: false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        SentToApOf(host, batchId).Should().BeNull();
    }

    [Fact(DisplayName = "151c-1a SEEDED: no Read on the batch is the uniform 404 on Mark Sent to AP and nothing is read or written")]
    public async Task Mark_NoReadOnTheBatch_Is404_AndNothingHappens()
    {
        var (batchId, host) = await ExportedAsync(grantAccess: false);
        await using var _ = host;
        host.World.Access.Grant(ApExportAuthorization.BatchEntitySet, batchId, AccessRights.Write | AccessRights.Append);
        var log = host.World.Dataverse.Log.Count;

        var response = await host.SendAsync(HttpMethod.Post, MarkUrl(batchId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BodyOf(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.RecordUnavailableReasonCode);
        host.World.Dataverse.Log.Count.Should().Be(log, "not one Dataverse call was made for a caller without Read");
        SentToApOf(host, batchId).Should().BeNull();
    }

    [Fact(DisplayName = "151c-1a: an unknown batch the caller holds Read and Write on is the same uniform 404 on Mark Sent to AP")]
    public async Task Mark_UnknownBatch_IsTheUniform404()
    {
        await using var host = await ApExportRoutesHost.StartAsync();
        var unknown = Guid.NewGuid();
        host.World.Access.Grant(ApExportAuthorization.BatchEntitySet, unknown, AccessRights.Read | AccessRights.Write);

        var response = await host.SendAsync(HttpMethod.Post, MarkUrl(unknown));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BodyOf(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.RecordUnavailableReasonCode);
    }
}
