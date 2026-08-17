# Coordination Baseline — spaarkeai-compose-r7 (Task 001)

> **Task**: 001 — Coordination gate + publish-size baseline + env verification
> **Phase**: 0 (Coordination Gate + Baseline)
> **Authored**: 2026-08-15
> **Rigor**: MINIMAL (read-only + note authoring; no source modified)
> **Branch**: `work/spaarkeai-compose-r7` @ `8695b7145`

This note is the **shared baseline** cited by every downstream BFF task (010/013/030/050/051/073/074) for NFR-01 delta reporting, and by FR-06 tasks (050/051) for the DI-gate precondition. Gate verdict at bottom.

---

## Finding 0 — net10-readiness ✅ READY

| Check | Result |
|---|---|
| Branch behind net10 master (`git rev-list --count HEAD..origin/master`) | **0** (up to date) |
| Working tree | **clean** |
| HEAD | `8695b7145` |
| `dotnet --list-sdks` shows 10.0.1xx | **Yes** — `10.0.101` present (also 8.0.424, 9.0.205) |
| `dotnet publish -c Release src/server/api/Sprk.Bff.Api/` | **clean, exit 0** (net10 framework-dependent) |
| Graph/Kiota break sites flagged in build | **None** (Graph 6.5.0 / Kiota 2.0 transitive already on master per `dotnet-10-upgrade-r1` task 033) |

**net10-ready = YES.** Safe to build/deploy the BFF from this tree onto the net10 dev runtime (no net8→503 risk). `global.json` pins SDK 10.0.100; the machine resolves 10.0.101 (compatible feature band). If a fresh shell errors "requested SDK 10.0.100 not found", it is stale SDK resolution — open a new terminal (not a code problem).

---

## Finding 1 — /conflict-check (R7 hot-path files) ✅ CLEAR

Hot-path files scanned: `ConversationPane.tsx`, `SprkChatInput.tsx`, `Services/Ai/**`, `Services/Compose/**`, `ComposeWorkspace.tsx`, `ComposeEditor.tsx`, `ComposeFormatToolbar.tsx`.

**Open-PR scan (all 100 open PRs, file-level):** the only open PR touching any `Services/Ai/` path is **PR #526** (`docs/comms-arch-assessment-and-hygiene`) → `Services/Ai/Membership/MembershipFieldDiscoveryService.cs`. That file is **NOT** an R7 target (R7's FR-11 touches `Services/Ai/ComposePdfIntakeSource.cs` only, consuming `PublicContracts/`). **No open PR touches** `ConversationPane.tsx`, `SprkChatInput.tsx`, `Services/Compose/**`, `ComposeWorkspace.tsx`, `ComposeEditor.tsx`, or `ComposeFormatToolbar.tsx`.

**Registry overlap (`projects/INDEX.md`):** the projects that *declare* overlap with R7's spine are all **INITIALIZED / execution owner-gated, NOT started** — no live PRs against the shared files:
- `spaarkeai-assistant-enhancements-r3` (BFF=Y, SpaarkeAi=Y) — declares `ConversationPane.tsx` / `SprkChatInput.tsx` / `Services/Ai/Chat`. Owner-gated, not started. **→ coordinate at PR time for tasks 061 (Ctrl+Shift+Space focus) before the SpaarkeAi PR.**
- `spaarke-ai-architecture-redesign-r2` — sole owner of `Services/Ai/`. R7 FR-11 (073) **consumes `PublicContracts/`, does NOT fork** — no contention by design.
- `code-quality-and-assurance-r3` (BFF=Y) — broad BFF assessment, INITIALIZED. Assessment-first/read-only; coordinate small sequential BFF PRs into quiet windows.
- `spaarkeai-compose-r6` — predecessor, **merged to master** (engines R7 rides on). Worktree row not yet archived.

**Escalation trigger (POML):** "active conflicting PR on ConversationPane/SprkChatInput/Services/Ai" → **DID NOT FIRE.** No such active PR. Gate is not blocked.

**Standing rule:** `/conflict-check` again before EVERY BFF PR and before the 061 SpaarkeAi PR — the owner-gated siblings may start mid-project.

---

## Finding 2 — BFF publish-size baseline (net10) 📏

