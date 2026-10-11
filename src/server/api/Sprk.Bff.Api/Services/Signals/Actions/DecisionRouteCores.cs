using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Api.Events;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Signals.Actions;

/// <summary>What a shipped route handler answered, read back from its <see cref="IResult"/>.</summary>
/// <param name="Status">The HTTP status the handler produced.</param>
/// <param name="Body">The JSON body, when there is one.</param>
/// <param name="ReasonCode">The problem's <c>reasonCode</c> extension, when it has one.</param>
/// <param name="Detail">The problem's <c>detail</c>, when it has one.</param>
public sealed record RouteReply(int Status, JsonElement? Body, string? ReasonCode, string? Detail)
{
    public bool IsSuccess => Status is >= 200 and < 300;

    /// <summary>The <c>id</c> a create route returns, when the body carries one.</summary>
    public Guid? CreatedId =>
        Body is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
        && Guid.TryParse(id.GetString(), out var parsed) && parsed != Guid.Empty
            ? parsed
            : null;
}

/// <summary>
/// The decision executors' adapters over the SHIPPED write cores (task 044): the events complete handler, the
/// child-records create and update cores, the communications send core and the events due-date/assignee write. Each adapter
/// calls the existing handler unchanged, with the commit route's own <see cref="HttpContext"/> (so every write is authorized
/// and attributed to the signed-in caller), and reads its answer back. Nothing here decides anything; the cores keep their
/// own checks, ownership rules and best-effort follow-ups.
/// </summary>
/// <remarks>
/// <para><b>Why the services are taken from <c>RequestServices</c>.</b> The handlers take their collaborators as route
/// parameters. Calling them from a service means supplying exactly the instances the route would have bound, which are the
/// request-scoped ones; resolving them from the request's own container does that without re-declaring a dozen constructor
/// parameters here. Methods are <c>virtual</c> so the executors' unit tests substitute this seam (the cores have their own
/// suites) while a seam test runs the real thing.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: the cores listed above, all <c>internal static</c>
/// handlers. Extension: that is what this is; no core changes behaviour for its current consumers (the POML's second
/// escalation trigger did not fire). Cost of doing nothing: the executors would re-implement the writes and drift from the
/// routes.</para>
/// </remarks>
public class DecisionRouteCores
{
    /// <summary>The events complete handler (<c>POST /api/v1/events/{id}/complete</c>). Authorization is the caller's: the
    /// executor has already asked for Write, as the route's filter does.</summary>
    public virtual async Task<RouteReply> CompleteEventAsync(HttpContext http, Guid eventId, CancellationToken ct)
    {
        var sp = http.RequestServices;
        var result = await EventEndpoints.CompleteEventAsync(
            eventId, http,
            sp.GetRequiredService<IEventDataverseService>(),
            sp.GetRequiredService<IRecordOwnershipResolver>(),
            sp.GetRequiredService<ICallerSystemUserResolver>(),
            sp.GetRequiredService<IGenericEntityService>(),
            sp.GetRequiredService<ILogger<Program>>(),
            ct).ConfigureAwait(false);
        return await ReadAsync(result, http).ConfigureAwait(false);
    }

    /// <summary>The child-records create core (<c>POST /api/v1/child-records/{table}</c>): checked as the caller, created by
    /// the application, owned by the team the ownership rule names. Also the ONE work-assignment create (task 046, D-113):
    /// <c>sprk_workassignment</c> goes through the same handler, secure-create plan and root completion included, and
    /// <see cref="RouteReply.CreatedId"/> is the new work assignment's id (task 043's <c>sprk_followons</c>).</summary>
    public virtual async Task<RouteReply> CreateChildAsync(HttpContext http, string table, JsonElement payload, CancellationToken ct)
    {
        var sp = http.RequestServices;
        var result = await ChildRecordEndpoints.CreateAsync(
            table, payload,
            sp.GetRequiredService<IDataverseUserClient>(),
            sp.GetRequiredService<IRecordOwnershipResolver>(),
            sp.GetRequiredService<IFieldMappingDataverseService>(),
            sp.GetRequiredService<CoreAncestorRestamper>(),
            sp.GetRequiredService<SecureChildShareSynchronizer>(),
            sp.GetRequiredService<SecureRootFilingGate>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            http,
            sp.GetRequiredService<ILogger<Program>>(),
            ct).ConfigureAwait(false);
        return await ReadAsync(result, http).ConfigureAwait(false);
    }

