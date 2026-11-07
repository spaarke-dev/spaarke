# provision-environment — MOVE-MAP (2026-11-03)

Original: `.claude/skills/provision-environment/SKILL.md` — 3,057 lines / 229 KB, about half of it inline PowerShell.
It is archived verbatim at `.claude/archive/2026-11-03/skills/provision-environment/SKILL.md`.

Status: COMPLETE. Verification was static only; nothing ran against Azure, Dataverse, Key Vault or L2.

## Line counts

| File | Lines |
|---|---|
| Original SKILL.md | 3,057 |
| New `SKILL.md` | 437 (under the 500 cap, so no `length-justification`) |
| `references/intake-fields.md` | 324 |
| `references/fallback-matrix.md` | 242 |
| `references/fresh-sub-feasibility.md` | 196 |
| `references/troubleshooting.md` | 172 |
| `references/manual-gates.md` | 126 |
| `references/copilot-agent-gate.md` | 44 |
| Scripts (17 new + `validate.ps1` changed) | 2,279 (see below) |

Every reference file over 100 lines has `## Contents` within its first 40 lines (`check-skill-length.py` rule 2).
Every relative link in SKILL.md and the references resolves against the worktree layout. None is broken.

## Script destination choice

- Prerequisite-manifest scripts sit beside the manifest they read, in `scripts/provisioning-prereqs/`
  (`Invoke-PrereqPass.ps1`, `Test-SpeTopology.ps1`). `validate.ps1` already lives there and mirrors Step 0.5b.
- Run-driver scripts (intake, L2 calls, registry, reports, postmortem) sit in a new sibling folder,
  `scripts/provision-environment/`, named after the skill, with one shared module `ProvisionEnvironment.psm1`.
  They are not prerequisites, so putting them in `provisioning-prereqs/` would mislabel them.
- Reuse check (repo CLAUDE.md §11): `scripts/Provision-Customer.ps1` is the older service-principal pipeline. It creates
  the RG, deploys Bicep and imports solutions itself. It does not talk to L2, and it authenticates as a service
  principal, which this skill forbids (NFR-11). So nothing in it can be called. `validate.ps1` and
  `validate-recipe-authoring.ps1` are author-time validators, not runtime scripts. They stay as they are, except that
  two message strings in `validate.ps1` now point at the new token-map location.

## Section map — original → destination

