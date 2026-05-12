/**
 * WorkspaceShell.styles.ts — Shared Griffel styles for the WorkspaceShell layout.
 *
 * Extracted to a separate file so WorkspaceShell.tsx stays focused on
 * rendering logic. Consumers can import individual style hooks if needed.
 *
 * Standards: ADR-021 (Fluent v9 tokens, no hard-coded colors, dark mode)
 */
/**
 * Styles for the WorkspaceShell outer container and row layout.
 */
export declare const useWorkspaceShellStyles: () => Record<"row" | "shell", string>;
/**
 * Styles for content padding inside a SectionPanel.
 * Used when card rows or other content need standard interior spacing.
 */
export declare const useSectionContentPaddingStyles: () => Record<"padded", string>;
/**
 * Toolbar divider — a thin vertical line separator between toolbar button groups.
 */
export declare const useToolbarDividerStyles: () => Record<"divider", string>;
//# sourceMappingURL=WorkspaceShell.styles.d.ts.map