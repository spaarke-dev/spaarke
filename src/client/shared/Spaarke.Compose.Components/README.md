# @spaarke/compose-components

TipTap-based ComposeWorkspace + ComposeEditor + ComposeToolbar widgets for the Spaarke Compose drafting workspace (spaarkeai-compose-r1). Mounts inside the LegalWorkspace section shim at `src/solutions/LegalWorkspace/src/sections/composeEditor.registration.ts` (Pattern D / Calendar precedent — same shape as `@spaarke/events-components`, `@spaarke/daily-briefing-components`, `@spaarke/smart-todo-components`).

## Status

**Phase 1b scaffold (Wave 1b follow-up, 2026-06-29)**: empty package skeleton only. Real widgets land in Phase 4 (tasks 042–046).

| Task | Deliverable |
|---|---|
| 042 | `ComposeWorkspace.tsx` (TipTap host + toolbar wrapper) |
| 043 | `ComposeToolbar.tsx` (Fluent v9 toolbar) |
| 044 | `ComposeEmptyState.tsx` (no-document picker) |
| 045 | `ComposeEditor.tsx` (TipTap StarterKit + extensions + mammoth/docx) |
| 046 | wire `ComposeWorkspace` into `composeEditor.registration.ts` (replaces inline `ComposeWorkspacePlaceholder` from Wave 1b-040) |

## Stack (locked at Spike #1, 2026-06-29)

- **React 19** (peer dependency)
- **TipTap 2.10.x core + StarterKit + 11 standard MIT extensions** ONLY (no TipTap Pro)
- **mammoth ^1.8.0** (BSD-2-Clause) — DOCX → HTML → TipTap import bridge
- **docx ^9.0.3** (MIT) — TipTap JSON → DOCX export bridge
- **@spaarke/auth** (dependency — provides `authenticatedFetch` for BFF calls)
- **@spaarke/document-operations** (dependency — Open-in-Word handoff per FR-12)
- TypeScript 5.3+ strict mode
- NOT PCF-safe (React 19)

## Licensing

All packages MIT or BSD-2-Clause. **Zero TipTap Pro. Zero proprietary deps.** Track Changes, Comments, Mathematics, Drawing (TipTap Pro features) are explicitly EXCLUDED — Open-in-Word handles them per FR-12. See `projects/spaarkeai-compose-r1/notes/spikes/spike-1-tiptap-docx-roundtrip.md` §3.1.

## Consumers

| Consumer | Path | Role |
|---|---|---|
| LegalWorkspace section shim | `src/solutions/LegalWorkspace/src/sections/composeEditor.registration.ts` | Imports `ComposeWorkspace` (when Phase 4 lands) + registers it in SECTION_REGISTRY. Today imports an inline `ComposeWorkspacePlaceholder` (W1b-040 stub). |
| SpaarkeAi (via LegalWorkspace section registry) | `src/solutions/SpaarkeAi/src/main.tsx` | Consumes the LegalWorkspace registry via `createLegalWorkspaceSectionRegistry({...})` — Pattern D dual-use. |

## Build

Built as part of `scripts/Build-AllClientComponents.ps1` ahead of downstream Vite solutions. Standalone:

```pwsh
npm install --legacy-peer-deps --no-audit --no-fund
npm run build
```
