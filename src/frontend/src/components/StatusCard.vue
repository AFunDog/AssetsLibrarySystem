<template>
  <section class="panel" :class="{ 'panel--accent': accent }">
    <h2>{{ title }}</h2>
    <slot />
  </section>
</template>

<script setup lang="ts">
defineProps<{
  title: string;
  accent?: boolean;
}>();
</script>
