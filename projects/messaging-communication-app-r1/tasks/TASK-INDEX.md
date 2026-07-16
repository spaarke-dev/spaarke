# TASK-INDEX — messaging-communication-app-r1

> **Generated**: 2026-07-16 via `/project-pipeline`
> **Total tasks**: 28 (27 work + 1 wrap-up)
> **Legend**: 🔲 not-started · 🔄 in-progress/needs-retry · ✅ complete · ⛔ blocked/gated

---

## Task Registry

| # | Title | Wave | Tags | FR | Deps | Blocks | Parallel-safe | Rigor | Model/Effort | Status |
|---|---|---|---|---|---|---|---|---|---|---|
| 001 | Phase-0: audit live `sprk_communication` schema + confirm `Message=100000004` choice integer (MCP read) | W0 | dataverse, spike | — | — | 004,005,006 | true | STANDARD | sonnet/high | ✅ (live Dataverse MCP; Message=100000004 confirmed; 6 cols + communicationUserId absent→clean adds; grouping stays (A)) |
| 002 | Phase-0: decide private-thread grant mechanism (`GrantAccess` vs `sprk_externalrecordaccess`) | W0 | spike, security | — | — | 042 | true | STANDARD | sonnet/high | ✅ (**option B APPROVED by owner 2026-07-16** — sprk_externalrecordaccess overlay ∪ ADR-034; binds 042/041/050) |
| 003 | Phase-0: ACS spike (identity + server-minted chat token + thread + Event Grid round-trip; latency, echo-dedup, publish-size) | W0 | spike, acs | — | — | 010,011,012,020,030 | true | STANDARD | sonnet/high | ✅ (ACS harness compile-verified vs real SDKs; publish delta **+0.22 MB MEASURED**→~45.52; echo-dedup key=ACS msg id; latency needs live infra; researcher-memory gap flagged) |
| 004 | Schema: `sprk_communicationthread` entity + `sprk_thread` lookup + thread↔channel child table | W0 | dataverse, schema | FR-05 | 001 | 040,050 | true | STANDARD | sonnet/high | ✅ (owner-created + **verified live** 2026-07-16: sprk_communicationthread entity + sprk_communicationchannelref child + sprk_thread lookup; anchor REUSES sprk_regardingrecord* — no sprk_anchor*) |
| 005 | Schema: `sprk_communication` privacy/internal-only/privilege/ACS-key columns + `communicationUserId` on user/contact | W0 | dataverse, schema | FR-08 | 001 | 042,010 | true | STANDARD | sonnet/high | ✅ (owner-created + **verified live**: lookup as-built **`sprk_communicationthread`** [not sprk_thread]; ACS field as-built **`sprk_acsthreadid`** [not sprk_acschatthreadid]; isprivate/isinternalonly/privilegeclassification/acsmessageid + communicationuserid on user+contact — see AS-BUILT table in messaging-schema-spec.md) |
| 006 | `CommunicationType.Message = 100000004` C# enum extension | W0 | bff-api | FR-16 | 001 | 020 | true | FULL | sonnet/high | ✅ (enum +Message=100000004; build clean, 352 Comm tests pass; gates PASS; publish ~0 delta; pre-existing Kiota HIGH CVE noted, not a regression) |
| 007 | Author **ADR-046** (ACS messaging channel) — concise + full; INDEX placeholder → Accepted | W0 | adr, docs | FR-17 | — | 020,021 | **false** (`.claude/`) | STANDARD | opus/high | ✅ (main-session; ADR-046 concise→Accepted + full docs/adr authored + INDEX updated; ADR-047 reserved for notification-spine) |
| 010 | ACS identity + server-side chat-token minting (`communicationUserId ↔ Dataverse`; uniform minting) | W1 | bff-api, acs, auth | FR-03 | 003,005 | 011,020,051 | true | FULL | sonnet/high | ✅ (Services/Communication/Acs/ IAcsIdentityService+impl; central TokenCredential ADR-028; sprk_communicationuserid persist; 7 tests; build clean; publish +~0.1MB; no new CVE; gates clean. Live ACS deferred) |
| 011 | ACS thread + membership ops (create thread w/ 30-day retention; Add/RemoveParticipants) | W1 | bff-api, acs | FR-15 | 010 | 020,041,051 | true | FULL | sonnet/high | 🔲 |
| 012 | ACS provisioning: per-customer resource + Event Grid system topic/subscriptions (ADR-027) | W1 | bff-api, acs, provisioning | FR-18 | 003 | 030 | true | FULL | sonnet/high | ✅ (Bicep-driven ADR-027: acs-communication.bicep + customer.bicep; ADR-032 Null-Object DeferredAcsBoundaryProvisioner in RegistrationModule [Path-A vs CommunicationModule to avoid 010 conflict]; 8 tests; publish ≈0; runbook notes/012-acs-provisioning-runbook.md. Live provisioning deferred) |
| 020 | `MessagingChannelSender` + `MessagingArchiver` (ADR-045 seam impls; dispatch by `Message`; transcript → SPE) | W2 | bff-api, communication, acs | FR-01 | 006,007,010,011 | 051,070 | true | FULL | sonnet/high | 🔲 |
| 021 | `ICommunicationChannelIngestor` seam (net-new) + `MessagingIngestor` | W2 | bff-api, communication, architecture | FR-02 | 007 | 030,031 | true | FULL | sonnet/high | 🔲 |
| 030 | Event Grid webhook ingress (subscription-validation handshake) → Service Bus job | W3 | bff-api, communication, jobs | FR-02 | 012,021 | 031 | true | FULL | sonnet/high | 🔲 |
| 031 | ACS-event normalizer → `NormalizedMessage` → idempotent persist (dedupe on ACS message id) + DLQ | W3 | bff-api, communication, jobs | FR-02,04 | 030,021 | 040,051 | false (serial after 030) | FULL | sonnet/xhigh | 🔲 |
| 040 | `IThreadResolver` (direction-symmetric) — extend `ThreadContinuityRung` + `CommunicationService`; email characterization first | W4 | bff-api, communication, refactoring | FR-06 | 004,031 | 041,042,043,050,051 | **false** (shared-path serial) | FULL | opus/xhigh | 🔲 |
| 041 | Membership derivation + reconcile job (open via ADR-034; reconcile ACS from Dataverse; event + sweep) | W4 | bff-api, communication, jobs | FR-07 | 040,011 | — | true | FULL | sonnet/high | 🔲 |
| 042 | Privacy / internal-only / privilege — BFF query-filter enforcement, point-forward | W4 | bff-api, communication, security | FR-08 | 040,005,002 | 050 | true | FULL | opus/xhigh | 🔲 |
| 043 | 1:1 direct threads — explicit two-participant membership | W4 | bff-api, communication | FR-09 | 040 | — | true | FULL | sonnet/high | 🔲 |
| 050 | BFF thread-read + unread-count endpoints (access-filtered per FR-08) | W5 | bff-api, communication | FR-11 | 040,042,004 | 060 | true | FULL | sonnet/high | 🔲 |
| 051 | Outbound send path — persist-on-send + echo-dedup wiring (messaging dispatch) | W5 | bff-api, communication, acs | FR-04 | 020,031,040,010,011 | 062 | true | FULL | sonnet/xhigh | 🔲 |
| 060 | Polling conversation/timeline component (`@spaarke/ui-components`, Fluent v9): interleaved email+chat, reply nesting, compose box, unread indicator, ~5s poll | W6 | frontend, fluent-ui | FR-10 | 050 | 061,063 | true | FULL | sonnet/high | 🔲 |
| 061 | Package timeline as PCF + deploy to OOB form + UI tests | W6 | pcf, deploy, e2e-test, dataverse | FR-10 | 060 | — | true | FULL | sonnet/high | 🔲 |
| 062 | PCF send/respond accessories on OOB `sprk_communication`/thread form (mirror `CommunicationActions`) | W6 | pcf, frontend, communication | FR-12 | 051,060 | — | true | FULL | sonnet/high | 🔲 |
| 063 | Bidirectional inline content quoting (email↔message) via `sprk_body` | W6 | frontend, communication | FR-13 | 060 | — | true | FULL | sonnet/high | 🔲 |
| 070 | Message attachment materialization (ACS/file → SPE → `sprk_document` → intersection); enforce `CHAT-ATTACHMENT-POLICY.md` | W7 | bff-api, communication, spe | FR-14 | 020,005 | — | true | FULL | sonnet/high | 🔲 |
| 080 | Vertical-slice seam tests: messaging send/archive/ingest, `IThreadResolver`, privacy; preserve email-inbound characterization | W8 | bff-api, testing | NFR-08 | 031,040,042,051 | — | true | TEST-MODIFYING | sonnet/high | 🔲 |
| 081 | Architecture doc: thread model + ACS transport + ingestor seam (wire ADR-046) | W8 | docs | FR-17 | 040,007 | — | true | STANDARD | sonnet/high | 🔲 |
| 090 | Project wrap-up (README Complete, lessons-learned, `/test-diet`, archive) | Wrap | wrapup | — | (all) | — | **false** | STANDARD | sonnet/high | 🔲 |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| **W0-A** | 001, 002, 003, 007 | — | Spikes + ADR (007 main-session `.claude/`); 002 security-sensitive; 003 gates all ACS work |
| **W0-B** | 004, 005, 006 | 001 | Schema + enum (distinct surfaces, parallel) |
| **W1** | 010 → 011; 012 (‖) | 003 (+005 for 010) | ACS server integration; identity before thread ops; 012 parallel |
| **W2** | 020, 021 | 006 + 007 + W1 | Channel provider seams; 021 adds net-new ingestor seam |
| **W3** | 030 → 031 | 012 + 021 | Inbound capture; 031 serial after 030 (idempotency/echo-dedup) |
| **W4** | **040 (serial)** → {041, 042, 043} | 004 + 031 | 040 serializes shared-path edit; 041–043 parallel after |
| **W5** | 050, 051 | 040 + 042 (050); 020/031/040 (051) | Endpoints — read + write |
| **W6** | 060 → {061, 062, 063} | 050 (+051 for 062) | Timeline component then PCF packaging + accessories + quoting |
| **W7** | 070 | 020 + 005 | Attachment materialization (parallel track after W2) |
| **W8** | 080, 081 | W1–W7 substantially complete | Seam tests + doc |

