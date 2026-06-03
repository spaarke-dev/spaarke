/**
 * OptionSetMultiFilterChip.stories — Storybook stories for the metadata-driven
 * option-set multi-select filter chip (task 006).
 *
 * Stories cover the three attribute types the chip supports per FR-DG-07:
 *   1. Picklist (priority Low / Medium / High) — no colors, neutral swatches
 *   2. Status (Active / Resolved / Cancelled) — colors from Dataverse metadata
 *   3. State (Active / Inactive) — colors from Dataverse metadata
 *
 * Each story is exposed via the `theme` argType so reviewers can toggle
 * light + dark + a "cleared" multi-select variant. The `cleared` argType
 * exposes the empty-selection visual.
 *
 * **Storybook config**: this project has no `.storybook/` configuration yet;
 * this file lives OUTSIDE `src/` so the TypeScript library build (`tsc`) does
 * not pick it up. When Storybook is wired in a later task, the standard
 * `main.ts` `stories: ['../storybook/**\/*.stories.@(ts|tsx)']` pattern picks
 * it up.
 *
 * @see projects/spaarke-datagrid-framework-r1/tasks/006-option-set-filter-chip.poml
 */

import * as React from 'react';
import { FluentProvider, webLightTheme, webDarkTheme, type Theme } from '@fluentui/react-components';
import { OptionSetMultiFilterChip } from '../src/components/DataGrid/chips/OptionSetMultiFilterChip';
import type { EntityMetadata } from '../src/services/IDataverseClient';

// ─────────────────────────────────────────────────────────────────────────────
// Mock metadata payloads — one per attribute type the chip supports.
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Picklist example — `sprk_priority` with three priority levels.
 * Picklist options typically lack `color` (Dataverse only ships colors on
 * Status / State). The chip renders neutral-outline swatches for this case.
 */
const picklistMetadata: EntityMetadata = {
  primaryIdAttribute: 'sprk_taskid',
  primaryNameAttribute: 'sprk_taskname',
  attributes: {
    sprk_taskid: { attributeType: 'String', isPrimaryId: true },
    sprk_taskname: { attributeType: 'String', isPrimaryName: true },
    sprk_priority: {
      attributeType: 'Picklist',
      optionSet: [
        { value: 100_000_000, label: 'Low' },
        { value: 100_000_001, label: 'Medium' },
        { value: 100_000_002, label: 'High' },
      ],
    },
  },
};

/**
 * Status example — `sprk_eventstatus` Status attribute with hex colors. Colors
 * here mirror typical Spaarke status-band conventions (green / blue / red).
 * `color` strings are DATA per the OptionSetOption contract — exempt from
 * NO-RAW-HEX.
 */
const statusMetadata: EntityMetadata = {
  primaryIdAttribute: 'sprk_eventid',
  primaryNameAttribute: 'sprk_eventname',
  attributes: {
    sprk_eventid: { attributeType: 'String', isPrimaryId: true },
    sprk_eventname: { attributeType: 'String', isPrimaryName: true },
    sprk_eventstatus: {
      attributeType: 'Status',
      optionSet: [
        { value: 1, label: 'Active', color: '#107C10' },
        { value: 2, label: 'Resolved', color: '#0078D4' },
        { value: 3, label: 'Cancelled', color: '#C50F1F' },
      ],
    },
  },
};

/**
 * State example — standard `statecode` State attribute with the platform's
 * Active / Inactive pair. Colors approximate Dataverse defaults.
 */
const stateMetadata: EntityMetadata = {
  primaryIdAttribute: 'sprk_eventid',
  primaryNameAttribute: 'sprk_eventname',
  attributes: {
    sprk_eventid: { attributeType: 'String', isPrimaryId: true },
    sprk_eventname: { attributeType: 'String', isPrimaryName: true },
    statecode: {
      attributeType: 'State',
      optionSet: [
        { value: 0, label: 'Active', color: '#107C10' },
        { value: 1, label: 'Inactive', color: '#8A8886' },
      ],
    },
  },
};

// ─────────────────────────────────────────────────────────────────────────────
// Storybook meta
// ─────────────────────────────────────────────────────────────────────────────

export default {
  title: 'DataGrid/Chips/OptionSetMultiFilterChip',
  component: OptionSetMultiFilterChip,
  argTypes: {
    theme: {
      control: { type: 'radio' },
      options: ['light', 'dark'],
    },
    cleared: {
      control: { type: 'boolean' },
      description: 'Render with no options selected (cleared state)',
    },
  },
};

interface StoryArgs {
  theme: 'light' | 'dark';
  cleared: boolean;
}

