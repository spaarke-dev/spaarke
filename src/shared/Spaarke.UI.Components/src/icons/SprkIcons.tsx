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
import {
    // File operations
    Add24Regular,
    Delete24Regular,
    ArrowUpload24Regular,
    ArrowDownload24Regular,
    DocumentAdd24Regular,
    DocumentEdit24Regular,
    FolderOpen24Regular,

    // Navigation
    Home24Regular,
    Settings24Regular,
    People24Regular,
    Apps24Regular,

    // Status
    CheckmarkCircle24Regular,
    ErrorCircle24Regular,
    Warning24Regular,
    Info24Regular,

    // Actions
    Save24Regular,
    Dismiss24Regular,
    Edit24Regular,
    Search24Regular,
    Filter24Regular,
    MoreVertical24Regular,

    // Common
    ChevronRight24Regular,
    ChevronDown24Regular,
    ChevronLeft24Regular,
    ChevronUp24Regular,
} from '@fluentui/react-icons';

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
export const SprkIcons = {
    // File Operations
    Add: Add24Regular,
    Delete: Delete24Regular,
    Upload: ArrowUpload24Regular,
    Download: ArrowDownload24Regular,
    DocumentAdd: DocumentAdd24Regular,
    DocumentEdit: DocumentEdit24Regular,
    FolderOpen: FolderOpen24Regular,

    // Navigation
    Home: Home24Regular,
    Settings: Settings24Regular,
    People: People24Regular,
    Apps: Apps24Regular,

    // Status
    Success: CheckmarkCircle24Regular,
    Error: ErrorCircle24Regular,
    Warning: Warning24Regular,
    Info: Info24Regular,

    // Actions
    Save: Save24Regular,
    Cancel: Dismiss24Regular,
    Edit: Edit24Regular,
    Search: Search24Regular,
    Filter: Filter24Regular,
    More: MoreVertical24Regular,

    // Common
    ChevronRight: ChevronRight24Regular,
    ChevronDown: ChevronDown24Regular,
    ChevronLeft: ChevronLeft24Regular,
    ChevronUp: ChevronUp24Regular,
} as const;

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
export function getIcon(name: SprkIconName): React.ComponentType {
    return SprkIcons[name];
}
