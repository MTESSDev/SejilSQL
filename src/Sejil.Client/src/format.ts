// Les horodatages du serveur sont en heure locale, sans fuseau (« 2026-10-07T19:21:41.083 »).
// On les lit par leurs composantes plutôt que par `Date`, pour ne dépendre ni du fuseau du navigateur ni de l'heure d'été.

const TS = /^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2}):(\d{2})(?:\.(\d+))?/;

export interface Parts {
  y: number;
  mo: number;
  d: number;
  h: number;
  mi: number;
  s: number;
  /** Millisecondes, 0 à 999. */
  ms: number;
}

export function parseTimestamp(ts: string): Parts | null {
  const m = TS.exec(ts);
  if (!m) return null;
  return { y: +m[1], mo: +m[2], d: +m[3], h: +m[4], mi: +m[5], s: +m[6], ms: m[7] ? +m[7].padEnd(3, '0').slice(0, 3) : 0 };
}

const two = (n: number) => String(n).padStart(2, '0');

/** « 2026-10-07 », pour regrouper les événements par jour. */
export function dayKey(ts: string): string {
  const p = parseTimestamp(ts);
  return p ? `${p.y}-${two(p.mo)}-${two(p.d)}` : ts.slice(0, 10);
}

/** « 19:21:41.083 ». */
export function timeOf(ts: string): string {
  const p = parseTimestamp(ts);
  return p ? `${two(p.h)}:${two(p.mi)}:${two(p.s)}.${String(p.ms).padStart(3, '0')}` : ts;
}

/** « mercredi 7 octobre 2026 », ou « Aujourd'hui » / « Hier » grâce aux libellés fournis. */
export function dayLabel(key: string, locale: string, today: string, yesterday: string, now = new Date()): string {
  const [y, m, d] = key.split('-').map(Number);
  const here = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const diff = Math.round((here.getTime() - new Date(y, m - 1, d).getTime()) / 86_400_000);
  const long = new Intl.DateTimeFormat(locale, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }).format(new Date(y, m - 1, d));
  return diff === 0 ? `${today} · ${long}` : diff === 1 ? `${yesterday} · ${long}` : long;
}

/**
 * Borne supérieure (exclue) qui fige les pages : un millième de seconde après l'événement le plus récent déjà chargé.
 * Le serveur compare au millième de seconde, tronqué : sans ce décalage, l'événement lui-même serait exclu.
 */
export function snapshotAfter(ts: string): string | undefined {
  const p = parseTimestamp(ts);
  if (!p) return undefined;
  const t = new Date(Date.UTC(p.y, p.mo - 1, p.d, p.h, p.mi, p.s, p.ms + 1));
  return `${t.getUTCFullYear()}-${two(t.getUTCMonth() + 1)}-${two(t.getUTCDate())}T${two(t.getUTCHours())}:${two(t.getUTCMinutes())}:${two(t.getUTCSeconds())}.${String(t.getUTCMilliseconds()).padStart(3, '0')}`;
}

/** « aaaa-mm-jj » + n jours. */
export function addDays(date: string, n: number): string {
  const [y, m, d] = date.split('-').map(Number);
  const t = new Date(Date.UTC(y, m - 1, d + n));
  return `${t.getUTCFullYear()}-${two(t.getUTCMonth() + 1)}-${two(t.getUTCDate())}`;
}

/** Valeur JSON lisible (objets et tableaux envoyés par Serilog), sinon `null`. */
export function prettyJson(value: string | null): string | null {
  if (!value || !/^\s*[{[]/.test(value)) return null;
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return null;
  }
}
