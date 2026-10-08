<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue';

defineProps<{ label: string; align?: 'left' | 'right' }>();

const open = ref(false);
const root = ref<HTMLElement | null>(null);

const close = () => (open.value = false);
const toggle = () => (open.value = !open.value);

function onPointer(event: MouseEvent) {
  if (open.value && root.value && !root.value.contains(event.target as Node)) close();
}
function onKey(event: KeyboardEvent) {
  if (event.key === 'Escape') close();
}

onMounted(() => {
  document.addEventListener('mousedown', onPointer);
  document.addEventListener('keydown', onKey);
});
onBeforeUnmount(() => {
  document.removeEventListener('mousedown', onPointer);
  document.removeEventListener('keydown', onKey);
});

defineExpose({ close });
</script>

<template>
  <div ref="root" class="popover">
    <slot name="trigger" :toggle="toggle" :open="open" />
    <div v-if="open" class="popover-panel" :class="{ right: align === 'right' }" role="dialog" :aria-label="label">
      <slot :close="close" />
    </div>
  </div>
</template>
