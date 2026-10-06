using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Spaarke.ArchTests;

/// <summary>
/// The REAL BFF, booted in-process from its own <c>Program</c> — once as <c>Development</c> and once as
/// <c>Production</c> — so the route guard can check against the running application what it otherwise reads from
/// source (unified-access-control-r2 task 167 f2-v2, main-session round 52: "verify BEHAVIOUR against the real
/// application wherever a guard claims behaviour").
///
/// <para><b>What is real.</b> <c>Program.cs</c> runs end to end: every module registration, the authentication and
/// authorization registrations (policies, handlers, the FallbackPolicy), the middleware pipeline and every endpoint
/// mapping. Nothing about authorization or routing is replaced.</para>
///
/// <para><b>What is not, and why each is outside the claims.</b></para>
/// <list type="bullet">
///   <item>The <c>TokenCredential</c> is the shared stub (<c>UseStubTokenCredential</c>) — a test host must never reach
///   IMDS. It mints outbound tokens; it decides no inbound request.</item>
///   <item>Hosted services are removed: they are background workers (Service Bus processors, schedulers) that would
///   connect to the fake endpoints in the configuration. They map no route and register no policy.</item>
///   <item>Production needs a reachable Redis (<c>CacheModule</c> connects at registration with
///   <c>AbortOnConnectFail</c>), so the Production boot points <c>Redis:Endpoint</c> at
///   <see cref="HandshakeOnlyRedis"/>, a loopback listener that answers StackExchange.Redis's connect handshake and
///   nothing else. Since master T242 a Production BFF connects only by its managed identity over TLS, so the boot swaps
///   the two network steps (<c>CacheModule.NetworkStepsForBootedHostTests</c>) for a plain RESP2 connect; the mode
///   selection itself runs unchanged. The cache is never used: no request is sent.</item>
///   <item>Configuration is the fake values every BFF test host uses, plus what Production's fail-fast validators
///   demand (an HTTPS CORS origin, a customer id, the public config, the onboarding HMAC key, an Application Insights
///   connection string pointed at a closed loopback port). Every feature gate that decides whether a route group is
///   MAPPED is ON (<c>DocumentIntelligence:Enabled</c>, <c>Analysis:Enabled</c>, <c>RecordMatchingEnabled</c>), so the
///   endpoint table is the largest the configuration allows.</item>
/// </list>
///
/// <para>Each boot is lazy (only the tests that need it pay) and shared by every test of the class through
/// <c>IClassFixture</c>; the fixture disposes both hosts and the Redis listener.</para>
/// </summary>
public sealed class BootedBff : IDisposable
{
    private readonly Lazy<BootedApp> _development = new(() => new BootedApp(Environments.Development), LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly Lazy<BootedApp> _production = new(() => new BootedApp(Environments.Production), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The BFF booted with <c>EnvironmentName = Development</c>.</summary>
    public BootedApp Development => _development.Value;

    /// <summary>The BFF booted with <c>EnvironmentName = Production</c> — the environment App Service runs.</summary>
    public BootedApp Production => _production.Value;

    public void Dispose()
    {
        if (_development.IsValueCreated)
        {
            _development.Value.Dispose();
        }

        if (_production.IsValueCreated)
        {
            _production.Value.Dispose();
        }
    }
}

/// <summary>One booted BFF: its environment, its root service provider and a snapshot of its endpoint table.</summary>
public sealed class BootedApp : IDisposable
{
    private readonly BffFactory _factory;
    private readonly HandshakeOnlyRedis? _redis;

    internal BootedApp(string environment)
    {
        Environment = environment;
        _redis = environment == Environments.Development ? null : new HandshakeOnlyRedis();
        _factory = new BffFactory(environment, _redis?.Endpoint);
        if (_redis is not null)
        {
            // Master T242: a Production BFF reaches Redis ONLY by its managed identity over TLS (no connection string outside
            // Development/Testing). The mode selection runs unchanged (the endpoint is required and parsed); only the two
            // network steps are swapped for the loopback listener, which speaks plain RESP2 and needs no Entra token.
            Sprk.Bff.Api.Infrastructure.DI.CacheModule.NetworkStepsForBootedHostTests = (
                (_, _) => Task.CompletedTask,
                options =>
                {
                    options.Ssl = false;
                    options.Protocol = StackExchange.Redis.RedisProtocol.Resp2;
                    return StackExchange.Redis.ConnectionMultiplexer.Connect(options);
                });
        }

        try
        {
            Services = _factory.Services;
            Endpoints = Services.GetServices<EndpointDataSource>()
                .SelectMany(s => s.Endpoints)
                .OfType<RouteEndpoint>()
                .ToList();
            HostEnvironmentName = Services.GetRequiredService<IWebHostEnvironment>().EnvironmentName;
        }
        catch
        {
            _factory.Dispose();
            _redis?.Dispose();
            throw;
        }
        finally
        {
            // Consulted only while Program registers its modules; never left set for anything else in the process.
            Sprk.Bff.Api.Infrastructure.DI.CacheModule.NetworkStepsForBootedHostTests = null;
        }
    }

    /// <summary>The environment the boot ASKED for.</summary>
    public string Environment { get; }

    /// <summary>The environment the booted host REPORTS — must equal <see cref="Environment"/>.</summary>
    public string HostEnvironmentName { get; }

    public IServiceProvider Services { get; }

    /// <summary>Every <see cref="RouteEndpoint"/> the booted application maps.</summary>
    public IReadOnlyList<RouteEndpoint> Endpoints { get; }

    public void Dispose()
    {
        _factory.Dispose();
        _redis?.Dispose();
    }

    private sealed class BffFactory : WebApplicationFactory<Program>
    {
        private readonly string _environment;
        private readonly string? _redisConnectionString;

        public BffFactory(string environment, string? redisConnectionString)
        {
            _environment = environment;
            _redisConnectionString = redisConnectionString;
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Host configuration: visible while Program.cs registers its modules (the same mechanism every BFF test host uses).
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(Settings(_redisConnectionString)));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(_environment);

            // As in Production (ValidateScopes is on by default only in Development), so both boots resolve the same graph.
            builder.UseDefaultServiceProvider(options =>
            {
                options.ValidateScopes = false;
                options.ValidateOnBuild = false;
            });

            builder.ConfigureTestServices(services =>
            {
                services.UseStubTokenCredential();
                services.RemoveAll<IHostedService>();
            });
        }

        private static Dictionary<string, string?> Settings(string? redisConnectionString)
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:ServiceBus"] = "Endpoint=sb://archtests.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=dGVzdA==",
                ["ServiceBus:ConnectionString"] = "Endpoint=sb://archtests.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=dGVzdA==",
                ["ServiceBus:QueueName"] = "sdap-jobs",
                // customer-provisioning-orchestration-r1 T246: outside Development/Testing the BFF refuses to start without a
                // Content Safety endpoint (keyless, UAMI) — a placeholder is enough for a boot that makes no AI call.
                ["AiSafety:ContentSafety:Endpoint"] = "https://archtests.cognitiveservices.azure.com/",

                ["Cors:AllowedOrigins:0"] = "https://localhost:5173",

                ["UAMI_CLIENT_ID"] = "00000000-0000-0000-0000-0000000000a1",
                ["TENANT_ID"] = "00000000-0000-0000-0000-0000000000a2",
                ["API_APP_ID"] = "00000000-0000-0000-0000-0000000000a3",
                ["API_CLIENT_SECRET"] = "archtests-secret",
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "00000000-0000-0000-0000-0000000000a2",
                ["AzureAd:ClientId"] = "00000000-0000-0000-0000-0000000000a3",
                ["AzureAd:Audience"] = "api://00000000-0000-0000-0000-0000000000a3",

                ["Graph:TenantId"] = "00000000-0000-0000-0000-0000000000a2",
                ["Graph:ClientId"] = "00000000-0000-0000-0000-0000000000a1",
                ["Graph:ClientSecret"] = "archtests-secret",
                ["Graph:ManagedIdentity:Enabled"] = "false",
                ["Graph:Scopes:0"] = "https://graph.microsoft.com/.default",
                ["ManagedIdentity:ClientId"] = "00000000-0000-0000-0000-0000000000a1",

                ["Dataverse:EnvironmentUrl"] = "https://archtests.crm.dynamics.com",
                ["Dataverse:ServiceUrl"] = "https://archtests.crm.dynamics.com",
                ["Dataverse:ClientId"] = "00000000-0000-0000-0000-0000000000a1",
                ["Dataverse:ClientSecret"] = "archtests-secret",
                ["Dataverse:TenantId"] = "00000000-0000-0000-0000-0000000000a2",

                ["SpeAdmin:KeyVaultUri"] = "https://archtests.vault.azure.net/",
                ["Ciam:Instance"] = "https://archtests.ciamlogin.com/",
                ["Ciam:TenantId"] = "00000000-0000-0000-0000-0000000000c1",
                ["Ciam:Domain"] = "archtests.onmicrosoft.com",
                ["Ciam:Audience"] = "api://00000000-0000-0000-0000-0000000000c2",
                ["Ciam:GraphProvisioner:ClientId"] = "00000000-0000-0000-0000-0000000000c2",
                ["Ciam:GraphProvisioner:CertificateName"] = "archtests-ciam-cert",
                ["CosmosPersistence:Endpoint"] = "https://archtests.documents.azure.com:443/",
                ["CosmosPersistence:DatabaseName"] = "spaarke-ai-archtests",

                // Every gate that decides whether a route group is MAPPED, on.
                ["DocumentIntelligence:Enabled"] = "true",
                ["Analysis:Enabled"] = "true",
                ["DocumentIntelligence:RecordMatchingEnabled"] = "true",
                ["DocumentIntelligence:OpenAiEndpoint"] = "https://archtests.openai.azure.com/",
                ["DocumentIntelligence:OpenAiKey"] = "archtests-key",
                ["DocumentIntelligence:OpenAiDeployment"] = "gpt-4o",
                ["DocumentIntelligence:AiSearchEndpoint"] = "https://archtests.search.windows.net",
                ["DocumentIntelligence:AiSearchKey"] = "archtests-key",
                ["AzureOpenAI:Endpoint"] = "https://archtests.openai.azure.com/",
                ["AzureOpenAI:ChatModelName"] = "gpt-4o",

                ["PowerBi:TenantId"] = "00000000-0000-0000-0000-0000000000a2",
                ["PowerBi:ClientId"] = "00000000-0000-0000-0000-0000000000a1",
                ["PowerBi:ClientSecret"] = "archtests-secret",
                ["PowerBi:ApiUrl"] = "https://api.powerbi.com",
                ["PowerBi:Scope"] = "https://analysis.windows.net/.default",

                ["OfficeRateLimit:Enabled"] = "false",
                ["AiSearchResilience:MaxRetryAttempts"] = "3",
                ["AiSearchResilience:CircuitBreakerFailureThreshold"] = "5",
                ["AiSearchResilience:CircuitBreakerDuration"] = "00:00:30",
                ["GraphResilience:MaxRetryAttempts"] = "3",
                ["GraphResilience:RetryDelay"] = "00:00:01",
                ["GraphResilience:CircuitBreakerFailureThreshold"] = "5",
                ["GraphResilience:CircuitBreakerDuration"] = "00:00:30",
                ["ModelSelector:DefaultModel"] = "gpt-4o",
                ["AgentService:Enabled"] = "false",
                ["AgentService:Endpoint"] = "https://archtests.services.ai.azure.com/api/projects/archtests",
                ["AgentService:AgentId"] = "archtests-agent",
                ["AgentService:MaxConcurrency"] = "4",
                ["AgentService:ThreadCacheExpiryMinutes"] = "60",

                // What Production's fail-fast validators demand (Development / Testing are exempt from each).
                ["Customer:Id"] = "archtst",
                ["PublicConfig:BffUrl"] = "https://archtests.example.com",
                ["PublicConfig:MsalClientId"] = "00000000-0000-0000-0000-0000000000a3",
                ["PublicConfig:TenantId"] = "00000000-0000-0000-0000-0000000000a2",
                ["Onboarding:HmacSigningKey"] = "archtests-hmac-signing-key",
                ["APPLICATIONINSIGHTS_CONNECTION_STRING"] =
                    "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=http://127.0.0.1:9/;LiveEndpoint=http://127.0.0.1:9/",
            };

            if (redisConnectionString is null)
            {
                settings["Redis:Enabled"] = "false";
                settings["Redis:AllowInMemoryFallback"] = "true";
            }
            else
            {
                settings["Redis:Enabled"] = "true";
                settings["Redis:Endpoint"] = redisConnectionString;   // host:port, as a Production App Service carries it
                settings["Redis:InstanceName"] = "archtests:";
            }

            return settings;
        }
    }
}


