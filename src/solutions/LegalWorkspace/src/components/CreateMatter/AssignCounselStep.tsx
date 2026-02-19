/**
 * AssignCounselStep.tsx
 * Follow-on step for "Assign Counsel" in the Create New Matter wizard.
 *
 * Layout:
 *   ┌──────────────────────────────────────────────────────────────────────┐
 *   │  Assign Counsel                                                       │
 *   │  Search for a contact to assign as lead counsel for this matter.     │
 *   │                                                                       │
 *   │  [Search contacts...              ]                                   │
 *   │                                                                       │
 *   │  ┌─ Search results ─────────────────────────────────────────────┐   │
 *   │  │  ○ Jane Smith  · jane.smith@example.com             [Select] │   │
 *   │  │  ○ John Doe    · john.doe@example.com               [Select] │   │
 *   │  └─────────────────────────────────────────────────────────────┘   │
 *   │                                                                       │
 *   │  Selected: Jane Smith  ·  [Clear]                                    │
 *   └──────────────────────────────────────────────────────────────────────┘
 *
 * Uses Xrm.WebApi (via searchContacts from matterService) to query
 * sprk_contact records filtered by name. Minimum 2 characters required
 * before a search fires. Results debounced 400ms.
 *
 * Constraints:
 *   - Fluent v9: Input, Text, Button, Spinner, MessageBar
 *   - makeStyles with semantic tokens — ZERO hardcoded colors
 */

import * as React from 'react';
import {
  Input,
  Text,
  Button,
  Spinner,
  MessageBar,
  MessageBarBody,
  makeStyles,
  tokens,
  mergeClasses,
} from '@fluentui/react-components';
import { PersonRegular, DismissRegular, SearchRegular } from '@fluentui/react-icons';
import { searchContacts } from './matterService';
import type { IContact } from '../../types/entities';
import type { IWebApi } from '../../types/xrm';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IAssignCounselStepProps {
  /** PCF WebApi for Dataverse queries. */
  webApi: IWebApi;
  /** Currently selected contact (or null). */
  selectedContact: IContact | null;
  /** Called when the user selects or clears a contact. */
  onContactChange: (contact: IContact | null) => void;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
  },

  headerText: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  stepTitle: {
    color: tokens.colorNeutralForeground1,
  },
  stepSubtitle: {
    color: tokens.colorNeutralForeground3,
  },

  // ── Search area ────────────────────────────────────────────────────────
  searchWrapper: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },

  // ── Results list ────────────────────────────────────────────────────────
  resultsList: {
    display: 'flex',
    flexDirection: 'column',
    gap: '2px',
    borderTopWidth: '1px',
    borderRightWidth: '1px',
    borderBottomWidth: '1px',
    borderLeftWidth: '1px',
    borderTopStyle: 'solid',
    borderRightStyle: 'solid',
    borderBottomStyle: 'solid',
    borderLeftStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke1,
    borderRightColor: tokens.colorNeutralStroke1,
    borderBottomColor: tokens.colorNeutralStroke1,
    borderLeftColor: tokens.colorNeutralStroke1,
    borderRadius: tokens.borderRadiusMedium,
    overflow: 'hidden',
    maxHeight: '240px',
    overflowY: 'auto',
  },
  resultItem: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalS,
    cursor: 'pointer',
    gap: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground1,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
    ':focus-visible': {
      outline: `2px solid ${tokens.colorBrandStroke1}`,
      outlineOffset: '-2px',
    },
  },
  resultItemSelected: {
    backgroundColor: tokens.colorBrandBackground2,
    ':hover': {
      backgroundColor: tokens.colorBrandBackground2Hover,
    },
  },
  resultInfo: {
    display: 'flex',
    flexDirection: 'column',
    gap: '1px',
    minWidth: 0,
    flex: '1 1 auto',
  },
  resultName: {
    color: tokens.colorNeutralForeground1,
  },
  resultEmail: {
    color: tokens.colorNeutralForeground3,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  personIcon: {
    color: tokens.colorBrandForeground1,
    flexShrink: 0,
    display: 'flex',
    alignItems: 'center',
    marginRight: tokens.spacingHorizontalS,
  },

  // ── Empty / loading / error states ────────────────────────────────────
  stateMessage: {
    color: tokens.colorNeutralForeground3,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    textAlign: 'center',
  },

  // ── Selected contact chip ─────────────────────────────────────────────
  selectedChip: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalXS,
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: tokens.colorBrandBackground2,
    borderTopWidth: '1px',
    borderRightWidth: '1px',
    borderBottomWidth: '1px',
    borderLeftWidth: '1px',
    borderTopStyle: 'solid',
    borderRightStyle: 'solid',
    borderBottomStyle: 'solid',
    borderLeftStyle: 'solid',
    borderTopColor: tokens.colorBrandStroke1,
    borderRightColor: tokens.colorBrandStroke1,
    borderBottomColor: tokens.colorBrandStroke1,
    borderLeftColor: tokens.colorBrandStroke1,
    alignSelf: 'flex-start',
  },
  selectedChipName: {
    color: tokens.colorBrandForeground2,
  },

  spinnerRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
  },
});

// ---------------------------------------------------------------------------
// AssignCounselStep (exported)
// ---------------------------------------------------------------------------

