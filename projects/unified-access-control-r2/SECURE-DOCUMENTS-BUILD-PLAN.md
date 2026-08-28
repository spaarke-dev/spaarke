# Secure Documents — Build Plan (Waves 1–2)

> **Created**: 2026-08-25 · **Owner decision recorded**: broker-only for BOTH workforce and external contacts
> **Status**: Wave 1 + Wave 2 tasks created (070–076). Nothing built yet.
> **Read this before executing 070–076.** It is the coordination contract; the POMLs are the work.

---

## 1. The decision

**The BFF is the single access-decision point for every document and file byte, for every principal kind.**

No user — workforce or contact — is ever granted a SharePoint Embedded container permission. `SpeContainerMembershipService.GrantMembershipAsync` has zero callers and **stays that way**. The BFF authorizes, then streams app-only.

This was chosen over "container ACLs for workforce, broker for contacts" because that alternative answers one question with two mechanisms, and divergence between mechanisms is the bug class that produced every finding in this project.

### What each component is FOR, after this decision

| Component | Role | NOT its role |
|---|---|---|
| **Dataverse BU + memberless owner team + `Secure Project Owner` role** | Isolates the secure *record* from standing role privilege | Not document isolation |
| **Per-project SPE container** | **Blast-radius containment.** If a route is missed or the BFF is compromised, secure bytes are not sitting in a container everyone else's content shares | **NOT the live ACL.** No user ACLs are granted on it |
| **BFF authorization** | **The** access decision, for both principal kinds, on every read path | — |
| **Explicit share on the parent** | The only way a human gains access to a secure record | Never a per-document share |

### The three invariants

1. **Every byte path and every metadata path routes through one decision function.** A path that does not is a hole by construction — this is how all four Wave 1 findings happened.
2. **Access flows from the parent.** A caller who can read a project/matter/work assignment can read its documents. There is no per-document grant, share, or revoke.
3. **Secure content lives only in its own container.** A secure record's documents resolve to `sprk_project.sprk_containerid` and nothing else. Fail closed if absent — never fall back to a shared container.

---

## 2. Why this is the moment

**Zero secure projects exist in any environment.** There is no secure content to migrate, no retro-securing problem, no cleanup. Build it right now and that debt never exists. Once one real secure project is created, everything below becomes a migration.

---

## 3. Current state (verified 2026-08-25, not assumed)

### Done

- `Secure Project Owner` role — `Read` @ **User depth** on the 3 entities carrying `sprk_issecure` (`sprk_project`, `sprk_matter`, `sprk_workassignment`), on one memberless team; System Administrator removed (task 046).
- **BU isolation validated end-to-end** — a user in a *sibling* BU keeping `Deep` was denied a team-owned secure Matter and regained it only by explicit share (operator test). The guarantee is *"no ordinary human sits at or above the secure BU"*, not *"reduce the depth"*.
- Document **id-keyed** routes gated (task 002) — `DocumentAuthorizationFilter` → `AuthorizationService` → `RetrievePrincipalAccess`, fail-closed.
- **FR-29 delegation implemented** — `DelegationRuleFilter` enforces Write-on-record via OBO. (This is why Manage Access silently fails: the server correctly 403s and the UI swallows it.)
- **Contact document path is correct** — `ExternalProjectDataEndpoints` checks project access **and** doc∈project before any SPE read, then streams app-only. **This is the reference implementation for Wave 3.**
- BFF deployed to dev 2026-08-25 (it had been running a build predating its own gates).

### Broken — Wave 1 closes these

| # | Gap | Exploitable? |
|---|---|---|
| 1 | `POST /api/ai/search` accepts `scope=all`; filter returns allow for **every** scope | **Yes, now.** Tenant-wide document names, AI summaries, TL;DRs, `driveId`, `speFileId`. Independent of container ACLs — never touches SPE |
| 2 | `OBOEndpoints` drive-keyed routes have **no** per-document check (`AddDocumentAuthorizationFilter` appears **zero times** in that file): read, PATCH, **DELETE**, enumerate children | Latent — these are **OBO**, so SPE denies without a container ACL, and no user has one. A bypass by construction; under broker-only they have no reason to exist |
| 3 | `POST /api/documents/{id}/share-link` — no filter; mints `scope=anonymous`, **non-expiring** | Latent (OBO), but escalates "container member" → "anyone with the URL" |
| 4 | `PUT /api/containers/{containerId}/files/{*path}` — any container id, **app-only MI** | **Yes, now.** No container ACL needed |

### Broken — Wave 2 closes this

**Nothing reads `sprk_project.sprk_containerid`.** Provisioning stamps it (task 021) and no read or write path consumes it. Uploads resolve from the *acting user's BU* (7 client sites) or a global `ArchiveContainerId` (server-side email/communication ingest). So secure documents land in shared containers.

**This cannot be fixed with per-item permissions.** SharePoint Embedded is **additive-only** — *"You can't break inheritance on arbitrary files or folders"* ([Microsoft Learn](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/share-files-manage-permissions)). Same shape as Dataverse's no-per-record-deny. The only fix is putting secure content in its own container.

---

## 4. Platform constraints (verified against current Microsoft docs, 2026-08-25)

