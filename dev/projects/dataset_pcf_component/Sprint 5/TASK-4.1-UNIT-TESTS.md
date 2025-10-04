# TASK-4.1: Unit Tests

**Status**: 🚧 IN PROGRESS
**Estimated Time**: 5 hours
**Sprint**: 5 - Universal Dataset PCF Component
**Phase**: 4 - Testing & Quality
**Dependencies**: TASK-3.5 (Entity Configuration)

---

## Objective

Create comprehensive unit tests for the Universal Dataset component library with ≥80% code coverage. Test all services, hooks, and utilities to ensure production-ready quality.

---

## Scope

### In Scope
- EntityConfigurationService tests (loading, merging, validation)
- CustomCommandFactory tests (command creation, token interpolation, execution)
- CommandRegistry tests (built-in and custom commands)
- Hook tests (useVirtualization, useKeyboardShortcuts)
- PrivilegeService tests
- Utility tests (theme detection, column rendering)
- Mock PCF context and WebAPI
- Coverage ≥80% (statements, functions, lines)

### Out of Scope
- React component tests (TASK-4.2)
- Integration tests (TASK-4.2)
- E2E tests (TASK-4.3)
- Performance benchmarks

---

## Testing Strategy

### Testing Framework
- **Jest 29.x**: Test runner and assertion library
- **@testing-library/react 14.x**: React hooks testing
- **@testing-library/jest-dom**: Custom matchers
- **ts-jest**: TypeScript support

### Test Organization
```
src/shared/Spaarke.UI.Components/
├── src/
│   ├── services/
│   │   ├── __tests__/
│   │   │   ├── EntityConfigurationService.test.ts
│   │   │   ├── CustomCommandFactory.test.ts
│   │   │   ├── CommandRegistry.test.ts
│   │   │   ├── PrivilegeService.test.ts
│   │   │   └── ColumnRendererService.test.ts
│   ├── hooks/
│   │   ├── __tests__/
│   │   │   ├── useVirtualization.test.ts
│   │   │   ├── useKeyboardShortcuts.test.ts
│   │   │   ├── useDatasetMode.test.ts
│   │   │   └── useHeadlessMode.test.ts
│   ├── utils/
│   │   └── __tests__/
│   │       └── themeDetection.test.ts
├── jest.config.js
├── jest.setup.js
└── package.json (updated with test scripts)
```

---

## Test Suites

### 1. EntityConfigurationService Tests

**File**: `src/services/__tests__/EntityConfigurationService.test.ts`

**Test Cases**:
- ✅ `loadConfiguration()`: Parse valid JSON
- ✅ `loadConfiguration()`: Handle invalid JSON gracefully
- ✅ `loadConfiguration()`: Handle null/undefined config
- ✅ `loadConfiguration()`: Validate schema version
- ✅ `getEntityConfiguration()`: Merge entity config with defaults
- ✅ `getEntityConfiguration()`: Return defaults for unknown entity
- ✅ `getEntityConfiguration()`: Override defaults with entity config
- ✅ `getCustomCommand()`: Get custom command by key
- ✅ `getCustomCommand()`: Return undefined for unknown command
- ✅ `validateConfiguration()`: Detect missing required fields
- ✅ `validateConfiguration()`: Validate custom command structure
- ✅ `isConfigurationLoaded()`: Return true when loaded

**Coverage Target**: 100%

---

### 2. CustomCommandFactory Tests

**File**: `src/services/__tests__/CustomCommandFactory.test.ts`

**Test Cases**:
- ✅ `createCommand()`: Create command from JSON config
- ✅ `createCommand()`: Map icon names to components
- ✅ `createCommand()`: Handle missing optional properties
- ✅ Token interpolation: `{selectedCount}`
- ✅ Token interpolation: `{entityName}`
- ✅ Token interpolation: `{parentRecordId}`
- ✅ Token interpolation: `{parentTable}`
- ✅ `executeCustomApi()`: Call webAPI.execute with correct request
- ✅ `executeAction()`: Execute bound action
- ✅ `executeAction()`: Execute unbound action
- ✅ `executeFunction()`: Execute OData function
- ✅ `executeWorkflow()`: Execute workflow with correct format
- ✅ Validate minSelection/maxSelection

**Coverage Target**: ≥85%

---

### 3. CommandRegistry Tests

**File**: `src/services/__tests__/CommandRegistry.test.ts`

**Test Cases**:
- ✅ `getCommand()`: Get built-in create command
- ✅ `getCommand()`: Get built-in open command
- ✅ `getCommand()`: Get built-in delete command
- ✅ `getCommand()`: Get built-in refresh command
- ✅ `getCommand()`: Return undefined for unknown command
- ✅ `getCommands()`: Filter by privilege (canCreate)
- ✅ `getCommands()`: Filter by privilege (canDelete)
- ✅ `getCommandsWithCustom()`: Include custom commands from config
- ✅ `getCommandsWithCustom()`: Prioritize built-in over custom
- ✅ `getCommandsWithCustom()`: Filter custom commands by privilege

**Coverage Target**: 100%

---

### 4. Hook Tests

**File**: `src/hooks/__tests__/useVirtualization.test.ts`

**Test Cases**:
- ✅ `useVirtualization()`: Should not virtualize <100 records
- ✅ `useVirtualization()`: Should virtualize ≥100 records
- ✅ `useVirtualization()`: Respect custom threshold
- ✅ `useVirtualization()`: Use default item height
- ✅ `useVirtualization()`: Use custom item height
- ✅ `useVirtualization()`: Set overscan count

**File**: `src/hooks/__tests__/useKeyboardShortcuts.test.ts`

