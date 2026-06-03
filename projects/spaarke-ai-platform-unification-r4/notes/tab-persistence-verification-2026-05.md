# Tab Persistence Verification — R4 A-5a (FR-05 verify phase)

> **Task**: 030 (A-5a)
> **Project**: spaarke-ai-platform-unification-r4
> **Phase**: 3 — UQ-03 verify + fix
> **Mode**: Static / read-only code analysis (operator browser smoke deferred — see §6)
> **Date**: 2026-05-26
> **Investigator**: Claude Code (task-execute STANDARD rigor)
> **Scope**: FR-05 / OC-R4-02. Verifies current behavior of workspace tab persistence after R3 task 065 landed the `SessionPersistenceService` extension. Gates task 031 (A-5b).

---

## Executive Summary

| Question | Result | Confidence |
|---|---|---|
| Q1: Tabs persist across browser **refresh**? | ✅ **YES** | High (code path is complete; matches R3 task 065 implementation) |
| Q2: Tabs persist across browser **close/reopen**? | ❌ **NO** (unless user manually resumes via History panel) | High (root cause traced to `sessionStorage`) |
| Q3: Active tab on reopen? | **First-paint placeholder → BFF's default workspace** (Daily Briefing in dev) — NOT Home, NOT last-active | High |
| Q4: Storage location? | **Two layers**: `chatSessionId` in client **`sessionStorage`** (lifetime = single browser tab/window); tab snapshot itself in **BFF Cosmos** (90d retention) — see §4 | High |
| Q5: Spec/UX expectation on reopen? | **Ambiguous in current artifacts** — flagged as open question; FR-05 only asserts "behavior matches operator expectation"; R3 task 065 acceptance text implied refresh-only | Medium |

**Actually broken gap**: Tabs ARE persisted server-side, but the **client-side lookup key (`chatSessionId`) does not survive browser close** because it is stored in `sessionStorage` (window-scoped) rather than `localStorage` (origin-scoped). On reopen the user lands on a fresh `chatSessionId`, the BFF GET targets a session that has no tabs, returns 404, and the workspace falls back to the BFF's default-layout cascade. The R3 backwards-compat memo (2026-05-20) only tested refresh — it did not exercise close/reopen — and so the gap was not detected.

**Recommendation for task 031**: **Promote `chatSessionId` (and `playbookId`) from `sessionStorage` → `localStorage`** with safe migration + a "fresh session" affordance. Estimated effort ~3-5h (well within original 4-8h band). Operator-confirmable in a 5-minute smoke (open 3 widget tabs, close browser, reopen, observe).

---

## 1. Code Evidence — the Persistence Pipeline (post-R3 task 065)

The persistence stack is already wired end-to-end. Code citations below correspond to current `master` / `work/spaarke-ai-platform-unification-r4`.

### 1.1 Client write-through (PATCH on mutation)

**File**: `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx`

- L141-150 — `WorkspaceTabManager` constructed with `onPersistChange` callback that forwards to `persistTabsRef.current`.
- L176-217 — `persistTabs` callback debounces by 200ms, then `PATCH`es the snapshot to `${bffBaseUrl}/ai/chat/sessions/${chatSessionId}/tabs` via `authenticatedFetch`. 404 treated as benign; other failures emit `TELEMETRY_TAB_RESTORE_SAVE_FAILURE`.
- L187 — **Guard**: `if (!chatSessionId || !bffBaseUrl || !isAuthenticated) return;` — write-through is a no-op when `chatSessionId` is null. This is the failure point on close/reopen.

### 1.2 Client read-on-mount (GET → restoreFromPersistence)

**File**: `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx`

- L283-336 — Mount effect issues `GET /ai/chat/sessions/${chatSessionId}/tabs`. 404 → benign. Success → `manager.restoreFromPersistence(snapshot, resolveWorkspaceWidget)`. Then dispatches `tab_count_change` so `ShellStageManager` advances stages.
- L284 — **Same guard**: `if (!chatSessionId || !bffBaseUrl || !isAuthenticated) return;`. If `chatSessionId` is null (the close/reopen case) the GET never fires.