| Constraint | Consequence for us |
|---|---|
| SPE permissions are **additive-only**; inheritance cannot be broken on a file | Per-project containers are mandatory, not an optimisation |
| Every SPE user must be an Entra **member or B2B guest**; app-only **cannot invite new guests** | External contacts can *never* hold SPE access → broker is the only option → confirms the decision |
| Containers count toward **2M sites+containers/tenant**; creation **5/sec**; 30M files & 25TB per container | Per-project containers scale fine |
| *"Containers provide the storage and security boundary… keep boundaries aligned with access, lifecycle, governance"* | Per-project is the sanctioned pattern |
| Container type defaults to the **open sharing model** (members with edit can add file permissions) | ⚠️ **Verify Spaarke's container type is set to `restrictive`.** Needs the owning app's token — the Azure CLI app lacks permission |
| `informationBarrier` on `fileStorageContainer` (Mar 2026, beta) | Possible native primitive for the ethical-wall veto — evaluate later, not in these waves |

---

## 5. The waves

### Wave 1 — close the holes (parallel; no design decisions)

| Task | Work |
|---|---|
| **070** | Gate `POST /api/ai/search` — constrain to the caller's accessible-record set; refuse `scope=all`; stop emitting `driveId`/`speFileId` |
| **071** | Delete (preferred) or gate the `OBOEndpoints` drive-keyed routes — under broker-only they serve no purpose |
| **072** | Gate `share-link` — filter + bounded expiry + drop `scope=anonymous` |
| **073** | Authorize `PUT /api/containers/{containerId}/files/{*path}` against the owning record |
| **074** | **ArchTest forcing function** — a route without an authorization filter or named waiver fails the build |

**074 is the most valuable task in both waves.** Enforcement here has been by enumeration and the count has been wrong *every* time: ~15 estimated → 22 found by task 022 → then `/api/ai/search` and `share-link` found *after* that sweep → then five more in `OBOEndpoints`. Precedent for the mechanism: the CORS-drift fitness test on master (`34ef54542`).

### Wave 2 — make the container real

| Task | Work |
|---|---|
| **075** | The **record-aware container resolver** — one shared seam: *secure record → its own `sprk_containerid`; else the BU cascade; fail closed if a secure record has none* |
| **076** | Route **every** call site through it — 7 client sites + server-side ingest — and **suppress the wizard's BU cascade for secure projects** |

Live end-to-end validation is **task 047** (already exists) — it must assert **inequality** against every BU container, because three existing projects already carry the root BU's container id, so "a container id is set" is precisely the false positive.

---

## 6. Definition of done for Waves 1–2

**The claim after Wave 2:**

> *A secure project's documents resolve only to that project's own SPE container. No document metadata or bytes are served on any BFF path without an authorization decision. A route added without one fails the build.*

Note what this claim does **not** yet include — those are Wave 3+:

- Access does not yet **flow from the parent** for workforce (computed 1-hop inheritance) — so after Wave 1 gating, a share-only user is locked out of secure documents entirely until Wave 3.
- `sprk_issecure` still appears in **no** authorization path — the veto is designed, not implemented.
- FR-28's share half is unbuilt — a secure project is isolated but unreachable.
- BU migration and the standing canary are operator-track and unscheduled.

**Do not overstate the claim at the end of Wave 2.** Isolation of *content location* and closure of *ungated paths* is real progress; it is not "secure projects work".

---

## 7. Rules that bind every task here

1. **Fail closed.** Any error, null, unresolved principal or missing config denies (ADR-003, NFR-01).
2. **Never widen a permission to make a failure disappear.** Task 046's lesson: when assignment failed, the field fix was to add three broad roles to the owner team. Add the one thing that is missing, and record what forced it.
3. **Verify against live metadata, not docs.** Nine "docs lose to live metadata" instances in this project, including the BU name itself.
4. **Success where you expect denial is the signal.** Configuration-shaped assertions pass while holes are open — that is exactly what happened with `Spaarke Basic User`. Prefer an impersonated-read test that requires a denial.
5. **BFF hygiene** (root CLAUDE.md §10): state the Placement Justification, verify publish size (≤60 MB; baseline 45.05 MB), no new HIGH CVE, update tests.
6. **Do not grant SPE container permissions to users.** `GrantMembershipAsync` stays at zero callers. If a task appears to need it, that is an escalation, not an implementation detail.

---

## 8. Open items NOT in these waves

| Item | Owner |
|---|---|
| Computed 1-hop inheritance (document access from parent) — **Wave 3** | agent |
| `sprk_issecure` veto in the read path — **Wave 3** | agent |
| FR-28 share half + Manage Access 403 surfacing — **Wave 4** | agent |
| BU migration (users out of root; re-home orphaned records) | **operator** |
| Standing empirical canary (provision → impersonated deny → share → allow) | agent, needs env |
| Verify container type is `restrictive`, not the default `open` | **operator** (needs owning-app token) |
| FR-31 wizard copy says secure is reversible — it is not, without retro-securing migration | **owner decision** |
| Child-entity ownership (18 entities / 19 lookups) | separate task, post-MVP |

---

## 9. Provenance

Findings: [`notes/task-046-secure-project-owner-role.md`](notes/task-046-secure-project-owner-role.md) §7b + §9 ·
model corrections: [`design.md`](design.md) §4.1, §5.1a, §5.1a-2, §5.1d ·
NFR-05 re-amendment: [`spec.md`](spec.md) ·
role runbook: [`../../docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md`](../../docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md)
