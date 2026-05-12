/**
 * CardIcon — branded circle wrapper for card icons.
 *
 * Renders a Fluent icon inside a colored circle, used as the left
 * column of RecordCardShell. Supports custom colors for entity-specific
 * theming (e.g., file-type icons for documents).
 *
 * @see ADR-021 - Fluent UI v9 design system
 */
import * as React from 'react';
import { makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    circle: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: tokens.borderRadiusCircular,
        flexShrink: 0,
    },
});
// ---------------------------------------------------------------------------
// CardIcon
// ---------------------------------------------------------------------------
export const CardIcon = ({ children, size = 40, backgroundColor, iconColor, className, }) => {
    const styles = useStyles();
    return (React.createElement("div", { className: mergeClasses(styles.circle, className), style: {
            width: size,
            height: size,
            backgroundColor: backgroundColor ?? tokens.colorBrandBackground2,
            color: iconColor ?? tokens.colorBrandForeground1,
            fontSize: Math.round(size * 0.5),
        }, "aria-hidden": "true" }, children));
};
export default CardIcon;
//# sourceMappingURL=CardIcon.js.map