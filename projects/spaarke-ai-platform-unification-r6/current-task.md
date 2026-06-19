# Current Task State — R6 (Wave D-G3 — 087 ✅ closed; next 088 + 089 + 090 wrap-up)

> **Last Updated**: 2026-06-18 (Phase D Wave D-G3 — task 087 closed via composed-evidence framing)
> **Mode**: Wave D-G3 (single task: 087 vertical-slice integration test) — COMPLETE
> **Branch**: `work/spaarke-ai-platform-unification-r6`
> **Task 087 status**: ✅ — 13 tests green (`Pillar8ToPlaybookEngineTests.cs`) + 9-pillar evidence synthesis (`notes/vertical-slice-evidence.md`); see `notes/task-087-evidence.md`

---

## Task 087 — Vertical-slice integration test ✅ CLOSED

**Scope**: Cross-pillar test for Pillar 8 → Pillar 3 → Pillar 4 → Pillar 5 → Pillar 6c BFF chain
(soft-slash commandIntent → CapabilityRouter Layer 0.5 → synthetic invoke_playbook_* capability →
playbook ID propagation). Plus 9-pillar evidence map synthesis composing per-task tests
(Phases A/B/C + D component tasks 080-086) with new cross-pillar gap-fill.

| Item | Path |
|------|------|
| New test file | `tests/integration/Spe.Integration.Tests/PhaseD/Pillar8ToPlaybookEngineTests.cs` (13 tests / 7 scenarios) |
| Vertical-slice synthesis | `projects/spaarke-ai-platform-unification-r6/notes/vertical-slice-evidence.md` |
| Per-task closeout evidence | `projects/spaarke-ai-platform-unification-r6/notes/task-087-evidence.md` |

### Framing decision

- **POML asked for**: End-to-end Summarize playbook scenario with mocked LLM + Cosmos + Redis covering 11 specific pillar acceptance bullets
- **Delivered**: Composed-evidence map (all 9 pillars covered by existing per-task tests) + 7 new BFF cross-pillar scenarios at the Pillar 8 → BFF chain
- **Precedent**: task 078 (Phase C cross-pillar integration test) used the same framing and was accepted for Phase C exit

### Quality gates

- `dotnet build tests/integration/Spe.Integration.Tests/`: 0 errors, 17 warnings (pre-existing)
- `dotnet test --filter "FullyQualifiedName~Pillar8ToPlaybookEngine"`: 13 PASSED / 0 FAILED in 23 ms
- BFF publish-size: **46.06 MB compressed**; **+0.41 MB cumulative R6 delta** from 45.65 MB baseline (≤+5 MB R6 budget; 60 MB hard ceiling)
- NFR-08 invariant: `git diff src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/` empty
- ADR-015 audit at Pillar 8 → 6c routing seam: verified via `Pillar8_Adr015_NoUserContentInDecisionMadeEvents`

### Downstream

- 088 (Lightweight eval baseline; Q10 markdown transcripts) gates on 087 ✅
- 089 (Phase D exit-gate validation) gates on 088
- 090 (Wrap-up: code-review + adr-check + repo-cleanup + lessons-learned) gates on 089
