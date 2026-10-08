<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue';
import DetailDrawer from './components/DetailDrawer.vue';
import EventList from './components/EventList.vue';
import FilterBar from './components/FilterBar.vue';
import SideBar from './components/SideBar.vue';
import Toasts from './components/Toasts.vue';
import TopBar from './components/TopBar.vue';
import { init, moveSelection, select, selectedEntry } from './composables/store';

const topbar = ref<InstanceType<typeof TopBar> | null>(null);
const menuOpen = ref(false);

// Raccourcis : « / » cherche, ↑ ↓ (ou k j) parcourent les événements, Échap ferme le détail.
function onKey(event: KeyboardEvent) {
  const target = event.target as HTMLElement;
  if (/^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName) || event.ctrlKey || event.metaKey || event.altKey) return;
  if (event.key === '/') {
    event.preventDefault();
    topbar.value?.focus();
  } else if (event.key === 'ArrowDown' || event.key === 'j') {
    event.preventDefault();
    moveSelection(1);
  } else if (event.key === 'ArrowUp' || event.key === 'k') {
    event.preventDefault();
    moveSelection(-1);
  } else if (event.key === 'Escape') {
    select(null);
  }
}

onMounted(() => {
  window.addEventListener('keydown', onKey);
  void init();
});
onBeforeUnmount(() => window.removeEventListener('keydown', onKey));
</script>

<template>
  <div class="app" :class="{ 'with-drawer': selectedEntry }">
    <TopBar ref="topbar" @menu="menuOpen = !menuOpen" />
    <FilterBar />
    <SideBar :open="menuOpen" @close="menuOpen = false" />
    <EventList />
    <DetailDrawer v-if="selectedEntry" :entry="selectedEntry" />
    <Toasts />
  </div>
</template>
