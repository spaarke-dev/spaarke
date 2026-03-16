/**
 * Mock for @spaarke/ui-components used during testing.
 * Only exports the types/components referenced by the code under test.
 */
export const SprkChat = jest.fn(() => null);
export const SprkChatBridge = jest.fn().mockImplementation(() => ({
  subscribe: jest.fn(() => jest.fn()),
  emit: jest.fn(),
  disconnect: jest.fn(),
  isDisconnected: false,
}));
