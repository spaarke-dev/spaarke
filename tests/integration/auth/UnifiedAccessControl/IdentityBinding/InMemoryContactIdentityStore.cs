using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;

/// <summary>
/// A small Dataverse-shaped ROW STORE behind <see cref="IContactIdentityStore"/> for task 141's tests. It is
/// the module-boundary double (ADR-038: no HTTP doubles) and it behaves like the platform where the binding
/// depends on it:
/// <list type="bullet">
///   <item>oid and email matching are case-INSENSITIVE (Dataverse's SQL collation), and the oid is matched on
///     the stored TEXT, so an upper-case stored oid still matches;</item>
///   <item>both lookups honour <c>$top=2</c>; the email lookup returns ACTIVE rows only, the oid lookup any state;</item>
///   <item>every row has a version; <c>If-Match</c> writes fail with 412 when it moved;</item>
///   <item>the <c>sprk_externalobjectid</c> alternate key is unique among non-null values (when
///     <see cref="KeyDefined"/>), so a create or bind that would duplicate an oid fails;</item>
///   <item><see cref="MaskBindingColumn"/> reproduces field-level-security masking: the oid comes back null in
///     rows AND is treated as null in filters (Dataverse substitutes null — documented).</item>
/// </list>
/// Failures are injected per operation. Every write is recorded in <see cref="Writes"/>, every read in
/// <see cref="Reads"/>, so a test asserts what the code DID, not only what it returned.
/// </summary>
public sealed class InMemoryContactIdentityStore : IContactIdentityStore
{
    private readonly object _gate = new();
    private int _version = 1;

    public sealed class Contact
    {
        public Guid Id { get; init; }
        public int StateCode { get; set; }
        public string? Oid { get; set; }
        public int? Plane { get; set; }
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public CollisionFlag? Flag { get; set; }
        public int Version { get; set; }
    }

    public sealed class SystemUser
    {
        public Guid Id { get; init; }
        public Guid? Oid { get; set; }
        public string? Email { get; set; }
        public string? DomainName { get; set; }
        public Guid? PrimaryContactId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public bool IsDisabled { get; set; }
        public int AccessMode { get; set; }
        public Guid? ApplicationId { get; set; }
        public int Version { get; set; }
    }

    public Dictionary<Guid, Contact> Contacts { get; } = new();
    public Dictionary<Guid, SystemUser> SystemUsers { get; } = new();

    /// <summary>(operation, rowId, detail) for every write that LANDED.</summary>
    public List<(string Op, Guid? RowId, string Detail)> Writes { get; } = new();

    /// <summary>Every read, by operation name.</summary>
    public List<string> Reads { get; } = new();

    public bool KeyDefined { get; set; } = true;
    public bool MaskBindingColumn { get; set; }
    public LookupStatus OidLookupStatus { get; set; } = LookupStatus.Read;
    public LookupStatus EmailLookupStatus { get; set; } = LookupStatus.Read;
    public bool FailReferences { get; set; }
    public bool FailGetContact { get; set; }
    public bool FailBind { get; set; }
    public bool FailCreate { get; set; }
    public bool FailFlagWrites { get; set; }
    public bool FailSystemUserScan { get; set; }
    public int ScanPageSize { get; set; } = 500;

    /// <summary>Oids whose NEXT oid lookup fails (one-shot each) — a transient read failure for one user.</summary>
    public HashSet<Guid> FailOidLookupOnceFor { get; } = new();

    /// <summary>Runs inside a create, after the key check and before the insert — to stage a race.</summary>
    public Func<Guid, Task>? BeforeCreate { get; set; }

    /// <summary>Runs inside a bind before its <c>If-Match</c> check — to stage a concurrent writer.</summary>
    public Action<Contact>? BeforeBind { get; set; }

    /// <summary>Marks a row as changed by someone else (a new version), as a concurrent write would.</summary>
    public void Touch(Contact contact) => contact.Version = NextVersion();

