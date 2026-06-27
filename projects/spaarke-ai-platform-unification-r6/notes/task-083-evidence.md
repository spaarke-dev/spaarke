# Task 083 Evidence — References Resolver (Pillar 8 / FR-51)

**Phase**: D — Command Router + Integration + Closeout
**Wave**: D-G1 (parallel with 081, 082)
**Status**: ✅ Complete (module + tests + integration handoff)
**Rigor**: FULL
**Completed**: 2026-06-18
**Worktree**: `c:/code_files/spaarke-wt-spaarke-ai-platform-unification-r6`
**Branch**: `work/spaarke-ai-platform-unification-r6`

---

## Summary

Built `ReferenceResolver.ts` per spec FR-51 — resolves the 3 reference types
emitted by the CommandRouter parser (task 080) at PARSE TIME so the agent
receives resolved entities in its prompt without extra LLM round-trips.

The module is **frontend-only**. It uses ONLY existing BFF endpoints
(`/api/ai/scopes/*`) via the existing `authenticatedFetch` contract.
**BFF publish-size delta = 0 MB** per ADR-029.

ConversationPane.tsx was NOT modified by this task (parallel coordination
with 081 + 082). Integration handoff is included in this evidence note for
the main session to wire after all three Phase-D-G1 sub-agents return.

---

## Files Created

| File | Purpose | LOC |
|---|---|---|
| `src/solutions/SpaarkeAi/src/components/conversation/ReferenceResolver.ts` | Resolver module + adapter helpers + module-scope cache | ~470 |
| `src/solutions/SpaarkeAi/src/components/conversation/ReferenceResolver.test.ts` | 27 unit tests (jest) | ~440 |

---

## 3 Resolvers Wired (Q6 closed)

| Type | Resolution path | Cache key | Fallback (NFR-01) |
|---|---|---|---|
| `scope` | `scopeFetch` adapter queries `/api/ai/scopes/{skills\|actions\|tools\|knowledge\|personas}?search=…&pageSize=1` — first non-empty match wins | `tenantId:sessionId:#scope` | `resolved: false`, `displayName = rawToken` |
| `entity` | Match against host's `entityContext.entityType` (case-insensitive) — short-circuits `@matter` to current matter record | `tenantId:sessionId:@<value>` | `resolved: false` (out-of-context refs degrade; see "Trade-off surfaced" below) |
| `file` | (a) Scan `openTabs` in-memory by `displayName` / `widgetData.filename` (no network); (b) on miss, call `fileLookup` adapter against the session-files index; (c) on both miss → unresolved | `tenantId:sessionId:#<filename>` | `resolved: false` |

Disambiguation between `#scope` and `#<filename>` is handled at the parser
(task 080 `extractReferences`): the literal token `#scope` (value === "scope")
becomes `kind: 'scope'`; any other `#<value>` is `kind: 'filename'`. The
resolver consumes the parser's classification; no fallback re-classification
needed.

---

## Tests: 27 passed / 0 failed

Run:
```
cd src/solutions/SpaarkeAi && npx jest src/components/conversation/ReferenceResolver.test.ts
```

Coverage by acceptance criterion:

| Criterion (POML) | Test name(s) | Verified |
|---|---|---|
| 3 reference types resolve | "scope resolution" / "entity resolution" / "file resolution" describe blocks | ✅ |
| `@matter` resolves to current matter | "resolves @matter to the host entity context" | ✅ |
| `#contract.docx` resolves to session file or open tab | "resolves a file ref via open workspace tabs" + "falls back to fileLookup" | ✅ |
| Cache hits avoid re-fetch | "does not re-fetch on a cache hit (scope)" + "(file)" | ✅ |
| `tenantId` in cache keys (ADR-014) | "isolates cache by tenantId" | ✅ |
| Cache invalidation on `/clear` | "invalidateSession clears entries for that session only" + "does not clear entries for OTHER sessions" | ✅ |
| Unresolved degrade with `resolved: false` | "never rejects when the host degrades all paths" (NFR-01 baseline) + 8 per-type degradation cases | ✅ |
| Mixed resolved + unresolved in one message | "handles a composition like `/draft response to @opposing-counsel about #motion-to-dismiss.pdf`" | ✅ |
| Race: concurrent resolveAll calls don't double-fetch | "does not double-fetch the same token across concurrent resolveAll calls" | ✅ |
| File ref checks open tabs FIRST | "resolves a file ref via open workspace tabs (no network)" — asserts `fileLookup` is NOT called | ✅ |

