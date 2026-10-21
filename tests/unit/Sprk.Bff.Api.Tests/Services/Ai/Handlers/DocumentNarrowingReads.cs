using System.Text.Json;
using Moq;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// unified-access-control-r2 task 188: answers the assistant tools' by-key reads (<c>{set}?$select=...&amp;$filter={key} eq {a}
/// or ...</c>) from seeded rows, so the document narrowing's parent-hop, communication and thread reads see exactly the rows
/// whose key the query names (as Dataverse does), whatever order or batching the predicate uses.
/// </summary>
internal static class DocumentNarrowingReads
{
    /// <summary>
    /// Every GET on <paramref name="set"/> whose filter names <paramref name="key"/> answers the seeded
    /// <paramref name="rowsJson"/> objects whose <paramref name="key"/> appears in the path. The paths are recorded in the
    /// returned list.
    /// </summary>
    public static List<string> AnswerByKey(Mock<IDataverseUserClient> dataverse, string set, string key, params string[] rowsJson)
    {
        var rows = rowsJson.Select(r => JsonDocument.Parse(r).RootElement.Clone()).ToList();
        var captured = new List<string>();
        dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith(set + "?") && p.Contains($"{key} eq ")), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((p, _) => captured.Add(p))
            .ReturnsAsync((string path, CancellationToken _) =>
            {
                var hit = rows.Where(r => path.Contains($"{key} eq {r.GetProperty(key).GetString()}", StringComparison.OrdinalIgnoreCase));
                return DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { value = hit }));
            });
        return captured;
    }
}
