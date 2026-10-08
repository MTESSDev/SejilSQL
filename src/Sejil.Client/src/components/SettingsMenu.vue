<script setup lang="ts">
import { changeMinLevel, state } from '../composables/store';
import { setTheme, theme } from '../composables/theme';
import type { ThemeMode } from '../composables/theme';
import { dictionary, t } from '../i18n';
import { LEVELS } from '../types';
import Icon from './Icon.vue';
import Popover from './Popover.vue';

const themes: { mode: ThemeMode; label: string }[] = [
  { mode: 'auto', label: t('themeAuto') },
  { mode: 'light', label: t('themeLight') },
  { mode: 'dark', label: t('themeDark') },
];

function onLevel(event: Event) {
  void changeMinLevel((event.target as HTMLSelectElement).value);
}
</script>

<template>
  <Popover :label="t('settings')" align="right">
    <template #trigger="{ toggle, open }">
      <button class="btn ghost icon" :aria-label="t('settings')" :title="t('settings')" :aria-expanded="open" @click="toggle">
        <Icon name="sliders" />
      </button>
    </template>

    <div class="settings">
      <h3>{{ t('theme') }}</h3>
      <div class="seg" role="radiogroup" :aria-label="t('theme')">
        <button v-for="item in themes" :key="item.mode" role="radio" :aria-checked="theme === item.mode" :class="{ on: theme === item.mode }" @click="setTheme(item.mode)">
          <Icon v-if="item.mode === 'light'" name="sun" />
          <Icon v-else-if="item.mode === 'dark'" name="moon" />
          {{ item.label }}
        </button>
      </div>

      <h3 style="margin-top: 16px">{{ t('minLevel') }}</h3>
      <select class="field" style="width: 100%" :value="state.minLevel" :aria-label="t('minLevel')" @change="onLevel">
        <option v-if="state.minLevel && !LEVELS.includes(state.minLevel as never)" :value="state.minLevel">{{ state.minLevel }}</option>
        <option v-for="level in LEVELS" :key="level" :value="level">{{ dictionary.levels[level] }}</option>
      </select>
      <p class="hint">{{ t('minLevelHint') }}</p>
    </div>
  </Popover>
</template>
