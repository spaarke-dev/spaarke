# @spaarke/compose-components

TipTap-based ComposeWorkspace + ComposeEditor + ComposeToolbar widgets for the Spaarke Compose drafting workspace (spaarkeai-compose-r1). Mounts inside the LegalWorkspace section shim at `src/solutions/LegalWorkspace/src/sections/composeEditor.registration.ts` (Pattern D / Calendar precedent — same shape as `@spaarke/events-components`, `@spaarke/daily-briefing-components`, `@spaarke/smart-todo-components`).

## Governance (ADR-012, 2026-11-24 amendment)

Sanctioned shared package: the single home of the Compose document-editing model (TipTap document, DOCX bridge, checkout / heartbeat, comments). Binding rules, enforced by `tests/Spaarke.ArchTests/SharedPackageCensusTests.cs`:

- **TipTap quarantine** — only this package's non-test source imports `@tiptap/*` / `prosemirror-*`; no other shared package and no PCF declares them.
- **Entity map** — every Dataverse name lives in `src/types/composeDocumentSchema.ts` (`COMPOSE_DOCUMENT_SCHEMA`); no other source file names a `sprk_` entity or column in code.
- **Editor split** — document editing goes here (`ComposeEditor`); a rich-text field uses `RichTextEditor` (Lexical) in `@spaarke/ui-components`.
- Never delete `src/utils/docxBridge.ts`.

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
- DOCX import and export run on the BFF (`ComposeDocxProjectionBuilder`, the server `.docx` renderer); `src/utils/docxBridge.ts` builds the content model the renderer consumes. The client `mammoth` import path was removed in Compose R4.5 and the dependency in 2026-11 (ADR-012 P-14); the package has no `docx` dependency.
- **@spaarke/auth** (dependency — provides `authenticatedFetch` for BFF calls)
- **@spaarke/ui-components** (dependency — `useDocumentActions` for the Open-in-Word handoff per FR-12, plus shared dialogs)
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
