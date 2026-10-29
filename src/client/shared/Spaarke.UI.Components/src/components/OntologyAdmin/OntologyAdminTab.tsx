/**
 * OntologyAdminTab - the shell of the Console's "Ontology admin" workspace tab (task 104; D-22, FR-56).
 *
 * A workspace tab, not a top bar or a full page (the Console has three panes only, #47). The host registers it only when the
 * capability probe (`fetchOntologyAdminCapability`) says the caller can author policies; this component does not ask again and
 * holds no role check of its own (#42). What it shows is never what is allowed: every admin write in an area goes through a BFF
 * route that runs as the caller, so Dataverse enforces the Spaarke Ontology Administrator role on each one.
 *
 * R1 has exactly two areas, Rules and Classification (spec FR-57..FR-59). The M-11..M-14 pages (Actions, Suppression, Health,
 * Access) are not part of R1 and have no area here.
 *
 * EXTENSION POINTS. Each area is a slot the area's task fills; the shell owns only the area switch:
 *   - `rules`          - task 100 (the rule list, read-only rule page, On/Off, Retire); task 102's rule wizard opens in-app from it.
 *   - `classification` - task 103 (triage categories).
 * The slot content arrives through the host widget (`@spaarke/ai-widgets` `OntologyAdminWorkspaceWidget`). An empty slot shows a
 * neutral empty state.
 *
 * Standards: ADR-012 (shared lib), ADR-021 (Fluent v9 semantic tokens only; light and dark), ADR-022 (React-version-agnostic).
 */

import * as React from 'react';
import { makeStyles, Tab, TabList, Text, tokens } from '@fluentui/react-components';
import type { SelectTabData, SelectTabEvent } from '@fluentui/react-components';

/** The R1 areas, a closed set (spec FR-57..FR-59). */
export type OntologyAdminAreaId = 'rules' | 'classification';

/** The areas in display order. Adding an area is a spec change, not a code extension. */
export const ONTOLOGY_ADMIN_AREAS: ReadonlyArray<{ readonly id: OntologyAdminAreaId; readonly label: string }> = [
  { id: 'rules', label: 'Rules' },
  { id: 'classification', label: 'Classification' },
];

export interface OntologyAdminTabProps {
  /** The Rules area (task 100). */
  rules?: React.ReactNode;
  /** The Classification area (task 103). */
  classification?: React.ReactNode;
  /** The area shown first; Rules when omitted. */
  defaultArea?: OntologyAdminAreaId;
}

const EMPTY_TEXT: Record<OntologyAdminAreaId, string> = {
  rules: 'Rules will appear here.',
  classification: 'Triage categories will appear here.',
};

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    width: '100%',
    minWidth: 0,
    boxSizing: 'border-box',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  areas: {
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    borderBottomStyle: 'solid',
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomColor: tokens.colorNeutralStroke2,
  },
  panel: {
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    minWidth: 0,
  },
  empty: {
    color: tokens.colorNeutralForeground3,
  },
});

export const OntologyAdminTab: React.FC<OntologyAdminTabProps> = ({ rules, classification, defaultArea = 'rules' }) => {
  const styles = useStyles();
  const [area, setArea] = React.useState<OntologyAdminAreaId>(defaultArea);

  const onTabSelect = React.useCallback((_event: SelectTabEvent, data: SelectTabData) => {
    setArea(data.value as OntologyAdminAreaId);
  }, []);

  const content: Record<OntologyAdminAreaId, React.ReactNode> = { rules, classification };
  const current = content[area];
  const label = ONTOLOGY_ADMIN_AREAS.find(a => a.id === area)?.label ?? area;

  return (
    <div className={styles.root} data-testid="ontology-admin-tab">
      <TabList
        className={styles.areas}
        selectedValue={area}
        onTabSelect={onTabSelect}
        aria-label="Ontology admin areas"
      >
        {ONTOLOGY_ADMIN_AREAS.map(a => (
          <Tab key={a.id} value={a.id} id={`ontology-admin-area-${a.id}`} data-testid={`ontology-admin-area-${a.id}`}>
            {a.label}
          </Tab>
        ))}
      </TabList>
      <div
        className={styles.panel}
        role="tabpanel"
        aria-labelledby={`ontology-admin-area-${area}`}
        aria-label={label}
        data-testid={`ontology-admin-panel-${area}`}
      >
        {current ?? <Text className={styles.empty}>{EMPTY_TEXT[area]}</Text>}
      </div>
    </div>
  );
};

OntologyAdminTab.displayName = 'OntologyAdminTab';
