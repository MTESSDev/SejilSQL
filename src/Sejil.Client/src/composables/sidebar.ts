import { ref } from 'vue';
import { readStored, writeStored } from './storage';

// Le panneau « Requêtes enregistrées » peut être replié; le choix est gardé sur le poste, dans le navigateur.
const KEY = 'sejil-sidebar-collapsed';

export const sidebarCollapsed = ref(readStored(KEY) === '1');

export function setSidebarCollapsed(value: boolean) {
  sidebarCollapsed.value = value;
  writeStored(KEY, value ? '1' : '0');
}

export function toggleSidebar() {
  setSidebarCollapsed(!sidebarCollapsed.value);
}
