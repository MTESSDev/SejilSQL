<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { t } from '../i18n';
import { foldFramework, hasAppFrames, hasFramework, parseException } from '../stackTrace';

const props = defineProps<{ text: string; wrap: boolean; hideFramework: boolean }>();

const items = computed(() => parseException(props.text));
const expanded = ref(new Set<number>());
const rows = computed(() => foldFramework(items.value, props.hideFramework, expanded.value));
const showLegend = computed(() => hasFramework(items.value) && hasAppFrames(items.value));

// Les séries redéployées à la main ne valent que pour l'exception et le réglage en cours.
watch([() => props.text, () => props.hideFramework], () => (expanded.value = new Set()));

function unfold(id: number) {
  expanded.value = new Set(expanded.value).add(id);
}
</script>

<template>
  <div>
    <div class="exc" :class="{ nowrap: !wrap }">
      <div class="exc-rows">
        <template v-for="(row, index) in rows" :key="index">
          <div v-if="row.kind === 'head'" class="exc-head" :class="{ inner: row.inner }">
            <div v-if="row.inner" class="exc-label">{{ t('innerException') }}</div>
            <div v-if="row.type" class="exc-type">{{ row.type }}</div>
            <div v-if="row.message" class="exc-msg">{{ row.message }}</div>
          </div>

          <div v-else-if="row.kind === 'frame'" class="fr" :class="row.framework ? 'fw' : 'mine'">
            <span class="n">{{ row.num }}</span>
            <span class="code"><span class="at">at </span><span class="ns">{{ row.ns }}</span><span class="meth">{{ row.method }}</span><span class="ns">({{ row.args }})</span><template v-if="row.file">{{ ' ' }}<span class="loc" :title="row.path">{{ row.file }}:{{ row.line }}</span></template></span>
          </div>

          <button v-else-if="row.kind === 'fold'" type="button" class="fold" @click="unfold(row.id)">▸ {{ t('frameworkLines', { n: row.frames.length }) }}</button>

          <div v-else-if="row.kind === 'sep'" class="exc-sep">{{ row.text }}</div>

          <div v-else class="exc-text">{{ row.text }}</div>
        </template>
      </div>
    </div>
    <div v-if="showLegend" class="exc-legend">
      <span><i class="mine" />{{ t('legendApp') }}</span>
      <span><i class="fw" />{{ t('legendFramework') }}</span>
    </div>
  </div>
</template>
