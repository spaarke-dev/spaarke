# Current Task State - spaarkeai-mcp-bff-api-r1

> **Last Updated**: 2026-11-12 (mcp-r1, task 002 completed)
> **Recovery**: Read "Quick Recovery" first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 003: Run and record Phase 0 tests T0–T14, E1–E5 (`tasks/003-record-phase0-results.poml`). Phase 0. STANDARD, sonnet / high. |
| **Step** | Not started. |
| **Status** | not-started. Gate "after 001 and 002": 002 ✅ 2026-11-12. **001 is still 🔲 in TASK-INDEX** although the spike (`755987b385`) is built and deployed; check 001's acceptance (the 1/3/6 MB size-test widgets may be part of it) and close or finish it before 003. |
| **Next Action** | Begin Step 1 of task 003: create `notes/phase0-results.md` (one row per test). Import T5 from 002 (passed 2026-11-12) and the 16:12Z T1 evidence (widget rendered inline in Copilot Chat web; `callServerTool` worked). Tell `uac-r2` before the 003 test window. |

### Critical Context
- **T5 passed 2026-11-12:** licensed non-admin `demo@demo.spaarke.com` in Copilot Chat (web), connector **Spaarke MCP** → `spike_whoami` returned that user's `systemuserid` via OBO; aud/scp/tid/oid present. Evidence: `notes/phase0-registration-facts.md` (Summary, (d)).
- **Testing in Copilot needs a Microsoft 365 Copilot licence** on the user (Business Premium alone = "Basic" Copilot Chat, no connector). Start a new chat after a licence change; select Spaarke MCP via **+ › Sources**. Copilot names our tools `SpaarkeMCP_<tool>`.
- **Dev App Insights now samples** MCP requests (25–33%); `tools/call` rows can be missing. The chat reply or widget output is the primary evidence; cross-check identities in Graph/Dataverse.
- Connector app `Spaarke Copilot Connector – dev` (`04f37fe4-…`), confidential, secret only in the Dev Portal registration (expires 2028-11-09). `23610794-…` is the External Access Teams app, never touch it.

---

## Active Lanes
- **Dev BFF runs `6dbe69b9e2`** (2026-11-12, uac-r2): local, never-pushed merge of origin/master `a654da59c4` + spike `755987b385`; ledger count 153. Gates: build clean, ArchTests 2506/2506, /healthz 200, /ping 200, anonymous `POST /mcp/legal-operations` 401. **Standing rule:** every uac-r2 dev deploy includes the spike tip until we declare Phase 0 done. The spike stays off master (PR #2615 draft, never merged).
- This branch: current with master at `d269f0abe7` (+ baseline re-record `c14e4bf3a4`).

## Next Actions
1. 003 (above). The size-test widgets (1/3/6 MB) are not built; they need a new spike SHA and a uac-r2 deploy with the owner's per-SHA go.
2. Phase B: 020 → 021 → 022 / 023 / 024 → 010 → 025; widgets 030–032; registration 040–043; docs 050–052; 090.
3. Owed: dev connector **secret rotation runbook** (task 052); **ADR-028 A10 follow-up note** (Dev Portal requires a secret); user-setup docs must state the Copilot licence requirement.

## Open Owner Questions / Blockers
- What did the demo user see at Connect (sign-in only or a consent prompt)? Record under T5 in `phase0-results.md`.
- The demo account holds two Copilot SKUs (bundle + add-on); one seat can be freed (owner).
- The owner reviews PR #2622 (Compose fix, ISS-002 / #2620).
- Billing review in Copilot (next slice) vs EB-D-194: does not block v1.

## Peer obligations
- **Ontology** (`ontology-r1`): APPROVED 018 (D-212). Ping them when PR #2587 merges to master.
- **uac-r2**: orchestrates dev deploys; the owner gives the per-SHA go there. Told 2026-11-12 that T5 passed and 003 is next.
