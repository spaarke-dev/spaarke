using System.Collections.Concurrent;
using System.Data;
using System.Runtime.CompilerServices;
using System.ServiceModel;
using Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Storage;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Files;

namespace Sprk.Bff.Api.Tests.Finance.Files;

// ebilling-r1 task 090: unit-only fakes of the Finance file store's three seams (blob gateway, Dataverse, caller rights). They live
// under tests/integration/contract/** (compiled into Sprk.Bff.Api.Tests) per the project rule for unit-only helpers.

/// <summary>An in-memory <see cref="BlobGateway"/> with tags, ETags, Last-Modified, fault injection and a call log.</summary>
internal sealed class InMemoryBlobGateway : BlobGateway
{
    internal sealed class Blob
    {
        public required byte[] Content { get; set; }
        public string? ContentType { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
        public DateTimeOffset LastModified { get; set; }
        public ETag ETag { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Tags { get; set; } = new(StringComparer.Ordinal);
    }

    private readonly ConcurrentDictionary<(string Container, string Name), Blob> _blobs = new();
    private int _etag;

    /// <summary>The clock stamped on writes.</summary>
    public DateTimeOffset Now { get; set; } = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Every call, as "op container/name".</summary>
    public ConcurrentQueue<string> Calls { get; } = new();

    /// <summary>When set, every call throws it (a storage fault).</summary>
    public Exception? FailEverything { get; set; }

    /// <summary>When set, only calls of this op ("create", "props", "tags", "open", "list", "delete") throw <see cref="FailEverything"/>-like faults.</summary>
    public Dictionary<string, Exception> FailOp { get; } = new(StringComparer.Ordinal);

    /// <summary>Runs before every call ("op", "container/name"): lets a test change state between two calls (a concurrent writer).</summary>
    public Action<string, string>? BeforeOp { get; set; }

    /// <summary>Runs once, on the first read of a stream <see cref="OpenReadAsync"/> returned (the first byte served).</summary>
    public Action? OnFirstRead { get; set; }

    public int StorageCalls => Calls.Count;

    public IReadOnlyCollection<(string Container, string Name)> Names => _blobs.Keys.ToList();

    public Blob? Get(string container, string name) => _blobs.TryGetValue((container, name), out var b) ? b : null;

    public Blob Seed(string container, string name, byte[] content, IDictionary<string, string>? metadata = null,
        IDictionary<string, string>? tags = null, DateTimeOffset? lastModified = null)
    {
        var blob = new Blob
        {
            Content = content,
            CreatedOn = lastModified ?? Now,
            LastModified = lastModified ?? Now,
            ETag = NextETag(),
            Metadata = new Dictionary<string, string>(metadata ?? new Dictionary<string, string>(), StringComparer.Ordinal),
            Tags = new Dictionary<string, string>(tags ?? new Dictionary<string, string>(), StringComparer.Ordinal),
        };
        _blobs[(container, name)] = blob;
        return blob;
    }

    /// <summary>Overwrites the content out of band (a later write): new ETag and Last-Modified, tags kept (worst case).</summary>
    public void Overwrite(string container, string name, byte[] content, DateTimeOffset at)
    {
        var blob = _blobs[(container, name)];
        blob.Content = content;
        blob.LastModified = at;
        blob.ETag = NextETag();
    }

    public override Task UploadAsync(string container, string blobName, BinaryData content, string? contentType, CancellationToken cancellationToken)
    {
        Record("upload", container, blobName);
        Seed(container, blobName, content.ToArray()).ContentType = contentType;
        return Task.CompletedTask;
    }

    public override async Task<BlobCreateOutcome> CreateIfNotExistsAsync(
        string container, string blobName, Stream content, string? contentType, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken)
    {
        Record("create", container, blobName);
        if (_blobs.ContainsKey((container, blobName)))
        {
            return BlobCreateOutcome.AlreadyExists;
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var blob = Seed(container, blobName, buffer.ToArray(), metadata.ToDictionary(k => k.Key, v => v.Value));
        blob.ContentType = contentType;
        return BlobCreateOutcome.Created;
    }

    public override Task<(BinaryData Content, string? ContentType)?> DownloadContentAsync(string container, string blobName, CancellationToken cancellationToken)
    {
        Record("download", container, blobName);
        var blob = Get(container, blobName);
        return Task.FromResult<(BinaryData, string?)?>(blob is null ? null : (new BinaryData(blob.Content), blob.ContentType));
    }

    public override Task<Stream?> OpenReadAsync(string container, string blobName, ETag? ifMatch, CancellationToken cancellationToken)
    {
        Record("open", container, blobName);
        var blob = Get(container, blobName);
        if (blob is null)
        {
            return Task.FromResult<Stream?>(null);
        }

        if (ifMatch is { } etag && etag != blob.ETag)
        {
            throw new RequestFailedException(412, "The condition specified using HTTP conditional header(s) is not met.", "ConditionNotMet", null);
        }

        return Task.FromResult<Stream?>(OnFirstRead is { } onRead ? new FirstReadStream(blob.Content, onRead) : new MemoryStream(blob.Content, writable: false));
    }

    private sealed class FirstReadStream(byte[] content, Action onFirstRead) : MemoryStream(content, writable: false)
    {
        private bool _read;

        public override int Read(byte[] buffer, int offset, int count)
        {
            Notify();
            return base.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            Notify();
            return base.Read(buffer);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Notify();
            return base.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Notify();
            return base.ReadAsync(buffer, cancellationToken);
        }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            Notify();
            return base.CopyToAsync(destination, bufferSize, cancellationToken);
        }

        private void Notify()
        {
            if (!_read)
            {
                _read = true;
                onFirstRead();
            }
        }
    }

    public override Task<BlobPropertiesInfo?> GetPropertiesAsync(string container, string blobName, CancellationToken cancellationToken)
    {
        Record("props", container, blobName);
        var blob = Get(container, blobName);
        return Task.FromResult(blob is null
            ? null
            : new BlobPropertiesInfo(blob.Content.LongLength, blob.ContentType, blob.LastModified, blob.CreatedOn, blob.ETag,
                new Dictionary<string, string>(blob.Metadata, StringComparer.Ordinal)));
    }

    public override Task<IReadOnlyDictionary<string, string>?> GetTagsAsync(string container, string blobName, CancellationToken cancellationToken)
    {
        Record("tags", container, blobName);
        var blob = Get(container, blobName);
        return Task.FromResult<IReadOnlyDictionary<string, string>?>(blob is null ? null : new Dictionary<string, string>(blob.Tags, StringComparer.Ordinal));
    }

    public override async IAsyncEnumerable<BlobListingItem> ListAsync(
        string container, string prefix, bool includeMetadata, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Record("list", container, prefix);
        await Task.Yield();
        foreach (var ((c, name), blob) in _blobs.ToList())
        {
            if (c == container && name.StartsWith(prefix, StringComparison.Ordinal))
            {
                yield return new BlobListingItem(name, blob.Content.LongLength, blob.CreatedOn, blob.LastModified,
                    includeMetadata ? new Dictionary<string, string>(blob.Metadata, StringComparer.Ordinal) : new Dictionary<string, string>(),
                    blob.ETag);
            }
        }
    }

    public override Task<bool> DeleteIfExistsAsync(string container, string blobName, ETag? ifMatch, CancellationToken cancellationToken)
    {
        Record("delete", container, blobName);
        if (ifMatch is { } etag && Get(container, blobName) is { } blob && etag != blob.ETag)
        {
            throw new RequestFailedException(412, "The condition specified using HTTP conditional header(s) is not met.", "ConditionNotMet", null);
        }

        return Task.FromResult(_blobs.TryRemove((container, blobName), out _));
    }

    private void Record(string op, string container, string name)
    {
        Calls.Enqueue($"{op} {container}/{name}");
        BeforeOp?.Invoke(op, $"{container}/{name}");
        if (FailEverything is { } fault)
        {
            throw fault;
        }

        if (FailOp.TryGetValue(op, out var opFault))
        {
            throw opFault;
        }
    }

    private ETag NextETag() => new($"\"0x{Interlocked.Increment(ref _etag):X8}\"");
}

/// <summary>Caller rights as Dataverse would answer them, recorded per call; any fault answers None like the real gate.</summary>
internal sealed class RecordingRightsProbe : CallerRecordAccessProbe
{
    public RecordingRightsProbe()
        : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
    {
    }

