# @spaarke/document-operations

Cross-surface document-operations hooks for Spaarke code pages and widgets. Hosts the canonical implementation of document actions (Open-in-Word web/desktop, download, delete, email-link, send-to-index) that previously lived inside the SemanticSearch code page and is now shared by SemanticSearch + Compose (FR-13 of `spaarkeai-compose-r1`).

## Status

**Phase 3 / Task 030**: Package skeleton only. The `useDocumentActions` hook is moved into this package by task 031; SemanticSearch is refactored to consume from here by task 032; Compose adopts the hook in task 033. Single-source-of-truth for document operations is the contract.

## Consumers

| Consumer | Path | Role |
|---|---|---|
| SemanticSearch | `src/client/code-pages/SemanticSearch/src/components/SearchCommandBar.tsx` | Original owner; refactored to consume from this package in task 032. |
| Compose | `src/solutions/Compose/` (or equivalent SpaarkeAi surface) | New consumer; wires up Open-in-Word for promoted documents (task 033). |

## Stack

- React 19 (peer dependency)
- `@spaarke/auth` (dependency — provides `authenticatedFetch` + `buildBffApiUrl`)
- TypeScript 5.3+ strict mode
- NOT PCF-safe (React 19)

## Build

Built as part of `scripts/Build-AllClientComponents.ps1` ahead of downstream Vite solutions and webpack code pages. Standalone:

```pwsh
npm install --legacy-peer-deps --no-audit --no-fund
npm run build
```
