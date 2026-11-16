import { describe, expect, it } from 'vitest';
import { foldFramework, hasAppFrames, hasFramework, parseException } from './stackTrace';

const SAMPLE = [
  'System.InvalidOperationException: Impossible de transmettre le dossier.',
  ' ---> System.Net.Http.HttpRequestException: Response status code does not indicate success: 502 (Bad Gateway).',
  '   at System.Net.Http.HttpResponseMessage.EnsureSuccessStatusCode()',
  '   at OIM.Maestro.Services.TransmissionService.EnvoyerAsync(Dossier dossier, CancellationToken ct) in C:\\src\\Maestro\\Services\\TransmissionService.cs:line 118',
  '   --- End of inner exception stack trace ---',
  '   at OIM.Maestro.Controllers.DossiersController.Transmettre(Int32 id) in C:\\src\\Maestro\\Controllers\\DossiersController.cs:line 52',
  '   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.Execute(...)',
  '   at Microsoft.AspNetCore.Routing.EndpointMiddleware.<Invoke>g__AwaitRequestTask|7_0(...)',
].join('\n');

describe('parseException', () => {
  const items = parseException(SAMPLE);

  it('reconnaît les en-têtes, y compris l’exception interne', () => {
    const heads = items.filter(i => i.kind === 'head');
    expect(heads).toHaveLength(2);
    expect(heads[0]).toMatchObject({ inner: false, type: 'System.InvalidOperationException', message: 'Impossible de transmettre le dossier.' });
    expect(heads[1]).toMatchObject({ inner: true, type: 'System.Net.Http.HttpRequestException' });
  });

  it('découpe un appel avec sa position dans le code source', () => {
    const frame = items.find(i => i.kind === 'frame' && i.method === 'EnvoyerAsync');
    expect(frame).toMatchObject({
      ns: 'OIM.Maestro.Services.TransmissionService.',
      args: 'Dossier dossier, CancellationToken ct',
      file: 'TransmissionService.cs',
      line: '118',
      framework: false,
    });
  });

  it('distingue le framework du code de l’application', () => {
    const frameworkMethods = items.filter(i => i.kind === 'frame' && i.framework).map(i => (i.kind === 'frame' ? i.method : ''));
    expect(frameworkMethods).toEqual(['EnsureSuccessStatusCode', 'Execute', '<Invoke>g__AwaitRequestTask|7_0']);
    expect(hasFramework(items)).toBe(true);
    expect(hasAppFrames(items)).toBe(true);
  });

  it('garde le séparateur, et numérote les appels à partir de 1 après chaque en-tête', () => {
    expect(items.filter(i => i.kind === 'sep')).toHaveLength(1);
    const nums = items.filter(i => i.kind === 'frame').map(i => (i.kind === 'frame' ? i.num : 0));
    expect(nums).toEqual([1, 2, 3, 4, 5]);
  });

  it('lit les piles en français', () => {
    const [, frame] = parseException('System.Exception: x\n   à Mon.Ns.Classe.Methode(Int32 a) dans C:\\a\\B.cs:ligne 7');
    expect(frame).toMatchObject({ kind: 'frame', method: 'Methode', file: 'B.cs', line: '7' });
  });

  it('ajoute les lignes qui suivent l’en-tête au message', () => {
    const [head, frame] = parseException('System.Exception: première ligne\nseconde ligne\n   at A.B.C()');
    expect(head).toMatchObject({ type: 'System.Exception', message: 'première ligne\nseconde ligne' });
    expect(frame).toMatchObject({ kind: 'frame', method: 'C' });
  });

  it('garde le code HRESULT de SQL Server dans le type', () => {
    const [head] = parseException('Microsoft.Data.SqlClient.SqlException (0x80131904): Violation de la contrainte PRIMARY KEY.\n   at A.B.C()');
    expect(head).toMatchObject({ type: 'Microsoft.Data.SqlClient.SqlException (0x80131904)', message: 'Violation de la contrainte PRIMARY KEY.' });
  });

  it('ne prend pas une phrase qui commence par « at » pour un appel', () => {
    const [head] = parseException('Erreur inattendue\nat least one value is required');
    expect(head).toMatchObject({ type: '', message: 'Erreur inattendue\nat least one value is required' });
  });

  it('affiche tel quel un texte sans type', () => {
    expect(parseException('quelque chose de brisé')).toEqual([{ kind: 'head', inner: false, type: '', message: 'quelque chose de brisé' }]);
  });
});

describe('foldFramework', () => {
  const items = parseException(SAMPLE);

  it('ne touche à rien quand le masquage est désactivé', () => {
    expect(foldFramework(items, false, new Set())).toBe(items);
  });

  it('replie chaque série d’appels du framework', () => {
    const rows = foldFramework(items, true, new Set());
    const folds = rows.filter(r => r.kind === 'fold');
    expect(folds.map(f => (f.kind === 'fold' ? f.frames.length : 0))).toEqual([1, 2]);
    expect(rows.some(r => r.kind === 'frame' && r.framework)).toBe(false);
  });

  it('redéploie la série demandée seulement', () => {
    const rows = foldFramework(items, true, new Set([foldIdOf(items, 'Execute')]));
    expect(rows.filter(r => r.kind === 'fold')).toHaveLength(1);
    expect(rows.filter(r => r.kind === 'frame' && r.framework)).toHaveLength(2);
  });
});

function foldIdOf(items: ReturnType<typeof parseException>, method: string): number {
  const rows = foldFramework(items, true, new Set());
  const fold = rows.find(r => r.kind === 'fold' && r.frames.some(f => f.method === method));
  if (!fold || fold.kind !== 'fold') throw new Error('série introuvable');
  return fold.id;
}
