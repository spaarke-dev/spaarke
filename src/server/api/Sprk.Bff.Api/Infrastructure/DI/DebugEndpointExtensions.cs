using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// Debug/diagnostic endpoints (temporary — remove in production).
/// </summary>
public static class DebugEndpointExtensions
{
    public static void MapDebugEndpoints(this WebApplication app)
    {
        app.MapGet("/debug/document/{id:guid}", async (Guid id, IDataverseService dv, ILogger<Program> log) =>
        {
            try
            {
                var doc = await dv.GetDocumentAsync(id.ToString());
                if (doc == null) return Results.Ok(new { status = "NOT_FOUND", documentId = id.ToString() });
                return Results.Ok(new { status = "FOUND", documentId = doc.Id, name = doc.Name, fileName = doc.FileName,
                    isEmailArchive = doc.IsEmailArchive, parentDocumentId = doc.ParentDocumentId,
                    matterId = doc.MatterId, projectId = doc.ProjectId, invoiceId = doc.InvoiceId });
            }
            catch (Exception ex) { log.LogError(ex, "Error {Id}", id); return Results.Ok(new { status = "ERROR", error = ex.Message }); }
        }).AllowAnonymous();

        app.MapGet("/debug/children/{parentId:guid}", async (Guid parentId, IDataverseService dv, ILogger<Program> log) =>
        {
            try
            {
                var children = (await dv.GetDocumentsByParentAsync(parentId)).ToList();
                return Results.Ok(new { status = "OK", parentDocumentId = parentId.ToString(), childCount = children.Count,
                    children = children.Select(c => new { documentId = c.Id, name = c.Name, fileName = c.FileName,
                        isEmailArchive = c.IsEmailArchive, parentDocumentId = c.ParentDocumentId, createdOn = c.CreatedOn }) });
            }
            catch (Exception ex) { log.LogError(ex, "Error parent {Id}", parentId); return Results.Ok(new { status = "ERROR", error = ex.Message }); }
        }).AllowAnonymous();

        MapServiceBusDebugEndpoints(app);
        MapDiagnosticEndpoints(app);
    }

    private static void MapServiceBusDebugEndpoints(WebApplication app)
    {
        app.MapGet("/debug/office-dlq", async (Azure.Messaging.ServiceBus.ServiceBusClient sbc, ILogger<Program> log) =>
        {
            try { return Results.Ok(await PeekDlq(sbc, "office-upload-finalization")); }
            catch (Exception ex) { log.LogError(ex, "Error peeking DLQ"); return Results.Ok(new { error = ex.Message }); }
        }).AllowAnonymous();

        app.MapGet("/debug/office-indexing", async (Azure.Messaging.ServiceBus.ServiceBusClient sbc, ILogger<Program> log) =>
        {
            try { return Results.Ok(await PeekQueue(sbc, "office-indexing")); }
            catch (Exception ex) { log.LogError(ex, "Error peeking queue"); return Results.Ok(new { error = ex.Message }); }
        }).AllowAnonymous();

        app.MapGet("/debug/office-profile", async (Azure.Messaging.ServiceBus.ServiceBusClient sbc, ILogger<Program> log) =>
        {
            try { return Results.Ok(await PeekQueue(sbc, "office-profile")); }
            catch (Exception ex) { log.LogError(ex, "Error peeking queue"); return Results.Ok(new { error = ex.Message }); }
        }).AllowAnonymous();

        app.MapGet("/debug/sdap-jobs-dlq", async (Azure.Messaging.ServiceBus.ServiceBusClient sbc, ILogger<Program> log) =>
        {
            try
            {
                var queueName = app.Configuration["ServiceBus:QueueName"] ?? "sdap-jobs";
                return Results.Ok(await PeekDlq(sbc, queueName));
            }
            catch (Exception ex) { log.LogError(ex, "Error peeking DLQ"); return Results.Ok(new { error = ex.Message }); }
        }).AllowAnonymous();
    }

