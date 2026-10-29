/**
 * useWorklistNarrative - the Assistant's narrative for the list the widget holds (task 062; FR-31, FR-34).
 *
 * It asks `service.narrate` for prose over the list's COUNTS (see `worklistNarrative.ts`) and returns it only while the list it was
 * written for is still the list on screen: the text is tied to the request it answered, so a re-read that changes the counts hides it
 * until the new text arrives (a narrative never describes a list that is no longer there). No `narrate` on the service, an empty list or
 * a failed call is no narrative, never an error. It reads nothing else, so it cannot reorder, add or drop an item.
 */

import * as React from 'react';
import { buildWorklistNarrativeRequest, cachedNarrative, rememberNarrative } from './worklistNarrative';
import type { WorklistNarrativeText } from './worklistNarrative';
import type { WorklistData, WorklistService } from './worklistService';

export function useWorklistNarrative(
  service: WorklistService,
  lanes: WorklistData | undefined,
  today: Date,
  enabled: boolean
): WorklistNarrativeText | null {
  const request = React.useMemo(
    () => (enabled && service.narrate ? buildWorklistNarrativeRequest(lanes, today) : null),
    [enabled, service, lanes, today]
  );
  const key = request ? JSON.stringify(request) : null;
  const [answer, setAnswer] = React.useState<{ key: string; text: WorklistNarrativeText } | null>(null);

  React.useEffect(() => {
    if (!request || !key || !service.narrate) return undefined;
    const cached = cachedNarrative(key);
    if (cached) {
      setAnswer({ key, text: cached });
      return undefined;
    }
    let live = true;
    void service.narrate(request).then(text => {
      if (!live || !text) return;
      rememberNarrative(key, text);
      setAnswer({ key, text });
    });
    return () => {
      live = false;
    };
    // `key` is the request's identity: the same counts never call twice.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, service]);

  return answer && answer.key === key ? answer.text : null;
}