    public Dictionary<(string Set, Guid Id), AccessRights> Rights { get; } = new();

    public ConcurrentQueue<(string Set, Guid Id, string? Token)> Calls { get; } = new();

    public void Grant(string set, Guid id, AccessRights rights) => Rights[(set, id)] = rights;

    public override Task<AccessRights> GetCallerRightsAsync(string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
    {
        Calls.Enqueue((entitySet, recordId, callerBearerToken));
        return Task.FromResult(callerBearerToken is not null && Rights.TryGetValue((entitySet, recordId), out var r) ? r : AccessRights.None);
    }
}

/// <summary>
/// An in-memory Dataverse for the store: <c>sprk_storedfile</c> rows with row versions, plus "other" rows (parents, submissions).
/// Backed by a Moq <see cref="IGenericEntityService"/>; every call is recorded.
/// </summary>
internal sealed class StoredFileDataverse
{
    private long _version = 1000;

    public StoredFileDataverse()
    {
        Mock.Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string[] _, CancellationToken _) => RetrieveAsync(table, id));
        Mock.Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns((Entity entity, CancellationToken _) => CreateAsync(entity));
        Mock.Setup(e => e.UpdateIfRowVersionMatchesAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string version, Dictionary<string, object> fields, CancellationToken _) => UpdateAsync(table, id, version, fields));
        Mock.Setup(e => e.DeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, CancellationToken _) => DeleteAsync(table, id));
        Mock.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression q, CancellationToken _) => RetrieveMultipleAsync(q));
    }

    public Mock<IGenericEntityService> Mock { get; } = new(MockBehavior.Strict);

    public IGenericEntityService Service => Mock.Object;

    public ConcurrentDictionary<Guid, Entity> Rows { get; } = new();

    /// <summary>Rows of other tables, keyed by (table, id).</summary>
    public ConcurrentDictionary<(string Table, Guid Id), Entity> Others { get; } = new();

    public ConcurrentQueue<string> Calls { get; } = new();

    public Exception? RetrieveFault { get; set; }
    public Exception? CreateFault { get; set; }
    public Exception? QueryFault { get; set; }

    /// <summary>Tables whose existence query faults.</summary>
    public HashSet<string> FaultingTables { get; } = new(StringComparer.Ordinal);

    /// <summary>When set, the next update sees a changed row version (a concurrent writer).</summary>
    public bool BumpVersionBeforeNextUpdate { get; set; }

    public int Writes => Calls.Count(c => c.StartsWith("create", StringComparison.Ordinal) || c.StartsWith("update", StringComparison.Ordinal) || c.StartsWith("delete", StringComparison.Ordinal));

    public void AddOther(string table, Guid id, params (string Column, object Value)[] values)
    {
        var e = new Entity(table, id);
        foreach (var (column, value) in values)
        {
            e[column] = value;
        }

        Others[(table, id)] = e;
    }

    public Entity AddRow(Entity row)
    {
        row["versionnumber"] = Interlocked.Increment(ref _version);
        row.RowVersion = ((long)row["versionnumber"]).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Rows[row.Id] = row;
        return row;
    }

    public static FaultException<OrganizationServiceFault> NotFound() =>
        new(new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040217), Message = "Does Not Exist" }, new FaultReason("Does Not Exist"));

    private Task<Entity> RetrieveAsync(string table, Guid id)
    {
        Calls.Enqueue($"retrieve {table} {id}");
        if (RetrieveFault is { } fault)
        {
            return Task.FromException<Entity>(fault);
        }

        if (table == StoredFileSchema.Entity && Rows.TryGetValue(id, out var row))
        {
            var copy = new Entity(row.LogicalName, row.Id) { RowVersion = row.RowVersion };
            foreach (var a in row.Attributes)
            {
                copy[a.Key] = a.Value;
            }

            return Task.FromResult(copy);
        }

        if (Others.TryGetValue((table, id), out var other))
        {
            return Task.FromResult(other);
        }

        return Task.FromException<Entity>(NotFound());
    }

    private Task<Guid> CreateAsync(Entity entity)
    {
        Calls.Enqueue($"create {entity.LogicalName} {entity.Id}");
        if (CreateFault is { } fault)
        {
            return Task.FromException<Guid>(fault);
        }

        AddRow(entity);
        return Task.FromResult(entity.Id);
    }

    private Task UpdateAsync(string table, Guid id, string version, Dictionary<string, object> fields)
    {
        Calls.Enqueue($"update {table} {id}");
        if (!Rows.TryGetValue(id, out var row))
        {
            return Task.FromException(NotFound());
        }

        if (BumpVersionBeforeNextUpdate)
        {
            BumpVersionBeforeNextUpdate = false;
            AddRow(row);
        }

        if (!string.Equals(row.RowVersion, version, StringComparison.Ordinal))
        {
            return Task.FromException(new DBConcurrencyException("ConcurrencyVersionMismatch"));
        }

        foreach (var (k, v) in fields)
        {
            row[k] = v;
        }

        AddRow(row);
        return Task.CompletedTask;
    }

    private Task DeleteAsync(string table, Guid id)
    {
        Calls.Enqueue($"delete {table} {id}");

        // Like DataverseServiceClientImpl.DeleteAsync: every fault arrives WRAPPED in InvalidOperationException.
        return table == StoredFileSchema.Entity && Rows.TryRemove(id, out _)
            ? Task.CompletedTask
            : Task.FromException(new InvalidOperationException($"Failed to delete {table} record {id}: Does Not Exist", NotFound()));
    }

    /// <summary>Parent ids a set query does not return (no privilege depth) while a read by id still finds them.</summary>
    public HashSet<Guid> HiddenFromQueries { get; } = [];

    /// <summary>Every page of the stored-file query, as (page number, paging cookie sent).</summary>
    public ConcurrentQueue<(int Page, string? Cookie)> PagesRequested { get; } = new();

    private Task<EntityCollection> RetrieveMultipleAsync(QueryExpression q)
    {
        Calls.Enqueue($"query {q.EntityName}");
        if (QueryFault is { } fault && q.EntityName == StoredFileSchema.Entity)
        {
            return Task.FromException<EntityCollection>(fault);
        }

        if (FaultingTables.Contains(q.EntityName))
        {
            return Task.FromException<EntityCollection>(new TimeoutException($"{q.EntityName} query timed out"));
        }

        if (q.EntityName == StoredFileSchema.Entity)
        {
            // Paged like Dataverse: PageInfo.Count rows per page, page N, a cookie the caller must send back for page N + 1.
            var size = q.PageInfo?.Count > 0 ? q.PageInfo.Count : 5000;
            var page = q.PageInfo?.PageNumber > 0 ? q.PageInfo.PageNumber : 1;
            PagesRequested.Enqueue((page, q.PageInfo?.PagingCookie));
            if (page > 1 && q.PageInfo?.PagingCookie != $"cookie-{page - 1}")
            {
                return Task.FromException<EntityCollection>(new InvalidOperationException($"page {page} requested without page {page - 1}'s cookie"));
            }

            var ordered = Rows.Values.OrderBy(r => r.Id).ToList();
            var slice = ordered.Skip((page - 1) * size).Take(size).ToList();
            return Task.FromResult(new EntityCollection(slice)
            {
                MoreRecords = ordered.Count > page * size,
                PagingCookie = $"cookie-{page}",
            });
        }

        // Existence query: "<table>id In (...)".
        var ids = q.Criteria.Conditions.SelectMany(c => c.Values).OfType<Guid>().ToHashSet();
        var found = Others.Where(kv => kv.Key.Table == q.EntityName && ids.Contains(kv.Key.Id) && !HiddenFromQueries.Contains(kv.Key.Id))
            .Select(kv => kv.Value).ToList();
        return Task.FromResult(new EntityCollection(found));
    }
}

