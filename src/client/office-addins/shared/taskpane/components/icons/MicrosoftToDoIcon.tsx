import * as React from 'react';
import { tokens } from '@fluentui/react-components';

/**
 * MicrosoftToDoIcon — recreated locally (the add-in does not depend on `@spaarke/ui-components`; this
 * project's documented ADR-012 Path-A exception — the same reason `FindResultsList.tsx`'s
 * `thinScrollbarStyle` is recreated locally rather than imported).
 *
 * Source of truth: `src/client/shared/Spaarke.UI.Components/src/icons/MicrosoftToDoIcon.tsx` (the
 * Microsoft To Do blue-check mark used by the Smart To Do surfaces, e.g.
 * `src/solutions/SmartTodo/src/components/KanbanHeader.tsx:169`). Keep the two files in step — the SVG
 * paths below are copied byte-for-byte from that file.
 *
 * Task 091 (UAT-2, owner 2026-10-03): replaces `TaskListAddRegular` on the pane's "To Do" tab so the tab
 * matches the Microsoft To Do app's own icon.
 */

export interface IMicrosoftToDoIconProps {
  /** Icon size in pixels (default 20, matching Fluent icon convention). */
  size?: number;
  /** When true, renders in Microsoft To Do brand blue; otherwise uses currentColor. */
  active?: boolean;
  /** Optional className for additional styling. */
  className?: string;
}

export const MicrosoftToDoIcon: React.FC<IMicrosoftToDoIconProps> = ({ size = 20, active = false, className }) => (
  <svg
    width={size}
    height={size}
    viewBox="0 0 1079 875"
    fill="none"
    xmlns="http://www.w3.org/2000/svg"
    className={className}
    aria-hidden="true"
  >
    {/* Back checkmark arm */}
    <path
      d="M203.646 233.698L60.1038 377.241C43.5065 393.838 43.5065 420.748 60.1038 437.345L407.293 784.534C423.891 801.132 450.8 801.132 467.397 784.534L610.94 640.992C627.537 624.394 627.537 597.485 610.94 580.888L263.751 233.698C247.153 217.101 220.244 217.101 203.646 233.698Z"
      fill={active ? tokens.colorBrandForeground1 : 'currentColor'}
    />
    {/* Front checkmark arm */}
    <path
      d="M1018.23 173.595L874.691 30.0521C858.094 13.4548 831.184 13.4548 814.587 30.052L263.751 580.888C247.153 597.485 247.153 624.395 263.751 640.992L407.293 784.535C423.891 801.132 450.8 801.132 467.397 784.535L1018.23 233.699C1034.83 217.102 1034.83 190.192 1018.23 173.595Z"
      fill={active ? tokens.colorBrandForeground2 : 'currentColor'}
    />
  </svg>
);

export default MicrosoftToDoIcon;