/// <summary>
/// A loopback TCP listener that answers StackExchange.Redis's CONNECT HANDSHAKE — <c>CLIENT</c> (SETNAME, SETINFO, ID),
/// <c>CONFIG GET</c>, <c>INFO</c>, <c>ECHO</c> (the tracer), <c>PING</c>, <c>SUBSCRIBE</c> and a <c>GET</c> of the tie-break
/// key — and refuses every other command. It exists only so the BFF's <c>CacheModule</c> can complete
/// <c>ConnectionMultiplexer.Connect</c> with <c>AbortOnConnectFail</c> when the app boots as Production. No test sends
/// a request that would use the cache, so no other command is answered.
/// </summary>
internal sealed class HandshakeOnlyRedis : IDisposable
{
    private static readonly byte[] Crlf = "\r\n"u8.ToArray();

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();

    public HandshakeOnlyRedis()
    {
        _listener.Start();
        _ = Task.Run(AcceptLoopAsync);
    }

    /// <summary>host:port — the shape of <c>Redis__Endpoint</c> (master T242: no credential, no options).</summary>
    public string Endpoint => $"127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var network = client.GetStream();
                var reader = new BufferedStream(network);   // reads only; replies go straight to the socket
                var subscribed = false;
                while (!_stop.IsCancellationRequested)
                {
                    var command = await ReadCommandAsync(reader, _stop.Token);
                    if (command is null || command.Count == 0)
                    {
                        return;
                    }

                    var reply = Reply(command, ref subscribed);
                    await network.WriteAsync(reply, _stop.Token);
                }
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                // Shutting down.
            }
            catch (IOException)
            {
                // The client closed the connection.
            }
        }
    }

    private static byte[] Simple(string line) => Encoding.ASCII.GetBytes(line + "\r\n");

    private static byte[] Bulk(byte[] value)
        => [.. Encoding.ASCII.GetBytes($"${value.Length}\r\n"), .. value, .. Crlf];

    private static byte[] Bulk(string value) => Bulk(Encoding.UTF8.GetBytes(value));

    private static byte[] Reply(IReadOnlyList<byte[]> command, ref bool subscribed)
    {
        var name = Encoding.ASCII.GetString(command[0]).ToUpperInvariant();
        var sub = command.Count > 1 ? Encoding.ASCII.GetString(command[1]).ToUpperInvariant() : string.Empty;
        switch (name)
        {
            case "PING":
                return subscribed
                    ? [.. "*2\r\n"u8, .. Bulk("pong"), .. Bulk(command.Count > 1 ? command[1] : [])]
                    : command.Count > 1 ? Bulk(command[1]) : Simple("+PONG");
            case "ECHO":
                return Bulk(command.Count > 1 ? command[1] : []);
            case "INFO":
                return Bulk("# Server\r\nredis_version:7.2.4\r\nredis_mode:standalone\r\n# Replication\r\nrole:master\r\nconnected_slaves:0\r\n");
            case "CONFIG":
                return Simple("*0");
            case "CLIENT":
                return sub == "ID" ? Simple(":1") : Simple("+OK");
            case "SELECT":
                return Simple("+OK");
            case "GET":
                return Simple("$-1");
            case "SUBSCRIBE":
            case "PSUBSCRIBE":
            {
                subscribed = true;
                var kind = Encoding.ASCII.GetBytes(name.ToLowerInvariant());
                var reply = new List<byte>();
                for (var i = 1; i < command.Count; i++)
                {
                    reply.AddRange("*3\r\n"u8.ToArray());
                    reply.AddRange(Bulk(kind));
                    reply.AddRange(Bulk(command[i]));
                    reply.AddRange(Simple($":{i}"));
                }

                return reply.ToArray();
            }

            case "CLUSTER":
                return Simple("-ERR This instance has cluster support disabled");
            default:
                return Simple($"-ERR unknown command '{name}'");
        }
    }

    /// <summary>One RESP command — an array of bulk strings — as raw bytes; null at end of stream.</summary>
    private static async Task<List<byte[]>?> ReadCommandAsync(Stream stream, CancellationToken cancellationToken)
    {
        var line = await ReadLineAsync(stream, cancellationToken);
        if (line is null || !line.StartsWith('*'))
        {
            return null;
        }

        var count = int.Parse(line[1..], System.Globalization.CultureInfo.InvariantCulture);
        var parts = new List<byte[]>(Math.Max(0, count));
        for (var i = 0; i < count; i++)
        {
            var header = await ReadLineAsync(stream, cancellationToken);
            if (header is null || !header.StartsWith('$'))
            {
                return null;
            }

            var length = int.Parse(header[1..], System.Globalization.CultureInfo.InvariantCulture);
            var buffer = new byte[length + 2];
            await stream.ReadExactlyAsync(buffer, cancellationToken);
            parts.Add(buffer[..length]);
        }

        return parts;
    }

    /// <summary>A protocol line (a <c>*</c> or <c>$</c> header — ASCII only), without its CRLF; null at end of stream.</summary>
    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(one, cancellationToken) == 0)
            {
                return null;
            }

            if (one[0] == (byte)'\n' && bytes.Count > 0 && bytes[^1] == (byte)'\r')
            {
                return Encoding.ASCII.GetString(bytes.ToArray(), 0, bytes.Count - 1);
            }

            bytes.Add(one[0]);
        }
    }
}
