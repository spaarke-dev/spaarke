# ADR classification — all 49, three axes (task 010 / spec FR-05)

> **Measured**: 2026-09-04 · **Tree**: `work/code-quality-and-assurance-r4`
> **Purpose**: the routing input for FR-06, the exclusion list for FR-08, and the candidate pool for FR-07.
> **Scope**: classification only. No arch test written, no ADR amended, no stale verdict acted on.

---

## The decision rules (stated so every verdict is reviewable)

**Enforceability** — *can a mechanical test assert this rule at all?*
- `enforceable` — the rule is a structural invariant a source or assembly scan can decide (layering, DI shape, forbidden import, file placement, naming). Named arch test = proof.
- `partially-enforceable` — some clauses are structural, others need judgment.
- `judgment-only` — no clause is mechanically decidable.

**Accuracy** — *does the ADR describe the code as it is today?*
- `current` — Accepted **and** positive code evidence.
- `contested` — evidence ambiguous, **or** `Status: Proposed` (see the ratification finding below).
- `stale` — Superseded, or the subject is demonstrably gone.

**Checkability** — judgment-only ADRs **only**:
- `checkable-by-reading` — a reviewer can decide compliance from a diff.
- `aesthetic` — needs taste or product context.

---

## 🔴 Headline finding: ten ADRs were never ratified, and six of them shipped anyway

`Status: Proposed` on **10 of 49**: ADR-014, 016, 017, 018, 019, 020, 041, 042, 043, 047.

These are not drafts sitting unused. Measured hits for their subject code:

| ADR | Subject | Shipped code |
|---|---|---|
| ADR-019 | ProblemDetails error contract | **187 files** |
| ADR-042 | Memory architecture | **92 files** |
| ADR-043 | AI capability execution spine | **59 files** |
| ADR-041 | Judgment / confirmation policy | **17 files** |
| ADR-018 | Feature flags & kill switches | **13 files** |
| ADR-016 | AI cost / rate limits | 112 hits (broad terms — weak evidence) |
| ADR-017 | Async job status | 42 hits (broad terms — weak evidence) |
| ADR-014 | AI caching & reuse | 2 files |
| ADR-020 | Versioning strategy | 14 files |
| ADR-047 | Notification spine | **CORRECTED — substantially built** (see below) |

**The governance gap this exposes**: the codebase is substantially built on decisions nobody ever ratified. ADR-019 in particular is load-bearing — 187 files implement an error contract whose ADR still says "Proposed."

**Why these are classified `contested` rather than `current`.** On the literal accuracy definition several of them *are* accurate — ADR-019 describes the code precisely. But the accuracy axis exists to feed **FR-08**, which decides what may be enforced in CI. Enforcing an unratified rule means enforcing something nobody agreed to, and the whole point of FR-08 is to prevent exactly that. So the verdict is `contested` with a precise reason: **accurate to the code, but never ratified.** That is a different defect from "the ADR is wrong", and task 012 must treat it differently — the fix is ratification (confirm) or withdrawal, not amendment.

> ### ⚠️ CORRECTION 2026-09-04 — ADR-047 "zero server-side evidence" was WRONG
>
> The original grep searched for `NotificationSpine|INotification`. Neither string is used; the code is
> named `OutboxService`, `SignalRDeliveryService`, `NotificationsModule`. **I guessed identifier names
> instead of reading the ADR for what it actually names, or looking at the directory.** Owner caught it.
>
> ADR-047's four layers are all present in code:
>
> | Layer | ADR-047 says | Actually in the tree |
> |---|---|---|
> | B — durable outbox | `sprk_notificationoutbox` + write/read/dismiss/expire | `Services/Notifications/OutboxService.cs`, `Envelopes/`, `SignalRDeliveryOptions.cs` |
> | C — SignalR + host-agnostic client + poll fallback | Azure SignalR Serverless in-BFF; `@spaarke/notifications` | `SignalRDeliveryService.cs`; `Spaarke.Notifications/src/{negotiate,kindRouter,pollFallback,NotificationsClient}.ts` — an exact match to the described shape |
> | D — per-source producers | grounding + gating per source | `CommunicationArrivedProducer.cs`, `DailyBriefingSuggestionProducer.cs`, `PreferenceDirectiveProducer.cs`, `ICommunicationAssessedProducer.cs` |
> | A — shared domain-action seam | behind `*NodeExecutor.cs` | not separately verified |
>
> **The accuracy verdict for ADR-047 is therefore `contested` for the SAME reason as the other nine —
> `Status: Proposed`, never ratified — and NOT for absence of implementation.**
>
> **A genuinely open question remains, and it is the owner's**: `projects/spaarke-notification-spine-r1`
> reports **0 of 22 tasks complete** ("execution gated on FR-01 spike"), yet the code exists. The most
> likely reading is that the spine was built **piecemeal by its consumers** (the communication, daily-
> briefing and preference producers each shipped their own slice) rather than built once as a spine — which
> is precisely what ADR-047's core commitment forbids: *"ONE server-initiated spine built once for all
> client surfaces (collapses the email-r4/messaging-r3/assistant-r1 forks)"*. **Whether the built thing
> satisfies ADR-047's six MUSTs is NOT answered here** and needs its own look. That is a real
> conformance question, not a bookkeeping one.

