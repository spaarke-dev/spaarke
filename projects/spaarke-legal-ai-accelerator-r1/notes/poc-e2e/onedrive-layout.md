# OneDrive and team-site layout for testing (from 2026-11-10)

One rule: **current files live only where the package puts them; everything from earlier rounds is in the archive.**
Copilot's search can still find archived files (gap #34), so tests identify which file was used by its
verification code, never by its name alone.

## Who owns which folder

| Folder (OneDrive `Documents/`) | Owner | What it is | Do we put files here? |
|---|---|---|---|
| `Cowork/Skills/<skill>/` | Microsoft defines the location; the content is ours | Cowork loads every `SKILL.md` found here at the start of each conversation (Microsoft Cowork FAQ: `Documents/Cowork/skills/<name>/SKILL.md`, up to 50 skills; tested T-01/T-02) | **Yes**: our skill folders, from the package zip |
| `Cowork/Profile/legal-profile.md` | Ours (written by `accelerator-setup`) | Attorney profile read by our skills | Written by setup, not copied |
| `Cowork/Tasks/`, `Cowork/Projects/`, `Cowork/AGENTS.md` | Cowork | Cowork's own task inputs/outputs, projects, and a note saying the folder is Cowork's durable, agent-read-only user folder | **No**: Cowork writes these |
| `Copilot/Created/` | Microsoft 365 Copilot | Where Copilot saves files it creates (Word Legal skill outputs, revised copies; observed, not documented) | **No** |
| `Copilot/Memory/v2/` | Microsoft 365 Copilot | Memory store (`MEMORY.md`, `PREFERENCES.md`, `CONTEXT.md`, JSON). Undocumented; ours are empty ("none yet") | **No, never edit** |
| `Legal Knowledge/` | Ours | Playbooks, review method, Start here (same tree as the team library) | **Yes**: from the package zip |
| `Spaarke Test Documents/` | Ours (tests only) | NDAs and legal hold drafts to review; outputs land beside them | **Yes**: from the test-documents zip |

`Documents/Copilot/` is **not** a folder Copilot reads instructions from. Nothing Microsoft publishes says so, and
in Act 7 Word found a file there only through search. It is Copilot's output and memory area.

## Archive

Everything from earlier rounds is in OneDrive **`Spaarke test archive (not current)/`** (drive root), with the
original path kept inside it:

| Was | Now |
|---|---|
| `Documents/Cowork/MVP-Evidence/`, `Playbooks/`, `Test/` (incl. all `POC-Act*/screens/`) | `Spaarke test archive (not current)/Documents-Cowork/…` |
| `Documents/Cowork/Skills/nda-library-review/`, `spaarke-visual-probe/` | `…/Documents-Cowork/Skills/` |
| `Documents/Cowork/Profile/legal-profile.md` | `…/Documents-Cowork/Profile/` |
| 22 test task folders in `Documents/Cowork/Tasks/` | `…/Documents-Cowork/Tasks/` |
| `Documents/Copilot/Skills/playbook-document-review.md` (owner protocol v1.0, PBR-1109-A) | `…/Documents-Copilot/Skills/` (source copy in the repo: `src/accelerators/core/shared-references/review-method.owner-v1.0.md`) |
| `Documents/Copilot/Created/` test outputs (Brightwater ×3, Harbor Point ×6, Identify Regulatory Gaps) | `…/Documents-Copilot/Created/` |
| `Microsoft Copilot Chat Files/` mutual-nda-review-playbook.docx, playbook-format.md, word-saved-playbooks-org-sharing-2026-11-06.md | `…/Microsoft Copilot Chat Files/` |
| `Documents/Legal AI Accelerator/` (empty) | `…/Legal AI Accelerator/` |

Paths in `results.md` and `gap-log.md` before 2026-11-10 refer to the old locations; prefix them with the archive
folder to find the file.

**Left in place on purpose:** `Cowork/Tasks/` document-review-request-2026-10-30, patent-search-…, reviewing-
invention-disclosure-… (not accelerator tests); `Cowork/Projects/NDA Review-cd425d15` (a Cowork project object);
`Copilot/Created/Spaarke_ExecSummary_UPDATED.docx`; `Copilot/Memory/`; `Microsoft Copilot Chat Files/` other uploads.

**Still to move:** `Documents/Copilot/Created/_before-Act2/mutual-nda-review-playbook.docx` (an old Act 1
playbook). The move fails with "resource locked" (423), so the file is open somewhere (Word, or a Copilot
session). Close it and move it to `…/Documents-Copilot/Created/_before-Act2/`, where its twin
`mutual-nda-review-playbook 2.docx` already is.

**Not file clean-up, but stale and visible:** Word's saved playbook cards from Act 2 (no delete, gap #32); the
Cowork plugin *Spaarke NDA (MVP)* (same skill names as the new skill folders, so turn it off before C-1); the three
Copilot Studio agents and three DA packages from spike DA-1.

## Team site (Acme Company) — unchanged

`Shared Documents/Legal Knowledge/<NDA | Legal Hold Notice>/` with `Playbook master`, `Policies`, `Precedents`,
`Templates`, `Archive`, and the Word playbook at the top of each type folder. This is the team-tier test setup, so
it was not touched. Note: `NDA/Archive/` holds seven superseded NDA playbooks (v1.0, v1.1), which search can
surface the same way (gap #34 at team level). `POC Workspace/` holds the Act 3–6 matters and the NDA batch.

## OneDrive sync on the test PC

On 2026-11-10 the OneDrive client was running but every download of a cloud-only file failed ("The cloud file
provider exited unexpectedly"), and the local copy was behind the cloud since about 2026-11-07. The moves above
were made in the cloud. Restart OneDrive (cloud icon › Settings › Pause/Quit, then start it) before installing
the package, then check that `Documents/Cowork/Test` has disappeared locally.
