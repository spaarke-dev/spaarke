# How to use the BFF `design.md` inside Code Quality R3

> **✅ RESOLVED / INTEGRATED 2026-08-06** — the recommendations in this note are now DONE:
> - The BFF design was **relocated** to `projects/code-quality-and-assurance-r3/workstreams/bff-api/design.md` (§2); all references updated; the `workstreams/{surface}/` convention now applies to every surface (assessment tasks 010–015 write there).
> - The BFF §5 phasing was **decomposed into r3 tasks 020–029** in the one `TASK-INDEX.md` (§3/§4), with the **A/B tranche split** encoded (Tranche A: 020,021,022,023,027 · Tranche B: 024,025,026,028,029).
> - The §6 owner gate is **RESOLVED to `@spaarke/auth`** (not HMAC) — encoded in task 023; no separate A/B/C decision task.
> - §7 hot-path superseded by r3's single declaration; §8/§9 justifications carried into the relevant task POMLs; the Dataverse pre-check is task 026's escalation.
> The body below is retained as the rationale/provenance for those changes.

---


> **Audience**: the r3 session (single-project, single-worktree model).
> **Subject**: `projects/code-quality-and-assurance-r3/workstreams/bff-api/design.md` (relocated 2026-08-06 from the standalone `projects/bff-api-cleanup-remediation-r1/` folder) — the BFF surface's verified assessment + remediation design.
> **Status**: written 2026-08-06 (uncommitted notes doc; commit when convenient).

---

## 1. What this document IS (and is NOT)

**IS** — the first **per-surface remediation design** deliverable of the r3 program (design §12 lists "per-surface remediation design.md" as a deliverable). It is the *verified* output of the r3 assessment method applied to the BFF surface (fan-out + adversarial verification — the verify pass caught 2 real prod bugs and corrected 2 false-positive "dead code" claims). It is the **WBS/input source** for the BFF workstream's tasks.

**IS NOT** — a separate project. Do **not** run `/project-setup` or `/project-pipeline` against `projects/bff-api-cleanup-remediation-r1/` — that would spawn a second project/branch/Issue, which the owner explicitly ruled out. There is ONE pipeline (this r3 program), ONE branch, ONE `TASK-INDEX.md`.

## 2. Where it should live

It's currently a sibling folder under `projects/`, which reads like a project. **Recommended (do after the running pipeline finishes):** relocate it under this program so it's unambiguously a workstream input:

```
projects/code-quality-and-assurance-r3/workstreams/bff-api/design.md
```

Fix the one reference in this program's `plan.md` (and the §4 link in this program's `design.md`) in the same commit. Future surfaces get siblings: `workstreams/shared-client-libs/`, `workstreams/pcf/`, etc. (Keeping it where it is also works — the header disclaims project-ness — but relocating removes the ambiguity.)

## 3. How it feeds r3's plan + tasks

The BFF design is the **BFF phase's WBS**. Flow:

