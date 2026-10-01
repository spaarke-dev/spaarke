# Task 141 — the user↔contact link contract

> **Owner**: `unified-access-control-r2` task 141 (2026-10-01). **Consumers waiting on it**:
> `spaarkeai-word-add-in-r1` task 083 (Office To Do defaults `sprk_assignedto` to the caller's contact), UAC-r2
> tasks 152 (briefing "assigned to" matching) and 013 (A-18 closure), and the C9 Assigned-To auto-grant work.
> Changes to this contract are made HERE and announced to those consumers — do not fork a second rule.
>
> **State of the world when this was written**: the code is on branch `task/uac-r2-141` (not merged). The Dataverse
> schema, the `acct` claim, the tenant setting and the job's writes are **pending manual gates** (see
> `task-141-identity-binding.md` §8). Until those land, dev behaves as today: 1 of 11 interactive systemusers has
> `sprk_primarycontact`.

---

## 1. The columns

| Table.column | Type | Meaning | Who may write |
|---|---|---|---|
| `contact.sprk_externalobjectid` | Text(100) | **The binding.** The Entra object id (`oid`) of the ONE identity this contact signs in as — a CIAM oid OR a workforce oid (one field, both planes; oids are globally unique). Always lowercase "D" GUID (`xxxxxxxx-xxxx-…`). | BFF only (field-secured) |
| `contact.sprk_identityplane` | Choice (global `sprk_identityplane`) | Which plane wrote the binding: `100000000` External (CIAM), `100000001` Workforce. Written together with the oid, never alone. | BFF only |
| `systemuser.sprk_primarycontact` | Lookup → contact (nav. property `sprk_PrimaryContact`) | **The link.** The contact that represents this licensed user. | BFF only (field-secured) |
| `contact.sprk_identitycollisionon` | Date/time | When a collision was flagged. **Empty = no open collision.** | BFF only |
| `contact.sprk_identitycollisionoid` | Text(100) | The oid of the identity that collided with this contact. | BFF only |
| `contact.sprk_identitycollisionplane` | Choice (`sprk_identityplane`) | The plane of the colliding identity. | BFF only |
| `contact.sprk_identitycollisionreason` | Choice (global `sprk_identitycollisionreason`) | Why — `100000000` bound to a different oid · `…01` email on >1 contact · `…02` oid on >1 contact · `…03` linked to another user · `…04` binding unreadable · `…05` guest email matches a contact · `…06` linked contact bound to a different oid · `…07` link and binding name different contacts · `…08` linked contact inactive · `…09` guest linked to an unbound contact · `…10` invite matches a workforce contact | BFF only |

Uniqueness: alternate key `sprk_ExternalObjectIdKey` on `contact(sprk_externalobjectid)` — **exactly one contact
per oid**. Nulls are not enforced, so unbound contacts are unaffected.

Field-level security: profile **"Spaarke Identity Link Readers"** (Read) is associated with every business unit's
default team, so every user keeps READING both secured fields; profile **"Spaarke Identity Link Writers"** (Read +
Create + Update) holds only the BFF application user(s), associated explicitly. A client write of either field
fails.

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
| **Inline, workforce sign-in** — `WorkforcePrincipalResolver` | A licensed user resolves (Teams/SPA) with no contact: `EnsureSystemUserLinkAsync(systemUserId, applyWrites: true)`. A Type-2 (unlicensed) member's first sign-in: `ResolveWorkforceCallerAsync`. | Bind / create / link, or a flag. The new link takes effect **on the same request** (`IIdentityNormalizationService.InvalidateAsync`). A user whose link cannot be made is not re-attempted for 10 minutes. |
| **Registration** — `RegistrationDataverseService.CreateSystemUserAsync` → `LinkContactForNewSystemUserAsync` | When the BFF creates a systemuser (demo provisioning) | Bind / create / link, in the **same** `targetDataverseUrl` environment as the systemuser. |
| **Reconciliation job** — `identity-link-reconciliation` (ADR-036 `IScheduledJob`, every 5 min) | Every enabled interactive systemuser (`isdisabled = false`, `applicationid` null, `accessmode` 0/1/2) | Pass 1: verify / link / bind / create, or flag. Pass 2 (only after a complete pass 1): clear flags whose collision no longer holds. **Report-only until `IdentityLink:Reconciliation:WritesEnabled = true`.** |
| **Invite** — `/invite`, `/invite-and-grant` | An external user is invited | Binds the new CIAM oid (plane External) onto the contact. Refuses (409) and flags an email that matches a workforce-bound contact or a contact a systemuser links to. |
| **CIAM first login** — `CiamContactPrincipalStrategy` | The invite's oid write had failed | Repair bind only (never creates a contact). |

