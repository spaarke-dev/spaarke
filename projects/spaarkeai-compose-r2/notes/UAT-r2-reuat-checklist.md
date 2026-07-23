# Compose R2 — re-UAT checklist (after Phase 9b deploy)

> Maps each of the 7 owner UAT items (2026-07-10) to its fix + the exact re-test path.
> **All 7 fixes require the task-114 deploy to be live.** Allow ≤5 min after deploy for the BFF
> catalog/route cache (5-min IMemoryCache TTL). If a fix appears not to work >5 min after deploy,
> bounce the BFF App Service once, then retry.

| # | Original UAT symptom | Fix (task, commit) | How to re-test | Expected now |
|---|---------------------|--------------------|----------------|--------------|
| 2 | Save → `sessionId is required … first-Save promotion rebind` (400) | 110 (`e13029fc8`) | Browse/open a local `.docx` into a fresh Compose widget (no chat session) → click **Save** | 200; a real `sprk_document` is created; no error banner |
| 3a | Highlight text → popup toolbar has broken layout | 111 (`76eb86872`) | Select text in Compose | Popup is a single clean bar of **AI actions only** (Explain / Compare / Draft / +overflow); no misaligned divider, no overflow |
| 3a+ | (new) point-insertion + browser right-click | 111 | Right-click anywhere in the Compose editor (with or without a selection) | Browser context menu is suppressed; the **AI toolbar** opens at the click point. (Formatting B/I/U/S/Link now lives in the always-visible **top** toolbar.) |
| 3b/3c | Click an AI action → JSON-looking blob in Assistant; "nothing happens" | 112 (`73a268760`) | Select a clause → click **Explain clause** (or Compare to playbook) | A readable **prose** answer appears in the Assistant (explanation + key concepts) — not a `json` code block; exactly one response |
| — | (design confirm) Draft alternative stays inline | 112 leaves it alone; 033/034 own it | Select a clause → **Draft alternative** | Proposed edit lands **inline** as a redline in the document (accept/reject), with a confirmation in the Assistant — NOT a plain Assistant text answer |
| 4 | Assistant "Summarize this document" doesn't see a file uploaded in Compose | 113 (`bd18321d1`) | Upload/Browse a file into Compose → in the Assistant ask **"summarize this document"** | The Assistant resolves **that** document (the one in Compose) |
| 5 | Assistant "edit in Compose" mounted the wrong/stale file | 113 | Upload a NEW file in the Assistant → say **"edit in the workspace Compose"** | The Compose tab opens with the **just-uploaded** file, not a previous one |
| 6 | "open the file in compose tab" → "need the exact layout name or layout ID" | 113 | Close Compose tabs → in the Assistant say **"open the file in compose tab"** | Opens the Compose tab (defaults to the Compose layout) — no layout-id prompt |
| 7 | Upload another file + "edit in compose" → same "need layout name/id" | 113 | Upload a file → say **"edit in compose"** | Opens Compose with that file; no layout-id prompt |

## Deploy prerequisites (task 114)
1. Re-merge origin/master into the branch (co-owned `SendWorkspaceArtifactHandler.cs` / `ChatSession.cs` with redesign-r2) + heads-up core before/after.
2. `pwsh -File scripts/Deploy-BffApi.ps1` — hash-verify MATCH + /healthz Healthy; compose + `active-document` + open-tab routes return 401 (live).
3. Clear Vite cache → recompile shared libs → `& "…Deploy-SpaarkeAi.ps1"`; verify a change marker in the built HTML.
4. §10: publish-size (45.26 MB last measured, ≤60 ceiling), CVE (no new HIGH; pre-existing Kiota only), NetArch Compose facade green.

## Verification status (pre-deploy)
- 110/111/112/113 all committed with passing through-the-wire tests (WAF / real-PaneEventBus / real-TipTap). Build gate green (BFF 0 err; shared libs clean; SpaarkeAi vite production build ✓).
- Not yet on the running env — the above table is actionable only after the task-114 deploy.
