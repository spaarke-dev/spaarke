# Task 084 — Evidence Note

> **Task**: D-D-05 Composition integration tests
> **Phase**: D — Command Router + Integration + Closeout (Wave D-G2)
> **Rigor**: STANDARD (test-only; no new code surface)
> **Status**: ✅ Completed 2026-06-18
> **Branch**: `work/spaarke-ai-platform-unification-r6`
> **Spec**: FR-52 (composition) + NFR-11 (backward compat)

---

## Scope

End-to-end integration test for Pillar 8 composition. Exercises the full
`parse → resolve → decorate → send` chain across the 4 Pillar 8 modules
(080 CommandRouter, 082 SoftSlashRouter, 083 ReferenceResolver — 081 hard
slashes do NOT compose with references and are out of scope per POML).

Tests-only; no production code changes.

---

## Files

| Path | Action |
|------|--------|
| `src/solutions/SpaarkeAi/src/components/conversation/__tests__/composition.integration.test.ts` | NEW — 12 tests across 3 describe blocks |
| `projects/spaarke-ai-platform-unification-r6/tasks/TASK-INDEX.md` | 084 🔲 → ✅ |
| `projects/spaarke-ai-platform-unification-r6/current-task.md` | 084 marked closed (D-G2 wave summary) |

---

## Coverage

| Scenario | Tests | Pass |
|---|---|---|
| `/summarize #engagement-letter.docx` (FR-52 example 1) | 5 | ✅ |
| `/draft response to @opposing-counsel about #motion-to-dismiss` (FR-52 example 2) | 4 | ✅ |
| NFR-11 NL backward compat | 3 | ✅ |
| **Total** | **12** | **12 ✅ / 0 failed** |

### Per-test names

```
PASS Pillar 8 composition integration (FR-52) > /summarize #engagement-letter.docx > parses to soft slash + single filename reference
PASS Pillar 8 composition integration (FR-52) > /summarize #engagement-letter.docx > resolves the file reference via the file adapter
PASS Pillar 8 composition integration (FR-52) > /summarize #engagement-letter.docx > decorates the outbound body with commandIntent + resolvedReferences
PASS Pillar 8 composition integration (FR-52) > /summarize #engagement-letter.docx > produces a coherent BFF payload
PASS Pillar 8 composition integration (FR-52) > /summarize #engagement-letter.docx > gracefully degrades when the file adapter cannot resolve
PASS Pillar 8 composition integration (FR-52) > /draft response to @opposing-counsel about #motion-to-dismiss > parses to soft slash + entity ref + filename ref
PASS Pillar 8 composition integration (FR-52) > /draft response to @opposing-counsel about #motion-to-dismiss > resolves both references in a single resolveAll call
PASS Pillar 8 composition integration (FR-52) > /draft response to @opposing-counsel about #motion-to-dismiss > preserves interleaved text in the message field
PASS Pillar 8 composition integration (FR-52) > /draft response to @opposing-counsel about #motion-to-dismiss > produces a coherent BFF payload with both refs surfaced
PASS Pillar 8 composition integration (FR-52) > NFR-11 backward compat > "summarize the engagement letter" → command:null (passthrough)
PASS Pillar 8 composition integration (FR-52) > NFR-11 backward compat > natural-language input produces undecorated body
PASS Pillar 8 composition integration (FR-52) > NFR-11 backward compat > "draft response to opposing counsel about the motion" → no decoration
```

---

## Composition contract verified

The integration test locks in the cross-module composition contract:

1. `CommandRouter.parse(text)` emits the structured `Intent` shape with
   `command`, `references[]`, `rawText`, `isHardSlash`, `isSoftSlash`.
2. `ReferenceResolver.resolveAll(intent.references, ctx)` returns one
   `ResolvedReference` per input — fully populated on success, NFR-01
   degraded shape (`resolved: false`, `displayName: rawToken`) on miss.
3. `SoftSlashRouter.decorateBody(intent, body)` adds `commandIntent` IFF
   `intent.isSoftSlash === true`; otherwise returns a shallow copy with
   no decoration.
4. The host composes the final BFF body by appending `resolvedReferences`
   to the SoftSlashRouter-decorated body. The two decorations target
   distinct fields and never collide.

NFR-11 binding: natural-language inputs (no slash, no sigils) produce
`command: null` + `references: []`. The final body carries neither
`commandIntent` nor `resolvedReferences` — the existing CapabilityRouter
Layer 1 keyword path runs UNCHANGED.

NFR-01 binding: an unresolved reference (file not in the session-files
index) still produces a coherent body — the agent prompt receives the
unresolved-flag entry for clarification. The conversation NEVER blocks.

---

## Quality gates

| Gate | Result |
|------|--------|
| `npx jest src/components/conversation/__tests__/composition.integration.test.ts` | 12 passed / 0 failed (0.67 s) |
| BFF publish-size delta | 0 MB (frontend-only, test-only) |
| ADR-029 | ✅ no `.cs` modified |
| NFR-11 regression locked | ✅ 3 NL backward-compat tests |
| NFR-01 non-blocking | ✅ unresolved-file degradation test |
| ADR-014 cache key shape (tenantId-scoped) | ✅ exercised via `__resetCacheForTests` + explicit `tenantId` in context |

---

## Downstream unblocked

- 087 (vertical-slice integration test, all 9 pillars per spec §6) — `dependencies: 084, 085, 086, 029, 049, 079` now has all D-G2 deps met.

---

## Notes for downstream consumers

The integration test does NOT spin up SprkChat or render the full
ConversationPane — it orchestrates the chain at the pure-function layer
(parse / resolve / decorate). This keeps the test fast (<1 s) and isolates
the composition contract from UI surface concerns. A future
"ConversationPane composition" test (out of scope here; could land in 087)
would verify the same chain runs INSIDE `handleBeforeSendMessage`.

The composition helper `runComposition(text, ctx)` in the test file is a
worked example of how `ConversationPane.handleBeforeSendMessage` should
assemble the final body once 085 + the SprkChat seam wire it through.