Measured via repo-standard: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`, then compressed (`Compress-Archive`, Optimal — representative of App Service zip-deploy).

| Convention | Compressed | Uncompressed (dir) |
|---|---|---|
| **incl PDBs** | **44.96 MB** | 137.21 MB |
| **excl PDBs** | **44.06 MB** | 134.99 MB |

**Baseline of record for all R7 NFR-01 delta reporting = 44.96 MB compressed, incl PDBs (net10).** This **supersedes** the ~46.94 MB figure cited in spec/plan (that was the net8 R6 baseline). Matches the `dotnet-10-upgrade-r1` + `code-quality-and-assurance-r3` net10 figure exactly (44.96 incl / 44.05–44.06 excl).

**Ceilings (root §10 / NFR-01):** ≤60 MB HARD; ≥+5 MB single-task delta → explicit justification; ≥55 MB cumulative → architecture review. State the PDB convention when reporting a delta (use **incl PDBs** to compare against 44.96).

---

## Finding 3 — PDF-intake compound DI gate 🔀 (anchors re-verified; live env deferred)

**Code (grep-verified, `Infrastructure/DI/AnalysisServicesModule.cs`):**
- `DocumentIntelligence:Enabled` read @ **L145**
- `Analysis:Enabled` (default `true`) read @ **L165**
- Compound gate `analysisEnabled && documentIntelligenceEnabled` @ **L166**
- Real `IComposePdfIntakeSource → ComposePdfIntakeSource` reg @ **L229–230** (compound-ON path)
- `NullComposePdfIntakeSource` compound-OFF peer @ **L510–511** (`AddNullObjectsForCompoundOff`, §F.1 / ADR-032)

Anchors match the POML's re-verified positions (module shifted post code-quality-r3, gate unchanged). Grep the symbols, not the exact lines, when editing.

**Config template (`appsettings.template.json`):**
- `DocumentIntelligence:Enabled = true` (L131); `.pdf → { Enabled: true, Method: "DocumentIntelligence" }` (L166); `.docx → DocumentIntelligence` (L167)
- `Analysis:Enabled = #{ANALYSIS_ENABLED}#` (L190) — a **deploy-time token** substituted per environment

**⚠️ Live dev App Service value NOT verifiable from this session.** `Analysis:Enabled` is a per-env deployment token, and reading the live App Service configuration (or an authenticated `/healthz`/analysis probe) requires Azure/MCP connector auth that is unavailable in this non-interactive session. **Action for FR-06 (tasks 050/051):** before UAT, confirm BOTH `Analysis:Enabled` AND `DocumentIntelligence:Enabled` are `true` in the target dev App Service settings. If either is OFF, PDF intake degrades to `NullComposePdfIntakeSource` → typed "PDF intake unavailable" and **FR-06 acceptance (PDF→editable parity) will fail in that env** — that would be an environment/config gap, not a code defect.

---

## Finding 4 — Watched PRs 👀

| PR | Title | State | Relevance |
|---|---|---|---|
| **#690** | `ci: pull Git-LFS corpus fixtures in Build & Test (fixes 5 Compose seam tests)` (`work/ci-lfs-fix-r1`) | **OPEN** | **FR-13 / task 075** — claims to fix 5 Compose seam tests via LFS fixture pull. **Do NOT double-fix** those 5 seam tests in 075; re-check #690's merge state at 075 start and scope 075 to the *remaining* jest/flake/fixture items. |
| **#266** | `deps: Bump DocumentFormat.OpenXml 3.4.1 → 3.5.1` (dependabot) | **OPEN** | OpenXml is central to the Compose save/render path. If #266 merges mid-project, re-run BFF build + Compose seam suite on the new base. Not a blocker; note for tasks touching `ComposeService.cs` (013/050) and 075. |

---

## Gate Verdict — ✅ OPEN (proceed to Phase 1)

- net10-ready ✅ · conflict-check CLEAR ✅ (escalation trigger did not fire) · baseline captured (44.96 MB incl PDBs net10) ✅ · DI-gate code-anchors verified ✅ (live env value deferred to FR-06 UAT) · watched PRs noted ✅
- **No source (.cs/.ts/.tsx) modified by this task.**
- **Next:** task 010 (stable non-rotating logical document id — opus; blocks Phase 4 draft key + all dedup vectors).

### Standing coordination reminders (carry into every task)
1. `/conflict-check` before EVERY BFF PR and before the 061 SpaarkeAi PR (owner-gated siblings may start mid-project).
2. Report publish delta vs **44.96 MB incl PDBs** (net10); flag ≥+5 MB.
3. FR-11 (073): consume `Services/Ai/PublicContracts/` — **NO fork** of `Services/Ai/`.
4. 075: watch PR #690 — don't double-fix the 5 LFS-fixed seam tests.
5. Deploy BFF + `sprk_spaarkeai` **together** at wrap-up (NFR-05). **NEVER delete `docxBridge.ts`** (NFR-06).
6. FR-06 (050/051): confirm the compound DI gate is ON in the target env before UAT.
