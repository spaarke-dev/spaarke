# Task 037 — Restricted post-max veto and Secure pre-max suppression

> **Completed** 2026-09-04 · `sonnet` tier @ high (run on Opus 5) · rigor FULL · `parallel-safe: false`
> **Depends on** 032 (evaluator spine) · **Spec** FR-21 / FR-22 · **Design** §4.5 vetoes 7–8, §5.1 · **Register** B-9/B-10, C-10, F-4
> Suite **12,082 passed / 0 failed** · ArchTests **191/191** · publish **+0.01 MB**

---

## Step 1 — the live-metadata verification, and why it mattered

The task required verifying the flag columns against live metadata before writing anything. Result:

| Entity | `sprk_issecure` | `sprk_accesspermission` | Restricted |
|---|---|---|---|
| `sprk_project` | BIT | CHOICE | **100000002** |
| `sprk_matter` | BIT | CHOICE | **100000002** |
| `sprk_workassignment` | BIT | CHOICE | **100000002** |

**Escalation trigger 1 does NOT fire** — all three root types carry both columns with identical option
sets, so no scoping question and no schema addition.

⚠️ **The task brief's citation for the raw value was to the wrong entity.** It pointed at
`TrackingFieldTrio/index.ts:136-138`, whose own comment reads: *"`sprk_communication` Access Permission
choice values … Entity-specific: lives ONLY here (the PCF caller), never in the shared core."* The number
happens to be identical on all three roots — but that was established by **querying metadata**, not by
trusting the citation. Had the sets diverged, a Standard record would have been treated as Restricted (or
worse, the reverse) with nothing failing.

---

## The design crux: suppression cannot be subtraction

`ExternalParticipationService` reads direct grants and organization grants as **two separate queries**
(org rows are marked by `_sprk_contact_value eq null`), then merges them and dedupes by record id keeping
the **highest** level.

That dedupe **destroys exactly what FR-22 needs**. Consider a contact with a **ViewOnly direct** grant and
a **Collaborate org** grant on the same secure record:

- the merge collapses them to `Collaborate`;
- Secure must suppress the org contribution;
- but there is no arithmetic that recovers `Read` from `Collaborate` — the direct level was already
  absorbed.

The acceptance criterion demands **exactly Read**. So the provenance must survive the dedupe.

### What was chosen, and what was rejected

**Chosen — an additive `DirectAccessLevel`** on `ExternalParticipation` and `ExternalRootGrant`, carrying
the highest level from the caller's **own** rows only (null when every contributing row was org-inherited).
One entry per record id; `AccessLevel` keeps its existing meaning, so every non-secure path is
byte-identical to before.

**Rejected — deduping by `(recordId, provenance)`** and keeping two entries. It is the more obvious shape,
and it is a trap: duplicate ids would reach `CallerPrincipal.ProjectAccess`, whose `GetEffectiveRights`
does `FirstOrDefault` and would return **whichever entry happened to be first**. An authorization answer
that depends on list order is strictly worse than an extra field.

**Escalation trigger 2 does NOT fire** — the brief permits "an additive provenance field", which is what
this is; no restructuring of `ExternalGrantSet`.

---

## Suppression is structural, not subtractive

`GrantedRightsFor` takes an `isSecure` predicate and, for a secure record, contributes the **direct** level
only. The org contribution is never added, so it never participates in the max. FR-22 says "suppression
must be structural (the term never contributes for that record), not a post-hoc subtraction" — this is that
requirement made mechanical rather than promised.

Same for the standing-membership term on the contact plane: secure ids are filtered **out of the term**
before `AccumulateTerm` sees them.

**Verified by perturbation.** Replacing `isSecure(id) ? DirectAccessLevel : AccessLevel` with plain
`AccessLevel` — i.e. making suppression a no-op — fails **3 of 37** tests: the ordering proof, the
org-vs-direct survivor pair, and the Type-1-via-contact case. Restored: 37/37.

---

## The Restricted veto

Post-max, **after** the (still no-op) deny-list slot. Every **contact-sourced** contribution is removed
regardless of strength — an explicit `FullAccess` grant included. What survives is whatever came from a
non-contact term, which today is the systemuser plane's ADR-034 membership ("only system users may have
access", register F-4).

On the **contact plane nothing survives**, because every term there is contact-sourced — which is precisely
FR-21's "denies ALL contact principals regardless of grant source".

**The veto removes the key.** It does not write `None`. A `None` value would still be a key in `Rights`,
would still appear in the derived `RecordIds` set, and would still read as "in the accessible set" to any
consumer that checks membership rather than rights — which is most of the read path. Pinned by a test
asserting absence from `Rights`, `RecordIds`, and `Contains`.

---

