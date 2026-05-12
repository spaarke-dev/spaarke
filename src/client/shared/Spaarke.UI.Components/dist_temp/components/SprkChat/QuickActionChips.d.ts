/**
 * QuickActionChips - Row of chip buttons above the SprkChat input bar.
 *
 * Displays up to 4 quick-action chips derived from the context mapping
 * response's inline actions. Chips are hidden when the pane is narrower
 * than 350px (NFR-04) via ResizeObserver on the provided containerRef.
 *
 * Each chip is a Fluent v9 Button (appearance="outline", size="small")
 * with an optional icon and label. Clicking a chip fires onChipClick
 * with the corresponding InlineAiAction.
 *
 * @see ADR-012 - Shared Component Library (no Xrm/ComponentFramework imports)
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode required
 */
import * as React from 'react';
import type { InlineAiAction } from '../InlineAiToolbar/inlineAiToolbar.types';
/** Props for the QuickActionChips component. */
export interface IQuickActionChipsProps {
    /**
     * Ordered list of inline actions to render as chip buttons.
     * Up to MAX_CHIPS (4) are displayed; excess entries are ignored.
     */
    actions: InlineAiAction[];
    /**
     * Callback fired when the user clicks a chip.
     * Receives the full InlineAiAction for the clicked chip.
     */
    onChipClick: (action: InlineAiAction) => void;
    /**
     * Ref to the container element whose width is observed.
     * When the container is narrower than 350px, chips hide automatically
     * to preserve space for the chat input. The ref element must be
     * in the DOM when this component mounts.
     */
    containerRef: React.RefObject<HTMLElement>;
    /** Whether chips are interactable. Pass true while a stream is in progress. */
    disabled?: boolean;
    /** Optional CSS class applied to the chip row wrapper. */
    className?: string;
}
/**
 * QuickActionChips renders a horizontal row of chip buttons above the
 * SprkChat input bar, populated from the context mapping response's
 * inline actions. Hidden when the container pane is narrower than 350px.
 *
 * @example
 * ```tsx
 * const containerRef = React.useRef<HTMLDivElement>(null);
 *
 * <div ref={containerRef} style={{ height: '100%' }}>
 *   <QuickActionChips
 *     actions={contextActions.slice(0, 4)}
 *     onChipClick={(action) => handleChipAction(action)}
 *     containerRef={containerRef}
 *     disabled={isStreaming}
 *   />
 *   <SprkChatInput onSend={handleSend} />
 * </div>
 * ```
 */
export declare const QuickActionChips: React.FC<IQuickActionChipsProps>;
export default QuickActionChips;
//# sourceMappingURL=QuickActionChips.d.ts.map