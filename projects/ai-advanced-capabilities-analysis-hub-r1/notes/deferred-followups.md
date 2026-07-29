# Deferred Follow-ups (surface at wrap-up / push audit)

Documented deferrals discovered during execution — NOT silent drops. Wrap-up (task 090) reconciles these.

| ID | From task | Item | Reason deferred | Blocking? |
|---|---|---|---|---|
| DF-01 | 023 | History-list **visual** loose/owned tier badge | Blocked on a pre-existing unrelated gap: `StoredSession.EntityRefs` is never written server-side, so the client can't distinguish tiers visually. Behavioral two-tier model (FR-07 core: loose=no FK, owned=FK, promotion) IS complete. | No — cosmetic only; consider filing a GitHub issue at wrap-up for the `EntityRefs` write gap |
| DF-02 | 025 | `sprk_ai2_workspaceTabs__*` localStorage key namespace has no eviction/cap | code-review Warning (non-blocking); not a defect vs acceptance criteria. Tab-anchor persistence works; unbounded key growth is a theoretical long-tail. | No — perf hygiene; add an eviction cap in a later pass |
| DF-03 | 025 | `AnalysisEditorWidget` per-keystroke `onDataChange` triggers an undebounced `WorkspacePane.syncState()` re-render | code-review Warning (non-blocking); the actual persistence write-through is already debounced 200ms, so only a re-render (not a write) is per-keystroke. | No — debounce the re-render in a later pass |
