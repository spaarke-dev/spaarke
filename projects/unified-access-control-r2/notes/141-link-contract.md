# Task 141 — the user↔contact link contract

> **Owner**: `unified-access-control-r2` task 141 (2026-10-01). **Consumers waiting on it**:
> `spaarkeai-word-add-in-r1` task 083 (Office To Do defaults `sprk_assignedto` to the caller's contact), UAC-r2
> tasks 152 (briefing "assigned to" matching) and 013 (A-18 closure), and the C9 Assigned-To auto-grant work.
> Changes to this contract are made HERE and announced to those consumers — do not fork a second rule.
>
> **State of the world when this was written**: the code is on branch `task/uac-r2-141-f3` (not merged; earlier
> rounds `task/uac-r2-141`, `-f1`, `-f2`; third fix round 2026-10-02). The Dataverse schema, the `acct` claim, the
> tenant setting and the job's writes are **pending manual gates the owner has approved** (see
> `task-141-identity-binding.md` §8; the main session runs them after deploy). Until those land, dev behaves as
> today: 1 of 11 interactive systemusers has `sprk_primarycontact`.
>
> ✅ **The uniqueness-vs-field-lock question is DECIDED** (owner round 4 item 4, 2026-10-01 — "B2"): field-level
> security stays on the binding `contact.sprk_externalobjectid`; the platform's "one contact per oid" moves to an
> unsecured mirror column `contact.sprk_externalobjectidkey` (§1.1). Nothing a consumer reads changes.

---

## 1. The columns

| Table.column | Type | Meaning | Who may write |
|---|---|---|---|
| `contact.sprk_externalobjectid` | Text(100) | **The binding.** The Entra object id (`oid`) of the ONE identity this contact signs in as — a CIAM oid OR a workforce oid (one field, both planes; oids are globally unique). Always lowercase "D" GUID (`xxxxxxxx-xxxx-…`). **The only column anyone resolves a contact by.** | BFF only (field-secured) |
| `contact.sprk_externalobjectidkey` | Text(100), **not** field-secured | **The uniqueness mirror.** The same oid as the binding, written by the BFF in the SAME request as every bind and create; carries the alternate key `sprk_ExternalObjectIdUniqueKey`. Carries NO identity — **never read it to resolve anyone**. | BFF (any user with contact Write *can* set it; §1.1 says what that buys them) |
| `contact.sprk_identityplane` | Choice (global `sprk_identityplane`) | Which plane wrote the binding: `100000000` External (CIAM), `100000001` Workforce. Written together with the oid, never alone. | BFF only |
| `systemuser.sprk_primarycontact` | Lookup → contact (nav. property `sprk_PrimaryContact`) | **The link.** The contact that represents this licensed user. | BFF only (field-secured) |
| `contact.sprk_identitycollisionon` | Date/time | When a collision was flagged. **Empty = no open collision.** | BFF only |
| `contact.sprk_identitycollisionoid` | Text(100) | The oid of the identity that collided with this contact. | BFF only |
| `contact.sprk_identitycollisionplane` | Choice (`sprk_identityplane`) | The plane of the colliding identity. | BFF only |
| `contact.sprk_identitycollisionreason` | Choice (global `sprk_identitycollisionreason`) | Why — `100000000` bound to a different oid · `…01` email on >1 contact · `…02` oid on >1 contact · `…03` linked to another user · `…04` binding unreadable · `…05` guest email matches a contact · `…06` linked contact bound to a different oid · `…07` link and binding name different contacts · `…08` linked contact inactive · `…09` guest linked to an unbound contact · `…10` invite matches a workforce contact · `…11` key mirror held by another contact (B2) | BFF only |
| `contact.sprk_identitycollisionparties` | Multi-line text (4000), JSON | **Every** identity that collided with this contact: `[{"oid","plane","reason","on"}]`, the first one being the four summary columns above (which is what the operator view lists). Added by the verifier fix round (finding 3) so a second identity's collision is recorded rather than swallowed. | BFF only — do not edit by hand (an unreadable value keeps the flag until an operator clears it) |

### 1.1 Uniqueness and the field lock — owner decision B2 (round 4 item 4, 2026-10-01)

