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
export interface ISecureProjectSectionProps {
    /** Current toggle state — controlled by parent. */
    isSecure: boolean;
    /** Called when user flips the toggle. */
    onSecureChange: (value: boolean) => void;
}
export declare const SecureProjectSection: React.FC<ISecureProjectSectionProps>;
export default SecureProjectSection;
//# sourceMappingURL=SecureProjectSection.d.ts.map