export const AssignCounselStep: React.FC<IAssignCounselStepProps> = ({
  webApi,
  selectedContact,
  onContactChange,
}) => {
  const styles = useStyles();

  const [searchTerm, setSearchTerm] = React.useState('');
  const [results, setResults] = React.useState<IContact[]>([]);
  const [loading, setLoading] = React.useState(false);
  const [searchError, setSearchError] = React.useState<string | null>(null);
  const debounceRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);

  // ── Debounced search ──────────────────────────────────────────────────
  React.useEffect(() => {
    if (debounceRef.current) {
      clearTimeout(debounceRef.current);
    }

    if (searchTerm.trim().length < 2) {
      setResults([]);
      setSearchError(null);
      return;
    }

    debounceRef.current = setTimeout(async () => {
      setLoading(true);
      setSearchError(null);

      try {
        const contacts = await searchContacts(webApi, searchTerm.trim());
        setResults(contacts);
      } catch {
        setSearchError('Search failed. Please try again.');
        setResults([]);
      } finally {
        setLoading(false);
      }
    }, 400);

    return () => {
      if (debounceRef.current) {
        clearTimeout(debounceRef.current);
      }
    };
  }, [searchTerm, webApi]);

  const handleSearchChange = React.useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      setSearchTerm(e.target.value);
      // Clear selected contact when user re-types
      if (selectedContact) {
        onContactChange(null);
      }
    },
    [selectedContact, onContactChange]
  );

  const handleSelectContact = React.useCallback(
    (contact: IContact) => {
      onContactChange(contact);
      setSearchTerm(contact.sprk_name);
      setResults([]);
    },
    [onContactChange]
  );

  const handleClearContact = React.useCallback(() => {
    onContactChange(null);
    setSearchTerm('');
    setResults([]);
  }, [onContactChange]);

  // ── Render ─────────────────────────────────────────────────────────────
  const showResults =
    !loading && !selectedContact && results.length > 0 && searchTerm.trim().length >= 2;
  const showEmpty =
    !loading && !selectedContact && results.length === 0 && searchTerm.trim().length >= 2 && !searchError;

  return (
    <div className={styles.root}>
      {/* Header */}
      <div className={styles.headerText}>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Assign Counsel
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Search for a contact to assign as lead counsel for this matter.
          Type at least 2 characters to search.
        </Text>
      </div>

      {/* Search input */}
      <div className={styles.searchWrapper}>
        <Input
          value={searchTerm}
          onChange={handleSearchChange}
          placeholder="Search contacts by name..."
          contentBefore={<SearchRegular aria-hidden="true" />}
          aria-label="Search contacts"
          autoComplete="off"
        />

        {/* Loading spinner */}
        {loading && (
          <div className={styles.spinnerRow}>
            <Spinner size="tiny" label="Searching..." />
          </div>
        )}

        {/* Error */}
        {searchError && (
          <MessageBar intent="error">
            <MessageBarBody>{searchError}</MessageBarBody>
          </MessageBar>
        )}

        {/* Results list */}
        {showResults && (
          <div className={styles.resultsList} role="listbox" aria-label="Contact search results">
            {results.map((contact) => (
              <div
                key={contact.sprk_contactid}
                className={mergeClasses(
                  styles.resultItem,
                  (selectedContact as IContact | null)?.sprk_contactid === contact.sprk_contactid
                    ? styles.resultItemSelected
                    : undefined
                )}
                role="option"
                aria-selected={
                  (selectedContact as IContact | null)?.sprk_contactid === contact.sprk_contactid
                }
                tabIndex={0}
                onClick={() => handleSelectContact(contact)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    handleSelectContact(contact);
                  }
                }}
              >
                <span className={styles.personIcon} aria-hidden="true">
                  <PersonRegular fontSize={18} />
                </span>
                <div className={styles.resultInfo}>
                  <Text size={300} weight="semibold" className={styles.resultName}>
                    {contact.sprk_name}
                  </Text>
                  {contact.sprk_email && (
                    <Text size={100} className={styles.resultEmail}>
                      {contact.sprk_email}
                    </Text>
                  )}
                </div>
                <Button
                  appearance="subtle"
                  size="small"
                  onClick={(e) => {
                    e.stopPropagation();
                    handleSelectContact(contact);
                  }}
                  aria-label={`Select ${contact.sprk_name}`}
                >
                  Select
                </Button>
              </div>
            ))}
          </div>
        )}

        {/* Empty results */}
        {showEmpty && (
          <Text size={200} className={styles.stateMessage}>
            No contacts found matching &ldquo;{searchTerm}&rdquo;.
          </Text>
        )}
      </div>

      {/* Selected contact chip */}
      {selectedContact && (
        <div className={styles.selectedChip}>
          <span className={styles.personIcon} aria-hidden="true">
            <PersonRegular fontSize={16} />
          </span>
          <Text size={300} weight="semibold" className={styles.selectedChipName}>
            {selectedContact.sprk_name}
          </Text>
          {selectedContact.sprk_email && (
            <Text size={200} className={styles.selectedChipName}>
              &middot; {selectedContact.sprk_email}
            </Text>
          )}
          <Button
            appearance="subtle"
            size="small"
            icon={<DismissRegular fontSize={14} />}
            onClick={handleClearContact}
            aria-label={`Remove ${selectedContact.sprk_name}`}
          />
        </div>
      )}
    </div>
  );
};
