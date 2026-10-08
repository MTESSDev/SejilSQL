<script setup lang="ts">
import { ref } from 'vue';
import { addCondition, applications, deleteSavedQuery, runQuery, state } from '../composables/store';
import { t } from '../i18n';
import Icon from './Icon.vue';

defineProps<{ open: boolean }>();
const emit = defineEmits<{ (e: 'close'): void }>();

const confirming = ref<string | null>(null);

function load(query: string) {
  void runQuery(query);
  emit('close');
}

function filterApp(name: string) {
  void addCondition('sourceApp', '=', name);
  emit('close');
}
</script>

<template>
  <div v-if="open" class="scrim" @click="emit('close')" />
  <aside class="sidebar" :class="{ open }" :aria-label="t('savedQueries')">
    <section>
      <h2>{{ t('savedQueries') }}</h2>
      <p v-if="!state.queries.length" class="empty">{{ t('noSavedQueries') }}</p>
      <ul v-else class="saved-list">
        <li v-for="query in state.queries" :key="query.name" :class="{ active: query.query === state.filters.query }">
          <button class="saved" :title="query.query" @click="load(query.query)">
            <strong>{{ query.name }}</strong>
            <code>{{ query.query }}</code>
          </button>
          <span v-if="confirming === query.name" class="confirm">
            <button class="btn danger" @click="(deleteSavedQuery(query), (confirming = null))">{{ t('yes') }}</button>
            <button class="btn ghost" @click="confirming = null">{{ t('no') }}</button>
          </span>
          <button v-else class="btn ghost icon del" :aria-label="`${t('deleteQuery')} ${query.name}`" :title="t('deleteQuery')" @click="confirming = query.name"><Icon name="trash" /></button>
        </li>
      </ul>
    </section>

    <section v-if="applications.length">
      <h2>{{ t('applications') }} <small>{{ t('applicationsHint') }}</small></h2>
      <ul class="apps">
        <li v-for="app in applications" :key="app.name">
          <button :title="t('filterBy', { what: app.name })" @click="filterApp(app.name)">
            <Icon name="layers" />
            <span class="app-name">{{ app.name }}</span>
            <span class="count">{{ app.count }}</span>
          </button>
        </li>
      </ul>
    </section>
  </aside>
</template>
