import { useMemo } from "react";

export interface VirtualizationConfig {
  enabled: boolean;
  threshold: number; // Number of records to trigger virtualization
  itemHeight: number; // Fixed height per row
  overscanCount?: number; // Extra rows to render outside viewport
}

export interface VirtualizationResult {
  shouldVirtualize: boolean;
  itemHeight: number;
  overscanCount: number;
}

const DEFAULT_CONFIG: VirtualizationConfig = {
  enabled: true,
  threshold: 100,
  itemHeight: 44, // Matches Fluent UI DataGrid row height
  overscanCount: 5
};

/**
 * Hook to determine if virtualization should be enabled based on record count
 */
export function useVirtualization(
  recordCount: number,
  config?: Partial<VirtualizationConfig>
): VirtualizationResult {
  const finalConfig = useMemo(() => ({
    ...DEFAULT_CONFIG,
    ...config
  }), [config]);

  return useMemo(() => ({
    shouldVirtualize: finalConfig.enabled && recordCount > finalConfig.threshold,
    itemHeight: finalConfig.itemHeight,
    overscanCount: finalConfig.overscanCount ?? DEFAULT_CONFIG.overscanCount!
  }), [recordCount, finalConfig]);
}
