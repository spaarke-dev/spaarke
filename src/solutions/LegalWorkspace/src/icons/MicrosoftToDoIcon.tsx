/**
 * MicrosoftToDoIcon — inline SVG icon based on the Microsoft To Do app branding.
 *
 * Renders a simplified checkmark (two paths, solid fills) derived from the
 * official Microsoft To Do logo. Gradients and masks are omitted so that
 * multiple instances on the same page have no SVG ID conflicts.
 *
 * Props:
 *   - size: pixel dimension (default 20, matching Fluent icon convention)
 *   - active: when true, uses the branded blue palette; otherwise uses
 *     currentColor so it inherits the parent text colour (token-friendly).
 *
 * Usage:
 *   <MicrosoftToDoIcon size={20} active={flagged} />
 */

import * as React from "react";

export interface IMicrosoftToDoIconProps {
  /** Icon size in pixels (default 20). */
  size?: number;
  /** When true, renders in Microsoft To Do brand blue; otherwise uses currentColor. */
  active?: boolean;
  /** Optional className for additional styling. */
  className?: string;
}

export const MicrosoftToDoIcon: React.FC<IMicrosoftToDoIconProps> = ({
  size = 20,
  active = false,
  className,
}) => (
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
      fill={active ? "#195ABD" : "currentColor"}
    />
    {/* Front checkmark arm */}
    <path
      d="M1018.23 173.595L874.691 30.0521C858.094 13.4548 831.184 13.4548 814.587 30.052L263.751 580.888C247.153 597.485 247.153 624.395 263.751 640.992L407.293 784.535C423.891 801.132 450.8 801.132 467.397 784.535L1018.23 233.699C1034.83 217.102 1034.83 190.192 1018.23 173.595Z"
      fill={active ? "#2987E6" : "currentColor"}
    />
  </svg>
);
