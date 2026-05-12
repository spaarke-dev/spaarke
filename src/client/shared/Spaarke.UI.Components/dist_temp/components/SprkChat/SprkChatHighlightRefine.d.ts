/**
 * SprkChatHighlightRefine - Text selection and refinement UI
 *
 * Detects text selection within a container element (local DOM) and/or
 * receives cross-pane selections from SprkChatBridge (via the
 * crossPaneSelection prop). Shows a floating "Refine" toolbar allowing the
 * user to submit a refinement instruction or click quick action presets.
 *
 * Cross-pane selections take priority over local DOM selections: when a
 * crossPaneSelection is present, the toolbar shows that text regardless of
 * any local highlight.
 *
 * Quick action presets (Simplify, Expand, Make Concise, Make Formal) are
 * shown as chips that auto-submit when clicked. Custom presets can be
 * provided via the quickActions prop.
 *
 * Source detection: editor selections show a document icon + "Selected in Editor"
 * badge; chat selections show a chat icon + "Selected in Chat" badge.
 *
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only (useState, useEffect, useRef, useCallback)
 * @see useSelectionListener (receives bridge events -> crossPaneSelection)
 */
import * as React from 'react';
import { ISprkChatHighlightRefineProps } from './types';
/**
 * SprkChatHighlightRefine - Floating toolbar for text selection refinement.
 *
 * Supports two selection sources:
 * 1. **Local DOM selection** -- text highlighted in the chat message list
 * 2. **Cross-pane selection** -- text selected in the Analysis Workspace editor,
 *    received via SprkChatBridge and passed as the crossPaneSelection prop
 *
 * Cross-pane selections display a sticky toolbar at the top of the container
 * with a document icon and "Selected in Editor" badge. Local selections show a
 * floating toolbar anchored near the selection range with a chat icon and
 * "Selected in Chat" badge.
 *
 * Quick action chips (Simplify, Expand, Make Concise, Make Formal) appear below
 * the input. Clicking a chip auto-submits the refinement with that instruction.
 *
 * @example
 * ```tsx
 * <SprkChatHighlightRefine
 *   contentRef={messageListRef}
 *   onRefine={(text, instruction) => handleRefine(text, instruction)}
 *   onRefineRequest={(req) => handleRefineRequest(req)}
 *   isRefining={false}
 *   crossPaneSelection={crossPaneSelection}
 *   quickActions={[{ key: "simplify", label: "Simplify", instruction: "Simplify this text" }]}
 * />
 * ```
 */
export declare const SprkChatHighlightRefine: React.FC<ISprkChatHighlightRefineProps>;
export default SprkChatHighlightRefine;
//# sourceMappingURL=SprkChatHighlightRefine.d.ts.map