export { CommunicationsWorkspaceWidget, default } from './CommunicationsWorkspaceWidget';
export type { CommunicationsWorkspaceWidgetProps } from './CommunicationsWorkspaceWidget';

// FR-22 communication-arrival awareness (task 045) — the HOST wires this ONCE at bootstrap, bound to its
// shared @spaarke/notifications client. See communicationArrivalsSeam.ts for the one-connection rationale.
export { setCommunicationArrivalsSubscribe, getCommunicationArrivalsSubscribe } from './communicationArrivalsSeam';
export type { ArrivalEvent, ArrivalSubscribe } from './useCommunicationArrivals';
