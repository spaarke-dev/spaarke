# PLAN — Messaging Communication App R1

> **Generated**: 2026-07-16 via `/project-pipeline`
> **Source**: [`spec.md`](spec.md) (18 FRs, 8 NFRs) · [`design.md`](design.md) (rev 2)
> **Branch**: `work/messaging-communication-app-r1`
> **Builds on (complete)**: `email-communication-solution-r4` — ADR-045 channel seams shipped
> **Coordinates with**: `spaarke-notification-spine-r1` (messaging is its R2 consumer)

---

## 1. Objective

Add **messaging (real-time chat) as the second channel** on Spaarke's existing communication platform. ACS Chat is transport; Dataverse `sprk_communication` is the record; the BFF is the sole policy-enforcement + token-minting point. R1 delivers server-side plumbing (channel provider + inbound ingestor + ACS integration), a **first-class communication thread data model**, and a **usable async (polling) message experience** in the MDA. **No live channel, no client-side ACS SDK, no notification fabric** in R1 (those are the next project / R2 / R3).

**Graduation** = all 11 spec Success Criteria met (see [`README.md`](README.md)).

---

## 2. Architecture Context — Discovered Resources

### Applicable ADRs (from spec §Technical Constraints)

| ADR | Relevance to R1 |
|---|---|
| **ADR-045** | Communication architecture / channel seams — the spine this extends (sender/archiver shipped; **ingestor is net-new**). |
| **ADR-046** (NEW) | ACS messaging channel — placeholder reserved main-session; **authored in this project** (task 007). |
| **ADR-034** | `MembershipResolverService` → open-thread membership derivation. |
| **ADR-028** | Auth v2 — server-side ACS token minting; central `TokenCredential`; MUST NOT `new` a credential. |
| **ADR-004 / ADR-036** | Job contract / background-job infra (Event Grid capture, membership reconcile). |
| **ADR-007** | `SpeFileStore` facade — transcript + attachment archive. |
| **ADR-024** | Polymorphic regarding family — thread anchor; MUST NOT add a second regarding mechanism. |
| **ADR-027** | Subscription isolation / per-customer resource provisioning (ACS + Event Grid). |
| **ADR-018 / ADR-032** | Kill-switch / Null-Object for feature-gated services + unconditional endpoint registration. |
| **ADR-008 / ADR-003 / ADR-010 / ADR-019** | Endpoint filters; authorization seams; DI minimalism; ProblemDetails. |
| **ADR-021 / ADR-022 / ADR-026 / ADR-006 / ADR-012** | Fluent v9; PCF platform libs (React 16/17); Code Page standard; UI surface arch; shared component library. |
| **ADR-038 / ADR-029** | Testing strategy (integration-heavy, vertical-slice seam tests); BFF publish hygiene. |
| **ADR-013** | AI facade (`Services/Ai/PublicContracts/`) — only if enrichment/AI is touched. |
| **ADR-015** | AI may flag but never decide (privilege classification). |

### Existing patterns / canonical implementations to follow

- `Services/Communication/Channels/EmailChannelSender.cs` + `EmailArchiver.cs` + `CommunicationChannelDispatcher.cs` — **the seam + dispatch pattern to mirror** (email-r4 task 016).
- `Services/Communication/IncomingCommunicationProcessor.cs` + `Engine/` — inbound capture + normalizer + rung pattern to mirror/extend.
- `Services/Jobs/` — job contract (idempotency, DLQ, retry).
- `Infrastructure/Graph/SpeFileStore.cs` + `Services/Communication/GraphAttachmentAdapter.cs` — attachment → SPE → `sprk_document`.
- `IMembershipResolverService` (ADR-034) — open-thread membership.
- `PlaybookSharingService` / `GrantExternalAccessEndpoint` / `sprk_externalrecordaccess` — private-grant precedents.
- email-r4 PCFs `CommunicationActions` / `CommunicationConnections` + `<EmailComposer/>` — OOB-form + PCF surface pattern.
- **Microsoft reference impls**: `Azure-Samples/communication-services-authentication-hero-csharp` (BFF trusted-service token minting + identity mapping); `...-dotnet-quickstarts` (thread/participant/send server code).

