# Task 001 — engine container: escalation record (2026-11-26)

Status: **blocked on escalation** (POML trigger 2: "engine CLI contract on master differs from what this task assumes"). No code written; POML status unchanged.

## Gates

- #3163 merged (4c80b81ed4); `src/accelerators/core/engines/` present on this branch.
- ADR-004 A2 on master (d148580e15, #3191).

## Engine CLI contract found on master

| Script | Invocation | Exit codes | Output |
|---|---|---|---|
| `segment_clauses.py` | `DOCX [--min-key 12]` | 0 ok, 2 bad input | JSON on stdout (sections with `para_key`, includes clause text) |
| `ooxml_redline.py` | `PLAN DOCX REDLINE [--external EXT] [--clean CLEAN] [--dry-run]` | 0 ok, 2 bad input/plan, 3 plan does not fit | stdout `{"edits","ins","del","comments","bookmarks","dry_run"}`; stderr `{"error": "edit <i>: <reason>"}` |
| `summary_table.py` | `PLAN REDLINE [--out INTERNAL] [--intro summary-intro.md]` | 0, 2, 3 | stdout `{"rows","out"}` |
| `check_outputs.py` | `PLAN INTERNAL CLEAN --original DOCX [--external EXT]` | 0 pass, 1 a check failed, 2 bad input | stdout `{"checks":[{"name","ok","detail"}]}`; checks: plan, package, untracked_edits, existing_revisions, revision_text, comments, summary, clean, external, residue |

The sequence is the one in `practices/nda/evals/run_local_e2e.py` (segment → dry-run → redline → summary → check).

## The divergence

The task assumes (FR-01g, acceptance criterion 4; spec FR-09(b)): *"An edit whose anchor is not unique or crosses an existing revision is listed in the check report and not applied."*

On master, `ooxml_redline.run()` is **all-or-nothing**: the first edit that does not fit raises `PlanError`, the process exits 3, and **no output file is written**. There is no skip mode, `check_outputs` has no refused-edit check, and `summary_table` has no "manual" row type. The accelerator's `nda-review` SKILL.md handles exit 3 with the model in the loop ("Exit 3 names the edit that does not fit; fix the plan and repeat"; its error table says to rewrite the edit as a comment-only entry on adjacent unrevised words).

Probe (synthetic nda-01 + golden plan + one non-unique `find`): exit 3, stderr `{"error": "edit 19: find 'the' occurs 5 times in the visible paragraph text"}`, zero `.docx` written.

Consequences for the wrapper design (needs a decision, not an engine edit):
1. Meeting FR-01g in the wrapper means a dry-run → drop-edit → retry loop driven by parsing `edit <i>:` out of stderr, plus a wrapper-owned "refused edits" list next to the check report. Dropping an edit that carries a `summary` row also changes the rows `summary_table`/`check_outputs` expect, and FR-09's "comment plus manual row" needs a new anchor (judgment) and a row type the engines do not have.
2. Alternatives: the accelerator adds a refuse-and-report mode to the engines (their change, their worktree), or the plan writer (task 004/015, BFF side) owns the repair and the job treats exit 3 as a non-retryable `Failed` with a code.
3. ADR-015: `PlanError` text quotes up to 50 characters of plan/clause text (`needle[:50]!r`). Whatever path is chosen, the wrapper must never log or forward engine stderr verbatim; it may report only the edit index and an error class.

## Other findings (no action taken)

- The engines' Clean output accepts **every** revision, the counterparty's included, and `check_outputs`' `clean` check requires that. Project CLAUDE.md says not to ship an accept-all Clean copy for drafts with prior markup (spec Unresolved Q1). Not a task-001 blocker, but task 015 must decide whether Clean is persisted for nda-02-style drafts.
- Task 003's workflow (`build-intake-engine-image.yml`) builds, Trivy-scans and size-checks; it does **not** run any test inside the image. "Leave in-container parity to CI" is not covered by it today.
- Task 002 Bicep comments say `DefaultAzureCredential + AZURE_CLIENT_ID`; the POML says `ManagedIdentityCredential(client_id=...)`. Compatible (the wrapper reads `AZURE_CLIENT_ID`); note only.
- Env vars defined by task 002 for the wrapper: `AZURE_CLIENT_ID`, `INTAKE_SERVICEBUS_FQDN`, `INTAKE_REQUEST_QUEUE`, `INTAKE_COMPLETION_QUEUE`, `INTAKE_STAGING_ACCOUNT_URL`, `INTAKE_STAGING_CONTAINER`, `INTAKE_MAX_DELIVERY_COUNT` (5), `INTAKE_JOB_TIMEOUT_SECONDS` (600), `APPLICATIONINSIGHTS_CONNECTION_STRING`.

## Escalation 2 (2026-11-26, after the owner's "A with B as backup" decision): `.docx` outputs are not byte-reproducible

POML trigger 3 ("container output not byte-identical to the local run: STOP and record the diff; do not normalise") fires before any container is built: **two local runs of the same engines on the same machine already differ.**

- Cause (engine code): `ooxml_redline.py:593-596` and `summary_table.py:276-278` write each part with `zipfile.ZipFile.writestr(<str name>, data)`. With a string name, CPython stamps the entry with `time.localtime(time.time())`, so every zip local header and central-directory entry carries the wall-clock local time of the run.
- Evidence: `run_local_e2e.py` run at 07:14 and again at 07:31 (Python 3.13, lxml 6.1.3, synthetic nda-01/02/03 + golden plan). All 9 `.docx` outputs differ by whole-file sha256. Per member, the content bytes and CRC-32 are identical; only `date_time` differs, e.g. `(2026,11,26,7,14,16)` vs `(2026,11,26,7,31,16)`. The 3 plan JSON files are identical.
- `check_outputs` report (nda-02, both runs): byte-identical (sha256 `CBB4E955…53B4BF`), no file paths in it.
- In a container the stamp is UTC while a developer machine uses local time, and the deflate stream depends on the image's zlib, so container output can never be byte-identical to a local run with the engines as they are.

Options (owner and accelerator to choose; none taken here):
1. **Accelerator change (recommended):** write zip entries with a fixed `ZipInfo(..., date_time=(1980, 1, 1, 0, 0, 0))` and `ZIP_DEFLATED`. Outputs become reproducible run to run. Cross-host bytes also depend on zlib being the same, which a CI run on the same image proves. It can ride with the skip-and-report request.
2. **Criterion change (owner):** define parity as "the same part names, and identical bytes and CRC-32 for every part"; zip header timestamps are excluded. The check-report JSON stays a whole-file byte comparison.
3. Rejected: running the engines under a faked clock or a monkeypatched `zipfile` (a runtime fork of engine behaviour), or rewriting the outputs' zip headers (normalising outputs, which the POML forbids).

Independent of this choice: the envelope, the marker, Azure I/O, retries and dead-lettering, the refused-plan outcome, the Dockerfile and the requirements do not depend on it. Only `test_golden_parity.py` and the CI in-image parity step do.

## Verified on this machine

- Local engine run (`run_local_e2e.py`, Python 3.13.x, lxml 6.1.3) on nda-01/02/03 + golden plan: all steps exit 0, all 10 checks ok on all three.
- Python 3.11 is installed (`py -3.11`). Docker and Trivy are **not** installed, so no image build, in-container parity or local scan is possible here.
