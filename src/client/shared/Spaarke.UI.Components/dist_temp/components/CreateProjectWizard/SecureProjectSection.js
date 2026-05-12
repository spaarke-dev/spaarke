/**
 * SecureProjectSection.tsx
 * Secure Project toggle section for the Create Project wizard.
 *
 * Displays a Fluent v9 Switch allowing users to designate the project as
 * "Secure". When toggled on, an expanded information panel explains:
 *   - What a Secure Project is
 *   - What additional infrastructure will be provisioned
 *   - That the designation is IRREVERSIBLE after creation
 *
 * This component is rendered as a section within CreateProjectStep rather
 * than as a standalone wizard step, so that toggle state persists naturally
 * through Back/Next navigation (it lives in the parent's form state).
 *
 * Constraints:
 *   - Fluent v9 only: Switch, Text, Divider, MessageBar, makeStyles
 *   - makeStyles with semantic tokens — ZERO hard-coded colours
 *   - Supports light, dark, and high-contrast modes (ADR-021)
 */
import * as React from 'react';
import { Divider, MessageBar, MessageBarBody, Switch, Text, makeStyles, tokens, } from '@fluentui/react-components';
import { LockClosedRegular, BuildingRegular, StorageRegular, PeopleTeamRegular, WarningRegular, } from '@fluentui/react-icons';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    root: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM,
    },
    // ── Divider row ───────────────────────────────────────────────────────────
    dividerRow: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
    },
    // ── Toggle row ────────────────────────────────────────────────────────────
    toggleRow: {
        display: 'flex',
        alignItems: 'flex-start',
        gap: tokens.spacingHorizontalM,
    },
    toggleIcon: {
        marginTop: '2px',
        color: tokens.colorNeutralForeground3,
        flexShrink: 0,
    },
    toggleIconSecure: {
        marginTop: '2px',
        color: tokens.colorBrandForeground1,
        flexShrink: 0,
    },
    toggleText: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXS,
        flex: 1,
    },
    toggleLabel: {
        color: tokens.colorNeutralForeground1,
    },
    toggleDescription: {
        color: tokens.colorNeutralForeground3,
    },
    // ── Expanded info panel ───────────────────────────────────────────────────
    infoPanel: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM,
        padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalM}`,
        backgroundColor: tokens.colorNeutralBackground2,
        borderRadius: tokens.borderRadiusMedium,
        borderLeft: `3px solid ${tokens.colorBrandBackground}`,
    },
    infoPanelTitle: {
        color: tokens.colorNeutralForeground1,
    },
    // ── Provisioning list ─────────────────────────────────────────────────────
    provisioningList: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalS,
    },
    provisioningItem: {
        display: 'flex',
        alignItems: 'flex-start',
        gap: tokens.spacingHorizontalS,
    },
    provisioningIcon: {
        marginTop: '2px',
        color: tokens.colorBrandForeground1,
        flexShrink: 0,
    },
    provisioningText: {
        display: 'flex',
        flexDirection: 'column',
        gap: '2px',
    },
    provisioningItemTitle: {
        color: tokens.colorNeutralForeground1,
    },
    provisioningItemDesc: {
        color: tokens.colorNeutralForeground3,
    },
    // ── Warning bar ───────────────────────────────────────────────────────────
    warningBar: {
        borderRadius: tokens.borderRadiusMedium,
    },
});
const PROVISIONING_ITEMS = [
    {
        icon: React.createElement(BuildingRegular, { fontSize: 16 }),
        title: 'Dedicated Business Unit',
        description: 'A Dataverse Business Unit is created to scope security roles and data access for this project.',
    },
    {
        icon: React.createElement(StorageRegular, { fontSize: 16 }),
        title: 'SharePoint Embedded Container',
        description: 'An isolated SPE document container is provisioned exclusively for this project\u2019s files.',
    },
    {
        icon: React.createElement(PeopleTeamRegular, { fontSize: 16 }),
        title: 'External Access Portal',
        description: 'A Power Pages workspace is activated so invited external users can access project documents and events.',
    },
];
// ---------------------------------------------------------------------------
// SecureProjectSection (exported)
// ---------------------------------------------------------------------------
export const SecureProjectSection = ({ isSecure, onSecureChange, }) => {
    const styles = useStyles();
    const handleToggleChange = React.useCallback((_ev, data) => {
        onSecureChange(data.checked);
    }, [onSecureChange]);
    return (React.createElement("div", { className: styles.root },
        React.createElement("div", { className: styles.dividerRow },
            React.createElement(Divider, null)),
        React.createElement("div", { className: styles.toggleRow },
            React.createElement(LockClosedRegular, { fontSize: 20, className: isSecure ? styles.toggleIconSecure : styles.toggleIcon, "aria-hidden": "true" }),
            React.createElement("div", { className: styles.toggleText },
                React.createElement(Text, { size: 400, weight: "semibold", className: styles.toggleLabel }, "Secure Project"),
                React.createElement(Text, { size: 200, className: styles.toggleDescription }, "Enables external access, an isolated document container, and dedicated security boundaries for this project.")),
            React.createElement(Switch, { checked: isSecure, onChange: handleToggleChange, label: isSecure ? 'Enabled' : 'Disabled', labelPosition: "before", "aria-label": "Mark this project as a Secure Project" })),
        isSecure && (React.createElement(React.Fragment, null,
            React.createElement("div", { className: styles.infoPanel },
                React.createElement(Text, { size: 300, weight: "semibold", className: styles.infoPanelTitle }, "What will be provisioned when this project is created:"),
                React.createElement("div", { className: styles.provisioningList }, PROVISIONING_ITEMS.map((item) => (React.createElement("div", { key: item.title, className: styles.provisioningItem },
                    React.createElement("span", { className: styles.provisioningIcon, "aria-hidden": "true" }, item.icon),
                    React.createElement("div", { className: styles.provisioningText },
                        React.createElement(Text, { size: 300, weight: "semibold", className: styles.provisioningItemTitle }, item.title),
                        React.createElement(Text, { size: 200, className: styles.provisioningItemDesc }, item.description))))))),
            React.createElement(MessageBar, { intent: "warning", className: styles.warningBar },
                React.createElement(MessageBarBody, null,
                    React.createElement(Text, { size: 200, weight: "semibold" },
                        React.createElement(WarningRegular, { fontSize: 14, "aria-hidden": "true" }),
                        ' ',
                        "This designation is permanent.",
                        ' '),
                    React.createElement(Text, { size: 200 }, "Once a project is marked as Secure and created, the secure designation cannot be removed. Please confirm this is correct before proceeding.")))))));
};
export default SecureProjectSection;
//# sourceMappingURL=SecureProjectSection.js.map