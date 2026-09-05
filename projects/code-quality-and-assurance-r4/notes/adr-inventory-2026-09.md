# ADR inventory — all 50 entries, enriched with every task 010/011 finding

> **Built** 2026-09-04, **updated** with tasks 010 (classification) + 011 (routing) + the accuracy re-verification.
> **Sorted by usage** (descending). **Purpose**: one artifact for judging manually which ADRs are actually used and useful.
> Companions: [classification](adr-classification-2026-09.md) · [accuracy re-verification](adr-accuracy-reverification-2026-09.md) · [routing](adr-routing-2026-09.md)

**50 entries** = 49 concise ADR files + ADR-038 (canonical in `docs/adr/`, indexed here). ADR-035 exists in neither tier.

## What to look at when reviewing

| Flag | Meaning | Count |
|---|---|---|
| 🔴 | **Drift** — Accepted, but the code moved underneath it. The ADR names artifacts that no longer exist. | 2 |
| 🟠 | **Orphaned `Proposed`** — never ratified, no gate named, ~2025-12 era. The real ratification gap. | 6 |
| 🔵 | **`Proposed` pending a named gate** — a legitimate state; ADR-039/040 were promoted this way. | 4 |
| ⚠️ **names nothing** | The ADR names **no checkable artifact** — its accuracy cannot be verified by tooling or by a person without interpretation. | 15 |
| ⛔ FR-08 | Routed, but **not scheduled for enforcement** until its contested/stale status resolves (task 012). | 13 |

**Columns.** *Usage* = citations across `projects/*/tasks/*.poml` — a proxy for how often it is loaded, **not** a quality measure; nothing follows from a high or low number. *Enforced by* — `named test` = an arch test named for the ADR; `unnamed guard` = asserted by a test that never names it. *Artifacts* = named artifacts found / named, from the re-verification screen. *Mechanism* = the FR-06 routing destination.