/**
 * Shared render shell — mounts a root `FluentProvider` with
 * `applyStylesToPortals` (NFR-03) and a small surface for the chip to sit
 * inside. The chip itself ALSO re-wraps its MenuPopover surface with
 * `applyStylesToPortals` per task 006 spec.
 */
const renderShell = (theme: 'light' | 'dark', content: React.ReactNode): React.ReactNode => {
  const resolved: Theme = theme === 'dark' ? webDarkTheme : webLightTheme;
  return (
    <FluentProvider
      applyStylesToPortals
      theme={resolved}
      style={{
        padding: '16px',
        minHeight: '320px',
        display: 'flex',
        alignItems: 'flex-start',
      }}
    >
      {content}
    </FluentProvider>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Story 1: Picklist (Low / Medium / High)
// ─────────────────────────────────────────────────────────────────────────────

export const Picklist = (args: StoryArgs) => {
  // Default: Low + High selected so "Low +1 more" pattern is visible. Toggle
  // `cleared` to show the empty-selection trigger label.
  const initial = args.cleared ? new Set<number>() : new Set<number>([100_000_000, 100_000_002]);
  const [value, setValue] = React.useState<Set<number>>(initial);
  // Reset state when arg toggles between cleared / pre-selected.
  React.useEffect(() => {
    setValue(initial);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [args.cleared]);

  return renderShell(
    args.theme,
    <OptionSetMultiFilterChip
      columnLogicalName="sprk_priority"
      entityMetadata={picklistMetadata}
      value={value}
      onChange={setValue}
      label="Priority"
      theme={args.theme === 'dark' ? webDarkTheme : webLightTheme}
    />
  );
};
Picklist.args = { theme: 'light' as const, cleared: false };
Picklist.storyName = 'Picklist (Priority, no colors)';

// ─────────────────────────────────────────────────────────────────────────────
// Story 2: Status (Active / Resolved / Cancelled)
// ─────────────────────────────────────────────────────────────────────────────

export const Status = (args: StoryArgs) => {
  const initial = args.cleared ? new Set<number>() : new Set<number>([1, 3]);
  const [value, setValue] = React.useState<Set<number>>(initial);
  React.useEffect(() => {
    setValue(initial);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [args.cleared]);

  return renderShell(
    args.theme,
    <OptionSetMultiFilterChip
      columnLogicalName="sprk_eventstatus"
      entityMetadata={statusMetadata}
      value={value}
      onChange={setValue}
      label="Status"
      theme={args.theme === 'dark' ? webDarkTheme : webLightTheme}
    />
  );
};
Status.args = { theme: 'light' as const, cleared: false };
Status.storyName = 'Status (with metadata colors)';

// ─────────────────────────────────────────────────────────────────────────────
// Story 3: State (Active / Inactive)
// ─────────────────────────────────────────────────────────────────────────────

export const State = (args: StoryArgs) => {
  const initial = args.cleared ? new Set<number>() : new Set<number>([0]);
  const [value, setValue] = React.useState<Set<number>>(initial);
  React.useEffect(() => {
    setValue(initial);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [args.cleared]);

  return renderShell(
    args.theme,
    <OptionSetMultiFilterChip
      columnLogicalName="statecode"
      entityMetadata={stateMetadata}
      value={value}
      onChange={setValue}
      label="State"
      theme={args.theme === 'dark' ? webDarkTheme : webLightTheme}
    />
  );
};
State.args = { theme: 'light' as const, cleared: false };
State.storyName = 'State (Active / Inactive)';

// ─────────────────────────────────────────────────────────────────────────────
// Story 4: All Cleared (visual baseline showing empty-selection labels)
// ─────────────────────────────────────────────────────────────────────────────

export const AllCleared = (args: StoryArgs) => {
  const [p, setP] = React.useState<Set<number>>(new Set());
  const [s, setS] = React.useState<Set<number>>(new Set());
  const [st, setSt] = React.useState<Set<number>>(new Set());
  const portalTheme = args.theme === 'dark' ? webDarkTheme : webLightTheme;

  return renderShell(
    args.theme,
    <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
      <OptionSetMultiFilterChip
        columnLogicalName="sprk_priority"
        entityMetadata={picklistMetadata}
        value={p}
        onChange={setP}
        label="Priority"
        theme={portalTheme}
      />
      <OptionSetMultiFilterChip
        columnLogicalName="sprk_eventstatus"
        entityMetadata={statusMetadata}
        value={s}
        onChange={setS}
        label="Status"
        theme={portalTheme}
      />
      <OptionSetMultiFilterChip
        columnLogicalName="statecode"
        entityMetadata={stateMetadata}
        value={st}
        onChange={setSt}
        label="State"
        theme={portalTheme}
      />
    </div>
  );
};
AllCleared.args = { theme: 'light' as const, cleared: true };
AllCleared.storyName = 'All cleared (3 chip strip)';
