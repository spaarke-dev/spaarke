/**
 * InlineAiToolbar - Floating absolutely-positioned AI action toolbar.
 *
 * Renders as an absolutely-positioned overlay above user text selections in
 * the Analysis Workspace editor. The toolbar stays in the DOM when hidden
 * (`visible=false`) using `display: 'none'` to avoid layout thrash from
 * repeated mount/unmount cycles.
 *
 * Positioning is driven by `props.position` ({top, left} in pixels relative
 * to the nearest positioned ancestor), computed by the `useInlineAiToolbar`
 * hook (task 012).
 *
 * Renders `InlineAiActions` as its content, passing through the `actions`
 * and `onAction` props.
 *
 * @see InlineAiActions - renders the action button row
 * @see inlineAiToolbar.types.ts - shared type definitions
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-012 - Shared Component Library (no Xrm/ComponentFramework imports)
 */
import * as React from 'react';
import { InlineAiToolbarProps } from './inlineAiToolbar.types';
/**
 * InlineAiToolbar is a floating absolutely-positioned container that renders
 * an AI action bar near a text selection. It stays mounted in the DOM when
 * hidden (controlled via the `visible` prop) to prevent flicker and layout
 * recalculation.
 *
 * @example
 * ```tsx
 * <div style={{ position: 'relative' }}>
 *   <LexicalEditor ... />
 *   <InlineAiToolbar
 *     visible={toolbarVisible}
 *     position={{ top: selectionBottom, left: selectionLeft }}
 *     actions={DEFAULT_INLINE_ACTIONS}
 *     onAction={(action, selectedText) => dispatchInlineAction(action, selectedText)}
 *   />
 * </div>
 * ```
 */
export declare const InlineAiToolbar: React.FC<InlineAiToolbarProps>;
//# sourceMappingURL=InlineAiToolbar.d.ts.map