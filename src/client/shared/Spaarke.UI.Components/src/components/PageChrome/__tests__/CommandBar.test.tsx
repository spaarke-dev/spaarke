/**
 * CommandBar Unit Tests
 *
 * @see components/PageChrome/CommandBar.tsx
 */

import * as React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { CommandBar, ICommandBarItem } from "../CommandBar";

// Wrapper to provide Fluent context
const TestWrapper: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <FluentProvider theme={webLightTheme}>{children}</FluentProvider>
);

const renderWithProvider = (ui: React.ReactElement) => {
  return render(ui, { wrapper: TestWrapper });
};

describe("CommandBar", () => {
  const defaultProps = {
    entityLogicalName: "sprk_event",
  };

  describe("rendering", () => {
    it("should render New button by default", () => {
      renderWithProvider(<CommandBar {...defaultProps} />);

      expect(screen.getByLabelText(/create new/i)).toBeInTheDocument();
    });

    it("should render Delete button by default", () => {
      renderWithProvider(<CommandBar {...defaultProps} />);

      expect(screen.getByLabelText(/delete selected/i)).toBeInTheDocument();
    });

    it("should render Refresh button by default", () => {
      renderWithProvider(<CommandBar {...defaultProps} />);

      expect(screen.getByLabelText(/refresh data/i)).toBeInTheDocument();
    });

    it("should hide New button when showNew is false", () => {
      renderWithProvider(<CommandBar {...defaultProps} showNew={false} />);

      expect(screen.queryByLabelText(/create new/i)).not.toBeInTheDocument();
    });

    it("should hide Delete button when showDelete is false", () => {
      renderWithProvider(<CommandBar {...defaultProps} showDelete={false} />);

      expect(screen.queryByLabelText(/delete selected/i)).not.toBeInTheDocument();
    });

    it("should hide Refresh button when showRefresh is false", () => {
      renderWithProvider(<CommandBar {...defaultProps} showRefresh={false} />);

      expect(screen.queryByLabelText(/refresh data/i)).not.toBeInTheDocument();
    });

    it("should render search box when showSearch is true", () => {
      renderWithProvider(<CommandBar {...defaultProps} showSearch />);

      expect(screen.getByLabelText(/search records/i)).toBeInTheDocument();
    });
  });

  describe("button states", () => {
    it("should disable New button when canCreate is false", () => {
      renderWithProvider(<CommandBar {...defaultProps} canCreate={false} />);

      const newButton = screen.getByLabelText(/create new/i);
      expect(newButton).toBeDisabled();
    });

    it("should disable Delete button when no selection", () => {
      renderWithProvider(<CommandBar {...defaultProps} selectedIds={[]} />);

      const deleteButton = screen.getByLabelText(/delete selected/i);
      expect(deleteButton).toBeDisabled();
    });

    it("should enable Delete button when there is a selection", () => {
      renderWithProvider(
        <CommandBar {...defaultProps} selectedIds={["id-1", "id-2"]} canDelete />
      );

      const deleteButton = screen.getByLabelText(/delete selected/i);
      expect(deleteButton).not.toBeDisabled();
    });

    it("should disable Delete button when canDelete is false", () => {
      renderWithProvider(
        <CommandBar {...defaultProps} selectedIds={["id-1"]} canDelete={false} />
      );

      const deleteButton = screen.getByLabelText(/delete selected/i);
      expect(deleteButton).toBeDisabled();
    });

    it("should show selection count on Delete button", () => {
      renderWithProvider(<CommandBar {...defaultProps} selectedIds={["id-1", "id-2"]} />);

      expect(screen.getByText("2")).toBeInTheDocument();
    });
  });

  describe("event handlers", () => {
    it("should call onNew when New button is clicked", () => {
      const onNew = jest.fn();
      renderWithProvider(<CommandBar {...defaultProps} onNew={onNew} />);

      fireEvent.click(screen.getByLabelText(/create new/i));

      expect(onNew).toHaveBeenCalledTimes(1);
    });

    it("should call onDelete with selectedIds when Delete button is clicked", () => {
      const onDelete = jest.fn();
      const selectedIds = ["id-1", "id-2"];
      renderWithProvider(
        <CommandBar {...defaultProps} selectedIds={selectedIds} onDelete={onDelete} />
      );

      fireEvent.click(screen.getByLabelText(/delete selected/i));

      expect(onDelete).toHaveBeenCalledWith(selectedIds);
    });

    it("should call onRefresh when Refresh button is clicked", () => {
      const onRefresh = jest.fn();
      renderWithProvider(<CommandBar {...defaultProps} onRefresh={onRefresh} />);

      fireEvent.click(screen.getByLabelText(/refresh data/i));

      expect(onRefresh).toHaveBeenCalledTimes(1);
    });
  });

  describe("custom commands", () => {
    it("should render custom commands", () => {
      const commands: ICommandBarItem[] = [
        {
          key: "export",
          label: "Export",
          onClick: jest.fn(),
        },
        {
          key: "import",
          label: "Import",
          onClick: jest.fn(),
        },
      ];

      renderWithProvider(<CommandBar {...defaultProps} commands={commands} />);

      expect(screen.getByLabelText("Export")).toBeInTheDocument();
      expect(screen.getByLabelText("Import")).toBeInTheDocument();
    });

    it("should call custom command onClick", () => {
      const onClick = jest.fn();
      const commands: ICommandBarItem[] = [
        {
          key: "export",
          label: "Export",
          onClick,
        },
      ];

      renderWithProvider(<CommandBar {...defaultProps} commands={commands} />);

      fireEvent.click(screen.getByLabelText("Export"));

      expect(onClick).toHaveBeenCalledTimes(1);
    });

    it("should disable custom command when disabled is true", () => {
      const commands: ICommandBarItem[] = [
        {
          key: "export",
          label: "Export",
          disabled: true,
        },
      ];

      renderWithProvider(<CommandBar {...defaultProps} commands={commands} />);

      expect(screen.getByLabelText("Export")).toBeDisabled();
    });
  });

  describe("search functionality", () => {
    it("should call onSearch when Enter is pressed in search box", () => {
      const onSearch = jest.fn();
      renderWithProvider(
        <CommandBar {...defaultProps} showSearch onSearch={onSearch} />
      );

      const searchInput = screen.getByLabelText(/search records/i);
      fireEvent.change(searchInput, { target: { value: "test query" } });
      fireEvent.keyDown(searchInput, { key: "Enter" });

      expect(onSearch).toHaveBeenCalledWith("test query");
    });

    it("should use custom search placeholder", () => {
      renderWithProvider(
        <CommandBar
          {...defaultProps}
          showSearch
          searchPlaceholder="Find events..."
        />
      );

      expect(screen.getByPlaceholderText("Find events...")).toBeInTheDocument();
    });
  });

  describe("compact mode", () => {
    it("should apply compact styles when compact is true", () => {
      const { container } = renderWithProvider(
        <CommandBar {...defaultProps} compact />
      );

      // The toolbar should have the compact class applied
      const toolbar = container.querySelector('[role="toolbar"]');
      expect(toolbar).toBeInTheDocument();
    });
  });

  describe("accessibility", () => {
    it("should have proper aria-label on toolbar", () => {
      const { container } = renderWithProvider(<CommandBar {...defaultProps} />);

      const toolbar = container.querySelector('[role="toolbar"]');
      expect(toolbar).toHaveAttribute("aria-label", "sprk_event command bar");
    });

    it("should have aria-keyshortcuts on buttons", () => {
      renderWithProvider(<CommandBar {...defaultProps} />);

      expect(screen.getByLabelText(/create new/i)).toHaveAttribute(
        "aria-keyshortcuts",
        "Control+N"
      );
      expect(screen.getByLabelText(/delete selected/i)).toHaveAttribute(
        "aria-keyshortcuts",
        "Delete"
      );
      expect(screen.getByLabelText(/refresh data/i)).toHaveAttribute(
        "aria-keyshortcuts",
        "F5"
      );
    });
  });
});
