using System.Data;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Tests.Api.Finance;
using Sprk.Bff.Api.Tests.DataMutation.Ebilling;
using Sprk.Bff.Api.Tests.Finance.Files;

namespace Sprk.Bff.Api.Tests.Auth.Finance;

/// <summary>
/// A test host of the REAL <c>MapApExportEndpoints</c>, <c>FinanceAuthorizationFilter</c>, <c>AuthorizationService</c> and eBilling kill switch over the
/// in-memory <see cref="ApExportWorld"/> (ebilling-r1 task 151a). It adds what the Mark Paid and download routes need beyond the export tests' host:
/// the REAL <see cref="InvoiceLifecycleService"/> over a Dataverse double that reads and writes the world's invoice rows (so the 409 reasons are the
/// lifecycle's own), and the world's Stored File service.
/// </summary>
internal sealed class ApExportRoutesHost : IAsyncDisposable
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public StatedCaller StepZero { get; private set; } = null!;

    public ApExportWorld World { get; } = new();

    /// <summary>Every conditional status write the lifecycle made, as the invoice id.</summary>
    public List<Guid> StatusWrites { get; } = [];

    /// <summary>The eBilling kill switch, read on every request (a test turns it off after setting its batch up).</summary>
    public bool EbillingEnabled { get; set; }

    /// <summary>How many times the lifecycle read an invoice's status (zero for a caller the route refused).</summary>
    public int StatusReads { get; private set; }

    /// <summary>When set, the lifecycle's Dataverse read throws it (the move faults).</summary>
    public Exception? LifecycleReadFault { get; set; }

    /// <summary>When true, a rival writer touches the invoice before every conditional status write (the version never matches).</summary>
    public bool RivalWriterOnEveryAttempt { get; set; }

    public static async Task<ApExportRoutesHost> StartAsync(bool ebillingEnabled = true, bool holdCreate = true)
    {
        var host = new ApExportRoutesHost();
        host.World.Probe.CallerSystemUserId = ApExportWorld.CallerSystemUser;
        if (holdCreate)
        {
            host.World.Probe.Hold(ApExportAuthorization.CreateBatchPrivilege);
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.Services
            .AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = FinanceAuthzTestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = FinanceAuthzTestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, FinanceAuthzTestAuthHandler>(FinanceAuthzTestAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        host.StepZero = StatedCaller.Register(builder.Services);
        host.EbillingEnabled = ebillingEnabled;
        builder.Services.AddOptions<EbillingOptions>();
        builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptionsMonitor<EbillingOptions>>(new SwitchableEbillingOptions(host));

        // The REAL authorization stack, substituted only at the access-data boundary and the privilege probe (the same fakes the world uses).
        builder.Services.AddSingleton<IAccessDataSource>(host.World.Access);
        builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
        builder.Services.AddScoped<AuthorizationService>();
        builder.Services.AddSingleton<CallerRecordAccessProbe>(host.World.Probe);

        // The REAL export service and Stored File service over the in-memory Dataverse and blob store; the REAL lifecycle over the same rows.
        builder.Services.AddScoped(_ => host.World.Service);
        builder.Services.AddScoped(_ => host.World.Files);
        builder.Services.AddScoped<IInvoiceLifecycleService>(_ => new InvoiceLifecycleService(
            host.LifecycleRecords(), Microsoft.Extensions.Logging.Abstractions.NullLogger<InvoiceLifecycleService>.Instance));

        builder.WebHost.UseTestServer();
        host._app = builder.Build();
        host._app.UseRouting();
        host._app.UseAuthentication();
        host._app.UseAuthorization();
        host._app.MapApExportEndpoints();
        await host._app.StartAsync();
        host._client = host._app.GetTestClient();
        return host;
    }

    /// <summary>The Dataverse boundary of <see cref="InvoiceLifecycleService"/> over the world's invoice rows (status read, version-conditional status write).</summary>
    private IFieldMappingDataverseService LifecycleRecords()
    {
        var records = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        records
            .Setup(r => r.RetrieveRecordFieldsAsync("sprk_invoice", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string[] _, CancellationToken _) =>
            {
                StatusReads++;
                if (LifecycleReadFault is { } fault)
                {
                    return Task.FromException<Dictionary<string, object?>>(fault);
                }

                try
                {
                    var row = World.Dataverse.Get(table, id);
                    return Task.FromResult(new Dictionary<string, object?>
                    {
                        ["statuscode"] = (long)row.GetAttributeValue<OptionSetValue>("statuscode").Value,
                        ["versionnumber"] = row.GetAttributeValue<long>("versionnumber"),
                    });
                }
                catch (Exception ex) when (StoredFileNotFound.Is(ex))
                {
                    return Task.FromResult(new Dictionary<string, object?>());
                }
            });
        records
            .Setup(r => r.UpdateRecordFieldsIfUnchangedAsync(
                "sprk_invoice", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, Dictionary<string, object?> fields, long version, CancellationToken _) =>
            {
                if (RivalWriterOnEveryAttempt)
                {
                    World.Dataverse.Touch(table, id, new Dictionary<string, object>());
                }

                var current = World.Dataverse.Get(table, id).GetAttributeValue<long>("versionnumber");
                if (current != version)
                {
                    return Task.FromException(new DBConcurrencyException("ConcurrencyVersionMismatch"));
                }

                World.Dataverse.Touch(table, id, new Dictionary<string, object>
                {
                    ["statuscode"] = new OptionSetValue(Convert.ToInt32(fields["statuscode"])),
                    ["statecode"] = new OptionSetValue(Convert.ToInt32(fields["statecode"])),
                });
                StatusWrites.Add(id);
                return Task.CompletedTask;
            });
        return records.Object;
    }

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, bool authenticated = true, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (authenticated)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
            request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "present");
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return _client.SendAsync(request);
    }

    /// <summary>Exports <paramref name="invoices"/> as one batch through the real POST route (as an authorized caller) and returns the new batch's id.</summary>
    public async Task<Guid> ExportAsync(IReadOnlyList<Entity> invoices, string[]? formats = null)
    {
        World.GrantEverything();
        var response = await SendAsync(HttpMethod.Post, "/api/finance/export-batches", body: new { invoiceIds = invoices.Select(i => i.Id).ToArray(), formats });
        var text = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != System.Net.HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"The export setup failed: {(int)response.StatusCode} {text}");
        }

        return JsonNode.Parse(text)!["batchId"]!.GetValue<Guid>();
    }

    /// <summary>Marks every blob the export wrote as scanned clean (the malware scanner's verdict, which the in-memory store does not produce).</summary>
    public void MarkExportedFilesScannedClean()
    {
        foreach (var (container, name) in World.Gateway.Names)
        {
            World.Gateway.Get(container, name)!.Tags = StoredFileRig.CleanTags(World.Gateway.Now.AddMinutes(1));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}

/// <summary>The kill switch's options, read per request from the host's <see cref="ApExportRoutesHost.EbillingEnabled"/>.</summary>
internal sealed class SwitchableEbillingOptions(ApExportRoutesHost host) : Microsoft.Extensions.Options.IOptionsMonitor<EbillingOptions>
{
    public EbillingOptions CurrentValue => new() { Enabled = host.EbillingEnabled };

    public EbillingOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<EbillingOptions, string?> listener) => null;
}
