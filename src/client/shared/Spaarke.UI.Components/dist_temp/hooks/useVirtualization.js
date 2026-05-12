import { useMemo } from 'react';
const DEFAULT_CONFIG = {
    enabled: true,
    threshold: 100,
    itemHeight: 44, // Matches Fluent UI DataGrid row height
    overscanCount: 5,
};
/**
 * Hook to determine if virtualization should be enabled based on record count
 */
export function useVirtualization(recordCount, config) {
    const finalConfig = useMemo(() => ({
        ...DEFAULT_CONFIG,
        ...config,
    }), [config]);
    return useMemo(() => ({
        shouldVirtualize: finalConfig.enabled && recordCount > finalConfig.threshold,
        itemHeight: finalConfig.itemHeight,
        overscanCount: finalConfig.overscanCount ?? DEFAULT_CONFIG.overscanCount,
    }), [recordCount, finalConfig]);
}
//# sourceMappingURL=useVirtualization.js.map