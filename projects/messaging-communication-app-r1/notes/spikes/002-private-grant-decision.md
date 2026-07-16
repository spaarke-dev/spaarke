# Spike 002 — Private-Thread Grant Mechanism Decision

> **Task**: 002 (Phase-0 verification) · **Project**: messaging-communication-app-r1
> **Type**: Decision spike — no code, no build, no schema change (per POML constraints)
> **Status**: DECIDED (recommendation) — 🔔 owner sign-off advised (security-sensitive, root §6)
> **Blocks**: task 042 (privacy / internal-only / privilege enforcement) · consumed by 041 (ACS reconcile) + 050 (thread-read endpoint)
> **Date**: 2026-07-16

---

## 0. Decision boundary (what this spike does and does NOT decide)

Design §5 and §14.5 already **locked** the access model:

- **Access boundary** = the Dataverse record (`sprk_communication` / `sprk_communicationthread`). Whoever can read the record can read the message.
- **Open (record-anchored) threads** = membership derives from `MembershipResolverService` (ADR-034). **Not reopened.**
- **ACS thread membership** = a reconciled *projection* of Dataverse-derived access, never a second source of truth. **Not reopened.**
- **No new authorization engine / no third mechanism** (design §5, root §11). **Binding.**

This spike resolves **only** the one open detail: **which existing per-record sharing primitive grants a named set of participants access to a PRIVATE thread** —

- **(A) Dataverse `GrantAccess` / POA** — the platform-native per-record share (`PlaybookSharingService` precedent).
- **(B) `sprk_externalrecordaccess`-style overlay** — Spaarke-owned grant rows the BFF query-filter honours (`GrantExternalAccessEndpoint` + `ExternalParticipationService` precedent).

---

## 1. Decision

**Chosen mechanism: (B) — the Spaarke overlay-grant model.**

