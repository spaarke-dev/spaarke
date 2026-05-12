/**
 * SidePaneShell - Reusable layout shell for Dataverse side pane web resources
 *
 * Provides the standard layout for all side panes across Spaarke:
 * - Fixed header at top
 * - Scrollable content area (entity-specific sections)
 * - Sticky footer at bottom (save button, messages)
 * - Optional read-only banner
 * - Dark mode support via Fluent UI tokens
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9, dark mode support
 * @see Task 108 - Extract shared side pane components
 *
 * @example
 * ```tsx
 * <SidePaneShell
 *   header={<HeaderSection eventId={id} onClose={close} />}
 *   footer={<Footer isDirty={isDirty} onSave={save} />}
 *   isReadOnly={!canWrite}
 *   readOnlyMessage="You do not have permission to edit this record"
 * >
 *   <StatusSection value={status} onChange={setStatus} />
 *   <KeyFieldsSection dueDate={dueDate} priority={priority} />
 *   {config.isSectionVisible("dates") && <DatesSection ... />}
 * </SidePaneShell>
 * ```
 */
import * as React from 'react';
export interface SidePaneShellProps {
    /** Fixed header content (e.g., record name, close button) */
    header: React.ReactNode;
    /** Sticky footer content (e.g., save button, messages) */
    footer: React.ReactNode;
    /** Scrollable main content (entity-specific sections) */
    children: React.ReactNode;
    /** Whether the record is in read-only mode */
    isReadOnly?: boolean;
    /** Message shown in read-only banner */
    readOnlyMessage?: string;
    /** Ref forwarded to the scrollable content area (for scroll position persistence) */
    contentRef?: React.RefObject<HTMLElement>;
}
export declare const SidePaneShell: React.FC<SidePaneShellProps>;
export default SidePaneShell;
//# sourceMappingURL=SidePaneShell.d.ts.map