### 1.3 Tab manager — serialize / restore methods

**File**: `src/solutions/SpaarkeAi/src/components/workspace/WorkspaceTabManager.ts`

- L74-91 — `SerializableWorkspaceTab` / `WorkspaceTabPersistenceSnapshot` types.
- L562-576 — `serializeForPersistence()` returns NON-HOME tabs + `activeTabId`. React `Component` refs excluded; `widgetType` drives re-resolution on restore.
- L608-661 — `restoreFromPersistence(snapshot, resolveWidget)` replays each tab via the registry, skips unresolvable ones (graceful degrade), honors `snapshot.activeTabId` only if it matches a restored tab. Idempotency guard: no-op if any non-Home tab is already open.
- L93-99 — Comment explicitly notes: "Tabs are persisted via the BFF `PATCH /api/ai/chat/sessions/{sessionId}/tabs` endpoint so non-Home tabs survive **a page refresh**." (Note the wording — "refresh", not "browser close".)

### 1.4 BFF persistence layer (Cosmos)

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/StoredSession.cs`
- L47-60 — `Tabs: List<StoredWorkspaceTab>` and `ActiveTabId: string?` fields on the Cosmos document. 90-day retention per ADR-015. Additive schema (older documents deserialize to empty list).

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionPersistenceService.cs`
- L1-55 — Redis (24h sliding TTL) + Cosmos (90d) write-through. Partition key `/tenantId`. Failure policy: log + swallow; never blocks the SSE stream.

**BFF endpoints**: `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` registers `PATCH /api/ai/chat/sessions/{sessionId}/tabs` and `GET /api/ai/chat/sessions/{sessionId}/tabs`. **Server-side persistence is correct**.

### 1.5 The smoking gun — `chatSessionId` lives in sessionStorage

**File**: `src/client/shared/Spaarke.AI.Widgets/src/providers/AiSessionProvider.tsx`

- L65-69 — Storage keys:
  ```ts
  const SESSION_KEY_PREFIX = 'sprk_ai2_';
  export const AI_SESSION_CHAT_SESSION_KEY = `${SESSION_KEY_PREFIX}chatSessionId`;
  export const AI_SESSION_PLAYBOOK_KEY = `${SESSION_KEY_PREFIX}playbookId`;
  ```
- L199-214 — `readSession(key)` and `writeSession(key, value)` wrap `sessionStorage.getItem` / `sessionStorage.setItem`. There is no `localStorage` path for these keys.
- L337-344 — `chatSessionId` initial state is seeded from `readSession(AI_SESSION_CHAT_SESSION_KEY)` (i.e. sessionStorage), and `setChatSessionId(...)` writes back to sessionStorage.

**Conclusion**: `chatSessionId` is bound to the lifetime of the browser **tab/window** (sessionStorage semantics per the HTML5 Web Storage spec). Closing the browser process drops it. **Closing the tab and opening a new tab drops it too.** Only an in-place refresh (Ctrl+R / F5) preserves it.

### 1.6 Confirmation — R3 task 065 author's mental model

Block comment in `WorkspacePane.tsx` L106-113 (R3 task 065):

> Per ADR-028: `authenticatedFetch` is obtained from useAiSession() (never snapshotted as a prop or token string). `bffBaseUrl` + `chatSessionId` also come from the session provider so write-through targets the correct session **and we can no-op cleanly when no session id is set yet**.

The author understood the no-op case but framed it as "no session id is set yet" (pre-first-message), not "the prior session's id was lost on close/reopen". The latter scenario wasn't on the design radar.

---

## 2. Q1 — Tabs persist across browser **refresh**? → ✅ YES

