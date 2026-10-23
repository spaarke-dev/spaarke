using System.Text.Json;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// Projects seeded communication rows through the <c>$select</c> of the OData query that was sent, as Dataverse does: a
/// column the query did not ask for is not in the response (annotations such as <c>_x_value@OData...FormattedValue</c> ride
/// with their column). A double that returns every seeded column regardless of <c>$select</c> cannot see a query that
/// dropped a lookup the withholding rule needs (r3 task 039 fix round, K3).
/// </summary>
internal static class AppOnlySelectProjection
{
    /// <summary>
    /// The one thread every seeded row sits in, Open (owner decision D7: the external read shows a row only when its thread
    /// reads explicitly Open). The suites that are not about D7 seed rows through <see cref="WithOpenThread"/> and answer the
    /// thread query with <see cref="OpenThreadAnswer"/>; the D7 cases themselves are in CommunicationOpenThreadGateTests.
    /// </summary>
    public static readonly Guid OpenThreadId = Guid.Parse("0be7f1e5-0000-4000-8000-000000000001");

    /// <summary>Gives every row without a thread lookup the Open thread.</summary>
    public static IReadOnlyList<Dictionary<string, JsonElement>> WithOpenThread(IEnumerable<Dictionary<string, JsonElement>> rows) =>
        rows.Select(r => r.ContainsKey("_sprk_communicationthread_value")
                ? r
                : new Dictionary<string, JsonElement>(r)
                {
                    ["_sprk_communicationthread_value"] = JsonSerializer.SerializeToElement(OpenThreadId.ToString("D")),
                })
            .ToList();

    /// <summary>The thread-privacy answer for the suites that are not about D7: the Open thread, projected through the query's $select.</summary>
    public static IReadOnlyList<Dictionary<string, JsonElement>> OpenThreadAnswer(string? odata) =>
        Project(
            new[]
            {
                new Dictionary<string, JsonElement>
                {
                    ["sprk_communicationthreadid"] = JsonSerializer.SerializeToElement(OpenThreadId.ToString("D")),
                    ["sprk_privacystate"] = JsonSerializer.SerializeToElement(100000000),
                },
            },
            odata);

    public static IReadOnlyList<Dictionary<string, JsonElement>> Project(
        IEnumerable<Dictionary<string, JsonElement>> rows, string? odata)
    {
        var start = odata?.IndexOf("$select=", StringComparison.Ordinal) ?? -1;
        if (odata is null || start < 0)
            return rows.ToList();

        var end = odata.IndexOf('&', start);
        var list = odata[(start + "$select=".Length)..(end < 0 ? odata.Length : end)];
        var columns = list.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        return rows
            .Select(r => r
                .Where(kv => columns.Contains(kv.Key.Split('@')[0]))
                .ToDictionary(kv => kv.Key, kv => kv.Value))
            .ToList();
    }
}
