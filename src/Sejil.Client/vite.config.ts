import { defineConfig } from 'vitest/config';
import vue from '@vitejs/plugin-vue';
import { viteSingleFile } from 'vite-plugin-singlefile';

// Le build produit UN seul fichier index.html (script et styles inclus) : c'est lui qui est embarqué dans SejilSQL.dll.
// En développement, `SEJIL_URL` pointe vers un site qui héberge SejilSQL (ex. SampleBasic : http://localhost:5100/sejil) :
// l'interface est servie à la racine par Vite et ses appels sont redirigés vers ce site.
const cible = process.env.SEJIL_URL ?? 'http://localhost:5100/sejil';
const { origin, pathname } = new URL(cible);
const routes = ['/events', '/log-query', '/log-queries', '/del-query', '/min-log-level', '/user-name', '/title'];

export default defineConfig({
  plugins: [vue(), viteSingleFile()],
  build: { target: 'es2020', chunkSizeWarningLimit: 600 },
  server: {
    port: 5174,
    proxy: Object.fromEntries(routes.map(r => [r, { target: origin, changeOrigin: true, rewrite: (p: string) => pathname.replace(/\/$/, '') + p }])),
  },
  test: { environment: 'node' },
});
