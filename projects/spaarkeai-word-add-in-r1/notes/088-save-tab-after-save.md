# Task 088 — the Save tab after a save, Open Document, and Open on the collision prompt

> UAT round 3 (2026-10-03) items UAT-1, UAT-5, UAT-6, UAT-7 — `notes/042-uat-round3-2026-10-03.md` §1, §3a.
> Owner decision the same day: **"Keep the form"** after a save; record links open **in the Spaarke app**.
> Executed in wave UAT3-W1 beside task 092 (same worktree; 092 owns App.tsx / FindView / FindResultsList /
> useFindRecordMatches). Nothing committed by this task; the main session commits after the wave.

## 1. What shipped

| Area | Change |
|---|---|
| **Saved state** (`SaveFlow.tsx`) | The full-screen success card (and its "Save Another") is gone. After a save the pane shows a **confirmation bar** ("Saved to Spaarke" / "New version saved to Spaarke"; "Filed to {record}." or "Not filed to a record.") with **View Document** + **Copy Link**, then the **Document Name** and the **Profile**, then the footer **[Cancel] … [Open Document] [Saved]**. "Saved" is a gray, disabled button (CreateTodoView's `savedBtn` style, copied). |
| **Save version** | In the saved state the next save is FR-11's existing version save of the saved document (`existingDocumentId` + `isNewVersion: true`) — no second mode. "Saved" becomes "Save version" when the name differs from the saved name (reverting returns to "Saved") or a Generate Profile is **accepted** (`DocumentProfileSection.onProfileGenerated`, fired on the 202; `useDocumentProfile.generateProfile` now resolves `true`/`false`). |
| **Open Document** | Moved from task 027's subtle "Open document record" link in the Profile section to the footer, immediately left of Save/Saved. Opens the saved document, else the resolved one. |
| **Record links** | View Document, Open Document and the duplicate card's **View Existing Document** (which used to pass an id where a URL was expected — a broken link) all open the `sprk_document` RECORD via `openRecordLauncher`. No Save-tab path opens the Graph `webUrl` any more; `SaveFlow.onViewDocument` and SaveView's `window.open(url)` fallback are removed. Copy Link still copies what it copied (owner). |
| **`appname=`** | `buildOpenRecordUrl(org, etn, id, appName)` adds `appname=<encoded>` first; blank → no `appname` (pre-088 behaviour). `openRecord` defaults `appName` to `configuredSpaarkeAppName()` (`process.env.SPAARKE_APP_NAME`), so **every** caller — including 092's Find "open" buttons, which call `openRecord` without an app name — gets it. `sendEmailService`'s record link passes it too. |
| **Build setting** | `SPAARKE_APP_NAME` in `webpack.config.js` (DefinePlugin, beside `ORG_URL`): unset → `sprk_MatterManagement`; set to empty → no app. `deploy-office-addins.yml` sets it explicitly; `.env.example` documents it. |
| **Collision (server)** | `OfficeService.ResolveNameCollisionAsync` now carries the owning document's **id + name on every owned collision** (all content types) and a new **`SaveError.CanSaveAsVersion`** = editable AND filed to the target record (task 055's #1005 rule, moved from "withhold the id" to a flag). `OfficeEndpoints.WithholdCollisionIdentityIfUnauthorizedAsync` strips id, name **and** the flag without Read; `NameCollisionExtensions` writes `canSaveAsVersion` **only alongside an id**, so an unreadable caller gets exactly the pre-088 payload. |
| **Collision (pane)** | `describeCollisionFailure` maps `canSaveAsVersion` → `collisionCanSaveAsVersion` (true only with an id; JSON nulls treated as absent). The prompt shows **Keep both · Save as new version (flag only) · Open (id present) · Dismiss**. **Open** calls the existing `GET /api/documents/{id}/open-links` (Read re-checked by its filter), launches its **`webUrl`** via `openFileUrl` (capability: `canOpenRecord` → `openBrowserWindow`, else `window.open(url,'_blank')` with `opener` cut; a blocked window is reported), and shows any failure's reason inline. |

**Placement (CLAUDE.md §10):** extends `OfficeService` / `OfficeEndpoints` / `SaveError` — one boolean on an existing
refusal; no new service, route, DI registration, package or background work. Belongs in the BFF because it is part of
the synchronous save's refusal and its existing ADR-008 Read gate.

## 2. Outlook (NFR-10 — nothing Word-only)

The saved state is host-neutral. What differs is decided by data/capability, never `hostType`: a "Save version"
needs document bytes, which a pane has only when SaveView passes `captureDocumentContent` (gated on
`canGetDocumentContent`). Without bytes (Outlook — an email is immutable and has no version path) the saved name is
read-only and the button stays "Saved". **Cancel** keeps its existing meaning (clear + reset) and so also leaves the
saved state — an empty form, which is what "Save Another" gave Outlook before. Collision **Open** also works for an
Outlook email collision (the id now travels for immutable captures too; `canSaveAsVersion` is always false there).

Escalation trigger 2 (an Outlook flow pinned by gated suites breaks) **did not fire**: the only gated pin of
"Save Another" was a *Word* capture-at-save test (`SaveFlow.captureAtSave`), rewritten to the new flow with its
invariant intact (the second attempt re-captures bytes); `SaveFlow.test.tsx`'s "save another" case is an empty stub.

## 3. Escalation trigger 1 FIRED — `ms-word:` cannot be launched by a supported call → `webUrl` everywhere

What was tried (desk research via the `researcher` subagent, 2026-10-03; no Office host available here):
- `Office.context.ui.openBrowserWindow(url)` — Microsoft Learn (Office.UI): *"The full URL … including protocol (http
  or https) … Other protocols like mailto aren't supported."* OfficeDev/office-js **#2820** ("openBrowserWindow does
  not work for Office URI schemes") closed **by design**. Also: OpenBrowserWindowApi 1.1 is **not supported in Office
  on the web / new Outlook**.
