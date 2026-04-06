/**
 * SearchInput component
 *
 * Provides the search text input with search button and info popover.
 * Toolbar action icons (add, refresh, open) are in the ResultsList header.
 *
 * @see ADR-021 for Fluent UI v9 requirements
 */

import * as React from 'react';
import { useCallback, KeyboardEvent, useState } from 'react';
import {
  makeStyles,
  tokens,
  Input,
  Button,
  Spinner,
  Tooltip,
  Popover,
  PopoverTrigger,
  PopoverSurface,
  Text,
} from '@fluentui/react-components';
import { Search20Regular, Info20Regular } from '@fluentui/react-icons';
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
 * SearchInput component with text input, search button, and info icon.
 */
export const SearchInput: React.FC<ISearchInputProps> = ({
  value,
  placeholder,
  disabled,
  onValueChange,
  onSearch,
}) => {
  const styles = useStyles();
  const [infoOpen, setInfoOpen] = useState(false);

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
      <Popover
        open={infoOpen}
        onOpenChange={(_ev, data) => setInfoOpen(data.open)}
        positioning="below-end"
        withArrow
      >
        <PopoverTrigger disableButtonEnhancement>
          <Tooltip content="How semantic search works" relationship="label">
            <Button
              appearance="subtle"
              size="small"
              icon={<Info20Regular />}
              aria-label="Search info"
            />
          </Tooltip>
        </PopoverTrigger>
        <PopoverSurface style={{ maxWidth: '300px', padding: tokens.spacingHorizontalM }}>
          <Text size={200}>
            Semantic search finds documents by meaning, not just keywords. Results are ranked by
            similarity to your query. Toggle "Associated Only" in the filter panel to show only
            documents directly linked to this record.
          </Text>
        </PopoverSurface>
      </Popover>
    </div>
  );
};

export default SearchInput;
