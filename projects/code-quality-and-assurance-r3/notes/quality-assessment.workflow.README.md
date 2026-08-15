# quality-assessment Workflow — Usage & Fallback

> **Artifact**: [`quality-assessment.workflow.js`](quality-assessment.workflow.js) (r3 task 003; spec **FR-02**)
> **Consumed by**: surface-assessment tasks **010–015** and **017** (and any future surface re-score, e.g. task 090 wrap-up re-score)
> **Method provenance**: the BFF assessment pass of 2026-08-05/06 (6 parallel read-only investigations + 3-agent Fable verification — [`workstreams/bff-api/design.md`](../workstreams/bff-api/design.md)), generalized to one finder per rubric dimension D1–D11 ([`docs/standards/CODE-QUALITY-RUBRIC.md`](../../../docs/standards/CODE-QUALITY-RUBRIC.md)).

---

## 1. Operator opt-in is REQUIRED (per run)

The Workflow tool only runs on an **explicit operator opt-in, per run** — the operator says **"use a workflow"** (and names this script) in the assessment turn. Claude Code MUST NOT auto-launch this workflow autonomously; the program CLAUDE.md encodes this ("Do NOT auto-launch a Workflow; the operator invokes each assessment turn explicitly"), and each assessment POML's Step 0 confirms the opt-in before proceeding.

A surface-assessment turn therefore looks like:

> **Operator**: "Work on task 010. **Use a workflow** — run `projects/code-quality-and-assurance-r3/notes/quality-assessment.workflow.js` with `surface=shared-client-libs; rootPaths=src/client/shared; surfaceTitle=Shared client libs (Spaarke.*)`."

If the operator has **not** opted in, the task must stop at Step 0 and ask — it must not silently substitute the manual fallback (see §4).

## 2. What the workflow does (three stages, one hard gate)

| Stage | Agents | Model | Read/write | Output |
|---|---|---|---|---|
| **1. fan-out** | 11 parallel finders, one per rubric dimension D1–D11 | session default | **READ-ONLY** (spawned as `Explore` — no write tools) | Per-dimension A–F grade + structured findings with `file:line` evidence |
| **2. fable-verify** | Batched adversarial verifiers over ALL deduped findings | **`fable` — mandatory, non-negotiable (NFR-05)** | READ-ONLY (`Explore`) | `CONFIRMED`/`REFUTED` verdict + reason per finding; corrected file:line; `requiresDataverseCheck` flags |
| **3. synthesize** | 1 synthesis agent | `fable` | Writes **exactly one file**: the surface `design.md` | Prioritized remediation design (severity/LOC/effort/risk, A/B tranche split) + SCORECARD row inputs (logged, not written) |

**The hard gate (NFR-05).** Synthesis is *structurally* unable to run on unverified findings:

- The only collection passed to the synthesis agent is `verifiedFindings`, built exclusively by joining Fable verdicts (`verdict === 'CONFIRMED'`) against the deduped finding set. There is no code path from raw finder output into synthesis.
- If **any** finding is missing a verdict (or gets a duplicate verdict), the workflow **throws** before the synthesize phase starts.
- If Fable refutes **>30%** of first-pass findings, the workflow **throws** (the escalation trigger from tasks 010–015/017) — a first pass that unreliable is reported to the operator, never synthesized.
- Refuted claims are carried into the design only as a record-only "do NOT act on" appendix (so future passes don't re-claim them), mirroring the BFF design's Explicit-KEEPs section.

**Verification checks encoded** (the ones that caught 2 real BFF bugs and saved 2 load-bearing "dead code" claims):

- Re-open every cited `file:line`; confirm-with-correction on drift.
- Dead-code claims re-checked against `src/` **and** `tests/` (**`InternalsVisibleTo`** can make a test project the only live consumer), DI registration, endpoint mapping, and deliberate seams (ADR-032 null-object/kill-switch layers, stub swap paths — wired stubs are NOT dead).
- **Data-driven dispatch is not grep-provable (NFR-08)**: anything dispatched via Dataverse `sprk_*` rows (e.g. `sprk_analysistool.sprk_handlerclass`) or class-name discovery is confirmed only as *static-analysis-confirmed* with `requiresDataverseCheck=true`; the design lists the exact live Dataverse pre-check remediation must run before any rename/delete.
- Broken-path claims require a hand-traced, cited reachability chain; auth claims require checking for covering filters/middleware/fallback policy.

**Read-only guarantee (NFR-03).** Finders and verifiers have no write tools at all. The single write in the entire run is the synthesis agent writing the surface `design.md` under `projects/.../workstreams/` — never `src/`, `tests/`, `docs/`, or `.claude/`. The workflow does **not** write `notes/SCORECARD.md`; it logs the ready-to-paste row and evidence bullets, and the invoking task appends the row (per each POML's own step).

## 3. Invocation — args per surface task

The script takes `args` as an object or a `key=value; key=value` string:

| Arg | Required | Meaning |
|---|---|---|
| `surface` | ✅ | Slug; names the workstream folder + default design path |
| `rootPaths` | ✅ | Comma-separated repo-relative roots to assess |
| `surfaceTitle` | — | Human title for the design + SCORECARD row (defaults to `surface`) |
| `designPath` | — | Output path; default `projects/code-quality-and-assurance-r3/workstreams/{surface}/design.md` |
| `extraContext` | — | Surface-specific notes for the finders/verifier: known KEEPs/seams, prior findings, exclusions |
| `excludePaths` | — | Comma-separated paths to skip |

Suggested per-task invocations (adjust `rootPaths` at run time if the tree has moved):

| Task | args |
|---|---|
| **010** shared client libs | `surface=shared-client-libs; rootPaths=src/client/shared; surfaceTitle=Shared client libs (Spaarke.*)` |
| **011** shared server libs | `surface=shared-server-libs; rootPaths=src/server/shared; surfaceTitle=Shared server libs (Spaarke.Core/Dataverse/Scheduling); extraContext=NG1 assess-then-decide track: this assessment must produce the verified NG1 design input (two Dataverse access stacks + #3b ClientSecret→MI in DataverseServiceClientImpl/DataverseWebApiService); consult notes/bff-auth-surface-map.md (task 019) for the credential graph.` |
| **012** PCF controls | `surface=pcf-controls; rootPaths=src/client/pcf; surfaceTitle=PCF controls (36); extraContext=Check ADR-022 ReactControl compliance; AssociationResolver is retired per CLAUDE.md but may still be in-tree; lifecycle/memory correctness (init/updateView/destroy).` |
| **013** Dataverse model + ALM | `surface=dataverse-model-alm; rootPaths=src/dataverse,src/solutions; surfaceTitle=Dataverse data model + solution ALM; extraContext=Focus D10: naming, option-sets, relationships, solution segmentation, field-mapping config. Schema lives in Dataverse — docs/data-model/ is the reference; flag doc-vs-environment drift as D11.` |
| **014** code pages + build sprawl | `surface=code-pages-build; rootPaths=src/solutions; surfaceTitle=Code Page solutions (35) + build/config sprawl; extraContext=69 package.json roots; npm ci broken on ~14/16 Vite solutions (known — grade, don't rediscover); 7 Create*Wizard duplication vs shared wizard lib; retired-but-present solutions (LegalWorkspace retirement doc).` |
| **015** plugins | `surface=plugins; rootPaths=src/dataverse/plugins; surfaceTitle=Dataverse plugins; extraContext=Small surface. Old R3 item #9: BaseProxyPlugin invert-vs-decommission (ADR-002) — the design should recommend a disposition.` |
| **017** config-deployment | `surface=config-deployment; rootPaths=src/server/api/Sprk.Bff.Api,scripts,infrastructure; surfaceTitle=Configuration & deployment architecture (#1 KV federation); designPath=projects/code-quality-and-assurance-r3/workstreams/config-deployment/design.md; extraContext=FR-24 cross-surface config assessment: 5 config sources / 94 deploy-time tokens / client-config endpoint / cache ceremony; include the FR-29 naming-drift census (env tokens in KV secret names, casing drift, orphan secrets — dev-vault spaarke-spekvcert evidence) producing the current→canonical rename map.` |

**Cost/model note**: finders inherit the session default model; the verify + synthesis stages force `model: 'fable'` at `effort: 'xhigh'` (that is the point of the engine — do not downgrade them to save budget; the fan-out size is the knob to trim, not the verification).

## 4. Fallback — manual agent fan-out (OPERATOR decision only)

If the Workflow tool is unavailable, errors mid-run, or the operator declines the opt-in, the **accepted fallback is the manual agent fan-out exactly as used on the BFF pass**:

1. Main session spawns parallel **read-only** finder subagents (Explore/general-purpose) — one per dimension or per documented cluster (the BFF pass used 6 clusters) — each returning structured findings with `file:line` evidence and a dimension grade.
2. Main session spawns **Fable** verification subagents over all findings (the BFF pass used 3) applying the same refutation-first checks (§2). **This stage may never be skipped** — NFR-05 applies identically to the fallback.
3. Main session synthesizes the design.md from **confirmed findings only**, with the same sections/tranche split and SCORECARD row inputs.

**Choosing the fallback is an OPERATOR decision, not an autonomous one.** If the workflow cannot run, the executing agent STOPs and asks (per each assessment POML's Step 0 + the program CLAUDE.md rule); it does not silently switch modes. Either mode must end in the same artifact shape: a Fable-verified `design.md` + a SCORECARD row.

## 5. Outputs & failure modes

| Outcome | Meaning |
|---|---|
| Design written + SCORECARD row logged | Success. Invoking task appends the row to `notes/SCORECARD.md` (no aggregate until all surfaces scored — FR-04) and flips its TASK-INDEX status. |
| Throw: "no Fable verdict" / "duplicate verdict" | Verification integrity failure — re-run; nothing was synthesized. |
| Throw: "ESCALATION (NFR-05): refuted >30%" | First pass unreliable — report to operator with the refuted list from the transcript; tune finder scope/`extraContext`; re-run. Do NOT hand-synthesize from the raw findings. |
| Throw: missing args | Pass `surface` + `rootPaths` (§3). |
| Grade-check log warns letter/points mismatch | Resolve manually against rubric §4 before appending the SCORECARD row. |

## 6. Change control

The finder dimension definitions embedded in the script are derived from `docs/standards/CODE-QUALITY-RUBRIC.md` §2–§3. The rubric is the ruler: if the rubric changes, update the script's `DIMENSIONS` table in the same PR (the script header carries the same warning). Grades live only in `notes/SCORECARD.md` — neither the script nor this note publishes grades.