**Test Cases**:
- ✅ `useKeyboardShortcuts()`: Execute command on Ctrl+N
- ✅ `useKeyboardShortcuts()`: Execute command on F5
- ✅ `useKeyboardShortcuts()`: Execute command on Delete
- ✅ `useKeyboardShortcuts()`: Prevent default behavior
- ✅ `useKeyboardShortcuts()`: Don't execute disabled commands
- ✅ `useKeyboardShortcuts()`: Don't execute when selection required
- ✅ `useKeyboardShortcuts()`: Cleanup event listener on unmount

**Coverage Target**: ≥80%

---

### 5. Utility Tests

**File**: `src/utils/__tests__/themeDetection.test.ts`

**Test Cases**:
- ✅ `detectTheme()`: Return webLightTheme for "Light"
- ✅ `detectTheme()`: Return webDarkTheme for "Dark"
- ✅ `detectTheme()`: Auto-detect from context
- ✅ `detectTheme()`: Fallback to light theme

**Coverage Target**: 100%

---

## Mock Objects

### Mock PCF Context
```typescript
export const createMockContext = (): any => ({
  webAPI: {
    createRecord: jest.fn(),
    updateRecord: jest.fn(),
    deleteRecord: jest.fn(),
    retrieveRecord: jest.fn(),
    retrieveMultipleRecords: jest.fn(),
    execute: jest.fn()
  },
  navigation: {
    openForm: jest.fn()
  },
  mode: {
    isControlDisabled: false,
    isVisible: true
  },
  parameters: {},
  utils: {
    getEntityMetadata: jest.fn()
  }
});
```

### Mock Dataset
```typescript
export const createMockDataset = (): ComponentFramework.PropertyTypes.DataSet => ({
  loading: false,
  error: false,
  errorMessage: "",
  sortedRecordIds: ["1", "2", "3"],
  records: {
    "1": createMockRecord("1"),
    "2": createMockRecord("2"),
    "3": createMockRecord("3")
  },
  columns: [
    { name: "name", displayName: "Name", dataType: "SingleLine.Text" }
  ],
  security: {
    editable: true,
    readable: true,
    secured: false
  }
} as any);
```

---

## Jest Configuration

**File**: `jest.config.js`

```javascript
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.test.ts', '**/__tests__/**/*.test.tsx'],
  collectCoverageFrom: [
    'src/**/*.{ts,tsx}',
    '!src/**/*.d.ts',
    '!src/**/index.ts',
    '!src/**/__tests__/**'
  ],
  coverageThreshold: {
    global: {
      statements: 80,
      branches: 80,
      functions: 80,
      lines: 80
    }
  },
  setupFilesAfterEnv: ['<rootDir>/jest.setup.js'],
  moduleNameMapper: {
    '\\.(css|less|scss|sass)$': 'identity-obj-proxy'
  },
  transform: {
    '^.+\\.tsx?$': ['ts-jest', {
      tsconfig: {
        jsx: 'react',
        esModuleInterop: true,
        allowSyntheticDefaultImports: true
      }
    }]
  }
};
```

---

## Package.json Updates

```json
{
  "scripts": {
    "test": "jest",
    "test:watch": "jest --watch",
    "test:coverage": "jest --coverage",
    "test:ci": "jest --ci --coverage --maxWorkers=2"
  },
  "devDependencies": {
    "@testing-library/react": "^14.0.0",
    "@testing-library/jest-dom": "^6.1.4",
    "@testing-library/user-event": "^14.5.1",
    "@types/jest": "^29.5.8",
    "jest": "^29.7.0",
    "jest-environment-jsdom": "^29.7.0",
    "ts-jest": "^29.1.1",
    "identity-obj-proxy": "^3.0.0"
  }
}
```

---

## Success Criteria

- ✅ All test suites pass
- ✅ Code coverage ≥80% (statements, functions, lines)
- ✅ No console errors or warnings in tests
- ✅ Tests run in <30 seconds
- ✅ Mock objects properly simulate PCF environment
- ✅ All critical paths tested (happy path + error cases)
- ✅ Tests are maintainable and well-documented

---

## Deliverables

1. **Jest configuration** (`jest.config.js`, `jest.setup.js`)
2. **EntityConfigurationService tests** (12 test cases)
3. **CustomCommandFactory tests** (13 test cases)
4. **CommandRegistry tests** (10 test cases)
5. **Hook tests** (13 test cases)
6. **Utility tests** (4 test cases)
7. **Mock utilities** (`__mocks__/pcfMocks.ts`)
8. **Coverage report** (≥80%)
9. **Updated package.json** with test scripts

---

## Implementation Notes

- Use `renderHook` from @testing-library/react for hook tests
- Mock all external dependencies (webAPI, navigation)
- Test both success and error paths
- Use descriptive test names (Given-When-Then pattern)
- Group related tests with `describe()` blocks
- Clean up after each test with `afterEach()`

---

## Timeline

- **Hour 1**: Set up Jest infrastructure, mock objects
- **Hour 2**: EntityConfigurationService + CustomCommandFactory tests
- **Hour 3**: CommandRegistry + PrivilegeService tests
- **Hour 4**: Hook tests (useVirtualization, useKeyboardShortcuts)
- **Hour 5**: Utility tests, coverage validation, documentation

---

## Standards Compliance

- ✅ **Jest 29.x**: Modern testing framework
- ✅ **TypeScript**: All tests in TypeScript
- ✅ **Coverage ≥80%**: Quality threshold
- ✅ **Fast tests**: <30 seconds total
- ✅ **Isolated tests**: No interdependencies
- ✅ **Readable tests**: Clear naming and structure