1. r3 `plan.md` has a **BFF workstream phase** that references this design for detail (don't inline all ~260 lines into plan.md — reference it).
2. `/task-create projects/code-quality-and-assurance-r3` for that phase reads this design and emits the BFF task `.poml` files into the **one** `tasks/` folder / `TASK-INDEX.md`.
3. Each task executes via `/task-execute` as small PRs off `work/code-quality-and-assurance-r3` (with `/conflict-check` first — 19 other worktrees touch BFF).

If the pipeline that just ran scoped only Phase-0 infrastructure (rubric, scorecard, `quality-assessment` workflow, re-baseline), then BFF tasks are generated **later**, when you reach the BFF phase, via `/task-create` against this design. That's the recommended sequencing — build the rubric first, then generate BFF tasks scored against it.

## 4. Section-by-section → how each part becomes tasks

The design's **§5 "Proposed workstreams → phases"** is the task map. Mapping:

| BFF design section | Becomes | Notes |
|---|---|---|
| **§6 Security decision** (Finance anon write) | **A DECISION task first — OWNER GATE** | Not code. Owner picks A/B/C (recommend B=HMAC). Blocks the auth task. Resolve before spec'ing Phase 3. |
| **§3.1 / §5 Phase 1** dead code | Deletion tasks | Exact file lists + LOC are in §3.1. **TEST-MODIFYING rigor** (touches `tests/`) → code-review + adr-check unconditionally. |
| **§3.2 / §5 Phase 2** the 2 prod bugs | Bug-fix tasks | Bug-1 = broken `IDataverseService as ServiceClient` casts (invoice totals); Bug-2 = dead `EmailToEmlConverter` half + dual registration. FULL rigor. Add a test for the invoice path. |
| **§3.3 / Phase 2** redundancy | Consolidation tasks | The `UnwrapServiceClient` extension (13→1); financial-handler rename **needs a Dataverse `sprk_analysistool` pre-check** first (data-driven dispatch — grep can't prove reachability). |
| **§3.4 / Phase 4** facade violations | Facade tasks | Add `PublicContracts` facade method(s); relocate `IPlaybookLookupService`; swap 3 Workspace consumers. |
| **§3.5 / Phase 3** auth | Auth tasks | Health-probe hardening (clear fix) + the §6 owner decision (gated). |
| **§3.6 / Phase 5** structure | Structure tasks | `Endpoints/`→`Api/` migration (zero route change), `CommunicationModule` decomposition, financial-handler rename. |
| **§3.6 repo hygiene** | Hygiene task | Untrack 2 tarballs, remove 127 MB artifacts, `.gitignore`. Low risk, do early. |
| **§5 Phase 6 wrap-up** | Wrap-up task | `/test-diet` + publish-size report + doc-drift. |

**Tranche order (design §0 / §5):** Tranche A first (Bug-1 in-place cast fix · repo hygiene · Finance-auth decision+fix · health probes — low/no conflict). Tranche B in a quiet window (13-site downcast consolidation · Safety-cluster deletion · facade · `CommunicationModule` decomposition · `Endpoints/` migration).

## 5. What to carry over vs. drop from the BFF design's own governance sections

Because BFF is now a *workstream* (not a project), some of its self-contained governance is **superseded by the r3 program**:

- **§7 Hot-Path Declaration** (in the BFF doc) → **superseded**. r3 carries ONE program-level declaration (r3 design §11: `bff=Y/spaarkeai=Y/ci=Y/skills=Y`). Don't emit a second one.
- **§8 Placement Justification / §9 Component Justification** → **keep + reuse verbatim** in the BFF PRs (still required by root CLAUDE.md §10/§11 for any BFF-touching PR).
- **Per-BFF-PR obligations still apply**: `dotnet publish` size check (≤60 MB compressed; baseline 46.89 MB), `dotnet list package --vulnerable`, test updates, `/conflict-check`. These attach to each BFF *task/PR*, not to a separate project.
- **§10 ADR tensions** → carry into the relevant tasks' `<escalation>`/ADR-tension notes.

## 6. Verification discipline (don't skip)

The BFF findings are already adversarially verified, but when generating/executing tasks:
- **Test-only consumers**: several "dead" items have `tests/` references (Safety cluster) — deletion tasks must update those tests (already noted in §3.1).
- **Data-driven dispatch**: the financial-handler rename and any `IToolHandler` change require a **Dataverse `sprk_analysistool` / `sprk_handlerclass` pre-check** — code grep alone can't prove reachability. Make that a gating sub-step.
- **Keep the confirmed KEEPs**: Todo `NullObject`+`Placeholder` double layer and `StubInsightGraph` are intentional (ADR-032 seams) — do not delete.

## 7. Reconciliation with the archived R3 items

- Old R3 **#10 "two parallel Dataverse implementations"** → this workstream owns the **bug-fix + downcast collapse** only; the full ServiceClient-vs-raw-HTTP **stack unification is a separate architecture project** (BFF design NG1), not in scope here.
- Old R3 **#1 "OfficeService.cs God class"** → re-measure current LOC; fold into this BFF/shared-server workstream if still oversized.

## 8. One-line summary

**Use it as the BFF phase's verified WBS**: reference it from `plan.md`, generate BFF tasks from its §5 phasing + §3 findings, resolve its §6 owner gate before the auth task, reuse its §8/§9 justifications in each PR, and ignore its §7 hot-path (r3's single declaration wins). Don't pipeline it as a project.
