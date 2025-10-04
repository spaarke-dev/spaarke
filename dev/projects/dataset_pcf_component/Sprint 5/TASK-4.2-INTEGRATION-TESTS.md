# TASK-4.2: Integration Tests

**Status**: 🚧 IN PROGRESS
**Estimated Time**: 4 hours
**Sprint**: 5 - Universal Dataset PCF Component
**Phase**: 4 - Testing & Quality
**Dependencies**: TASK-4.1 (Unit Tests)

---

## Objective

Create integration tests for React components to verify rendering, user interactions, and integration with services. Target ≥70% coverage for component code.

---

## Scope

### In Scope
- UniversalDatasetGrid component rendering
- GridView, ListView, CardView rendering
- VirtualizedGridView, VirtualizedListView rendering
- CommandToolbar component and command execution
- User interactions (clicks, selections, keyboard)
- Service integration (CommandRegistry, EntityConfigurationService)
- Error boundary testing
- Loading states and error states
- Coverage ≥70% for components

### Out of Scope
- E2E tests (TASK-4.3)
- Visual regression tests
- Performance benchmarks
- PCF framework integration (requires actual PCF runtime)

---

## Testing Strategy

### Testing Libraries
- **@testing-library/react 16.x**: Component rendering and queries
- **@testing-library/user-event 14.x**: User interaction simulation
- **jest-dom**: Custom DOM matchers
- **React 18.2**: Testing concurrent features

### Test Organization
```
src/shared/Spaarke.UI.Components/
├── src/
│   ├── components/
│   │   ├── DatasetGrid/
│   │   │   ├── __tests__/
│   │   │   │   ├── UniversalDatasetGrid.test.tsx
│   │   │   │   ├── GridView.test.tsx
│   │   │   │   ├── ListView.test.tsx
│   │   │   │   ├── CardView.test.tsx
│   │   │   │   ├── VirtualizedGridView.test.tsx
│   │   │   │   └── VirtualizedListView.test.tsx
│   │   ├── Toolbar/
│   │   │   └── __tests__/
│   │   │       └── CommandToolbar.test.tsx
```

---

## Test Suites

### 1. UniversalDatasetGrid Integration Tests

**File**: `src/components/DatasetGrid/__tests__/UniversalDatasetGrid.test.tsx`

**Test Cases**:
- ✅ Render with dataset records
- ✅ Render with headless configuration
- ✅ Switch between Grid/List/Card view modes
- ✅ Load entity configuration from JSON
- ✅ Merge entity config with props config
- ✅ Display correct columns based on dataset
- ✅ Handle empty dataset
- ✅ Handle loading state
- ✅ Handle error state
- ✅ Execute commands via toolbar
- ✅ Keyboard shortcuts integration
- ✅ Virtualization enabled for large datasets
- ✅ Custom commands from entity configuration
- ✅ Privilege-based command filtering

**Coverage Target**: ≥70%

---

### 2. GridView Integration Tests

**File**: `src/components/DatasetGrid/__tests__/GridView.test.tsx`

**Test Cases**:
- ✅ Render records in grid layout
- ✅ Display columns with correct headers
- ✅ Handle row selection (single/multi)
- ✅ Render formatted values
- ✅ Render lookup columns
- ✅ Sort columns
- ✅ Resize columns
- ✅ Use VirtualizedGridView for >1000 records
- ✅ Handle empty records
- ✅ Apply field-level security (hidden columns)

**Coverage Target**: ≥70%

---

### 3. ListView Integration Tests

**File**: `src/components/DatasetGrid/__tests__/ListView.test.tsx`

**Test Cases**:
- ✅ Render records in list layout
- ✅ Display primary field prominently
- ✅ Handle row selection
- ✅ Use VirtualizedListView for >100 records
- ✅ Render secondary fields
- ✅ Handle click on list item
- ✅ Apply compact mode
- ✅ Handle empty records

**Coverage Target**: ≥70%

---

### 4. CardView Integration Tests

**File**: `src/components/DatasetGrid/__tests__/CardView.test.tsx`

**Test Cases**:
- ✅ Render records as cards
- ✅ Display card title and subtitle
- ✅ Handle card selection
- ✅ Display card image (if configured)
- ✅ Render card actions
- ✅ Handle click on card
- ✅ Apply grid layout (responsive)
- ✅ Handle empty records

**Coverage Target**: ≥70%

---

### 5. VirtualizedGridView Integration Tests

**File**: `src/components/DatasetGrid/__tests__/VirtualizedGridView.test.tsx`

**Test Cases**:
- ✅ Render large dataset (10000+ records)
- ✅ Virtual scrolling active
- ✅ Sticky header row
- ✅ Handle row selection in virtual list
- ✅ Render only visible rows
- ✅ Update on scroll
- ✅ Overscan count applied

**Coverage Target**: ≥70%

---

### 6. VirtualizedListView Integration Tests

**File**: `src/components/DatasetGrid/__tests__/VirtualizedListView.test.tsx`

**Test Cases**:
- ✅ Render large dataset
- ✅ Virtual scrolling active
- ✅ Handle row selection
- ✅ Render formatted values
- ✅ Update on scroll

**Coverage Target**: ≥70%

---

### 7. CommandToolbar Integration Tests

**File**: `src/components/Toolbar/__tests__/CommandToolbar.test.tsx`

**Test Cases**:
- ✅ Render toolbar with commands
- ✅ Execute command on button click
- ✅ Disable commands based on selection
- ✅ Group commands (primary, secondary, overflow)
- ✅ Display overflow menu when >8 commands
- ✅ Show tooltips with keyboard shortcuts
- ✅ Show confirmation dialog for confirmationMessage
- ✅ Show success message after execution
- ✅ Refresh dataset after command execution
- ✅ Compact toolbar mode
- ✅ Icon-only mode
- ✅ Divider after command