/// <summary>Builds a <see cref="StoredFileService"/> over the fakes, with step 0 answering as <see cref="StepZero"/> says.</summary>
internal sealed class StoredFileRig
{
    public static readonly Guid Tenant = Guid.Parse("7a1e0000-0000-4000-8000-000000000090");
    public static readonly Guid TeamId = Guid.Parse("7ea70000-0000-4000-8000-000000000090");

    public StoredFileRig(
        bool? malwareScanningEnabled = true, bool configured = true, StepZeroCallerKind stepZero = StepZeroCallerKind.FlagFalse,
        InMemoryBlobGateway? sharedGateway = null, Guid? tenant = null, Func<CancellationToken, Task<Guid>>? organizationId = null)
    {
        Gateway = sharedGateway ?? new InMemoryBlobGateway();
        TenantId = tenant ?? Tenant;
        Blobs = new StoredFileBlobs(configured ? Gateway : null, configured ? TenantId : Guid.Empty, malwareScanningEnabled, Logger,
            timeProvider: new GatewayClock(Gateway), organizationId: organizationId);
        var services = new ServiceCollection();
        StepZero = StatedCaller.Register(services, stepZero);
        Provider = services.BuildServiceProvider();
        Owners.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .Callback((RecordOwnershipContext context, CancellationToken _) => OwnerContexts.Add(context))
            .Returns((RecordOwnershipContext context, CancellationToken token) =>
                Ownership is { } directory ? directory.Resolver().ResolveOwnerAsync(context, token) : Task.FromResult(OwnerAnswer));
        Service = new StoredFileService(Dataverse.Service, Blobs, Probe, Owners.Object, Provider, NullLogger<StoredFileService>.Instance);
    }

