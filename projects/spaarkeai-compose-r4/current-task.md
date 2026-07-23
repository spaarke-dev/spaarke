# Current Task State — Spaarke Compose R4

> **Last Updated**: 2026-07-23 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. Branch `work/spaarkeai-compose-r4`.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project** | spaarkeai-compose-r4 (Shadow Document Architecture — hard-replace of Compose save layer) |
| **Progress** | **All code + verification COMPLETE. Stopped at the deploy boundary (owner-orchestrated).** |
| **Status** | ⏸ **Autonomous run paused at deploy boundary** — remaining = 035/062 (deploy), 063 (gate, post-deploy), 090 (wrap-up) |
| **Next Action** | OWNER orchestrates deploy: task 035 (dev) + task 062 (full R4 + CIPO UAT). Then 063 flagship gate + 090 wrap-up. |

### What shipped this session (all committed on work/spaarkeai-compose-r4)
- **036** (`bae44955b`) — RETIRED push-to-Word annotations (Path B); deleted `DocxAnnotationWriter` (last text-search byte-author). I-7 complete.
- **037/033** — resolved **C-revised** (owner): born-in-editor stays on `ComposeDocumentRenderer` (clean-authoring, zero text-search — cited I-5 exception). Two byte-authors kept separate by design.
- **038** (`a5368d5b5`) — **ZERO-ERROR guardrail pass**: unsupported edit-path controls disabled on loaded docs, table gating corrected, hyperlinks disabled, formatted-paste informs via banner, and the critical **op-log-preservation fix** (no rejected save can lose a batch).
- **060** (`da1ab0e94`) — hard-replace core complete (write path fully on the engine; both legacy WRITERS gone). mammoth RETAINED for 3 transient docx mounts (Browse/upload/open-in-Compose) — §6.5 Path-A exception → R5 G6.
- **061** (`0a9710cd1`) — acceptance evidence ALL GREEN: 28/28 corpus byte-diff, 515 server + 531 client tests, publish **46.11 MB** (−3.52 vs 49.63), no new HIGH CVE, ADR-013 NetArch green.

### 🔔 OWNER — the deploy boundary (the only thing left before wrap-up)
1. **Task 035** — deploy patch-engine core to **dev** (verified at 46.11 MB; held for owner).
2. **Task 062** — deploy full R4 + **CIPO operator UAT** (owner-orchestrated per user: "I need to help orchestrate the deploy").
3. After deploy: **063** flagship gate (8 criteria — Criterion 7 "one byte-author" met with the two documented C-revised/mammoth exceptions), then **090** wrap-up (+ `/test-diet`).

### Owner decisions on record (this session)
- **036 → Path B (retire push-annotations).** **037 → C-revised (keep renderer, cite I-5 exception).** **060 → Path-A (retain mammoth for transient mounts → R5).**
- **Scope boundary**: R4 = keep two authors separate, ship **error-free with documented functional limits**; editing-completeness deferred to **`projects/spaarkeai-compose-r5`** (gaps G1–G6, code-grounded + sized).
- **KEY constraint honored**: no user-triggerable errors, no silent data loss (task 038 + the transient-mount limit is pre-existing/non-error).

## Deferred to R5 (`projects/spaarkeai-compose-r5/README.md`)
G1 cross-session authored-doc clean lifecycle · G2 clean-apply mode · G3 setBlockAttr applier (headings/lists/alignment on edit path) · G4 table op (tracked) · G5 hyperlinks · G6 transient-mount projection unification (removes mammoth). None requires merging the two byte-authors.

## Health
- Every task build-verified + committed. Server Compose 515/515; client 531/531; 038 guardrails 64/64; corpus byte-diff 28/28. Publish 46.11 MB compressed (≤60). Only HIGH CVE = pre-existing `System.Security.Cryptography.Xml` transitive. ADR-007 arch failure = pre-existing Communication/Office (zero Compose types).

## How to resume
`/project-continue` or "where was I?". Deploy is owner-led — do NOT run 035/062 autonomously. After deploy, 063 → 090.

## Portfolio
[Project #679](https://github.com/spaarke-dev/spaarke/issues/679) · Branch `work/spaarkeai-compose-r4`.