| ADR | Name | Decision (first line) | Status | Usage | Enforced by | Artifacts | Mechanism (FR-06) | Sched |
|---|---|---|---|---|---|---|---|---|
| ADR-021 | Fluent UI v9 Design System | All Spaarke UI must follow the Microsoft Fluent UI v9.x design system. This is the authoritative | Accepted (Revised 2026-02-23, Upda | **3062** | — | 0/1 | arch test + nightly review | ✓ |
| ADR-013 | AI Architecture | Default: extend Sprk.Bff.Api with AI endpoints in-process. The bulk of AI synthesis, chat, RAG,  | Accepted (amended 2026-07-05) | **2111** | **named test** | 4/4 | arch test (blocking) | ✓ |
| ADR-038 | Testing Strategy - Integration-heavy pyramid | Integration-heavy pyramid; 7 KEEP path categories as MUST rules; coverage is observation never g | Accepted (2026-06-26) | **1778** | **named test** | n/a | arch test (blocking) | ✓ |
| ADR-028 | Spaarke Auth Architecture (v2) | Adopt function-based auth as the only public contract at every consumer boundary. Eliminate snap | Accepted (resolution path B — amen | **1524** | unnamed guard | 20/23 | arch test (blocking) | ✓ |
| ADR-010 | DI Minimalism | Keep dependency injection minimal and concrete. Register concretes unless a genuine seam exists. | Accepted | **1477** | **named test** | 1/3 | arch test (blocking) | ✓ |
| ADR-012 | Shared Component Library | Maintain a shared TypeScript/React component library at src/client/shared/Spaarke.UI.Components/ | Accepted (Amended 2026-07-12, 2026 | **1252** | unnamed guard | 25/25 | arch test (blocking) | ✓ |
| ADR-015 | AI Data Governance | Apply data minimization and logging hygiene to all AI operations. Never log content; always scop | Accepted (Amended 2026-05-17) | **1100** | — | 1/1 | arch test + nightly review | ✓ |
| ADR-029 | BFF Publish Hygiene | The Sprk.Bff.Api publish output MUST be framework-dependent linux-x64, MUST exclude wwwroot//*.j | Accepted | **1048** | — | 2/2 | arch test (blocking) | ✓ |
| ADR-022 | PCF Platform Libraries (Field-Bound Controls | Scope: This ADR applies only to field-bound PCF controls (form-embedded controls that use Datave | Accepted (Revised 2026-07-07) | **918** | — | 3/3 | arch test + nightly review | ✓ |
| ADR-039 | Grounded Execution & Closed Catalogs | Spaarke AI has exactly one dispatch protocol (three entry paths) over | Accepted (2026-07-05) — promoted P | **858** | — | ⚠️ **names nothing** | arch test + nightly review | ✓ |
| ADR-008 | Endpoint Filters for Authorization | Use endpoint filters for resource-based authorization, not global middleware. | Accepted | **743** | **named test** | ⚠️ **names nothing** | arch test (blocking) | ✓ |
| ADR-001 | Minimal API + BackgroundService | Run the BFF on a single ASP.NET Core App Service using: | Accepted | **702** | **named test** | 1/1 | arch test (blocking) | ✓ |
| ADR-024 | Polymorphic Resolver Pattern | Use a dual-field strategy for polymorphic associations where a child record can be related to mu | Accepted | **583** | — | 25/30 | arch test + nightly review | ✓ |
| ADR-007 | SpeFileStore Facade | Use single focused facade (SpeFileStore) for all SPE/Graph operations. No generic IResourceStore | Accepted | **559** | **named test** | 2/4 | arch test (blocking) | ✓ |
| ADR-019 🟠 | ProblemDetails & Error Handling | Use RFC 7807 ProblemDetails for all API errors. Include stable error codes and correlation IDs.  | Proposed | **555** | — | ⚠️ **names nothing** | arch test (blocking) | ⛔ FR-08 |
| ADR-040 | Session Ledger | Every AI session has an append-only, addressable, typed ledger — the ONLY | Accepted (2026-07-05 at gate G-P0  | **483** | unnamed guard | ⚠️ **names nothing** | arch test (blocking) | ✓ |
| ADR-006 | UI Surface Architecture — Code Pages, PCF, a | All Spaarke frontend UI is built using three surface types, each chosen based on the hosting con | Accepted (Revised 2026-03-19) | **470** | — | 2/2 | arch test + nightly review | ✓ |
| ADR-032 | BFF Null-Object Kill-Switch Pattern | For every BFF service T that is registered conditionally (inside a feature-gate if (flag) { … }  | Accepted | **440** | unnamed guard | 1/4 | arch test (blocking) | ✓ |
| ADR-030 | PaneEventBus — Typed Multi-Subscriber Cross- | The PaneEventBus is the single authorized cross-pane communication primitive for the SpaarkeAi s | Accepted (v2 — amendment 2026-06-2 | **415** | — | 6/7 | arch test + nightly review | ✓ |
| ADR-014 🟠 | AI Caching and Reuse Policy | Apply AI-specific caching rules on top of ADR-009 Redis-first policy. Cache derived artifacts (t | Proposed | **414** | — | ⚠️ **names nothing** | arch test + nightly review | ⛔ FR-08 |
| ADR-045 | Communication Architecture — Canonical Send  | Communication (send and receive, email today, any channel later) is governed by four coupled rul | Accepted | **400** | unnamed guard | 9/9 | arch test + nightly review | ✓ |
| ADR-018 🟠 | Feature Flags and Kill Switches | Use options-based feature flags with typed validation. Disabled features return 503 ProblemDetai | Proposed | **359** | — | 2/2 | arch test (blocking) | ⛔ FR-08 |
| ADR-009 | Redis-First Caching | Use Redis as distributed cache. Per-request cache for within-request de-dupe. No hybrid L1+L2 wi | Accepted | **335** | **named test** | 6/6 | arch test (blocking) | ✓ |
| ADR-034 | User-Record Membership Resolution Pattern | Spaarke ships ONE canonical mechanism for "records this user is associated with, by entity type" | Accepted — shipped in R3 (2026-06- | **306** | unnamed guard | 13/16 | arch test + nightly review | ✓ |
| ADR-049 | Compose Shadow Document Architecture | The OOXML .docx is the server-authoritative source of truth; the TipTap/ProseMirror editor is a  | Accepted, amended three times — R4 | **305** | unnamed guard | 7/7 | arch test + nightly review | ✓ |
| ADR-016 🟠 | AI Cost, Rate Limits, and Backpressure | Apply layered throttling to AI operations: per-endpoint rate limiting, bounded concurrency, and  | Proposed | **296** | — | ⚠️ **names nothing** | arch test + nightly review | ⛔ FR-08 |
| ADR-004 | Async Job Contract | Use one standard Job Contract for all async work. Process via BackgroundService workers with ide | Accepted | **269** | — | ⚠️ **names nothing** | arch test + nightly review | ✓ |
| ADR-026 | Full-Page Custom Page Standard | Full-page Dataverse surfaces (sitemap entries, navigation pages, side panes) and standalone dial | Accepted (Revised 2026-03-19) | **233** | — | 8/10 | arch test + nightly review | ✓ |
| ADR-027 | Subscription Isolation and Dataverse Solutio |  | (none) | **214** | — | ⚠️ **names nothing** | arch test (blocking) | ✓ |
| ADR-003 | Lean Authorization Seams | Use two seams only: IAccessDataSource for UAC data, SpeFileStore for storage. Implement authoriz | Accepted | **157** | unnamed guard | 1/2 | arch test (blocking) | ✓ |
| ADR-046 | ACS Messaging Channel — Transport, First-Cla | Messaging is a provider on the ADR-045 process, governed by seven coupled rules: | Accepted | **135** | — | 8/8 | arch test + nightly review | ✓ |
| ADR-033 🔴 | Streaming chat-tool side channel | Chat-tool handlers that emit document-stream SSE side-channel events (a separate SSE channel fro | Accepted | **124** | — | 7/14 | arch test + nightly review | ⛔ FR-08 |
| ADR-050 | Canonical Modal Shell | All Spaarke modals are built on ONE canonical shell — SprkModal (in @spaarke/ui-components) plus | Accepted (2026-08-01) | **113** | — | 1/1 | arch test + nightly review | ✓ |
| ADR-043 🔵 | AI Capability Execution Spine | The execution spine = three surfaces converging at one disposition→ledger→render layer, fed by o | Proposed (2026-07-09, Phase-E kick | **112** | — | 3/4 | arch test (blocking) | ⛔ FR-08 |
| ADR-002 | Dataverse Plugins Are Not an Execution Runti | Dataverse plugins (C# or low-code) are not used as an application runtime. | Accepted | **103** | **named test** | ⚠️ **names nothing** | arch test (blocking) | ✓ |
| ADR-036 | Background-Job Infrastructure (Spaarke.Sched | A new shared library src/server/shared/Spaarke.Scheduling/ provides a uniform contract + host +  | Accepted — shipped in R3 (2026-06- | **103** | unnamed guard | 10/13 | arch test + nightly review | ✓ |
| ADR-037 | Multi-Node Output Composition | Add a new playbook execution node type — NodeType.DeliverComposite — that composes N upstream Ac | Accepted (amended 2026-07-05) | **101** | — | 4/4 | arch test + nightly review | ✓ |
| ADR-047 🔵 | Notification & Action Spine | ONE spine: a producer *grounds + gates* a typed signal → writes a durable, kind-typed outbox row | Proposed (2026-07-21, `spaarke-not | **98** | — | 3/4 | arch test + nightly review | ⛔ FR-08 |
| ADR-041 🔵 | Judgment, Confirmation & Completion Policy | A three-part judgment policy sits on top of ADR-039 dispatch: D-F0 resourcefulness | Proposed (2026-07-09, spec time of | **97** | — | ⚠️ **names nothing** | nightly review | ⛔ FR-08 |
| ADR-031 | Stage Lifecycle Pattern | The SpaarkeAi three-pane shell and its embedded widgets operate under a four-stage lifecycle wit | Accepted | **64** | — | 12/12 | arch test + nightly review | ✓ |
| ADR-042 🔵 | Memory Architecture & Governance | Memory = governed structured objects in exactly TWO active scopes — | Proposed (2026-07-10) — Accepted a | **59** | — | 1/1 | arch test + nightly review | ⛔ FR-08 |
| ADR-023 | Choice Dialog Pattern |  | Superseded — demoted to pattern (2 | **55** | — | ⚠️ **names nothing** | deliberately unenforced | ⛔ FR-08 |
| ADR-017 🟠 | Async Job Status and Persistence | Standardize job status persistence and client contract for all async work. Every job must persis | Proposed | **51** | — | ⚠️ **names nothing** | arch test + nightly review | ⛔ FR-08 |
| ADR-044 | Dataverse GUID Canonicalization at System Bo | Dataverse GUIDs MUST be canonicalized to a single form — bare (no braces) and lowercase — at eve | Accepted | **51** | — | 2/2 | arch test (blocking) | ✓ |
| ADR-048 | Communication Participant Index — Message-Gr | Ship sprk_communicationparticipant — a queryable participant index — governed by five coupled ru | Accepted | **32** | — | 13/13 | arch test + nightly review | ✓ |
| ADR-020 🟠 | Versioning Strategy | Use SemVer for packages, tolerant readers for payloads, and explicit schema versioning for evolv | Proposed | **30** | — | ⚠️ **names nothing** | arch test (blocking) | ⛔ FR-08 |
| ADR-025 | Icon Library and Deployment Strategy | ### 1. Standardize on Fluent UI System Icons (20px Regular) | (none) | **22** | — | ⚠️ **names nothing** | nightly review | ✓ |
| ADR-005 🔴 | Flat Storage in SPE | Use flat storage in SharePoint Embedded containers. No folder hierarchies. Represent hierarchy v | Accepted | **15** | — | 2/3 | arch test (blocking) | ⛔ FR-08 |
| ADR-011 | List & Grid UI — Dataset PCF vs React Code P | Use Dataset PCF for list/grid views that are embedded on Dataverse entity forms (require dataset | Accepted (Revised 2026-02-23) | **15** | — | 0/1 | arch test + nightly review | ✓ |
| ADR-051 | Scrollable Lists Use Infinite Lazy-Scroll, N | Every Spaarke scrollable list / record collection uses infinite lazy-scroll — rows load progress | Accepted (2026-08-31) | **0** | — | ⚠️ **names nothing** | arch test + nightly review | ✓ |
