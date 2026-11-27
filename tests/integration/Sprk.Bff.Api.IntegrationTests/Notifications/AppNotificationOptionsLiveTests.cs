// Live proof that the appnotification option sets AppNotificationOptions sends are the ones Dataverse defines. READ-ONLY.
//
// Opt-in only (skip-via-return, the repo's convention; see Playbooks/NotificationPlaybookFetchXmlLiveTests): a plain
// `dotnet test` touches no network. It uses the same gate as that file:
//   SPAARKE_LIVE_PLAYBOOKS_DATAVERSE_URL=https://<env>.crm.dynamics.com
// and an `az login` session for an identity that may read table metadata in that environment.
//
// It reads the priority, toasttype and icontype option sets of the appnotification table (Web API GET on
// EntityDefinitions metadata only) and compares them with the checked-in snapshot the unit test
// AppNotificationOptionsTests compares with the constants. Nothing is written.
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Sprk.Bff.Api.Services;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.IntegrationTests.Notifications;

public sealed class AppNotificationOptionsLiveTests
{
    private const string UrlVar = "SPAARKE_LIVE_PLAYBOOKS_DATAVERSE_URL";

    /// <summary>Reported as SKIPPED (not passed) when <see cref="UrlVar"/> is not set, so a run that never touched
    /// Dataverse cannot read as live evidence.</summary>
    private sealed class LiveAppNotificationFactAttribute : FactAttribute
    {
        public LiveAppNotificationFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(UrlVar)))
                Skip = $"Live leg: set {UrlVar} to run it against a real environment (read-only).";
        }
    }

    private readonly ITestOutputHelper _out;

    public AppNotificationOptionsLiveTests(ITestOutputHelper output) => _out = output;

    [LiveAppNotificationFact]
    public async Task LiveMetadata_PriorityToastTypeAndIconTypeOptionSets_MatchTheSnapshotAndTheConstants()
    {
        var url = Environment.GetEnvironmentVariable(UrlVar);
        if (string.IsNullOrWhiteSpace(url))
            return; // not opted in

        var token = await new AzureCliCredential().GetTokenAsync(new TokenRequestContext([$"{url.TrimEnd('/')}/.default"]), default);
        using var dv = new HttpClient { BaseAddress = new Uri($"{url.TrimEnd('/')}/api/data/v9.2/") };
        dv.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        dv.DefaultRequestHeaders.Add("OData-Version", "4.0");

        var snapshot = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Notifications", "appnotification-options.snapshot.json"))).RootElement;

        foreach (var (column, constants) in new (string, Type)[]
                 {
                     ("priority", typeof(AppNotificationOptions.Priority)),
                     ("toasttype", typeof(AppNotificationOptions.ToastType)),
                     ("icontype", typeof(AppNotificationOptions.IconType)),
                 })
        {
            var live = await ReadOptionSetAsync(dv, column);
            var expected = snapshot.GetProperty(column).EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.GetInt32(), StringComparer.OrdinalIgnoreCase);
            var sent = constants.GetFields().Where(f => f.IsLiteral)
                .ToDictionary(f => f.Name, f => (int)f.GetRawConstantValue()!, StringComparer.OrdinalIgnoreCase);

            _out.WriteLine($"{column}: live {string.Join(", ", live.Select(o => $"{o.Key}={o.Value}"))}");
            live.Should().BeEquivalentTo(expected, $"the snapshot for {column} must equal the live option set");
            sent.Should().BeEquivalentTo(live, $"the values the BFF sends for {column} must be the live option set");
        }
    }

    /// <summary>GET only: the option set of one appnotification picklist column, as label to value.</summary>
    private static async Task<Dictionary<string, int>> ReadOptionSetAsync(HttpClient dv, string column)
    {
        var meta = await dv.GetFromJsonAsync<JsonElement>(
            $"EntityDefinitions(LogicalName='appnotification')/Attributes(LogicalName='{column}')" +
            "/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?$select=LogicalName&$expand=OptionSet($select=Options),GlobalOptionSet($select=Options)");

        // A column bound to a global choice (priority, toasttype) reports its options under OptionSet or GlobalOptionSet.
        foreach (var holder in new[] { "OptionSet", "GlobalOptionSet" })
        {
            if (meta.TryGetProperty(holder, out var set) && set.ValueKind == JsonValueKind.Object
                && set.TryGetProperty("Options", out var options) && options.GetArrayLength() > 0)
            {
                return options.EnumerateArray().ToDictionary(
                    o => o.GetProperty("Label").GetProperty("UserLocalizedLabel").GetProperty("Label").GetString()!,
                    o => o.GetProperty("Value").GetInt32(),
                    StringComparer.OrdinalIgnoreCase);
            }
        }

        throw new InvalidOperationException($"appnotification.{column} returned no option set.");
    }
}