### Applicable skills

`dataverse-create-schema` / `dataverse-deploy` (schema), `pcf-deploy` (timeline PCF + accessories), `fluent-v9-component` (timeline component), `bff-deploy` (BFF), `adr-check` + `code-review` (Step 9.5 gates), `conflict-check` (BFF hot-path), `researcher` (ACS knowledge — already run 2026-07-16, memoized).

### Knowledge / constraints

- `.claude/constraints/bff-extensions.md` — **binding** BFF-addition checklist (load before every BFF task).
- `docs/standards/CHAT-ATTACHMENT-POLICY.md` — message-attachment sizing (reused verbatim).
- `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md` — `Xrm.WebApi` vs BFF (timeline reads via BFF).
- `docs/standards/MODAL-DECISION-CRITERIA.md` — for any modal surface.
- `.claude/agent-memory/researcher/acs-chat-integration-2026-07-16.md` — ACS integration findings (trusted-service model, token minting, Event Grid idempotency).

---

## 3. Hot-Path Declaration (root §10 / FR-C04)

```xml
<hot-path-declaration>
  <bff>Y</bff>                  <!-- endpoints, ACS integration, ingestor, capture job, thread resolver -->
  <spaarke-ai>N</spaarke-ai>    <!-- MDA PCFs, not the SpaarkeAi code page -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>   <!-- ADR-046 authored in .claude/adr/ main-session, but no skill edits -->
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

**⚠️ Hot-path overlap (from `projects/INDEX.md`)**: `email-communication-solution-r4` (BFF=Y, `Services/Communication/**` primary) is the sibling that shipped the seams this project extends. R1's `IThreadResolver` (task 040) **edits shared `Services/Communication/` code** (`ThreadContinuityRung`, `CommunicationService`). `spaarke-notification-spine-r1` also touches this area (messaging is its R2 consumer). **Run `/conflict-check` at project start and before every BFF wave.** Align the `threadId` contract + `kind` taxonomy with notification-spine at joint intake (not an R1 blocker — R1 polls).

### Placement Justification (root §10)

Messaging endpoints + ACS integration + inbound ingestor **live in the existing BFF** (D-07): it is the sole policy-enforcement + ACS-token-minting + `sprk_communication` mutation point. A separate service would fork enforcement and the association engine. Cite `.claude/constraints/bff-extensions.md` on every BFF-touching PR. ACS BFF SDKs (`Azure.Communication.Chat` 1.4.0 + `Azure.Communication.Identity` 1.3.1) are thin over `Azure.Core` — negligible vs the 60 MB ceiling (**~45.30 MB** baseline post-R4).

---

## 4. Phase Breakdown (Wave WBS)

Baseline BFF publish size: **~45.30 MB** compressed (post-R4). Ceiling ≤60 MB. Report absolute + delta on every BFF-touching task.

### W0 — Phase 0 Verification + Foundation
Lean delta validation (design §12) + schema + enum + ADR. Spikes de-risk ACS and the private-grant mechanism before build.
- **001** Phase-0: confirm live `sprk_communication` schema + `Message=100000004` choice integer (MCP read) — *spike*
- **002** Phase-0: private-grant mechanism decision (`GrantAccess` vs `sprk_externalrecordaccess`) — *spike, security-sensitive*
- **003** Phase-0: ACS spike (identity + server-minted token + thread + Event Grid round-trip; latency; publish-size delta) — *spike*
- **004** Schema: `sprk_communicationthread` entity + `sprk_thread` lookup + thread↔channel child table (FR-05)
- **005** Schema: `sprk_communication` new columns (privacy, internal-only, privilege, ACS `messageId`/`chatThreadId`) + `communicationUserId` on user/contact (FR-08 support)
- **006** `CommunicationType.Message = 100000004` C# enum extension (FR-16)
- **007** Author **ADR-046** (concise + full); INDEX placeholder → Accepted (FR-17) — *main-session (`.claude/`)*

### W1 — ACS Integration (server-side only)
Net-new `Acs/` folder. Leverage the auth-hero-csharp sample. No client SDK.
- **010** ACS identity + server-side token minting (`communicationUserId ↔ Dataverse`; uniform chat-scope minting) (FR-03)
- **011** ACS thread + membership ops (create thread w/ 30-day retention; Add/RemoveParticipants) (FR-15)
- **012** ACS provisioning: per-customer resource + Event Grid system topic/subscriptions in provisioning orchestrator (ADR-027) (FR-18)

### W2 — Channel Provider (ADR-045 seams — the second impl)
- **020** `MessagingChannelSender` + `MessagingArchiver` (`ICommunicationChannelSender`/`ICommunicationArchiver` impls; dispatch by `Message`; transcript → SPE) (FR-01)
- **021** `ICommunicationChannelIngestor` seam (**net-new**) + `MessagingIngestor` (FR-02, seam part)

### W3 — Inbound Capture Pipeline (Event Grid → persist)
- **030** Event Grid webhook ingress (subscription-validation handshake) → Service Bus job (FR-02)
- **031** ACS-event normalizer → `NormalizedMessage` → idempotent persist (dedupe on ACS message id — echo-dedup) + DLQ to Storage (FR-02, FR-04, NFR-03)

### W4 — Thread Resolver + Membership + Privacy (shared-path; security-sensitive)
- **040** `IThreadResolver` (direction-symmetric find-or-create) — extend `ThreadContinuityRung` (inbound) + `CommunicationService` (outbound); **characterization-test existing email flows first** (FR-06)
- **041** Membership derivation + reconcile job (open via ADR-034; reconcile ACS from Dataverse access — event + sweep) (FR-07)
- **042** Privacy / internal-only / privilege — BFF query-filter enforcement, point-forward (FR-08) — *security-sensitive, opus @ xhigh*
- **043** 1:1 direct threads — explicit two-participant membership (FR-09)

### W5 — BFF Endpoints (drive the poll + outbound)
- **050** BFF thread-read + unread-count endpoints (access-filtered per FR-08) (FR-11)
- **051** Outbound send path — persist-on-send + echo-dedup wiring via `CommunicationService` messaging dispatch (FR-04)

### W6 — UI (polling timeline + PCF accessories) — MDA surface
ADR-026 Path-A exception (OOB form + PCFs). No client-side ACS SDK.
- **060** Polling conversation/timeline component in `@spaarke/ui-components` (Fluent v9): interleaved email+chat, reply nesting, compose box, unread indicator, ~5s poll; reuse `<EmailComposer/>` sub-components (FR-10)
- **061** Package timeline as PCF + deploy to OOB form (FR-10) — *pcf, deploy, e2e-test*
- **062** PCF send/respond accessories on OOB `sprk_communication`/thread form (mirror `CommunicationActions`) (FR-12)
- **063** Bidirectional inline content quoting (email↔message) via `sprk_body` (FR-13)

### W7 — Attachments
- **070** Message attachment materialization (ACS/file → SPE → `sprk_document` → intersection); enforce `CHAT-ATTACHMENT-POLICY.md` (FR-14)

### W8 — Tests + Docs
- **080** Vertical-slice seam tests: messaging send/archive/ingest, `IThreadResolver`, privacy enforcement; preserve existing email-inbound characterization (NFR-08)
- **081** Architecture doc: extend communication architecture with the thread model + ACS transport + ingestor seam (wire ADR-046) (FR-17 support)

### Wrap
- **090** Project wrap-up (README Complete, lessons-learned, `/test-diet`, archive) — *main-session*

---

## 5. Critical Path

```
001 → 004 → 005 → 006 → 020 → 021 → 030 → 031 → 040 → 050 → 051 → 060 → 061 → 080 → 090
                    (003 ACS spike gates 010/011/012 → 020/021)
```

The genuine serial spine: schema (004/005/006) → channel provider (020/021) → inbound (030/031) → thread resolver (040) → endpoints (050/051) → UI (060/061) → tests (080). ACS integration (W1) runs parallel after the 003 spike and feeds 020/021/030. Privacy (042) is the highest-risk security-sensitive item and gates 050's access-filtering.

## 6. Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| **W0-A** | 001, 002, 003, 007 | — | Spikes + ADR (007 main-session `.claude/`); 002 security-sensitive |
| **W0-B** | 004, 005, 006 | 001 | Schema + enum (parallel; distinct surfaces) |
| **W1** | 010, 011, 012 | 003 (ACS spike) | ACS server integration; 010→011 loosely ordered (identity before thread ops) |
| **W2** | 020, 021 | 006 + W1 (010/011) | Channel provider seams; 021 adds ingestor seam |
| **W3** | 030 → 031 | 021 + 012 (Event Grid) | Inbound capture; 031 needs 030's job + idempotency |
| **W4** | 040 → {041, 042, 043} | 020/021 + 005 | 040 serializes the shared-path edit; 041–043 parallel after |
| **W5** | 050, 051 | 040 + 042 (privacy filter) | Endpoints; 050 read + 051 write |
| **W6** | 060 → 061, 062, 063 | 050/051 | Timeline component then PCF packaging + accessories + quoting |
| **W7** | 070 | 020 (archiver) + 005 | Attachment materialization |
| **W8** | 080, 081 | W1–W7 substantially complete | Seam tests + doc |

**Max concurrency**: 6 agents/wave. `.claude/`-touching tasks (007) + wrap-up (090) run main-session, sequential.

**Model tiers (per CLAUDE.md §8.5)**: default **sonnet @ high**. **opus** on: **007** (ADR authoring), **040** (shared-path engine extension — direction-symmetric resolver over the frozen email path), **042** (privacy enforcement — security-sensitive). **effort: xhigh** on: **031** (idempotent capture / echo-dedup correctness), **040**, **042**, **051** (echo-dedup wiring). All others sonnet @ high.

## 7. High-Risk / Watch Items

- **042 (privacy/privilege)** — R1's highest-risk area (NFR-06, security-sensitive). Explicit `code-review` + `adr-check` at Step 9.5; ACS membership never exceeds Dataverse-derived access; point-forward switch.
- **040 (`IThreadResolver`)** — edits shared `Services/Communication/` code that email-r4 shipped. Characterization tests for existing email flows MUST stay green before extending. `/conflict-check` before PR.
- **031 (idempotent capture)** — Event Grid at-least-once + duplicates + own-echo; dedupe on ACS message id via `IIdempotencyService`; DLQ from day one.
- **003 (ACS spike)** — ACS entirely net-new; gates W1/W2. Measure send→persist latency, echo-dedup, publish-size delta before committing to the build.
- **notification-spine coordination** — align `threadId` contract + `kind` taxonomy at joint intake; not an R1 blocker (R1 polls).
- Every BFF-touching task: `/conflict-check` before PR + publish-size + CVE report (root §10).

## 8. FR Coverage

FR-01→020 · FR-02→021,030,031 · FR-03→010 · FR-04→031,051 · FR-05→004 · FR-06→040 · FR-07→041 · FR-08→005,042 · FR-09→043 · FR-10→060,061 · FR-11→050 · FR-12→062 · FR-13→063 · FR-14→070 · FR-15→011 · FR-16→006 · FR-17→007,081 · FR-18→012. NFRs distributed (031,042,080; publish-size on every BFF task).

## 9. References

- [`spec.md`](spec.md) · [`design.md`](design.md) · [`spaarke-messaging-solution-synopsis.md`](spaarke-messaging-solution-synopsis.md)
- Root `CLAUDE.md` §10 (BFF Hygiene) + §11 (Component Justification) + §6.5 (ADR Conflict Resolution)
- `.claude/constraints/bff-extensions.md` · `docs/standards/CHAT-ATTACHMENT-POLICY.md`
- `projects/INDEX.md` (hot-path registry) · sibling `projects/email-communication-solution-r4/`
