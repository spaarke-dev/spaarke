/**
 * Spaarke Icon Library - Fluent UI v9 Icons
 *
 * Central registry for all icons used across the Spaarke platform.
 * Import from this library instead of directly from @fluentui/react-icons.
 *
 * Usage:
 *   import { SprkIcons } from '@spaarke/ui-components';
 *   <Button icon={<SprkIcons.Add />}>Add</Button>
 *
 * ADR: Use Fluent UI v9 icons exclusively (no other icon libraries)
 * Standard: All icons are 24x24 Regular variant for consistency
 */
import * as React from 'react';
/**
 * Spaarke icon collection.
 *
 * All icons are 24x24 Regular variant for consistency across the platform.
 *
 * Icon categories:
 * - File Operations: Add, Delete, Upload, Download, DocumentAdd, DocumentEdit, FolderOpen
 * - Navigation: Home, Settings, People, Apps
 * - Status: Success, Error, Warning, Info
 * - Actions: Save, Cancel, Edit, Search, Filter, More
 * - Common: ChevronRight, ChevronDown, ChevronLeft, ChevronUp
 */
export declare const SprkIcons: {
    readonly Add: import("@fluentui/react-icons").FluentIcon;
    readonly Delete: import("@fluentui/react-icons").FluentIcon;
    readonly Upload: import("@fluentui/react-icons").FluentIcon;
    readonly Download: import("@fluentui/react-icons").FluentIcon;
    readonly DocumentAdd: import("@fluentui/react-icons").FluentIcon;
    readonly DocumentEdit: import("@fluentui/react-icons").FluentIcon;
    readonly FolderOpen: import("@fluentui/react-icons").FluentIcon;
    readonly Home: import("@fluentui/react-icons").FluentIcon;
    readonly Settings: import("@fluentui/react-icons").FluentIcon;
    readonly People: import("@fluentui/react-icons").FluentIcon;
    readonly Apps: import("@fluentui/react-icons").FluentIcon;
    readonly Success: import("@fluentui/react-icons").FluentIcon;
    readonly Error: import("@fluentui/react-icons").FluentIcon;
    readonly Warning: import("@fluentui/react-icons").FluentIcon;
    readonly Info: import("@fluentui/react-icons").FluentIcon;
    readonly Save: import("@fluentui/react-icons").FluentIcon;
    readonly Cancel: import("@fluentui/react-icons").FluentIcon;
    readonly Edit: import("@fluentui/react-icons").FluentIcon;
    readonly Search: import("@fluentui/react-icons").FluentIcon;
    readonly Filter: import("@fluentui/react-icons").FluentIcon;
    readonly More: import("@fluentui/react-icons").FluentIcon;
    readonly ChevronRight: import("@fluentui/react-icons").FluentIcon;
    readonly ChevronDown: import("@fluentui/react-icons").FluentIcon;
    readonly ChevronLeft: import("@fluentui/react-icons").FluentIcon;
    readonly ChevronUp: import("@fluentui/react-icons").FluentIcon;
};
/**
 * Icon name type for type-safe usage.
 *
 * @example
 * const iconName: SprkIconName = 'Add'; // ✅ Type-safe
 * const iconName: SprkIconName = 'Invalid'; // ❌ TypeScript error
 */
export type SprkIconName = keyof typeof SprkIcons;
/**
 * Get icon component by name.
 *
 * @param name - Icon name from SprkIconName
 * @returns Icon component
 *
 * @example
 * const IconComponent = getIcon('Add');
 * <IconComponent />
 */
export declare function getIcon(name: SprkIconName): React.ComponentType;
//# sourceMappingURL=SprkIcons.d.ts.map