Order of a systemuser link decision (the full table is `task-141-identity-binding.md` §3):

- **Has a link** → verify it. A link is never re-pointed and never cleared. If it collides, flag the contact and
  leave the link exactly as it is.
- **No link** → the contact bound to the user's oid, else the ONE active unbound contact carrying the user's
  directory email (`internalemailaddress`; not for a `#EXT#` guest), else a new contact keyed by the oid. Anything
  ambiguous or bound elsewhere is a collision: no write except the flag.

## 4. Refusal and flag semantics

- A collision is **refused AND flagged**. A log line alone is not a flag.
- A flag is written only onto a contact that carries no open flag — a retried sign-in cannot turn the deny path
  into a stream of writes.
- Only the reconciliation job clears a flag, and only when its collision no longer holds. Operators do not clear
  flags by hand (procedure: `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §6.5.3).
- An unreadable binding (marker set but oid absent — the FLS-masking signature — or a malformed oid) is never
  treated as "unbound". It denies.
- Sign-in denials carry their own reason codes (`sdap.access.deny.contact_bound_to_different_oid`,
  `…contact_email_ambiguous`, `…contact_oid_ambiguous`, `…contact_inactive`, `…contact_binding_unreadable`,
  `…binding_column_missing`, `…binding_column_masked`, `…workforce_acct_claim_missing`, …). Invites return HTTP 409
  with `reasonCode` `sdap.access.invite.workforce_bound_contact` / `…contact_linked_to_internal_user` /
  `…email_ambiguous`.

## 5. How a consumer reads the link

**Rule: read the link, never match by email, never pick the first of two.** A null contact is a normal outcome
every consumer must handle.

| You have | Use | You get |
|---|---|---|
| A **systemuser id** (server) | `IIdentityNormalizationService.ResolveAsync(systemUserId, ct)` → `PersonIdentity.ContactId` | `sprk_primarycontact` (honoured as-is, including a flagged kept link); when absent, the ONE active contact bound to the user's oid; otherwise `null`. Cached 10 min; writers invalidate. **This is the value membership (`MembershipResolverService`) and the Assigned-To / briefing matching see.** |
| The **caller's claims**, and you need proof the contact IS this identity ("assign it to me") | `ICallerContactResolver.ResolveAsync(principal, ct)` | Only the active contact whose `sprk_externalobjectid` = the caller's oid. Unresolved (`no-matching-contact`, `ambiguous-binding`, `lookup-failed`) otherwise — it does **not** follow a flagged kept link. |
| A **workforce request principal** (Teams/SPA) | `WorkforcePrincipalResolution.ContactId` | The contact the resolver derived/bound for this request. |
| **Client code** running as the signed-in user | `systemuser._sprk_primarycontact_value` (Web API) | Read only. Writing it fails (FLS). |

Do **not**: query `contact.azureactivedirectoryobjectid`; resolve a person's contact by email; write either
secured field from a client; treat two contacts on one oid as a choice.

## 6. For `spaarkeai-word-add-in-r1` task 083 specifically

- **Recommended reader**: resolve the caller's `systemuserid` with OfficeService's existing task-067 resolver, then
  `IIdentityNormalizationService.ResolveAsync(systemUserId, ct).ContactId`. That is the same value task 152's
  briefing matcher and the membership resolver use, so the To Do you stamp is the To Do the briefing finds —
  including for a user whose existing link is flagged (Ralph in dev).
- **Ambiguity rule**: two contacts carrying one oid → `null` (never one of them). Inactive contact → `null`.
- **Null** → leave `sprk_assignedto` empty and log the warning, exactly as your constraint already says.
- **Do not** call `ContactIdentityBinder` from the Office path: linking is the job's and the sign-in resolver's
  responsibility, and a To Do create must not become an identity write.
- **Availability** (your escalation trigger "link unavailable for most users"): dev today has 1 of 11 interactive
  systemusers linked. After the gates in `task-141-identity-binding.md` §8 run (schema → BFF deploy → one
  report-only job run reviewed → writes enabled), the step-0 data predicts **9 of 11** with a contact: 1 bind +
  7 creates linked fresh, Ralph's kept (flagged) link, and 2 flagged without a contact (eyal.iffergan, the
  hotmail guest) until an operator resolves them. Re-measure after the gate rather than relying on this forecast.
