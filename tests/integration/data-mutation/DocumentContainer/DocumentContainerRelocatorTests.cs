// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/DocumentContainer/**`
//   - Justification: the relocator is the ONE writer of a sprk_document's SharePoint Embedded pointer outside a path
//     that uploads the bytes itself (unified-access-control-r2 task 166 f1; owner round 21 item 1 (i)-(ii), round 26
//     item 3, round 37). Each test pins a write contract: what is stamped, in which order the copy / verify / re-point /
//     settle steps run, what a move re-keys, and what is NEVER written (a forged pointer is never copied; a source is
//     never deleted before its copy is verified, while a row still uses it, or — on a repeat call — unless it still
//     matches the WITNESS recorded when its copy was verified (owner round 45 item 4; never compared with the document's
//     current file); nothing is written once the relocation lock is lost (round 54 item 2)).
//
// Doubles are module boundaries only (ADR-038 §4): the REAL RecordContainerResolver over its substituted registry,
// entity service and item reader (TestRecordContainerResolver.DocumentPointerWorld — the relocator shares that entity
// service, as in production), SpeFileStore at its virtual facade methods (the codebase idiom), the
// IRelocatedFileIndexing facade (the PublicContracts boundary, ADR-013) as a recording fake, the ADR-004 lock as an
// in-memory fake OR the real IdempotencyService over an expiring in-memory cache, and the access source as a fake OR the
// real CachedAccessDataSource over an in-memory cache. No Mock<HttpMessageHandler>, no DI-registration assertion, no
// constructor null-check.

using Sprk.Bff.Api.Tests.TestInfrastructure;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Caching;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Documents;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;
using Resolver = Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver;
using World = TestRecordContainerResolver.DocumentPointerWorld;

namespace Sprk.Bff.Api.Tests.DataMutation.DocumentContainer;

public class DocumentContainerRelocatorTests
{
    internal const string Item = "01ITEMOFTHEDOCUMENT";
    internal const string ArchiveContainer = "b!archive-container";
    internal const string SecureContainer = "b!secure-matter-own-container";
    internal const string CustomerAContainer = "b!customer-a-container";
    internal const string CustomerA1Container = "b!customer-a-child-container";
    internal const string CustomerBContainer = "b!customer-b-container";
    internal const string Ledger = DocumentContainerRelocator.RelocationLedgerColumn;

    internal static readonly Guid Root = TestRecordContainerResolver.PointerWorldRootBusinessUnit;
    internal static readonly Guid CustomerA = Guid.Parse("a0000000-0000-4000-8000-00000000f1a0");
    internal static readonly Guid CustomerA1 = Guid.Parse("a1000000-0000-4000-8000-00000000f1a0");
    internal static readonly Guid CustomerB = Guid.Parse("b0000000-0000-4000-8000-00000000f1a0");
    internal static readonly Guid SecureMatter = Guid.Parse("1a000000-0000-4000-8000-00000000f1a0");
    internal static readonly Guid PlainMatter = Guid.Parse("1b000000-0000-4000-8000-00000000f1a0");
    internal static readonly Guid DocumentId = Guid.Parse("1d000000-0000-4000-8000-00000000f1a0");
    internal static readonly Guid OtherDocumentId = Guid.Parse("1e000000-0000-4000-8000-00000000f1a0");
    internal static readonly Guid OtherPerson = Guid.Parse("2a000000-0000-4000-8000-00000000f1a0");
    internal static readonly Guid OtherPersonObjectId = Guid.Parse("2b000000-0000-4000-8000-00000000f1a0");

    internal static readonly string Creator = TestRecordContainerResolver.PointerWorldCreatorObjectId.ToString("D");
    internal static readonly string BffApplication = TestRecordContainerResolver.PointerWorldBffApplicationId.ToString("D");

    internal static World Environment()
    {
        var world = new World { ArchiveContainerId = ArchiveContainer };
        world.BusinessUnits[CustomerA] = (Root, CustomerAContainer);
        world.BusinessUnits[CustomerA1] = (CustomerA, CustomerA1Container);
        world.BusinessUnits[CustomerB] = (Root, CustomerBContainer);
        world.SecureClaims[SecureContainer] = ("sprk_matter", SecureMatter);
        world.Rows[("sprk_matter", SecureMatter)] = new Entity("sprk_matter", SecureMatter)
        {
            ["sprk_issecure"] = true,
            ["sprk_containerid"] = SecureContainer,
            ["owningbusinessunit"] = new EntityReference("businessunit", CustomerA),
        };
        world.Rows[("sprk_matter", PlainMatter)] = new Entity("sprk_matter", PlainMatter)
        {
            ["sprk_issecure"] = false,
            ["owningbusinessunit"] = new EntityReference("businessunit", CustomerA1),
        };
        world.Rows[("systemuser", OtherPerson)] = new Entity("systemuser", OtherPerson)
        {
            ["azureactivedirectoryobjectid"] = OtherPersonObjectId,
        };
        return world;
    }

    /// <summary>A document of <paramref name="matter"/> (default: the plain matter, derived container A1).</summary>
    internal static Entity Doc(Guid? matter = null, string? drive = null, string? item = null, Guid? createdBy = null, Guid? id = null)
    {
        var row = World.Document(id ?? DocumentId, createdBy, CustomerA);
        row["sprk_matter"] = new EntityReference("sprk_matter", matter ?? PlainMatter);
        if (drive is not null)
        {
            row["sprk_graphdriveid"] = drive;
        }

        if (item is not null)
        {
            row["sprk_graphitemid"] = item;
        }

        return row;
    }

    /// <summary>An unfiled document owned in <paramref name="owner"/> that names the given file.</summary>
    internal static Entity UnfiledDoc(Guid id, Guid owner, string drive, string item)
    {
        var row = World.Document(id, owningBusinessUnit: owner);
        row["sprk_graphdriveid"] = drive;
        row["sprk_graphitemid"] = item;
        return row;
    }

    /// <summary>The <see cref="IRelocatedFileIndexing"/> facade at its boundary: records every call, answers as told.</summary>
    internal sealed class RecordingIndexing : IRelocatedFileIndexing
    {
        public List<RelocatedFileIndexRequest> Calls { get; } = new();

        public Func<RelocatedFileIndexRequest, RelocatedFileIndexOutcome> Answer { get; set; }
            = _ => RelocatedFileIndexOutcome.Done("indexed");

        public Task<RelocatedFileIndexOutcome> ReindexRelocatedFileAsync(
            RelocatedFileIndexRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add(request);
            return Task.FromResult(Answer(request));
        }
    }

    /// <summary>The relocator over the world (sharing its entity service), a recording SPE facade and indexing facade.</summary>
    /// <remarks>
    /// The SPE facade models what a relocation relies on (task 166 f1-v2, owner round 45 item 1): every item has a version
    /// list (<see cref="Versions"/>; an item not listed has ONE version "1.0" written by the document's creator); a prior
    /// version downloads as that version's bytes; an upload with <see cref="ConflictBehavior.Replace"/> to a name an
    /// upload already created writes a NEW VERSION of that same item (SharePoint's behaviour), any other upload creates an
    /// item; each upload appends a version written by the BFF identity, and <see cref="CopyVersionLimit"/> drops the
    /// oldest beyond the target container's version limit.
    /// </remarks>
    internal sealed class Rig
    {
        private (string Drive, string Item, string? Version)? _lastDownload;
        private readonly Dictionary<(string Drive, string Name), string> _names = new();
        private readonly Dictionary<(string Drive, string Item), int> _uploads = new();

        /// <summary>When an item's versions were written, unless a test says otherwise.</summary>
        public static readonly DateTimeOffset Written = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

        /// <summary>The display name Graph reports for <see cref="Creator"/>.</summary>
        public const string CreatorName = "Casey Creator";

