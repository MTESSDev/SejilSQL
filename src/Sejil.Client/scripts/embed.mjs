// Copie le build (un seul fichier) dans la bibliothèque, où il est embarqué comme ressource.
import { copyFileSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const racine = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const source = resolve(racine, 'dist', 'index.html');
const cible = resolve(racine, '..', 'Sejil.Server', 'index.html');

copyFileSync(source, cible);
console.log(`index.html (${(statSync(cible).size / 1024).toFixed(0)} Ko) copié vers ${cible}`);
