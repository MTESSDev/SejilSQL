<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { loadMore, search, select, setAutoRefresh, state } from '../composables/store';
import { dayKey, dayLabel, timeOf } from '../format';
import { locale, t } from '../i18n';
import type { LogEntry } from '../types';
import Icon from './Icon.vue';
import LevelChip from './LevelChip.vue';

const scroller = ref<HTMLElement | null>(null);
const sentinel = ref<HTMLElement | null>(null);
let observer: IntersectionObserver | undefined;

const MARGIN = 400;

// La fin de la liste est-elle (presque) à l'écran ? On mesure au moment de décider : un indicateur mis à jour par
// l'observateur arrive en retard après un changement de liste et chargerait des pages dont personne n'a besoin.
function sentinelIsNear(): boolean {
  if (!scroller.value || !sentinel.value) return false;
  return sentinel.value.getBoundingClientRect().top <= scroller.value.getBoundingClientRect().bottom + MARGIN;
}

const intervals = [0, 5, 10, 30, 60];

const groups = computed(() => {
  const result: { day: string; label: string; items: LogEntry[] }[] = [];
  for (const entry of state.entries) {
    const day = dayKey(entry.timestamp);
    const last = result[result.length - 1];
    if (last?.day === day) last.items.push(entry);
    else result.push({ day, label: dayLabel(day, locale, t('today'), t('yesterday')), items: [entry] });
  }
  return result;
});

function onInterval(event: Event) {
  setAutoRefresh(Number((event.target as HTMLSelectElement).value));
}

function showNewest() {
  state.newCount = 0;
  scroller.value?.scrollTo({ top: 0, behavior: 'smooth' });
}

onMounted(() => {
  observer = new IntersectionObserver(
    entries => {
      if (entries.some(e => e.isIntersecting)) void loadMore();
    },
    { root: scroller.value, rootMargin: `${MARGIN}px 0px` },
  );
  if (sentinel.value) observer.observe(sentinel.value);
});
onBeforeUnmount(() => observer?.disconnect());

// Si la fin de la liste est encore à l'écran après un chargement (écran haut, peu de résultats), on continue.
watch(
  () => [state.entries.length, state.loading] as const,
  async ([count]) => {
    await nextTick();
    if (count === 0) scroller.value?.scrollTo({ top: 0 });   // nouvelle recherche : on repart du haut
    else if (sentinelIsNear() && !state.loading && !state.exhausted && !state.error) void loadMore();
  },
);
</script>

<template>
  <section class="events" aria-label="Événements">
    <div class="events-bar">
      <span class="count" aria-live="polite">{{ t('events', { n: state.entries.length }) }}<template v-if="!state.exhausted && state.entries.length">+</template></span>
      <button v-if="state.newCount" class="pill" @click="showNewest">{{ t('newEvents', { n: state.newCount }) }}</button>
      <span class="spacer" />
      <label class="auto">
        <span>{{ t('autoRefresh') }}</span>
        <select class="field" :value="state.autoRefresh" @change="onInterval">
          <option v-for="seconds in intervals" :key="seconds" :value="seconds">{{ seconds ? t('everyNSeconds', { n: seconds }) : t('off') }}</option>
        </select>
      </label>
      <button class="btn ghost icon" :class="{ spin: state.loading }" :aria-label="t('refresh')" :title="t('refresh')" @click="search()"><Icon name="refresh" /></button>
    </div>

    <div ref="scroller" class="events-scroll">
      <div v-if="state.error" class="notice error" role="alert">
        <div>
          <strong>{{ t('loadError') }}</strong>
          <p>{{ state.error }}</p>
        </div>
        <button class="btn" @click="state.entries.length ? loadMore() : search()">{{ t('retry') }}</button>
      </div>

      <template v-for="group in groups" :key="group.day">
        <h2 class="day">{{ group.label }}</h2>
        <ul class="rows">
          <li v-for="entry in group.items" :key="entry.id">
            <button class="row" :class="[`lv-${entry.level.toLowerCase()}`, { selected: entry.id === state.selectedId }]" :aria-pressed="entry.id === state.selectedId" @click="select(entry.id === state.selectedId ? null : entry.id)">
              <time>{{ timeOf(entry.timestamp) }}</time>
              <LevelChip :level="entry.level" />
              <span class="app">{{ entry.sourceApp }}</span>
              <span class="msg">{{ entry.message }}</span>
              <Icon v-if="entry.exception" name="alert" class="exc" :title="t('exception')" />
            </button>
          </li>
        </ul>
      </template>

      <div v-if="!state.loading && !state.error && !state.entries.length" class="empty-state">
        <svg viewBox="0 0 64 64" width="64" height="64" aria-hidden="true">
          <rect x="8" y="12" width="48" height="40" rx="8" fill="var(--surface-2)" stroke="var(--border-strong)" stroke-width="2" />
          <path d="M18 24h28M18 32h18M18 40h22" stroke="var(--border-strong)" stroke-width="3" stroke-linecap="round" />
        </svg>
        <strong>{{ t('noResults') }}</strong>
        <p>{{ t('noResultsHint') }}</p>
      </div>

      <div ref="sentinel" class="sentinel">
        <span v-if="state.loading">{{ t('loading') }}</span>
        <span v-else-if="state.entries.length && state.exhausted">{{ t('endOfResults') }}</span>
        <button v-else-if="state.entries.length && !state.error" class="btn" @click="loadMore()">{{ t('loadMore') }}</button>
      </div>
    </div>
  </section>
</template>
