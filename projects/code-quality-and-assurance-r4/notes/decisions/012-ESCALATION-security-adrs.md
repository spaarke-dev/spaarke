# 🔔 Task 012 — 8 ADRs require owner sign-off before a path may be chosen

> **Date** 2026-09-04 · **Escalation trigger FIRED** — a legitimate outcome, not a failure.
> **Authority**: CLAUDE.md §6.5 — *"Not an excuse to bypass auth, security, or compliance ADRs without explicit human sign-off."* Task 012's own constraint and escalation trigger say the same. Project `CLAUDE.md` lists this as a named hard stop.
> **Status**: **no path chosen for any of the 8.** Analysis and a recommendation only. 5 of the 13 held ADRs were decided and are recorded separately in this folder.

---

## Why these 8 and not the other 5

I checked each of the 13 held ADRs against its **actual MUST rules**, not its title — the title would have got two of these wrong.

**Two I would have mis-sorted from the title alone:**

- **ADR-005** reads as a storage-layout ADR ("flat storage, no folders"). But its MUST list contains **"MUST evaluate permissions via UAC (not SPE native)"** — that is an authorization-placement rule.
- **ADR-041** reads as a UX policy ("Judgment, Confirmation & Completion"). Its MUSTs are a **fail-closed authorization model for AI side effects**: classify request origin *deterministically and fail-closed*, gate writes/side-effects by risk tier × origin × completeness.

**Two I judged *not* security and am flagging so you can overrule:**

- **ADR-016** (rate limits) scored **zero** on every auth/security/compliance term and is framed throughout as cost and capacity. But per-endpoint rate limiting is also abuse control. I classified it *cost* and decided it. **Say the word and I'll move it here.**
- **ADR-019** (ProblemDetails) — its single keyword hit is a section heading ("Compliance checklist"), not a rule. Error responses *can* leak information, but this ADR governs format, not disclosure. Decided.

---

## The 8, with the rule that triggers sign-off

| ADR | The security/compliance rule (verbatim or near) | Why it needs you |
|---|---|---|
| **005** Flat Storage in SPE | *"MUST evaluate permissions via UAC (not SPE native)"* | Decides **where authorization is evaluated**. Ratifying or amending changes the authoritative permission surface for every document. |
| **014** AI Caching & Reuse | *"never cache raw content without governance approval"* | A **data-governance gate**. Ratifying makes it binding; withdrawing removes the only stated control on caching raw content. |
| **017** Async Job Status | *"MUST enforce authorization on job status endpoints (ADR-008)"* | An **authorization requirement** on an endpoint class. |
| **018** Feature Flags | *"Flags never bypass authorization"* | The invariant that a kill switch **cannot disable an authz check**. Among the highest-consequence rules in the estate. Also note: **ADR-032 (Accepted) exists to enforce ADR-018 (Proposed)** — an accepted ADR enforcing an unratified one. |
| **041** Judgment/Confirmation | *"classify request origin deterministically and **fail-closed**"*; writes/side-effects gated by risk × origin × completeness | The **fail-closed safety model** deciding when AI may act without human confirmation. |
| **042** Memory Architecture | `retentionClass` → per-item Cosmos TTL at write | **Data retention** — a compliance control. |
| **043** AI Capability Spine | *"hybrid authorization — autonomous low-risk / confirm …"* | The **authorization model for AI action execution**. |
| **047** Notification Spine | *"MUST NOT place message bodies, privileged content, or pre-authorized action tokens"* on the transport | A **data-exposure control** on the wire. |

---

## What I recommend for each (recommendation only — not a decision)

All 8 are `Proposed`, so the question is **ratify (path C) or amend-then-ratify (path B)**. None is a case where the code contradicts the ADR — where I could check, the code complies.

