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
