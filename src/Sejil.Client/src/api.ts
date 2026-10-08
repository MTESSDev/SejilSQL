import type { Filters, LogEntry, SavedQuery } from './types';
import { addDays } from './format';

/** Le site sert l'interface à l'adresse de SejilSQL (« /logs ») : ses routes sont juste en dessous. */
const root = location.pathname.replace(/\/+$/, '');

export const PAGE_SIZE = 100;

export class HttpError extends Error {
  constructor(public status: number, statusText: string) {
    super(statusText || `HTTP ${status}`);
  }
}

async function call<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(root + path, init);
  if (!response.ok) throw new HttpError(response.status, response.statusText);
  return (await response.json()) as T;
}

async function send(path: string, body: string): Promise<void> {
  const response = await fetch(root + path, { method: 'POST', body });
  if (!response.ok) throw new HttpError(response.status, response.statusText);
}

export const api = {
  events(filters: Filters, page: number, startingTs?: string): Promise<LogEntry[]> {
    const params = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) });
    if (startingTs) params.set('startingTs', startingTs);
    return call<LogEntry[]>(`/events?${params}`, {
      method: 'POST',
      body: JSON.stringify({
        queryText: filters.query,
        dateFilter: filters.period,
        // le serveur exclut la borne de fin : on envoie le lendemain pour que le dernier jour choisi soit inclus
        dateRangeFilter: filters.range ? [filters.range.from, addDays(filters.range.to, 1)] : null,
        levelFilter: filters.level,
        exceptionsOnly: filters.exceptionsOnly,
      }),
    });
  },
  queries: () => call<SavedQuery[]>('/log-queries'),
  saveQuery: (q: SavedQuery) => send('/log-query', JSON.stringify(q)),
  deleteQuery: (name: string) => send('/del-query', name),
  minLevel: () => call<{ minimumLogLevel: string }>('/min-log-level'),
  setMinLevel: (level: string) => send('/min-log-level', level),
  userName: () => call<{ userName: string }>('/user-name'),
  title: () => call<{ title: string }>('/title'),
};
