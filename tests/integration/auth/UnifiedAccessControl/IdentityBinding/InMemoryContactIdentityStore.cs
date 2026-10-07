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
///   <item>the schema is owner round 4 item 4's B2: the BINDING (<see cref="Contact.Oid"/>,
///     <c>sprk_externalobjectid</c>) is field-secured, and the platform's uniqueness lives on a separate UNSECURED
///     mirror (<see cref="Contact.KeyMirror"/>, <c>sprk_externalobjectidkey</c>), whose alternate key (when
///     <see cref="KeyDefined"/>) is unique among non-null values. A create is create-only on the MIRROR (412 when
///     any contact — of any state — holds the oid there), and a bind writes binding and mirror together and fails
///     with the duplicate-key fault (<see cref="StoreWriteStatus.KeyConflict"/>) when ANOTHER contact holds the
///     oid in its mirror. Seeding a binding seeds the matching mirror (schema step (a) copies every existing
///     binding into it); <c>keyMirror</c> seeds a squatted or half-cleared one. This is a combination Dataverse
///     allows — the key column is not field-secured — unlike the pre-B2 double, which modelled a key on the
///     secured column;</item>
///   <item><see cref="MaskBindingColumn"/> reproduces field-level-security masking of the BINDING only: the oid
///     comes back null in rows AND is treated as null in filters (Dataverse substitutes null — documented). The
///     mirror is not secured, so it is never masked and the unique index still applies.</item>
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

        /// <summary><c>sprk_externalobjectidkey</c> — the unsecured uniqueness mirror (B2).</summary>
        public string? KeyMirror { get; set; }

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

    /// <summary>
    /// Runs inside EVERY flag write or clear before its <c>If-Match</c> check — to stage a concurrent writer, e.g.
    /// another identity's party appended between the job's scan and its clear. The hook decides which row and how
    /// often it fires. Call <see cref="Touch"/> inside it to move the row version, as a real concurrent write would.
    /// </summary>
    public Action<Contact>? BeforeFlagWrite { get; set; }

    /// <summary>The masking probe itself cannot be read (<see cref="BindingReadability.Failed"/>).</summary>
    public bool FailProbe { get; set; }

    /// <summary>The flagged-contact scan returns rows WITHOUT a row version (a read that carried no etag).</summary>
    public bool ScanFlaggedWithoutETags { get; set; }

    /// <summary>
    /// Runs after EVERY read is recorded, with the read's name (the <see cref="Reads"/> entry) — to stage a
    /// concurrent writer at a precise point, e.g. between the job's flag scan and its clear.
    /// </summary>
    public Action<string>? AfterRead { get; set; }

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
            FailReferences = FailGetContact = FailBind = FailCreate = FailFlagWrites = FailSystemUserScan = FailProbe = false;
            ScanFlaggedWithoutETags = false;
            FailOidLookupOnceFor.Clear();
            BeforeCreate = null;
            BeforeBind = null;
            BeforeFlagWrite = null;
            AfterRead = null;
        }
    }

    // ── Seeding ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a contact. A well-formed binding seeds the matching uniqueness mirror (lowercase "D"), as schema step
    /// (a) copies every existing binding into it and every BFF write sets both. <paramref name="keyMirror"/> seeds
    /// the mirror explicitly instead — a squatted or half-cleared one; <paramref name="deriveKeyMirror"/> = false
    /// seeds a binding with NO mirror (a binding an operator wrote by hand).
    /// </summary>
    public Contact AddContact(Guid id, string? email = null, string? oid = null, IdentityPlaneMarker? plane = null,
        int stateCode = 0, CollisionFlag? flag = null, string? keyMirror = null, bool deriveKeyMirror = true)
    {
        var mirror = keyMirror
            ?? (deriveKeyMirror && oid is not null && Guid.TryParse(oid.Trim(), out var g) && g != Guid.Empty
                ? g.ToString("D")
                : null);
        var c = new Contact
        {
            Id = id, Email = email, Oid = oid, KeyMirror = mirror, Plane = plane is { } p ? (int)p : null,
            StateCode = stateCode, Flag = flag, Version = NextVersion(),
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
            AfterRead?.Invoke("oid");
            if (OidLookupStatus != LookupStatus.Read) return Task.FromResult(Failed(OidLookupStatus));
            if (FailOidLookupOnceFor.Remove(oid)) return Task.FromResult(ContactLookup.Failed);
            var rows = Contacts.Values
                .Where(c => !MaskBindingColumn && OidEquals(c.Oid, oid))
                .Take(2).Select(Row).ToArray();
            return Task.FromResult(ContactLookup.Of(rows));
        }
    }

    public Task<ContactLookup> FindContactsByKeyMirrorAsync(Guid oid, CancellationToken ct)
    {
        lock (_gate)
        {
            Reads.Add("key-mirror");
            AfterRead?.Invoke("key-mirror");
            var rows = Contacts.Values.Where(c => OidEquals(c.KeyMirror, oid)).Take(2).Select(Row).ToArray();
            return Task.FromResult(ContactLookup.Of(rows));
        }
    }

    public Task<ContactLookup> FindActiveContactsByEmailAsync(string email, CancellationToken ct)
    {
        lock (_gate)
        {
            Reads.Add("email");
            AfterRead?.Invoke("email");
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
            AfterRead?.Invoke("contact");
            if (FailGetContact) return Task.FromResult(ContactLookup.Failed);
            return Task.FromResult(Contacts.TryGetValue(contactId, out var c) ? ContactLookup.Of(Row(c)) : ContactLookup.Of());
        }
    }

    public Task<ReferenceLookup> FindSystemUsersLinkingAsync(IReadOnlyCollection<Guid> contactIds, CancellationToken ct)
    {
        lock (_gate)
        {
            Reads.Add("references");
            AfterRead?.Invoke("references");
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
            AfterRead?.Invoke("systemuser");
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
            AfterRead?.Invoke("probe");
            if (FailProbe) return Task.FromResult(BindingReadability.Failed);
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

            // The unique index is on the MIRROR (B2), and it counts rows of every state: another contact holding the
            // oid there makes the platform refuse the whole write with the duplicate-key fault.
            if (KeyDefined && Contacts.Values.Any(o => o.Id != contactId && OidEquals(o.KeyMirror, oid)))
            {
                Writes.Add(("refused-duplicate-key-bind", contactId, oid.ToString("D")));
                return Task.FromResult(new StoreWriteResult(StoreWriteStatus.KeyConflict, Error: "0x80060892 duplicate key"));
            }

            c.Oid = oid.ToString("D");
            c.KeyMirror = oid.ToString("D");
            c.Plane = (int)plane;
            c.Version = NextVersion();
            Writes.Add(("bind", contactId, $"{c.Oid}|{plane}"));
            return Task.FromResult(StoreWriteResult.Written);
        }
    }

    private static StoreWriteResult DuplicateMirrorKey()
        => new(StoreWriteStatus.KeyConflict, Error: "0x80060892 Entity Key External Object ID (unique) violated");

    public async Task<StoreWriteResult> CreateContactForOidAsync(Guid oid, IdentityPlaneMarker plane, NewContactDetails details, CancellationToken ct)
    {
        lock (_gate)
        {
            if (FailCreate) return new StoreWriteResult(StoreWriteStatus.Failed, Error: "injected");
            if (!KeyDefined) return new StoreWriteResult(StoreWriteStatus.KeyMissing);
            if (Contacts.Values.Any(c => OidEquals(c.KeyMirror, oid))) return DuplicateMirrorKey();
        }

        if (BeforeCreate is not null)
        {
            await BeforeCreate(oid).ConfigureAwait(false);
        }

        lock (_gate)
        {
            // The unique index on the MIRROR decides, atomically, at insert time — exactly one contact per oid. The
            // POST carries the binding and the mirror in one body: both land together, or the key refuses the row
            // with the platform's duplicate fault (412 0x80060892, verified live 2026-10-02).
            if (Contacts.Values.Any(c => OidEquals(c.KeyMirror, oid))) return DuplicateMirrorKey();
            var id = Guid.NewGuid();
            Contacts[id] = new Contact
            {
                Id = id, Oid = oid.ToString("D"), KeyMirror = oid.ToString("D"), Plane = (int)plane, Email = details.Email,
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

    public Task<StoreWriteResult> WriteCollisionFlagAsync(Guid contactId, CollisionFlag flag, string? etag, CancellationToken ct)
    {
        lock (_gate)
        {
            if (FailFlagWrites) return Task.FromResult(new StoreWriteResult(StoreWriteStatus.Failed, Error: "injected"));
            if (!Contacts.TryGetValue(contactId, out var c)) return Task.FromResult(new StoreWriteResult(StoreWriteStatus.NotFound));
            RunBeforeFlagWrite(c);
            // Mirrors DataverseContactIdentityStore: conditional on the version when there is one, "*" otherwise.
            if (DataverseContactIdentityStore.RowVersionPrecondition(etag) is not null && !VersionMatches(etag, c.Version))
            {
                return Task.FromResult(new StoreWriteResult(StoreWriteStatus.PreconditionFailed));
            }

            c.Flag = flag;
            c.Version = NextVersion();
            Writes.Add(("flag", contactId, $"{flag.Reason}|{flag.CollidingOid:D}|{flag.CollidingPlane}|parties={flag.Parties.Count}"));
            return Task.FromResult(StoreWriteResult.Written);
        }
    }

    public Task<StoreWriteResult> ClearCollisionFlagAsync(Guid contactId, string? etag, CancellationToken ct)
    {
        lock (_gate)
        {
            // Mirrors DataverseContactIdentityStore: no row version, no clear (clearing is the fail-open direction).
            if (DataverseContactIdentityStore.RowVersionPrecondition(etag) is null)
            {
                Writes.Add(("refused-unversioned-clear", contactId, string.Empty));
                return Task.FromResult(new StoreWriteResult(StoreWriteStatus.Failed, Error: "no row version for the clear"));
            }

            if (!Contacts.TryGetValue(contactId, out var c)) return Task.FromResult(new StoreWriteResult(StoreWriteStatus.NotFound));
            RunBeforeFlagWrite(c);
            if (!VersionMatches(etag, c.Version)) return Task.FromResult(new StoreWriteResult(StoreWriteStatus.PreconditionFailed));
            c.Flag = null;
            c.Version = NextVersion();
            Writes.Add(("clear", contactId, string.Empty));
            return Task.FromResult(StoreWriteResult.Written);
        }
    }

    private void RunBeforeFlagWrite(Contact c) => BeforeFlagWrite?.Invoke(c);

    // ── Scans ──────────────────────────────────────────────────────────────────────────────────────

    public Task<StorePage<SystemUserIdentityRow>> ScanInteractiveSystemUsersAsync(string? continuation, CancellationToken ct)
    {
        lock (_gate)
        {
            Reads.Add("scan-users");
            AfterRead?.Invoke("scan-users");
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
            AfterRead?.Invoke("scan-flags");
            var all = Contacts.Values.Where(c => c.Flag is not null).OrderBy(c => c.Id).ToList();
            return Task.FromResult(Page(all, continuation,
                c => ScanFlaggedWithoutETags ? Row(c) with { ETag = null } : Row(c)));
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
        => new(c.Id, c.StateCode, MaskBindingColumn ? null : c.Oid, c.Plane, $"W/\"{c.Version}\"", c.Email, c.Flag,
            c.KeyMirror);

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
