/**
 * RegardingLinkRenderer - Custom cell renderer for Regarding Record links
 *
 * Renders a clickable link that navigates to the parent record when the row says which record
 * that is; otherwise the grid's DEFAULT renderer is kept (the override returns null).
 *
 * v1.1.0 (unified-access-control-r2 task 168 f1): ported to the documented grid customizer
 * signature `(props, rendererParams)`. Earlier it rendered a plain span when it could not resolve
 * the target, replacing the default renderer; it now leaves such cells to the default renderer.
 *
 * ADR Compliance:
 * - ADR-021: Fluent UI v9 with dark mode support
 */

import * as React from 'react';
import { Link } from '@fluentui/react-components';
import { OpenRegular } from '@fluentui/react-icons';
import type { CellRendererProps, GetRendererParams, RowData } from '../types/PAGridCustomizer';

/**
 * Entity type to entity logical name mapping
 * Based on sprk_eventregardingtype option set values
 */
const REGARDING_TYPE_TO_ENTITY: Record<number, string> = {
  0: 'sprk_project', // Project
  1: 'sprk_matter', // Matter
  2: 'sprk_opportunity', // Opportunity
  3: 'account', // Account (system entity)
  4: 'contact', // Contact (system entity)
};

/**
 * Extracts the regarding record ID from the row data
 */
export function getRegardingRecordId(rowData: RowData | undefined): string | null {
  if (!rowData) return null;

  const idFields = ['sprk_regardingrecordid', '_sprk_regardingrecordid_value', 'regardingrecordid'];

  for (const field of idFields) {
    const value = rowData[field];
    if (typeof value === 'string' && value) {
      return value;
    }
  }

  return null;
}

/**
 * Extracts the regarding record type from the row data
 */
export function getRegardingRecordType(rowData: RowData | undefined): string | null {
  if (!rowData) return null;

  const typeFields = ['sprk_regardingrecordtype', 'regardingrecordtype'];

  for (const field of typeFields) {
    const value = rowData[field];
    if (typeof value === 'number') {
      return REGARDING_TYPE_TO_ENTITY[value] || null;
    }
    if (typeof value === 'string' && value) {
      const numValue = parseInt(value, 10);
      if (!isNaN(numValue)) {
        return REGARDING_TYPE_TO_ENTITY[numValue] || null;
      }
      // Already an entity name
      return value;
    }
  }

  return null;
}

interface XrmNavigationLike {
  Navigation?: { openForm?: (options: { entityName: string; entityId: string; openInNewWindow?: boolean }) => unknown };
}

/**
 * Opens a record: the model-driven app's navigation when present, else a record URL.
 */
function openRecord(entityName: string, recordId: string): void {
  const xrm = (globalThis as unknown as { Xrm?: XrmNavigationLike }).Xrm;
  if (xrm?.Navigation?.openForm) {
    void xrm.Navigation.openForm({ entityName, entityId: recordId, openInNewWindow: false });
    return;
  }
  const baseUrl = window.location.origin;
  window.open(`${baseUrl}/main.aspx?etn=${entityName}&id=${recordId}&pagetype=entityrecord`, '_blank');
}

interface RegardingLinkProps {
  displayName: string;
  entityName: string;
  recordId: string;
}

/**
 * RegardingLinkRenderer Component: a clickable link to the regarding (parent) record.
 */
export const RegardingLinkRenderer: React.FC<RegardingLinkProps> = ({ displayName, entityName, recordId }) => {
  const handleClick = (event: React.MouseEvent): void => {
    event.preventDefault();
    event.stopPropagation();
    openRecord(entityName, recordId);
  };

  const handleKeyDown = (event: React.KeyboardEvent): void => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      event.stopPropagation();
      openRecord(entityName, recordId);
    }
  };

  return React.createElement(
    Link,
    {
      className: 'sprk-regarding-link',
      onClick: handleClick,
      onKeyDown: handleKeyDown,
      role: 'link',
      tabIndex: 0,
      title: `Open ${displayName}`,
      'aria-label': `Open ${displayName} record`,
    },
    React.createElement(OpenRegular, {
      className: 'sprk-regarding-link-icon',
      'aria-hidden': true,
    }),
    React.createElement(
      'span',
      {
        className: 'sprk-regarding-link-text',
      },
      displayName
    )
  );
};

/**
 * The renderer override for a regarding name/id cell: a link when the row names its target,
 * else null (the grid's default renderer).
 */
export function renderRegardingLink(props: CellRendererProps, params: GetRendererParams): React.ReactElement | null {
  const raw = typeof props.formattedValue === 'string' && props.formattedValue ? props.formattedValue : props.value;
  const displayName = typeof raw === 'string' ? raw : '';
  if (!displayName) {
    return null;
  }
  const recordId = getRegardingRecordId(params.rowData);
  const entityName = getRegardingRecordType(params.rowData);
  if (!recordId || !entityName) {
    return null;
  }
  return React.createElement(RegardingLinkRenderer, { displayName, entityName, recordId });
}

export default RegardingLinkRenderer;
