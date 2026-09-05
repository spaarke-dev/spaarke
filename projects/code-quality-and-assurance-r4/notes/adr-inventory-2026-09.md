# ADR inventory — all 49, for manual utility review

> **Built**: 2026-09-04 (task 010 follow-on, at owner request) · **Sorted by usage** (descending POML citations)
> **Purpose**: let a human judge which ADRs are actually used / useful, and test the premise of each one's utility.
> Companion to [`adr-classification-2026-09.md`](adr-classification-2026-09.md), which carries the three-axis verdicts.

**Legend** — 🟠 orphaned `Proposed` (no gate named, ~2025-12 era) · 🔵 `Proposed` pending a **named gate** (a legitimate state — ADR-039 and ADR-040 were both promoted this way)

**Usage** = citations across all `projects/*/tasks/*.poml`. A proxy for how often the ADR is loaded into an execution context — not a quality measure, and nothing follows automatically from a high or low number.

**Enforcement** — `named test` = an arch test named for the ADR · `unnamed guard` = asserted by a test that never names it in its title · `—` = no mechanical enforcement today.

| ADR | Name | Decision (first line) | Status | Usage | Enforcement |
|---|---|---|---|---|---|
| ADR-021 | Fluent UI v9 Design System | All Spaarke UI must follow the Microsoft Fluent UI v9.x design system. This is the authoritative standard for  | Accepted (Revised 2026-02-23, Updated 20 | **3062** | — |
| ADR-013 | AI Architecture | Default: extend Sprk.Bff.Api with AI endpoints in-process. The bulk of AI synthesis, chat, RAG, safety, capabi | Accepted (amended 2026-07-05) | **2111** | **named test** |
| ADR-028 | Spaarke Auth Architecture (v2) | Adopt function-based auth as the only public contract at every consumer boundary. Eliminate snapshot patterns. | Accepted (resolution path B — amendment, | **1524** | unnamed guard |
| ADR-010 | DI Minimalism | Keep dependency injection minimal and concrete. Register concretes unless a genuine seam exists. | Accepted | **1477** | **named test** |
| ADR-012 | Shared Component Library | Maintain a shared TypeScript/React component library at src/client/shared/Spaarke.UI.Components/ as the single | Accepted (Amended 2026-07-12, 2026-09-04 | **1252** | unnamed guard |
| ADR-015 | AI Data Governance | Apply data minimization and logging hygiene to all AI operations. Never log content; always scope by tenant; d | Accepted (Amended 2026-05-17) | **1100** | — |
| ADR-029 | BFF Publish Hygiene | The Sprk.Bff.Api publish output MUST be framework-dependent linux-x64, MUST exclude wwwroot//*.js.map, MUST pa | Accepted | **1048** | — |
| ADR-022 | PCF Platform Libraries (Field-Bound Controls Only) | Scope: This ADR applies only to field-bound PCF controls (form-embedded controls that use Dataverse bound prop | Accepted (Revised 2026-07-07) | **918** | — |
| ADR-039 | Grounded Execution & Closed Catalogs | Spaarke AI has exactly one dispatch protocol (three entry paths) over | Accepted (2026-07-05) — promoted Propose | **858** | — |
| ADR-008 | Endpoint Filters for Authorization | Use endpoint filters for resource-based authorization, not global middleware. | Accepted | **743** | **named test** |
| ADR-001 | Minimal API + BackgroundService | Run the BFF on a single ASP.NET Core App Service using: | Accepted | **702** | **named test** |
| ADR-024 | Polymorphic Resolver Pattern | Use a dual-field strategy for polymorphic associations where a child record can be related to multiple parent  | Accepted | **583** | — |
| ADR-007 | SpeFileStore Facade | Use single focused facade (SpeFileStore) for all SPE/Graph operations. No generic IResourceStore. Expose only  | Accepted | **559** | **named test** |
| ADR-019 🟠 | ProblemDetails & Error Handling | Use RFC 7807 ProblemDetails for all API errors. Include stable error codes and correlation IDs. For SSE, emit  | Proposed | **555** | — |
| ADR-040 | Session Ledger | Every AI session has an append-only, addressable, typed ledger — the ONLY | Accepted (2026-07-05 at gate G-P0 of | **483** | unnamed guard |
| ADR-006 | UI Surface Architecture — Code Pages, PCF, and Web | All Spaarke frontend UI is built using three surface types, each chosen based on the hosting context — not as  | Accepted (Revised 2026-03-19) | **470** | — |
| ADR-032 | BFF Null-Object Kill-Switch Pattern | For every BFF service T that is registered conditionally (inside a feature-gate if (flag) { … } in a *Module.c | Accepted | **440** | unnamed guard |
| ADR-030 | PaneEventBus — Typed Multi-Subscriber Cross-Pane C | The PaneEventBus is the single authorized cross-pane communication primitive for the SpaarkeAi shell. Five typ | Accepted (v2 — amendment 2026-06-21 adds | **415** | — |
| ADR-014 🟠 | AI Caching and Reuse Policy | Apply AI-specific caching rules on top of ADR-009 Redis-first policy. Cache derived artifacts (text, embedding | Proposed | **414** | — |
| ADR-045 | Communication Architecture — Canonical Send + Asso | Communication (send and receive, email today, any channel later) is governed by four coupled rules: | Accepted | **400** | unnamed guard |
| ADR-018 🟠 | Feature Flags and Kill Switches | Use options-based feature flags with typed validation. Disabled features return 503 ProblemDetails. Flags neve | Proposed | **359** | — |
| ADR-009 | Redis-First Caching | Use Redis as distributed cache. Per-request cache for within-request de-dupe. No hybrid L1+L2 without profilin | Accepted | **335** | **named test** |
| ADR-034 | User-Record Membership Resolution Pattern | Spaarke ships ONE canonical mechanism for "records this user is associated with, by entity type" — replacing t | Accepted — shipped in R3 (2026-06-22; Ph | **306** | unnamed guard |
| ADR-049 | Compose Shadow Document Architecture | The OOXML .docx is the server-authoritative source of truth; the TipTap/ProseMirror editor is a lossy view + c | Accepted, amended three times — R4 (2026 | **305** | unnamed guard |
| ADR-016 🟠 | AI Cost, Rate Limits, and Backpressure | Apply layered throttling to AI operations: per-endpoint rate limiting, bounded concurrency, and explicit budge | Proposed | **296** | — |
| ADR-004 | Async Job Contract | Use one standard Job Contract for all async work. Process via BackgroundService workers with idempotent handle | Accepted | **269** | — |
| ADR-026 | Full-Page Custom Page Standard | Full-page Dataverse surfaces (sitemap entries, navigation pages, side panes) and standalone dialogs/wizards us | Accepted (Revised 2026-03-19) | **233** | — |
| ADR-027 | Subscription Isolation and Dataverse Solution Mana |  | (none) | **214** | — |
| ADR-003 | Lean Authorization Seams | Use two seams only: IAccessDataSource for UAC data, SpeFileStore for storage. Implement authorization via orde | Accepted | **157** | unnamed guard |
| ADR-046 | ACS Messaging Channel — Transport, First-Class Thr | Messaging is a provider on the ADR-045 process, governed by seven coupled rules: | Accepted | **135** | — |
| ADR-033 | Streaming chat-tool side channel | Chat-tool handlers that emit document-stream SSE side-channel events (a separate SSE channel from the chat out | Accepted | **124** | — |
| ADR-050 | Canonical Modal Shell | All Spaarke modals are built on ONE canonical shell — SprkModal (in @spaarke/ui-components) plus a small set o | Accepted (2026-08-01) | **113** | — |
| ADR-043 🔵 | AI Capability Execution Spine | The execution spine = three surfaces converging at one disposition→ledger→render layer, fed by one input model | Proposed (2026-07-09, Phase-E kickoff of | **112** | — |
| ADR-002 | Dataverse Plugins Are Not an Execution Runtime | Dataverse plugins (C# or low-code) are not used as an application runtime. | Accepted | **103** | **named test** |
| ADR-036 | Background-Job Infrastructure (Spaarke.Scheduling) | A new shared library src/server/shared/Spaarke.Scheduling/ provides a uniform contract + host + admin surface  | Accepted — shipped in R3 (2026-06-22; Ph | **103** | unnamed guard |
| ADR-037 | Multi-Node Output Composition | Add a new playbook execution node type — NodeType.DeliverComposite — that composes N upstream Action node outp | Accepted (amended 2026-07-05) | **101** | — |
| ADR-047 🔵 | Notification & Action Spine | ONE spine: a producer *grounds + gates* a typed signal → writes a durable, kind-typed outbox row → best-effort | Proposed (2026-07-21, `spaarke-notificat | **98** | — |
| ADR-041 🔵 | Judgment, Confirmation & Completion Policy | A three-part judgment policy sits on top of ADR-039 dispatch: D-F0 resourcefulness | Proposed (2026-07-09, spec time of `spaa | **97** | — |
| ADR-031 | Stage Lifecycle Pattern | The SpaarkeAi three-pane shell and its embedded widgets operate under a four-stage lifecycle with deterministi | Accepted | **64** | — |
| ADR-042 🔵 | Memory Architecture & Governance | Memory = governed structured objects in exactly TWO active scopes — | Proposed (2026-07-10) — Accepted at gate | **59** | — |
| ADR-023 | Choice Dialog Pattern |  | Superseded — demoted to pattern (2026-03 | **55** | — |
| ADR-017 🟠 | Async Job Status and Persistence | Standardize job status persistence and client contract for all async work. Every job must persist status trans | Proposed | **51** | — |
| ADR-044 | Dataverse GUID Canonicalization at System Boundari | Dataverse GUIDs MUST be canonicalized to a single form — bare (no braces) and lowercase — at every boundary wh | Accepted | **51** | — |
| ADR-048 | Communication Participant Index — Message-Grain Ju | Ship sprk_communicationparticipant — a queryable participant index — governed by five coupled rules: | Accepted | **32** | — |
| ADR-020 🟠 | Versioning Strategy | Use SemVer for packages, tolerant readers for payloads, and explicit schema versioning for evolving contracts. | Proposed | **30** | — |
| ADR-025 | Icon Library and Deployment Strategy | ### 1. Standardize on Fluent UI System Icons (20px Regular) | (none) | **22** | — |
| ADR-005 | Flat Storage in SPE | Use flat storage in SharePoint Embedded containers. No folder hierarchies. Represent hierarchy via Dataverse m | Accepted | **15** | — |
| ADR-011 | List & Grid UI — Dataset PCF vs React Code Page | Use Dataset PCF for list/grid views that are embedded on Dataverse entity forms (require dataset binding). Use | Accepted (Revised 2026-02-23) | **15** | — |
| ADR-051 | Scrollable Lists Use Infinite Lazy-Scroll, Not Pag | Every Spaarke scrollable list / record collection uses infinite lazy-scroll — rows load progressively as the r | Accepted (2026-08-31) | **0** | — |
