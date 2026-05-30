<template>
  <section class="metric-card">
    <p class="metric-card__label">{{ label }}</p>
    <strong class="metric-card__value">{{ value }}</strong>
    <span v-if="hint" class="metric-card__hint">{{ hint }}</span>
  </section>
</template>

<script setup lang="ts">
defineProps<{
  label: string;
  value: string;
  hint?: string;
}>();
</script>
