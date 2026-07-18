# Messaging Communication App — R1

> **Status**: ✅ **Complete** — 27/27 work tasks + 090 wrap-up done; BFF + PCFs deployed to dev; merged to master. (Owner config gates + one product follow-up remain — see "Post-completion" below.)
> **Created**: 2026-07-16 via `/project-pipeline`
> **Completed**: 2026-07-18
> **Branch**: `work/messaging-communication-app-r1`
> **Portfolio**: [Project #654](https://github.com/spaarke-dev/spaarke/issues/654) · Epic [#431 EMAIL & MESSAGING](https://github.com/spaarke-dev/spaarke/issues/431) · [Board #2](https://github.com/users/spaarke-dev/projects/2) · Status: **Archived** · Start 2026-07-16 · Completed 2026-07-18
> **Follow-on**: [`messaging-communication-app-r2`](../messaging-communication-app-r2/) (Communication Workspace) — draft design; 4/5 owner decisions locked 2026-07-18.

Add **messaging (real-time chat) as the second channel** on Spaarke's existing communication platform — not a new module or pipeline. Azure Communication Services (ACS) Chat is the transport; Dataverse `sprk_communication` is the system of record; the BFF is the sole policy-enforcement and token-minting point. R1 delivers the server-side plumbing (channel provider + inbound ingestor + ACS integration), a first-class communication thread data model, and a usable **async (polling)** message experience in the MDA. The live open channel, spine-pushed notifications, SMS, and Teams/portal surfaces are deferred to the next project / R2 / R3.

---

## Documents

| File | Purpose |
|---|---|
| [`spec.md`](spec.md) | AI-optimized spec (18 FRs, 8 NFRs) — implementation source of truth |
| [`design.md`](design.md) | Human design (rev 2) — rationale, current-state truth, hot-path declaration |
| [`plan.md`](plan.md) | Wave WBS + critical path + discovered resources + coordination |
| [`current-task.md`](current-task.md) | Active task state (context recovery) |
| [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) | All tasks + status + parallel groups + dependency graph |
| [`spaarke-messaging-solution-synopsis.md`](spaarke-messaging-solution-synopsis.md) | Original idea source (folded into design) |

---

## Graduation Criteria (spec Success Criteria)

> Legend: ✅ built + verified · 🔧 built + deployed, **gated on owner config** · ⚠️ built, product follow-up open

1. [🔧] User sends a message from the MDA; persists **once** as `sprk_communication` (type=Message), threaded, appears in the polling timeline within ~5s (echo-dedup). — *code + PCFs deployed; live send gated on owner ACS-endpoint (set on dev) + Share privilege.*
2. [✅] Inbound ACS message captured via Event Grid appears in the thread; duplicate delivery yields **one** record. — *idempotent capture (dedupe on ACS msg id) built + tested; live Event Grid sub deferred (needs reachable BFF).*
3. [✅] An email reply and a chat message in the same conversation both carry a `sprk_thread`; the timeline renders them grouped. — *`IThreadResolver` + interleaved timeline.*
4. [✅] Existing email inbound association still passes after the `IThreadResolver` extension (characterization tests green). — *414→421 characterization, 0 email regressions.*
5. [🔧] A private thread's messages are invisible without a grant; internal-only invisible to non-internal (security review). — *impersonation-based read filter built (task 042); **gated on owner: app-user Delegate role + tables Read=User-level**.*
6. [🔧] A 1:1 direct thread works with explicit two-participant membership. — *built (task 043); **gated on owner: app-user Share privilege on both messaging tables**.*
7. [✅] An email's content quotes into a new message and vice-versa. — *`quoteBody` helper, XSS-safe, 20 tests.*
8. [⚠️] A message attachment lands in SPE as a linked `sprk_document`, policy-enforced. — *materializer built + CHAT-ATTACHMENT-POLICY enforced; **see open finding: message SEND path never invokes the archiver** (no SPE transcript per chat message).*
9. [✅] BFF publish size ≤ 60 MB, delta reported. — *~46.99 MB peak (task 043), well under ceiling.*
10. [✅] ADR-046 authored (concise + full); INDEX updated to Accepted.
11. [✅] No client-side ACS SDK import in R1 client code. — *grep-verified 0 in both PCF bundles (NFR-04).*

## Post-completion — owner actions + open items

**Owner config gates** (make deployed features functional):
- Set `Communication__Acs__Endpoint` on **staging/prod** (dev is set) — required for message SEND.
- App-user **Delegate role + both messaging tables Read = User-level** → enables impersonated reads (crit. 5).
- App-user **Share privilege on both messaging tables** → enables Direct/Open message-access grants (crit. 6).

**Open findings** (tracked for follow-up, not R1 blockers):
- **Messaging-archival gap (MED)** — `SendMessageAsync` never calls `ResolveArchiver`/`ArchiveToSpeAsync`; chat messages get no SPE transcript. `MessagingArchiver` is real + unit-tested but uncalled on send. Decide: wire archival into the send path, or spec/ADR-correct if descoped for chat. (Source: `notes/080-seam-coverage-map.md` Finding 1, `notes/test-diet-report.md`.)
- **Send-into-existing-ACS-thread reuse (R2)** — R1 does Dataverse thread association on send; reusing the live ACS chat thread is deferred to R2.
- **DI-cycle refactor (LOW)** — task 052 broke a real 3-node cycle with `Lazy<>`; a future refactor could extract the participant-reader to remove it entirely.

---

## Scope Guardrails (owner-locked MUST NOT)

- ❌ No Dataverse/Power Apps **Activities**, OOB `email`/activity entities, or **portal comments**.
- ❌ No **native Teams chat** capture (Graph chat/channel-message APIs).
- ❌ No second communication pipeline, second regarding mechanism, or second channel-provider contract.
- ❌ No **live client / ACS client SDK / ACS composites** in R1 (that is the next project).
- ❌ AI may **flag** privilege, never **decide** (ADR-015).
- ❌ No messaging-only notification hub (R1 polls; R2 consumes `notification-spine-r1`).

---

## Execution

All task work MUST use the `task-execute` skill (root CLAUDE.md §4). To begin:

```
work on task 001
```

or `continue` to pick up the first 🔲 in [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md).

**Coordination**: run `/conflict-check` at start and before every BFF wave — this project edits shared `Services/Communication/` code. Coordinate the `threadId` contract with `spaarke-notification-spine-r1`.
