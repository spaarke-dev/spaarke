/**
 * Services barrel export — re-exports all service classes and helpers
 * for clean import paths in consuming components and hooks.
 *
 * Usage:
 *   import { DataverseService } from '../services';
 *   import { buildMattersQuery } from '../services';
 */

export { DataverseService } from './DataverseService';
export * from './queryHelpers';
