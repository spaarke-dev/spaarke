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

export interface SprkButtonProps {
    /** Tooltip text (optional - shows on hover) */
    tooltip?: string;

    /** Button appearance */
    appearance?: 'primary' | 'secondary' | 'subtle';

    /** Icon element */
    icon?: React.ReactElement;

    /** Disabled state */
    disabled?: boolean;

    /** Click handler */
    onClick?: () => void;

    /** Button content */
    children?: React.ReactNode;
}

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
export const SprkButton: React.FC<SprkButtonProps> = ({
    tooltip,
    appearance,
    icon,
    disabled,
    onClick,
    children
}) => {
    const button = (
        <Button
            appearance={appearance}
            icon={icon}
            disabled={disabled}
            onClick={onClick}
        >
            {children}
        </Button>
    );

    // Wrap with tooltip if provided
    if (tooltip) {
        return (
            <Tooltip content={tooltip} relationship="label">
                {button}
            </Tooltip>
        );
    }

    return button;
};