Other coverage:

- Case-insensitive matching on `@<entityType>` and `#<filename>`
- `scopeFetch` skips a 500-response catalog and tries the next
- Tenant-id-missing fallback (resolver still works, no caching)
- Adapter helpers (`createScopeFetch` order-of-traversal; `createFileLookupFromSessionMap`)

---

## Cache hit/miss behavior verified

Empirical trace:

1. `resolveAll([#scope], ctx)` (cold) → `scopeFetch` invoked once.
2. `resolveAll([#scope], ctx)` × 2 more → cache hit, `scopeFetch` count stays at 1.
3. `invalidateSession(sessionId)` → cache entries for that session cleared.
4. `resolveAll([#scope], ctx)` → cold again → `scopeFetch` invoked second time.

This matches ADR-014 expectations + NFR-05 (single PaneEventBus channel
reused — the `/clear` hard-slash executor in task 081 dispatches on the
existing `conversation` channel and the host effect calls
`invalidateSession(sessionId)`).

---

## Unresolved degradation verified

Per NFR-01 ("Resolver never blocks the conversation"):

```typescript
// Test: "never rejects when the host degrades all paths"
const refs = [mkScope("scope"), mkEntity("matter"), mkFile("contract.docx")];
const out = await resolveAll(refs, ctxFor()); // no adapters, no entityContext, no tabs
// All three return { resolved: false, displayName === rawToken }
// The promise never rejects.
```

Per-source degradation paths:
- `scopeFetch` throws → caught, returns `unresolvedRef('scope', raw)`
- `fileLookup` throws → caught, returns `unresolvedRef('file', raw)`
- `entityContext` mismatch → returns `unresolvedRef('entity', raw)`
- Empty `tenantId` (ADR-014 fallback) → resolves WITHOUT caching but still returns valid `ResolvedReference[]`

---

## ADR-014 cache key audit: PASS

Cache key format: ``${tenantId}:${session}:${rawToken}`` where `session` is
the literal `sessionId` or the sentinel `__no-session__`. The function
`buildCacheKey` is the SOLE producer.

Test "isolates cache by tenantId" confirms that two `resolveAll` calls with
the same `rawToken` + `sessionId` but different `tenantId` produce TWO
backend fetches (cache miss across tenants) — proving tenant isolation.

