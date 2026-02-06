/**
 * ViewSelector Unit Tests
 *
 * @see components/DatasetGrid/ViewSelector.tsx
 */

import * as React from "react";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { ViewSelector, IViewSelectorProps } from "../ViewSelector";
import type { XrmContext } from "../../../utils/xrmContext";
import type { IViewDefinition } from "../../../types/FetchXmlTypes";

// Mock ViewService
jest.mock("../../../services/ViewService", () => ({
  ViewService: jest.fn().mockImplementation(() => ({
    getViews: jest.fn(),
    clearCache: jest.fn(),
  })),
}));

import { ViewService } from "../../../services/ViewService";

// Wrapper to provide Fluent context
const TestWrapper: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <FluentProvider theme={webLightTheme}>{children}</FluentProvider>
);

const renderWithProvider = (ui: React.ReactElement) => {
  return render(ui, { wrapper: TestWrapper });
};

// Mock Xrm context
const createMockXrm = (): XrmContext => ({
  WebApi: {
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    retrieveRecord: jest.fn().mockResolvedValue({}),
    createRecord: jest.fn().mockResolvedValue({ id: "mock-id", entityType: "mock" }),
    updateRecord: jest.fn().mockResolvedValue({ id: "mock-id", entityType: "mock" }),
    deleteRecord: jest.fn().mockResolvedValue({ id: "mock-id", entityType: "mock" }),
  },
});

// Mock views data
const mockViews: IViewDefinition[] = [
  {
    id: "view-1",
    name: "Active Records",
    entityLogicalName: "account",
    fetchXml: "<fetch/>",
    layoutXml: "<grid/>",
    isDefault: true,
    viewType: "savedquery",
    sortOrder: 0,
  },
  {
    id: "view-2",
    name: "All Records",
    entityLogicalName: "account",
    fetchXml: "<fetch/>",
    layoutXml: "<grid/>",
    isDefault: false,
    viewType: "savedquery",
    sortOrder: 100,
  },
  {
    id: "view-3",
    name: "Custom View",
    entityLogicalName: "account",
    fetchXml: "<fetch/>",
    layoutXml: "<grid/>",
    isDefault: false,
    viewType: "custom",
    sortOrder: 50,
  },
];

describe("ViewSelector", () => {
  let mockXrm: XrmContext;
  let mockGetViews: jest.Mock;

  beforeEach(() => {
    mockXrm = createMockXrm();
    mockGetViews = jest.fn().mockResolvedValue(mockViews);
    (ViewService as jest.Mock).mockImplementation(() => ({
      getViews: mockGetViews,
      clearCache: jest.fn(),
    }));
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  const defaultProps: IViewSelectorProps = {
    xrm: createMockXrm(),
    entityLogicalName: "account",
  };

  describe("loading state", () => {
    it("should show loading spinner initially", () => {
      mockGetViews.mockImplementation(
        () => new Promise(() => {}) // Never resolves
      );

      renderWithProvider(<ViewSelector {...defaultProps} />);

      expect(screen.getByRole("progressbar")).toBeInTheDocument();
    });

    it("should show default view name while loading", () => {
      mockGetViews.mockImplementation(() => new Promise(() => {}));

      renderWithProvider(
        <ViewSelector {...defaultProps} defaultViewName="My View" />
      );

      expect(screen.getByText("My View")).toBeInTheDocument();
    });
  });

  describe("loaded state", () => {
    it("should render dropdown when views are loaded", async () => {
      renderWithProvider(<ViewSelector {...defaultProps} />);

      await waitFor(() => {
        expect(screen.getByRole("combobox")).toBeInTheDocument();
      });
    });

    it("should call ViewService.getViews with correct entity", async () => {
      renderWithProvider(
        <ViewSelector {...defaultProps} entityLogicalName="contact" />
      );

      await waitFor(() => {
        expect(mockGetViews).toHaveBeenCalledWith("contact", expect.any(Object));
      });
    });

    it("should include custom views when requested", async () => {
      renderWithProvider(
        <ViewSelector {...defaultProps} includeCustomViews />
      );

      await waitFor(() => {
        expect(mockGetViews).toHaveBeenCalledWith("account", {
          includeCustom: true,
          includePersonal: false,
        });
      });
    });

    it("should include personal views when requested", async () => {
      renderWithProvider(
        <ViewSelector {...defaultProps} includePersonalViews />
      );

      await waitFor(() => {
        expect(mockGetViews).toHaveBeenCalledWith("account", {
          includeCustom: false,
          includePersonal: true,
        });
      });
    });
  });

  describe("selection", () => {
    it("should display selected view name", async () => {
      renderWithProvider(
        <ViewSelector {...defaultProps} selectedViewId="view-2" />
      );

      await waitFor(() => {
        expect(screen.getByRole("combobox")).toHaveTextContent("All Records");
      });
    });

    it("should call onViewChange when selection changes", async () => {
      const onViewChange = jest.fn();

      renderWithProvider(
        <ViewSelector
          {...defaultProps}
          selectedViewId="view-1"
          onViewChange={onViewChange}
        />
      );

      await waitFor(() => {
        expect(screen.getByRole("combobox")).toBeInTheDocument();
      });

      // Open dropdown
      fireEvent.click(screen.getByRole("combobox"));

      // Select a different option
      await waitFor(() => {
        const option = screen.getByText("All Records");
        fireEvent.click(option);
      });

      expect(onViewChange).toHaveBeenCalledWith(
        expect.objectContaining({ id: "view-2", name: "All Records" })
      );
    });

    it("should auto-select default view when no selection provided", async () => {
      const onViewChange = jest.fn();

      renderWithProvider(
        <ViewSelector {...defaultProps} onViewChange={onViewChange} />
      );

      await waitFor(() => {
        expect(onViewChange).toHaveBeenCalledWith(
          expect.objectContaining({ id: "view-1", isDefault: true })
        );
      });
    });
  });

  describe("error state", () => {
    it("should display error message when loading fails", async () => {
      mockGetViews.mockRejectedValue(new Error("Network error"));

      renderWithProvider(<ViewSelector {...defaultProps} />);

      await waitFor(() => {
        expect(screen.getByText(/Error:/)).toBeInTheDocument();
      });
    });
  });

  describe("empty state", () => {
    it("should show disabled dropdown when no views available", async () => {
      mockGetViews.mockResolvedValue([]);

      renderWithProvider(<ViewSelector {...defaultProps} />);

      await waitFor(() => {
        const combobox = screen.getByRole("combobox");
        expect(combobox).toBeDisabled();
      });
    });
  });

  describe("compact mode", () => {
    it("should apply compact styles when compact prop is true", async () => {
      renderWithProvider(<ViewSelector {...defaultProps} compact />);

      await waitFor(() => {
        expect(screen.getByRole("combobox")).toBeInTheDocument();
      });
    });
  });

  describe("disabled state", () => {
    it("should disable dropdown when disabled prop is true", async () => {
      renderWithProvider(<ViewSelector {...defaultProps} disabled />);

      await waitFor(() => {
        expect(screen.getByRole("combobox")).toBeDisabled();
      });
    });
  });

  describe("accessibility", () => {
    it("should have proper aria-label", async () => {
      renderWithProvider(
        <ViewSelector {...defaultProps} entityLogicalName="contact" />
      );

      await waitFor(() => {
        expect(screen.getByRole("combobox")).toHaveAttribute(
          "aria-label",
          "Select view for contact"
        );
      });
    });
  });
});
