<script setup lang="ts">
import { computed, ref } from 'vue';
import { addCondition, moveSelection, select } from '../composables/store';
import { dayKey, prettyJson, timeOf } from '../format';
import { t } from '../i18n';
import { isFilterableName, isFilterableValue } from '../queryText';
import type { LogEntry } from '../types';
import Icon from './Icon.vue';
import LevelChip from './LevelChip.vue';

const props = defineProps<{ entry: LogEntry }>();

const copiedKey = ref<string | null>(null);
let copiedTimer = 0;

const when = computed(() => `${dayKey(props.entry.timestamp)}  ${timeOf(props.entry.timestamp)}`);

const properties = computed(() =>
  [...props.entry.properties]
    .sort((a, b) => a.name.localeCompare(b.name))
    .map(p => ({ ...p, json: prettyJson(p.value), canFilter: isFilterableName(p.name) && isFilterableValue(p.value) })),
);

async function copy(key: string, text: string) {
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    // contexte non sécurisé (HTTP) : repli par une zone de texte temporaire
    const area = document.createElement('textarea');
    area.value = text;
    area.style.position = 'fixed';
    area.style.opacity = '0';
    document.body.appendChild(area);
    area.select();
    document.execCommand('copy');
    area.remove();
  }
  copiedKey.value = key;
  window.clearTimeout(copiedTimer);
  copiedTimer = window.setTimeout(() => (copiedKey.value = null), 1400);
}

const asJson = () =>
  JSON.stringify(
    {
      timestamp: props.entry.timestamp,
      level: props.entry.level,
      sourceApp: props.entry.sourceApp,
      message: props.entry.message,
      exception: props.entry.exception,
      properties: Object.fromEntries(props.entry.properties.map(p => [p.name, p.value])),
    },
    null,
    2,
  );
</script>

<template>
  <aside class="drawer" :aria-label="t('details')">
    <header>
      <LevelChip :level="entry.level" big />
      <div class="meta">
        <strong>{{ entry.sourceApp }}</strong>
        <time>{{ when }}</time>
      </div>
      <span class="spacer" />
      <button class="btn ghost icon" :aria-label="t('previous')" :title="`${t('previous')} (↑)`" @click="moveSelection(-1)"><Icon name="chevronUp" /></button>
      <button class="btn ghost icon" :aria-label="t('next')" :title="`${t('next')} (↓)`" @click="moveSelection(1)"><Icon name="chevronDown" /></button>
      <button class="btn ghost icon" :aria-label="t('close')" :title="`${t('close')} (Échap)`" @click="select(null)"><Icon name="x" /></button>
    </header>

    <div class="drawer-body">
      <section>
        <div class="section-head">
          <h3>{{ t('message') }}</h3>
          <button class="btn ghost" @click="copy('message', entry.message)"><Icon :name="copiedKey === 'message' ? 'check' : 'copy'" />{{ copiedKey === 'message' ? t('copied') : t('copy') }}</button>
        </div>
        <p class="message">{{ entry.message }}</p>
      </section>

      <section v-if="entry.exception">
        <div class="section-head">
          <h3><Icon name="alert" /> {{ t('exception') }}</h3>
          <button class="btn ghost" @click="copy('exception', entry.exception)"><Icon :name="copiedKey === 'exception' ? 'check' : 'copy'" />{{ copiedKey === 'exception' ? t('copied') : t('copy') }}</button>
        </div>
        <pre class="exception">{{ entry.exception }}</pre>
      </section>

      <section>
        <div class="section-head">
          <h3>{{ t('properties') }} <small>{{ entry.properties.length }}</small></h3>
          <button class="btn ghost" @click="copy('json', asJson())"><Icon :name="copiedKey === 'json' ? 'check' : 'copy'" />{{ copiedKey === 'json' ? t('copied') : t('copyJson') }}</button>
        </div>
        <p v-if="!properties.length" class="empty">{{ t('noProperties') }}</p>
        <dl v-else class="props">
          <div v-for="property in properties" :key="property.name" class="prop">
            <dt>{{ property.name }}</dt>
            <dd>
              <pre v-if="property.json">{{ property.json }}</pre>
              <span v-else-if="property.value === null" class="null">null</span>
              <span v-else>{{ property.value }}</span>
            </dd>
            <div class="prop-actions">
              <button v-if="property.canFilter" class="btn ghost icon" :title="t('keepOnly')" :aria-label="`${t('keepOnly')} : ${property.name}`" @click="addCondition(property.name, '=', property.value as string)"><Icon name="plus" /></button>
              <button v-if="property.canFilter" class="btn ghost icon" :title="t('exclude')" :aria-label="`${t('exclude')} : ${property.name}`" @click="addCondition(property.name, '!=', property.value as string)"><Icon name="minus" /></button>
              <button class="btn ghost icon" :title="t('copy')" :aria-label="`${t('copy')} : ${property.name}`" @click="copy(property.name, property.value ?? '')"><Icon :name="copiedKey === property.name ? 'check' : 'copy'" /></button>
            </div>
          </div>
        </dl>
      </section>
    </div>
  </aside>
</template>