`invalidateSession(sessionId)` keys on the session segment, so clearing
session A does NOT affect session B (test: "does not clear entries for
OTHER sessions").

---

## NFR-01 non-blocking: verified

Empirical paths exercised:

| Failure mode | Behavior |
|---|---|
| `scopeFetch` rejects | Caught; ref returns `resolved: false` |
| `fileLookup` rejects | Caught; ref returns `resolved: false` |
| BFF returns 500 (mock) | Caught by adapter; next catalog tried; if all miss → unresolved |
| No adapters wired | All refs return `resolved: false`; `resolveAll` never rejects |
| Empty `tenantId` | Slow path (no caching); still resolves; no throw |

The `resolveAll` contract: returns `Promise<ResolvedReference[]>`. The
promise NEVER rejects. Per-reference failures surface as `resolved: false`
records — the consumer (ConversationPane) attaches the full array to the
chat message metadata so the agent prompt receives all tokens, resolved
and unresolved.

---

## ADR-013 facade audit: PASS

The resolver does NOT import any `Services/Ai/PublicContracts/` types
(they're server-side anyway). It consumes:

- Existing `GET /api/ai/scopes/*` endpoints via `authenticatedFetch`
- Existing chat session-files metadata via a host-provided adapter
- Existing `WorkspaceTabManager`-shaped `openTabs` via a host-provided snapshot

No new BFF endpoint, no new facade, no new DI registration. BFF publish-size
delta = 0 MB (ADR-029).

---

## Trade-off surfaced (per CLAUDE.md §"ADRs Are Defaults")

**Issue**: The POML "Discovery FIRST" step lists "polymorphic-resolver pattern
(Dataverse)" as the resolution path for `@<entity>` references that DON'T
match the host entityContext (e.g., `@opposing-counsel`, `@acme-corp` when
the host pane is a matter, not a contact).

**Constraint cited**: NFR-03 ("no new ADRs in R6") + the spec's MUST NOT
("MUST NOT add new feature flags per ADR-018" — not directly applicable —
and the binding rule under CLAUDE.md §10 "BFF Hygiene" which requires every
BFF addition to cite the decision criteria).

**Current implementation choice**: Out-of-context `@<entity>` references
degrade gracefully (`resolved: false`). The agent prompt receives the raw
token so the LLM can ask the user for clarification ("which `@opposing-counsel`
do you mean?"). This satisfies NFR-01 (non-blocking) and FR-51 ("resolved
entities appear in agent prompt" — unresolved tokens still appear, marked).

**Optimal alternative** (NOT implemented): Add a `POST /api/ai/dataverse/lookup`
endpoint that drives polymorphic resolution server-side. This would be a
new BFF endpoint — additive scope vs the spec's intent that R6 use existing
contracts only.

**Why current choice is acceptable**:
- The binding Phase D exit criterion 4 case (`@matter` → current matter)
  IS covered via the host entityContext short-circuit (test passes).
- The agent prompt receives the unresolved token; LLM clarification is the
  expected UX for out-of-context refs (NFR-01 binding).
- A future R7 task can add the BFF endpoint without breaking the resolver
  contract — `ResolverContext` accepts an optional adapter, just like
  `scopeFetch` and `fileLookup`. The change would be additive.

**Closure**: Surfacing here for visibility; no blocker. The resolver's
shape supports future expansion without API change.

---

## Integration handoff (FOR MAIN SESSION)

**File to modify**: `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx`

### 1. Imports to add

After the existing `import { parse as parseCommandIntent } from "./CommandRouter";`
block (around line 111):

```typescript
import ReferenceResolver, {
  createScopeFetch,
  createFileLookupFromSessionMap,
  type ResolvedReference,
  type OpenWorkspaceTab,
  type SessionFileMetadata,
} from "./ReferenceResolver";
```

### 2. Cache lifecycle (mount level)

Inside `ConversationPane()` after the `useAiSession()` destructuring:

```typescript
// R6 task 083 (Pillar 8 references) — wire /clear cache invalidation.
//
// The resolver maintains a module-scope cache keyed by tenantId:sessionId:rawToken
// (ADR-014). When the user issues /clear (task 081) the conversation channel
// dispatches a session-cleared event; we invalidate the resolver's per-session
// entries so subsequent references re-resolve against any new state.
//
// We DO NOT need a new PaneEventBus channel (NFR-05 + ADR-030); /clear lands
// on the existing `conversation` channel via task 081.
React.useEffect(() => {
  return () => {
    // On unmount or session change, drop the cache for the prior session.
    if (chatSessionId) ReferenceResolver.invalidateSession(chatSessionId);
  };
}, [chatSessionId]);
```

(Task 081 will additionally call `ReferenceResolver.invalidateSession(chatSessionId)`
inside its `/clear` executor — coordinate with that agent's handoff.)

### 3. Resolution inside `handleBeforeSendMessage`

The integration point lands in `handleBeforeSendMessage` (around line 1077).
The order matters — apply AFTER 080's `parseCommandIntent`, AFTER 081's
hard-slash short-circuit, AFTER 082's `decorateMessage` (if `isSoftSlash`).

Insert AFTER the soft-slash branch and BEFORE the message is sent:

```typescript
// R6 task 083 — resolve references BEFORE send so the agent prompt
// receives canonical entities (FR-51 binding). Non-blocking per NFR-01.
let resolvedReferences: ResolvedReference[] = [];
if (commandIntent.references.length > 0) {
  // Build a snapshot of open workspace tabs for the in-memory file check.
  // (Use whatever WorkspaceTab snapshot is already available in scope — the
  // R6 task 050 / 6a `WorkspaceState` flow exposes this via the workspace
  // PaneEventBus channel; if not yet wired in this code path, pass an
  // empty array and the resolver will fall through to `fileLookup`.)
  const openTabs: OpenWorkspaceTab[] = []; // wire from active workspace state when available

  // Build session-files lookup from whatever uploaded-file metadata the
  // host already tracks (R5 task 036 maintains an `uploadedFileCount` +
  // chip array; the documentId/filename mapping lives there).
  const sessionFiles: ReadonlyMap<string, SessionFileMetadata> = new Map();
  const fileLookup = createFileLookupFromSessionMap(sessionFiles);

  resolvedReferences = await ReferenceResolver.resolveAll(commandIntent.references, {
    tenantId, // from useAiSession() — already destructured at top of the component
    sessionId: chatSessionId,
    entityContext: entityContext
      ? {
          entityType: entityContext.entityType as string,
          entityId: entityContext.entityId,
          displayName: entityContext.displayName,
        }
      : null,
    openTabs,
    fileLookup,
    scopeFetch: createScopeFetch(bffBaseUrl, authenticatedFetch),
  });
}
```

### 4. Attach to message payload metadata

When the message is dispatched to SprkChat (the existing `onBeforeSendMessage`
is informational-only per R5 lessons-learned — we cannot transform the
outgoing message body from this hook). The integration target is the SprkChat
prop surface OR the session-state side channel.

**Recommended attach point** (additive, non-breaking):

```typescript
// Stash on the AiSessionProvider for the next /messages POST to pick up.
// The chat send path (SprkChat's internal /messages call) should be
// extended in a follow-up (post-D-G1) to read this and attach to the
// request body's `metadata.resolvedReferences` field.
streaming.setPendingResolvedReferences?.(resolvedReferences);
```

If `streaming.setPendingResolvedReferences` doesn't exist on the
`useAiSession()` surface yet (depends on prior tasks' state), the
fallback is to log + attach via the existing `onBeforeSendMessage`
information surface for now — task 084's composition integration test
will catch the contract gap and the main session can decide where the
agent-prompt bridge lands.

### 5. Order-of-operations summary in `handleBeforeSendMessage`

```
1. parseCommandIntent(rawText) → commandIntent          (task 080 — already wired)
2. if (isHardSlash) → HardSlashExecutor.execute(...)    (task 081)
3. if (isSoftSlash) → SoftSlashRouter.decorateMessage(...)  (task 082)
4. if (references.length > 0) → ReferenceResolver.resolveAll(...)  (THIS task)
5. Attach resolvedReferences to message metadata        (this task)
6. Send message via existing SprkChat path              (unchanged)
```

Steps 2 and 3 may short-circuit (hard slashes never reach the LLM); step 4
still runs because hard-slash composition (e.g., `/save-to-matter @matter`)
should resolve the entity ref so the executor knows WHICH matter to save to.

---

## ADRs Honored

| ADR | How |
|---|---|
| **ADR-013** | No new facade; consumes existing `/api/ai/scopes/*` only |
| **ADR-014** | `tenantId:sessionId:rawToken` cache keys; `tenantId` mandatory in cache surface |
| **ADR-015** | No user message content logged; resolver records only canonical IDs + names |
| **ADR-028** | `authenticatedFetch` contract — token never snapshotted; passed via context |
| **ADR-029** | Frontend-only; BFF publish-size delta = 0 MB |
| **ADR-030** | No new PaneEventBus channel; `/clear` invalidation rides existing `conversation` channel |

## NFRs Honored

| NFR | How |
|---|---|
| **NFR-01** | `resolveAll` promise never rejects; every ref returns a valid `ResolvedReference` (resolved or degraded) |
| **NFR-03** | No new ADRs; trade-off surfaced for visibility (see "Trade-off surfaced" above) |
| **NFR-05** | PaneEventBus stays at 4 channels (no new channel; no new event type required for this task) |

---

## Build status

- **TypeScript type-check**: PASS (no errors introduced by ReferenceResolver.ts;
  pre-existing `TS6133 unused-import` warnings in other shared-lib files are
  out of scope for this task)
- **Tests**: 27/27 pass
- **BFF publish-size delta**: 0 MB (frontend-only task)

---

## Tool count

~17 tool calls (within the 50-call stream-idle guard rail).

---

## Reminders for the main session at integration time

1. **Coordinate the order of D-G1 sub-agent integrations** in
   `handleBeforeSendMessage`: task 080 first (already done), then 081
   (hard-slash short-circuit), then 082 (soft-slash decoration), then 083
   (reference resolution). Steps 081 and 082 may short-circuit step 083
   for some commands (e.g., `/clear` doesn't need refs), but composition
   like `/save-to-matter @matter` benefits from step 4 running regardless.

2. **Cache invalidation on /clear**: task 081's `/clear` executor MUST call
   `ReferenceResolver.invalidateSession(chatSessionId)`. If the main session
   forgets this, the next user turn after `/clear` will see stale resolved
   refs from the prior session. Coordinated wire-up reminder.

3. **Open-tab + session-file snapshot wiring**: the integration sample above
   uses empty placeholders for `openTabs` and `sessionFiles`. Wire from the
   actual workspace-state surface (R6 task 050 / 6a) at integration time —
   otherwise file references will always miss the in-memory check and fall
   through to the (currently uncovered) `fileLookup` slow path.

4. **`tenantId`** is already available from `useAiSession()` per the existing
   destructuring at the top of `ConversationPane()` — verify it's added to
   the destructure list if not already there.

---

*End of evidence note.*
