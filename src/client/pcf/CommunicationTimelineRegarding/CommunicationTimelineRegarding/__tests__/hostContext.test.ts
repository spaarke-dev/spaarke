import { resolveRegardingContext, REGARDING_FAMILY_ENTITIES } from '../hostContext';

describe('resolveRegardingContext', () => {
  it('placed on a supported regarding-family entity (sprk_matter) → returns entityType + id', () => {
    const result = resolveRegardingContext('sprk_matter', 'matter-guid-1');
    expect(result).toEqual({ entityType: 'sprk_matter', id: 'matter-guid-1' });
  });

  it.each(REGARDING_FAMILY_ENTITIES)('placed on %s (all 11 families) → returns entityType + id', entity => {
    const result = resolveRegardingContext(entity, 'record-guid');
    expect(result).toEqual({ entityType: entity, id: 'record-guid' });
  });

  it('placed on an unsupported entity → undefined', () => {
    const result = resolveRegardingContext('sprk_communication', 'comm-guid-1');
    expect(result).toBeUndefined();
  });

  it('no host record id → undefined', () => {
    const result = resolveRegardingContext('sprk_matter', undefined);
    expect(result).toBeUndefined();
  });

  it('host entity undefined (Xrm frame-walk failed) → undefined', () => {
    const result = resolveRegardingContext(undefined, undefined);
    expect(result).toBeUndefined();
  });

  it('REGARDING_FAMILY_ENTITIES has exactly the 11 ADR-024 families', () => {
    expect(REGARDING_FAMILY_ENTITIES).toHaveLength(11);
  });
});
