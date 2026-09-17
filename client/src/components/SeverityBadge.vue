<script setup>
import { computed } from 'vue'

const props = defineProps({
  severity: { type: String, required: true }
})

// The label is always rendered, so severity is never carried by colour alone.
const tone = computed(() => props.severity.toLowerCase())
</script>

<template>
  <span class="pill severity" :class="`severity--${tone}`">
    <span class="severity__dot" aria-hidden="true"></span>
    {{ severity }}
  </span>
</template>

<style scoped>
.severity {
  border: 1px solid transparent;
}

.severity__dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: currentColor;
  flex: none;
}

.severity--critical {
  background: var(--sev-critical-bg);
  color: var(--sev-critical-ink);
}

.severity--high {
  background: var(--sev-high-bg);
  color: var(--sev-high-ink);
}

.severity--medium {
  background: var(--sev-medium-bg);
  color: var(--sev-medium-ink);
}

.severity--low {
  background: var(--sev-low-bg);
  color: var(--sev-low-ink);
}
</style>