    /// <summary>The child-records update core (<c>PATCH /api/v1/child-records/{table}/{id}</c>): the caller's own PATCH.</summary>
    public virtual async Task<RouteReply> UpdateChildAsync(
        HttpContext http, string table, Guid id, JsonElement payload, CancellationToken ct)
    {
        var sp = http.RequestServices;
        var result = await ChildRecordEndpoints.UpdateAsync(
            table, id, payload,
            sp.GetRequiredService<IDataverseUserClient>(),
            sp.GetRequiredService<IRecordOwnershipResolver>(),
            sp.GetRequiredService<CoreAncestorRestamper>(),
            sp.GetRequiredService<SecureChildReconciler>(),
            http,
            sp.GetRequiredService<ILogger<Program>>(),
            ct).ConfigureAwait(false);
        return await ReadAsync(result, http).ConfigureAwait(false);
    }

    /// <summary>The events due-date/assignee write (the narrow route's own core), as the caller.</summary>
    internal virtual async Task<EventDueAssigneeResult> WriteEventDueAssigneeAsync(
        HttpContext http, Guid eventId, UpdateEventDueAssigneeRequest request, CancellationToken ct)
    {
        var sp = http.RequestServices;
        var logger = sp.GetRequiredService<ILogger<Program>>();
        var callerContact = await EventEndpoints.ResolveActingUserContactAsync(
            sp.GetRequiredService<ICallerSystemUserResolver>(),
            sp.GetRequiredService<IIdentityNormalizationService>(),
            http, logger, ct).ConfigureAwait(false);
        return await EventDueAssigneeWrite.ApplyAsync(
            sp.GetRequiredService<IDataverseUserClient>(), eventId, request, callerContact,
            (sp.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow(), ct).ConfigureAwait(false);
    }

    /// <summary>Today in the caller's own time zone (the completed-date rule, task 098), for a recorded response.</summary>
    public virtual async Task<DateOnly> TodayForCallerAsync(HttpContext http, CancellationToken ct)
    {
        var sp = http.RequestServices;
        return await EventCompletionDate.ForCallerAsync(
            http.User,
            sp.GetRequiredService<ICallerSystemUserResolver>(),
            sp.GetRequiredService<IGenericEntityService>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System,
            sp.GetRequiredService<ILogger<Program>>(),
            ct).ConfigureAwait(false);
    }

    /// <summary>The communications send core (<c>CommunicationService.SendAsync</c>). A refusal the service throws is returned
    /// as a <see cref="RouteReply"/> with its status and code, never rethrown.</summary>
    public virtual async Task<(RouteReply Reply, Guid? CommunicationId)> SendCommunicationAsync(
        HttpContext http, SendCommunicationRequest request, CancellationToken ct)
    {
        try
        {
            var response = await http.RequestServices.GetRequiredService<CommunicationService>()
                .SendAsync(request, http, ct).ConfigureAwait(false);
            return (new RouteReply(StatusCodes.Status200OK, null, null, null), response.CommunicationId);
        }
        catch (SdapProblemException ex)
        {
            return (new RouteReply(ex.StatusCode, null, ex.Code, ex.Message), null);
        }
    }

    /// <summary>Executes <paramref name="result"/> against a scratch response and reads status and JSON body back.</summary>
    internal static async Task<RouteReply> ReadAsync(IResult result, HttpContext source)
    {
        var scratch = new DefaultHttpContext { RequestServices = source.RequestServices };
        await using var body = new MemoryStream();
        scratch.Response.Body = body;
        await result.ExecuteAsync(scratch).ConfigureAwait(false);

        JsonElement? json = null;
        if (body.Length > 0)
        {
            body.Position = 0;
            try
            {
                using var doc = await JsonDocument.ParseAsync(body).ConfigureAwait(false);
                json = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                // Not JSON: the status is the answer.
            }
        }

        string? reason = null;
        string? detail = null;
        if (json is { ValueKind: JsonValueKind.Object } o)
        {
            reason = Str(o, "reasonCode") ?? Str(o, "code");
            detail = Str(o, "detail");
        }

        return new RouteReply(scratch.Response.StatusCode, json, reason, detail);
    }

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
