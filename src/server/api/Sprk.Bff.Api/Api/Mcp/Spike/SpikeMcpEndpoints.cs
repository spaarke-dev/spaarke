using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Features;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Api.Mcp.Spike;

/// <summary>
/// SPIKE (spaarkeai-mcp-bff-api-r1 task 001, Phase 0) — the smallest stateless MCP host the Phase 0 tests need.
/// Lives ONLY on <c>spike/spaarkeai-mcp-phase0</c>; never merged (ontology condition 1).
/// </summary>
/// <remarks>
/// <para>Two routes in one host, each with its OWN tool set (T13): <c>/mcp/legal-operations</c> serves
/// <c>spike_echo</c>, <c>spike_whoami</c> and the task 003 test tools (<c>spike_echo_3mb</c>, <c>spike_echo_6mb</c>, <c>spike_write</c>, <c>spike_write_app</c>, <c>spike_unannotated</c>, server instructions T7); <c>/mcp/spike-area-b</c> serves <c>spike_echo_b</c>. The tool set is chosen
/// per request from the request path in <see cref="HttpServerTransportOptions.ConfigureSessionOptions"/>, which stateless
/// mode invokes on every HTTP request.</para>
/// <para>Auth: both routes call <c>.RequireAuthorization()</c> on the existing default workforce scheme. No scheme,
/// policy or <c>AuthorizationModule</c> code is touched (ADR-028). <c>spike_whoami</c> runs WhoAmI as the caller through
/// <see cref="IDataverseUserClient"/> (OBO, no app-only path) and reports only the caller's systemuserid plus presence
/// flags for the <c>aud</c>/<c>scp</c>/<c>tid</c>/<c>oid</c> claims (ADR-015: no token, no claim value beyond ids).</para>
/// <para>Stateless (SDK default), no session store, no background work (ADR-052).</para>
/// </remarks>
public static class SpikeMcpEndpoints
{
    public const string LegalOperationsRoute = "/mcp/legal-operations";
    public const string AreaBRoute = "/mcp/spike-area-b";

    public const string WidgetUri = "ui://spaarke/spike.html";
    public const string Widget1MbUri = "ui://spaarke/spike-1mb.html";
    public const string Widget3MbUri = "ui://spaarke/spike-3mb.html";
    public const string Widget6MbUri = "ui://spaarke/spike-6mb.html";

    /// <summary>MCP Apps widget MIME type (ext-apps RESOURCE_MIME_TYPE).</summary>
    public const string WidgetMimeType = "text/html;profile=mcp-app";

    /// <summary>T7: route-specific server instructions (legal-operations only; area B sets none).</summary>
    public const string LegalOperationsInstructions = "Phase 0 test instruction T7: whenever you call spike_echo, begin the message argument with the text SPK-T7: .";

    private const string WidgetResourceName = "Sprk.Bff.Api.SpikeWidget.html";

