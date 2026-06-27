/**
 * LookupUserMembershipForm - Configuration form for Lookup User Membership nodes.
 *
 * Resolves the set of user record IDs that have membership on a given parent entity
 * (e.g., matter team members, organization users). Backed by `LookupUserMembershipNodeExecutor`
 * (server, ActionType=52) which calls `IMembershipResolverService` in-process.
 *
 * Fields (matches server config contract — see LookupUserMembershipNodeExecutor):
 * - entityType (required): Dataverse logical name of the parent record (e.g., sprk_matter).
 *   Free-text per owner Q4 — discovery service determines validity at runtime; NO hardcoded allow-list.
 * - roles (optional): Comma-separated role names to filter by. Empty = all roles.
 * - outputVariable (required): Canvas variable name where resolved user IDs are bound.
 * - includeRelated (optional, default false): 1-hop transitive membership inclusion
 *   per owner Q3 (multi-hop options DELIBERATELY not exposed — executor + resolver enforce 1-hop max).
 *
 * Validation: missing entityType OR outputVariable surfaces as a config error in
 * `node.data.validationErrors` (set by NodePropertiesForm's NodeValidationBadge consumer).
 *
 * @see ADR-021 - Fluent UI v9 design system (dark mode required)
 * @see spec FR-1B.4 - PlaybookBuilder properties form for LookupUserMembership
 * @see owner Q5 - Align with existing per-ActionType form pattern (CreateNotificationForm); DO NOT invent new patterns
 */

import { useCallback, useMemo, memo } from 'react';
import { makeStyles, tokens, Text, Input, Label, Switch } from '@fluentui/react-components';
import type { SwitchOnChangeData } from '@fluentui/react-components';
import type { NodeFormProps } from '../../types/forms';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  form: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  field: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  fieldRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
  },
  fieldHint: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase100,
  },
});

// ---------------------------------------------------------------------------
// Config shape — matches server LookupUserMembershipNodeExecutor contract
// ---------------------------------------------------------------------------

interface LookupUserMembershipConfig {
  /** Dataverse logical name of the parent record whose members to resolve. */
  entityType: string;
  /** Optional role names to filter by. Empty array = all roles. */
  roles: string[];
  /** Canvas variable name to bind the resolved user ID list to. */
  outputVariable: string;
  /** Include 1-hop transitive memberships (Q3: multi-hop NOT supported). */
  includeRelated: boolean;
}

const DEFAULT_CONFIG: LookupUserMembershipConfig = {
  entityType: '',
  roles: [],
  outputVariable: '',
  includeRelated: false,
};

// ---------------------------------------------------------------------------
// Helpers — parse/serialize + roles comma-split
// ---------------------------------------------------------------------------

function parseConfig(json: string): LookupUserMembershipConfig {
  try {
    const parsed = JSON.parse(json) as Partial<LookupUserMembershipConfig>;
    return {
      entityType: typeof parsed.entityType === 'string' ? parsed.entityType : DEFAULT_CONFIG.entityType,
      roles: Array.isArray(parsed.roles)
        ? parsed.roles.filter((r): r is string => typeof r === 'string')
        : DEFAULT_CONFIG.roles,
      outputVariable: typeof parsed.outputVariable === 'string' ? parsed.outputVariable : DEFAULT_CONFIG.outputVariable,
      includeRelated:
        typeof parsed.includeRelated === 'boolean' ? parsed.includeRelated : DEFAULT_CONFIG.includeRelated,
    };
  } catch {
    return { ...DEFAULT_CONFIG };
  }
}

function serializeConfig(config: LookupUserMembershipConfig): string {
  return JSON.stringify(config);
}

/** Convert a comma-separated input to a trimmed, non-empty string[]. */
function parseRolesInput(raw: string): string[] {
  return raw
    .split(',')
    .map(s => s.trim())
    .filter(s => s.length > 0);
}

/** Render the roles array as a comma-separated string for the Input value. */
function formatRolesValue(roles: string[]): string {
  return roles.join(', ');
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const LookupUserMembershipForm = memo(function LookupUserMembershipForm({
  nodeId,
  configJson,
  onConfigChange,
}: NodeFormProps) {
  const styles = useStyles();
  const config = useMemo(() => parseConfig(configJson), [configJson]);

  const update = useCallback(
    (patch: Partial<LookupUserMembershipConfig>) => {
      onConfigChange(serializeConfig({ ...config, ...patch }));
    },
    [config, onConfigChange]
  );

  // -- Handlers --

  const handleEntityTypeChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      update({ entityType: e.target.value });
    },
    [update]
  );

  const handleRolesChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      update({ roles: parseRolesInput(e.target.value) });
    },
    [update]
  );

  const handleOutputVariableChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      update({ outputVariable: e.target.value });
    },
    [update]
  );

  const handleIncludeRelatedChange = useCallback(
    (_e: React.ChangeEvent<HTMLInputElement>, data: SwitchOnChangeData) => {
      update({ includeRelated: data.checked });
    },
    [update]
  );

  // -- Render --

  return (
    <div className={styles.form}>
      {/* Entity Type (required) */}
      <div className={styles.field}>
        <Label htmlFor={`${nodeId}-entityType`} size="small" required>
          Entity Type
        </Label>
        <Input
          id={`${nodeId}-entityType`}
          size="small"
          value={config.entityType}
          onChange={handleEntityTypeChange}
          placeholder="e.g., sprk_matter, sprk_organization"
        />
        <Text className={styles.fieldHint}>
          Dataverse logical name of the parent record. Validity is determined by the membership discovery service at
          runtime.
        </Text>
      </div>

      {/* Roles (optional, comma-separated) */}
      <div className={styles.field}>
        <Label htmlFor={`${nodeId}-roles`} size="small">
          Roles
        </Label>
        <Input
          id={`${nodeId}-roles`}
          size="small"
          value={formatRolesValue(config.roles)}
          onChange={handleRolesChange}
          placeholder="e.g., Owner, ProjectManager (leave empty for all roles)"
        />
        <Text className={styles.fieldHint}>
          Comma-separated role names to filter membership. Empty returns members with any role.
        </Text>
      </div>

      {/* Output Variable (required) */}
      <div className={styles.field}>
        <Label htmlFor={`${nodeId}-outputVariable`} size="small" required>
          Output Variable
        </Label>
        <Input
          id={`${nodeId}-outputVariable`}
          size="small"
          value={config.outputVariable}
          onChange={handleOutputVariableChange}
          placeholder="e.g., matterMembers"
        />
        <Text className={styles.fieldHint}>
          Canvas variable name that downstream nodes reference, e.g. {'{{matterMembers.output.userIds}}'} or via{' '}
          {'{{joinIds matterMembers.output.userIds}}'}.
        </Text>
      </div>

      {/* Include Related (1-hop transitive, default false) */}
      <div className={styles.field}>
        <div className={styles.fieldRow}>
          <Switch
            id={`${nodeId}-includeRelated`}
            checked={config.includeRelated}
            onChange={handleIncludeRelatedChange}
          />
          <Label htmlFor={`${nodeId}-includeRelated`} size="small">
            Include related (1-hop)
          </Label>
        </div>
        <Text className={styles.fieldHint}>
          When on, includes users transitively related through linked records (1 hop only). Multi-hop traversal is not
          supported.
        </Text>
      </div>
    </div>
  );
});
