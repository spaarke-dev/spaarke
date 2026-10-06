// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/DocumentContainer/**`
//   - Justification: a relocation REPLAYS the source's version history into its copy (unified-access-control-r2 task 166,
//     owner round 45 item 1), and both the replay plan and the versions-truncated check are derived from the app-only
//     version listing. A listing cut at one page would lose the older history SILENTLY — never reported as truncated
//     (owner round 54 item 4, seed Sd). These tests pin the listing itself: every page is followed, through the URL the
//     server handed back, and a listing that does not end is never returned as if it were the whole history.
//
// Doubles: the Kiota IRequestAdapter — the Graph SDK's own abstraction boundary, which GraphServiceClient is built from
// (the SpeContainerPagingTests precedent). The real request-builder chain, the real OdataNextLink plumbing and the real
// loop run; no HTTP anywhere. No Mock<HttpMessageHandler>.

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Abstractions.Store;
using Moq;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.DocumentContainer;

public class DocumentVersionListPagingTests
{
    private const string Drive = "b!source-drive";
    private const string Item = "01ITEMWITHALONGHISTORY";
    private const string NextLink =
        "https://graph.microsoft.com/v1.0/drives/" + Drive + "/items/" + Item + "/versions?$skiptoken=PAGE";

    private static readonly DateTimeOffset Start = new(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Version <paramref name="number"/>.0, written <paramref name="number"/> days after <see cref="Start"/>.</summary>
    private static DriveItemVersion Version(int number) => new()
    {
        Id = $"{number}.0",
        LastModifiedDateTime = Start.AddDays(number),
        Size = 100 + number,
    };

    private static DriveItemVersionCollectionResponse Page(string? nextLink, params int[] numbers)
        => new() { Value = numbers.Select(Version).ToList(), OdataNextLink = nextLink };

    private static (DriveItemOperations Operations, ScriptedVersionAdapter Adapter) Over(params DriveItemVersionCollectionResponse[] pages)
    {
        var adapter = new ScriptedVersionAdapter(pages);
        var factory = new Mock<IGraphClientFactory>();
        factory.Setup(f => f.ForApp()).Returns(new GraphServiceClient(adapter));
        return (new DriveItemOperations(factory.Object, NullLogger<DriveItemOperations>.Instance), adapter);
    }

    [Fact]
    public async Task AHistoryOnTwoPages_IsListedWhole_NewestFirst()
    {
        var (operations, adapter) = Over(Page($"{NextLink}2", 5, 4, 3), Page(null, 2, 1));

        var versions = await operations.ListFileVersionsAsync(Drive, Item);

        versions!.Select(v => v.Id).Should().Equal(["5.0", "4.0", "3.0", "2.0", "1.0"],
            "the versions on the second page are part of the history a relocation replays");
        adapter.RequestedUrls.Should().HaveCount(2);
        adapter.RequestedUrls[1].Should().Contain("$skiptoken=PAGE2", "the follow-up request is the URL the server handed back");
    }

    [Fact]
    public async Task AHistoryOnThreePages_FollowsEveryLink()
    {
        var (operations, adapter) = Over(Page($"{NextLink}2", 6, 5), Page($"{NextLink}3", 4, 3), Page(null, 2, 1));

        var versions = await operations.ListFileVersionsAsync(Drive, Item);

        versions!.Should().HaveCount(6, "following one link is not the same as listing the history");
        adapter.RequestedUrls.Should().HaveCount(3);
    }

    [Fact]
    public async Task AHistoryOnOnePage_IsOneRequest()
    {
        var (operations, adapter) = Over(Page(null, 2, 1));

        (await operations.ListFileVersionsAsync(Drive, Item))!.Should().HaveCount(2);
        adapter.RequestedUrls.Should().ContainSingle();
    }

    [Fact]
    public async Task AListingThatDoesNotEnd_IsNeverReturnedAsTheWholeHistory()
    {
        // Every page names a next one: after the page bound the listing is reported as not fully enumerated (it throws;
        // the relocation that asked fails, reported) — never returned as a prefix that would replay a cut history.
        var pages = Enumerable.Range(0, DriveItemOperations.MaxVersionPages + 1)
            .Select(i => Page($"{NextLink}{i + 2}", i + 1))
            .ToArray();
        var (operations, adapter) = Over(pages);

        var listing = () => operations.ListFileVersionsAsync(Drive, Item);

        (await listing.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("could not be fully enumerated");
        adapter.RequestedUrls.Should().HaveCount(DriveItemOperations.MaxVersionPages);
    }

    /// <summary>
    /// Serves a scripted sequence of version pages and records every request's URL. Throws on any unmodelled call rather
    /// than answering a default (the SpeContainerPagingTests discipline).
    /// </summary>
    private sealed class ScriptedVersionAdapter : IRequestAdapter
    {
        private readonly DriveItemVersionCollectionResponse[] _pages;
        private int _index;

        public ScriptedVersionAdapter(DriveItemVersionCollectionResponse[] pages) => _pages = pages;

        public List<string> RequestedUrls { get; } = [];

        public ISerializationWriterFactory SerializationWriterFactory =>
            throw new NotSupportedException("Serialization is not exercised by these tests.");

        public string? BaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";

        public void EnableBackingStore(IBackingStoreFactory backingStoreFactory)
        {
        }

        public Task<ModelType?> SendAsync<ModelType>(
            RequestInformation requestInfo,
            ParsableFactory<ModelType> factory,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default,
            CancellationToken cancellationToken = default)
            where ModelType : IParsable
        {
            RequestedUrls.Add(requestInfo.URI.ToString());
            if (_index >= _pages.Length)
            {
                throw new InvalidOperationException(
                    $"Page {_index + 1} was requested but only {_pages.Length} were scripted — a test-design error.");
            }

            return Task.FromResult((ModelType?)(object)_pages[_index++]);
        }

        public Task SendNoContentAsync(
            RequestInformation requestInfo,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The version listing sends no request without content.");

        public Task<IEnumerable<ModelType>?> SendCollectionAsync<ModelType>(
            RequestInformation requestInfo,
            ParsableFactory<ModelType> factory,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default,
            CancellationToken cancellationToken = default)
            where ModelType : IParsable =>
            throw new NotSupportedException("SendCollectionAsync is not on the version-listing path.");

        public Task<ModelType?> SendPrimitiveAsync<ModelType>(
            RequestInformation requestInfo,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("SendPrimitiveAsync is not on the version-listing path.");

        public Task<IEnumerable<ModelType>?> SendPrimitiveCollectionAsync<ModelType>(
            RequestInformation requestInfo,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("SendPrimitiveCollectionAsync is not on the version-listing path.");

        public Task<NativeResponseType?> ConvertToNativeRequestAsync<NativeResponseType>(
            RequestInformation requestInfo,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("ConvertToNativeRequestAsync is not used by these tests.");
    }
}
