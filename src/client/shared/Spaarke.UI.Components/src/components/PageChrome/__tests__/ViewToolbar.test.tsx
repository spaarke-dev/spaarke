/**
 * ViewToolbar Unit Tests
 *
 * @see components/PageChrome/ViewToolbar.tsx
 */

import * as React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { ViewToolbar, IViewToolbarProps } from "../ViewToolbar";

// Wrapper to provide Fluent context
const TestWrapper: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <FluentProvider theme={webLightTheme}>{children}</FluentProvider>
);

const renderWithProvider = (ui: React.ReactElement) => {
  return render(ui, { wrapper: TestWrapper });
};

describe("ViewToolbar", () => {
  describe("rendering", () => {
    it("should render children when provided", () => {
      renderWithProvider(
        <ViewToolbar>
          <span data-testid="view-selector">View Selector</span>
        </ViewToolbar>
      );

      expect(screen.getByTestId("view-selector")).toBeInTheDocument();
    });

    it("should render view name button when viewName is provided", () => {
      renderWithProvider(<ViewToolbar viewName="Active Records" />);

      expect(screen.getByText("Active Records")).toBeInTheDocument();
    });

    it("should render record count when provided", () => {
      renderWithProvider(
        <ViewToolbar viewName="Active Records" recordCount={42} />
      );

      expect(screen.getByText("(42 records)")).toBeInTheDocument();
    });

    it("should use singular 'record' for count of 1", () => {
      renderWithProvider(
        <ViewToolbar viewName="Active Records" recordCount={1} />
      );

      expect(screen.getByText("(1 record)")).toBeInTheDocument();
    });

    it("should format large record counts with locale", () => {
      renderWithProvider(
        <ViewToolbar viewName="Active Records" recordCount={1234567} />
      );

      // Number should be formatted with commas
      expect(screen.getByText(/1,234,567/)).toBeInTheDocument();
    });
  });

  describe("Edit filters button", () => {
    it("should not render Edit filters button by default", () => {
      renderWithProvider(<ViewToolbar viewName="Test" />);

      expect(screen.queryByLabelText("Edit filters")).not.toBeInTheDocument();
    });

    it("should render Edit filters button when showEditFilters is true", () => {
      renderWithProvider(<ViewToolbar viewName="Test" showEditFilters />);

      expect(screen.getByLabelText("Edit filters")).toBeInTheDocument();
    });

    it("should call onEditFilters when button is clicked", () => {
      const onEditFilters = jest.fn();
      renderWithProvider(
        <ViewToolbar viewName="Test" showEditFilters onEditFilters={onEditFilters} />
      );

      fireEvent.click(screen.getByLabelText("Edit filters"));

      expect(onEditFilters).toHaveBeenCalledTimes(1);
    });
  });

  describe("Edit columns button", () => {
    it("should not render Edit columns button by default", () => {
      renderWithProvider(<ViewToolbar viewName="Test" />);

      expect(screen.queryByLabelText("Edit columns")).not.toBeInTheDocument();
    });

    it("should render Edit columns button when showEditColumns is true", () => {
      renderWithProvider(<ViewToolbar viewName="Test" showEditColumns />);

      expect(screen.getByLabelText("Edit columns")).toBeInTheDocument();
    });

    it("should call onEditColumns when button is clicked", () => {
      const onEditColumns = jest.fn();
      renderWithProvider(
        <ViewToolbar viewName="Test" showEditColumns onEditColumns={onEditColumns} />
      );

      fireEvent.click(screen.getByLabelText("Edit columns"));

      expect(onEditColumns).toHaveBeenCalledTimes(1);
    });
  });

  describe("view name button", () => {
    it("should call onViewClick when view name button is clicked", () => {
      const onViewClick = jest.fn();
      renderWithProvider(
        <ViewToolbar viewName="Active Records" onViewClick={onViewClick} />
      );

      fireEvent.click(screen.getByLabelText("Change view"));

      expect(onViewClick).toHaveBeenCalledTimes(1);
    });

    it("should have aria-haspopup attribute", () => {
      renderWithProvider(<ViewToolbar viewName="Test" />);

      expect(screen.getByLabelText("Change view")).toHaveAttribute(
        "aria-haspopup",
        "listbox"
      );
    });
  });

  describe("compact mode", () => {
    it("should hide button labels in compact mode", () => {
      renderWithProvider(
        <ViewToolbar
          viewName="Test"
          showEditFilters
          showEditColumns
          compact
        />
      );

      // Buttons should be present but without text labels
      expect(screen.getByLabelText("Edit filters")).toBeInTheDocument();
      expect(screen.getByLabelText("Edit columns")).toBeInTheDocument();
      // Text should not be visible (buttons are icon-only)
      expect(screen.queryByText("Edit filters")).not.toBeInTheDocument();
      expect(screen.queryByText("Edit columns")).not.toBeInTheDocument();
    });
  });

  describe("both buttons", () => {
    it("should render both buttons when both are enabled", () => {
      renderWithProvider(
        <ViewToolbar viewName="Test" showEditFilters showEditColumns />
      );

      expect(screen.getByLabelText("Edit filters")).toBeInTheDocument();
      expect(screen.getByLabelText("Edit columns")).toBeInTheDocument();
    });
  });

  describe("with children", () => {
    it("should render record count alongside children", () => {
      renderWithProvider(
        <ViewToolbar recordCount={100}>
          <span data-testid="view-selector">Custom Selector</span>
        </ViewToolbar>
      );

      expect(screen.getByTestId("view-selector")).toBeInTheDocument();
      expect(screen.getByText("(100 records)")).toBeInTheDocument();
    });
  });

  describe("accessibility", () => {
    it("should have toolbar role", () => {
      const { container } = renderWithProvider(<ViewToolbar viewName="Test" />);

      expect(container.querySelector('[role="toolbar"]')).toBeInTheDocument();
    });

    it("should have aria-label on toolbar", () => {
      const { container } = renderWithProvider(<ViewToolbar viewName="Test" />);

      expect(container.querySelector('[role="toolbar"]')).toHaveAttribute(
        "aria-label",
        "View toolbar"
      );
    });
  });
});
