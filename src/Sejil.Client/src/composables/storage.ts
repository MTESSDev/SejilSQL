// Lecture et écriture dans le stockage du navigateur, sans jamais échouer :
// en navigation privée ou avec une politique restrictive, les préférences valent pour la visite en cours seulement.

export function readStored(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

export function writeStored(key: string, value: string) {
  try {
    localStorage.setItem(key, value);
  } catch {
    // le choix vaut pour cette visite seulement
  }
}