Private-thread participants are represented as **explicit grant rows** (a thread-scoped participant/grant record, following the `sprk_externalrecordaccess` pattern and living on the thread entity's already-planned participant-set home — design §6.1(A)). The **BFF thread-read filter (042/050) is the single enforcement point**; it unions the ADR-034-derived open-membership set with the overlay grant set to compute the caller's authorized message set.

This is **not a third mechanism**: it is the existing overlay *pattern* applied to the thread entity, exactly as design §5 ("explicit named-participant grant on the thread record … Thread privacy-state field + grant call + BFF filter") and §6.1(A) ("the thread entity … is the home for … participant set") already frame it. No new authorization engine; open membership stays on ADR-034.

> 🔔 **Human Input Required — see §6.** The two options **materially diverge on enforcement correctness** because of the BFF's app-only read path and the base-record-access posture. The recommendation below is B, but the mechanism is R1's highest-consequence security decision (NFR-06) and warrants explicit owner sign-off before task 042 is authored. This is a recommendation, **not a block** — 042 can be authored against B pending sign-off.

---

## 2. The deciding factor — the BFF read path is app-only

The single most load-bearing fact, verified in code:

**Every precedent Dataverse *read* service authenticates with an app-only `TokenCredential` (managed identity / `.default` scope), not the caller's user token.**

- `PlaybookSharingService.EnsureAuthenticatedAsync` → `_credential.GetTokenAsync(... "/.default")` (app-only). `Services/Ai/PlaybookSharingService.cs:58-72`.
- `ExternalParticipationService.GetAppOnlyTokenAsync` → app-only. `Infrastructure/ExternalAccess/ExternalParticipationService.cs:227-251`.
- `ExternalDataService.GetAppOnlyTokenAsync` → app-only. `Infrastructure/ExternalAccess/ExternalDataService.cs:580-607`.

An app-only application-user token runs at the **application user's** privilege. Dataverse's native security engine — including **POA / `GrantAccess` per-user shares — is only auto-evaluated for the principal being queried.** When the BFF reads as the application user, **POA shares are invisible to the read**: the application user either sees the row (its own base access) or it doesn't; a share granted to *end-user X* does not filter what the *application user* sees.

**Consequence:** even if we pick (A) `GrantAccess`, the BFF read path would **still** have to explicitly query the POA system table (`principalobjectaccessset?$filter=objectid eq …`, exactly as `PlaybookSharingService.GetSharedTeamsAsync` does at `:372-428`) and compute the allow-set itself. GrantAccess's headline advantage — "native enforcement, no custom filter" — **does not materialise on the actual read path.** Both options therefore require the BFF to compute an explicit allow-set. Given that, the overlay is the purpose-built, cleaner allow-set model.

---

## 3. Comparison matrix (evidence-backed)

| Axis | (A) `GrantAccess` / POA | (B) `sprk_externalrecordaccess` overlay | Winner |
|---|---|---|---|
| **BFF-filter enforcement correctness (042/050)** | BFF read is **app-only** → POA NOT auto-honored; BFF must query `principalobjectaccessset` by objectid + expand team principals to users (`PlaybookSharingService.cs:372-428`). Works, but you build a filter *and* depend on a system table. | Overlay is **built to be filter-honored**: `ExternalParticipationService` reads grant rows (`_sprk_contact_value eq X and statecode eq 0`, `ExternalParticipationService.cs:151-199`) and `ExternalCallerAuthorizationFilter` gates every read to the grant set. This is precisely the FR-11/042 read model. | **B** |
| **Composition with ADR-034 open membership** | ADR-034 returns `ids[]` / `byRole` id-sets (`MembershipResponse`). POA returns *principals* (users/teams) the BFF must expand + cross-map to the authorized-message set. Two dissimilar shapes to union. | Both ADR-034 and the overlay are **BFF-computed allow-sets** → the union is a plain set-union in the filter: `authorized = MembershipResolver(thread.anchor) ∪ overlayGrants(thread)`. Homogeneous. | **B** |
| **Revocation + point-forward (D-04)** | `RevokeAccess` message deletes the POA row (`PlaybookSharingService.cs:331-350`) — audit only via Dataverse audit log. POA is per-(record,principal); it has **no "effective-from"** → message-level point-forward is not expressible in POA and must live in a BFF timestamp filter anyway. | Revoke = soft-delete (`statecode=1, statuscode=2`, `RevokeExternalAccessEndpoint.cs:85-93`) — the row **persists** with `grantedBy`/`grantedDate`(/`expiryDate`) intact. A grant row is the natural home for an **effective-from** column → point-forward ("messages from grant moment forward") is `message.createdon ≥ grant.effectiveFrom`, evaluated in the same filter. | **B** |
| **Auditability** | Shares audited only via Dataverse's generic audit log; POA is a system table (harder to report/annotate; no grantedBy/reason columns). | Grant rows are **first-class queryable records**: `sprk_grantedby`, `sprk_granteddate`, `sprk_expirydate`, soft-delete-on-revoke retains history (`GrantExternalAccessEndpoint.BuildGrantPayload:176-203`). Self-contained, reportable audit trail. | **B** |
| **ACS-reconcile computability (041)** | Reconcile job computes the participant set from POA (`principalobjectaccessset` by objectid) + team expansion. Doable but two-step (principals → users). | Grant rows **directly enumerate participants**; the authorized set = `MembershipResolver ∪ overlayGrants`, both explicit Dataverse queries the reconcile job runs anyway. Trivially projectable to `AddParticipants`/`RemoveParticipant`. | **B** |
| **Internal-now / external-later (R2 BYOI) reach** | POA principals are `systemuser` / `team`. External participants are **contacts**, not systemusers → POA does not cleanly reach BYOI externals. | Overlay **already models `contact` grants** (`sprk_contactid@odata.bind`, `GrantExternalAccessEndpoint.cs:180`) and is the shipped external-access primitive. R2 external reach is native. | **B** |
| **Native defense-in-depth for OOB reads (read-as-user)** | POA **is** auto-honored by any read done *as the user* — e.g. an OOB main-form / subgrid / Advanced Find over `sprk_communication`. A granted user sees the shared record natively. | Overlay is **invisible** to non-BFF reads. If a private message were surfaced via an OOB subgrid/view, the overlay would not gate it — enforcement depends entirely on all content reads funnelling through the BFF filter. | **A** |

**Score: B wins 6 of 7 axes.** The one axis A wins (native OOB honoring) is addressed by the base-access precondition in §5 and is undercut by R1's actual read surface (BFF-polling timeline, §4).

---

## 4. Why the one A-advantage does not carry the decision

A's only real edge — POA is natively honored by reads done **as the user** — matters only if private-thread **message content** is read outside the BFF. It is not, in R1:

- Design §6.2 / NFR-04 / FR-11: the R1 message surface is a **polling timeline that reads persisted `sprk_communication` rows from the BFF**. Content reads funnel through the BFF thread-read endpoint (050) and its access filter (042).
- Design §3.1 (D-07) / §9.1: **"the BFF is the sole policy-enforcement point."** ACS membership is a projection the reconcile job computes. This is an explicit-filter architecture — the overlay's home turf, not POA's.

A subtlety worth stating plainly: **POA can only *add* access; it cannot *remove* it.** Neither mechanism restricts an OOB read if the base record access on `sprk_communication` is broad (org/BU read). So OOB-read safety is governed by **base record access**, not by the choice between A and B (see §5 precondition). Given the base-access precondition holds, the overlay yields the safest posture for private content: non-BFF reads **fail closed**, and the BFF filter is the single, auditable place private access is decided.

---

## 5. Binding precondition for task 042 (the security correctness condition)

Because option B concentrates enforcement in the BFF filter, task 042 MUST establish the fail-closed base posture so no read path bypasses it:

1. **Base record access for private threads MUST fail closed.** `sprk_communication` rows belonging to a private thread (and the `sprk_communicationthread` row itself) MUST NOT be broadly org/BU-readable such that an OOB view / subgrid / Advanced Find surfaces private content to a user without a grant. Private content is reachable **only** through the BFF thread-read endpoint. (This precondition is required under *either* option — POA cannot remove pre-existing broad access either — but it is load-bearing for B and MUST be verified in 042's security review.)
2. **The BFF thread-read filter is the sole content-enforcement locus.** `authorizedMessages(thread, caller) = openMembership ∪ privateGrants`, where `openMembership = MembershipResolverService(thread.anchor)` (ADR-034) and `privateGrants = active overlay grant rows for (caller, thread)`.
3. **Point-forward (D-04)** is implemented in the filter as `message.createdon ≥ grant.effectiveFrom` per active grant; prior private messages stay excluded. Retroactive open is a separate audited bulk action (out of R1 scope, design §5).
4. **Internal-only + privilege** compose *after* the membership union as additional filter predicates (user-attribute visibility for internal-only, per design §5 / D-05; privilege is classification metadata, ADR-015 — AI may flag, never decide).

---

## 6. 🔔 Human Input Required (root §6 — security-sensitive)

- **Situation**: The private-thread grant primitive is R1's highest-consequence security decision (NFR-06; a wrong choice leaks privileged content). The two options **materially diverge on enforcement correctness**: (A)'s native-enforcement advantage is nullified by the app-only BFF read path (§2), and (B) concentrates all enforcement in the BFF filter, which is only safe under the §5 base-access precondition.
- **Options**:
  - **(A) `GrantAccess` / POA** — platform-native; but on the app-only read path the BFF must query POA itself anyway, point-forward isn't expressible in POA, and external (BYOI) reach is weak.
  - **(B) overlay grant model** *(recommended)* — purpose-built for BFF-filter honoring, clean set-union with ADR-034, native point-forward + revoke + audit + R2-external reach; requires the §5 fail-closed base-access precondition.
- **Recommendation**: **Adopt (B).** It wins 6 of 7 comparison axes, matches the design's "BFF is the sole policy-enforcement point" posture, and gives the cleanest composition with ADR-034 and the ACS reconcile projection. Author task 042 against B with the §5 precondition as an explicit, review-gated acceptance criterion.
- **Sign-off asked of the owner**: confirm (B) and confirm the §5 base-access precondition is acceptable (private `sprk_communication` / thread rows fail closed to non-BFF reads). If the owner wants private threads to also be natively browsable via OOB views (read-as-user), that argues for (A) + restrictive base access + a BFF-side POA read — say so and 042 will be authored against A instead.
- **Not blocking**: task 042 can be authored against B now; sign-off can land before 042's Step 9.5 security review.

---

## 7. Rejected alternative — (A) `GrantAccess` / POA (why not)

`GrantAccess` is the platform-native primitive and is the right tool when reads are performed **as the end user** (OBO or host-context), letting Dataverse's security engine filter rows natively. It is rejected for private-thread messaging because:

1. **The BFF read path is app-only** (§2, verified in three precedent services). POA is not auto-honored for an app-only principal, so the "native enforcement, no filter" benefit does not materialise — the BFF must query the POA system table and expand team principals itself. The custom filter A was supposed to avoid is unavoidable.
2. **No effective-from → no native point-forward (D-04).** POA is per-(record, principal) with no time dimension; message-level point-forward would live in a BFF timestamp filter anyway, so the overlay's effective-date row is the cleaner home.
3. **Weak external reach.** POA principals are systemusers/teams; R2 BYOI participants are contacts. The overlay already models contact grants — no rework at R2.
4. **Composition friction with ADR-034.** ADR-034 yields id-sets; POA yields principals to expand and cross-map — two dissimilar shapes to union, versus B's homogeneous set-union.
5. **Thinner audit.** POA revoke deletes the row (audit only via the generic Dataverse audit log); the overlay soft-deletes and retains grantedBy/grantedDate/expiry as first-class, reportable columns.

A remains a legitimate fallback **if** the owner requires private threads to be natively browsable through OOB views as the user (§6) — in which case A + restrictive base access + a BFF-side POA read is the coherent alternative.

---

## 8. Consumed by 042 / 041 / 050

**Task 042 — privacy / internal-only / privilege enforcement (BFF filter):**
- Implement the thread-read access filter as `authorized = MembershipResolverService(thread.anchor) ∪ activeOverlayGrants(thread, caller)`.
- Model private grants as overlay rows following the `sprk_externalrecordaccess` pattern, scoped to `(participant, thread)` with `effectiveFrom` (point-forward), `grantedBy`, `grantedDate`, optional `expiryDate`, and `statecode` (soft-delete revoke). Home = the `sprk_communicationthread` participant set (design §6.1(A)); do **not** invent a parallel table if the thread-participant child table can carry a `grantType`/`effectiveFrom`.
- Enforce the **§5 fail-closed base-access precondition** as a review-gated acceptance criterion (security-sensitive — explicit code-review + adr-check at Step 9.5, per design §5 note).
- Layer internal-only (user-attribute visibility, D-05) and privilege (classification, ADR-015) as post-union predicates.
- Point-forward: `message.createdon ≥ grant.effectiveFrom`.

**Task 041 — ACS membership reconcile (projection):**
- Compute the authorized participant set from the **same** union: `MembershipResolverService(thread.anchor) ∪ activeOverlayGrants(thread)`. Both are explicit Dataverse queries.
- Project that set onto ACS via `ChatThreadClient.AddParticipants` / `RemoveParticipant` (design §8.4). Dataverse is authoritative; ACS membership is the projection. Event-driven reconcile + periodic sweep; audit each change.
- Revocation (overlay soft-delete) → participant removed from ACS on the next reconcile.

**Task 050 — BFF thread-read + unread-count endpoints:**
- Apply the 042 filter to every returned `sprk_communication` row (`sprk_body`, channel type, sender, attachments) so the endpoint returns only messages the caller may read (FR-11 acceptance).
- Unread count computed over the same access-filtered set (messages since caller's last-seen).
- The polling timeline PCF reads only through this endpoint (NFR-04, no client-side ACS SDK).

---

## 9. Compliance check (acceptance criteria)

- ✅ Names exactly one chosen mechanism (B, overlay).
- ✅ Evidence-backed comparison across BFF-filter correctness, ADR-034 composition, revocation + point-forward, auditability, ACS-reconcile, internal/external reach — each citing inspected precedent code (`PlaybookSharingService.cs`, `GrantExternalAccessEndpoint.cs`, `ExternalParticipationService.cs`, `RevokeExternalAccessEndpoint.cs`, `ExternalDataService.cs`, ADR-034).
- ✅ Rejected alternative (A) documented with concrete reasons (§7).
- ✅ "Consumed by 042/041/050" section specifies exactly how the filter + reconcile use the mechanism (§8).
- ✅ No new authorization engine / third mechanism; open membership stays on ADR-034 (§0–§1). No code or schema changed.
- ✅ Options materially diverge on correctness → 🔔 Human Input Required escalation recorded with a recommendation (§6).

---

## Appendix — code inspected

| File | What it establishes |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSharingService.cs` | Option A: `GrantAccess`/`RevokeAccess` Web API messages; POA read via `principalobjectaccessset`; **app-only token** on the read path (`:58-72`, `:302-350`, `:372-428`). |
| `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/GrantExternalAccessEndpoint.cs` | Option B grant: creates `sprk_externalrecordaccess` row with grantedBy/grantedDate/expiry (`:88-109`, `:176-203`). |
| `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/RevokeExternalAccessEndpoint.cs` | Option B revoke: soft-delete `statecode=1, statuscode=2` — audit-preserving (`:85-93`). |
| `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalParticipationService.cs` | Option B read/honor: query active grant rows (`statecode eq 0`), cache, **app-only token** (`:151-199`, `:227-251`). |
| `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalDataService.cs` | Option B enforcement in practice: all reads gated to the grant set via the authorization filter; **app-only token** (`:509-520`, `:580-607`). |
| `.claude/adr/ADR-034-user-record-membership.md` | Open-membership derivation: `IMembershipResolverService.ResolveAsync` → `ids[]`/`byRole` id-sets to union with private grants. |
| `projects/messaging-communication-app-r1/design.md` §5, §6.1, §8.4 | Access model lock; thread entity as participant-set home; ACS membership as projection. |

---

## OWNER DECISION (2026-07-16)

✅ **APPROVED — Option B** (`sprk_externalrecordaccess`-style overlay grant), unioned with ADR-034 open membership in the BFF thread-read filter. Owner sign-off obtained; this closes the §6 escalation. Tasks 042 (enforcement filter), 041 (ACS membership reconcile), and 050 (thread-read/unread endpoints) are authored/executed against option B. Filter contract: `MembershipResolver(anchor) ∪ activeOverlayGrants(thread, caller)`, point-forward via `createdon ≥ grant.effectiveFrom`.
