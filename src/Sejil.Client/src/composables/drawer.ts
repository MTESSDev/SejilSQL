import { ref, watch } from 'vue';
import { sidebarCollapsed } from './sidebar';
import { readStored as read, writeStored as write } from './storage';
import { selectedEntry } from './store';

// Préférences d'affichage du panneau de détails, mémorisées dans le navigateur.
const WIDTH_KEY = 'sejil-drawer-width';
const WRAP_KEY = 'sejil-exception-wrap';
const FRAMEWORK_KEY = 'sejil-exception-hide-framework';

export const MIN_WIDTH = 380;
/** Place laissée à la liste des événements quand le panneau est à sa largeur maximale (grands écrans). */
const MIN_LIST = 380;
const SIDEBAR = 276;
/** Largeur du ruban qui reste quand le panneau latéral est replié. */
const SIDEBAR_RAIL = 48;
/** En dessous de cette largeur d'écran, le panneau passe par-dessus la liste (voir layout.css). */
const OVERLAY_BELOW = 1280;

function loadWidth(): number | null {
  const n = Number(read(WIDTH_KEY));
  return Number.isFinite(n) && n >= MIN_WIDTH ? n : null;
}

/** Largeur choisie en pixels, ou `null` pour la largeur par défaut (voir `--drawer-w` dans layout.css). */
export const drawerWidth = ref<number | null>(loadWidth());
/** Lecture plein écran : le panneau prend presque toute la place. Revient à la normale à la fermeture. */
export const drawerExpanded = ref(false);
/** Vrai pendant que la personne tire la bordure du panneau. */
export const drawerResizing = ref(false);
export const wrapLines = ref(read(WRAP_KEY) !== '0');
export const hideFramework = ref(read(FRAMEWORK_KEY) === '1');

export function maxDrawerWidth(viewport = window.innerWidth): number {
  const sidebar = sidebarCollapsed.value ? SIDEBAR_RAIL : SIDEBAR;
  return Math.max(MIN_WIDTH, viewport < OVERLAY_BELOW ? viewport : viewport - sidebar - MIN_LIST);
}

export function setDrawerWidth(width: number | null) {
  if (width === null) {
    drawerWidth.value = null;
    write(WIDTH_KEY, '');
    return;
  }
  drawerWidth.value = Math.round(Math.min(maxDrawerWidth(), Math.max(MIN_WIDTH, width)));
}

export function saveDrawerWidth() {
  if (drawerWidth.value !== null) write(WIDTH_KEY, String(drawerWidth.value));
}

export function toggleExpanded() {
  drawerExpanded.value = !drawerExpanded.value;
}

export function setWrapLines(value: boolean) {
  wrapLines.value = value;
  write(WRAP_KEY, value ? '1' : '0');
}

export function setHideFramework(value: boolean) {
  hideFramework.value = value;
  write(FRAMEWORK_KEY, value ? '1' : '0');
}

watch(selectedEntry, entry => {
  if (!entry) drawerExpanded.value = false;
});
