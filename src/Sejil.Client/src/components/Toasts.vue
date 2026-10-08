<script setup lang="ts">
import { dismiss, toasts } from '../composables/toasts';
import { t } from '../i18n';
import Icon from './Icon.vue';
</script>

<template>
  <div class="toasts" role="status" aria-live="polite">
    <div v-for="toast in toasts" :key="toast.id" class="toast" :class="toast.kind">
      <span style="flex: 1">{{ toast.text }}</span>
      <button class="btn ghost icon" style="height: 22px; width: 22px" :aria-label="t('close')" @click="dismiss(toast.id)"><Icon name="x" /></button>
    </div>
  </div>
</template>