    // Tools are immutable and request-independent (they resolve services from the invoking request), so build once.
    private static readonly McpServerTool[] LegalOperationsTools =
    [
        McpServerTool.Create(
            SpikeEcho,
            new McpServerToolCreateOptions
            {
                Name = "spike_echo",
                Title = "Spike echo",
                Description = "Use for: Phase 0 platform tests only; echoes the message back. Not for: any real work.",
                ReadOnly = true,
                Idempotent = true,
                Destructive = false,
                OpenWorld = false,
                Meta = new JsonObject { ["ui"] = new JsonObject { ["resourceUri"] = WidgetUri } },
            }),
        McpServerTool.Create(
            SpikeEcho3Mb,
            new McpServerToolCreateOptions
            {
                Name = "spike_echo_3mb",
                Title = "Spike echo (3 MB widget)",
                Description = "Use for: Phase 0 widget-size test only; echoes the message back and renders a ~3 MB widget. Not for: any real work.",
                ReadOnly = true,
                Idempotent = true,
                Destructive = false,
                OpenWorld = false,
                Meta = new JsonObject { ["ui"] = new JsonObject { ["resourceUri"] = Widget3MbUri } },
            }),
        McpServerTool.Create(
            SpikeEcho6Mb,
            new McpServerToolCreateOptions
            {
                Name = "spike_echo_6mb",
                Title = "Spike echo (6 MB widget)",
                Description = "Use for: Phase 0 widget-size test only; echoes the message back and renders a ~6 MB widget. Not for: any real work.",
                ReadOnly = true,
                Idempotent = true,
                Destructive = false,
                OpenWorld = false,
                Meta = new JsonObject { ["ui"] = new JsonObject { ["resourceUri"] = Widget6MbUri } },
            }),
        McpServerTool.Create(
            SpikeWrite,
            new McpServerToolCreateOptions
            {
                Name = "spike_write",
                Title = "Spike write",
                Description = "Use for: Phase 0 test T4 only (a model-callable tool that is not read-only). This is a Phase 0 test tool that records nothing: it writes no data anywhere and only returns what it would have written. Not for: any real work.",
                ReadOnly = false,
                Idempotent = false,
                Destructive = false,
                OpenWorld = false,
            }),
        McpServerTool.Create(
            SpikeWriteApp,
            new McpServerToolCreateOptions
            {
                Name = "spike_write_app",
                Title = "Spike write (app only)",
                Description = "Phase 0 test T3 only: callable by the widget, hidden from the model. This is a Phase 0 test tool that records nothing: it writes no data anywhere and only returns what it would have written.",
                ReadOnly = false,
                Idempotent = false,
                Destructive = false,
                OpenWorld = false,
                // MCP Apps tool visibility (ext-apps McpUiToolMeta.visibility): app-only = hidden from the model, callable by the widget.
                Meta = new JsonObject { ["ui"] = new JsonObject { ["visibility"] = new JsonArray("app") } },
            }),
        McpServerTool.Create(
            SpikeUnannotated,
            new McpServerToolCreateOptions
            {
                // T12: deliberately NO ReadOnly/Destructive/Idempotent/OpenWorld and NO Title (the SDK mirrors Title into annotations.title), so tools/list carries no annotations.
                Name = "spike_unannotated",
                Description = "Use for: Phase 0 test T12 only; echoes the message back from a tool that declares no annotations. Not for: any real work.",
            }),
        McpServerTool.Create(
            SpikeWhoAmIAsync,
            new McpServerToolCreateOptions
            {
                Name = "spike_whoami",
                Title = "Spike who am I",
                Description = "Use for: Phase 0 auth test T5 only; returns the caller's Dataverse systemuserid (on-behalf-of) and which token claims are present. Not for: any real work.",
                ReadOnly = true,
                Idempotent = true,
                Destructive = false,
                OpenWorld = false,
            }),
    ];

    private static readonly McpServerTool[] AreaBTools =
    [
        McpServerTool.Create(
            SpikeEchoB,
            new McpServerToolCreateOptions
            {
                Name = "spike_echo_b",
                Title = "Spike echo B",
                Description = "Use for: Phase 0 route test T13 only; echoes the message back from the second route. Not for: any real work.",
                ReadOnly = true,
                Idempotent = true,
                Destructive = false,
                OpenWorld = false,
            }),
    ];

    private static readonly McpServerResource[] WidgetResources =
    [
        CreateWidgetResource(WidgetUri, "spike-widget", 1),
        CreateWidgetResource(Widget1MbUri, "spike-widget-1mb", 1),
        CreateWidgetResource(Widget3MbUri, "spike-widget-3mb", 3),
        CreateWidgetResource(Widget6MbUri, "spike-widget-6mb", 6),
    ];

