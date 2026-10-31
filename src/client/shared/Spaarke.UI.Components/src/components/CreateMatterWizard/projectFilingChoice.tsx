/**
 * Where an existing project is filed, and the user's choice to move it (unified-access-control-r2 task 195, #2352).
 *
 * The Create Matter wizard links the new matter to a picked project and, when the project is not filed under anything,
 * files it under the new matter through the BFF. A project ALREADY filed under another matter is moved only when the
 * user chooses to (owner, 2026-10-31: re-filing is allowed, but never as a side effect): the default keeps it where it
 * is, and the link is still written.
 */
import * as React from 'react';
import { MessageBar, MessageBarBody, Radio, RadioGroup, Text, tokens } from '@fluentui/react-components';

import type { IDataService } from '../../types/serviceInterfaces';
import { cleanGuid } from '../../utils/guid';

/** Where a project is filed, read before the matter is created. */
export type ProjectFiling =
  | { kind: 'unfiled' }
  /** The pair names a record; `label` is its name or number when one could be read ('another matter' otherwise). */
  | { kind: 'filed'; recordId: string; label: string }
  /** The pair could not be read: the project is linked but not filed (fail closed — never a blind re-file). */
  | { kind: 'unreadable'; message: string };

const PAIR_SELECT = '?$select=sprk_regardingrecordid,sprk_regardingrecordname,_sprk_regardingrecordtype_value';

function text(value: unknown): string {
  return typeof value === 'string' ? value.trim() : '';
}

/**
 * Reads the project's ADR-024 regarding pair (`sprk_regardingrecordid` + `sprk_regardingrecordtype`) as the user. Never
 * throws: a failed read is `unreadable`.
 */
export async function readProjectFiling(dataService: IDataService, projectId: string): Promise<ProjectFiling> {
  let row: Record<string, unknown>;
  try {
    row = await dataService.retrieveRecord('sprk_project', cleanGuid(projectId), PAIR_SELECT);
  } catch (err) {
    return { kind: 'unreadable', message: err instanceof Error ? err.message : String(err) };
  }

  const recordId = cleanGuid(text(row['sprk_regardingrecordid']));
  const typeId = text(row['_sprk_regardingrecordtype_value']);
  if (!recordId && !typeId) return { kind: 'unfiled' };

  let label = text(row['sprk_regardingrecordname']);
  if (!label && recordId) {
    try {
      const matter = await dataService.retrieveRecord('sprk_matter', recordId, '?$select=sprk_matternumber');
      label = text(matter['sprk_matternumber']);
      // eslint-disable-next-line no-restricted-syntax -- only the display label is lost; the choice still shows, with the generic label below
    } catch {
      // Not a matter, or not readable: the generic label below.
    }
  }
  return { kind: 'filed', recordId, label: label || 'another matter' };
}

export interface IProjectFilingChoiceProps {
  /** The picked project's name. */
  projectName: string;
  /** Where it is filed; `undefined` while the read is in flight. */
  filing: ProjectFiling | undefined;
  /** True when the user chose to move it to the new matter (default false: keep). */
  move: boolean;
  onMoveChange: (move: boolean) => void;
}

/** The notice (and, for a project filed elsewhere, the keep/move choice) shown above the matter's details. */
export const ProjectFilingChoice: React.FC<IProjectFilingChoiceProps> = ({
  projectName,
  filing,
  move,
  onMoveChange,
}) => {
  if (!filing) {
    return (
      <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
        Checking where &ldquo;{projectName}&rdquo; is filed&hellip;
      </Text>
    );
  }
  if (filing.kind === 'unfiled') return null;
  if (filing.kind === 'unreadable') {
    return (
      <MessageBar intent="warning">
        <MessageBarBody>
          Where &ldquo;{projectName}&rdquo; is filed could not be read, so it will be linked to the new matter but not
          filed under it.
        </MessageBarBody>
      </MessageBar>
    );
  }
  return (
    <MessageBar intent="warning" layout="multiline">
      <MessageBarBody>
        <div>
          &ldquo;{projectName}&rdquo; is filed under {filing.label}. Move it to the new matter? Its owning team and the
          records under it follow the matter.
        </div>
        <RadioGroup
          value={move ? 'move' : 'keep'}
          onChange={(_e, data) => onMoveChange(data.value === 'move')}
          aria-label="Where the project is filed"
        >
          <Radio value="keep" label={`Keep it filed under ${filing.label}`} />
          <Radio value="move" label="Move it to the new matter" />
        </RadioGroup>
      </MessageBarBody>
    </MessageBar>
  );
};