        public Rig(World world, Func<SpeItemCreator, SpeItemCreator>? copyFacts = null)
        {
            World = world;
            var resolver = world.Build();
            Resolver = resolver;
            var gcf = Mock.Of<IGraphClientFactory>();
            Spe = new Mock<SpeFileStore>(MockBehavior.Loose,
                new ContainerOperations(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<ILogger<ContainerOperations>>()),
                new DriveItemOperations(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<ILogger<DriveItemOperations>>()),
                new UploadSessionManager(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>()),
                new UserOperations(gcf, Mock.Of<ILogger<UserOperations>>()),
                null!);
            Spe.Setup(s => s.GetItemCreatorAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string drive, string item, CancellationToken _) => world.ItemFacts(drive, item));
            Spe.Setup(s => s.ListFileVersionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string drive, string item, CancellationToken _) =>
                    world.ItemFacts(drive, item) is null ? null : (IReadOnlyList<VersionInfoDto>)VersionsOf(drive, item).ToList());
            Spe.Setup(s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(async (string drive, string item, CancellationToken _) =>
                {
                    Steps.Add($"download {drive}/{item}");
                    if (BeforeDownload is { } hook)
                    {
                        await hook(drive, item);
                    }

                    _lastDownload = (drive, item, null);
                    return (Stream?)new MemoryStream(new byte[world.ItemFacts(drive, item)?.Size ?? 1234]);
                });
            Spe.Setup(s => s.DownloadFileVersionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(async (string drive, string item, string version, CancellationToken _) =>
                {
                    Steps.Add($"download {drive}/{item}@{version}");
                    if (BeforeVersionDownload is { } hook)
                    {
                        await hook(drive, item, version);
                    }

                    if (VersionDownloadFails(drive, item, version))
                    {
                        return null;
                    }

                    _lastDownload = (drive, item, version);
                    var size = VersionsOf(drive, item).First(v => v.Id == version).Size;
                    return (Stream?)new MemoryStream(new byte[size]);
                });
            Spe.Setup(s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<ConflictBehavior>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string drive, string name, Stream content, ConflictBehavior conflict, CancellationToken _) =>
                {
                    Steps.Add($"upload {drive} ({conflict})");
                    var length = content.Length;
                    if (conflict != ConflictBehavior.Replace || !_names.TryGetValue((drive, name), out var copyId))
                    {
                        copyId = Copies.Count == 0 ? CopyItem : $"{CopyItem}-{Copies.Count + 1}";
                        Copies.Add((drive, copyId));
                        if (_names.ContainsKey((drive, name)))
                        {
                            name = $"{Path.GetFileNameWithoutExtension(name)} {Copies.Count}{Path.GetExtension(name)}"; // Rename
                        }

                        _names[(drive, name)] = copyId;
                    }

                    var (sourceDrive, sourceItem, sourceVersion) = _lastDownload ?? (drive, Item, null);
                    var source = world.ItemFacts(sourceDrive, sourceItem)!;
                    var written = sourceVersion is null ? source : source with { Size = length, QuickXorHash = $"hash-{sourceItem}@{sourceVersion}" };
                    // The copy is uploaded APP-ONLY by the BFF identity: no user, the BFF's application id.
                    world.Items[(drive, copyId)] = copyFacts?.Invoke(written)
                        ?? written with { UserObjectId = null, ApplicationId = BffApplication, WebUrl = $"https://contoso.sharepoint.com/{copyId}" };

                    // Every upload is a new version of the item it lands on, written by the BFF identity now.
                    _uploads[(drive, copyId)] = _uploads.GetValueOrDefault((drive, copyId)) + 1;
                    if (!Versions.TryGetValue((drive, copyId), out var history))
                    {
                        Versions[(drive, copyId)] = history = new List<VersionInfoDto>();
                    }

                    history.Add(new VersionInfoDto($"{_uploads[(drive, copyId)]}.0", null, DateTimeOffset.UtcNow, length, "SharePoint App")
                    {
                        LastModifiedByApplicationId = BffApplication,
                    });
                    while (CopyVersionLimit is { } limit && history.Count > limit)
                    {
                        history.RemoveAt(0); // the target's version limit drops the OLDEST
                    }

                    return new FileHandleDto(copyId, name, TargetRoot, length, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                        $"\"{{{copyId}}},{_uploads[(drive, copyId)]}\"", false, $"https://contoso.sharepoint.com/{copyId}", drive);
                });
            Spe.Setup(s => s.DeleteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string drive, string item, CancellationToken _) =>
                {
                    if (DeleteFails(drive, item))
                    {
                        Steps.Add($"delete FAILED {drive}/{item}");
                        return false;
                    }

                    // Records whether the row had already been re-pointed when the file was deleted.
                    Steps.Add($"delete {drive}/{item}{(world.Updates.Count > 0 ? " (after re-point)" : string.Empty)}");
                    world.Items[(drive, item)] = null; // gone: the item reader no longer finds it
                    return true;
                });
            Relocator = NewRelocator();
        }

        /// <summary>A relocator over this rig's world, SPE facade and indexing — with another lock store, access source or clock.</summary>
        public DocumentContainerRelocator NewRelocator(
            Sprk.Bff.Api.Services.Jobs.IIdempotencyService? locks = null, IAccessDataSource? access = null, TimeProvider? time = null)
            => new(Resolver, World.EntityService!, Spe.Object, Indexing, locks ?? Locks, access ?? Access,
                NullLogger<DocumentContainerRelocator>.Instance, time, Attribution);

        /// <summary>Who an app-only upload was made for (task 171 attach fix) — real, over an in-memory cache.</summary>
        public FaultableAttribution Attribution { get; } = new();

        /// <summary><see cref="UploadAttribution"/> whose READ can be made to fault (a Redis outage).</summary>
        internal sealed class FaultableAttribution : UploadAttribution
        {
            public FaultableAttribution()
                : base(new Sprk.Bff.Api.Infrastructure.Cache.TenantCache(
                    new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
                    NullLogger<Sprk.Bff.Api.Infrastructure.Cache.TenantCache>.Instance))
            {
            }

            public bool ReadFaults { get; set; }

            public override Task<MatchOutcome> MatchAsync(
                string tenantId, Guid caller, string drive, string item, CancellationToken ct = default)
                => ReadFaults
                    ? Task.FromException<MatchOutcome>(new TimeoutException("Redis unavailable"))
                    : base.MatchAsync(tenantId, caller, drive, item, ct);
        }

        /// <summary>Runs inside every CURRENT-content download, before its bytes are returned (a pause, a lock theft).</summary>
        public Func<string, string, Task>? BeforeDownload { get; set; }

        /// <summary>Runs inside every PRIOR-version download, before its bytes are returned.</summary>
        public Func<string, string, string, Task>? BeforeVersionDownload { get; set; }

        /// <summary>
        /// The ADR-004 processing lock, in memory — the lock store's contract: one holder per key; a holder taken with an
        /// owner is renewed and released only by that owner; a key added to <see cref="Held"/> directly is another
        /// (ownerless) relocation's.
        /// </summary>
        internal sealed class InMemoryLocks : Sprk.Bff.Api.Services.Jobs.IIdempotencyService
        {
            private readonly object _gate = new();
            private readonly Dictionary<string, string> _owners = new(StringComparer.Ordinal);

            public HashSet<string> Held { get; } = new(StringComparer.Ordinal);
            public List<string> Released { get; } = new();

            /// <summary>When set, taking a lock FAULTS (the lock store is unreachable).</summary>
            public Exception? Fault { get; set; }

            /// <summary>Renewals from this one on (1 = the confirmation right after the take) answer "no longer yours".</summary>
            public int? FailRenewalsFrom { get; set; }

            /// <summary>How many renewals were asked for.</summary>
            public int Renewals { get; private set; }

            /// <summary>Another relocation takes the key over (the lock expired under its holder and was taken again).</summary>
            public void Steal(string eventId, string thief)
            {
                lock (_gate)
                {
                    Held.Add(eventId);
                    _owners[eventId] = thief;
                }
            }

            public string? OwnerOf(string eventId)
            {
                lock (_gate)
                {
                    return _owners.GetValueOrDefault(eventId);
                }
            }

            public Task<bool> IsEventProcessedAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);

            public Task MarkEventAsProcessedAsync(string eventId, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public Task<bool> TryAcquireProcessingLockAsync(string eventId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default)
            {
                lock (_gate)
                {
                    return Fault is not null ? Task.FromException<bool>(Fault) : Task.FromResult(Held.Add(eventId));
                }
            }

            public Task<bool> TryAcquireProcessingLockAsync(
                string eventId, string ownerId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default)
            {
                lock (_gate)
                {
                    if (Fault is not null)
                    {
                        return Task.FromException<bool>(Fault);
                    }

                    if (!Held.Add(eventId))
                    {
                        return Task.FromResult(false);
                    }

                    _owners[eventId] = ownerId;
                    return Task.FromResult(true);
                }
            }

            public Task<bool> RenewProcessingLockAsync(
                string eventId, string ownerId, TimeSpan lockDuration, CancellationToken cancellationToken = default)
            {
                lock (_gate)
                {
                    Renewals++;
                    return Task.FromResult(
                        (FailRenewalsFrom is not { } from || Renewals < from)
                        && Held.Contains(eventId)
                        && _owners.TryGetValue(eventId, out var owner) && owner == ownerId);
                }
            }

            public Task ReleaseProcessingLockAsync(string eventId, CancellationToken cancellationToken = default)
            {
                lock (_gate)
                {
                    Held.Remove(eventId);
                    _owners.Remove(eventId);
                    Released.Add(eventId);
                    return Task.CompletedTask;
                }
            }

            public Task ReleaseProcessingLockAsync(string eventId, string ownerId, CancellationToken cancellationToken = default)
            {
                lock (_gate)
                {
                    if (_owners.TryGetValue(eventId, out var owner) && owner == ownerId)
                    {
                        Held.Remove(eventId);
                        _owners.Remove(eventId);
                        Released.Add(eventId);
                    }

                    return Task.CompletedTask;
                }
            }
        }

        public InMemoryLocks Locks { get; } = new();

        /// <summary>
        /// Dataverse's answer "may this person write the document?" (RetrievePrincipalAccess) at the IAccessDataSource
        /// boundary: by Entra object id; the document's creator may, anyone else may not unless a test says so.
        /// </summary>
        internal sealed class PrincipalAccess : IAccessDataSource
        {
            public Dictionary<string, AccessRights> Rights { get; } = new(StringComparer.OrdinalIgnoreCase)
            {
                [Creator] = AccessRights.Read | AccessRights.Write,
            };

            public List<(string User, string Resource)> Asked { get; } = new();

            public Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default)
            {
                Asked.Add((userId, resourceId));
                return Task.FromResult(new AccessSnapshot
                {
                    UserId = userId,
                    ResourceId = resourceId,
                    AccessRights = Rights.GetValueOrDefault(userId, AccessRights.None),
                });
            }

            public Task<AccessSnapshot> GetRecordAccessAsync(string userId, string entitySetName, Guid recordId, string? userAccessToken, CancellationToken ct = default)
                => throw new NotSupportedException("the relocator asks about the document only");
        }

        public PrincipalAccess Access { get; } = new();

        public const string CopyItem = "01COPYINTHETARGET";
        public const string TargetRoot = "01ROOTOFTHETARGETDRIVE";
        public World World { get; }
        public Resolver Resolver { get; }
        public Mock<SpeFileStore> Spe { get; }
        public RecordingIndexing Indexing { get; } = new();
        public DocumentContainerRelocator Relocator { get; }
        public List<string> Steps { get; } = new();
        public List<(string Drive, string Item)> Copies { get; } = new();
        public Func<string, string, bool> DeleteFails { get; set; } = (_, _) => false;

        /// <summary>Version lists by item (see the class remarks for the default).</summary>
        public Dictionary<(string Drive, string Item), List<VersionInfoDto>> Versions { get; } = new();

        /// <summary>The most versions a copy keeps (the target container's version limit); null = unlimited.</summary>
        public int? CopyVersionLimit { get; set; }

        /// <summary>A prior version's download returns nothing (Graph 404 for it).</summary>
        public Func<string, string, string, bool> VersionDownloadFails { get; set; } = (_, _, _) => false;

        /// <summary>The item's versions, oldest first.</summary>
        public IReadOnlyList<VersionInfoDto> VersionsOf(string drive, string item)
            => Versions.TryGetValue((drive, item), out var listed)
                ? listed
                : World.ItemFacts(drive, item) is { } facts
                    ? [new VersionInfoDto("1.0", null, Written, facts.Size ?? 0, CreatorName) { LastModifiedByUserId = Creator }]
                    : [];

        public string? LedgerOf(Guid id) => World.Rows[("sprk_document", id)].GetAttributeValue<string>(Ledger);
        public string? VersionRecordOf(Guid id) => World.Rows[("sprk_document", id)].GetAttributeValue<string>(RelocatedVersionHistory.Column);
        public string? ItemOf(Guid id) => World.Rows[("sprk_document", id)].GetAttributeValue<string>("sprk_graphitemid");
        public string? DriveOf(Guid id) => World.Rows[("sprk_document", id)].GetAttributeValue<string>("sprk_graphdriveid");

        /// <summary>What the version history route reports for the document's CURRENT file (round 45 item 1).</summary>
        public async Task<IReadOnlyList<VersionInfoDto>> ReportedHistoryAsync(Guid id)
        {
            var drive = DriveOf(id)!;
            var item = ItemOf(id)!;
            var listed = VersionsOf(drive, item).Reverse().ToList(); // Graph lists newest first
            return await RelocatedVersionHistory.WithOriginalAuthorshipAsync(
                World.EntityService!, id, item, listed, NullLogger.Instance, CancellationToken.None);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // (i) The pointer attach — the client's first file (owner round 21 item 1 (i))
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Attach_TheCreatorsOwnUpload_InTheDerivedContainer_IsStampedAppOnly_WithTheFileFlag()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc();
        world.Items[(CustomerA1Container, Item)] = new SpeItemCreator("brief.docx", Creator, null, 10, null, "https://contoso/brief.docx");
        var rig = new Rig(world);

        var result = await rig.Relocator.AttachFileAsync(DocumentId, Creator, CustomerA1Container, Item);

        result.Outcome.Should().Be(PointerAttachOutcome.Attached);
        result.AlreadyAttached.Should().BeFalse();
        var stamp = world.Updates.Should().ContainSingle().Subject;
        stamp.Entity.Should().Be("sprk_document");
        stamp.Id.Should().Be(DocumentId);
        stamp.Fields["sprk_graphdriveid"].Should().Be(CustomerA1Container);
        stamp.Fields["sprk_graphitemid"].Should().Be(Item);
        stamp.Fields[Spaarke.Dataverse.DocumentPointerBinding.BoundItemIdColumn].Should().Be(Item,
            "round 72 F4: the field-secured copy is written in the SAME update as the pointer, with the same value");
        stamp.Fields["sprk_hasfile"].Should().Be(true, "'has a file' and 'points at a file' are written together");
        stamp.Fields["sprk_filepath"].Should().Be("https://contoso/brief.docx", "the web URL is read from Graph, not taken from the client");
        stamp.Fields.Should().NotContainKey(Ledger, "a first attach owes nothing");
    }

    // ── Task 171 attach fix: an APP-ONLY upload is attached only for the person the BFF uploaded it for ──────────────

    private const string Tenant = "17150000-0000-4000-8000-0000000000a1";
    private static readonly string OtherUser = Guid.Parse("17150000-0000-4000-8000-0000000000b2").ToString("D");

    private static SpeItemCreator BffUpload(string name = "brief.docx")
        => new(name, null, TestRecordContainerResolver.PointerWorldBffApplicationId.ToString("D"), 10, null, "https://contoso/brief.docx");

    private static Task<PointerAttachResult> Attach(Rig rig, string caller = "", string? tenant = Tenant, string? drive = null)
        => rig.Relocator.AttachFileAsync(DocumentId, caller.Length == 0 ? Creator : caller, drive ?? CustomerA1Container, Item,
            callerTenantId: tenant);

    private static World AttachWorld(Rig? _ = null)
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc();
        world.Items[(CustomerA1Container, Item)] = BffUpload();
        return world;
    }

    [Fact(DisplayName = "Attach fix: a BFF-uploaded item whose ITEM binding names the caller is attached, and the binding is consumed")]
    public async Task Attach_ABffUploadedItem_WithTheCallersBinding_IsAttached_AndTheBindingConsumed()
    {
        var world = AttachWorld();
        var rig = new Rig(world);
        await rig.Attribution.RecordItemAsync(Tenant, Creator, CustomerA1Container, Item);

        var result = await Attach(rig);

        result.Outcome.Should().Be(PointerAttachOutcome.Attached, result.Detail);
        world.Updates.Should().ContainSingle().Which.Fields["sprk_graphitemid"].Should().Be(Item);
        (await rig.Attribution.MatchAsync(Tenant, Guid.Parse(Creator), CustomerA1Container, Item))
            .Should().Be(UploadAttribution.MatchOutcome.None, "a binding is consumed once its attach succeeded");
    }

    [Fact(DisplayName = "Attach fix: a BFF-uploaded item bound to ANOTHER user is refused — a Write holder never attaches someone else's file")]
    public async Task Attach_ABffUploadedItem_BoundToAnotherUser_IsRefused()
    {
        var world = AttachWorld();
        var rig = new Rig(world);
        await rig.Attribution.RecordItemAsync(Tenant, OtherUser, CustomerA1Container, Item);

        (await Attach(rig)).Outcome.Should().Be(PointerAttachOutcome.NotTheUploader);
        world.Updates.Should().BeEmpty();
    }

    [Fact(DisplayName = "Attach fix: a BFF-uploaded item with NO binding is refused — 'any item the BFF uploaded' would admit everyone's")]
    public async Task Attach_ABffUploadedItem_WithNoBinding_IsRefused()
    {
        var world = AttachWorld();
        var rig = new Rig(world);

        (await Attach(rig)).Outcome.Should().Be(PointerAttachOutcome.NotTheUploader);
        world.Updates.Should().BeEmpty();
    }

    [Fact(DisplayName = "Attach fix (K3): a binding recorded for the item in ANOTHER drive does not admit it")]
    public async Task Attach_ABindingForAnotherDrive_IsRefused()
    {
        var world = AttachWorld();
        var rig = new Rig(world);
        await rig.Attribution.RecordItemAsync(Tenant, Creator, CustomerBContainer, Item);

        (await Attach(rig)).Outcome.Should().Be(PointerAttachOutcome.NotTheUploader,
            "an item id is only meaningful in its drive — a binding for the same id elsewhere says nothing about this file");
        world.Updates.Should().BeEmpty();
    }

    [Fact(DisplayName = "Attach fix (K1): a binding is scoped to the caller's TENANT — the same caller in another tenant is refused, and no tenant refuses")]
    public async Task Attach_TheBindingIsTenantScoped()
    {
        var world = AttachWorld();
        var rig = new Rig(world);
        await rig.Attribution.RecordItemAsync(Tenant, Creator, CustomerA1Container, Item);

        (await Attach(rig, tenant: "another-tenant")).Outcome.Should().Be(PointerAttachOutcome.NotTheUploader);
        (await Attach(rig, tenant: null)).Outcome.Should().Be(PointerAttachOutcome.NotTheUploader);
        world.Updates.Should().BeEmpty();
    }

    [Fact(DisplayName = "Attach fix: a binding read that FAULTS refuses with a retryable outcome — never allows")]
    public async Task Attach_ABffUploadedItem_WhenTheBindingReadFaults_IsRefusedRetryably()
    {
        var world = AttachWorld();
        var rig = new Rig(world);
        await rig.Attribution.RecordItemAsync(Tenant, Creator, CustomerA1Container, Item);
        rig.Attribution.ReadFaults = true;

        (await Attach(rig)).Outcome.Should().Be(PointerAttachOutcome.UploaderUnverifiable);
        world.Updates.Should().BeEmpty();
    }

    // Verifier F1 (2026-10-07) — the attack WAS: a binding recorded for a session's PATH before any item existed, matched
    // later by whatever item stood at that path. The upload-session route records nothing now (pinned at the ROUTE by
    // RecordKeyedUploadRouteChildRecordTests.UploadSession_RecordsNothing); what the attach must guarantee is that the
    // ONLY thing that admits a BFF-uploaded item is an item binding naming the caller. These two pin that, for the two
    // states the old attack exploited.

    [Fact(DisplayName = "Verifier F1: after ANOTHER user's item binding was consumed by their attach, the caller cannot attach that item — nothing else admits it")]
    public async Task ABffItem_WhoseOnlyBindingWasAnotherUsersAndIsConsumed_IsRefused()
    {
        var world = AttachWorld();
        var rig = new Rig(world);
        await rig.Attribution.RecordItemAsync(Tenant, OtherUser, CustomerA1Container, Item);
        await rig.Attribution.ConsumeAsync(Tenant, Item); // B's successful attach

        (await Attach(rig)).Outcome.Should().Be(PointerAttachOutcome.NotTheUploader);
        world.Updates.Should().BeEmpty();
    }

    [Fact(DisplayName = "Verifier F1: an app-only item that NO upload bound to anyone (another BFF writer's, or one a session created) cannot be attached")]
    public async Task ABffItem_WithNoBindingAtAll_IsRefused()
    {
        var world = AttachWorld();
        var rig = new Rig(world);

        (await Attach(rig)).Outcome.Should().Be(PointerAttachOutcome.NotTheUploader,
            "the document's own creator, holding Write, still cannot attach an app-only file nobody's upload bound to them");
        world.Updates.Should().BeEmpty();
    }

    [Fact(DisplayName = "Attach fix: a file uploaded by ANOTHER PERSON is still refused (the user-uploaded branch is unchanged)")]
    public async Task Attach_AnotherPersonsUpload_IsStillRefused()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc();
        world.Items[(CustomerA1Container, Item)] = new SpeItemCreator("brief.docx", OtherPersonObjectId.ToString("D"), null, 10);
        var rig = new Rig(world);

        (await rig.Relocator.AttachFileAsync(DocumentId, Creator, CustomerA1Container, Item)).Outcome
            .Should().Be(PointerAttachOutcome.NotTheUploader);
    }

    [Fact]
    public async Task Attach_TheSameFileAgain_IsIdempotent_AndWritesNothing()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerA1Container, item: Item);
        var rig = new Rig(world);

        var result = await rig.Relocator.AttachFileAsync(DocumentId, Creator, CustomerA1Container, Item);

        result.Outcome.Should().Be(PointerAttachOutcome.Attached);
        result.AlreadyAttached.Should().BeTrue();
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Attach_ToARowThatAlreadyHasAnotherFile_IsRefused_ReattachingIsNeverAClientsJob()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerA1Container, item: "01THEORIGINALFILE");
        var rig = new Rig(world);

        var result = await rig.Relocator.AttachFileAsync(DocumentId, Creator, CustomerA1Container, Item);

        result.Outcome.Should().Be(PointerAttachOutcome.AlreadyAttached);
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Attach_ByAWriterWhoDidNotCreateTheRow_IsRefused()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc();
        var rig = new Rig(world);

        var result = await rig.Relocator.AttachFileAsync(DocumentId, OtherPersonObjectId.ToString("D"), CustomerA1Container, Item);

        result.Outcome.Should().Be(PointerAttachOutcome.NotTheCreator);
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Attach_ToARowTheBffCreated_WithNoRecordedPerson_IsRefused()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(createdBy: TestRecordContainerResolver.PointerWorldBffUser);
        var rig = new Rig(world);

        var result = await rig.Relocator.AttachFileAsync(DocumentId, Creator, CustomerA1Container, Item);

        result.Outcome.Should().Be(PointerAttachOutcome.NotTheCreator);
        world.Updates.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]    // the recorded person is the caller
    [InlineData(false)]   // the recorded person is someone else
    public async Task Attach_ToARowTheBffCreated_FollowsTheRecordedPerson_TheSameCreatorTheItemCheckReads(bool callerIsRecorded)
    {
        // createdby is the BFF, so the row's creator is sprk_createdbyperson (the round-23 definition, one reader).
        var world = Environment();
        var row = Doc(createdBy: TestRecordContainerResolver.PointerWorldBffUser);
        row[Resolver.CreatedByPersonColumn] =
            new EntityReference("systemuser", callerIsRecorded ? TestRecordContainerResolver.PointerWorldCreator : OtherPerson);
        world.Rows[("sprk_document", DocumentId)] = row;
        world.Items[(CustomerA1Container, Item)] = new SpeItemCreator("brief.docx", Creator, null, 10);
        var rig = new Rig(world);

        var result = await rig.Relocator.AttachFileAsync(DocumentId, Creator, CustomerA1Container, Item);

        if (callerIsRecorded)
        {
            result.Outcome.Should().Be(PointerAttachOutcome.Attached);
            world.Updates.Should().ContainSingle();
            (await rig.Resolver.IsDocumentPointerContainerAllowedAsync(DocumentId, CustomerA1Container, Item))
                .Should().BeTrue("the attached pointer is one the pointer check then honours");
        }
        else
        {
            result.Outcome.Should().Be(PointerAttachOutcome.NotTheCreator);
            world.Updates.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(CustomerAContainer)]   // the same customer, but not the document's derived container
    [InlineData(CustomerBContainer)]   // another customer's
    [InlineData(SecureContainer)]      // a secure record's
    public async Task Attach_AFileOutsideTheDerivedContainer_IsRefused(string drive)
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc();
        var rig = new Rig(world);

        var result = await rig.Relocator.AttachFileAsync(DocumentId, Creator, drive, Item);

        result.Outcome.Should().Be(PointerAttachOutcome.WrongContainer);
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Attach_AFileSomeoneElseUploaded_IsRefused()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc();
        world.Items[(CustomerA1Container, Item)] = new SpeItemCreator("payroll.xlsx", OtherPersonObjectId.ToString("D"), null);
        var rig = new Rig(world);

        var result = await rig.Relocator.AttachFileAsync(DocumentId, Creator, CustomerA1Container, Item);

        result.Outcome.Should().Be(PointerAttachOutcome.NotTheUploader);
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Attach_AnItemThatIsNotInTheDrive_IsRefused()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc();
        world.Items[(CustomerA1Container, Item)] = null;
        var rig = new Rig(world);

        var result = await rig.Relocator.AttachFileAsync(DocumentId, Creator, CustomerA1Container, Item);

        result.Outcome.Should().Be(PointerAttachOutcome.NotTheUploader);
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Attach_WhenTheDocumentsContainerCannotBeDerived_IsRefused()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: Guid.NewGuid()); // an unreadable matter
        var rig = new Rig(world);

        var result = await rig.Relocator.AttachFileAsync(DocumentId, Creator, CustomerA1Container, Item);

        result.Outcome.Should().Be(PointerAttachOutcome.ContainerUndetermined);
        world.Updates.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, Item)]
    [InlineData(CustomerA1Container, " ")]
    public async Task Attach_WithoutAFile_IsAnInvalidRequest(string? drive, string item)
    {
        var rig = new Rig(Environment());

        var result = await rig.Relocator.AttachFileAsync(DocumentId, Creator, drive, item);

        result.Outcome.Should().Be(PointerAttachOutcome.InvalidRequest);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // (ii) Relocation (owner round 26 item 3)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Relocate_AFileAlreadyInItsDerivedContainer_IsInPlace_AndNothingIsTouched()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerA1Container, item: Item);
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.InPlace);
        rig.Steps.Should().BeEmpty();
        world.Updates.Should().BeEmpty();
        rig.Indexing.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Relocate_ReportOnly_PlansTheMove_AndReadsOrWritesNoBytes()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: false);

        outcome.State.Should().Be(RelocationState.WouldRelocate);
        outcome.TargetDrive.Should().Be(CustomerA1Container);
        rig.Steps.Should().BeEmpty("a dry run downloads, uploads and deletes nothing");
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Relocate_CopiesThenVerifiesThenRepoints_AndOnlyThenDeletesTheSource()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Relocated);
        outcome.TargetItem.Should().Be(Rig.CopyItem);
        outcome.Pending.Should().BeEmpty();
        var repoint = world.Updates.First();
        repoint.Fields["sprk_graphdriveid"].Should().Be(CustomerA1Container);
        repoint.Fields["sprk_graphitemid"].Should().Be(Rig.CopyItem);
        repoint.Fields[Spaarke.Dataverse.DocumentPointerBinding.BoundItemIdColumn].Should().Be(Rig.CopyItem,
            "round 72 F4: the re-point binds the copy it points at, in the same update");
        repoint.Fields[Ledger].Should().BeOfType<string>().Which.Should().Contain(Item,
            "the re-point records the old item in the row's ledger in the SAME update");
        rig.Steps.Should().Equal(
            $"download {CustomerBContainer}/{Item}",
            $"upload {CustomerA1Container} (Rename)",
            $"delete {CustomerBContainer}/{Item} (after re-point)");
        rig.ItemOf(DocumentId).Should().Be(Rig.CopyItem, "by the time the source is deleted, the row already names the verified copy");
        rig.LedgerOf(DocumentId).Should().BeNull("everything the move owed is settled, so the ledger is cleared");
    }

    [Fact]
    public async Task Relocate_WhenTheCopyDoesNotVerify_RemovesTheCopy_AndLeavesTheRowAndTheSource()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = new Rig(world, copyFacts: source => source with { Size = source.Size - 1 });

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Failed);
        world.Updates.Should().BeEmpty("nothing points at a copy that did not verify");
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerA1Container, Rig.CopyItem, It.IsAny<CancellationToken>()), Times.Once);
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerBContainer, Item, It.IsAny<CancellationToken>()), Times.Never,
            "the source is never deleted before its copy is verified");
    }

    [Fact]
    public async Task Relocate_WhenTheHashDiffers_TheCopyDoesNotVerify()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = new Rig(world, copyFacts: source => source with { QuickXorHash = "another-hash" });

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Failed);
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Relocate_WhenTheRepointFails_RemovesTheCopy_AndKeepsTheSource()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        world.UpdateFault = new HttpRequestException("Dataverse unavailable");
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Failed);
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerA1Container, Rig.CopyItem, It.IsAny<CancellationToken>()), Times.Once);
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerBContainer, Item, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Relocate_WhenARowOfAnotherRecordStillUsesTheSource_KeepsIt_ForThatRecord_AndIsComplete()
    {
        // Round 37 item 2: a row OUTSIDE the document's container keeps the source; it is that record's file now.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        world.Rows[("sprk_document", OtherDocumentId)] = UnfiledDoc(OtherDocumentId, CustomerB, CustomerBContainer, Item);
        var rig = new Rig(world);

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], CustomerA1Container, RelocationPurpose.LegacyMigration, apply: true);

        var outcome = result.Outcomes.Should().ContainSingle().Subject;
        outcome.State.Should().Be(RelocationState.RelocatedSourceKeptForOtherRecords);
        result.Complete.Should().BeTrue("a source kept for another record is never incomplete");
        result.SourceKeptForOtherRecords.Should().ContainSingle().Which.KeptFor.Should().ContainSingle($"sprk_document:{OtherDocumentId}");
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerBContainer, Item, It.IsAny<CancellationToken>()), Times.Never,
            "deleting it would break the other record's document");
        rig.Indexing.Calls.Should().ContainSingle().Which.OldItemRemoved.Should().BeFalse(
            "the old item stays the other record's file, so only THIS document's chunks of it are removed");
    }

    [Fact]
    public async Task Relocate_AFileThatIsNotVerifiablyTheRowsOwn_IsNeverCopied()
    {
        // A forged pointer to another person's file: the migration reports it for an administrator, and never copies
        // it into the document's own container (that would serve someone else's file through this row).
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        world.Items[(CustomerBContainer, Item)] = new SpeItemCreator("payroll.xlsx", OtherPersonObjectId.ToString("D"), null, 10);
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.SourceUnverified);
        rig.Steps.Should().BeEmpty();
        world.Updates.Should().BeEmpty();
    }

    [Theory(DisplayName = "Round 74 V1: a row whose item id DIFFERS from its field-secured copy is never moved — nothing copied, re-pointed or deleted")]
    [InlineData(RelocationPurpose.LegacyMigration)]
    [InlineData(RelocationPurpose.MakeSecure)]
    public async Task Relocate_ARowWhoseItemDiffersFromItsBoundCopy_IsNotMoved_AndNothingIsDeleted(RelocationPurpose purpose)
    {
        // A move writes a MATCHING copy for whatever item it moves, so moving a forged pointer would launder it — and the
        // settle would then delete the item it was forged to. Make Secure skips the uploader test, which made it the path.
        var world = Environment();
        var row = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        row[Spaarke.Dataverse.DocumentPointerBinding.BoundItemIdColumn] = "01THEITEMTHEBFFBOUND";
        world.Rows[("sprk_document", DocumentId)] = row;
        world.Items[(CustomerAContainer, Item)] = new SpeItemCreator("memo.docx", OtherPersonObjectId.ToString("D"), null, 10, "h");
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true, purpose);

        outcome.State.Should().Be(RelocationState.SourceUnverified);
        world.Updates.Should().BeEmpty("nothing is re-pointed — and so nothing is re-bound");
        rig.Steps.Should().BeEmpty("nothing is downloaded, uploaded or deleted");
    }

    [Fact(DisplayName = "Round 74 V1: once the backfill is complete, a row with NO copy is never moved")]
    public async Task Relocate_ARowWithNoCopy_AfterTheBackfill_IsNotMoved()
    {
        var world = Environment();
        world.ItemIdBoundBackfillComplete = true;
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.SourceUnverified);
        world.Updates.Should().BeEmpty();
        rig.Steps.Should().BeEmpty();
    }

    [Fact]
    public async Task Relocate_MakeSecure_MovesTheFileTheDocumentShows_IntoTheSecureContainer_WhateverItsUploader()
    {
        // Round 26 item 3: a record just made secure moves its files into its own container. The move only NARROWS who
        // can reach the bytes, so it is not gated on the uploader (a colleague's upload on the record moves too).
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        world.Items[(CustomerAContainer, Item)] = new SpeItemCreator("memo.docx", OtherPersonObjectId.ToString("D"), null, 10, "h");
        var rig = new Rig(world);

        var legacy = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: false);
        var makeSecure = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true, RelocationPurpose.MakeSecure);

        legacy.State.Should().Be(RelocationState.SourceUnverified, "the migration never copies another person's file");
        makeSecure.State.Should().Be(RelocationState.Relocated);
        world.Updates.First().Fields["sprk_graphdriveid"].Should().Be(SecureContainer);
    }

    /// <summary>
    /// Batch-4 integration: word-add-in-r1's archive bug. <c>ArchiveOutboundAttachmentsAsync</c> wrote each attachment's
    /// sprk_document ID into <c>sprk_graphitemid</c> (and the archive container into <c>sprk_graphdriveid</c>). Such a row
    /// names no file: it is reported (FileMissing, with the reason), never read from Graph as an item (a Graph read of a
    /// non-item id answers 400 and would fault the batch), never copied, re-pointed or deleted — and it does not stop
    /// the real document beside it in the same batch from moving.
    /// </summary>
    [Theory]
    [InlineData("D")]
    [InlineData("B")]
    public async Task Relocate_MakeSecure_AnArchiveRowWhoseItemIsADocumentId_IsReported_NeverMovedOrDeleted(string format)
    {
        var archived = OtherDocumentId.ToString(format);
        var real = Guid.Parse("1f000000-0000-4000-8000-00000000f1a0");
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: ArchiveContainer, item: archived);
        world.Rows[("sprk_document", real)] = Doc(id: real, matter: SecureMatter, drive: CustomerAContainer, item: Item);
        world.Items[(CustomerAContainer, Item)] = new SpeItemCreator("memo.docx", Creator, null, 10, "h");
        var rig = new Rig(world);
        // A Graph read of a GUID as an item id is a 400, not a 404: had the relocator asked, the batch would fault.
        rig.Spe.Setup(s => s.GetItemCreatorAsync(ArchiveContainer, archived, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Graph 400: invalid item id"));

        var batch = await rig.Relocator.RelocateDocumentsAsync(
            [DocumentId, real], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        var archivedRow = batch.Outcomes.Single(o => o.DocumentId == DocumentId);
        archivedRow.State.Should().Be(RelocationState.FileMissing);
        archivedRow.Detail.Should().Be(DocumentContainerRelocator.UnresolvableArchiveItemDetail);
        archivedRow.SourceItem.Should().Be(archived, "the report names the row's pointer as it is");
        batch.Incomplete.Should().Contain(o => o.DocumentId == DocumentId, "it is reported, not silently settled");
        rig.Spe.Verify(s => s.GetItemCreatorAsync(ArchiveContainer, archived, It.IsAny<CancellationToken>()), Times.Never(),
            "a document id is never asked of Graph as an item");
        rig.Steps.Should().NotContain(step => step.Contains(archived), "nothing of it is downloaded, uploaded or deleted");
        world.Updates.Should().NotContain(u => u.Id == DocumentId, "the archive row is never re-pointed");

        batch.Outcomes.Single(o => o.DocumentId == real).State.Should().Be(RelocationState.Relocated,
            "the real document beside it moves as usual");
    }

    [Fact]
    public async Task Relocate_MakeSecure_DoesNotWidenToANonSecureTarget()
    {
        // The MakeSecure exemption applies only when the derived container IS the secure one.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        world.Items[(CustomerBContainer, Item)] = new SpeItemCreator("memo.docx", OtherPersonObjectId.ToString("D"), null, 10);
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true, RelocationPurpose.MakeSecure);

        outcome.State.Should().Be(RelocationState.SourceUnverified);
        rig.Steps.Should().BeEmpty();
    }

    [Fact]
    public async Task Relocate_ToARequestedTargetThatIsNotTheDerivedContainer_MovesNothing()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(
            DocumentId, apply: true, RelocationPurpose.MakeSecure, expectedTargetContainer: SecureContainer);

        outcome.State.Should().Be(RelocationState.Undecidable, "the relocator never puts a file where the strict rule would refuse it");
        rig.Steps.Should().BeEmpty();
    }

    [Fact]
    public async Task Relocate_AFileLargerThanASingleRequestCopy_IsReported_NotHalfCopied()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        world.Items[(CustomerBContainer, Item)] = new SpeItemCreator("video.mp4", Creator, null, DocumentContainerRelocator.MaxRelocatableBytes + 1);
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Failed);
        rig.Steps.Should().BeEmpty();
    }

    [Fact]
    public async Task Relocate_ARowWithNoFile_AndAnUndecidableRow_AndAMissingItem_AreEachReported()
    {
        var noFile = Environment();
        noFile.Rows[("sprk_document", DocumentId)] = Doc();
        var undecidable = Environment();
        undecidable.Rows[("sprk_document", DocumentId)] = Doc(matter: Guid.NewGuid(), drive: CustomerBContainer, item: Item);
        var missing = Environment();
        missing.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        missing.Items[(CustomerBContainer, Item)] = null;

        (await new Rig(noFile).Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true)).State.Should().Be(RelocationState.NoFile);
        (await new Rig(undecidable).Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true)).State.Should().Be(RelocationState.Undecidable);
        (await new Rig(missing).Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true)).State.Should().Be(RelocationState.FileMissing);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // F1 / owner round 37 item 3 — a relocated file is SERVED under the interim rule (the default until the strict flip)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ARelocatedFile_IsServedUnderTheInterimRule_BeforeTheStrictFlip()
    {
        // Probe P1 of the f1 verification, on the REAL resolver and relocator: the creator's own upload sits in a
        // container of the owner's customer (served by the interim rule), but not in the document's derived container.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerAContainer, item: Item);
        var rig = new Rig(world);
        rig.Resolver.StrictDerivedContainerMode.Should().BeFalse("the interim rule is the default until gate 25");
        (await rig.Resolver.IsDocumentPointerContainerAllowedAsync(DocumentId, CustomerAContainer, Item))
            .Should().BeTrue("precondition: the interim rule serves the file before the move");

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Relocated);
        world.ItemFacts(CustomerA1Container, Rig.CopyItem)!.UserObjectId.Should().BeNull("the copy is uploaded app-only by the BFF identity");
        (await rig.Resolver.IsDocumentPointerContainerAllowedAsync(DocumentId, CustomerA1Container, Rig.CopyItem))
            .Should().BeTrue("round 37 item 3: a BFF-placed file in the document's derived container is served by the interim rule");
    }

    [Fact]
    public async Task AMakeSecureRelocation_IsServedUnderTheInterimRule()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true, RelocationPurpose.MakeSecure, expectedTargetContainer: SecureContainer);

        outcome.State.Should().Be(RelocationState.Relocated);
        (await rig.Resolver.IsDocumentPointerContainerAllowedAsync(DocumentId, SecureContainer, Rig.CopyItem))
            .Should().BeTrue("every Make Secure move must stay downloadable while the interim rule is in force");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // F2 / owner round 37 item 2 — re-entry settles what a move left owing; a source kept for another record is complete
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MakeSecure_WhenTheSourceDeleteFails_IsIncomplete_AndARepeatCallDeletesIt()
    {
        // Probe P3: the first source delete fails. The row is already re-pointed, so the repeat call reads it as in place —
        // and must still delete the secure file's source from the shared container.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        var rig = new Rig(world) { DeleteFails = (drive, item) => drive == CustomerAContainer && item == Item };

        var first = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        first.Complete.Should().BeFalse("the secure record's bytes are still in the shared container");
        var pending = first.Incomplete.Should().ContainSingle().Subject;
        pending.State.Should().Be(RelocationState.RelocationPending);
        pending.Pending.Should().Contain(p => p.StartsWith("source-pending", StringComparison.Ordinal));
        rig.LedgerOf(DocumentId).Should().Contain(Item, "the row records the source it still owes");
        rig.ItemOf(DocumentId).Should().Be(Rig.CopyItem);

        rig.DeleteFails = (_, _) => false;
        var second = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        second.Complete.Should().BeTrue();
        second.Outcomes.Should().ContainSingle().Which.State.Should().Be(RelocationState.InPlace);
        rig.Steps.Should().Contain($"delete {CustomerAContainer}/{Item} (after re-point)", "the repeat call deleted the source");
        world.ItemFacts(CustomerAContainer, Item).Should().BeNull();
        rig.LedgerOf(DocumentId).Should().BeNull("settled: the ledger is cleared");
        rig.Copies.Should().ContainSingle("the repeat call never copies again");
    }

    [Fact]
    public async Task MakeSecure_ASourceKeptForAnotherRecord_IsComplete_AndARepeatCallDeletesItOnceThatRowIsGone()
    {
        // Probe P2: the source was kept because another record's row used it; that row is then gone.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        world.Rows[("sprk_document", OtherDocumentId)] = UnfiledDoc(OtherDocumentId, CustomerB, CustomerAContainer, Item);
        var rig = new Rig(world);

        var first = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        first.Complete.Should().BeTrue("round 37: a source kept for a row OUTSIDE the secure record's subtree is that record's file");
        first.Outcomes.Should().ContainSingle().Which.State.Should().Be(RelocationState.RelocatedSourceKeptForOtherRecords);
        first.SourceKeptForOtherRecords.Should().ContainSingle().Which.KeptFor.Should().Contain($"sprk_document:{OtherDocumentId}");
        world.ItemFacts(CustomerAContainer, Item).Should().NotBeNull("never deleted while a row uses it");
        rig.LedgerOf(DocumentId).Should().NotBeNull("the kept source stays recorded so a later pass can delete it");

        world.Rows.Remove(("sprk_document", OtherDocumentId));
        var second = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        second.Complete.Should().BeTrue();
        world.ItemFacts(CustomerAContainer, Item).Should().BeNull("no row uses the source any more, so the repeat call deleted it");
        rig.LedgerOf(DocumentId).Should().BeNull();
        rig.Indexing.Calls.Select(c => c.OldItemRemoved).Should().Equal(false, true);
    }

    [Fact]
    public async Task ASourceKeptForAnotherRecord_IsNeverReportedIncomplete_OnARepeatCall()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        world.Rows[("sprk_document", OtherDocumentId)] = UnfiledDoc(OtherDocumentId, CustomerB, CustomerAContainer, Item);
        var rig = new Rig(world);
        await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);
        var writesAfterFirst = world.Updates.Count;

        var again = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        again.Complete.Should().BeTrue("round 37: it never loops on, and never reports incomplete for, a source kept for another record");
        again.Outcomes.Should().ContainSingle().Which.State.Should().Be(RelocationState.InPlace);
        again.SourceKeptForOtherRecords.Should().ContainSingle();
        world.Updates.Count.Should().Be(writesAfterFirst, "nothing changed, so nothing is rewritten");
        rig.Copies.Should().ContainSingle();
    }

    [Fact]
    public async Task MakeSecure_ARowInTheSecureSubtreeNamingTheSameFile_IsMovedAlong_WithItsOwnCopy()
    {
        // Round 37 item 2: every referencing row INSIDE the secure record's subtree moves to the secure container. Each
        // gets its own copy — sprk_graphitemid_uk is a unique key on the item id, so two rows can never share one.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        world.Rows[("sprk_document", OtherDocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item, id: OtherDocumentId);
        var rig = new Rig(world);

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        result.Complete.Should().BeTrue();
        var outcome = result.Outcomes.Should().ContainSingle().Subject;
        outcome.State.Should().Be(RelocationState.Relocated);
        outcome.MovedAlong.Should().ContainSingle().Which.DocumentId.Should().Be(OtherDocumentId);
        rig.DriveOf(OtherDocumentId).Should().Be(SecureContainer);
        rig.ItemOf(OtherDocumentId).Should().NotBe(rig.ItemOf(DocumentId), "one item per row");
        rig.Copies.Should().HaveCount(2);
        world.ItemFacts(CustomerAContainer, Item).Should().BeNull("once both rows left it, nothing uses the source");
        rig.LedgerOf(DocumentId).Should().BeNull();
        rig.LedgerOf(OtherDocumentId).Should().BeNull();
    }

    [Fact(DisplayName = "Round 74 K1: a row named for move-along whose item DIFFERS from its field-secured copy is not moved along, and the source is not deleted on its account")]
    public async Task MakeSecure_AMoveAlongRowWithAMismatchedCopy_IsNotRepointed()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        var other = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item, id: OtherDocumentId);
        other[Spaarke.Dataverse.DocumentPointerBinding.BoundItemIdColumn] = "01THEITEMTHEBFFBOUNDTOTHEOTHERROW";
        world.Rows[("sprk_document", OtherDocumentId)] = other;
        var rig = new Rig(world);

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        var outcome = result.Outcomes.Should().ContainSingle().Subject;
        outcome.MovedAlong.Should().ContainSingle().Which.State.Should().Be(RelocationState.SourceUnverified);
        rig.ItemOf(OtherDocumentId).Should().Be(Item, "a forged pointer is never moved along — and so never re-bound");
        rig.DriveOf(OtherDocumentId).Should().Be(CustomerAContainer);
        rig.Copies.Should().ContainSingle("only the document's own file was copied");
        world.ItemFacts(CustomerAContainer, Item).Should().NotBeNull("a row still names the source, so it is not deleted");
    }

    [Fact]
    public async Task ALedgerEntryWithoutAWitness_NeverDeletesOrCopiesItsSource()
    {
        // The ledger is the only record of a source on a repeat call, and since round 45 item 4 its WITNESS is what a
        // source is deleted against. An entry without one (only a hand-written ledger lacks it — the column is BFF-written
        // and field-secured from creation) names a file the relocator never verified: never deleted, never copied.
        var world = Environment();
        var row = Doc(matter: SecureMatter, drive: SecureContainer, item: Rig.CopyItem);
        row[Ledger] = LedgerJson(CustomerAContainer, "01SOMEONEELSESFILE");
        world.Rows[("sprk_document", DocumentId)] = row;
        world.Items[(SecureContainer, Rig.CopyItem)] = new SpeItemCreator("memo.docx", null, BffApplication, 1234, "hash-1234");
        world.Items[(CustomerAContainer, "01SOMEONEELSESFILE")] = new SpeItemCreator("payroll.xlsx", OtherPersonObjectId.ToString("D"), null, 1234, "another-hash");
        var rig = new Rig(world);

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        result.Complete.Should().BeFalse();
        result.Incomplete.Should().ContainSingle().Which.Pending.Should().Contain(p => p.Contains("source-unverifiable", StringComparison.Ordinal));
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerAContainer, "01SOMEONEELSESFILE", It.IsAny<CancellationToken>()), Times.Never);
        rig.Copies.Should().BeEmpty("an unverified source is never copied into the document either");
        result.SourceChangedAfterMove.Should().BeEmpty();
    }

    [Fact]
    public async Task AWitnessWithoutAHash_IsMatchedByTheSourcesVersion_AndTheSourceIsDeleted()
    {
        // Graph returned no quickXorHash for the source: the witness's version id decides (SharePoint mints a new version
        // for every content change). Unchanged version, same size → the source is the one that was copied → deleted.
        var world = Environment();
        var row = Doc(matter: SecureMatter, drive: SecureContainer, item: Rig.CopyItem);
        row[Ledger] = LedgerJson(CustomerAContainer, Item, witnessSize: 1234, witnessHash: null, witnessVersion: "1.0");
        world.Rows[("sprk_document", DocumentId)] = row;
        world.Items[(SecureContainer, Rig.CopyItem)] = new SpeItemCreator("memo.docx", null, BffApplication, 1234, null);
        world.Items[(CustomerAContainer, Item)] = new SpeItemCreator("memo.docx", Creator, null, 1234, null);
        var rig = new Rig(world);

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        result.Complete.Should().BeTrue("the source still is the version the witness recorded");
        world.ItemFacts(CustomerAContainer, Item).Should().BeNull();
        rig.Copies.Should().BeEmpty();
    }

    [Fact]
    public void TheWitnessRule_SizeAndHash_ElseSizeAndVersion()
    {
        var witness = new DocumentContainerRelocator.RelocationWitness(10, "h", "2.0");
        var same = new SpeItemCreator("f", null, null, 10, "h");

        DocumentContainerRelocator.Compare(witness, same, null).Should().Be(DocumentContainerRelocator.WitnessComparison.Matches);
        DocumentContainerRelocator.Compare(witness, same with { Size = 11 }, null).Should().Be(DocumentContainerRelocator.WitnessComparison.Changed);
        DocumentContainerRelocator.Compare(witness, same with { QuickXorHash = "x" }, null).Should().Be(DocumentContainerRelocator.WitnessComparison.Changed);
        DocumentContainerRelocator.Compare(witness, same with { QuickXorHash = null }, "2.0").Should().Be(DocumentContainerRelocator.WitnessComparison.Matches,
            "without a hash on one side the version decides");
        DocumentContainerRelocator.Compare(witness, same with { QuickXorHash = null }, "3.0").Should().Be(DocumentContainerRelocator.WitnessComparison.Changed);
        DocumentContainerRelocator.Compare(witness, same with { QuickXorHash = null }, null).Should().Be(DocumentContainerRelocator.WitnessComparison.Unknown,
            "a version list that could not be read decides nothing — retried, never deleted");
        DocumentContainerRelocator.Compare(null, same, "2.0").Should().Be(DocumentContainerRelocator.WitnessComparison.NoWitness);
        DocumentContainerRelocator.Compare(new DocumentContainerRelocator.RelocationWitness(10, null, null), same, "2.0")
            .Should().Be(DocumentContainerRelocator.WitnessComparison.NoWitness, "a witness without a hash or a version proves nothing");
        DocumentContainerRelocator.Compare(witness, null, "2.0").Should().Be(DocumentContainerRelocator.WitnessComparison.Changed);
    }

    [Fact]
    public async Task AnUnreadableLedger_IsNeverActedOn_AndTheFileIsNotMoved()
    {
        var world = Environment();
        var row = Doc(drive: CustomerBContainer, item: Item);
        row[Ledger] = "{ this is not a relocation ledger";
        world.Rows[("sprk_document", DocumentId)] = row;
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Failed, "moving would overwrite a ledger nobody can read");
        outcome.Pending.Should().Contain(p => p.StartsWith("ledger-unreadable", StringComparison.Ordinal));
        rig.Steps.Should().BeEmpty();
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutTheLedgerColumn_NothingIsMoved_FailClosed()
    {
        // The schema gate (Set-DocumentRelocationSchema.ps1) comes before any relocation: without a place to record what a
        // move owes, no file is moved.
        var world = new World { ArchiveContainerId = ArchiveContainer, RelocationLedgerColumnMissing = true };
        world.BusinessUnits[CustomerA] = (Root, CustomerAContainer);
        world.BusinessUnits[CustomerA1] = (CustomerA, CustomerA1Container);
        world.BusinessUnits[CustomerB] = (Root, CustomerBContainer);
        world.Rows[("sprk_matter", PlainMatter)] = Environment().Rows[("sprk_matter", PlainMatter)];
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = new Rig(world);

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], CustomerA1Container, RelocationPurpose.LegacyMigration, apply: true);

        result.Complete.Should().BeFalse();
        result.Outcomes.Should().ContainSingle().Which.State.Should().Be(RelocationState.Failed);
        rig.Steps.Should().BeEmpty();
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task ARelocationOfADocumentAnotherRelocationHolds_MovesNothing_AndIsIncomplete()
    {
        // One writer per document: a double-clicked Make Secure (or Make Secure meeting the migration pass) must not copy
        // and re-point twice — the loser's copy and ledger entry would be lost.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        var rig = new Rig(world);
        rig.Locks.Held.Add(DocumentContainerRelocator.RelocationLockKey(DocumentId));

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        result.Complete.Should().BeFalse();
        result.Outcomes.Should().ContainSingle().Which.State.Should().Be(RelocationState.Failed);
        rig.Steps.Should().BeEmpty();
        world.Updates.Should().BeEmpty();
        rig.Locks.Released.Should().BeEmpty("a lock this call did not take is never released by it");
    }

    [Fact]
    public async Task TheRelocationLock_IsReleased_EvenWhenTheMoveFails()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        world.UpdateFault = new HttpRequestException("Dataverse unavailable");
        var rig = new Rig(world);

        await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        rig.Locks.Held.Should().BeEmpty();
        rig.Locks.Released.Should().Equal(DocumentContainerRelocator.RelocationLockKey(DocumentId));
    }

    [Fact]
    public async Task ARowThatCannotBeMovedAlong_BecauseAnotherRelocationHoldsIt_KeepsTheSourcePending()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        world.Rows[("sprk_document", OtherDocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item, id: OtherDocumentId);
        var rig = new Rig(world);
        rig.Locks.Held.Add(DocumentContainerRelocator.RelocationLockKey(OtherDocumentId));

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        result.Complete.Should().BeFalse("a row of the secure subtree still names the source in the shared container");
        world.ItemFacts(CustomerAContainer, Item).Should().NotBeNull("never deleted while a row uses it");
        rig.ItemOf(OtherDocumentId).Should().Be(Item);
    }

    [Fact]
    public async Task ReportOnly_WithALedgerStillOwing_ReportsItPending_AndWritesNothing()
    {
        var world = Environment();
        var row = Doc(matter: SecureMatter, drive: SecureContainer, item: Rig.CopyItem);
        row[Ledger] = LedgerJson(CustomerAContainer, Item);
        world.Rows[("sprk_document", DocumentId)] = row;
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: false);

        outcome.State.Should().Be(RelocationState.RelocationPending);
        outcome.Pending.Should().Contain(p => p.StartsWith("source-pending", StringComparison.Ordinal));
        world.Updates.Should().BeEmpty();
        rig.Steps.Should().BeEmpty();
        rig.Indexing.Calls.Should().BeEmpty();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // F4 / owner round 37 item 1 — every reference to the old item is re-keyed, and the index follows the move
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Relocate_ReKeysTheRowsOwnColumnsThatHeldTheOldIds_InTheSameUpdateAsThePointer()
    {
        var world = Environment();
        var row = Doc(drive: CustomerBContainer, item: Item);
        row["sprk_driveitemid"] = Item;
        row["spk_fileviewerid"] = Item;
        row["sprk_containerid"] = CustomerBContainer;
        row["sprk_parentfolderid"] = "01OLDFOLDERINB";
        row["sprk_etag"] = "\"{OLD},3\"";
        world.Rows[("sprk_document", DocumentId)] = row;
        var rig = new Rig(world);

        await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        var repoint = world.Updates.First().Fields;
        repoint["sprk_graphitemid"].Should().Be(Rig.CopyItem);
        repoint["sprk_driveitemid"].Should().Be(Rig.CopyItem);
        repoint["spk_fileviewerid"].Should().Be(Rig.CopyItem);
        repoint["sprk_containerid"].Should().Be(CustomerA1Container);
        repoint["sprk_parentfolderid"].Should().Be(Rig.TargetRoot);
        repoint["sprk_etag"].Should().Be($"\"{{{Rig.CopyItem}}},1\"");
    }

    [Fact]
    public async Task Relocate_LeavesAnOwnColumnThatHeldSomethingElse()
    {
        var world = Environment();
        var row = Doc(drive: CustomerBContainer, item: Item);
        row["sprk_driveitemid"] = "01ANUNRELATEDITEM";
        row["sprk_containerid"] = "b!an-unrelated-container";
        world.Rows[("sprk_document", DocumentId)] = row;
        var rig = new Rig(world);

        await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        world.Updates.First().Fields.Should().NotContainKeys("sprk_driveitemid", "sprk_containerid");
    }

    [Fact]
    public async Task Relocate_ReKeysAChildAttachmentsParentItem_AndTheCommunicationAttachmentRow()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var child = Guid.Parse("3c000000-0000-4000-8000-00000000f1a0");
        var strangersChild = Guid.Parse("3d000000-0000-4000-8000-00000000f1a0");
        var linkedAttachment = Guid.Parse("3e000000-0000-4000-8000-00000000f1a0");
        world.Rows[("sprk_document", child)] = new Entity("sprk_document", child)
        {
            ["sprk_parentdocument"] = new EntityReference("sprk_document", DocumentId),
            ["sprk_parentgraphitemid"] = Item,
        };
        world.Rows[("sprk_document", strangersChild)] = new Entity("sprk_document", strangersChild)
        {
            ["sprk_parentdocument"] = new EntityReference("sprk_document", Guid.NewGuid()),
            ["sprk_parentgraphitemid"] = Item,
        };
        world.Rows[("sprk_communicationattachment", linkedAttachment)] = new Entity("sprk_communicationattachment", linkedAttachment)
        {
            ["sprk_document"] = new EntityReference("sprk_document", DocumentId),
            ["sprk_graphdriveid"] = CustomerBContainer,
            ["sprk_graphitemid"] = Item,
        };
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Relocated);
        world.Rows[("sprk_document", child)].GetAttributeValue<string>("sprk_parentgraphitemid").Should().Be(Rig.CopyItem);
        world.Rows[("sprk_document", strangersChild)].GetAttributeValue<string>("sprk_parentgraphitemid").Should().Be(Item,
            "another parent's child is not this document's reference");
        var attachment = world.Rows[("sprk_communicationattachment", linkedAttachment)];
        attachment.GetAttributeValue<string>("sprk_graphdriveid").Should().Be(CustomerA1Container);
        attachment.GetAttributeValue<string>("sprk_graphitemid").Should().Be(Rig.CopyItem);
        world.ItemFacts(CustomerBContainer, Item).Should().BeNull("the linked attachment row moved with the document, so nothing uses the source");
    }

    // (f1-v1's ACommunicationsOwnAttachmentRecord_KeepsTheSource_ForThatCommunication pinned F-B — every unlinked attachment
    // row kept the source whatever its communication's subtree. Replaced by the round-45 item 3 tests below.)

    [Fact]
    public async Task Relocate_WhenAReKeyFails_IsPending_AndARepeatCallCompletesIt()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var linkedAttachment = Guid.Parse("3e000000-0000-4000-8000-00000000f1a0");
        world.Rows[("sprk_communicationattachment", linkedAttachment)] = new Entity("sprk_communicationattachment", linkedAttachment)
        {
            ["sprk_document"] = new EntityReference("sprk_document", DocumentId),
            ["sprk_graphdriveid"] = CustomerBContainer,
            ["sprk_graphitemid"] = Item,
        };
        world.FaultQueriesOf = "sprk_communicationattachment";
        var rig = new Rig(world);

        var first = await rig.Relocator.RelocateDocumentsAsync([DocumentId], CustomerA1Container, RelocationPurpose.LegacyMigration, apply: true);

        first.Complete.Should().BeFalse();
        first.Incomplete.Should().ContainSingle().Which.Pending.Should().Contain(p => p.StartsWith("rekey-pending", StringComparison.Ordinal));
        world.ItemFacts(CustomerBContainer, Item).Should().NotBeNull("an unknown reference keeps the source");

        world.FaultQueriesOf = null;
        var second = await rig.Relocator.RelocateDocumentsAsync([DocumentId], CustomerA1Container, RelocationPurpose.LegacyMigration, apply: true);

        second.Complete.Should().BeTrue();
        world.Rows[("sprk_communicationattachment", linkedAttachment)].GetAttributeValue<string>("sprk_graphitemid").Should().Be(Rig.CopyItem);
        world.ItemFacts(CustomerBContainer, Item).Should().BeNull();
        rig.LedgerOf(DocumentId).Should().BeNull();
    }

    [Fact]
    public async Task Relocate_ReindexesTheNewItem_AndRemovesAllTheOldItemsChunks_OnceTheSourceIsGone()
    {
        var world = Environment();
        var row = Doc(drive: CustomerBContainer, item: Item);
        row["sprk_searchindexname"] = "spaarke-files-matter-index";
        world.Rows[("sprk_document", DocumentId)] = row;
        var rig = new Rig(world);

        await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        var call = rig.Indexing.Calls.Should().ContainSingle().Subject;
        call.DocumentId.Should().Be(DocumentId);
        call.DriveId.Should().Be(CustomerA1Container);
        call.ItemId.Should().Be(Rig.CopyItem);
        call.OldItemId.Should().Be(Item);
        call.OldItemRemoved.Should().BeTrue();
        call.PreviousSearchIndexName.Should().Be("spaarke-files-matter-index", "the old chunks are where the document was last indexed");
    }

    [Fact]
    public async Task Relocate_WhenTheIndexStepFails_IsIndexPending_Incomplete_AndARepeatCallRetries()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        var rig = new Rig(world);
        rig.Indexing.Answer = _ => RelocatedFileIndexOutcome.Pending("the re-index of the new item could not be enqueued (ServiceBusException)");

        var first = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        first.Complete.Should().BeFalse("round 37 item 1: an indexing failure counts as incomplete");
        first.Incomplete.Should().ContainSingle().Which.Pending.Should().Contain(p => p.StartsWith("index-pending", StringComparison.Ordinal));
        rig.ItemOf(DocumentId).Should().Be(Rig.CopyItem, "an indexing failure does not undo the move");

        rig.Indexing.Answer = _ => RelocatedFileIndexOutcome.Done("indexed");
        var second = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        second.Complete.Should().BeTrue();
        rig.Indexing.Calls.Should().HaveCount(2);
        rig.LedgerOf(DocumentId).Should().BeNull();
    }

    [Theory]
    [InlineData(RelocationState.Relocated, true)]
    [InlineData(RelocationState.RelocatedSourceKeptForOtherRecords, true)]
    [InlineData(RelocationState.InPlace, true)]
    [InlineData(RelocationState.NoFile, true)]
    [InlineData(RelocationState.RelocationPending, false)]
    [InlineData(RelocationState.WouldRelocate, false)]
    [InlineData(RelocationState.SourceUnverified, false)]
    [InlineData(RelocationState.Undecidable, false)]
    [InlineData(RelocationState.FileMissing, false)]
    [InlineData(RelocationState.Failed, false)]
    public void ABatchIsComplete_OnlyWhenEveryFileIsSettled(RelocationState state, bool settled)
    {
        DocumentRelocationBatchResult.IsSettledState(state).Should().Be(settled);
        DocumentRelocationBatchResult.IsSettled(DocumentRelocationOutcome.Of(DocumentId, state, null, null, null, null, "x")
            with { Pending = ["index-pending: x"] }).Should().BeFalse("a row that still owes anything is never settled");
    }

    [Fact]
    public void CopyVerification_ComparesSizeAndHash()
    {
        var source = new SpeItemCreator("f", null, null, 10, "h");

        DocumentContainerRelocator.CopyMismatch(source, source).Should().BeNull();
        DocumentContainerRelocator.CopyMismatch(source, source with { Size = 11 }).Should().NotBeNull();
        DocumentContainerRelocator.CopyMismatch(source, source with { QuickXorHash = "x" }).Should().NotBeNull();
        DocumentContainerRelocator.CopyMismatch(source, source with { QuickXorHash = null }).Should().BeNull(
            "a hash Graph has not computed yet is not a mismatch; the size still is checked");
        DocumentContainerRelocator.CopyMismatch(source with { Size = null }, source).Should().NotBeNull(
            "an unknown source size cannot verify anything");
        DocumentContainerRelocator.CopyMismatch(source, null).Should().NotBeNull();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // F-A / owner round 45 item 4 — a repeat call deletes the source by the WITNESS recorded at verification, and a
    // source edited after the move is re-copied by the relocator itself (never a manual step)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ARepeatCall_DeletesTheSourceByItsWitness_ThoughTheDocumentWasEditedSinceTheMove()
    {
        // Probe Q1 of the f1-v1 verification: the first source delete fails, the user then edits the RELOCATED document
        // (a new size and hash), and the repeat call must still delete the secure file's source from the shared
        // container — it is compared with what was copied, not with the document's current file.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        var rig = new Rig(world) { DeleteFails = (drive, item) => drive == CustomerAContainer && item == Item };

        var first = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);
        first.Complete.Should().BeFalse("the source delete failed");
        rig.LedgerOf(DocumentId).Should().Contain("\"witness\"", "the ledger records the source's witness when the copy is verified");

        world.Items[(SecureContainer, Rig.CopyItem)] = world.ItemFacts(SecureContainer, Rig.CopyItem)! with { Size = 5555, QuickXorHash = "edited-after-the-move" };
        rig.DeleteFails = (_, _) => false;
        var second = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);
        var third = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        second.Complete.Should().BeTrue("the source still matches its witness, whatever the document became since");
        world.ItemFacts(CustomerAContainer, Item).Should().BeNull("the secure file's source is gone from the shared container");
        rig.LedgerOf(DocumentId).Should().BeNull();
        third.Complete.Should().BeTrue();
        rig.Copies.Should().ContainSingle("nothing was edited at the source, so nothing is re-copied");
        second.SourceChangedAfterMove.Should().BeEmpty();
    }

    [Fact]
    public async Task ASourceKeptForAnotherRecord_ThenReleased_IsDeletedByItsWitness_ThoughTheDocumentWasEdited()
    {
        // F-A case (2): a source kept for another row, that row later gone, the document edited meanwhile.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        world.Rows[("sprk_document", OtherDocumentId)] = UnfiledDoc(OtherDocumentId, CustomerB, CustomerAContainer, Item);
        var rig = new Rig(world);
        (await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true))
            .Complete.Should().BeTrue();

        world.Items[(SecureContainer, Rig.CopyItem)] = world.ItemFacts(SecureContainer, Rig.CopyItem)! with { Size = 7777, QuickXorHash = "edited" };
        world.Rows.Remove(("sprk_document", OtherDocumentId));
        var again = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        again.Complete.Should().BeTrue();
        world.ItemFacts(CustomerAContainer, Item).Should().BeNull();
        rig.LedgerOf(DocumentId).Should().BeNull();
    }

    /// <summary>A Make Secure move whose source delete fails, then a save to the OLD file by <paramref name="editor"/>.</summary>
    private static async Task<(World World, Rig Rig, DateTimeOffset EditedAt)> MovedThenEditedAtTheOldLocationAsync(string editor)
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        var rig = new Rig(world) { DeleteFails = (drive, item) => drive == CustomerAContainer && item == Item };
        (await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true))
            .Complete.Should().BeFalse();

        // Someone saves to the old file after the move (a still-open session): a new version of the source.
        var editedAt = new DateTimeOffset(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);
        world.Items[(CustomerAContainer, Item)] = world.ItemFacts(CustomerAContainer, Item)! with { Size = 2000, QuickXorHash = "source-edited" };
        rig.Versions[(CustomerAContainer, Item)] =
        [
            new VersionInfoDto("1.0", null, Rig.Written, 1234, Rig.CreatorName) { LastModifiedByUserId = Creator },
            new VersionInfoDto("2.0", null, editedAt, 2000, "Late Editor") { LastModifiedByUserId = editor },
        ];
        rig.DeleteFails = (_, _) => false;
        return (world, rig, editedAt);
    }

    [Fact]
    public async Task ASourceEditedAfterTheMove_IsReportedWithTheRowId_AndReCopiedWithItsEdits_ThenDeleted()
    {
        // Round 45 item 4: the source no longer matches its witness — reported source-changed-after-move with the row id,
        // never deleted as it stands, and closed by the relocator's own re-entry: re-copy (the document's file and history,
        // then the source's edits), verify, re-point. Nothing is lost and nobody acts by hand. The editor may write the
        // document, so the edit becomes its current content.
        var (world, rig, editedAt) = await MovedThenEditedAtTheOldLocationAsync(OtherPersonObjectId.ToString("D"));
        rig.Access.Rights[OtherPersonObjectId.ToString("D")] = AccessRights.Read | AccessRights.Write;

        var second = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        second.Complete.Should().BeTrue("the re-copy closed it on this call");
        var changed = second.SourceChangedAfterMove.Should().ContainSingle(c => c.NewItem != null).Subject;
        changed.DocumentId.Should().Be(DocumentId, "reported with the row id");
        changed.SourceItem.Should().Be(Item);
        changed.CarriedVersions.Should().Be(1, "exactly the version written after the move");
        changed.EditIsCurrent.Should().BeTrue();
        rig.ItemOf(DocumentId).Should().Be(changed.NewItem).And.NotBe(Rig.CopyItem, "re-pointed to the re-copy");
        rig.DriveOf(DocumentId).Should().Be(SecureContainer);
        world.ItemFacts(CustomerAContainer, Item).Should().BeNull("re-copied, so the edited source is deleted against its new witness");
        world.ItemFacts(SecureContainer, Rig.CopyItem).Should().BeNull("the replaced copy is deleted too — its history is in the re-copy");
        rig.LedgerOf(DocumentId).Should().BeNull();
        world.ItemFacts(SecureContainer, changed.NewItem!)!.Size.Should().Be(2000, "the document now carries the edit");
        rig.Access.Asked.Should().Contain((OtherPersonObjectId.ToString("D"), DocumentId.ToString("D")),
            "the editor's right is Dataverse's answer for that person on this document");

        var history = await rig.ReportedHistoryAsync(DocumentId);
        history.Select(v => (v.LastModifiedBy, v.LastModifiedDateTime)).Should().Equal(
            [("Late Editor", editedAt), (Rig.CreatorName, Rig.Written)],
            "newest first: the edit made after the move, then the original — each with its ORIGINAL author and date");
    }

    [Fact]
    public async Task AnEditAtTheOldLocation_ByAPersonWhoMayNotWriteTheDocument_IsKeptInItsHistory_ButNeverBecomesCurrent()
    {
        // The old file stayed writable by the old (shared) container's audience. A person who lost access with the move
        // must not change the secure document through it: the edit is carried (nothing is lost; its author is recorded),
        // but the document's own content stays current — fail closed.
        var (world, rig, editedAt) = await MovedThenEditedAtTheOldLocationAsync(OtherPersonObjectId.ToString("D"));

        var second = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        second.Complete.Should().BeTrue();
        var changed = second.SourceChangedAfterMove.Should().ContainSingle(c => c.NewItem != null).Subject;
        changed.EditIsCurrent.Should().BeFalse();
        world.ItemFacts(SecureContainer, rig.ItemOf(DocumentId)!)!.Size.Should().Be(1234, "the document's own content stays current");
        world.ItemFacts(CustomerAContainer, Item).Should().BeNull("the edit is in the history, so the source is closed");
        var history = await rig.ReportedHistoryAsync(DocumentId);
        history.Select(v => (v.LastModifiedBy, v.LastModifiedDateTime, v.Size)).Should().Equal(
            [(Rig.CreatorName, Rig.Written, 1234L), ("Late Editor", editedAt, 2000L)],
            "newest first: the document's own content, current; the edit kept in the history with its author");
    }

    [Fact]
    public async Task AnEditAtTheOldLocation_WhoseAuthorsRightCannotBeRead_IsNeverMadeCurrent()
    {
        var (world, rig, _) = await MovedThenEditedAtTheOldLocationAsync(Creator);
        var faulty = new Mock<IAccessDataSource>();
        faulty.Setup(a => a.GetUserAccessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Dataverse unavailable"));
        var relocator = new DocumentContainerRelocator(rig.Resolver, world.EntityService!, rig.Spe.Object, rig.Indexing, rig.Locks,
            faulty.Object, NullLogger<DocumentContainerRelocator>.Instance);

        var second = await relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        second.Complete.Should().BeTrue();
        second.SourceChangedAfterMove.Should().ContainSingle(c => c.NewItem != null).Which.EditIsCurrent.Should().BeFalse(
            "an answer that cannot be had is not a right (ADR-003)");
        world.ItemFacts(SecureContainer, rig.ItemOf(DocumentId)!)!.Size.Should().Be(1234);
    }

    [Fact]
    public async Task ASourceEditedWhileItIsBeingMoved_IsReCopiedOnTheSameCall()
    {
        // The source changes between the copy's verification and the settle that would delete it (a save racing the
        // move): it no longer matches the witness recorded moments earlier, so it is re-copied on this same call.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        var rig = new Rig(world);
        var edited = false;
        rig.Spe.Setup(s => s.GetItemCreatorAsync(CustomerAContainer, Item, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                if (world.Updates.Count > 0 && !edited)
                {
                    edited = true; // the first read after the re-point sees the racing save
                    world.Items[(CustomerAContainer, Item)] = world.ItemFacts(CustomerAContainer, Item)! with { Size = 3000, QuickXorHash = "saved-during-the-move" };
                    rig.Versions[(CustomerAContainer, Item)] =
                    [
                        new VersionInfoDto("1.0", null, Rig.Written, 1234, Rig.CreatorName) { LastModifiedByUserId = Creator },
                        new VersionInfoDto("2.0", null, Rig.Written.AddHours(1), 3000, Rig.CreatorName) { LastModifiedByUserId = Creator },
                    ];
                }

                return world.ItemFacts(CustomerAContainer, Item);
            });

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        result.Complete.Should().BeTrue();
        result.SourceChangedAfterMove.Should().ContainSingle().Which.NewItem.Should().Be(rig.ItemOf(DocumentId));
        world.ItemFacts(SecureContainer, rig.ItemOf(DocumentId)!)!.Size.Should().Be(3000, "the racing save is in the document");
        world.ItemFacts(CustomerAContainer, Item).Should().BeNull();
        rig.LedgerOf(DocumentId).Should().BeNull();
    }

    [Fact]
    public async Task ASourceEditedAfterTheMove_WhoseReCopyFails_IsPending_NeverDeleted_AndARepeatCallClosesIt()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        var rig = new Rig(world) { DeleteFails = (drive, item) => drive == CustomerAContainer && item == Item };
        await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);
        world.Items[(CustomerAContainer, Item)] = world.ItemFacts(CustomerAContainer, Item)! with { Size = 2000, QuickXorHash = "source-edited" };
        rig.Versions[(CustomerAContainer, Item)] =
        [
            new VersionInfoDto("1.0", null, Rig.Written, 1234, Rig.CreatorName) { LastModifiedByUserId = Creator },
            new VersionInfoDto("2.0", null, Rig.Written.AddDays(1), 2000, "Late Editor"),
        ];
        rig.DeleteFails = (_, _) => false;
        rig.Spe.Setup(s => s.UploadSmallAsync(SecureContainer, It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<ConflictBehavior>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Graph unavailable")); // every upload into the target now faults

        var second = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        second.Complete.Should().BeFalse("the edited source could not be re-copied yet");
        second.Incomplete.Should().ContainSingle().Which.Pending.Should().Contain(p => p.StartsWith("source-changed-after-move", StringComparison.Ordinal));
        second.SourceChangedAfterMove.Should().ContainSingle().Which.DocumentId.Should().Be(DocumentId);
        world.ItemFacts(CustomerAContainer, Item).Should().NotBeNull("never deleted while its edit is not carried");
        rig.ItemOf(DocumentId).Should().Be(Rig.CopyItem);
        rig.LedgerOf(DocumentId).Should().Contain(Item);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Owner round 45 item 1 — a moved file keeps its version history; its original authorship is recorded
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    private static readonly DateTimeOffset T1 = new(2025, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2025, 6, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T3 = new(2025, 9, 1, 9, 0, 0, TimeSpan.Zero);

    private static Rig WithThreeVersions(World world, string drive)
    {
        var rig = new Rig(world);
        rig.Versions[(drive, Item)] =
        [
            new VersionInfoDto("3.0", null, T3, 1234, "Carol Current") { LastModifiedByUserId = Creator }, // listed newest first
            new VersionInfoDto("1.0", null, T1, 100, "Alice First") { LastModifiedByUserId = Creator },
            new VersionInfoDto("2.0", null, T2, 200, "Bob Second") { LastModifiedByUserId = OtherPersonObjectId.ToString("D") },
        ];
        return rig;
    }

    [Fact]
    public async Task Relocate_ReplaysTheSourcesHistory_OldestFirst_TheCurrentContentLast()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = WithThreeVersions(world, CustomerBContainer);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Relocated);
        rig.Steps.Should().Equal(
            $"download {CustomerBContainer}/{Item}@1.0", $"upload {CustomerA1Container} (Rename)",
            $"download {CustomerBContainer}/{Item}@2.0", $"upload {CustomerA1Container} (Replace)",
            $"download {CustomerBContainer}/{Item}", $"upload {CustomerA1Container} (Replace)",
            $"delete {CustomerBContainer}/{Item} (after re-point)");
        rig.Copies.Should().ContainSingle("every version after the first is written to the SAME item");
        rig.VersionsOf(CustomerA1Container, Rig.CopyItem).Select(v => v.Size).Should().Equal(100, 200, 1234);
        outcome.VersionsTruncated.Should().BeEmpty();
    }

    [Fact]
    public async Task Relocate_RecordsEachReplayedVersionsOriginalAuthorAndDate_InTheSameUpdateAsThePointer()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = WithThreeVersions(world, CustomerBContainer);

        await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        var repoint = world.Updates.First().Fields;
        repoint["sprk_graphitemid"].Should().Be(Rig.CopyItem);
        repoint[RelocatedVersionHistory.Column].Should().BeOfType<string>()
            .Which.Should().Contain("Alice First", "the version record is written with the re-point, one update");
        var history = await rig.ReportedHistoryAsync(DocumentId);
        history.Select(v => (v.Id, v.LastModifiedBy, v.LastModifiedDateTime, v.Size)).Should().Equal(
            [("3.0", "Carol Current", T3, 1234L), ("2.0", "Bob Second", T2, 200L), ("1.0", "Alice First", T1, 100L)],
            "the history a user sees is unchanged: the replayed versions report their ORIGINAL author and date");
        history.Select(v => v.LastModifiedByUserId).Should().Equal(Creator, OtherPersonObjectId.ToString("D"), Creator);
    }

    [Fact]
    public async Task Relocate_WhenAReplayedVersionCannotBeRead_DeletesTheCopy_AndLeavesTheRowAndTheSource()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = WithThreeVersions(world, CustomerBContainer);
        rig.VersionDownloadFails = (_, _, version) => version == "2.0";

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.FileMissing);
        outcome.Detail.Should().Contain("version 2.0");
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerA1Container, Rig.CopyItem, It.IsAny<CancellationToken>()), Times.Once,
            "the partial copy (version 1.0 already written) is removed");
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerBContainer, Item, It.IsAny<CancellationToken>()), Times.Never);
        world.Updates.Should().BeEmpty("the row is untouched; a repeat call retries");
    }

    [Fact]
    public async Task Relocate_WhenAReplayedVersionIsWrittenWithTheWrongSize_DeletesTheCopy_AndFails()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = WithThreeVersions(world, CustomerBContainer);
        // Graph lists version 2.0 at 200 bytes, but its download yields 250: the upload's size check catches it.
        rig.Spe.Setup(s => s.DownloadFileVersionAsync(CustomerBContainer, Item, "2.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(new byte[250]));

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Failed);
        outcome.Detail.Should().Contain("size");
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerA1Container, Rig.CopyItem, It.IsAny<CancellationToken>()), Times.Once);
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Relocate_WhenTheTargetKeepsFewerVersions_StatesVersionsTruncated_WithCounts_AndIsComplete()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = WithThreeVersions(world, CustomerBContainer);
        rig.CopyVersionLimit = 2;

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], CustomerA1Container, RelocationPurpose.LegacyMigration, apply: true);

        result.Complete.Should().BeTrue("a truncated history is a STATED outcome — no retry could change the target's limit");
        var truncated = result.VersionsTruncated.Should().ContainSingle().Subject;
        (truncated.DocumentId, truncated.ReplayedVersions, truncated.KeptVersions).Should().Be((DocumentId, 3, 2));
        var history = await rig.ReportedHistoryAsync(DocumentId);
        history.Select(v => v.LastModifiedBy).Should().Equal("Carol Current", "Bob Second");
    }

    [Fact]
    public async Task ASecondMove_KeepsTheFirstOriginalAuthorship()
    {
        // A file moved twice (the migration, then Make Secure): the second replay reads the first move's record, so a
        // version keeps its ORIGINAL author, never "the BFF, at the first move".
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = WithThreeVersions(world, CustomerBContainer);
        await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);
        world.Rows[("sprk_document", DocumentId)]["sprk_matter"] = new EntityReference("sprk_matter", SecureMatter);

        var second = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true, RelocationPurpose.MakeSecure, expectedTargetContainer: SecureContainer);

        second.State.Should().Be(RelocationState.Relocated);
        rig.DriveOf(DocumentId).Should().Be(SecureContainer);
        var history = await rig.ReportedHistoryAsync(DocumentId);
        history.Select(v => (v.LastModifiedBy, v.LastModifiedDateTime)).Should().Equal(
            [("Carol Current", T3), ("Bob Second", T2), ("Alice First", T1)]);
    }

    [Fact]
    public async Task TheVersionRecord_IsReportedOnlyForTheItemItDescribes_AndAnUnreadableOneChangesNothing()
    {
        var graph = new List<VersionInfoDto>
        {
            new("2.0", null, T2, 200, "SharePoint App"),
            new("1.0", null, T1, 100, "SharePoint App"),
        };
        var record = RelocatedVersionHistory.Map.Parse(new RelocatedVersionHistory.Map("01COPY",
            [new RelocatedVersionHistory.Entry("1.0", "Alice First", Creator, null, T1.AddYears(-1), 100, "01SRC", "1.0")]).Serialize().Json)!;

        var applied = RelocatedVersionHistory.Apply(record, "01COPY", graph);
        applied.Select(v => v.LastModifiedBy).Should().Equal("SharePoint App", "Alice First");
        applied[1].LastModifiedDateTime.Should().Be(T1.AddYears(-1));
        RelocatedVersionHistory.Apply(record, "01ANOTHERITEM", graph).Should().Equal(graph, "a record of another item says nothing about this one");

        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveAsync("sprk_document", DocumentId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", DocumentId) { [RelocatedVersionHistory.Column] = "{ not a record" });
        (await RelocatedVersionHistory.WithOriginalAuthorshipAsync(entities.Object, DocumentId, "01COPY", graph, NullLogger.Instance, CancellationToken.None))
            .Should().Equal(graph);
        entities.Setup(e => e.RetrieveAsync("sprk_document", DocumentId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Dataverse unavailable"));
        (await RelocatedVersionHistory.WithOriginalAuthorshipAsync(entities.Object, DocumentId, "01COPY", graph, NullLogger.Instance, CancellationToken.None))
            .Should().Equal(graph, "presentation only: an unreadable record never fails the history");
    }

    [Fact]
    public async Task WithoutTheVersionRecordColumn_NothingIsMoved_FailClosed()
    {
        var world = new World { ArchiveContainerId = ArchiveContainer, RelocatedVersionsColumnMissing = true };
        world.BusinessUnits[CustomerA] = (Root, CustomerAContainer);
        world.BusinessUnits[CustomerA1] = (CustomerA, CustomerA1Container);
        world.BusinessUnits[CustomerB] = (Root, CustomerBContainer);
        world.Rows[("sprk_matter", PlainMatter)] = Environment().Rows[("sprk_matter", PlainMatter)];
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = new Rig(world);

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], CustomerA1Container, RelocationPurpose.LegacyMigration, apply: true);

        result.Complete.Should().BeFalse();
        rig.Steps.Should().BeEmpty("without a place to record who wrote the history, no file is moved");
        world.Updates.Should().BeEmpty();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // F-B / owner round 45 item 3 — a communication's own attachment record is classified by its communication's subtree
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    private static readonly Guid UnlinkedAttachment = Guid.Parse("3f000000-0000-4000-8000-00000000f1a0");
    private static readonly Guid CommunicationId = Guid.Parse("3a000000-0000-4000-8000-00000000f1a0");

    private static void AddUnlinkedAttachment(World world, string drive, Guid? communication)
    {
        var row = new Entity("sprk_communicationattachment", UnlinkedAttachment)
        {
            ["sprk_graphdriveid"] = drive,
            ["sprk_graphitemid"] = Item,
        };
        if (communication is { } c)
        {
            row["sprk_communication"] = new EntityReference("sprk_communication", c);
        }

        world.Rows[("sprk_communicationattachment", UnlinkedAttachment)] = row;
    }

    [Fact]
    public async Task MakeSecure_AnUnlinkedAttachmentRowOfACommunicationInsideTheSubtree_IsReKeyedToTheCopy_AndTheSourceIsDeleted()
    {
        // The f1-v1 exposure: this row kept the secure bytes in the shared container while the transition said COMPLETE.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        world.Rows[("sprk_communication", CommunicationId)] = new Entity("sprk_communication", CommunicationId)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureMatter),
        };
        AddUnlinkedAttachment(world, CustomerAContainer, CommunicationId);
        var rig = new Rig(world);

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        result.Complete.Should().BeTrue();
        result.SourceKeptForOtherRecords.Should().BeEmpty("a communication of the secure record's subtree belongs with the moved file");
        var attachment = world.Rows[("sprk_communicationattachment", UnlinkedAttachment)];
        attachment.GetAttributeValue<string>("sprk_graphdriveid").Should().Be(SecureContainer);
        attachment.GetAttributeValue<string>("sprk_graphitemid").Should().Be(Rig.CopyItem);
        world.ItemFacts(CustomerAContainer, Item).Should().BeNull("no secure bytes stay in the shared container");
    }

    [Fact]
    public async Task ACommunicationsOwnAttachmentRecord_OutsideTheSubtree_KeepsTheSource_ForThatCommunication()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        // A communication regarding nothing resolves to the archive container: not where the moved file now is.
        world.Rows[("sprk_communication", CommunicationId)] = new Entity("sprk_communication", CommunicationId);
        AddUnlinkedAttachment(world, CustomerBContainer, CommunicationId);
        var rig = new Rig(world);

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], CustomerA1Container, RelocationPurpose.LegacyMigration, apply: true);

        result.Complete.Should().BeTrue();
        result.SourceKeptForOtherRecords.Should().ContainSingle().Which.KeptFor.Should().Contain($"sprk_communicationattachment:{UnlinkedAttachment}");
        world.ItemFacts(CustomerBContainer, Item).Should().NotBeNull();
        world.Rows[("sprk_communicationattachment", UnlinkedAttachment)].GetAttributeValue<string>("sprk_graphitemid").Should().Be(Item);
    }

    [Theory]
    [InlineData(false)] // it names no communication
    [InlineData(true)]  // its communication cannot be read
    public async Task AnUnlinkedAttachmentRowWhoseCommunicationsSubtreeIsUndecidable_IsPending_AndKeepsTheSource(bool namesACommunication)
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        AddUnlinkedAttachment(world, CustomerAContainer, namesACommunication ? Guid.NewGuid() : null); // an unmodelled (unreadable) row
        var rig = new Rig(world);

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        result.Complete.Should().BeFalse("undecidable is pending, never 'outside'");
        result.Incomplete.Should().ContainSingle().Which.Pending.Should().Contain(p => p.Contains($"sprk_communicationattachment {UnlinkedAttachment}", StringComparison.Ordinal));
        world.ItemFacts(CustomerAContainer, Item).Should().NotBeNull();
        result.SourceKeptForOtherRecords.Should().BeEmpty();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // F-C — the guards f1-v1 built that no test proved (verifier seeds V6, V8)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MoveAlong_UnderTheLegacyMigration_NeverCopiesARowWhoseFileIsNotVerifiablyItsOwn()
    {
        // V6: a row inside the moved file's container names the same file, but the file is not ITS creator's upload. The
        // move-along applies the same legitimacy rule as a relocation: it is reported for an administrator, never copied.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        world.Rows[("sprk_document", OtherDocumentId)] = Doc(drive: CustomerBContainer, item: Item, createdBy: OtherPerson, id: OtherDocumentId);
        var rig = new Rig(world);

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], CustomerA1Container, RelocationPurpose.LegacyMigration, apply: true);

        var outcome = result.Outcomes.Should().ContainSingle().Subject;
        outcome.MovedAlong.Should().ContainSingle().Which.State.Should().Be(RelocationState.SourceUnverified);
        rig.Copies.Should().ContainSingle("only the document's own file was copied");
        rig.ItemOf(OtherDocumentId).Should().Be(Item, "the other row is not moved");
        world.ItemFacts(CustomerBContainer, Item).Should().NotBeNull("a row still names it, so the source is not deleted");
        outcome.KeptForOtherRecords.Should().ContainSingle().Which.KeptFor.Should().Contain(k => k.Contains(OtherDocumentId.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenTheRelocationLockCannotBeTaken_NothingMoves_FailClosed()
    {
        // V8: whether another relocation of the document runs is UNKNOWN (the lock store faults) — treated as held.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        var rig = new Rig(world);
        rig.Locks.Fault = new TimeoutException("Redis unavailable");

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        result.Complete.Should().BeFalse();
        result.Outcomes.Should().ContainSingle().Which.State.Should().Be(RelocationState.Failed);
        rig.Steps.Should().BeEmpty();
        world.Updates.Should().BeEmpty();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Owner round 54 item 1 — the Write check that makes a post-move edit current is read FRESH
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AStaleCachedWriteAnswer_CannotMakeAPostMoveEditCurrent()
    {
        // The editor could write the document until Make Secure took that right away; the REAL 60-second
        // CachedAccessDataSource still holds the Write answer it read just before. Whatever source the relocator is
        // given, an answer older than its question is not the editor's right NOW: the edit is kept in the history.
        var editor = OtherPersonObjectId.ToString("D");
        var (world, rig, _) = await MovedThenEditedAtTheOldLocationAsync(editor);
        // Integration: the cache is tenant-keyed (ADR-009) and caches nothing without the request's tid, so the
        // precondition below runs it inside a request that carries one.
        var cached = new CachedAccessDataSource(
            rig.Access,
            new Sprk.Bff.Api.Infrastructure.Cache.TenantCache(
                new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
                NullLogger<Sprk.Bff.Api.Infrastructure.Cache.TenantCache>.Instance),
            new Microsoft.AspNetCore.Http.HttpContextAccessor
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                        [new System.Security.Claims.Claim("tid", "11111111-1111-1111-1111-111111111166")], "test")),
                },
            },
            NullLogger<CachedAccessDataSource>.Instance);
        rig.Access.Rights[editor] = AccessRights.Read | AccessRights.Write;
        (await cached.GetUserAccessAsync(editor, DocumentId.ToString("D"))).AccessRights.Should().HaveFlag(AccessRights.Write);
        rig.Access.Rights[editor] = AccessRights.Read; // Make Secure: the editor may no longer write
        (await cached.GetUserAccessAsync(editor, DocumentId.ToString("D"))).AccessRights.Should().HaveFlag(AccessRights.Write,
            "precondition: for its 60 seconds the cache still answers Write");

        var second = await rig.NewRelocator(access: cached)
            .RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        second.Complete.Should().BeTrue("the edit is carried into the history either way");
        second.SourceChangedAfterMove.Should().ContainSingle(c => c.NewItem != null).Which.EditIsCurrent.Should().BeFalse(
            "a Write answer cached before the question was asked is not the editor's right now");
        world.ItemFacts(SecureContainer, rig.ItemOf(DocumentId)!)!.Size.Should().Be(1234, "the document's own content stays current");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Owner round 54 item 2 — the relocation lock lasts as long as the move; a lost lock stops the move
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The lock store's key of a document's relocation lock (<c>IdempotencyService</c>'s lock key).</summary>
    private static string StoredLockKey(Guid documentId) => "idempotency:lock:" + DocumentContainerRelocator.RelocationLockKey(documentId);

    /// <summary>Waits (bounded) for something a background continuation does; false when it did not happen in time.</summary>
    private static async Task<bool> EventuallyAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        return condition();
    }

    [Fact]
    public async Task ASecondRelocator_CannotStartAMove_WhileTheFirstStillHoldsItsRenewedLock()
    {
        // Two BFF instances share the lock store (the REAL IdempotencyService over one cache). The first move pauses in its
        // replay for longer than one lock duration; its heartbeat renews the lock, so a second relocation of the same
        // document (a Make Secure retry, the migration pass) is refused all along — and the first then completes.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        var rig = new Rig(world);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));
        var store = new ExpiringCache(time);
        var first = rig.NewRelocator(locks: new IdempotencyService(store, NullLogger<IdempotencyService>.Instance, time), time: time);
        var second = rig.NewRelocator(locks: new IdempotencyService(store, NullLogger<IdempotencyService>.Instance, time), time: time);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.BeforeDownload = async (_, _) =>
        {
            if (paused.TrySetResult())
            {
                await resume.Task; // only the first move's first download pauses
            }
        };

        try
        {
            var moving = Task.Run(() => first.RelocateIfMisplacedAsync(
                DocumentId, apply: true, RelocationPurpose.MakeSecure, expectedTargetContainer: SecureContainer));
            await paused.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var writesAtTake = store.WritesOf(StoredLockKey(DocumentId)); // the take and its confirmation
            for (var minute = 1; minute <= 12; minute++)
            {
                time.Advance(TimeSpan.FromMinutes(1));
                var due = writesAtTake + minute / 2; // one renewal per 2-minute heartbeat
                await EventuallyAsync(() => store.WritesOf(StoredLockKey(DocumentId)) >= due);
            }

            var refused = await second.RelocateIfMisplacedAsync(
                DocumentId, apply: true, RelocationPurpose.MakeSecure, expectedTargetContainer: SecureContainer);

            refused.State.Should().Be(RelocationState.Failed,
                "12 minutes in — past one 10-minute lock — the first move's lock is renewed, so it is still held");
            refused.Detail.Should().Contain("another relocation of this document is running");
            rig.Copies.Should().BeEmpty("the refused call copies nothing; the first is still paused before its first upload");

            resume.SetResult();
            var moved = await moving.WaitAsync(TimeSpan.FromSeconds(30));
            moved.State.Should().Be(RelocationState.Relocated);
            rig.Copies.Should().ContainSingle();
            store.Holds(StoredLockKey(DocumentId)).Should().BeFalse("the first move released its lock when it finished");
        }
        finally
        {
            resume.TrySetResult();
        }
    }

    [Fact]
    public async Task AMoveThatLosesItsLock_StopsBeforeTheRepoint_RemovesItsCopy_AndTouchesNothingElse()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        var rig = new Rig(world);
        rig.Locks.FailRenewalsFrom = 2; // 1 = the confirmation at the take; the next (before the re-point) finds it lost

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true, RelocationPurpose.MakeSecure);

        outcome.State.Should().Be(RelocationState.Failed);
        outcome.Detail.Should().StartWith(DocumentContainerRelocator.LockLostPrefix);
        world.Updates.Should().BeEmpty("nothing is re-pointed without the lock");
        rig.Spe.Verify(s => s.DeleteFileAsync(SecureContainer, Rig.CopyItem, It.IsAny<CancellationToken>()), Times.Once,
            "the copy (this call's own, referenced by nothing) is removed");
        world.ItemFacts(CustomerAContainer, Item).Should().NotBeNull("the source is untouched");
    }

    [Fact]
    public async Task ARepeatCallThatLosesItsLock_ReKeysNothing_DeletesNothing_AndLeavesTheLedger()
    {
        var world = Environment();
        var row = Doc(matter: SecureMatter, drive: SecureContainer, item: Rig.CopyItem);
        var ledger = LedgerJson(CustomerAContainer, Item, witnessSize: 1234, witnessHash: "hash-1234", witnessVersion: "1.0");
        row[Ledger] = ledger;
        world.Rows[("sprk_document", DocumentId)] = row;
        world.Items[(SecureContainer, Rig.CopyItem)] = new SpeItemCreator("memo.docx", null, BffApplication, 1234, "hash-1234");
        var linked = Guid.Parse("3e000000-0000-4000-8000-00000000f1a0");
        world.Rows[("sprk_communicationattachment", linked)] = new Entity("sprk_communicationattachment", linked)
        {
            ["sprk_document"] = new EntityReference("sprk_document", DocumentId),
            ["sprk_graphdriveid"] = CustomerAContainer,
            ["sprk_graphitemid"] = Item,
        };
        var rig = new Rig(world);
        rig.Locks.FailRenewalsFrom = 2; // the settle's first confirmation finds the lock lost

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        result.Complete.Should().BeFalse();
        result.Incomplete.Should().ContainSingle().Which.Pending.Should().Contain(p => p.StartsWith(DocumentContainerRelocator.LockLostPrefix, StringComparison.Ordinal));
        world.Rows[("sprk_communicationattachment", linked)].GetAttributeValue<string>("sprk_graphitemid").Should().Be(Item, "no re-key without the lock");
        world.ItemFacts(CustomerAContainer, Item).Should().NotBeNull("no delete without the lock");
        rig.LedgerOf(DocumentId).Should().Be(ledger, "the ledger is not rewritten without the lock");
        world.Updates.Should().BeEmpty();
        rig.Indexing.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ALockLostRightBeforeTheSourceDelete_KeepsTheSource()
    {
        var world = Environment();
        var row = Doc(matter: SecureMatter, drive: SecureContainer, item: Rig.CopyItem);
        var ledger = LedgerJson(CustomerAContainer, Item, witnessSize: 1234, witnessHash: "hash-1234", witnessVersion: "1.0");
        row[Ledger] = ledger;
        world.Rows[("sprk_document", DocumentId)] = row;
        world.Items[(SecureContainer, Rig.CopyItem)] = new SpeItemCreator("memo.docx", null, BffApplication, 1234, "hash-1234");
        var rig = new Rig(world);
        rig.Locks.FailRenewalsFrom = 3; // the take's confirmation and the settle's pass; the delete's confirmation fails

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        result.Complete.Should().BeFalse();
        result.Incomplete.Should().ContainSingle().Which.Pending.Should().Contain(p => p.StartsWith(DocumentContainerRelocator.LockLostPrefix, StringComparison.Ordinal));
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerAContainer, Item, It.IsAny<CancellationToken>()), Times.Never,
            "the source matches its witness, but it is never deleted without the lock");
        rig.LedgerOf(DocumentId).Should().Be(ledger);
        rig.Indexing.Calls.Should().BeEmpty("the settle stops at the lost lock; the index step is not reached either");
    }

    [Fact]
    public async Task AMoveWhoseHeartbeatFindsTheLockLost_StopsItsCopyAtTheNextVersion()
    {
        // The copy runs past one heartbeat and that renewal fails: the move stops at the next version (no more uploads),
        // removes its partial copy, and never re-points.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = WithThreeVersions(world, CustomerBContainer);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));
        rig.Locks.FailRenewalsFrom = 2; // the take's confirmation holds; the first heartbeat finds the lock lost
        rig.BeforeVersionDownload = async (_, _, version) =>
        {
            if (version == "1.0")
            {
                time.Advance(DocumentContainerRelocator.RelocationLockRenewInterval);
                await EventuallyAsync(() => rig.Locks.Renewals >= 2);
            }
        };

        var outcome = await rig.NewRelocator(time: time).RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Failed);
        outcome.Detail.Should().StartWith(DocumentContainerRelocator.LockLostPrefix).And.Contain("during the copy");
        rig.Steps.Count(s => s.StartsWith("upload", StringComparison.Ordinal)).Should().Be(1,
            "once the heartbeat found the lock lost, no further version is written");
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerA1Container, Rig.CopyItem, It.IsAny<CancellationToken>()), Times.Once);
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task ALockLostBeforeTheSettledLedgerIsWritten_LeavesTheStoredLedger()
    {
        // Everything the entry owed is done (the source is already gone, the index re-keyed), then the lock is lost: the
        // settled ledger is not written — writing it unlocked could overwrite what another relocation now records.
        var world = Environment();
        var row = Doc(matter: SecureMatter, drive: SecureContainer, item: Rig.CopyItem);
        var ledger = LedgerJson(CustomerAContainer, Item, witnessSize: 1234, witnessHash: "hash-1234", witnessVersion: "1.0");
        row[Ledger] = ledger;
        world.Rows[("sprk_document", DocumentId)] = row;
        world.Items[(SecureContainer, Rig.CopyItem)] = new SpeItemCreator("memo.docx", null, BffApplication, 1234, "hash-1234");
        world.Items[(CustomerAContainer, Item)] = null; // the source is gone
        var rig = new Rig(world);
        rig.Locks.FailRenewalsFrom = 3; // the take's confirmation and the settle's pass; the ledger write's confirmation fails

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        result.Complete.Should().BeFalse();
        result.Incomplete.Should().ContainSingle().Which.Pending.Should().Contain(p => p.StartsWith(DocumentContainerRelocator.LockLostPrefix, StringComparison.Ordinal));
        rig.LedgerOf(DocumentId).Should().Be(ledger, "the repeat call redoes the idempotent steps and records the ledger");
    }

    [Fact]
    public async Task ARelocationWhoseLockWasTakenOver_NeverReleasesTheNewHoldersLock()
    {
        // The lock is taken over mid-copy (it expired under this call and another relocation took it): this call stops
        // before the re-point AND leaves the other relocation's lock in place.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        var rig = new Rig(world);
        var key = DocumentContainerRelocator.RelocationLockKey(DocumentId);
        rig.BeforeDownload = (_, _) =>
        {
            rig.Locks.Steal(key, "another-relocation");
            return Task.CompletedTask;
        };

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true, RelocationPurpose.MakeSecure);

        outcome.State.Should().Be(RelocationState.Failed);
        outcome.Detail.Should().StartWith(DocumentContainerRelocator.LockLostPrefix);
        world.Updates.Should().BeEmpty();
        rig.Locks.OwnerOf(key).Should().Be("another-relocation", "the lock another relocation now holds is left to it");
        rig.Locks.Released.Should().BeEmpty();
    }

    /// <summary>A lock store that answers every take, renewal and release "yes" — a store that has stopped excluding anyone.</summary>
    private sealed class AlwaysTakenLocks : Sprk.Bff.Api.Services.Jobs.IIdempotencyService
    {
        public Task<bool> IsEventProcessedAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task MarkEventAsProcessedAsync(string eventId, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> TryAcquireProcessingLockAsync(string eventId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> TryAcquireProcessingLockAsync(
            string eventId, string ownerId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> RenewProcessingLockAsync(
            string eventId, string ownerId, TimeSpan lockDuration, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task ReleaseProcessingLockAsync(string eventId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task OneRelocator_NeverRunsTwoMovesOfOneDocumentAtOnce_EvenWhenItsLockStoreStopsExcluding()
    {
        // The relocator keeps its own record of the documents it is moving: a second move of the same document on the same
        // instance (a caller that relocates in parallel) is refused even if the lock store answers "taken" to both.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        var rig = new Rig(world);
        var relocator = rig.NewRelocator(locks: new AlwaysTakenLocks());
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.BeforeDownload = async (_, _) =>
        {
            if (paused.TrySetResult())
            {
                await resume.Task;
            }
        };

        try
        {
            var moving = Task.Run(() => relocator.RelocateIfMisplacedAsync(DocumentId, apply: true, RelocationPurpose.MakeSecure));
            await paused.Task.WaitAsync(TimeSpan.FromSeconds(30));

            var second = await relocator.RelocateIfMisplacedAsync(DocumentId, apply: true, RelocationPurpose.MakeSecure);

            second.State.Should().Be(RelocationState.Failed);
            second.Detail.Should().Contain("another relocation of this document is running");
            rig.Copies.Should().BeEmpty();
            resume.SetResult();
            (await moving.WaitAsync(TimeSpan.FromSeconds(30))).State.Should().Be(RelocationState.Relocated);
            rig.Copies.Should().ContainSingle();
        }
        finally
        {
            resume.TrySetResult();
        }
    }

    [Fact]
    public async Task ALockStoreThatAnswersTakenWithoutHoldingTheLock_RunsNothing()
    {
        // The real IdempotencyService fails OPEN when its cache faults (#984): its acquire answers "taken" though nothing
        // is stored. A relocation reads its lock back before anything runs — not ours, so nothing moves (fail closed).
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        var rig = new Rig(world);
        var store = new ExpiringCache(TimeProvider.System) { Fault = new TimeoutException("Redis unavailable") };
        var relocator = rig.NewRelocator(locks: new IdempotencyService(store, NullLogger<IdempotencyService>.Instance));

        var outcome = await relocator.RelocateIfMisplacedAsync(DocumentId, apply: true, RelocationPurpose.MakeSecure);

        outcome.State.Should().Be(RelocationState.Failed);
        rig.Steps.Should().BeEmpty("nothing is downloaded, uploaded or deleted without a lock that reads back as this call's");
        world.Updates.Should().BeEmpty();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Owner round 54 item 3 — no post-move edit is dropped: versions whose ids are not numbers are ordered by their times
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    private static readonly DateTimeOffset EditedAt1 = new(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EditedAt2 = new(2026, 10, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AnEditAtTheOldLocation_WhoseVersionIdsAreNotNumbers_CarriesEveryVersionWrittenAfterTheMove_InTimeOrder()
    {
        var editor = OtherPersonObjectId.ToString("D");
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        var rig = new Rig(world) { DeleteFails = (drive, item) => drive == CustomerAContainer && item == Item };
        rig.Versions[(CustomerAContainer, Item)] = [new VersionInfoDto("v-a", null, Rig.Written, 1234, Rig.CreatorName) { LastModifiedByUserId = Creator }];
        (await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true))
            .Complete.Should().BeFalse();
        world.Items[(CustomerAContainer, Item)] = world.ItemFacts(CustomerAContainer, Item)! with { Size = 2000, QuickXorHash = "source-edited" };
        rig.Versions[(CustomerAContainer, Item)] =
        [
            new VersionInfoDto("v-c", null, EditedAt2, 2000, "Late Editor") { LastModifiedByUserId = editor }, // listed newest first
            new VersionInfoDto("v-a", null, Rig.Written, 1234, Rig.CreatorName) { LastModifiedByUserId = Creator },
            new VersionInfoDto("v-b", null, EditedAt1, 1500, "Late Editor") { LastModifiedByUserId = editor },
        ];
        rig.DeleteFails = (_, _) => false;
        rig.Access.Rights[editor] = AccessRights.Read | AccessRights.Write;

        var second = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        second.Complete.Should().BeTrue();
        second.SourceChangedAfterMove.Should().ContainSingle(c => c.NewItem != null).Which.CarriedVersions.Should().Be(2,
            "BOTH versions written after the move are carried — the intermediate one is never dropped");
        var history = await rig.ReportedHistoryAsync(DocumentId);
        history.Select(v => (v.LastModifiedDateTime, v.Size)).Should().Equal(
            [(EditedAt2, 2000L), (EditedAt1, 1500L), (Rig.Written, 1234L)], "newest first, each edit in its time order");
        world.ItemFacts(CustomerAContainer, Item).Should().BeNull();
    }

    [Fact]
    public async Task AWitnessRecordedWithoutAVersion_StillCarriesEveryVersionWrittenAfterTheMove_ByTheirTimes()
    {
        // Graph listed no version when the copy was verified: the witness records the item's time, and the versions the
        // source receives after the move are put in order against it.
        var editor = OtherPersonObjectId.ToString("D");
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        world.Items[(CustomerAContainer, Item)] = new SpeItemCreator("memo.docx", Creator, null, 1234, "hash-1234", LastModified: Rig.Written);
        var rig = new Rig(world) { DeleteFails = (drive, item) => drive == CustomerAContainer && item == Item };
        rig.Versions[(CustomerAContainer, Item)] = [];
        (await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true))
            .Complete.Should().BeFalse();
        rig.LedgerOf(DocumentId).Should().Contain("\"modified\"", "the witness records the time of the content it saw");
        world.Items[(CustomerAContainer, Item)] = world.ItemFacts(CustomerAContainer, Item)! with { Size = 2000, QuickXorHash = "source-edited" };
        rig.Versions[(CustomerAContainer, Item)] =
        [
            new VersionInfoDto("2.0", null, EditedAt2, 2000, "Late Editor") { LastModifiedByUserId = editor },
            new VersionInfoDto("1.0", null, EditedAt1, 1500, "Late Editor") { LastModifiedByUserId = editor },
        ];
        rig.DeleteFails = (_, _) => false;
        rig.Access.Rights[editor] = AccessRights.Read | AccessRights.Write;

        var second = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        second.Complete.Should().BeTrue();
        second.SourceChangedAfterMove.Should().ContainSingle(c => c.NewItem != null).Which.CarriedVersions.Should().Be(2);
        world.ItemFacts(SecureContainer, rig.ItemOf(DocumentId)!)!.Size.Should().Be(2000);
    }

    [Theory]
    [InlineData("same-time")]                // two versions written after the move carry the same time
    [InlineData("no-witness-time")]          // the witness recorded neither a numbered version nor a time
    [InlineData("a-version-without-time")]   // a version written after the move carries no time
    [InlineData("no-later-version")]         // the content changed, yet no version is later than the witness
    [InlineData("times-contradict-numbers")] // the current version is not the latest by time
    public async Task APostMoveHistoryWhoseOrderCannotBeDecided_IsHistoryUndecidable_AndTheRowAndTheSourceAreUntouched(string history)
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        if (history != "no-witness-time")
        {
            // The witness records the item's time (Graph lists no version at the move, except in the same-time case).
            world.Items[(CustomerAContainer, Item)] = new SpeItemCreator("memo.docx", Creator, null, 1234, "hash-1234", LastModified: Rig.Written);
        }

        var rig = new Rig(world) { DeleteFails = (drive, item) => drive == CustomerAContainer && item == Item };
        rig.Versions[(CustomerAContainer, Item)] = history == "same-time"
            ? [new VersionInfoDto("v-a", null, Rig.Written, 1234, Rig.CreatorName) { LastModifiedByUserId = Creator }]
            : []; // no version listed: the witness has no version id (and, for no-witness-time, no time either)
        (await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true))
            .Complete.Should().BeFalse();
        world.Items[(CustomerAContainer, Item)] = world.ItemFacts(CustomerAContainer, Item)! with { Size = 2000, QuickXorHash = "source-edited" };
        rig.Versions[(CustomerAContainer, Item)] = history switch
        {
            "same-time" =>
            [
                new VersionInfoDto("v-a", null, Rig.Written, 1234, Rig.CreatorName) { LastModifiedByUserId = Creator },
                new VersionInfoDto("v-b", null, EditedAt1, 1500, "Late Editor"),
                new VersionInfoDto("v-c", null, EditedAt1, 2000, "Late Editor"),
            ],
            "a-version-without-time" =>
            [
                new VersionInfoDto("1.0", null, default, 1500, "Late Editor"),
                new VersionInfoDto("2.0", null, EditedAt2, 2000, "Late Editor"),
            ],
            "no-later-version" =>
            [
                new VersionInfoDto("1.0", null, Rig.Written.AddDays(-2), 1500, "Late Editor"),
                new VersionInfoDto("2.0", null, Rig.Written.AddDays(-1), 2000, "Late Editor"),
            ],
            "times-contradict-numbers" =>
            [
                new VersionInfoDto("1.0", null, EditedAt2, 1500, "Late Editor"),
                new VersionInfoDto("2.0", null, EditedAt1, 2000, "Late Editor"),
            ],
            _ =>
            [
                new VersionInfoDto("1.0", null, EditedAt1, 1500, "Late Editor"),
                new VersionInfoDto("2.0", null, EditedAt2, 2000, "Late Editor"),
            ],
        };
        rig.DeleteFails = (_, _) => false;

        var second = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        second.Complete.Should().BeFalse("never silently carries only the latest");
        second.Incomplete.Should().ContainSingle().Which.Pending.Should().Contain(
            p => p.StartsWith(DocumentContainerRelocator.HistoryUndecidablePrefix, StringComparison.Ordinal));
        rig.ItemOf(DocumentId).Should().Be(Rig.CopyItem, "the row is untouched");
        world.ItemFacts(CustomerAContainer, Item).Should().NotBeNull("the source is untouched");
        rig.Copies.Should().ContainSingle("nothing is re-copied");
        rig.LedgerOf(DocumentId).Should().Contain(Item, "still owed: a repeat call retries");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Owner round 54 item 4 — every fail-closed branch of the replay and the witness has a test that bites
    // (the f1-v2 verification's seeds Sk, Sq, Si, Sj, Sm, Sw; Sd is DocumentVersionListPagingTests)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("faults")]
    [InlineData("not-found")]
    [InlineData("empty")]
    public async Task ASourceThatCannotBeComparedWithItsWitness_IsNeverDeleted(string versionList)
    {
        // Sk. The witness has no hash, so the source's current VERSION decides — and its version list cannot be read.
        // Unknown is never "matches": the source is kept, pending, retried.
        var world = Environment();
        var row = Doc(matter: SecureMatter, drive: SecureContainer, item: Rig.CopyItem);
        row[Ledger] = LedgerJson(CustomerAContainer, Item, witnessSize: 1234, witnessHash: null, witnessVersion: "1.0");
        world.Rows[("sprk_document", DocumentId)] = row;
        world.Items[(SecureContainer, Rig.CopyItem)] = new SpeItemCreator("memo.docx", null, BffApplication, 1234, null);
        world.Items[(CustomerAContainer, Item)] = new SpeItemCreator("memo.docx", Creator, null, 1234, null);
        var rig = new Rig(world);
        var listing = rig.Spe.Setup(s => s.ListFileVersionsAsync(CustomerAContainer, Item, It.IsAny<CancellationToken>()));
        switch (versionList)
        {
            case "faults":
                listing.ThrowsAsync(new InvalidOperationException("Graph unavailable"));
                break;
            case "not-found":
                listing.ReturnsAsync((IReadOnlyList<VersionInfoDto>?)null);
                break;
            default:
                listing.ReturnsAsync(Array.Empty<VersionInfoDto>());
                break;
        }

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        result.Complete.Should().BeFalse();
        result.Incomplete.Should().ContainSingle().Which.Pending.Should().Contain(
            p => p.Contains("could not be compared with the witness", StringComparison.Ordinal));
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerAContainer, Item, It.IsAny<CancellationToken>()), Times.Never);
        rig.LedgerOf(DocumentId).Should().Contain(Item);
        rig.Copies.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]  // the version list faults
    [InlineData(false)] // the item is not found when its versions are listed
    public async Task ASourceWhoseVersionsCannotBeListed_IsNeverMoved_SoNoHistoryIsSilentlyLost(bool faults)
    {
        // Sq. A move with the current content alone would lose the history without saying so.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = new Rig(world);
        var listing = rig.Spe.Setup(s => s.ListFileVersionsAsync(CustomerBContainer, Item, It.IsAny<CancellationToken>()));
        if (faults)
        {
            listing.ThrowsAsync(new InvalidOperationException("Graph unavailable"));
        }
        else
        {
            listing.ReturnsAsync((IReadOnlyList<VersionInfoDto>?)null);
        }

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Failed);
        outcome.Detail.Should().Contain("version history could not be copied");
        rig.Steps.Should().BeEmpty("nothing is copied, so nothing is copied without its history");
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task ASourceThatCouldNeverBeProvedUnchanged_IsNeverMoved()
    {
        // Si. No quickXorHash and no version listed: no witness a later call could compare the source with — the source
        // could then never be deleted, so the move never starts.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        world.Items[(CustomerBContainer, Item)] = new SpeItemCreator("memo.docx", Creator, null, 1234, null);
        var rig = new Rig(world);
        rig.Versions[(CustomerBContainer, Item)] = [];

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Failed);
        outcome.Detail.Should().Contain("could never prove it unchanged");
        rig.Steps.Should().BeEmpty();
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task APriorVersionLargerThanASingleRequestCopy_FailsTheMove_BeforeAByteMoves()
    {
        // Sj. The current content fits, a PRIOR version does not: reported, never half-copied.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = new Rig(world);
        rig.Versions[(CustomerBContainer, Item)] =
        [
            new VersionInfoDto("1.0", null, T1, DocumentContainerRelocator.MaxRelocatableBytes + 1, "Alice First") { LastModifiedByUserId = Creator },
            new VersionInfoDto("2.0", null, T2, 1234, "Carol Current") { LastModifiedByUserId = Creator },
        ];
        rig.VersionDownloadFails = (_, _, version) => version == "1.0"; // a test never materializes 250 MB

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Failed);
        outcome.Detail.Should().Contain("larger than a single-request copy");
        rig.Steps.Should().BeEmpty();
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task AReplayedVersionWrittenToAnotherItem_FailsTheMove_AndRemovesBothItems()
    {
        // Sm. A later version must become a new version of THE copy; written anywhere else, the replay is not the history.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = WithThreeVersions(world, CustomerBContainer);
        rig.Spe.Setup(s => s.UploadSmallAsync(CustomerA1Container, It.IsAny<string>(), It.IsAny<Stream>(), ConflictBehavior.Replace, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string drive, string name, Stream content, ConflictBehavior _, CancellationToken _) =>
                new FileHandleDto("01STRAYITEM", name, Rig.TargetRoot, content.Length, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                    "\"{01STRAYITEM},1\"", false, "https://contoso.sharepoint.com/01STRAYITEM", drive));

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Failed);
        outcome.Detail.Should().Contain("was written to another item");
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerA1Container, "01STRAYITEM", It.IsAny<CancellationToken>()), Times.Once, "the stray item is removed");
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerA1Container, Rig.CopyItem, It.IsAny<CancellationToken>()), Times.Once, "and so is the partial copy");
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerBContainer, Item, It.IsAny<CancellationToken>()), Times.Never);
        world.Updates.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]  // the copy's version list faults
    [InlineData(false)] // it lists nothing
    public async Task ACopyWhoseVersionsCannotBeListed_IsRemoved_AndTheRowIsNeverRepointed(bool faults)
    {
        // Sw. Without the copy's version ids the replayed versions' original authorship cannot be recorded; a row is never
        // re-pointed to a copy whose history would read "the BFF, at the move".
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = WithThreeVersions(world, CustomerBContainer);
        var listing = rig.Spe.Setup(s => s.ListFileVersionsAsync(CustomerA1Container, It.IsAny<string>(), It.IsAny<CancellationToken>()));
        if (faults)
        {
            listing.ThrowsAsync(new InvalidOperationException("Graph unavailable"));
        }
        else
        {
            listing.ReturnsAsync(Array.Empty<VersionInfoDto>());
        }

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Failed);
        outcome.Detail.Should().Contain("original authorship could not be recorded");
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerA1Container, Rig.CopyItem, It.IsAny<CancellationToken>()), Times.Once);
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerBContainer, Item, It.IsAny<CancellationToken>()), Times.Never);
        world.Updates.Should().BeEmpty();
    }

    /// <summary>A stored ledger owing one source (pending delete, re-key and index), with or without a witness.</summary>
    internal static string LedgerJson(
        string sourceDrive, string sourceItem, long? witnessSize = null, string? witnessHash = null, string? witnessVersion = null,
        DateTimeOffset? witnessModified = null)
    {
        var modified = witnessModified is { } at ? $",\"modified\":\"{at:O}\"" : string.Empty;
        var witness = witnessSize is null && witnessHash is null && witnessVersion is null
            ? string.Empty
            : $$""","witness":{"size":{{witnessSize?.ToString() ?? "null"}},"quickXorHash":{{(witnessHash is null ? "null" : $"\"{witnessHash}\"")}},"version":{{(witnessVersion is null ? "null" : $"\"{witnessVersion}\"")}}{{modified}}}""";
        return $$"""{"v":1,"entries":[{"sourceDrive":"{{sourceDrive}}","sourceItem":"{{sourceItem}}","source":"pending","rekeyPending":true,"indexed":"none","at":"2026-10-05T00:00:00+00:00"{{witness}}}]}""";
    }
}
