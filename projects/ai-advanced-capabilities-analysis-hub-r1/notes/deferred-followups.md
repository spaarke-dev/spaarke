# Deferred Follow-ups (surface at wrap-up / push audit)

Documented deferrals discovered during execution — NOT silent drops. Wrap-up (task 090) reconciles these.

| ID | From task | Item | Reason deferred | Blocking? |
|---|---|---|---|---|
| DF-01 | 023 | History-list **visual** loose/owned tier badge | Blocked on a pre-existing unrelated gap: `StoredSession.EntityRefs` is never written server-side, so the client can't distinguish tiers visually. Behavioral two-tier model (FR-07 core: loose=no FK, owned=FK, promotion) IS complete. | No — cosmetic only; consider filing a GitHub issue at wrap-up for the `EntityRefs` write gap |
