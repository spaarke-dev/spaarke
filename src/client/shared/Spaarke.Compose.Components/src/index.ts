// @spaarke/compose-components — barrel export
//
// TipTap-based ComposeWorkspace + ComposeEditor + ComposeToolbar widgets
// for the spaarkeai-compose-r1 drafting workspace. Mounted by the
// LegalWorkspace section shim at
// `src/solutions/LegalWorkspace/src/sections/composeEditor.registration.ts`
// (task 046 swaps the W1b inline placeholder for `ComposeWorkspace`).
//
// PHASE 4 (task 045 W4 — this commit): `ComposeEditor` exported. The other
// Phase 4 widgets land in their respective W4/W5 tasks:
//   - 042: ComposeWorkspace.tsx (TipTap host + toolbar wrapper) — W5
//   - 043: ComposeToolbar.tsx (Fluent v9 toolbar) — W4 sibling
//   - 044: ComposeEmptyState.tsx (no-document picker) — W4 sibling
//   - 045: ComposeEditor.tsx (TipTap StarterKit + 11 MIT extensions + mammoth/docx) — THIS COMMIT
//   - 046: wire ComposeWorkspace into composeEditor.registration.ts — W6
//
// Licensing constraint (LOCKED at Spike #1, 2026-06-29): TipTap core +
// StarterKit + 11 standard MIT extensions ONLY. No TipTap Pro packages.
// DOCX bridge: mammoth ^1.8.0 (BSD-2-Clause) + docx ^9.0.3 (MIT). All OSS.

export { ComposeEditor } from './widgets/ComposeEditor';
export type { ComposeEditorProps, ComposeEditorHandle, ComposeEditorDocumentRef } from './widgets/ComposeEditor';
export { ComposeFormatToolbar } from './widgets/ComposeFormatToolbar';
export type { ComposeFormatToolbarProps } from './widgets/ComposeFormatToolbar';

// DOCX bridge helpers — exported for advanced consumers + R2 tests. Most
// consumers should use ComposeEditor (which orchestrates these internally).
export { docxToTipTapHtml, tipTapToDocxBytes } from './utils/docxBridge';
export type { MammothConversionResult } from './utils/docxBridge';
