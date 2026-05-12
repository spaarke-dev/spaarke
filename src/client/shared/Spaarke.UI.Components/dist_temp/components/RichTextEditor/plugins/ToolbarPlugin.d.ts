/**
 * ToolbarPlugin - Rich text formatting toolbar for Lexical editor
 *
 * Provides formatting buttons using Fluent UI v9 components:
 * - Text formatting: Bold, Italic, Underline, Strikethrough
 * - Block formatting: Headings (H1, H2, H3), Quote
 * - Lists: Ordered, Unordered
 * - History: Undo, Redo
 *
 * Standards: ADR-012 (shared component library)
 */
import * as React from 'react';
interface ToolbarPluginProps {
    isDarkMode?: boolean;
}
export declare function ToolbarPlugin({ isDarkMode }: ToolbarPluginProps): React.ReactElement;
export default ToolbarPlugin;
//# sourceMappingURL=ToolbarPlugin.d.ts.map