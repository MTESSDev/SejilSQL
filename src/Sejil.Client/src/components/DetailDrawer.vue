<script setup lang="ts">
import { computed, ref } from 'vue';
import { drawerExpanded, drawerResizing, drawerWidth, hideFramework, maxDrawerWidth, MIN_WIDTH, saveDrawerWidth, setDrawerWidth, setHideFramework, setWrapLines, toggleExpanded, wrapLines } from '../composables/drawer';
import { addCondition, moveSelection, select } from '../composables/store';
import { dayKey, prettyJson, timeOf } from '../format';
import { t } from '../i18n';
import { isFilterableName, isFilterableValue } from '../queryText';
import type { LogEntry } from '../types';
import ExceptionView from './ExceptionView.vue';
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

// Bordure gauche : se tire à la souris, ou aux flèches ← → quand elle a le focus. Le panneau est collé à droite.
const grip = ref<HTMLElement | null>(null);
const panel = ref<HTMLElement | null>(null);
const currentWidth = () => Math.round(panel.value?.getBoundingClientRect().width ?? MIN_WIDTH);

function startResize(event: PointerEvent) {
  if (event.button !== 0 || drawerExpanded.value) return;
  grip.value?.setPointerCapture(event.pointerId);
  drawerResizing.value = true;
  event.preventDefault();
}
function resize(event: PointerEvent) {
  if (drawerResizing.value) setDrawerWidth(window.innerWidth - event.clientX);
}
function stopResize() {
  if (!drawerResizing.value) return;
  drawerResizing.value = false;
  saveDrawerWidth();
}
function resizeByKey(event: KeyboardEvent) {
  const step = event.shiftKey ? 120 : 30;
  if (event.key === 'ArrowLeft') setDrawerWidth(currentWidth() + step);
  else if (event.key === 'ArrowRight') setDrawerWidth(currentWidth() - step);
  else if (event.key === 'Home') setDrawerWidth(maxDrawerWidth());
  else return;
  event.preventDefault();
  saveDrawerWidth();
}
function resetWidth() {
  setDrawerWidth(null);
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
  <aside ref="panel" class="drawer" :aria-label="t('details')">
    <div
      v-if="!drawerExpanded"
      ref="grip"
      class="grip"
      :class="{ active: drawerResizing }"
      role="separator"
      aria-orientation="vertical"
      tabindex="0"
      :title="t('resizePanel')"
      :aria-label="t('resizePanel')"
      :aria-valuenow="drawerWidth ?? undefined"
      :aria-valuemin="MIN_WIDTH"
      @pointerdown="startResize"
      @pointermove="resize"
      @pointerup="stopResize"
      @pointercancel="stopResize"
      @dblclick="resetWidth"
      @keydown="resizeByKey"
    ><span /></div>
    <header>
      <LevelChip :level="entry.level" big />
      <div class="meta">
        <strong>{{ entry.sourceApp }}</strong>
        <time>{{ when }}</time>
      </div>
      <span class="spacer" />
      <button class="btn ghost icon" :aria-label="t('previous')" :title="`${t('previous')} (↑)`" @click="moveSelection(-1)"><Icon name="chevronUp" /></button>
      <button class="btn ghost icon" :aria-label="t('next')" :title="`${t('next')} (↓)`" @click="moveSelection(1)"><Icon name="chevronDown" /></button>
      <button class="btn ghost icon expand" :class="{ on: drawerExpanded }" :aria-pressed="drawerExpanded" :aria-label="drawerExpanded ? t('collapse') : t('expand')" :title="drawerExpanded ? t('collapse') : t('expand')" @click="toggleExpanded"><Icon :name="drawerExpanded ? 'collapse' : 'expand'" /></button>
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
          <div class="section-tools">
            <button class="btn ghost toggle" :class="{ on: wrapLines }" :aria-pressed="wrapLines" @click="setWrapLines(!wrapLines)"><Icon name="wrap" />{{ t('wrapLines') }}</button>
            <button class="btn ghost toggle" :class="{ on: hideFramework }" :aria-pressed="hideFramework" @click="setHideFramework(!hideFramework)"><Icon name="layers" />{{ t('hideFramework') }}</button>
            <button class="btn ghost" @click="copy('exception', entry.exception)"><Icon :name="copiedKey === 'exception' ? 'check' : 'copy'" />{{ copiedKey === 'exception' ? t('copied') : t('copy') }}</button>
          </div>
        </div>
        <ExceptionView :text="entry.exception" :wrap="wrapLines" :hide-framework="hideFramework" />
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