## Fail-closed, and its mirror image

An unreadable flag row resolves to `RootRecordFlags.Unreadable` = **secure AND restricted**, not merely
restricted. Treating an unknown record as non-secure would let a derived-member or org-expansion term
contribute access to a record nobody could confirm is safe to share.

Three ways a record lands there: a transport fault, a non-success status, **and an id the query did not
return at all** (deleted, filtered, or invisible to the app-only identity). The third is the one worth
naming — it is indistinguishable from a failed read, and defaulting it to open would be a silent hole.

**The mirror image is tested too**: fail-closed must not become fail-*broken*. A faulted flag read denies
contact-sourced terms but leaves the systemuser's ADR-034 membership intact — internal staff are not locked
out of their own records by a transient Dataverse blip.

---

## 🔴 What the change surfaced in the test suite

Adding the flag read made **five existing test doubles** fail, all for the same reason: they subclass
`ExternalParticipationService` with `credential: null!`, so the real flag read threw and **failed closed** —
every record came back secure+restricted and whole planes composed to nothing.

That is the production fail-closed path behaving correctly, observed from an unexpected direction. Each
double now overrides `GetRootRecordFlagsAsync` to return "unflagged", which is the right default for a test
that predates the vetoes. One of them (`ThrowingFlagParticipationService`) deliberately keeps the faulting
behavior, so the NFR-01 path stays covered on purpose rather than by accident.

A second, smaller bug surfaced the same way: the candidate id set is `membership ∪ grants`, so the **same
id legitimately arrives twice**. The real implementation dedupes; the first fake did not and threw
`ArgumentException` on a duplicate key. Fixed in the fakes, and the contract is now documented on the
method.

---

## Quality gates

**`adr-check`** — 0 violations. ADR-001/007/008/009/013/028-A4 clean by grep on the changed files. The
ADR-003 MUSTs this task is *about* hold structurally: vetoes run after the max in the order deny→Restricted
(slot comments + code order), Secure suppresses before the max (perturbation-proven), no value represents
"No Access" (the veto calls `composed.Remove`), fail-closed on every error path, and **no authorization
decision is cached** — flags are read live per request, so marking a record Restricted takes effect
immediately rather than after a TTL.

**`code-review`** — 0 critical. No new interfaces, **no DI-module change at all** (see Placement below),
no new packages.

**Placement Justification (CLAUDE.md §10 / §11)** — no new component.

- **Existing**: `SecurableEntityRegistry` answers "which *entities* carry `sprk_issecure`" from metadata —
  a schema question, not a per-record one, and it does not read `sprk_accesspermission`.
  `RecordContainerResolver.ResolveForRecordAsync` reads `sprk_issecure` for **one** record to route a
  container; it is in `Infrastructure/Dataverse`, returns a container decision, and is per-record where
  NFR-02 requires batched.
- **Extension**: the flag read was added as a method on `ExternalParticipationService`, which already owns
  the app-only Dataverse read plumbing (client, token, api-url) for this evaluator and is already
  registered. A new service would have duplicated that plumbing; extending `RecordContainerResolver` would
  have given a container-routing class a second reason to change.
- **Cost of doing nothing**: FR-21/FR-22 cannot be evaluated at all — a contact with an explicit FullAccess
  grant on a Restricted matter keeps write access, and a Type 1 user derives access to a secure record
  through their linked contact's org grant. Both are concrete disclosures, not abstractions.

Because it extended a registered service, **`Infrastructure/DI/ExternalAccessModule.cs` needed no change** —
a deviation from the task brief's file list, in the direction of less surface.

**Publish size (NFR-01)** — master `eb71df826` freshly built and measured **today** at **45.46 MB**; branch
**45.47 MB**; delta **+0.01 MB**; ceiling 60 (14.53 MB headroom). Zipped with PowerShell
`Compress-Archive -CompressionLevel Optimal`, matching `scripts/Deploy-BffApi.ps1`.

**CVE** — no vulnerable packages.

---

## For task 039

The deny-list slot is still a no-op and runs **first** by construction — a record removed there is gone
before Restricted looks at it, so a deny can never be downgraded into a survivable Restricted outcome.
`ApplyVetoPipeline` now takes `flags` and `survivesRestricted`; 039 adds the deny set as a third input and
removes keys in slot 1. **Do not** give a denied record a low rights value: `IsOperationPermittedAsync`
explicitly rejects `AccessRights.None` as a caller bug (task 033), so a `None` written by a veto would be
refused as a *malformed request* rather than honoured as a denial. Removal remains the only representation
of no access.

For task 043 (org-expansion term, Phase 2): route it through the same `isSecure` predicate. The hook is
plane-agnostic and already applies on both planes.