| ADR | Recommended | Reasoning |
|---|---|---|
| **018** | **C — ratify, and treat as urgent** | The strongest case in the set. "Flags never bypass authorization" is a rule you want binding, and ADR-032 already enforces it. The current state — an Accepted ADR enforcing an unratified one — is the anomaly. |
| **047** | **C — ratify** | Its gate (`spine-r1` Phase 1) is effectively reached: 21/22 tasks complete, and task **090 explicitly does this promotion**. Running 090 may be the cleaner route than deciding here. ⚠️ **But see the open conformance question below.** |
| **042** | **C — ratify** | Gated `Proposed` with a named gate (G-R2-B); 92 files implement it. Same pattern as ADR-039/040, both of which promoted cleanly. |
| **043** | **C — ratify** | Gated `Proposed`; 59 files; the dispatch spine other ADRs depend on. |
| **041** | **C — ratify** | Gated `Proposed`. The fail-closed default is the conservative one — ratifying makes the safe behaviour binding. |
| **005** | **B — amend, then ratify** | The permission rule is fine, but the ADR names **`sprk_documentassociation`, which exists nowhere in the repo** (only in the ADR text). The hierarchy mechanism it points at was renamed or never built. Amend the artifact reference first; the authorization rule itself needs no change. |
| **017** | **C — ratify** | Evidence is weak (broad keyword matches only). The authorization requirement simply defers to ADR-008, which is Accepted and enforced by a named test. |
| **014** | **C — ratify, or withdraw** | ⚠️ **The weakest evidence in the set — only 2 files match.** It may never have shipped. If it did not, ratifying a policy nobody implements creates a rule that is false on day one. **Withdrawal is a legitimate answer here and is neither path B nor C** — see the mapping gap below. |

---

## Two things you should know before deciding

### 1. The B/C mapping has a genuine gap for orphaned `Proposed` ADRs

Task 012's constraint says **paths B and C only** — path A does not apply. That is right for *"is this ADR still correct?"*. But for a `Proposed` ADR the real question is **ratify / amend-then-ratify / withdraw**, and **withdraw maps to neither path.**

It matters for exactly one ADR here — **ADR-014**, where "this was proposed, never implemented, and we should drop it" may be the honest answer. Forcing that into C ("comply") would ratify a policy the codebase does not follow.

**I did not invent a fourth path**, per the constraint. Flagging it as a real gap in the instruction rather than working around it silently.

### 2. ADR-047 carries an unanswered conformance question

Ratifying ADR-047 asserts the built thing matches the ADR. **Nobody has checked that**, and there is a specific reason to doubt it: `spaarke-notification-spine-r1` is 21/22 complete, yet the four producers (`CommunicationArrivedProducer`, `DailyBriefingSuggestionProducer`, `PreferenceDirectiveProducer`, `ICommunicationAssessedProducer`) arrived through *consumer* projects. If the spine was assembled per-consumer rather than built once, that is precisely what ADR-047's core commitment forbids — *"ONE spine built once for all client surfaces (collapses the email-r4/messaging-r3/assistant-r1 forks)"*.

**Recommendation: run `spine-r1` task 090 before ratifying ADR-047.** That task's stated scope is exactly this — "ADR-047 Proposed→Accepted, doc-drift reconciliation" — so the conformance check is already scheduled work, not new scope.

---

## Enforcement block (recorded for task 020)

**All 13 held ADRs remain ⛔ excluded from the FR-07 criterion set** — the 5 decided here and the 8 awaiting sign-off alike. A decided path is not a completed one: ADR-033 needs its amendment applied, and the ratifications need a status flip. **Nothing on this list is schedulable for an arch test until its record is acted on.**

⛔ **005, 014, 016, 017, 018, 019, 020, 023, 033, 041, 042, 043, 047**

---

## What I need from you

1. **Sign-off (or a different path) on the 8** — or a subset; partial sign-off is fine and I will proceed with whatever is decided.
2. **ADR-014 specifically**: ratify, or withdraw? Withdrawal needs your call because it is outside the B/C frame.
3. **ADR-047**: decide here, or run `spine-r1` task 090 first? I lean toward 090.
4. **ADR-016**: I decided it as *cost*. Overrule if you read rate limiting as abuse control.
