import { ref } from 'vue';

export type ThemeMode = 'auto' | 'light' | 'dark';

const KEY = 'sejil-theme';

function load(): ThemeMode {
  try {
    const value = localStorage.getItem(KEY);
    if (value === 'light' || value === 'dark' || value === 'auto') return value;
  } catch {
    // stockage indisponible (navigation privée, politique du navigateur) : thème automatique
  }
  return 'auto';
}

export const theme = ref<ThemeMode>(load());

function apply() {
  const root = document.documentElement;
  if (theme.value === 'auto') root.removeAttribute('data-theme');
  else root.setAttribute('data-theme', theme.value);
}

export function setTheme(mode: ThemeMode) {
  theme.value = mode;
  try {
    localStorage.setItem(KEY, mode);
  } catch {
    // le choix vaut pour cette visite seulement
  }
  apply();
}

apply();
