using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Abstractions.Store;
using Moq;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// #1830 (uac-r2 task 192) — one SharePoint Embedded container as the real Graph SDK sees it, behind a recording
/// <see cref="IRequestAdapter"/> (the SDK's own seam, not a transport mock — the <c>DemoSpeGrantLifecycleTests</c>
/// convention). The container read either answers <see cref="ContainerReadError"/> or a container with no custom
/// properties; the permission collection answers <see cref="PermissionPages"/> empty pages (each but the last with an
/// <c>@odata.nextLink</c>) unless <see cref="PermissionPageError"/> names one; permission DELETEs and
/// <c>customProperties</c> PATCHes are recorded.
/// </summary>
internal sealed class SpeContainerGraphStub : IRequestAdapter
{
    private int _permissionPagesServed;

    /// <summary>Thrown by every container GET when set (as the SDK throws a mapped Graph error).</summary>
    public ODataError? ContainerReadError { get; set; }

    /// <summary>How many pages the permission collection has.</summary>
    public int PermissionPages { get; set; } = 1;

    /// <summary>Thrown by the permission-collection GET of this 1-based page when set (2 = the first nextLink page).</summary>
    public (int Page, ODataError Error)? PermissionPageError { get; set; }

    /// <summary>Every request sent, as <c>METHOD url</c>.</summary>
    public List<string> Requests { get; } = [];

    public GraphServiceClient Client => new(this);

    /// <summary>A Graph error with this status, code and message — the shape the SDK hands to callers.</summary>
    public static ODataError Error(int status, string code, string message)
        => new() { ResponseStatusCode = status, Error = new MainError { Code = code, Message = message } };

    /// <summary>What dev Graph answered for project 65a3fab2's container on 2026-10-22, with any token.</summary>
    public static ODataError InvalidHostname()
        => Error(400, "invalidRequest", "Invalid hostname for this tenancy");

    public ISerializationWriterFactory SerializationWriterFactory => throw new NotSupportedException();

    public string? BaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";

    public void EnableBackingStore(IBackingStoreFactory backingStoreFactory) { }

    public Task<ModelType?> SendAsync<ModelType>(
        RequestInformation requestInfo, ParsableFactory<ModelType> factory,
        Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default, CancellationToken cancellationToken = default)
        where ModelType : IParsable
    {
        Requests.Add($"{requestInfo.HttpMethod} {requestInfo.URI}");
        if (typeof(ModelType) == typeof(FileStorageContainer))
        {
            if (ContainerReadError is { } error)
                throw error;
            return Task.FromResult((ModelType?)(object)new FileStorageContainer { Id = "stub" });
        }

        if (typeof(ModelType) == typeof(PermissionCollectionResponse))
        {
            var page = ++_permissionPagesServed;
            if (PermissionPageError is { } failing && failing.Page == page)
                throw failing.Error;
            return Task.FromResult((ModelType?)(object)new PermissionCollectionResponse
            {
                Value = [],
                OdataNextLink = page < PermissionPages ? $"{BaseUrl}/storage/fileStorage/containers/stub/permissions?$skiptoken={page}" : null,
            });
        }

        throw new NotSupportedException($"Unexpected Graph read {requestInfo.URI}.");
    }

    public Task SendNoContentAsync(
        RequestInformation requestInfo,
        Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default,
        CancellationToken cancellationToken = default)
    {
        Requests.Add($"{requestInfo.HttpMethod} {requestInfo.URI}");
        return Task.CompletedTask;
    }

    public Task<ModelType?> SendPrimitiveAsync<ModelType>(
        RequestInformation requestInfo,
        Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default, CancellationToken cancellationToken = default)
    {
        // The marker PATCH (SpeAdminGraphService.SendGraphJsonAsync) — an empty body.
        Requests.Add($"{requestInfo.HttpMethod} {requestInfo.URI}");
        return Task.FromResult(default(ModelType));
    }

    public Task<IEnumerable<ModelType>?> SendCollectionAsync<ModelType>(
        RequestInformation requestInfo, ParsableFactory<ModelType> factory,
        Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default, CancellationToken cancellationToken = default)
        where ModelType : IParsable
        => throw new NotSupportedException();

    public Task<IEnumerable<ModelType>?> SendPrimitiveCollectionAsync<ModelType>(
        RequestInformation requestInfo,
        Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<NativeResponseType?> ConvertToNativeRequestAsync<NativeResponseType>(
        RequestInformation requestInfo, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}

/// <summary>
/// An ownership guard whose answer per call is scripted — a fault (e.g. a throttled ownership read's
/// <see cref="SpaarkeStorageException"/>), owned, or refused. Clients come from <paramref name="graph"/>.
/// </summary>
internal sealed class ScriptedOwnershipGuard(GraphServiceClient graph, Func<int, string, bool> isOwned)
    : SpeContainerOwnershipGuard(
        Mock.Of<IGraphClientFactory>(f => f.ForApp() == graph), Array.Empty<string?>(), TestSpeOwnership.Customer("cust1"),
        NullLogger<SpeContainerOwnershipGuard>.Instance)
{
    private int _calls;

    /// <summary>How many ownership checks were asked.</summary>
    public int Calls => _calls;

    public override Task<bool> IsOwnedAsync(string containerOrDriveId, CancellationToken ct = default)
        => Task.FromResult(isOwned(Interlocked.Increment(ref _calls), containerOrDriveId));
}