- `window.open('ms-word:…')` from a task pane — **undocumented** on WebView2 / WKWebView / the web iframe. The only
  positive evidence (office-js **#6926**) uses a synthetic `<a href>` click — a third mechanism the trigger forbids.

So Open launches the open-links **`webUrl`** (Word for the web) on every host; `desktopUrl` is never used. The
POML's "launches DesktopUrl or WebUrl per capability" reduces to: **webUrl, opened by the capability-chosen
mechanism**. If the owner wants desktop Word, the follow-up is a live probe of `window.open(desktopUrl)` from Word
desktop's pane (and a decision on the anchor-click route), not a code change here.

## 4. Escalation trigger 3 — judged NOT fired (cost stated)

No new kind of round trip: the Read gate is the existing `AuthorizationService.GetCallerAccessAsync` call (one
OBO-scoped Dataverse access query), which already ran on every 409 that carried an id. It now also runs on owned
collisions filed to another record (previously their id was dropped before the gate). Cost: **≤ 1 access query per
refused save** that resolved an owning document — a rare, user-facing refusal path. The colliding-row lookup
(`FindCollisionTargetByLocationAsync`) already ran on every collision.

## 5. Open items for the owner / main session

1. **A version save does not rename.** AC2 re-enables "Save version" on a name edit, and the request carries the
   new `document.title`, but the server's version path never renames (task 023 D-4/D-5). The record keeps its name.
   Owner decision: rename on the version path (a server change on task 023's path), or drop name edits as a trigger.
2. **The saved state lives in SaveFlow.** A tab switch remounts the Save tab and loses it (App.tsx — 092's file this
   wave — would have to hold it). After a remount the next save is a create → a self-collision prompt that offers
   Save as new version (if filed to a record) or Keep both. 042 §2's "write the identity mark into the open document"
   is the real fix.
3. **"A completed Generate Profile" = accepted (202).** The hook does not poll the job (#1090).
4. **Deploy order: BFF before the add-in.** The pane offers "Save as new version" only on `canSaveAsVersion`; an
   older BFF never sends it, so that button would disappear until the BFF ships.
5. **Unverified (note 025 Q6):** whether a user can OBO-write a new version of an item the BFF created app-only. If
   not, "Save version" after a create is refused (OFFICE_009); the error offers **Save as new document**, which now
   also leaves the saved state (tested).
6. The Word Send Email "Spaarke email" composer URL is a web-resource page, not a record link — no `appname` added.
7. The `researcher` subagent wrote its project memory to `.claude/agent-memory/researcher/` (MEMORY.md + one note).
8. **Publish size: owed** (§6). **Live AC10: open** (no deploy/host in this session).

## 6. Gates

See the POML `<notes>` for numbers. Publish-size delta is **owed**: this worktree was built repeatedly during the
task, so per root CLAUDE.md §10 hazard three its publish is not comparable to a fresh master build, and the wave
rules forbid a commit-less branch copy. The BFF change is three source files with no package, file or DI change.

## 7. Tests (ADR-038 — what is in scope)

- **Server** (`tests/integration/data-mutation/OfficeVersionSave/`): readable+filed → id, name, `canSaveAsVersion:
  true`; readable+filed elsewhere → id, name, `false`, Read evaluated, nothing written/re-associated; unreadable
  (filed, and filed elsewhere — new) → no id, no name, **no flag key**, Read evaluated; immutable capture filed to the
  target → id present, flag `false`. Seeded both ways: gate bypassed → both unreadable tests red; association ignored
  → filed-elsewhere test red; `isEditable` dropped → immutable test red.
- **Pane**: `SaveFlow.savedState` (new, gated), `SaveFlowCollision` (Open, flag-only offer — seeded: offering on id
  presence turns 2 tests red), `errorMessages.collision`, `openRecordLauncher` (appname default/empty/encoded,
  `openFileUrl`), `sendEmailService`, `DocumentProfileSection`, `SaveFlow.openRecord`, `SaveFlow.captureAtSave`,
  `SaveView` (two webUrl-forwarding tests replaced by one pinning that the seam is gone).