    /// <summary>Clears every row, record and injected failure (shared-fixture reuse).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            Contacts.Clear();
            SystemUsers.Clear();
            Writes.Clear();
            Reads.Clear();
            KeyDefined = true;
            MaskBindingColumn = false;
            OidLookupStatus = LookupStatus.Read;
            EmailLookupStatus = LookupStatus.Read;
            FailReferences = FailGetContact = FailBind = FailCreate = FailFlagWrites = FailSystemUserScan = false;
            FailOidLookupOnceFor.Clear();
            BeforeCreate = null;
            BeforeBind = null;
        }
    }

    // ── Seeding ────────────────────────────────────────────────────────────────────────────────────

    public Contact AddContact(Guid id, string? email = null, string? oid = null, IdentityPlaneMarker? plane = null,
        int stateCode = 0, CollisionFlag? flag = null)
    {
        var c = new Contact
        {
            Id = id, Email = email, Oid = oid, Plane = plane is { } p ? (int)p : null, StateCode = stateCode,
            Flag = flag, Version = NextVersion(),
        };
        Contacts[id] = c;
        return c;
    }

    public SystemUser AddSystemUser(Guid id, Guid? oid, string? email, Guid? primaryContactId = null,
        string? domainName = null, bool disabled = false, int accessMode = 0, Guid? applicationId = null)
    {
        var u = new SystemUser
        {
            Id = id, Oid = oid, Email = email, DomainName = domainName ?? email, PrimaryContactId = primaryContactId,
            IsDisabled = disabled, AccessMode = accessMode, ApplicationId = applicationId, Version = NextVersion(),
            FirstName = "First", LastName = email?.Split('@')[0] ?? "Last",
        };
        SystemUsers[id] = u;
        return u;
    }

    public IEnumerable<Contact> ContactsBoundTo(Guid oid)
    {
        lock (_gate)
        {
            return Contacts.Values.Where(c => OidEquals(c.Oid, oid)).ToList();
        }
    }

    // ── Reads ──────────────────────────────────────────────────────────────────────────────────────

    public Task<ContactLookup> FindContactsByOidAsync(Guid oid, CancellationToken ct)
    {
        lock (_gate)
        {
            Reads.Add("oid");
            if (OidLookupStatus != LookupStatus.Read) return Task.FromResult(Failed(OidLookupStatus));
            if (FailOidLookupOnceFor.Remove(oid)) return Task.FromResult(ContactLookup.Failed);
            var rows = Contacts.Values
                .Where(c => !MaskBindingColumn && OidEquals(c.Oid, oid))
                .Take(2).Select(Row).ToArray();
            return Task.FromResult(ContactLookup.Of(rows));
        }
    }

    public Task<ContactLookup> FindActiveContactsByEmailAsync(string email, CancellationToken ct)
    {
        lock (_gate)
        {
            Reads.Add("email");
            if (EmailLookupStatus != LookupStatus.Read) return Task.FromResult(Failed(EmailLookupStatus));
            var rows = Contacts.Values
                .Where(c => c.StateCode == 0 && string.Equals(c.Email, email, StringComparison.OrdinalIgnoreCase))
                .Take(2).Select(Row).ToArray();
            return Task.FromResult(ContactLookup.Of(rows));
        }
    }

    public Task<ContactLookup> GetContactAsync(Guid contactId, CancellationToken ct)
    {
        lock (_gate)
        {
            Reads.Add("contact");
            if (FailGetContact) return Task.FromResult(ContactLookup.Failed);
            return Task.FromResult(Contacts.TryGetValue(contactId, out var c) ? ContactLookup.Of(Row(c)) : ContactLookup.Of());
        }
    }

    public Task<ReferenceLookup> FindSystemUsersLinkingAsync(IReadOnlyCollection<Guid> contactIds, CancellationToken ct)
    {
        lock (_gate)
        {
            Reads.Add("references");
            if (FailReferences) return Task.FromResult(ReferenceLookup.Failed);
            var refs = SystemUsers.Values
                .Where(u => u.PrimaryContactId is { } p && contactIds.Contains(p))
                .Select(u => new SystemUserReference(u.Id, u.Oid, u.PrimaryContactId!.Value))
                .ToArray();
            return Task.FromResult(ReferenceLookup.Of(refs));
        }
    }

    public Task<SystemUserLookup> GetSystemUserAsync(Guid systemUserId, CancellationToken ct)
    {
        lock (_gate)
        {
            Reads.Add("systemuser");
            return Task.FromResult(SystemUsers.TryGetValue(systemUserId, out var u)
                ? new SystemUserLookup(LookupStatus.Read, UserRow(u))
                : SystemUserLookup.Failed);
        }
    }

    public Task<BindingReadability> ProbeBindingReadabilityAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            Reads.Add("probe");
            var marked = Contacts.Values.Where(c => c.Plane is not null).Take(50).Select(Row).ToArray();
            return Task.FromResult(DataverseContactIdentityStore.ClassifyProbe(ContactLookup.Of(marked)));
        }
    }

    // ── Writes ─────────────────────────────────────────────────────────────────────────────────────

    public Task<StoreWriteResult> BindOidAsync(Guid contactId, string? etag, Guid oid, IdentityPlaneMarker plane, CancellationToken ct)
    {
        lock (_gate)
        {
            if (FailBind) return Task.FromResult(new StoreWriteResult(StoreWriteStatus.Failed, Error: "injected"));
            // Mirrors DataverseContactIdentityStore: no row version, no write (never an unconditional bind).
            if (DataverseContactIdentityStore.RowVersionPrecondition(etag) is null)
            {
                Writes.Add(("refused-unversioned-bind", contactId, string.Empty));
                return Task.FromResult(new StoreWriteResult(StoreWriteStatus.Failed, Error: "no row version for the bind"));
            }

            if (!Contacts.TryGetValue(contactId, out var c)) return Task.FromResult(new StoreWriteResult(StoreWriteStatus.NotFound));
            var hook = BeforeBind;
            BeforeBind = null; // one-shot
            hook?.Invoke(c);
            if (!VersionMatches(etag, c.Version)) return Task.FromResult(new StoreWriteResult(StoreWriteStatus.PreconditionFailed));
            if (KeyDefined && Contacts.Values.Any(o => o.Id != contactId && OidEquals(o.Oid, oid)))
            {
                return Task.FromResult(new StoreWriteResult(StoreWriteStatus.Failed, Error: "duplicate key"));
            }

            c.Oid = oid.ToString("D");
            c.Plane = (int)plane;
            c.Version = NextVersion();
            Writes.Add(("bind", contactId, $"{c.Oid}|{plane}"));
            return Task.FromResult(StoreWriteResult.Written);
        }
    }

    public async Task<StoreWriteResult> CreateContactForOidAsync(Guid oid, IdentityPlaneMarker plane, NewContactDetails details, CancellationToken ct)
    {
        lock (_gate)
        {
            if (FailCreate) return new StoreWriteResult(StoreWriteStatus.Failed, Error: "injected");
            if (!KeyDefined) return new StoreWriteResult(StoreWriteStatus.KeyMissing);
            if (Contacts.Values.Any(c => OidEquals(c.Oid, oid))) return new StoreWriteResult(StoreWriteStatus.PreconditionFailed);
        }

        if (BeforeCreate is not null)
        {
            await BeforeCreate(oid).ConfigureAwait(false);
        }

        lock (_gate)
        {
            // The unique index decides, atomically, at insert time — exactly one contact per oid.
            if (Contacts.Values.Any(c => OidEquals(c.Oid, oid))) return new StoreWriteResult(StoreWriteStatus.PreconditionFailed);
            var id = Guid.NewGuid();
            Contacts[id] = new Contact
            {
                Id = id, Oid = oid.ToString("D"), Plane = (int)plane, Email = details.Email,
                FirstName = details.FirstName, LastName = details.LastName, StateCode = 0, Version = NextVersion(),
            };
            Writes.Add(("create", id, $"{oid:D}|{plane}|{details.Email}|{details.LastName}"));
            return new StoreWriteResult(StoreWriteStatus.Written, id);
        }
    }

    public Task<StoreWriteResult> SetPrimaryContactAsync(Guid systemUserId, string? etag, Guid contactId, CancellationToken ct)
    {
        lock (_gate)
        {
            if (DataverseContactIdentityStore.RowVersionPrecondition(etag) is null)
            {
                Writes.Add(("refused-unversioned-link", systemUserId, string.Empty));
                return Task.FromResult(new StoreWriteResult(StoreWriteStatus.Failed, Error: "no row version for the link"));
            }

            if (!SystemUsers.TryGetValue(systemUserId, out var u)) return Task.FromResult(new StoreWriteResult(StoreWriteStatus.NotFound));
            if (!VersionMatches(etag, u.Version)) return Task.FromResult(new StoreWriteResult(StoreWriteStatus.PreconditionFailed));
            u.PrimaryContactId = contactId;
            u.Version = NextVersion();
            Writes.Add(("link", systemUserId, contactId.ToString("D")));
            return Task.FromResult(StoreWriteResult.Written);
        }
    }

    public Task<StoreWriteResult> WriteCollisionFlagAsync(Guid contactId, CollisionFlag flag, CancellationToken ct)
    {
        lock (_gate)
        {
            if (FailFlagWrites) return Task.FromResult(new StoreWriteResult(StoreWriteStatus.Failed, Error: "injected"));
            if (!Contacts.TryGetValue(contactId, out var c)) return Task.FromResult(new StoreWriteResult(StoreWriteStatus.NotFound));
            c.Flag = flag;
            c.Version = NextVersion();
            Writes.Add(("flag", contactId, $"{flag.Reason}|{flag.CollidingOid:D}|{flag.CollidingPlane}"));
            return Task.FromResult(StoreWriteResult.Written);
        }
    }

    public Task<StoreWriteResult> ClearCollisionFlagAsync(Guid contactId, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!Contacts.TryGetValue(contactId, out var c)) return Task.FromResult(new StoreWriteResult(StoreWriteStatus.NotFound));
            c.Flag = null;
            c.Version = NextVersion();
            Writes.Add(("clear", contactId, string.Empty));
            return Task.FromResult(StoreWriteResult.Written);
        }
    }

    // ── Scans ──────────────────────────────────────────────────────────────────────────────────────

    public Task<StorePage<SystemUserIdentityRow>> ScanInteractiveSystemUsersAsync(string? continuation, CancellationToken ct)
    {
        lock (_gate)
        {
            Reads.Add("scan-users");
            if (FailSystemUserScan)
            {
                return Task.FromResult(new StorePage<SystemUserIdentityRow>(LookupStatus.Failed, Array.Empty<SystemUserIdentityRow>(), null, "injected"));
            }

            var all = SystemUsers.Values
                .Where(u => !u.IsDisabled && u.ApplicationId is null && u.AccessMode is 0 or 1 or 2)
                .OrderBy(u => u.Id).ToList();
            return Task.FromResult(Page(all, continuation, UserRow));
        }
    }

    public Task<StorePage<ContactBindingRow>> ScanFlaggedContactsAsync(string? continuation, CancellationToken ct)
    {
        lock (_gate)
        {
            Reads.Add("scan-flags");
            var all = Contacts.Values.Where(c => c.Flag is not null).OrderBy(c => c.Id).ToList();
            return Task.FromResult(Page(all, continuation, Row));
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────

    private StorePage<TOut> Page<TIn, TOut>(List<TIn> all, string? continuation, Func<TIn, TOut> map)
    {
        var skip = continuation is null ? 0 : int.Parse(continuation, System.Globalization.CultureInfo.InvariantCulture);
        var page = all.Skip(skip).Take(ScanPageSize).Select(map).ToList();
        var next = skip + ScanPageSize < all.Count ? (skip + ScanPageSize).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        return new StorePage<TOut>(LookupStatus.Read, page, next);
    }

    private ContactBindingRow Row(Contact c)
        => new(c.Id, c.StateCode, MaskBindingColumn ? null : c.Oid, c.Plane, $"W/\"{c.Version}\"", c.Email, c.Flag);

    private SystemUserIdentityRow UserRow(SystemUser u)
        => new(u.Id, u.Oid, u.Email, u.DomainName, u.PrimaryContactId, $"W/\"{u.Version}\"",
            u.PrimaryContactId is { } p && Contacts.TryGetValue(p, out var linked) ? Row(linked) : null,
            u.FirstName, u.LastName);

    private static ContactLookup Failed(LookupStatus status)
        => status == LookupStatus.ColumnMissing ? ContactLookup.ColumnMissing : ContactLookup.Failed;

    private static bool VersionMatches(string? etag, int version)
        => etag == $"W/\"{version}\"";

    private static bool OidEquals(string? stored, Guid oid)
        => stored is not null && Guid.TryParse(stored, out var g) && g == oid;

    private int NextVersion() => Interlocked.Increment(ref _version);
}
