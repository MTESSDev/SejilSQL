<script setup lang="ts">
import { hasActiveFilters, resetFilters, setExceptionsOnly, setLevel, setPeriod, setRange, state } from '../composables/store';
import { dictionary, t } from '../i18n';
import { LEVELS, PERIODS } from '../types';

function onRange(which: 'from' | 'to', event: Event) {
  const value = (event.target as HTMLInputElement).value;
  let from = which === 'from' ? value : (state.filters.range?.from ?? '');
  let to = which === 'to' ? value : (state.filters.range?.to ?? '');
  if (!from && !to) return void setRange(null);
  from ||= to;
  to ||= from;
  if (from > to) [from, to] = [to, from];
  void setRange({ from, to });
}
</script>

<template>
  <section class="filters" aria-label="Filtres">
    <div class="group">
      <span class="group-label">{{ t('period') }}</span>
      <div class="seg" role="group" :aria-label="t('period')">
        <button :class="{ on: !state.filters.period && !state.filters.range }" :aria-pressed="!state.filters.period && !state.filters.range" @click="setPeriod(null)">{{ t('all') }}</button>
        <button v-for="period in PERIODS" :key="period" :class="{ on: state.filters.period === period }" :aria-pressed="state.filters.period === period" @click="setPeriod(period)">
          {{ dictionary.periods[period] }}
        </button>
      </div>
      <label class="date">
        <span>{{ t('from') }}</span>
        <input class="field" type="date" :value="state.filters.range?.from ?? ''" @change="onRange('from', $event)" />
      </label>
      <label class="date">
        <span>{{ t('to') }}</span>
        <input class="field" type="date" :value="state.filters.range?.to ?? ''" @change="onRange('to', $event)" />
      </label>
    </div>

    <div class="group">
      <span class="group-label">{{ t('level') }}</span>
      <div class="levels" role="group" :aria-label="t('level')">
        <button v-for="level in LEVELS" :key="level" class="level-btn" :class="[`lv-${level.toLowerCase()}`, { on: state.filters.level === level }]" :aria-pressed="state.filters.level === level" @click="setLevel(level)">
          <span class="dot" />{{ dictionary.levels[level] }}
        </button>
      </div>
      <div class="extras">
        <label class="toggle">
          <input type="checkbox" :checked="state.filters.exceptionsOnly" @change="setExceptionsOnly(($event.target as HTMLInputElement).checked)" />
          <span class="track" aria-hidden="true" />
          {{ t('exceptionsOnly') }}
        </label>
        <!-- toujours présent, seulement inactif : le faire apparaître au premier filtre décalerait toute la barre -->
        <button class="btn ghost" :disabled="!hasActiveFilters" @click="resetFilters()">{{ t('reset') }}</button>
      </div>
    </div>
  </section>
</template>
