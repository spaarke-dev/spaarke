import * as React from 'react';
import type { FirmLookup } from '../services/firmLookup';

/**
 * The names of the firms a rule set list or detail refers to, by id (lower case). The rule set API returns only the firm's id; the name is a read-only
 * lookup. A lookup that fails is not an error to the admin: the row shows the id instead.
 */
export function useFirmNames(lookup: FirmLookup, ids: readonly (string | null)[]): Record<string, string> {
  const [names, setNames] = React.useState<Record<string, string>>({});
  const wanted = React.useMemo(
    () => Array.from(new Set(ids.filter((id): id is string => !!id).map(id => id.toLowerCase()))).sort(),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [ids.join('|')]
  );

  React.useEffect(() => {
    const missing = wanted.filter(id => !(id in names));
    if (missing.length === 0) return;
    let cancelled = false;
    lookup
      .names(missing)
      .then(found => {
        if (!cancelled) setNames(prev => ({ ...prev, ...found }));
      })
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [wanted, lookup]);

  return names;
}

/** Where a rule set applies, in words. */
export function describeScope(scope: string, firmId: string | null, names: Record<string, string>): string {
  if (scope === 'ClientDefault') return 'Client default';
  if (scope === 'Firm') return firmId ? `Firm: ${names[firmId.toLowerCase()] ?? firmId}` : 'Firm (none chosen)';
  return 'Not assigned';
}