**Coverage Target**: ≥75%

---

## Mock Utilities Extensions

### Extended PCF Mocks
```typescript
// Add to pcfMocks.ts

export const createMockFluentProvider = (children: React.ReactNode) => (
  <FluentProvider theme={webLightTheme}>
    {children}
  </FluentProvider>
);

export const renderWithProviders = (
  ui: React.ReactElement,
  options?: RenderOptions
) => {
  return render(ui, {
    wrapper: ({ children }) => createMockFluentProvider(children),
    ...options
  });
};
```

---

## Integration Test Patterns

### 1. Component Rendering
```typescript
it('should render UniversalDatasetGrid with records', () => {
  const mockDataset = createMockDataset(['1', '2', '3'], 'account');
  const mockContext = createMockContext();

  renderWithProviders(
    <UniversalDatasetGrid
      dataset={mockDataset}
      context={mockContext}
      config={{ viewMode: 'Grid' }}
    />
  );

  expect(screen.getByRole('grid')).toBeInTheDocument();
  expect(screen.getAllByRole('row')).toHaveLength(4); // Header + 3 rows
});
```

### 2. User Interaction
```typescript
it('should execute command on toolbar button click', async () => {
  const mockHandler = jest.fn();
  const user = userEvent.setup();

  renderWithProviders(
    <CommandToolbar
      commands={[
        {
          key: 'create',
          label: 'New',
          requiresSelection: false,
          handler: mockHandler
        }
      ]}
      context={mockContext}
    />
  );

  const button = screen.getByRole('button', { name: /new/i });
  await user.click(button);

  expect(mockHandler).toHaveBeenCalledWith(mockContext);
});
```

### 3. Service Integration
```typescript
it('should load entity configuration and apply custom commands', () => {
  const configJson = JSON.stringify({
    schemaVersion: "1.0",
    defaultConfig: {},
    entityConfigs: {
      account: {
        enabledCommands: ["open", "customAction"]
      }
    }
  });

  renderWithProviders(
    <UniversalDatasetGrid
      dataset={mockDataset}
      context={mockContext}
      configJson={configJson}
    />
  );

  expect(screen.getByRole('button', { name: /open/i })).toBeInTheDocument();
});
```

### 4. Keyboard Events
```typescript
it('should execute command on keyboard shortcut', async () => {
  const mockHandler = jest.fn();
  const user = userEvent.setup();

  renderWithProviders(
    <UniversalDatasetGrid
      dataset={mockDataset}
      context={mockContext}
      config={{ enabledCommands: ['create'] }}
    />
  );

  await user.keyboard('{Control>}N{/Control}');

  expect(mockHandler).toHaveBeenCalled();
});
```

---

## Jest Configuration Updates

Update `jest.config.js` to include component coverage:

```javascript
collectCoverageFrom: [
  'src/services/EntityConfigurationService.ts',
  'src/services/CustomCommandFactory.ts',
  'src/services/CommandRegistry.ts',
  'src/services/CommandExecutor.ts',
  'src/hooks/useVirtualization.ts',
  'src/hooks/useKeyboardShortcuts.ts',
  'src/utils/themeDetection.ts',
  'src/components/**/*.{ts,tsx}', // Add component coverage
  '!src/**/*.d.ts',
  '!src/**/index.ts',
  '!src/**/__tests__/**',
  '!src/__mocks__/**'
],
coverageThreshold: {
  global: {
    statements: 75, // Adjusted for component tests
    branches: 70,
    functions: 75,
    lines: 75
  }
}
```

---

## Success Criteria

- ✅ All integration tests pass
- ✅ Component coverage ≥70% (statements, functions, lines)
- ✅ No console errors or warnings in tests
- ✅ Tests run in <15 seconds
- ✅ User interactions tested (click, keyboard, selection)
- ✅ Service integration verified
- ✅ Error states tested

---

## Deliverables

1. **UniversalDatasetGrid tests** (14 test cases)
2. **GridView tests** (10 test cases)
3. **ListView tests** (8 test cases)
4. **CardView tests** (8 test cases)
5. **VirtualizedGridView tests** (7 test cases)
6. **VirtualizedListView tests** (5 test cases)
7. **CommandToolbar tests** (12 test cases)
8. **Extended mock utilities**
9. **Updated Jest configuration**
10. **Coverage report** (≥70%)

**Total**: ~64 integration test cases

---

## Timeline

- **Hour 1**: UniversalDatasetGrid + GridView tests
- **Hour 2**: ListView + CardView tests
- **Hour 3**: VirtualizedGridView + VirtualizedListView tests
- **Hour 4**: CommandToolbar tests, coverage validation, documentation

---

## Standards Compliance

- ✅ **Testing Library**: Best practices (queries, user-event)
- ✅ **React 18**: Concurrent features support
- ✅ **TypeScript**: All tests in TypeScript
- ✅ **Coverage ≥70%**: Component code coverage
- ✅ **Accessible**: Test with ARIA roles and semantic queries
- ✅ **Fast tests**: <15 seconds total

---

## Implementation Notes

- Use `screen.getByRole()` for accessible queries
- Use `userEvent` over `fireEvent` for realistic interactions
- Mock Fluent UI FluentProvider for theme support
- Test loading/error states with conditional rendering
- Use `waitFor()` for async state updates
- Clean up after each test with `cleanup()`
