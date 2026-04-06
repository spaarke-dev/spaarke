/**
 * SearchInput component
 *
 * Provides the search text input with search button and toolbar icon buttons.
 * Supports placeholder configuration and triggers search on button click or Enter key.
 * Empty query is allowed — returns all documents in scope.
 *
 * Toolbar layout: [Search input] [Search button] [+ Add] [Open Viewer]
 *
 * @see ADR-021 for Fluent UI v9 requirements
 */

import * as React from 'react';
import { useCallback, KeyboardEvent } from 'react';
import { makeStyles, tokens, Input, Button, Spinner, Tooltip } from '@fluentui/react-components';
import { Search20Regular, AddRegular, OpenRegular } from '@fluentui/react-icons';
import { ISearchInputProps } from '../types';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
    alignItems: 'center',
  },
  input: {
    flex: 1,
    minWidth: '200px',
  },
  searchButton: {
    minWidth: '90px',
  },
});

/**
 * SearchInput component with text input, search button, and toolbar icon buttons.
 */
export const SearchInput: React.FC<ISearchInputProps> = ({
  value,
  placeholder,
  disabled,
  onValueChange,
  onSearch,
  onAddDocument,
  onOpenViewer,
}) => {
  const styles = useStyles();

  const handleInputChange = useCallback(
    (ev: React.ChangeEvent<HTMLInputElement>) => {
      onValueChange(ev.target.value);
    },
    [onValueChange]
  );

  const handleKeyDown = useCallback(
    (ev: KeyboardEvent<HTMLInputElement>) => {
      if (ev.key === 'Enter' && !disabled) {
        onSearch();
      }
    },
    [disabled, onSearch]
  );

  const handleSearchClick = useCallback(() => {
    if (!disabled) {
      onSearch();
    }
  }, [disabled, onSearch]);

  return (
    <div className={styles.container}>
      <Input
        className={styles.input}
        value={value}
        placeholder={placeholder}
        disabled={disabled}
        onChange={handleInputChange}
        onKeyDown={handleKeyDown}
        contentBefore={<Search20Regular />}
        appearance="outline"
        size="medium"
      />
      <Button
        className={styles.searchButton}
        appearance="primary"
        disabled={disabled}
        onClick={handleSearchClick}
        icon={disabled ? <Spinner size="tiny" /> : undefined}
      >
        {disabled ? 'Searching...' : 'Search'}
      </Button>
      <Tooltip content="Add Document" relationship="label">
        <Button
          appearance="subtle"
          icon={<AddRegular />}
          onClick={onAddDocument}
          disabled={disabled}
          aria-label="Add Document"
        />
      </Tooltip>
      {onOpenViewer && (
        <Tooltip content="Open full viewer" relationship="label">
          <Button
            appearance="subtle"
            icon={<OpenRegular />}
            onClick={onOpenViewer}
            disabled={disabled}
            aria-label="Open full viewer"
          />
        </Tooltip>
      )}
    </div>
  );
};

export default SearchInput;