**Behavior**: User opens 3 non-Home tabs (e.g. via Context wizards). Tabs PATCH to BFF as each is added (debounced 200ms). User hits Ctrl+R / F5. Browser refreshes the page. `sessionStorage` survives an in-place reload (per the W3C spec — sessionStorage is scoped to the browsing context, not the document load). `chatSessionId` is therefore still present on remount. The GET fires, BFF returns the persisted snapshot, `restoreFromPersistence()` replays the tabs.

**Evidence chain**:
- `AiSessionProvider.tsx` L337-339: `useState(() => readSession(AI_SESSION_CHAT_SESSION_KEY))` — initial state reads sessionStorage on mount. After refresh this returns the same value that was written before the refresh.
- `WorkspacePane.tsx` L284: guard passes because `chatSessionId !== null`.
- `WorkspacePane.tsx` L289-308: GET issues, snapshot returns, restore happens.

**Test signal** (existing): `src/client/shared/Spaarke.AI.Widgets/src/providers/__tests__/AiSessionProvider.test.tsx` L352-357 explicitly verifies that `setChatSessionId('session-abc-123')` persists to sessionStorage.

**R3 backwards-compat memo (2026-05-20, pre-task-065) found this case BROKEN.** Task 065 fixed it. **The fix is real and is verifiably in the code today.**

---

## 3. Q2 — Tabs persist across browser **close/reopen**? → ❌ NO (default path)

**Behavior**: User opens 3 tabs. PATCH succeeds; BFF Cosmos now holds the snapshot. User closes the browser process (or just the last window — `sessionStorage` is per-window). User reopens browser, navigates back to SpaarkeAi. `sessionStorage` is empty — its lifetime ended when the window closed. `AiSessionProvider` mounts, `readSession(AI_SESSION_CHAT_SESSION_KEY)` returns `null`, `chatSessionId` initial state is `null`. The `WorkspacePane` restore effect's guard at L284 short-circuits → GET never fires → restore never happens.

What the user sees instead:
1. **First-paint placeholder** (Spinner) — `WorkspacePane.tsx` L779-791 — while auth + the BFF default-layout fetch resolve.
2. **Auto-installed default workspace tab** — `WorkspacePane.tsx` L377-440 (Wave 2b / task 109). The BFF's cascade picks (in order): per-user default → Dataverse system default (typically Daily Briefing in dev) → null. The chosen layout opens as the first tab. The user's 3 previously-open tabs are gone.
3. **(Maybe) Auto-opened pinned workspaces** — `WorkspacePane.tsx` L480-536 (task 092/101). Reads `localStorage` key `spaarke:workspace:pinned-list` (note: localStorage — pins survive close/reopen!) and opens each pinned workspace. So if the user had pinned any of the 3 tabs, those pins reopen — **but not the unpinned tabs and not the active-tab selection**.

**Partial recovery path**: The Conversation pane's `HistoryOverlay` and `ChatHistoryPanel` (`src/solutions/SpaarkeAi/src/components/ChatHistoryPanel.tsx` L170-184) let the user manually pick a prior session by clicking "Resume" — this calls `setChatSessionId(...)` which then triggers the WorkspacePane restore effect. So a determined user CAN get their tabs back by going to History → Resume. But it is not the default flow and most users won't know to do it.

**Hidden subtlety**: Even after Resume, the `ThreePaneShell.tsx` L442-499 wires `useSessionRestore` to the URL `sessionId` query param, not to `chatSessionId` from storage. So the canonical "resume by URL" path requires the user to have bookmarked or otherwise preserved the session URL — which they generally won't have.

**Conclusion**: For the **default** browser-close-then-reopen flow (no URL bookmark, no manual Resume click), the user lands on Daily Briefing (or whatever the BFF default is) plus any pinned workspaces. The 3 widget tabs they had open are functionally lost from the user's point of view.

---

## 4. Q3 — Active tab on reopen?

Three steady states are possible:

| Scenario | Resulting active tab |
|---|---|
| **Refresh** (sessionStorage intact, GET succeeds) | The `activeTabId` from the persisted snapshot (could be Home, a widget tab id, or null — `restoreFromPersistence` honors snapshot.activeTabId if it matches a restored tab; otherwise active stays null and ShellStageManager + the first auto-installed widget take over) |
| **Close/reopen, no pinned workspaces** | BFF default workspace (Daily Briefing in dev, per Wave 2a seed). The active tab is whatever the auto-install effect dispatches. |
| **Close/reopen, with pinned workspaces** | First pinned workspace in pin order. Default workspace is skipped if it's in the pinned list (see `WorkspacePane.tsx` L408-412); otherwise default + pins all open and the first pin wins activation (each `addTab` auto-activates, so the LAST one dispatched is active — `WorkspacePaneMenu`'s use of pin order matters here). |

There is **no fallback to "Home"** because the hard-coded Home tab was removed in R3 Wave 2b task 109 (Round 8 architectural-unity decision). Every workspace tab now flows through `widget_load → WorkspaceLayoutWidget` — there is no "Home" entity anymore.

---

## 5. Q4 — Where is state stored?

Two distinct layers, with different lifetimes:

| Layer | Stores | Lifetime | Storage |
|---|---|---|---|
| **A. Client lookup key** | `chatSessionId` (the key used to GET tabs from BFF), `playbookId` | **Per browser window/tab** | `sessionStorage` keys `sprk_ai2_chatSessionId`, `sprk_ai2_playbookId` |
| **B. Tab snapshot** | `{ tabs: [{id, widgetType, widgetData, displayName}], activeTabId }` | 24h hot (Redis sliding) / 90d warm (Cosmos) | BFF `StoredSession.Tabs` keyed by `{tenantId, sessionId}` |
| **(adjacent)** Pinned workspaces | `[{ layoutId, layoutName }, …]` | Per-origin durable | `localStorage` key `spaarke:workspace:pinned-list` (`pinnedWorkspaces.ts`) |
| **(adjacent)** Pane collapse state | which of `assistant/workspace/context` is collapsed | Per-origin durable | `localStorage` (`usePaneCollapse.ts`) |

Tabs themselves live in Cosmos and survive close/reopen indefinitely (within the 90d retention window). **But they are unreachable** if the client cannot present the right `chatSessionId` — which it can't, because that key was thrown away with sessionStorage. The infrastructure for durable persistence already exists; only the key plumbing is wrong.

This is **the** structural diagnosis: the fix is small (move two keys from sessionStorage → localStorage) but the symptom looks catastrophic ("my tabs are gone").

---

## 6. Q5 — Spec/UX expectation on reopen?

**FR-05 (R4 spec.md L119)** says: "Tab persistence behavior across browser refresh and browser close/reopen MUST be verified against operator expectation, then any actually-broken gap MUST be remediated." Acceptance: this memo, plus operator-confirmed end-to-end. FR-05 does NOT pre-commit to either outcome (close/reopen = restore vs close/reopen = Home).

**OC-R4-02 (plan.original.md L272)** says: "Original R3 verification said tabs persist; user feedback contradicts. Verify or remediate first? Verify first; user feedback says different gap is the visible issue."

**R3 task 065 acceptance text** (recovered from `WorkspaceTabManager.ts` block comment L93-99): "Workspace tabs are persisted via the BFF `PATCH /api/ai/chat/sessions/{sessionId}/tabs` endpoint so non-Home tabs survive **a page refresh**." Explicitly scoped to refresh; close/reopen was out of the original scope.

**R3 backwards-compat memo (2026-05-20)** §c: "tabs are NOT persisted across **refresh**" — also scoped to refresh, not close/reopen.

**Verdict**: The spec/design artifacts do **not** unambiguously assert "browser-reopen MUST restore tabs". They were written when refresh was the only persistence target. The user-feedback referenced by OC-R4-02 is — based on the failure mode in §3 — almost certainly the close/reopen case: the user expected tabs to come back, they didn't, the user inferred persistence was broken when actually refresh-persistence works and close/reopen-persistence was never built.

