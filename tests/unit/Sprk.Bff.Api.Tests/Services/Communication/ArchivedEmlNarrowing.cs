using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Identity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Ontology task 177: an archived <c>.eml</c> as the send created it, decided by the REAL document narrowing on both planes —
/// the external plane (<see cref="ExternalDocumentNarrowing.DecideAsync"/>, as a contact with a grant on the record is narrowed)
/// and the internal plane's document routes (<see cref="InternalPlaneDocumentNarrowing.ForCallerAsync"/> for a systemuser flagged
/// external, whose <see cref="InternalRouteDocumentNarrowing"/> is that same decision). The narrowing's app-only reads are
/// answered from <see cref="World"/>.
/// </summary>
internal static class ArchivedEmlNarrowing
{
    /// <summary>The document row exactly as the send handed it to the writer, under the id the writer returned.</summary>
    public static Entity AsStored(Entity created, Guid id)
    {
        var document = new Entity("sprk_document", id);
        foreach (var attribute in created.Attributes)
            document[attribute.Key] = attribute.Value;
        return document;
    }

    /// <summary>The external plane: whether the narrowing shows the document, and why not.</summary>
    public static async Task<(bool Visible, DocumentNarrowingReason Reason)> ExternalAsync(Entity document, World world)
    {
        var row = ExternalDocumentNarrowing.FromEntity(document);
        var result = await ExternalDocumentNarrowing.DecideAsync([row], world.Query, CancellationToken.None);
        return (result.IsVisible(row), result.ReasonFor(row));
    }

    /// <summary>The internal plane's document routes, for a resolved systemuser whose <c>sprk_isexternal</c> is set.</summary>
    public static async Task<bool> InternalPlaneFlaggedExternalAsync(Entity document, World world)
    {
        var systemUserId = Guid.NewGuid();
        var callers = new Mock<ICallerSystemUserResolver>();
        callers.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(systemUserId.ToString("D")));
        var identity = new Mock<ISystemUserIdentityResolver>();
        identity.Setup(i => i.IsExternalAsync(systemUserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var row = ExternalDocumentNarrowing.FromEntity(document);
        var services = new ServiceCollection()
            .AddSingleton(callers.Object)
            .AddSingleton(identity.Object)
            .AddSingleton<InternalRouteDocumentNarrowing>((ids, ct) =>
                ExternalDocumentNarrowing.DecideAsync(ids.Contains(document.Id) ? [row] : [], world.Query, ct))
            .BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", Guid.NewGuid().ToString())], "Test")),
        };

        var visibility = await InternalPlaneDocumentNarrowing.ForCallerAsync(
            context, [document.Id.ToString()], "test", logger: null);
        return visibility.IsShown(document.Id.ToString());
    }

    /// <summary>
    /// The narrowing's app-only reads over seeded rows: each by-key read answers the rows whose key is in the filter, projected
    /// through the query's own <c>$select</c> (a column the predicate does not select reads as absent).
    /// </summary>
    public sealed class World
    {
        private readonly Dictionary<string, (string Key, List<Dictionary<string, JsonElement>> Rows)> _sets = new();

        public void Add(string set, string key, Guid id, Dictionary<string, object?> columns)
        {
            if (!_sets.TryGetValue(set, out var entry))
                _sets[set] = entry = (key, new List<Dictionary<string, JsonElement>>());
            var row = columns.ToDictionary(kv => kv.Key, kv => JsonSerializer.SerializeToElement(kv.Value));
            row[key] = JsonSerializer.SerializeToElement(id.ToString());
            entry.Rows.Add(row);
        }

        /// <summary>A communication and its thread, as the narrowing's communication leg reads them.</summary>
        public void AddCommunication(Guid communicationId, bool internalOnly, int threadPrivacy)
        {
            var threadId = Guid.NewGuid();
            Add("sprk_communications", "sprk_communicationid", communicationId, new Dictionary<string, object?>
            {
                ["sprk_isinternalonly"] = internalOnly,
                ["_sprk_communicationthread_value"] = threadId.ToString(),
            });
            Add("sprk_communicationthreads", "sprk_communicationthreadid", threadId, new Dictionary<string, object?>
            {
                ["sprk_privacystate"] = threadPrivacy,
            });
        }

        public Task<IReadOnlyList<Dictionary<string, JsonElement>>> Query(string set, string odata, CancellationToken ct)
        {
            if (!_sets.TryGetValue(set, out var entry))
                return Task.FromResult<IReadOnlyList<Dictionary<string, JsonElement>>>([]);
            var ids = Regex.Matches(odata, $@"{entry.Key} eq ([0-9a-fA-F-]{{36}})").Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var select = Regex.Match(odata, @"\$select=([^&]*)").Groups[1].Value.Split(',').ToHashSet(StringComparer.Ordinal);
            IReadOnlyList<Dictionary<string, JsonElement>> hit = entry.Rows
                .Where(r => ids.Contains(r[entry.Key].GetString()!))
                .Select(r => r.Where(kv => select.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value))
                .ToList();
            return Task.FromResult(hit);
        }
    }
}