    public InMemoryBlobGateway Gateway { get; }

    /// <summary>This rig's tenant (<see cref="Tenant"/> unless another was given).</summary>
    public Guid TenantId { get; }
    public StoredFileDataverse Dataverse { get; } = new();
    public RecordingRightsProbe Probe { get; } = new();
    public Mock<IRecordOwnershipResolver> Owners { get; } = new(MockBehavior.Strict);
    public RecordOwnerResolution OwnerAnswer { get; set; } = RecordOwnerResolution.Owned(TeamId);

    /// <summary>When set, the owner is answered by the REAL resolver over this directory instead of <see cref="OwnerAnswer"/>.</summary>
    public Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory? Ownership { get; set; }

    /// <summary>Every ownership question the service asked, in order.</summary>
    public List<RecordOwnershipContext> OwnerContexts { get; } = [];
    public ILogger Logger { get; } = NullLogger.Instance;
    public StatedCaller StepZero { get; }
    public ServiceProvider Provider { get; }
    public StoredFileBlobs Blobs { get; }
    public StoredFileService Service { get; }

    /// <summary>A user principal whose oid is the step-0 test caller's.</summary>
    public static System.Security.Claims.ClaimsPrincipal Caller() =>
        new(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("oid", Sprk.Bff.Api.Tests.Api.Finance.FinanceAuthzTestAuthHandler.CallerObjectId)], "test"));

    /// <summary>Seeds a complete Servable (or other status) row and its blob, tagged as given; returns the row.</summary>
    public Entity SeedFile(
        Guid fileId, StoredFileParent parent, byte[] bytes, StoredFileStatus status = StoredFileStatus.Servable,
        StoredFilePurpose purpose = StoredFilePurpose.InvoicePdf, Guid? sourceDocument = null, IDictionary<string, string>? tags = null,
        string? blobPathOverride = null, string contentType = "application/pdf", string? environment = StoredFileBlobs.TestEnvironment)
    {
        var path = blobPathOverride ?? StoredFileBlobName.Build(TenantId, parent, fileId);
        var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        var metadata = new Dictionary<string, string> { ["sha256"] = sha };
        if (environment is not null)
        {
            // As StageAsync writes it: the staging deployment's environment marker.
            metadata["environment"] = environment;
        }

        Gateway.Seed(StoredFileSchema.ContainerOf(purpose), StoredFileBlobName.Build(TenantId, parent, fileId), bytes,
            metadata, tags ?? CleanTags(Gateway.Now.AddMinutes(1)));
        var row = new Entity(StoredFileSchema.Entity, fileId)
        {
            [StoredFileSchema.Purpose] = new OptionSetValue((int)purpose),
            [StoredFileSchema.Status] = new OptionSetValue((int)status),
            [parent.Table] = new EntityReference(parent.Table, parent.Id),
            [StoredFileSchema.Sha256] = sha,
            [StoredFileSchema.ContentType] = contentType,
            [StoredFileSchema.BlobPath] = path,
            [StoredFileSchema.CreatedOn] = Gateway.Now.UtcDateTime,
        };
        if (sourceDocument is { } doc)
        {
            row[StoredFileSchema.SourceDocument] = new EntityReference(StoredFileSchema.DocumentTable, doc);
        }

        return Dataverse.AddRow(row);
    }

    public static Dictionary<string, string> CleanTags(DateTimeOffset scannedAt) => new()
    {
        [StoredFileBlobs.ScanResultTagKey] = StoredFileBlobs.ScanCleanValue,
        [StoredFileBlobs.ScanTimeTagKey] = scannedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
    };
}

/// <summary>
/// The store's clock in the rig: one hour after the fake gateway's write clock, so a scan stamped a minute after a write is in the
/// past (never a future scan time) wherever a test moves <see cref="InMemoryBlobGateway.Now"/>.
/// </summary>
internal sealed class GatewayClock(InMemoryBlobGateway gateway) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => gateway.Now.AddHours(1);
}

/// <summary>A seekable stream that reports a chosen length and reads zeros (no allocation of that length).</summary>
internal sealed class FakeLengthStream(long length) : Stream
{
    public int Reads { get; private set; }
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position { get; set; }
    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count)
    {
        Reads++;
        var n = (int)Math.Min(count, Math.Max(0, Length - Position));
        Array.Clear(buffer, offset, n);
        Position += n;
        return n;
    }

    public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => Position + offset,
        _ => Length + offset,
    };

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