**Max concurrency**: 6 agents/wave. `.claude/`-touching tasks (007) + wrap-up (090) run main-session, sequential.

**Model tiers (per CLAUDE.md §8.5)**: default **sonnet @ high**. **opus** on: **007** (ADR authoring), **040** (shared-path engine extension over the frozen email path), **042** (privacy — security-sensitive). **effort: xhigh** on: **031** (idempotent capture / echo-dedup), **040**, **042**, **051** (echo-dedup wiring). All others sonnet @ high.

## Critical Path

```
001 → 004 → 040 → 050 → 060 → 061 → 080 → 090
003 → 010/011 → 020 → 021 → 030 → 031 → 040  (ACS + inbound spine feeds 040)
```

## High-Risk / Watch Items

- **042 (privacy/privilege)** — R1's highest-risk area (NFR-06, security-sensitive). Explicit `code-review` + `adr-check` at Step 9.5; ACS membership never exceeds Dataverse-derived access; point-forward switch.
- **040 (`IThreadResolver`)** — edits shared `Services/Communication/` code (email-r4). Characterization tests for existing email MUST stay green before extending. `parallel-safe: false`. `/conflict-check` before PR.
- **031 (idempotent capture)** — Event Grid at-least-once + duplicates + own-echo; dedupe on ACS message id via `IIdempotencyService`; DLQ from day one.
- **003 (ACS spike)** — gates W1/W2; measure send→persist latency, echo-dedup, publish-size before build.
- **notification-spine coordination** — align `threadId` contract + `kind` taxonomy at joint intake; not an R1 blocker (R1 polls).
- Every BFF-touching task: `/conflict-check` before PR + publish-size + CVE report (root §10).

## FR Coverage

FR-01→020 · FR-02→021,030,031 · FR-03→010 · FR-04→031,051 · FR-05→004 · FR-06→040 · FR-07→041 · FR-08→005,042 · FR-09→043 · FR-10→060,061 · FR-11→050 · FR-12→062 · FR-13→063 · FR-14→070 · FR-15→011 · FR-16→006 · FR-17→007,081 · FR-18→012. NFRs distributed (031,042,080; publish-size on every BFF task).