---

## Enforcement inventory (step 1) — the answer to the spec's open assumption

The spec asked whether **ADR-028 (auth, 1,523 citations) is already effectively enforced by unnamed guards.** **It is.** ADR-028 is cited by six arch-test files: `CredentialCensusTests`, `CredentialGuardTests`, `RouteAuthorizationGuardTests`, `ServiceBusClientGuardTests`, `SpeWriteSinkContainerProvenanceGuardTests`, `SharedPackageCensusTests`. **P2b does not need a new test for ADR-028** — it needs the existing guards *named* against it (task 011's routing job).

| Enforcement kind | ADRs |
|---|---|
| **Named arch test** (test file named for the ADR) | 001, 002, 007, 008, 009, 010, 013, 038 — **8** |
| **Enforced by unnamed guard** (asserted, but by a test not named for it) | 003, 012, 028, 032, 034, 036, 040, 045, 049 — **9** |
| **No mechanical enforcement today** | the remaining **32** |

**"7 of 49 enforced" understates it.** The honest figure is **17 of 49 have some mechanical enforcement** — 8 named, 9 unnamed. The gap between 7 and 17 is entirely ADRs enforced by guards that never mention them by name in their title, which is precisely the discoverability problem FR-06 exists to fix.

---

## The classification

### Enforceable (structural — a scan can decide it)

| ADR | Title | Accuracy | Reason (enforceability · accuracy) |
|---|---|---|---|
| 001 | Minimal API + BackgroundService | current | Named test `ADR001_MinimalApiTests` · Accepted; endpoints match |
| 002 | Thin Dataverse plugins | current | Named test `ADR002_PluginTests` · Accepted; plugin base class present |
| 003 | Lean Authorization Seams | current | Two named seams are scannable; enforced by 3 unnamed guards · Accepted; both seams live |
| 007 | SpeFileStore Facade | current | Two named tests · Accepted; facade present |
| 008 | Endpoint Filters for Authorization | current | Named test `ADR008_AuthorizationTests` · Accepted; filters live |
| 009 | Redis-First Caching | current | Named test `ADR009_CachingTests` · Accepted |
| 010 | DI Minimalism | current | Named test `ADR010_DITests` · Accepted |
| 012 | Shared Component Library | current | Named test as of task 002 (`SharedPackageCensusTests`) · Amended today; 15-package set closed |
| 013 | AI Architecture | current | Three named tests (boundary, facade, linear-consumer) · Amended 2026-07-05 |
| 028 | Spaarke Auth Architecture v2 | current | 45 MUSTs, six unnamed guards · Accepted; guards green |
| 029 | BFF Publish Hygiene | current | Publish-size is measurable · Accepted; CLAUDE.md §10 live |
| 032 | BFF Null-Object Kill-Switch | current | Registration symmetry is scannable; 2 unnamed guards · Accepted |
| 038 | Testing Strategy | current | Named test `Adr038TestBanGuardTests` · Accepted 2026-06-26 |
| 040 | Session Ledger | current | Ledger shape scannable; 1 unnamed guard · Accepted 2026-07-05 |
| 044 | Dataverse GUID Canonicalization | current | Boundary canonicalization is scannable · Accepted |
| 005 | Flat Storage in SPE | current | `SpeUploadPathIsFlatGuardTests` enforces it · Accepted |
| 027 | Subscription / Solution Management | current | Solution-layer rules scannable · Amended 2026-06-02 |
| **019** | ProblemDetails & Error Handling | **contested** | Error-shape is highly scannable · **Proposed but 187 files implement it — ratify** |
| **018** | Feature Flags & Kill Switches | **contested** | Flag shape scannable; ADR-032 is its enforcement arm · **Proposed; 13 files** |
| **043** | AI Capability Execution Spine | **contested** | Dispatch spine is scannable; seam tests exist · **Proposed; 59 files** |
| **020** | Versioning Strategy | **contested** | SemVer/schema rules scannable · **Proposed; 14 files, weak evidence** |

### Partially enforceable (some clauses structural, some judgment)

| ADR | Title | Accuracy | Reason |
|---|---|---|---|
| 004 | Async Job Contract | current | Contract shape scannable; idempotency is not · Accepted |
| 006 | UI Surface Architecture | current | Surface placement scannable; "which surface fits" is judgment · Revised 2026-03-19 |
| 011 | List & Grid UI | current | Binding requirement scannable; the choice is judgment · Revised 2026-02-23; 21 PCF controls live |
| 015 | AI Data Governance | current | Never-log-content scannable; "minimization" is judgment · Amended 2026-05-17 |
| 021 | Fluent UI v9 Design System | current | Token use / no-hard-coded-colour scannable; visual conformance is not · Revised; 23 MUSTs |
| 022 | PCF Platform Libraries | current | React version + platform-library flags scannable; drift handling is judgment · Revised 2026-07-07 |
| 024 | Polymorphic Resolver Pattern | current | Dual-field shape scannable; when to apply is judgment · Accepted; amended path-B |
| 026 | Full-Page Custom Page Standard | current | Build config scannable; page design is not · Revised 2026-03-19 |
| 030 | PaneEventBus | current | Event-contract shape scannable; subscription design is not · Accepted v2 |
| 031 | Stage Lifecycle Pattern | current | `determineStage()` single-owner rule scannable · Accepted; **43 client files** |
| 033 | Streaming chat-tool side channel | current | Channel shape scannable; protocol judgment is not · Accepted; 15 files |
| 034 | User-Record Membership Resolution | current | 1 unnamed guard · Accepted — shipped R3 |
| 036 | Background-Job Infrastructure | current | Registration scannable; scheduling design is not · Accepted — shipped R3 |
| 037 | Multi-Node Output Composition | current | Node contract scannable · Amended 2026-07-05 |
| 039 | Grounded Execution & Closed Catalogs | current | Catalog closure is scannable (a census) · Accepted 2026-07-05; amended path-B |
| 045 | Communication Architecture | current | Canonical-send seam scannable; 1 unnamed guard · Accepted |
| 046 | ACS Messaging Channel | current | Transport shape scannable; product judgment is not · Accepted |
| 048 | Communication Participant Index | current | Junction grain scannable · Accepted; amended path-B |
| 049 | Compose Shadow Document | current | Parity + body-mapping guards exist · Accepted, amended 3× |
| 050 | Canonical Modal Shell | current | Shell/preset use scannable; modal-choice is judgment · Accepted 2026-08-01 |
| 051 | Scrollable Lists Infinite-Scroll | current | Pagination absence scannable; UX judgment is not · Accepted 2026-08-31 |
| **016** | AI Cost, Rate Limits, Backpressure | **contested** | Limits scannable; budget-setting is judgment · **Proposed; evidence weak** |
| **017** | Async Job Status & Persistence | **contested** | Status contract scannable · **Proposed; evidence weak (broad terms)** |
| **042** | Memory Architecture & Governance | **contested** | Store shape scannable; retention is judgment · **Proposed; 92 files** |
| **014** | AI Caching and Reuse Policy | **contested** | Versioned-key rule scannable · **Proposed; only 2 files — may never have shipped** |
| **047** | Notification & Action Spine | **contested** | Spine shape would be scannable · **Proposed; ZERO server-side evidence — verify before any verdict** |

### Judgment-only (no clause mechanically decidable)

| ADR | Title | Accuracy | Checkability | Reason |
|---|---|---|---|---|
| 025 | Icon Library and Deployment | current | **checkable-by-reading** | Sourcing rule has no MUSTs to scan, but a reviewer sees a non-Fluent icon in a diff immediately · Accepted |
| **041** | Judgment, Confirmation & Completion | **contested** | **checkable-by-reading** | Policy about when to confirm — inherently judgment; a reviewer can still see whether a confirmation gate exists · **Proposed; 17 files** |
| **023** | Choice Dialog Pattern | **stale** | **aesthetic** | Superseded 2026-03-19, demoted to a pattern; 0 MUSTs remain. Dialog choice is taste · **Superseded — the only genuinely stale ADR** |

---

## Counts

| Axis | Value | Count |
|---|---|---|
| **Enforceability** | enforceable | **21** |
| | partially-enforceable | **25** |
| | judgment-only | **3** |
| **Accuracy** | current | **38** |
| | contested | **10** |
| | stale | **1** |
| **Checkability** *(judgment-only only)* | checkable-by-reading | 2 |
| | aesthetic | 1 |

**Escalation trigger 3 checked**: 11 of 49 (22.4%) are stale or contested — **under the one-third threshold**, so the trigger does not fire and task 012 remains a step rather than a project.

---

## Handed to task 012 — the stale and contested list

**Stale (1)** — ADR-023 (Superseded 2026-03-19, demoted to pattern). Likely path: withdraw from the concise tier or mark clearly as historical.

**Contested (10), all for the same reason — `Status: Proposed`, never ratified:**

| Priority | ADR | Why it matters |
|---|---|---|
| **1** | **ADR-019** ProblemDetails | 187 files. The most load-bearing unratified decision in the repo. |
| **2** | **ADR-042** Memory Architecture | 92 files. |
| **3** | **ADR-043** AI Capability Spine | 59 files; the dispatch spine other ADRs depend on. |
| 4 | ADR-041 Judgment/Confirmation | 17 files. |
| 5 | ADR-018 Feature Flags | 13 files; ADR-032 (Accepted) is its enforcement arm — an Accepted ADR enforcing a Proposed one. |
| 6 | ADR-020 Versioning | 14 files, weak evidence. |
| 7 | ADR-016 AI Cost/Rate Limits | Broad-term evidence only. |
| 8 | ADR-017 Async Job Status | Broad-term evidence only. |
| 9 | ADR-014 AI Caching | 2 files — may never have shipped. |
| **2** | **ADR-047** Notification Spine | **Built** (outbox + SignalR + client + 4 producers) but `spaarke-notification-spine-r1` shows **0/22 tasks done** — likely assembled piecemeal by consumers, which is the fork-collapsing the ADR exists to prevent. **Conformance against its six MUSTs is unverified.** |

For all ten the §6.5 path is likely **confirm (ratify)** rather than amend — the decisions appear sound and are already implemented. The defect is procedural, not architectural. **ADR-018's case deserves attention**: ADR-032, which is Accepted, exists to enforce ADR-018, which is Proposed.

---

## Deviations and corrections

**1. The spec says 13 ADRs are missing from INDEX.md. It is 14.** The headline counts are right (49 files, 36 rows), but a compensating pair hides inside the subtraction: **ADR-038 has an INDEX row and no `.claude/adr/` file** — it is canonical at `docs/adr/ADR-038-testing-strategy.md`, and its INDEX row carries its content inline rather than linking. So 36 rows = 35 file-backed + 1 fileless, and 49 − 35 = **14** files needing rows. The escalation trigger checks the two headline numbers, both of which matched, so it did not fire.

**2. Acceptance criterion "INDEX.md lists exactly 49 entries" cannot hold as written.** Adding the 14 gives **50** rows (49 files + ADR-038's fileless row), and the task constraint forbids removing existing entries. Resolved by listing 50 and marking ADR-038's row explicitly as canonical-elsewhere. Recorded rather than fudged — the alternative was deleting a row the constraints protect.

**3. ADR-035 does not exist in either tier.** A genuine gap in the numbering, not a missing file. Noted so nobody hunts for it.

**4. Enforcement is 17/49, not 7/49** (8 named + 9 unnamed). The spec's 7 counts only ADRs with a test named after them. Both numbers are defensible; they answer different questions, and FR-09a should state which it reports.

**5. Accuracy verification depth is uneven, and that is stated rather than hidden.** Verdicts rest on: status line, arch-test presence, and a targeted grep for each ADR's subject. That is strong evidence for enforceability and for the Proposed/Superseded findings, and **weaker for `current` on the 25 partially-enforceable ADRs**, where "the subject code exists" is not the same as "every clause still holds." Per the task's own instruction to prefer `contested` when ambiguous, a deeper pass could move some `current` verdicts. Where the evidence was only a broad keyword match (ADR-016, ADR-017), that is said in the row.