    private static void MapDiagnosticEndpoints(WebApplication app)
    {
        app.MapGet("/debug/job-handlers", (IServiceProvider sp, ILogger<Program> log) =>
        {
            try
            {
                using var scope = sp.CreateScope();
                var handlers = scope.ServiceProvider.GetServices<Sprk.Bff.Api.Services.Jobs.IJobHandler>().ToList();
                var info = handlers.Select(h => new { jobType = h.JobType, handlerType = h.GetType().FullName }).ToList();
                string? err1 = TryResolve<Sprk.Bff.Api.Services.Jobs.Handlers.AppOnlyDocumentAnalysisJobHandler>(scope);
                string? err2 = TryResolve<Sprk.Bff.Api.Services.Ai.IAppOnlyAnalysisService>(scope);
                return Results.Ok(new { totalHandlers = handlers.Count, handlers = info,
                    hasAppOnlyDocumentAnalysis = handlers.Any(h => h.JobType == "AppOnlyDocumentAnalysis"),
                    directHandlerResolution = err1 ?? "OK", analysisServiceResolution = err2 ?? "OK" });
            }
            catch (Exception ex) { log.LogError(ex, "Error"); return Results.Ok(new { error = ex.Message }); }
        }).AllowAnonymous();

        app.MapGet("/debug/communication-services", async (IServiceProvider sp, ILogger<Program> log) =>
        {
            var results = new Dictionary<string, string>();
            try
            {
                var svc = sp.GetRequiredService<Sprk.Bff.Api.Services.Communication.CommunicationAccountService>();
                results["CommunicationAccountService"] = "OK";
                try { var accts = await svc.QueryReceiveEnabledAccountsAsync(); results["ReceiveEnabledAccounts"] = $"{accts.Length} found";
                    foreach (var a in accts) results[$"Account:{a.EmailAddress}"] = $"SubId={a.SubscriptionId ?? "none"}, AutoCreate={a.AutoCreateRecords}"; }
                catch (Exception ex) { results["ReceiveEnabledAccounts"] = $"FAILED: {ex.Message}"; }
            }
            catch (Exception ex) { results["CommunicationAccountService"] = $"FAILED: {ex.Message}"; }
            try
            {
                var gf = sp.GetRequiredService<Sprk.Bff.Api.Infrastructure.Graph.IGraphClientFactory>();
                var subs = await gf.ForApp().Subscriptions.GetAsync();
                results["ActiveGraphSubscriptions"] = $"{subs?.Value?.Count ?? 0}";
                if (subs?.Value != null) foreach (var s in subs.Value) results[$"Subscription:{s.Id}"] = $"Resource={s.Resource}, Expires={s.ExpirationDateTime}";
            }
            catch (Exception ex) { results["ActiveGraphSubscriptions"] = $"FAILED: {ex.Message}"; }
            var hosted = sp.GetServices<Microsoft.Extensions.Hosting.IHostedService>().ToList();
            results["TotalHostedServices"] = hosted.Count.ToString();
            foreach (var s in hosted.Where(s => s.GetType().Namespace?.Contains("Communication") == true))
                results[$"HostedService:{s.GetType().Name}"] = "Running";
            return Results.Ok(results);
        }).AllowAnonymous();
    }

    private static string? TryResolve<T>(IServiceScope scope)
    {
        try { var svc = scope.ServiceProvider.GetService<T>(); return svc == null ? $"{typeof(T).Name} returned null" : null; }
        catch (Exception ex) { return $"{typeof(T).Name} failed: {ex.Message}"; }
    }

    private static async Task<object> PeekDlq(Azure.Messaging.ServiceBus.ServiceBusClient sbc, string queueName)
    {
        await using var receiver = sbc.CreateReceiver(queueName, new Azure.Messaging.ServiceBus.ServiceBusReceiverOptions
        {
            SubQueue = Azure.Messaging.ServiceBus.SubQueue.DeadLetter,
            ReceiveMode = Azure.Messaging.ServiceBus.ServiceBusReceiveMode.PeekLock
        });
        var messages = await receiver.PeekMessagesAsync(10);
        return new { queue = $"{queueName}/$DeadLetterQueue", count = messages.Count,
            messages = messages.Select(m => new { m.SequenceNumber, m.DeadLetterReason, m.EnqueuedTime, m.MessageId,
                bodyPreview = m.Body.ToString().Length > 500 ? m.Body.ToString()[..500] + "..." : m.Body.ToString() }) };
    }

    private static async Task<object> PeekQueue(Azure.Messaging.ServiceBus.ServiceBusClient sbc, string queueName)
    {
        await using var receiver = sbc.CreateReceiver(queueName);
        var messages = await receiver.PeekMessagesAsync(10);
        return new { queue = queueName, count = messages.Count,
            messages = messages.Select(m => new { m.SequenceNumber, m.EnqueuedTime, m.MessageId,
                bodyPreview = m.Body.ToString().Length > 500 ? m.Body.ToString()[..500] + "..." : m.Body.ToString() }) };
    }
}
