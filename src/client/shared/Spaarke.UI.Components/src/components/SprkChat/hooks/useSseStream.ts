/**
 * useSseStream — Re-export from canonical shared location
 *
 * The canonical implementation lives at:
 *   src/client/shared/Spaarke.UI.Components/src/hooks/useSseStream.ts
 *
 * This file exists only to preserve the import path used by:
 *   - SprkChat.tsx (imports from './hooks/useSseStream')
 *   - SprkChat __tests__ (import from '../hooks/useSseStream')
 *
 * Do NOT add implementation here. All changes go to the canonical file.
 *
 * @see AIPU2-082 — Consolidate duplicate useSseStream implementations
 */

export { useSseStream, parseSseEvent, parsePaneEvent } from '../../../hooks/useSseStream';
