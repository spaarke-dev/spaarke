# R1 UAT Feedback — 2026-07-17 (Ralph, dev)

> First live UAT pass after the R1 dev deploy (BFF `spaarke-bff-dev` + SpaarkeAi + Create*Wizard code pages). Prioritized remediation backlog (R1.1). P0 = functional break; P1 = high-value UX; P2 = polish/larger rework.

## 🔴 P0 — Create-matter flow is broken (the headline R1 deliverable)

| # | Symptom | Root-cause diagnosis |
|---|---|---|
| 9 | After uploading a file + "Create a matter", the **wizard did NOT open** | Text-path (typed request) likely doesn't trigger client `launchSurface` — 013b wired the CHIP path (`useConsumerChips`), not the agent-turn/text path. The SpaarkeAi client must detect a `surface_launch` disposition from the agent turn and launch. |
| 10 | Assistant said *"I've drafted a new matter proposal… proceed to create this matter record in the system now?"* + chips "Proceed to create the matter record now" | This is **old draft-then-create-in-chat behavior**, not R1's draft→wizard. The chips shown are NOT the R1 create-matter chips ("Add a related task"/"Add a to-do") — the agent is not (only) using the create-matter capability. |
| 11 | "yes create" → **Confirm Action: SYS-Dataverse_Create_Record** gate | The agent invoked the **raw `dataverse.create_record` tool** — a path that exists in the agent's tool set independent of the create-matter capability. |
| 12 | Error: *"SYS-Dataverse_Create_Record could not be dispatched: Entity 'sprk_MatterType_Ref' With Id = 335df5f6-98ed-eb11-bacb-6045bd0383fc Does Not Exist"* | **The exact P1 failure R1 targets** — the LLM resolved a closed-set matter-type to a **hallucinated/nonexistent GUID**. |

**Verified NOT the cause** (catalog is correct): create-matter binding = SurfaceLaunch, `requiresnoattachedrecord=Yes`, tool-description = R1 rewrite ("do NOT call dataverse.create_record"); `CREATE-MATTER@v1` Action `allowstools=No`.

**The fix (two parts):**
- **(a) ✅ DONE 2026-07-17 — gate the raw dataverse Write tools by context.** Owner decision: don't drop them, gate by context (re-enablable for future). Flipped `dataverse.create_record` / `update_record` / `delete_record` `sprk_availableincontexts` **Chat → Playbook** (live spaarkedev1) so the assistant agent no longer projects them → all record mutation must go through SurfaceLaunch. `email.draft` + `memory.write` left in Chat (legit chat flows). Instantly reversible (flip back to Chat/Both).
- **(b) TODO — wire the TEXT path to launch the wizard.** Precise root cause found: `BindingCapabilityTool.InvokeCoreAsync` (Services/Ai/Chat/BindingCapabilityTool.cs) reads the dispatch's `chunk.Summary/Content` but **ignores `chunk.Disposition`/`ConsumerType`**, and its return message (lines ~321-327, from the 2026-07-07 compose UAT rounds) tells the agent *"invoke the corresponding write tool NOW"* — never made SurfaceLaunch-aware. Fix: when `chunk.Disposition == "surface_launch"`, (1) emit a client SSE event (new `ChatSseSurfaceLaunchData` — mirror the shipped `elicitation_modal` pattern; `_sseWriter` already exists) carrying `consumerType` + the draft payload (draftValues + resolvedLookups + fileIds), and (2) return a surface-launch-aware agent message ("a pre-seeded {consumerType} surface is opening for the user; do NOT invoke any write tool; the surface owns the create"). Then the **SpaarkeAi chat SSE client** handles that event → `launchSurface` (reuse 013b's `launchSurface`). This is the text-path analogue of 013b's chip-path wiring.

## 🟠 P1 — High-value UX (initial load + file upload)

| # | Item | Notes |
|---|---|---|
| 1 | **Get-started cards on first open** — (1) Summarize a document → opens file-browse popup; (2) Create a matter → opens Create Matter wizard; (3) Compose a document → opens the Compose tab (blank) | Present before any user input. Overlaps QuickStart (task 041) — reuse/extend. |
| 5 | **File-upload entries take too much UI space** — collapse the per-file detail entries under a drop-down (keep them, hide by default) | |
| 5b | **Remove "Insert into document" after every line item** — only show when there's truly something to insert (rare) | |
| 6 | **Auto-scroll to latest** on any new entry (file add or message) — viewport pins to newest message (scroll up for history), like MS Copilot | |
| 7 | **File-upload options**: "Summarize this file" (NOT "Document"), "Create a matter", "Draft a response", "More…" | |
| 8 | **"More…" opens Quick Start** (not playbooks — playbooks are retired) | |

## 🟡 P2 — Header, History, My Assistant (larger rework)

| # | Item | Notes |
|---|---|---|
| 2 | **Header tool reorder**: left = History (change word → history icon, Claude-Code style); middle = New session (keep icon); right = Tools drop-down | |
| 3 | **History is not stored** — no persisted session history | Needs a history store + retrieval. |
| 4 | **My Assistant → convert modal to a WIZARD** with richer descriptions. Steps: **(1) "Your role"** — intro explanation + Select primary role (dropdown) + Office location (with (i) popover on what/how/why); **(2) "Practice areas"** — description + dropdown (**BUG: dropdown doesn't work in the modal**) + "Describe your focus areas" (description + (i) popover with examples); **(3) "Preferences"** — "Describe your preferences in using the Assistant" (1-2 sentence explanation + (i) icon with examples) | Item 42 built the modal; this is a wizard rework + the dropdown bug. |

## 🟢 Already-scoped (owner-directed, pre-UAT)
- **list-tasks → SurfaceLaunch filtered Event-Task grid** (050 decision; new client vertical, not yet built).

## Suggested sequencing
1. **P0 create-matter** (a)+(b) — the headline flow must work; blocks meaningful UAT of the create vertical.
2. **P1 upload/first-load UX** (1, 5, 5b, 6, 7, 8) — high-frequency, mostly SpaarkeAi client.
3. **P2** (2, 3, 4) + the list-tasks grid vertical.