**Operator-validation gate (OPEN)**: Confirm with operator: when a user closes their browser at end of day and reopens it the next morning, what SHOULD happen?
- **(a)** Restore the prior workspace tabs (the natural expectation for a "workspace"-style product)
- **(b)** Open the default workspace (Daily Briefing) and let the user pick from History or pins (the lighter touch — closer to current behavior, fewer surprises)
- **(c)** Some hybrid — restore tabs IF the session is recent (e.g. < 24h) and default otherwise

Recommendation §7 assumes **(a)** because (i) FR-05 framing as a "broken gap to remediate" presupposes a non-trivial fix; (ii) the architecture already invests in 90d Cosmos persistence, which makes no sense if the intent is "default to Home on reopen"; (iii) the user feedback that triggered OC-R4-02 explicitly contradicted "tabs persist", which means the user expected them to. **Operator to confirm before 031 begins.**

---

## 7. Actually-Broken Gap + Recommendation for Task 031 (A-5b)

### 7.1 Statement of the gap (precise)

> Workspace tabs are correctly persisted to BFF Cosmos (90d retention), and the restore-on-mount client code works. However, the lookup key `chatSessionId` is stored in `sessionStorage`, whose lifetime ends when the last browser window of the origin closes. On browser close/reopen the client has no `chatSessionId` to present to the BFF, the GET is skipped, and the user lands on a freshly-defaulted workspace. The data is intact in Cosmos but unreachable from the client.

### 7.2 Recommended fix path — **Path A: promote `chatSessionId` + `playbookId` to localStorage**

**Files to change** (all client-side; no BFF change required):

1. `src/client/shared/Spaarke.AI.Widgets/src/providers/AiSessionProvider.tsx` — replace `sessionStorage.getItem/setItem` calls in `readSession` / `writeSession` helpers (L199-214) with `localStorage` equivalents. Bump the key prefix from `sprk_ai2_` → `sprk_ai3_` (or keep `sprk_ai2_` but document the silent semantic change — operator's call). Add a one-shot migration that reads the old `sessionStorage` key, copies to the new `localStorage` key, and clears the old, so a user mid-session at the time of the deploy doesn't lose their session.
2. **(Optional)** Add a small `clearChatSession()` exposed via `useAiSession()` so the History pane's "Start new conversation" button can explicitly drop the current `chatSessionId` (otherwise the user is stuck on their old session forever because there's no expiry).
3. **(Optional)** Add a stale-session detector: if the persisted `chatSessionId` is older than N days (e.g. 30) or BFF GET returns 404, auto-clear it and start fresh.
4. **Test**: extend `AiSessionProvider.test.tsx` to assert localStorage rather than sessionStorage; add a new test for the migration path.

**Acceptance criteria (operator-confirmable)**:
- Open 3 non-Home workspace tabs in SpaarkeAi.
- Close the entire browser process (not just the tab).
- Reopen browser, navigate to SpaarkeAi.
- Observe: same 3 tabs return; active tab is the one that was active at close-time. p95 restore latency < 500 ms (NFR target inherited from R2 D-08).

**Risk**:
- Migration glitch could lose a single in-flight session for users at the moment of deploy. Mitigation: the migration is one-shot and self-healing; worst case the user has to click "Resume" in History once.
- A new edge case: if two browsers (Chrome + Edge) on the same machine both have an SpaarkeAi tab open, they will now share a `chatSessionId`. **This was already the case** for pinned workspaces and pane-collapse state (both already in localStorage), so behavior is consistent with existing conventions.
- ADR-028 / auth: no impact. `chatSessionId` is opaque to auth; the BFF still validates the JWT and uses `tenantId` from the JWT to scope the Cosmos GET. There is no token snapshot in localStorage. Spaarke Auth v2 invariants are preserved.

