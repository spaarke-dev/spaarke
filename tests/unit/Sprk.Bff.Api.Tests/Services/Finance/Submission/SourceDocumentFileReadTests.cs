using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 118b review (F1-b): the optional per-file limit of <see cref="SourceDocumentFileRead.ReadAsync"/> (the intake passes
/// <see cref="InvoiceSubmissionService.MaxFileBytes"/> for a supporting document; the job passes none). The row's recorded
/// <c>sprk_filesize</c> is judged before anything is downloaded; the stream is judged as it is copied, so a row with no size (or a wrong
/// one) cannot fill memory.
/// </summary>
/// <remarks>Maintain-class (ADR-038): the file-size limit is a contract of the only door the app copies a document's bytes through.</remarks>
public sealed class SourceDocumentFileReadTests
{
    private const string Container = "drive-1";
    private const long Limit = 200_000; // more than two 80 KiB copy chunks, so the boundary is crossed mid-stream
    private static readonly Guid DocumentId = Guid.Parse("118b0000-0000-4000-8000-0000000000d1");

    /// <summary>A stream that delivers <paramref name="length"/> bytes and does not know its own size (not seekable, no <c>Length</c>).</summary>
    private sealed class UnsizedStream(long length) : Stream
    {
        public long Served { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, length - Served);
            Array.Fill(buffer, (byte)'x', offset, n);
            Served += n;
            return n;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var n = (int)Math.Min(buffer.Length, length - Served);
            buffer.Span[..n].Fill((byte)'x');
            Served += n;
            return ValueTask.FromResult(n);
        }
    }

    private sealed class World
    {
        public object? RecordedSize { get; init; }
        public required long StreamLength { get; init; }
        public UnsizedStream? Stream { get; private set; }
        public int Downloads { get; private set; }

        public async Task<SourceDocumentFile> ReadAsync(long? maxBytes)
        {
            var entities = new Mock<IGenericEntityService>();
            entities.Setup(e => e.RetrieveAsync("sprk_document", DocumentId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid id, string[] columns, CancellationToken _) =>
                {
                    columns.Should().Contain("sprk_filesize", "the recorded size is read in the same retrieve");
                    var row = new Entity("sprk_document", id)
                    {
                        ["sprk_graphdriveid"] = Container,
                        ["sprk_graphitemid"] = "item-1",
                        ["versionnumber"] = 7L,
                    };
                    if (RecordedSize is not null)
                    {
                        row["sprk_filesize"] = RecordedSize;
                    }

                    return Task.FromResult(row);
                });
            var spe = new Mock<ISpeFileOperations>(MockBehavior.Strict);
            spe.Setup(s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    Downloads++;
                    Stream = new UnsizedStream(StreamLength);
                    return Task.FromResult<Stream?>(Stream);
                });

            return await SourceDocumentFileRead.ReadAsync(
                entities.Object, spe.Object, TestRecordContainerResolver.ForBusinessUnitContainers(Container), DocumentId, CancellationToken.None, maxBytes);
        }
    }

    [Theory(DisplayName = "118b review F1-b (must fire): a row whose recorded sprk_filesize is over the limit is TooLarge BEFORE anything is downloaded (an int or a long column value)")]
    [InlineData(Limit + 1, false)]
    [InlineData(Limit + 1, true)]
    [InlineData(long.MaxValue, true)]
    public async Task RecordedSizeOverTheLimit_IsTooLarge_NothingDownloaded(long recorded, bool asLong)
    {
        var world = new World { RecordedSize = asLong ? (object)recorded : (int)recorded, StreamLength = Limit + 1 };

        var file = await world.ReadAsync(Limit);

        file.Outcome.Should().Be(SourceDocumentFileOutcome.TooLarge);
        file.Content.Should().BeNull();
        world.Downloads.Should().Be(0, "the row says it is too big: no download");
    }

    [Theory(DisplayName = "118b review F1-b (must fire): a stream with no size that delivers more than the limit is TooLarge and the copy STOPS (no recorded size, or a wrong one)")]
    [InlineData(null)]
    [InlineData(10)]
    [InlineData(0)]
    public async Task StreamOverTheLimit_IsTooLarge_AndTheCopyStops(object? recorded)
    {
        var world = new World { RecordedSize = recorded, StreamLength = 50L * 1024 * 1024 };

        var file = await world.ReadAsync(Limit);

        file.Outcome.Should().Be(SourceDocumentFileOutcome.TooLarge);
        file.Content.Should().BeNull();
        world.Downloads.Should().Be(1);
        world.Stream!.Served.Should().BeLessThan(Limit + 100_000, "the copy stops at the first chunk past the limit, it does not drain 50 MB");
    }

    [Fact(DisplayName = "118b review F1-b (must fire, the boundary): one byte over the limit is TooLarge")]
    public async Task OneByteOverTheLimit_IsTooLarge()
    {
        var world = new World { StreamLength = Limit + 1 };

        (await world.ReadAsync(Limit)).Outcome.Should().Be(SourceDocumentFileOutcome.TooLarge);
    }

    [Theory(DisplayName = "118b review F1-b (must not fire): a file of exactly the limit is read, whole, whether or not its row records the size")]
    [InlineData(null)]
    [InlineData((int)Limit)]
    public async Task ExactlyTheLimit_IsRead(object? recorded)
    {
        var world = new World { RecordedSize = recorded, StreamLength = Limit };

        var file = await world.ReadAsync(Limit);

        file.Outcome.Should().Be(SourceDocumentFileOutcome.Read);
        file.Content.Should().HaveCount((int)Limit).And.OnlyContain(b => b == (byte)'x');
        file.Version.Should().Be(7);
    }

    [Fact(DisplayName = "118b review F1-b (must not fire): no limit (the job's call) reads any file, whatever its row records - unchanged")]
    public async Task NoLimit_ReadsEverything()
    {
        var world = new World { RecordedSize = int.MaxValue, StreamLength = 3L * Limit };

        var file = await world.ReadAsync(maxBytes: null);

        file.Outcome.Should().Be(SourceDocumentFileOutcome.Read);
        file.Content.Should().HaveCount(3 * (int)Limit);
        world.Downloads.Should().Be(1);
    }
}
