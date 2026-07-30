# Task 001 — Shared hardened `sanitizeEmailHtml` (FR-16 / NFR-03)

**Status**: implemented · build green · 16/16 unit tests pass
**Files**:
- `src/client/shared/Spaarke.UI.Components/src/utils/sanitizeEmailHtml.ts` (new)
- `src/client/shared/Spaarke.UI.Components/src/utils/__tests__/sanitizeEmailHtml.test.ts` (new)
- `src/client/shared/Spaarke.UI.Components/src/utils/index.ts` (barrel export)
- `.../components/CommunicationTimeline/subcomponents/MessageRow.tsx` (retrofit)
- `.../components/ConversationView/subcomponents/MessageBubble.tsx` (retrofit)

## What changed

Replaced the permissive `DOMPurify.sanitize(body, { USE_PROFILES: { html: true } })`
inlined in `MessageRow.tsx` and `MessageBubble.tsx` with ONE shared, allow-list
sanitizer. The `USE_PROFILES:{html:true}` profile allowed a broad tag/attr set and
did NOT restrict URL schemes or harden anchors — a `javascript:` href or data-exfil
link rendered unmodified. Closed via explicit closed allow-list + post-sanitize hook.

## Allow-list decisions

| Decision | Choice | Rationale |
|---|---|---|
| **URL schemes** | `http` / `https` / `mailto` only (`ALLOWED_URI_REGEXP`) | NFR-03. `javascript:`/`vbscript:`/`file:`/`tel:`/scheme-relative dropped. |
| **`data:` URIs** | **NOT allowed** (incl. `data:` image `src`) | Secure default. Field-body path renders Graph's stripped `uniqueBody`, which does not carry inline data-URI images; `data:text/html` is a script vector. Inline `.eml` images are the server `.eml` render path (task 010) + sandboxed iframe, not here. Revisit only if this util is reused for the `.eml` client display branch — owner decision per `<escalation>`. |
| **`style` attribute** | **Allowed**, with dangerous-CSS scrub | Legitimate email layout relies on inline styles; stripping them (as `renderMarkdown` does) would visibly degrade email rendering. ADR-021: the util touches only body HTML, never host component chrome, so dark-mode tokens on the surrounding Fluent components are untouched. |
| **`on*` handlers** | Removed | Not on `ALLOWED_ATTR` closed list (+ DOMPurify strips them natively). |
| **Anchors** | Forced `rel="noopener noreferrer" target="_blank"` | Overwrites any attacker-supplied `target`/`rel`. |
| **`srcset`** | Dropped from allow-list | Comma-separated URL list not scheme-checked by DOMPurify; inline email images use `src`. |
| **Forbidden tags** | `script`/`iframe`/`object`/`embed`/`form`/`style`/`link`/`meta`/`base` | Closed `ALLOWED_TAGS` already excludes; `FORBID_TAGS` restates for auditable intent. |

## Two hardening gaps the tests caught (and closed)

DOMPurify config alone was insufficient; a post-sanitize `afterSanitizeAttributes`
hook (`hardenNode`) closes two gaps:

1. **`data:` on `<img>`** — DOMPurify's built-in `DATA_URI_TAGS` path allows `data:`
   URIs on `img`/media tags *regardless of* a custom `ALLOWED_URI_REGEXP`. The hook
   re-verifies the scheme on `href`/`src`/`xlink:href` and removes any non-allowed
   scheme, closing this.
2. **Dangerous CSS in `style`** — DOMPurify (in jsdom) does not scrub
   `url(javascript:…)`/`expression()`/`@import`/`behavior:`/`-moz-binding` inside an
   allowed `style` value. The hook drops the whole `style` attribute when those
   tokens appear (legitimate `margin`/`color`/`background-color` styles unaffected).

## Hook isolation

DOMPurify hooks are process-global. `hardenNode` is registered with `addHook`
immediately before the (synchronous) `sanitize` and removed with `removeHook` in a
`finally`, so it never leaks onto other DOMPurify consumers (`renderMarkdown`,
`quoteBody`). Verified by a dedicated test + by re-running those suites green.

## ADR posture
- **ADR-022** — pure TS, no React import, no React-runtime API, no `as React.ComponentType` cast. Layer-1-safe.
- **ADR-021** — sanitizer operates on body HTML only; surrounding Fluent chrome + dark-mode tokens untouched.
- **ADR-012** — extends `@spaarke/ui-components`; no new sanitizer dependency (reuses `dompurify ^3.4`).

## Scope guardrails honored
- Only the two named call sites retrofitted; `dangerouslySetInnerHTML` structure unchanged (only the sanitize source swapped).
- No edits to `@spaarke/communication-components` / BFF (sibling agents own those).
- TASK-INDEX.md / current-task.md left for the orchestrator.