**Estimated effort**: 3-5 hours
- ~1h: file edits + key migration logic
- ~1h: test updates
- ~1h: operator smoke + cross-browser verification
- ~1h: optional clearChatSession + stale-session detector
- Buffer: ~0.5-1h

This is well within the 4-8h band in `plan.original.md` L203.

### 7.3 Alternatives considered (rejected)

**Path B: derive `chatSessionId` from a stable user-scoped key.** E.g. compute it from `oid` (Azure AD object id) + workspace name. Would survive close/reopen by construction. **Rejected** because (i) it breaks the "multiple concurrent sessions per user" model (currently a user can have a long-running session AND start a fresh side-conversation); (ii) it requires a BFF schema change to support multi-session lookup by `oid`; (iii) it conflicts with the existing History feature which assumes session IDs are independently addressable.

**Path C: URL-bind the session.** Always include `?sessionId=...` in the SpaarkeAi URL and rely on browser history. **Rejected** because (i) it requires every internal navigation to preserve the query string; (ii) it shifts UX from "your workspace is restored" to "you bookmark URLs" which is unusual for a Workspace product; (iii) the existing URL-restore path (`useSessionRestore`) is already plumbed but never reached in the default flow, so this would be a bigger refactor.

**Path D: do nothing; document close/reopen → default-workspace as the expected behavior.** **Rejected** because OC-R4-02 explicitly says the user feedback identifies a gap; the question for 031 is "what fix", not "whether to fix".

---

## 8. Method Notes & Limitations

1. **Static / read-only verification.** No deployed-env browser smoke was performed by this investigation. The behavioral conclusions in §2 and §3 are derived from the code paths in `WorkspacePane.tsx`, `AiSessionProvider.tsx`, and `WorkspaceTabManager.ts` cross-referenced with the W3C `sessionStorage` semantics. Confidence is **High** for refresh (Q1) and close/reopen (Q2) because both paths are fully traceable in code; **Medium** for the precise active-tab outcome on close/reopen (Q3) because the auto-install + pin auto-open + ShellStageManager interaction is timing-sensitive. **Pre-031 operator smoke recommended** (5 minutes — see §7.2 acceptance criteria).

2. **The R3 backwards-compat memo (`projects/spaarke-ai-platform-unification-r3/notes/backwards-compat-verification.md`)** identified a refresh-persistence gap, R3 task 065 fixed it, and that fix is verifiably in the code. The R3 memo did NOT cover close/reopen — so the "original R3 verification was wrong" framing in OC-R4-02 is more precisely "the R3 verification was correct for the scenario it tested, and the user-reported gap is a different scenario the verification didn't exercise". Worth noting in the R4 lessons-learned: verification scoping should include both refresh AND close/reopen for any state-persistence acceptance criterion.

3. **No production code was modified by this task.** Only the file at this path was created.

---

## 9. Pre-031 Operator Gate (REQUIRED)

Before task 031 begins, operator should confirm:

- [ ] **Q5 expectation**: Is the desired behavior on browser close/reopen to **restore the prior tabs** (Path A), **default to Daily Briefing** (acceptable status quo), or **a hybrid** (e.g. restore if recent, default if stale)?
- [ ] **Q1/Q2 smoke** (optional but recommended): Open 3 non-Home tabs in SpaarkeAi; (a) refresh — confirm tabs return; (b) close entire browser, reopen — observe whether tabs return or you see Daily Briefing.
- [ ] **Migration concern**: Is a one-shot in-place migration of the `sessionStorage` → `localStorage` key acceptable, or does R4 prefer a hard cutover (everyone gets a fresh session at deploy)?

If operator confirms Path A (recommended), task 031 proceeds with the 3-5h scope in §7.2. If operator picks "status quo + document", task 031 is downscoped to a doc-only fix (~1h) and FR-05 acceptance becomes a documentation update rather than a code change.

---

*Verification complete. Memo author: Claude Code task-execute (STANDARD rigor). Next: operator review → task 031 scope confirmation → implement.*
