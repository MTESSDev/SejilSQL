// Aide à l'écriture du langage de requête de SejilSQL : « UserName = 'bob' && Duree != '0' ».

/** Le langage lit `nom opérateur valeur` avec un nom fait de lettres, chiffres et _. */
export function isFilterableName(name: string): boolean {
  return /^\w+$/.test(name);
}

/** Une valeur qui contient un opérateur logique serait coupée par l'analyseur du serveur : on ne propose pas de la filtrer. */
export function isFilterableValue(value: string | null): value is string {
  return value !== null && value.length > 0 && value.length <= 200 && !/[\r\n]/.test(value) && !/&&|\|\||\band\b|\bor\b/i.test(value);
}

/** Ajoute `nom op 'valeur'` à la requête, avec `&&`, en protégeant par des parenthèses une requête qui contient un « ou ». */
export function withCondition(query: string, name: string, op: '=' | '!=' | 'like' | 'not like', value: string): string {
  const condition = `${name} ${op} '${value}'`;
  const current = query.trim();
  if (!current) return condition;
  const base = /\|\||\bor\b/i.test(current) && !/^\(.*\)$/.test(current) ? `(${current})` : current;
  return `${base} && ${condition}`;
}
