<script setup lang="ts">
import { ref } from 'vue';
import { saveCurrentQuery, search, state } from '../composables/store';
import { t } from '../i18n';
import Icon from './Icon.vue';
import Popover from './Popover.vue';
import SettingsMenu from './SettingsMenu.vue';

const emit = defineEmits<{ (e: 'menu'): void }>();

const input = ref<HTMLInputElement | null>(null);
const name = ref('');

const examples = [
  [t('helpEx1'), t('helpEx1d')],
  [t('helpEx2'), t('helpEx2d')],
  [t('helpEx3'), t('helpEx3d')],
  [t('helpEx4'), t('helpEx4d')],
] as const;

defineExpose({ focus: () => input.value?.focus() });

async function save(close: () => void) {
  const value = name.value.trim();
  if (value && (await saveCurrentQuery(value))) {
    name.value = '';
    close();
  }
}
</script>

<template>
  <header class="topbar">
    <button class="btn ghost icon only-narrow" :aria-label="t('menu')" @click="emit('menu')"><Icon name="menu" /></button>

    <div class="brand">
      <svg viewBox="0 0 32 32" width="28" height="28" aria-hidden="true">
        <rect width="32" height="32" rx="8" fill="var(--accent)" />
        <path d="M8 10h16M8 16h11M8 22h14" stroke="var(--on-accent)" stroke-width="2.6" stroke-linecap="round" />
      </svg>
      <span class="brand-name">{{ state.title }}</span>
    </div>

    <form class="searchbar" role="search" @submit.prevent="search()">
      <Icon name="search" />
      <input ref="input" v-model="state.filters.query" type="text" spellcheck="false" autocomplete="off" :placeholder="t('searchPlaceholder')" :aria-label="t('search')" />
      <Popover :label="t('helpTitle')">
        <template #trigger="{ toggle }">
          <button type="button" class="btn ghost icon" style="height: 28px; width: 28px" :aria-label="t('help')" :title="t('help')" @click="toggle"><Icon name="help" /></button>
        </template>
        <template #default="{ close }">
          <div class="help">
            <h3>{{ t('helpTitle') }}</h3>
            <p class="hint" style="margin-top: 0">{{ t('helpIntro') }}</p>
            <ul>
              <li v-for="[example, description] in examples" :key="example">
                <button type="button" @click="((state.filters.query = example), close(), input?.focus())">
                  <code>{{ example }}</code>
                  <span>{{ description }}</span>
                </button>
              </li>
            </ul>
          </div>
        </template>
      </Popover>
      <button class="btn primary" type="submit">{{ t('search') }}</button>
    </form>

    <Popover :label="t('saveQueryTitle')" align="right">
      <template #trigger="{ toggle }">
        <button class="btn" :disabled="!state.filters.query.trim()" :title="t('saveQueryTitle')" @click="toggle">
          <Icon name="bookmark" /><span class="hide-narrow">{{ t('saveQuery') }}</span>
        </button>
      </template>
      <template #default="{ close }">
        <form class="save" @submit.prevent="save(close)">
          <h3>{{ t('saveQueryTitle') }}</h3>
          <code class="query-preview">{{ state.filters.query }}</code>
          <label class="sr-only" for="query-name">{{ t('queryName') }}</label>
          <input id="query-name" v-model="name" class="field" style="width: 100%" maxlength="255" :placeholder="t('queryName')" autofocus />
          <div class="row-end">
            <button type="button" class="btn ghost" @click="close()">{{ t('cancel') }}</button>
            <button type="submit" class="btn primary" :disabled="!name.trim()">{{ t('save') }}</button>
          </div>
        </form>
      </template>
    </Popover>

    <span class="spacer" />
    <SettingsMenu />
    <span v-if="state.userName" class="user" :title="state.userName"><Icon name="user" /><span class="hide-narrow">{{ state.userName }}</span></span>
  </header>
</template>
