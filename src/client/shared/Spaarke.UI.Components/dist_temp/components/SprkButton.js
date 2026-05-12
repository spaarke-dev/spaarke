/**
 * Spaarke Button - Fluent UI v9 wrapper with Spaarke standards
 *
 * Extends Fluent UI Button with:
 * - Built-in tooltip support
 * - Type-safe icon integration
 * - Consistent styling
 */
import * as React from 'react';
import { Button } from '@fluentui/react-button';
import { Tooltip } from '@fluentui/react-tooltip';
/**
 * Standard button component with optional tooltip.
 *
 * @example
 * ```tsx
 * import { SprkButton, SprkIcons } from '@spaarke/ui-components';
 *
 * <SprkButton
 *   appearance="primary"
 *   icon={<SprkIcons.Add />}
 *   tooltip="Add a new item"
 *   onClick={handleAdd}
 * >
 *   Add Item
 * </SprkButton>
 * ```
 */
export const SprkButton = ({ tooltip, appearance, icon, disabled, onClick, children }) => {
    const button = (React.createElement(Button, { appearance: appearance, icon: icon, disabled: disabled, onClick: onClick }, children));
    // Wrap with tooltip if provided
    if (tooltip) {
        return (React.createElement(Tooltip, { content: tooltip, relationship: "label" }, button));
    }
    return button;
};
//# sourceMappingURL=SprkButton.js.map