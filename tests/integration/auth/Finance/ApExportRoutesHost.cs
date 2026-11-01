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
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
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

    /// <summary>The mail seam Email to AP sends through (task 151c-1a): a recording stand-in by default; a test may swap in a real one.</summary>
    public ApExportMailer Mailer { get; set; } = new RecordingApMailer();

    /// <summary>The stand-in <see cref="Mailer"/> (null when a test swapped in another).</summary>
    public RecordingApMailer? FakeMailer => Mailer as RecordingApMailer;

    /// <summary>The clock Email to AP and Mark Sent to AP stamp the date with.</summary>
    public FixedClock Clock { get; } = new(new DateTimeOffset(2026, 11, 2, 9, 30, 0, TimeSpan.Zero));

    /// <summary>What the delivery service logged.</summary>
    public CapturingLogger<ApExportDeliveryService> DeliveryLog { get; } = new();

    /// <summary>The mailboxes of the receive-enabled communication accounts (task 151c-1a D2 a): a recipient among them is refused.</summary>
    public List<string> MonitoredMailboxes { get; } = [];

    /// <summary>The REAL account service over a Dataverse double that answers <see cref="MonitoredMailboxes"/> (no cache entry, so it is read every time).</summary>
    public CommunicationAccountService Accounts { get; }

    public ApExportRoutesHost()
    {
        var dataverse = new Mock<ICommunicationDataverseService>();
        dataverse
            .Setup(d => d.QueryCommunicationAccountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string filter, string _, CancellationToken _) => Task.FromResult(
                filter.Contains("sprk_receiveenabled eq true", StringComparison.Ordinal)
                    ? MonitoredMailboxes.Select(address => new Entity("sprk_communicationaccount", Guid.NewGuid())
                    {
                        ["sprk_emailaddress"] = address,
                        ["sprk_name"] = address,
                        ["sprk_receiveenabled"] = true,
                        ["sprk_accounttype"] = new OptionSetValue(100000000),
                    }).ToArray()
                    : []));
        Accounts = new CommunicationAccountService(
            dataverse.Object, Mock.Of<IGenericEntityService>(), Mock.Of<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CommunicationAccountService>.Instance);
    }

    /// <summary>A delivery service over this host's world, mailer, accounts, log and clock (a test may hand it another Dataverse seam).</summary>
    public ApExportDeliveryService NewDelivery(IGenericEntityService? entities = null) =>
        new(World.Service, entities ?? World.Dataverse.Service, World.Files, Mailer, Accounts, DeliveryLog, Clock);

    /// <summary>Puts (or replaces) the active eBilling settings row with this AP export email (null: the row has none).</summary>
    public void SetApEmail(string? address)
    {
        var row = new Entity("sprk_ebillingsetting", Guid.Parse("15100000-0000-4000-8000-0000000000e5"))
        {
            ["statecode"] = new OptionSetValue(0),
            ["createdon"] = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        if (address is not null)
        {
            row["sprk_apexportemail"] = address;
        }

        World.Dataverse.Put(row);
    }

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
        builder.Services.AddScoped(_ => host.Mailer);
        builder.Services.AddScoped(_ => host.NewDelivery());
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

/// <summary>The stand-in for the communication send: records each request (with its server-only options) and answers a new communication id; can be told to fail.</summary>
internal sealed class RecordingApMailer : ApExportMailer
{
    public RecordingApMailer() : base(null!)
    {
    }

    public List<SendCommunicationRequest> Sent { get; } = [];

    /// <summary>When set, a send throws it (a Graph fault or a refusal).</summary>
    public Exception? Fault { get; set; }

    /// <summary>Runs while the send is in flight, after it succeeded (for example a client that disconnects).</summary>
    public Action? DuringSend { get; set; }

    public override Task<Guid?> SendAsync(SendCommunicationRequest request, Microsoft.AspNetCore.Http.HttpContext httpContext, CancellationToken ct)
    {
        if (Fault is { } fault)
        {
            return Task.FromException<Guid?>(fault);
        }

        Sent.Add(request);
        DuringSend?.Invoke();
        return Task.FromResult<Guid?>(Guid.NewGuid());
    }
}

/// <summary>A clock that reads a settable instant.</summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>A logger that keeps what it was given, for a test to assert an Error was written.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}

/// <summary>The kill switch's options, read per request from the host's <see cref="ApExportRoutesHost.EbillingEnabled"/>.</summary>
internal sealed class SwitchableEbillingOptions(ApExportRoutesHost host) : Microsoft.Extensions.Options.IOptionsMonitor<EbillingOptions>
{
    public EbillingOptions CurrentValue => new() { Enabled = host.EbillingEnabled };

    public EbillingOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<EbillingOptions, string?> listener) => null;
}
