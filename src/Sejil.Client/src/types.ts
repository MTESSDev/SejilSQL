export const LEVELS = ['Verbose', 'Debug', 'Information', 'Warning', 'Error', 'Fatal'] as const;
export type LevelName = (typeof LEVELS)[number];

export const PERIODS = ['5m', '15m', '1h', '6h', '12h', '24h', '2d', '5d'] as const;
export type Period = (typeof PERIODS)[number];

export interface LogProperty {
  name: string;
  value: string | null;
}

export interface LogEntry {
  id: number;
  message: string;
  sourceApp: string;
  level: LevelName;
  /** Heure locale du serveur, sans fuseau : « 2026-10-07T19:21:41.083 ». */
  timestamp: string;
  exception: string | null;
  properties: LogProperty[];
}

export interface SavedQuery {
  name: string;
  query: string;
}

/** Bornes incluses, au format « aaaa-mm-jj ». */
export interface DateRange {
  from: string;
  to: string;
}

export interface Filters {
  query: string;
  period: Period | null;
  range: DateRange | null;
  level: LevelName | null;
  exceptionsOnly: boolean;
}
