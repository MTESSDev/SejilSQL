<script setup lang="ts">
import { dictionary } from '../i18n';
import type { LevelName } from '../types';

defineProps<{ level: LevelName; big?: boolean }>();
</script>

<template>
  <span class="chip-level" :class="[`lv-${level.toLowerCase()}`, { big }]" :title="dictionary.levels[level]">
    {{ big ? dictionary.levels[level] : dictionary.levelShort[level] }}
  </span>
</template>