| Original lines | Original heading | Destination |
|---|---|---|
| 1–10 | frontmatter | SKILL.md frontmatter — description/tags/appliesTo unchanged; `name:` added; `last-reviewed: 2026-11-03` |
| 11–32 | title, intro, trigger phrases | SKILL.md intro (narrative history dropped) |
| 33–50 | Quick Reference | SKILL.md Quick Reference |
| 51–76 | Critical Rules (MUST / NEVER) | SKILL.md Critical Rules — all rules kept; added "run the scripts" and "never `Read-Host`" (D1) |
| — | (new) | SKILL.md "How the scripts keep state" + exit-code table |
| 77–82 | Procedure / Step 0 intro | SKILL.md Step 0 |
| 83–157, 217–273 | 0a, 0b, 0c, 0e, 0f, 0g | `scripts/provision-environment/Test-OperatorPrereqs.ps1`; SKILL.md keeps headings + call + gate |
| 158–216 | 0d Dataverse MCP status | `Set-McpStatus.ps1` (BAT-04); SKILL.md 0d keeps the MCP probe call (an agent tool call that cannot run in a script) |
| 274–459, 541–561 | 0.5, 0.5a, 0.5b, 0.5d | `scripts/provisioning-prereqs/Invoke-PrereqPass.ps1 -Pass Env` (token map moved verbatim); SKILL.md keeps the rules |
| 460–540 | 0.5c SPE topology | `scripts/provisioning-prereqs/Test-SpeTopology.ps1` |
| 562–565 | Step 1 intro | SKILL.md Step 1 + `references/intake-fields.md` "How to ask" |
| 566–689 | 1.0 Batch mode | `New-ProvisionSession.ps1 -Mode Batch`; semantics + sample intake → intake-fields.md §1.0 |
| 690–775 | 1a customerId + upgrade probe | `Test-ProvisionIntake.ps1 -Step 1a`, `Invoke-RegistryStep.ps1 -Action Probe`; prose → intake-fields.md |
| 776–974 | 1a-bis … 1e | `Test-ProvisionIntake.ps1`; field meaning → intake-fields.md; hard rules inline in SKILL.md |
| 975–1183 | 1e-bis users / Exchange / mailbox / B2B checks | `Test-ProvisionIntake.ps1 -Step 1e-bis` (live PRQ-C-10a/b, -11, -12 checks); prose → intake-fields.md |
| 1184–1213 | 1e-ter customer prereq pass | `Invoke-PrereqPass.ps1 -Pass Customer` |
| 1214–1312 | 1f placeholder registry record | `Invoke-RegistryStep.ps1 -Action Plan / CreateWebApi / Confirm`; field lists → intake-fields.md §1f |
| 1313–1350 | 1g intake summary | SKILL.md 1g; sample → intake-fields.md §1g |
| 1351–1404 | Step 2 | SKILL.md Step 2 (bullets kept); cost check → `Test-CostEnvelope.ps1`; sample preflight display → troubleshooting.md (D15); EXEC-02 blockquote condensed to one sentence |
| 1405–1525 | Step 2.5 | `references/fresh-sub-feasibility.md` (manual queries kept as code, since they were never automated) |
| 1526–1603 | Step 3 confirmation gate + RUN PLAN | SKILL.md Step 3 (RUN PLAN inline); check → `Confirm-ProvisionGate.ps1` |
| 1604–1697 | 4.0 Enqueue | `Start-ProvisioningRun.ps1`; SKILL.md 4.0 keeps the closed-set + 403/409 rules |
| 1698–1844 | 4a poll loop, 4b terminal states (incl. batch BAT-06/07) | `Watch-ProvisioningRun.ps1`, `Invoke-RunAction.ps1`; 4b table inline in SKILL.md |
| 1845–1930 | Step 5 intro, DAG note, batch BAT-08 dispatch | dispatch → `Watch-ProvisioningRun.ps1`; DAG note → SKILL.md + manual-gates.md |
| 1931–2025 | 5a–5d gate messages, advance rule | `references/manual-gates.md`; advance rule inline in SKILL.md |
| 2026–2221 | 6a registry update (MED#10, HIGH#7, HIGH#10) | `Sync-RegistryOnCompletion.ps1`; rules inline in SKILL.md 6a; four-state table → fallback-matrix.md "Step 6a background" |
| 2222–2317 | 6b handoff report | `Write-HandoffReport.ps1 -Action Write/Check` + `scripts/provision-environment/templates/handoff-report.md` |
| 2318–2401 | 6c registry-stale diagnostic | `Write-HandoffReport.ps1` (stale report body). Option A rewritten (D2). Option C ("-ResumeRegistryPatch, not yet implemented") now exists: Option A reruns `-Action ApplyWebApi` |
| 2402–2451 | 6d final summary | SKILL.md 6d (condensed; same fields) |
| 2452–2467 | 6e secure-record verification | SKILL.md 6e (unchanged) |
| 2468–2508 | 6f Copilot agent gate | `references/copilot-agent-gate.md`; trigger + batch rule inline in SKILL.md |
| 2509–2653 | Step 7 (7a–7d) | `Write-Postmortem.ps1 -Action Prepare/Finalize`; section walk-through inline in SKILL.md 7b |
| 2654–2841 | Fallback Matrix F1/F2/F3 + cross-references | `references/fallback-matrix.md` (annotated D2/D11/D13); automatic parts in the module + Watch |
| 2842–2890 | Tool Matrix, Auth Flow | `references/troubleshooting.md` |
| 2891–2911 | Dry-Run Mode | **Dropped as stale** (see below) |
| 2912–2990 | Troubleshooting, Related Skills/Docs, Tips for AI, Failure Modes | `references/troubleshooting.md` (annotated D2/D11); the every-run rules are also in SKILL.md Critical Rules / Gotchas |
| 2992–3057 | Fresh-Sub Automation Gaps, roadmap, web app | `references/fresh-sub-feasibility.md` |

## Dropped (clearly obsolete), with reason

- **"Dry-Run Mode" section (2891–2911)** describes `mode:"preflight-dry-run"` and a plan-preview response. Step 2 of
  the same file calls that "a skill fiction": `CreateRunRequest` has no `mode` field (`RunsEndpoints.cs`). The real
  dry-run options (`secureRecordSetupDryRun` and the `dryRun` nonSecret parameter) are kept in Step 1b-sexies and 4.0.
- **"Companion Task 076 placeholder" header** above the Fallback Matrix. The matrix it promised is present.
- **Per-audit narrative blockquotes and code comments** (the SESSION-n, EXEC-02, ISH-10, Bucket B and COMP-04 history;
  "earlier drafts used…"; "prior behaviour threw…"). The rule each one produced is kept; the story stays in the archive.
- **The trailing footnote** "Fresh-Sub Automation Gaps section added 2026-08-22…".

## Scripts — static verification

Parse check: every file gives 0 errors with `[System.Management.Automation.Language.Parser]::ParseFile`.

`scripts/quality/Invoke-PSAnalysis.ps1`: 0 errors overall.
- `scripts/provision-environment/` has 5 warnings:
  - 2 × `PSPossibleIncorrectUsageOfRedirectionOperator`, a false positive on `(git … 2>$null) -eq 'true'`
    (New-ProvisionSession L76, Set-ProvisionSession L32).
  - 3 × `PSUseShouldProcessForStateChangingFunctions` on the internal helpers `Stop-Provision`,
    `Update-ProvisionSession` and `Set-Stale`.
- `scripts/provisioning-prereqs/` has 0 warnings.

| Script | Lines | Steps |
|---|---|---|
| `scripts/provision-environment/ProvisionEnvironment.psm1` | 226 | shared: session I/O, intake normalisation, L2 calls with F2 refresh, registry Web API (F1 B), constants |
| `…/New-ProvisionSession.ps1` | 113 | session create; batch Step 1.0; `-ResumeRunId` (re-attach, D16) |
| `…/Set-ProvisionSession.ps1` | 43 | interactive: register intake file / env / skip flag |
| `…/Test-OperatorPrereqs.ps1` | 129 | 0a 0b 0c 0e 0f 0g |
| `…/Set-McpStatus.ps1` | 59 | 0d (BAT-04) |
| `…/Test-ProvisionIntake.ps1` | 250 | 1a 1a-bis 1b 1b-bis 1b-ter 1b-quater 1b-quinquies 1b-sexies 1e 1e-bis |
| `…/Invoke-RegistryStep.ps1` | 132 | 1a probe (BAT-05), 1f plan / Web API create / confirm (PRQ-05) |
| `…/Test-CostEnvelope.ps1` | 26 | 2 (BAT-10) |
| `…/Confirm-ProvisionGate.ps1` | 42 | 3 |
| `…/Start-ProvisioningRun.ps1` | 103 | 4.0 |
| `…/Watch-ProvisioningRun.ps1` | 165 | 4a 4b, batch 5 (BAT-06/07/08), F3 |
| `…/Invoke-RunAction.ps1` | 42 | resume (4b) / gate advance (5) |
| `…/Sync-RegistryOnCompletion.ps1` | 106 | 6a |
| `…/Write-HandoffReport.ps1` | 132 | 6b 6c 6d (+ `templates/handoff-report.md`) |
| `…/Write-Postmortem.ps1` | 130 | 7a 7b 7c 7d (BAT-09) |
| `scripts/provisioning-prereqs/Invoke-PrereqPass.ps1` | 201 | 0.5a 0.5b 0.5d, 1e-ter |
| `scripts/provisioning-prereqs/Test-SpeTopology.ps1` | 77 | 0.5c |
| `scripts/provisioning-prereqs/validate.ps1` (**changed**) | 303 | comment (L6) + message (L198) now point at `Invoke-PrereqPass.ps1 (skill Step 0.5b)` |

Other worktree change: `.gitignore` adds `runs/provision-session-*.json`. Session files hold no personal data, but they
are per-invocation state.

## State-passing design (the PowerShell tool keeps no variables between calls)

- **Session file** `runs/provision-session-{yyyyMMdd-HHmmss}.json`.
  - `New-ProvisionSession.ps1` creates it and prints `SESSION=`.
  - Every later script takes `-SessionFile` and reads or merges it (`Read-ProvisionSession`, `Update-ProvisionSession`).
  - It replaces every `$script:` and cross-block variable of the original:
    - mode, env and operatorUpn;
    - environmentId and isUpgradeRun;
    - the confirmation phrase and the intake SHA-256;
    - runId, alreadyResumed and the gate-wait start;
    - the registry-stale flag, diagnostic and fields;
    - the prereq results and the MCP-fallback flag.
  - It holds no personal data.
- **Intake file.** In batch mode it is the operator's file, never modified. In interactive mode the agent writes the
  operator's answers to `runs/{customerId}-intake.json` (schema shape, git-ignored) and registers it with
  `Set-ProvisionSession.ps1`. `Get-ProvisionIntake` normalises both the same way.
- **Exit codes and stdout keys** carry results back to the agent.
  - Keys: `SESSION=`, `REGISTRY=`, `ENVIRONMENT_ID=`, `RUN_ID=`, `GATE_ID=`, `PHASE=`, `HANDOFF=`, `LESSONS=`, `FIELD=`.
  - Exits: 0, 1 (hard stop), 2, 3, 4, 5, 6, 7 (ask the operator), 9 and 10. The table is in SKILL.md.
- **Agent tool calls stay in SKILL.md.** Dataverse MCP reads and writes (`read_query`, `create_record`,
  `update_record`) cannot run inside a script. The scripts print the payload and take the MCP result back as
  `-ProbeJson`. When the MCP is down (F1) they use the Web API themselves.
- **No stdin.** The harness PowerShell tool is non-interactive, so no script prompts. The agent asks in chat and
  passes the answers as parameters (`-OperatorAnswer`, `-Phrase`) or writes them to the intake file (exit 7 /
  `FIELD=`).
- **Long waits.** `Watch-ProvisioningRun.ps1` polls for at most `-MaxMinutes 9`, under the 10-minute tool cap, then
  exits 10 and the agent calls it again. The batch `pollUntilTimeout` start time is kept in the session.

## Defects found

Fixed. These are certain and fixed in the scripts; the original text is kept in the archive.

- **D1** — The original prompted with `Read-Host` loops. The harness PowerShell tool runs `-NonInteractive` with null
  stdin, so every prompt would fail. Scripts never prompt; the agent asks in chat (exit 7 / `FIELD=`).
- **D2** — `pac data query`, `create` and `update` do not exist: pac 1.46.1 `pac data` lists only `export` and
  `import`.
  - Affected: the 1a and 1f fallbacks, F1 Fallback A, 6c Option A, the Tool Matrix, Troubleshooting and Failure Modes.
  - Fix: the Web API path (F1 Fallback B) is implemented and used instead. The references are annotated.
- **D3** — Step 6a wrote `sprk_setupstatus = 'Ready'`, a string, to an option set.
  - The server writes `2` (`EnvironmentSetupStatus.Ready`); `DataverseEnvironmentRegistryClientTests` asserts
    `GetInt32() == 2`.
  - Fix: scripts write `2`, and an observed value of `2` or `Ready` counts as Ready.
- **D4** — Interactive env timing was broken in four ways:
  - Step 0c hard-coded `$env = "dev"`, while 0.5a expected it to be unset.
  - `once_per_env` prereqs never ran interactively: the prose said "re-checked at Step 2", but no code did it.
  - 0.5c would stop on a null container type.
  - Fix: interactive mode asks for the env at Step 0 (Step 1d confirms it), and the 1e-ter pass runs `once_per_env`
    when Step 0.5 did not.
- **D5** — The batch code read run fields that do not exist on `ProvisioningRun`: `gateId`, `gateReason`,
  `gateInstructions`, `rejectionCode`, `rejectionMessage`, `quarantineReason`, `currentHandler` and `StartedOn`.
  - Result: diagnostics were null, and the H8 25-hour cap could never apply.
  - Fix: scripts use `gateStates` (Pending), `currentPhase`, `errorDetail`, `quarantine.reason` and `createdOn`.
- **D6** — Interactive `$operatorUpn` was never assigned (only batch Step 1.0 set it), yet 4.0 and 7a used it. Both
  modes now set it from `az`.
- **D7** — The 6b template put the BFF version into both `{bffversion}` and the solution version. The solution version
  now has its own token.
- **D8** — 7a copied the template into `provisioning-runs/{customerId}-{runId}/` without creating that folder first.
  The script now creates it.
- **D16** — Resuming an existing run had no implementation.
  - Never implemented: the `--resume <runId>` flag (frontmatter and the batch `WAITING.md`), and "re-invoke and the
    skill resumes at the Step 4 poll" (F3, Tips).
  - Fix: `New-ProvisionSession.ps1 -ResumeRunId` records the run id. `Start-ProvisioningRun.ps1` then refuses a second
    POST, and Step 4a polls the existing run. No L2 call is added.

Reported, not changed (needs an owner decision, or the fix is not certain):

- **D9** — Exit-code collision: `Cancelled` = 5 (4b) and registry-stale = 5 (6c). Kept as is; the SKILL.md table tells
  them apart by the script that returned the code.
- **D10** — 7c appends a 4-column row to `provisioning-runs/INDEX.md`, whose table has 10 columns. Kept as is.
- **D11** — Contradictions in the original, annotated in the references:
  - F1 recipes write `sprk_currentrunid = null`, against the 6a single-writer rule. The scripts never write it.
  - 6c said "Do NOT touch `sprk_setupstatus`", against MED#10. The stale report now says not to re-write it when it is
    already Ready.
  - Tips and Failure Modes say to use `/resume` for gates, against Step 5 and `RunsEndpoints.cs`, which use advance.
  - Troubleshooting cites `GET /api/runs?customerId=`, which is not a route.
- **D12** — Step 2.5 F3 references `.claude/skills/provision-environment/refs/test-s1-plan.bicep`, which does not
  exist, and uses the undefined `$tempRg`, `$candidateRegion` and `{stack}`. Kept as manual queries, annotated.
- **D13** — F2 and Step 5 send "abandon" to `POST /api/runs/{id}/quarantine`. `RunsEndpoints.cs` has no such route:
  its routes are create, preflight, get, gate advance, resume, cancel and clear-quarantine. The references now say to
  use `cancel`.
- **D14** — Step 2 says to re-validate the intake against `intake.schema.json` in interactive mode. The schema requires
  `confirmationAcknowledgment`, which an interactive intake lacks until Step 3, so the check cannot pass. It is not
  run interactively; noted in SKILL.md.
- **D15** — The Step 2 sample "PREFLIGHT (H0) RESULT" shows server-side checks the client cannot produce. Moved to
  troubleshooting.md and labelled illustrative.

Deliberate small behaviour additions:
- Step 0 collects every check before exiting, as its prose said.
- 1b validates `tenantId` as a GUID; the prose rule is now in code.
- Batch `autoResumeOnce` skips a `secure_setup.dry_run` end, per the 4b table rule.
- The postmortem records the prerequisite results.
- `Start-ProvisioningRun.ps1` refuses a second POST, and a POST without the phrase or the environmentId.

Observations for the owner:

- `provisioning-runs/_templates/handoff-report.md` (the run-folder template) overlaps with the new
  `scripts/provision-environment/templates/handoff-report.md`. The skill never wrote the run-folder file. Whether to
  consolidate them is an owner question.
- `validate.ps1` lines 13/43/65/81/261/298 and the context-defaults comments still say "SKILL Step 0.5a/0.5b". That is
  still valid, because the step ids are unchanged.
- The KV credential-lifecycle summary in troubleshooting.md is dated 2026-08-27 and time-boxed
  (`Dataverse-ClientSecret` until 2026-11-23). It is now labelled "apply the current text of
  `.claude/constraints/provisioning.md`".

## Step ids cited elsewhere — all preserved

Other files cite these ids in about 790 citation lines, mostly under `projects/customer-provisioning-orchestration-r1/**`:
- 0, 0a, 0b, 0c, 0d, 0f
- 0.5, 0.5a, 0.5b, 0.5c
- 1, 1.0, 1a, 1a-bis, 1b, 1b-bis, 1b-ter, 1b-quinquies, 1c, 1d, 1e, 1e-bis, 1e-ter, 1f, 1g
- 2, 2.5, 3
- 4, 4.0, 4a, 4b
- 5, 5a, 5b, 5c, 5d
- 6a, 6b, 6c, 6d, 6e, 6f
- 7a, 7b

Every one of them, plus 0e, 0g, 0.5d, 1b-quater, 1b-sexies, 6, 7, 7c and 7d, is a heading in the new SKILL.md. 1a-bis
through 1e share one combined heading line. This was checked mechanically. No test parses SKILL.md.

## For the main session to apply

1. Copy `SKILL.md` and `references/` from this scratchpad folder to `.claude/skills/provision-environment/`,
   replacing SKILL.md. The original is already archived.
2. Optionally copy this MOVE-MAP to `.claude/archive/2026-11-03/skills/provision-environment/MOVE-MAP.md`.
3. The scripts, `.gitignore` and `validate.ps1` changes are already in the worktree, uncommitted.
