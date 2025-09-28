public sealed class SpeFileStore
{
    private readonly GraphServiceClient _graph;
    private readonly ILogger<SpeFileStore> _log;
    public SpeFileStore(GraphServiceClient graph, ILogger<SpeFileStore> log) { _graph = graph; _log = log; }

    public async Task<UploadSessionDto> StartUploadAsync(ResourceRef resource, long length, CancellationToken ct)
    {
        // Resolve driveItem; create upload session; return SDAP DTO
    }

    public async Task<FileHandleDto> GetDownloadHandleAsync(ResourceRef resource, CancellationToken ct)
    {
        // Build short-lived URL or stream and return as SDAP DTO
    }
}