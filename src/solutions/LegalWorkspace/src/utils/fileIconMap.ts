/**
 * fileIconMap.ts — maps file types/extensions to Fluent UI v9 icons.
 *
 * Used by DocumentCard to display a dynamic icon based on sprk_filetype.
 * Logic extracted from DocumentItem.tsx (MyPortfolio) and enhanced with
 * additional file type support.
 */

import type { FluentIcon } from '@fluentui/react-icons';
import {
  DocumentPdfRegular,
  DocumentTextRegular,
  TableRegular,
  DocumentRegular,
  SlideTextRegular,
  ImageRegular,
  CodeRegular,
  MailRegular,
  ArchiveRegular,
} from '@fluentui/react-icons';

/**
 * Derive the file extension from a document name or file type string.
 * Returns lowercase extension without the dot, or empty string.
 */
export function getFileExtension(nameOrType: string): string {
  if (!nameOrType) return '';
  const trimmed = nameOrType.trim().toLowerCase();
  // If it looks like an extension already (no dot), return as-is
  if (!trimmed.includes('.')) return trimmed;
  const lastDot = trimmed.lastIndexOf('.');
  if (lastDot === -1 || lastDot === trimmed.length - 1) return '';
  return trimmed.substring(lastDot + 1);
}

/**
 * Map a file extension to a Fluent UI icon component.
 *
 * @param fileType - The sprk_filetype value (e.g. "pdf", "docx") or a filename
 * @returns The matching FluentIcon component
 */
export function getFileTypeIcon(fileType: string | undefined): FluentIcon {
  const ext = getFileExtension(fileType ?? '');
  switch (ext) {
    case 'pdf':
      return DocumentPdfRegular;
    case 'doc':
    case 'docx':
    case 'rtf':
    case 'odt':
    case 'txt':
      return DocumentTextRegular;
    case 'xls':
    case 'xlsx':
    case 'csv':
    case 'ods':
      return TableRegular;
    case 'ppt':
    case 'pptx':
    case 'odp':
      return SlideTextRegular;
    case 'jpg':
    case 'jpeg':
    case 'png':
    case 'gif':
    case 'bmp':
    case 'svg':
    case 'webp':
    case 'tiff':
    case 'tif':
      return ImageRegular;
    case 'html':
    case 'htm':
    case 'xml':
    case 'json':
    case 'js':
    case 'ts':
    case 'css':
      return CodeRegular;
    case 'msg':
    case 'eml':
      return MailRegular;
    case 'zip':
    case 'rar':
    case '7z':
    case 'tar':
    case 'gz':
      return ArchiveRegular;
    default:
      return DocumentRegular;
  }
}
