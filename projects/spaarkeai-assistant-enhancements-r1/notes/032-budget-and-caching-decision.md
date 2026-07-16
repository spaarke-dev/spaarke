# Task 032 — `EnvelopeBudget.User` amendment, byte-stability, caching + FR-E5 scope

> **Task**: 032 (NFR-01 / NFR-02 / NFR-03; ADR-tension #2 Path B). **Depends on**: 030 (stated-profile producer).
> **Status**: implemented + eval-green; **Path-B amendment SIGNED OFF by owner 2026-07-15** (CLAUDE.md §6.5).
> **Date**: 2026-07-15.

> **Owner sign-off (2026-07-15)** — ralph.schroeder:
> - **Path-B `EnvelopeBudget.User` 300 → 700**: **APPROVED** as sized (§1).
> - **FR-E5 BU/team**: **DESCOPE from R1, track as follow-on** — stated role (030) satisfies FR-E5 acceptance (§4). Filed in `notes/defer-issues.md`.
> - **Caching**: defer-cache decision (§2) accepted as recommended.

---

## 1. `EnvelopeBudget.User` amendment — 300 → 700 (ADR-tension #2, Path B)

### The tension (why this is Path B, not a silent edit)

`EnvelopeBudget.User` is a **ratified constant + golden-utterance merge-gate assertion**
(`ContextBudgetBreachEvalTests.BindingBudgets_MatchTheRatifiedReconciliation`, `Category=GoldenUtteranceEval`).
Changing it is an ADR-039/NFR-01 ratified-baseline change — CLAUDE.md §6.5 requires it be surfaced as a
documented Path-B amendment with code-review/owner sign-off, not squeezed in silently.

### Measurement basis

The r2 reconciliation (`spaarke-ai-architecture-redesign-r2/notes/054-budget-reconciliation.md`) ratified
**User = 300**, measuring ~40 tokens — but at that time the User slice carried **only the current message**.
R1's FR-E2 (task 030) now composes **TWO producers into the one User slice**, in this order:

| Producer | Cap | Realistic worst (tokens, chars/4) |
|---|---|---|
| Stated-profile block (`StatedProfileRenderer`) | **uncapped** (free-text `sprk_focusareas` / `sprk_assistantpreferences`) | ~282 — heading (6) + role label (≤9) + N:N practice areas (~80) + focus (~75) + office (~28) + preferences (~82) |
| User-memory recall (`MemoryItemStore.ToUserPromptFragmentAsync`) | **250** (`MemoryItemStore.MaxUserFragmentTokens`) | ≤250 |
| join (`\n\n`) | — | ~1 |
| **Composed User fragment** | — | **~532 realistic worst** |

The ≤300 estimate **predated this two-producer composition**. ~532 realistic worst already exceeds 300.

### Chosen value: **700**

- Covers ~532 realistic worst + **~32% headroom** (168 tokens).
- Lands at the top of the task's predicted ~500–700 band — justified by the composition, not padding.
- **Envelope-ceiling safety**: `EnvelopeCeiling` (4,200) is an *independent* bound. Raising User's per-slice
  max 300→700 lifts the sum-of-maxes 4,550→4,950, but normal turns sit ~2,025–2,410 (~50–57% of ceiling);
  a profiled turn adds ≤~500, still far under 4,200. No ceiling-budget change needed.
- **Pathological profiles** (a user pasting 2,000 chars into `sprk_assistantpreferences`) still **surface as a
  breach warning, never truncated** (FR-B-05 invariant; live turns never 500). Bounding the free-text fields is
  **task 052's** (security/prompt-injection) concern, not a budget concern. We do NOT add render-side
  truncation — that would violate the "surface, never silently truncate" invariant.

### Escalation trigger — did NOT fire

The task's trigger: *"if the sized budget or re-baseline would break unrelated golden-utterance cases, STOP."*
Raising a ceiling is a **pure relaxation** — it can only make breaches *less* likely. The only assertion that
changes is the reconciliation pin itself (intentionally re-baselined). No unrelated golden-utterance case
asserts `User ≤ 300`. Trigger not fired; the amendment stays project-scoped (one constant + its pin + this note).

### Byte-stability (NFR-02) — what changed vs what was already true

The render was **already deterministic** (030): fixed field order, practice areas ordinal-sorted *by the reader*,
free-text fields read as opaque stored strings. Task 032 adds the **byte-stability PINS** that were deferred
from 030:

- `StatedProfileRendererTests` — golden byte-frozen block + determinism + given-order-preserved + partial/empty.
- `ContextEnvelopeRendererTests.RenderStablePrefixAdditions_StatedProfileUserFragment_IsByteStableAndFirst` —
  composition-level pin (User stated-profile fragment renders byte-identically and precedes Business).

**"Canonicalized prefs JSON" is a WRITE-side obligation, not a render-side one.** `sprk_assistantpreferences`
is read back as the **exact stored string** (Dataverse returns bytes verbatim) — reading is inherently byte-stable
turn-to-turn, so no render-side canonicalization is needed (and canonicalizing opaque free text at render could
corrupt non-JSON content). If task 042 writes prefs as serialized JSON, **042 MUST canonicalize key order at
write time** so the *stored* string is stable across edits. Recorded as 042's obligation (see §4).

---

## 2. Caching + latency decision (NFR-03)

### The cost

The stated-profile read (`StatedProfileReader.ReadAsync`) adds **two Dataverse round-trips per turn** on the hot
bind path: (1) keyed `sprk_userprofile` retrieve by `sprk_systemuser` (`TopCount=1`, `NoLock`), (2) the N:N
practice-area query (`NoLock`). Both already **soft-fail-to-null** (ADR-032 P2) — a read error or timeout degrades
the stated block to absent; a bind is never taken down by the profile.

### Precedent considered: `IdentityNormalizationService` (Redis, 10-min TTL, ADR-009)

`IdentityNormalizationService` caches a per-`systemuserid` `PersonIdentity` in `ITenantCache` (Redis) with a
**10-minute TTL** (key `tenant:{t}:membership-identity:{userId:D}:v1`). It is the directly-applicable precedent —
same key shape (per-user), same hot-path identity read.

### Decision: **DEFER the cache in R1** — cite the precedent, reject it *for now*, with a telemetry trigger

**Latency budget**: the profile read must add **≤50 ms p50 / ≤150 ms p95** to `ContextBinder.BindAsync`. The
soft-fail already bounds the tail (a slow/failed read degrades to null rather than blocking).

**Rationale for defer (not build)**:
- The read is two cheap keyed one-hops with `NoLock`; the bind path already issues several Dataverse reads
  (caller-contact resolver, caller-systemuser resolver, record memory) without per-read caches.
- Adding cache-key machinery + an invalidation story (the profile changes when task 042 writes it — a cache would
  need write-side invalidation to avoid a ≤10-min stale window on the user's *own just-saved* profile) is **new
  surface** (CLAUDE.md §11) whose value is unproven until we have bind-path latency telemetry.
- Consistent with the operator's standing "don't build what won't be used" ruling on this codebase (r2 2026-07-09 (g)).

**Trigger to adopt** (recorded follow-on): if bind-path telemetry shows the profile read exceeds the latency
budget at p95, OR profiled turns become a hot fraction of traffic, adopt the `IdentityNormalizationService`
pattern verbatim — `ITenantCache`, per-`systemuserid` key, but with **write-through invalidation from task 042's
upsert** (shorter effective staleness than a blind 10-min TTL, because the user editing their own profile must
see it next turn).

---

## 3. Publish-size (bff-extensions §10 bullet 4)

Additive C# only (one constant value change + tests). Zero package references added/changed → no new CVE.
Publish-size delta expected ≈ 0 vs the ~49.63 MB (incl. PDB) baseline; well under the ≤60 MB ceiling and the
≥+5 MB single-task escalation threshold. Measured value reported in the task summary / PR.

---

## 4. FR-E5 (role/BU/team) — scope recommendation for the Path-B gate

**FR-E5 acceptance criterion (spec)**: *"role reflected in the profile fragment."* This is **already satisfied by
task 030** — the stated `sprk_primaryrole` **label** is rendered in the stated-profile block (`- Role: …`).

The carried-forward note asked to *also* fold **BU/team** via `sprk_userentityassociation` +
`IMembershipResolverService` into the same User fragment. Recommendation: **descope the BU/team second read from
R1** (owner decision — surfaced at the gate), because:

1. **Wrong mechanism named.** `IMembershipResolverService` resolves **record-membership** (which `sprk_matter` /
   document / event rows a user is on, keyed by a *target entityType*, grouped by role) — it does **not** return
   the user's business unit or teams. BU/team live on `systemuser.businessunitid` + `teammembership`; the service
   that actually carries them is `IdentityNormalizationService.PersonIdentity` (`BusinessUnitId`, `TeamIds[]`),
   already Redis-cached.
2. **Second hot-path read for speculative bias value.** Folding BU/team means another per-turn Dataverse/identity
   read on the bind path to nudge one agent turn — value unproven, cost real (see §2).
3. **Scope-boundary smuggling.** BU/team is **organizational** context. The design is explicit: the User reader is
   *"Not `IOrganizationalContextProvider`"* and the org graph is *"reference, not copy"* (design §6 / lines
   327–328); `IOrganizationalContextProvider` is the **deferred** org-scope seam. Folding org-hierarchy BU/team
   into the *User* slice back-doors deferred org-scope.

**If the owner still wants BU/team in R1**: the cheap, in-boundary path is to render from the **already-cached**
`IdentityNormalizationService.PersonIdentity` (no fresh read) — e.g. a `- Team: …` / `- Business Unit: …` line —
rather than a new membership read. That is a small, separate follow-up; it should not gate the NFR-01/02/03 core
of task 032.

**Net**: FR-E5's stated acceptance is met by 030. The BU/team enrichment is recommended **deferred** and tracked;
awaiting owner sign-off at the gate.
