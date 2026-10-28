/**
 * Test doubles for the one "Add person" lookup (task 193, #2338): the host's `pickPerson` answers a contact or a
 * system user. These wrap a test's contact or user pick in that answer (`null`, a cancelled lookup, stays `null`).
 */

import type { IContactSearchResult, IPersonPick, IUserPick } from '../types';

export function personFromUser(user: IUserPick | null): IPersonPick | null {
  return user ? { kind: 'user', user } : null;
}

export function personFromContact(contact: IContactSearchResult | null): IPersonPick | null {
  return contact ? { kind: 'contact', contact } : null;
}