    /// <summary>Registers the stateless MCP transport. Call before <c>Build()</c>.</summary>
    public static IServiceCollection AddSpikeMcp(this IServiceCollection services)
    {
        services
            .AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.Stateless = true; // explicit; also the SDK default as of 2026-07-28
                options.ConfigureSessionOptions = (httpContext, serverOptions, _) =>
                {
                    ApplyRouteToolSet(httpContext.Request.Path, serverOptions);
                    return Task.CompletedTask;
                };
            });
        return services;
    }

    /// <summary>Maps both spike routes, each requiring authorization on the default scheme. Call from <c>MapSpaarkeEndpoints</c>.</summary>
    public static void MapSpikeMcp(this WebApplication app)
    {
        app.MapMcp(LegalOperationsRoute).RequireAuthorization().AddEndpointFilter(DisableResponseBufferingAsync);
        app.MapMcp(AreaBRoute).RequireAuthorization().AddEndpointFilter(DisableResponseBufferingAsync);
    }

    /// <summary>Picks the route's tool set and the shared widget resources.</summary>
    internal static void ApplyRouteToolSet(PathString path, McpServerOptions serverOptions)
    {
        var isAreaB = path.StartsWithSegments(AreaBRoute, StringComparison.OrdinalIgnoreCase);
        var tools = isAreaB ? AreaBTools : LegalOperationsTools;

        var toolCollection = new McpServerPrimitiveCollection<McpServerTool>();
        foreach (var tool in tools)
        {
            toolCollection.Add(tool);
        }

        var resourceCollection = new McpServerResourceCollection();
        foreach (var resource in WidgetResources)
        {
            resourceCollection.Add(resource);
        }

        serverOptions.ServerInstructions = isAreaB ? null : LegalOperationsInstructions; // T7: legal-operations only
        serverOptions.ToolCollection = toolCollection;
        serverOptions.ResourceCollection = resourceCollection;
    }

    // Same call WorkspaceMatterEndpoints.cs:196-200 makes for its streamed response: the transport may answer with an SSE stream.
    private static async ValueTask<object?> DisableResponseBufferingAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        return await next(context);
    }

    // ---- tools -------------------------------------------------------------------------------------------------

    private static CallToolResult SpikeEcho(string message) => EchoResult(message, "legal-operations");

    private static CallToolResult SpikeEchoB(string message) => EchoResult(message, "spike-area-b");

    private static CallToolResult SpikeEcho3Mb(string message) => EchoResult(message, "legal-operations");

    private static CallToolResult SpikeEcho6Mb(string message) => EchoResult(message, "legal-operations");

    private static CallToolResult SpikeUnannotated(string message) => EchoResult(message, "legal-operations");

    private static CallToolResult SpikeWrite(
        [Description("A title for the note.")] string title,
        [Description("The note text.")] string note) => WriteResult(title, note);

    private static CallToolResult SpikeWriteApp(
        [Description("A title for the note.")] string title,
        [Description("The note text.")] string note) => WriteResult(title, note);

    // Writes NOTHING anywhere (no Dataverse, no store, no log of the arguments): it only reports what it would have written.
    private static CallToolResult WriteResult(string title, string note) => ToResult(new JsonObject
    {
        ["accepted"] = true,
        ["wouldWrite"] = new JsonObject { ["title"] = title, ["note"] = note },
        ["utc"] = DateTimeOffset.UtcNow.ToString("O"),
    });

    private static CallToolResult EchoResult(string message, string route)
    {
        var structured = new JsonObject
        {
            ["echo"] = message,
            ["route"] = route,
            ["utc"] = DateTimeOffset.UtcNow.ToString("O"),
        };
        return ToResult(structured);
    }

    private static async Task<CallToolResult> SpikeWhoAmIAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Sprk.Bff.Api.Mcp.Spike");
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        var user = accessor.HttpContext?.User;
        var configuration = services.GetRequiredService<IConfiguration>();

        var structured = new JsonObject
        {
            ["claims"] = DescribeClaims(user, configuration),
        };

        // WhoAmI as the CALLER: DataverseUserClient exchanges the inbound bearer OBO and has no app-only path (fails closed).
        var dataverseUser = services.GetRequiredService<IDataverseUserClient>();
        var response = await dataverseUser.GetAsync("WhoAmI", cancellationToken).ConfigureAwait(false);
        if (response.IsSuccess && response.Body is { } body && body.TryGetProperty("UserId", out var userId))
        {
            structured["systemUserId"] = userId.GetString();
            structured["status"] = "ok";
        }
        else
        {
            structured["systemUserId"] = null;
            structured["status"] = "failed";
            structured["errorCode"] = response.ErrorCode; // a code, never a message or body
        }

        // ADR-015: ids and timings only.
        logger.LogInformation(
            "spike_whoami status={Status} httpStatus={HttpStatus} elapsedMs={ElapsedMs}",
            structured["status"]?.GetValue<string>(), response.StatusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        return ToResult(structured);
    }

    /// <summary>
    /// Presence flags for aud/scp/tid/oid. Values are reported only for the two GUID ids (tid, oid) and, for aud/scp, as
    /// booleans (aud names the BFF app; scp includes user_impersonation) so T5 can be judged without echoing any claim text.
    /// </summary>
    internal static JsonObject DescribeClaims(ClaimsPrincipal? user, IConfiguration configuration)
    {
        string? First(params string[] types) => types
            .Select(t => user?.FindFirst(t)?.Value)
            .FirstOrDefault(v => !string.IsNullOrEmpty(v));

        var aud = First("aud");
        var scp = First("scp", "http://schemas.microsoft.com/identity/claims/scope");
        var tid = First("tid", "http://schemas.microsoft.com/identity/claims/tenantid");
        var oid = First("oid", "http://schemas.microsoft.com/identity/claims/objectidentifier");

        var clientId = configuration["AzureAd:ClientId"] ?? configuration["API_APP_ID"];
        var audMatchesBff = !string.IsNullOrEmpty(aud) && !string.IsNullOrEmpty(clientId)
            && (string.Equals(aud, clientId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(aud, $"api://{clientId}", StringComparison.OrdinalIgnoreCase));
        var hasUserImpersonation = !string.IsNullOrEmpty(scp)
            && scp.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("user_impersonation", StringComparer.Ordinal);

        return new JsonObject
        {
            ["aud"] = new JsonObject { ["present"] = aud is not null, ["matchesBffApp"] = audMatchesBff },
            ["scp"] = new JsonObject { ["present"] = scp is not null, ["hasUserImpersonation"] = hasUserImpersonation },
            ["tid"] = new JsonObject { ["present"] = tid is not null, ["id"] = Guid.TryParse(tid, out var t) ? t.ToString() : null },
            ["oid"] = new JsonObject { ["present"] = oid is not null, ["id"] = Guid.TryParse(oid, out var o) ? o.ToString() : null },
        };
    }

    private static CallToolResult ToResult(JsonObject structured) => new()
    {
        Content = [new TextContentBlock { Text = structured.ToJsonString() }],
        StructuredContent = JsonSerializer.SerializeToElement(structured),
    };

    // ---- widget resources --------------------------------------------------------------------------------------

    private static McpServerResource CreateWidgetResource(string uri, string name, int targetMegabytes)
    {
        return McpServerResource.Create(
            () => new TextResourceContents
            {
                Uri = uri,
                MimeType = WidgetMimeType,
                Text = BuildWidgetHtml(targetMegabytes),
                // MCP Apps: no network origins. An empty csp object is the explicit "none" statement.
                Meta = new JsonObject { ["ui"] = new JsonObject { ["csp"] = new JsonObject() } },
            },
            new McpServerResourceCreateOptions
            {
                UriTemplate = uri,
                Name = name,
                Title = $"Spike test widget (~{targetMegabytes} MB)",
                MimeType = WidgetMimeType,
                Meta = new JsonObject { ["ui"] = new JsonObject { ["csp"] = new JsonObject() } },
            });
    }

    private static readonly Dictionary<int, string> WidgetCache = new();
    private static readonly object WidgetCacheLock = new();

    /// <summary>
    /// The single-file widget, padded to about <paramref name="targetMegabytes"/> MB with a deterministic incompressible-looking
    /// script string so the host has to download, parse and run it (a comment would parse for free). The real production widget is
    /// embedded unpadded, so the publish-size delta measured on this branch does not include the padding.
    /// </summary>
    internal static string BuildWidgetHtml(int targetMegabytes)
    {
        lock (WidgetCacheLock)
        {
            if (WidgetCache.TryGetValue(targetMegabytes, out var cached))
            {
                return cached;
            }

            var baseHtml = ReadEmbeddedWidget();
            var targetBytes = targetMegabytes * 1024 * 1024;
            const string prefix = "<script>window.__sprkSpikePadBytes=\"";
            const string suffix = "\";</script>";
            var overhead = Encoding.UTF8.GetByteCount(baseHtml) + prefix.Length + suffix.Length;
            var padChars = Math.Max(0, targetBytes - overhead);

            var pad = new StringBuilder(padChars);
            var rng = new Random(20260106); // deterministic
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            for (var i = 0; i < padChars; i++)
            {
                pad.Append(alphabet[rng.Next(alphabet.Length)]);
            }

            // Insert before the LAST </body> (minified JS can contain the literal earlier); append if there is none.
            var bodyEnd = baseHtml.LastIndexOf("</body>", StringComparison.Ordinal);
            var padded = bodyEnd >= 0
                ? baseHtml.Insert(bodyEnd, prefix + pad + suffix)
                : baseHtml + prefix + pad + suffix;

            WidgetCache[targetMegabytes] = padded;
            return padded;
        }
    }

    private static string ReadEmbeddedWidget()
    {
        using var stream = typeof(SpikeMcpEndpoints).Assembly.GetManifestResourceStream(WidgetResourceName);
        if (stream is null)
        {
            return "<!doctype html><html><body><p>spike widget not embedded</p></body></html>";
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