Microsoft documents that a field-secured column cannot be an alternate key ("Attributes must not have field-level
security applied" — *Work with alternate keys*), so the two properties live on two columns:

- **The write lock** — field-level security on `contact.sprk_externalobjectid` and `systemuser.sprk_primarycontact`.
  Profile **"Spaarke Identity Link Readers"** (Read) is associated with every business unit's default team, so every
  user keeps READING both secured fields; profile **"Spaarke Identity Link Writers"** (Read + Create + Update) holds
  only the BFF application user(s), associated explicitly. A client write fails.
- **Exactly one contact per oid** — the alternate key `sprk_ExternalObjectIdUniqueKey` on the unsecured mirror
  `contact.sprk_externalobjectidkey`. The BFF writes binding and mirror together, in one request, on every bind and
  every create (the create is a `POST contacts` carrying the binding AND the mirror; a second contact for the same
  oid is refused with 412 `0x80060892`). **Not** a keyed `PATCH contacts(sprk_externalobjectidkey='<oid>')` with
  `If-None-Match: *`: Dataverse answers that with 404 `0x80060891` and creates nothing (found by G-6 live, fixed in
  PR #1097, 2026-10-02). The platform's unique index — which counts rows of every state — therefore admits one contact
  per oid, **including when two first sign-ins race** (the loser's create is refused and it resolves the winner's
  contact by the binding). The schema step copies the existing bindings into the mirror before creating the key.
- **What the unsecured mirror allows** — a user with contact Write can put someone's oid into a mirror. That can
  only DENY SERVICE to that identity: its bind or create is refused by the index, the BFF denies
  `sdap.access.deny.contact_key_conflict` and flags the holder (reason `…11` "key mirror held by another
  contact"). It can never make a contact resolve as someone else — nothing resolves by the mirror.
- ✅ Unchanged: **two contacts carrying one oid in the binding always DENY** (`contact_oid_ambiguous`), on every
  plane and in every reader below — no path picks one of two. (Possible only for a binding written by hand without
  its mirror.) Where the key is not yet defined, contact **creation denies** (`contact_create_unavailable`) rather
  than risk two contacts.

All of it is applied by `scripts/Set-ContactIdentityBindingSchema.ps1` (gate G-1 in the notes).

`contact.azureactivedirectoryobjectid` is **not** part of this contract. It does not exist in dev, nothing writes
it, and a source guard (`ContactAadObjectIdColumnGuardTests`) fails the build if any query against `contact` names
it again.

## 2. What "linked" means

A systemuser **S** is **linked** to contact **C** when ALL of:

1. `S.sprk_primarycontact = C`
2. `C.sprk_externalobjectid` parses to the same GUID as `S.azureactivedirectoryobjectid` (Guid compare, never string)
3. `C.statecode = 0` (active)

Three other states exist and consumers must expect them:

| State | Example in dev | What a consumer sees |
|---|---|---|
| **Existing link kept, flagged** — `S.sprk_primarycontact = C` but C is bound to a different oid (or otherwise collides) | ralph.schroeder@spaarke.com → 8e9918a9 (bound to a CIAM oid) | The link is honoured as it is (owner decision I2 = (1): never cleared, never re-pointed automatically). C carries an open flag. |
| **Flagged, no link** | eyal.iffergan@spaarke.com, ralph.schroeder@hotmail.com (guest) | No contact. The colliding contact carries an open flag. |
| **Not yet reconciled** | any user created since the last job run | No link yet; the job links within one cycle (§4). |

## 3. Who writes, and when

Every write is made by the BFF, app-only (S2S), through ONE component: `ContactIdentityBinder`
(`Infrastructure/ExternalAccess/ContactIdentityBinder.cs`) over `IContactIdentityStore` (Dataverse Web API). No
plugin, no client, no `Xrm.WebApi` (DATAVERSE-WRITE-PATH-ARCHITECTURE WP-1/WP-3; the FLS lock enforces it).

| Writer | When | Writes |
|---|---|---|
| **Inline, workforce sign-in** — `WorkforcePrincipalResolver` | A licensed user resolves (Teams/SPA) with no contact: `EnsureSystemUserLinkAsync(systemUserId, applyWrites: true)` — **only when `IdentityLink:Reconciliation:WritesEnabled = true`** (the job's rollout switch; verifier finding 4). A Type-2 (unlicensed) member's first sign-in: `ResolveWorkforceCallerAsync` (not gated). | Bind / create / link, or a flag. The new link takes effect **on the same request** (`IIdentityNormalizationService.InvalidateAsync`). A user whose link cannot be made is not re-attempted for 10 minutes. With the switch off a licensed user is simply not linked inline (no read, no write) — the report-only job shows what would happen. |
| **Registration** — `RegistrationDataverseService.CreateSystemUserAsync` → `LinkContactForNewSystemUserAsync` | When the BFF creates a systemuser (demo provisioning) | Bind / create / link, in the **same** `targetDataverseUrl` environment as the systemuser. A link that does not land there (fault, deny, lost race, collision) is **re-decided by the reconciliation job's next run**, which reconciles every environment this BFF provisions into (third fix round — the second round's "not retried" gap is closed). |
| **Reconciliation job** — `identity-link-reconciliation` (ADR-036 `IScheduledJob`, every 5 min) | Every enabled interactive systemuser (`isdisabled = false`, `applicationid` null, `accessmode` 0/1/2) in the BFF's own `Dataverse:ServiceUrl` AND in every provisioning target (`DATAVERSE_URL` and every active `sprk_dataverseenvironment` row) | Per environment — pass 1: verify / link / bind / create, or flag. Pass 2 (only after a complete, untruncated pass 1 with a readable masking probe): re-evaluate every recorded party; drop the ones whose collision no longer holds, and clear the flag only when none holds AND no systemuser collided with the contact this run. One environment's failure fails the run, never the others. **Report-only until `IdentityLink:Reconciliation:WritesEnabled = true`, in every environment.** |
| **Invite** — `/invite`, `/invite-and-grant` | An external user is invited | Binds the new CIAM oid (plane External) onto the contact. Refuses (409) and flags an email that matches a workforce-bound contact or a contact a systemuser links to. `/invite-and-grant` makes this ONE resolution before task 139's grant checks (never-lower, No Access list) and hands the same resolution to onboarding, so the checks judge exactly the contact that is provisioned (merge with 138/139, 2026-10-02). |
| **CIAM first login** — `CiamContactPrincipalStrategy` | The invite's oid write had failed | Repair bind only (never creates a contact). |

Order of a systemuser link decision (the full table is `task-141-identity-binding.md` §3):

- **Has a link** → verify it. A link is never re-pointed and never cleared. If it collides, flag the contact and
  leave the link exactly as it is.
- **No link** → the contact bound to the user's oid, else the ONE active unbound contact carrying the user's
  directory email (`internalemailaddress`; not for a `#EXT#` guest), else a new contact keyed by the oid. Anything
  ambiguous or bound elsewhere is a collision: no write except the flag.

## 4. Refusal and flag semantics

- A collision is **refused AND flagged**. A log line alone is not a flag.
- A flag records **every colliding identity** (a party = oid + plane + reason + time). A party is written at most
  once per contact — a retried sign-in cannot turn the deny path into a stream of writes — but a DIFFERENT
  identity colliding with an already-flagged contact is added, so its collision does not disappear when the first
  one is resolved (verifier finding 3). Appends are conditional on the row version; a lost race re-reads and
  appends again, so no party overwrites another.
- The reconciliation job drops a party whose collision no longer holds and clears the flag only when no party
  holds; a prune or clear is conditional on the row version the verdict was made on. Operators resolve the
  collision, not the flag (procedure: `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §6.5.3). Two exceptions an operator
  clears by hand after resolving: a flag at 20 parties (a later party was not recorded) and a flag whose parties
  column was edited into something unreadable.
- An unreadable binding (marker set but oid absent — the FLS-masking signature — or a malformed oid) is never
  treated as "unbound". It denies.
- Sign-in denials carry their own reason codes (`sdap.access.deny.contact_bound_to_different_oid`,
  `…contact_email_ambiguous`, `…contact_oid_ambiguous`, `…contact_inactive`, `…contact_binding_unreadable`,
  `…binding_column_missing`, `…binding_column_masked`, `…contact_key_conflict` (B2: another contact holds the oid's
  unique-index slot), `…workforce_acct_claim_missing`, …). Invites return HTTP 409
  with `reasonCode` `sdap.access.invite.workforce_bound_contact` / `…contact_linked_to_internal_user` /
  `…email_ambiguous` / `…contact_binding_unreadable`. An invite whose contact lookup (email, or the systemuser
  reference check) could not be read is NOT a refusal: HTTP **503** with `reasonCode`
  `sdap.access.invite.contact_lookup_failed` and the message "…could not be looked up. Nothing was created; try
  again." — nothing is created, bound, flagged or granted, on both `/invite` and `/invite-and-grant` (second fix
  round, verifier finding 5; it used to be folded into the generic 500).

## 5. How a consumer reads the link

**Rule: read the link, never match by email, never pick the first of two.** A null contact is a normal outcome
every consumer must handle.

| You have | Use | You get |
|---|---|---|
| A **systemuser id** (server) | `IIdentityNormalizationService.ResolveAsync(systemUserId, ct)` → `PersonIdentity.ContactId` | `sprk_primarycontact` (honoured as-is, including a flagged kept link); when absent, the ONE contact BOUND (`sprk_externalobjectid`, never the mirror) to the user's oid if it is active — read over EVERY statecode, so two contacts on the oid (even one active plus one inactive) give `null`, exactly as the binder denies them (second fix round, verifier finding 6); otherwise `null`. Cached 10 min; writers invalidate. **This is the value membership (`MembershipResolverService`) and the Assigned-To / briefing matching see.** ⚠️ Stated explicitly (verifier finding 5, owner decision I2 = (1)): a flagged kept link is honoured EVERYWHERE this value is used — including `AccessibleRecordSetService`, whose contact-grant term then loads that contact's external grants into the licensed user's accessible set. In dev that is ralph.schroeder@spaarke.com → 8e9918a9, a contact bound to a CIAM oid: his set includes that CIAM identity's grants. This is the mixing the POML's rejection of option (b) warned about; it is pre-existing behaviour (the link predates task 141) that the owner chose to keep rather than clear the link, and it ends when an operator resolves the collision. |
| The **caller's claims**, and you need proof the contact IS this identity ("assign it to me") | `ICallerContactResolver.ResolveAsync(principal, ct)` | Only the ONE contact whose `sprk_externalobjectid` = the caller's oid, when it is active (read over every statecode — the binder's own question). Unresolved (`no-matching-contact`, `ambiguous-binding` — two contacts in any state —, `inactive-contact`, `lookup-failed`) otherwise — it does **not** follow a flagged kept link. |
| A **workforce request principal** (Teams/SPA) | `WorkforcePrincipalResolution.ContactId` | The contact the resolver derived/bound for this request. |
| **Client code** running as the signed-in user | `systemuser._sprk_primarycontact_value` (Web API) | Read only. Writing it fails (FLS). |

Do **not**: query `contact.azureactivedirectoryobjectid`; resolve a person's contact by email OR by the
`sprk_externalobjectidkey` mirror; write either secured field from a client; treat two contacts on one oid as a
choice.

## 6. For `spaarkeai-word-add-in-r1` task 083 specifically

- **Recommended reader**: resolve the caller's `systemuserid` with OfficeService's existing task-067 resolver, then
  `IIdentityNormalizationService.ResolveAsync(systemUserId, ct).ContactId`. That is the same value task 152's
  briefing matcher and the membership resolver use, so the To Do you stamp is the To Do the briefing finds —
  including for a user whose existing link is flagged (Ralph in dev).
- **Ambiguity rule**: two contacts carrying one oid → `null` (never one of them), whatever their state — one
  active plus one inactive is ambiguous too. Inactive contact → `null`.
- **Null** → leave `sprk_assignedto` empty and log the warning, exactly as your constraint already says.
- **Do not** call `ContactIdentityBinder` from the Office path: linking is the job's and the sign-in resolver's
  responsibility, and a To Do create must not become an identity write.
- **Availability** (your escalation trigger "link unavailable for most users"): dev today has 1 of 11 interactive
  systemusers linked. After the gates in `task-141-identity-binding.md` §8 run (schema → BFF deploy → one
  report-only job run reviewed → writes enabled), the step-0 data predicts **9 of 11** with a contact: 1 bind +
  7 creates linked fresh, Ralph's kept (flagged) link, and 2 flagged without a contact (eyal.iffergan, the
  hotmail guest) until an operator resolves them. Re-measure after the gate rather than relying on this forecast.

## 7. What changed in the third fix round (2026-10-02) — for consumers

- **Nothing you read changed.** The link (`systemuser.sprk_primarycontact`), the binding
  (`contact.sprk_externalobjectid`) and every reader in §5 are as before. Do not read the new
  `sprk_externalobjectidkey` column — it is the platform's uniqueness mirror and carries no identity (§1.1).
- **Uniqueness is decided** (owner B2) and becomes live with the schema gate; criterion "exactly one contact per oid,
  including two racing first sign-ins" holds once `Set-ContactIdentityBindingSchema.ps1 -Apply` has run.
- **One new deny code**, `sdap.access.deny.contact_key_conflict`, and one new collision reason (`…11`). A consumer that
  only reads the link sees it as an ordinary "no contact" (`null`).
- **More users get linked**: the reconciliation job now also covers every environment this BFF provisions users into,
  so a demo-registered user whose link did not land at creation is linked by the next run (§3).
