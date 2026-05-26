using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Services.Ai.RecordSearch;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Default implementation of <see cref="IRecordMatchingAi"/>: a thin wrapper around
/// <see cref="IRecordSearchService"/>.
/// </summary>
public sealed class RecordMatchingAi : IRecordMatchingAi
{
    private readonly IRecordSearchService _recordSearch;

    public RecordMatchingAi(IRecordSearchService recordSearch)
    {
        _recordSearch = recordSearch ?? throw new ArgumentNullException(nameof(recordSearch));
    }

    /// <inheritdoc />
    public Task<RecordSearchResponse> SearchAsync(
        RecordSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _recordSearch.SearchAsync(request, cancellationToken);
    }
}
