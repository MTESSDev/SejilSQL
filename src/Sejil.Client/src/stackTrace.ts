// Découpe le texte d'une exception .NET (`exception.ToString()`) en lignes affichables : en-têtes (type + message),
// appels de la pile, séparateurs. Le texte n'est jamais modifié : ce qu'on ne reconnaît pas est affiché tel quel.

export interface ExceptionHead {
  kind: 'head';
  /** Vrai pour une exception interne (« ---> »). */
  inner: boolean;
  /** « System.InvalidOperationException »; vide si le texte ne commence pas par un type. */
  type: string;
  message: string;
}

export interface ExceptionFrame {
  kind: 'frame';
  /** Numéro dans la pile, à partir de 1, repart à zéro à chaque en-tête. */
  num: number;
  /** « OIM.Maestro.Services.TransmissionService. » (point final inclus). */
  ns: string;
  method: string;
  args: string;
  /** Nom du fichier source seulement; vide si inconnu. */
  file: string;
  /** Chemin complet du fichier source (pour l'infobulle). */
  path: string;
  line: string;
  /** Appel du framework : aucune position dans le code source et un espace de noms connu. */
  framework: boolean;
}

export interface ExceptionSeparator {
  kind: 'sep';
  text: string;
}

export interface ExceptionText {
  kind: 'text';
  text: string;
}

export type ExceptionItem = ExceptionHead | ExceptionFrame | ExceptionSeparator | ExceptionText;

/** Une série d'appels du framework repliée sous un seul bouton. */
export interface ExceptionFold {
  kind: 'fold';
  /** Position du premier appel de la série dans la liste d'origine : identifie la série. */
  id: number;
  frames: ExceptionFrame[];
}

export type ExceptionRow = ExceptionItem | ExceptionFold;

// « at Ns.Classe.Methode(Int32 a, String b) in C:\x\Fichier.cs:line 12 », ou en français « à … dans …:ligne 12 ».
const FRAME = /^\s*(?:at|à)\s+([^\s(]+)\((.*?)\)(?:\s+(?:in|dans)\s+(.+?):(?:line|ligne)\s+(\d+))?\s*$/;
// « --- End of inner exception stack trace --- » (mais pas « ---> », qui ouvre une exception interne).
const SEPARATOR = /^\s*---(?!>).*---\s*$/;
const INNER = /^\s*--->\s?(.*)$/;
const INNER_LABEL = /^\(Inner Exception #\d+\)\s*/;
// « System.Foo.BarException », éventuellement suivi du code HRESULT que SQL Server ajoute : « SqlException (0x80131904) ».
const TYPE = /(?:[\w`+]+(?:\.[\w`+]+)+|\w*(?:Exception|Error))(?: \(0x[0-9A-Fa-f]+\))?/.source;
const TYPE_AND_MESSAGE = new RegExp(`^(${TYPE})\\s*:\\s?([\\s\\S]*)$`);
const TYPE_ONLY = new RegExp(`^(${TYPE})$`);
const FRAMEWORK = /^(System|Microsoft|Windows|Newtonsoft|Serilog)\./;

function parseHead(text: string, inner: boolean): ExceptionHead {
  const s = text.replace(INNER_LABEL, '');
  const typed = TYPE_AND_MESSAGE.exec(s);
  if (typed) return { kind: 'head', inner, type: typed[1], message: typed[2] };
  const only = TYPE_ONLY.exec(s.trim());
  if (only) return { kind: 'head', inner, type: only[1], message: '' };
  return { kind: 'head', inner, type: '', message: s };
}

function parseFrame(match: RegExpExecArray, num: number): ExceptionFrame {
  const [, full, args, location = '', line = ''] = match;
  const dot = full.lastIndexOf('.');
  const ns = dot < 0 ? '' : full.slice(0, dot + 1);
  const method = dot < 0 ? full : full.slice(dot + 1);
  const known = location !== '' && !location.startsWith('<');
  const file = known ? location.split(/[\\/]/).pop() ?? location : '';
  return {
    kind: 'frame',
    num,
    ns,
    method,
    args,
    file,
    path: known ? location : '',
    line: known ? line : '',
    framework: !known && FRAMEWORK.test(full),
  };
}

export function parseException(text: string): ExceptionItem[] {
  const items: ExceptionItem[] = [];
  let inHeader = true;
  let num = 0;

  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trimEnd();
    if (items.length === 0) {
      items.push(parseHead(line, false));
      continue;
    }
    const inner = INNER.exec(line);
    if (inner) {
      items.push(parseHead(inner[1], true));
      inHeader = true;
      num = 0;
      continue;
    }
    const frame = FRAME.exec(line);
    if (frame) {
      items.push(parseFrame(frame, ++num));
      inHeader = false;
      continue;
    }
    if (SEPARATOR.test(line)) {
      items.push({ kind: 'sep', text: line.trim() });
      inHeader = false;
      continue;
    }
    const last = items[items.length - 1];
    if (inHeader && last.kind === 'head') {
      // suite d'un message sur plusieurs lignes
      last.message += `\n${line}`;
    } else if (line.trim() !== '') {
      items.push({ kind: 'text', text: line });
    }
  }

  const first = items[0];
  if (first.kind === 'head') first.message = first.message.trimEnd();
  for (const item of items) if (item.kind === 'head') item.message = item.message.trimEnd();
  return items;
}

/**
 * Prépare les lignes à afficher. Quand `hideFramework` est vrai, chaque série d'appels du framework devient une
 * seule ligne repliée, sauf si son identifiant est dans `expanded`.
 */
export function foldFramework(items: ExceptionItem[], hideFramework: boolean, expanded: ReadonlySet<number>): ExceptionRow[] {
  if (!hideFramework) return items;
  const rows: ExceptionRow[] = [];
  let run: ExceptionFold | null = null;
  items.forEach((item, index) => {
    if (item.kind === 'frame' && item.framework && !expanded.has(runStart(items, index))) {
      if (!run) rows.push((run = { kind: 'fold', id: index, frames: [] }));
      run.frames.push(item);
      return;
    }
    run = null;
    rows.push(item);
  });
  return rows;
}

/** Position du premier appel de la série d'appels du framework qui contient `index`. */
function runStart(items: ExceptionItem[], index: number): number {
  let start = index;
  while (start > 0) {
    const before = items[start - 1];
    if (before.kind !== 'frame' || !before.framework) break;
    start--;
  }
  return start;
}

export const hasFramework = (items: ExceptionItem[]) => items.some(i => i.kind === 'frame' && i.framework);
export const hasAppFrames = (items: ExceptionItem[]) => items.some(i => i.kind === 'frame' && !i.framework);
