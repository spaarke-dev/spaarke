/**
 * FileUploadZone.tsx
 * Generic drag-and-drop file upload zone for the Spaarke shared component library.
 *
 * Default accepted types: PDF (.pdf), DOCX (.docx), XLSX (.xlsx)
 * Default maximum size:   10 MB per file
 *
 * Consumers can override defaults via the `validationConfig` prop, including
 * accepted extensions, max file size, and a custom validator callback.
 *
 * Provides visual feedback (border highlight) on dragover.
 * Zero hardcoded colors — all styling via Fluent v9 semantic tokens.
 */
import * as React from 'react';
import { IFileUploadZoneProps } from './fileUploadTypes';
export declare const FileUploadZone: React.FC<IFileUploadZoneProps>;
//# sourceMappingURL=FileUploadZone.d.ts.map