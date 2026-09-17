<script setup>
import { computed } from 'vue'

const props = defineProps({
  label: { type: String, required: true },
  value: { type: [Number, String], required: true },
  caption: { type: String, default: '' },
  /** Optional delta: { value: Number, percent: Number|null, goodDirection: 'down'|'up' } */
  delta: { type: Object, default: null },
  loading: { type: Boolean, default: false }
})

const displayValue = computed(() =>
  typeof props.value === 'number' ? props.value.toLocaleString() : props.value
)

const deltaDirection = computed(() => {
  if (!props.delta || props.delta.value === null || props.delta.value === undefined) return null
  if (props.delta.value > 0) return 'up'
  if (props.delta.value < 0) return 'down'
  return 'flat'
})

/** Fewer bugs found is good; more open bugs is bad. Callers say which way is which. */
const deltaTone = computed(() => {
  if (!deltaDirection.value || deltaDirection.value === 'flat') return 'flat'
  const good = props.delta.goodDirection ?? 'down'
  return deltaDirection.value === good ? 'good' : 'bad'
})

const deltaLabel = computed(() => {
  if (!props.delta || deltaDirection.value === null) return ''
  const { value, percent } = props.delta
  const magnitude = Math.abs(value)
  const suffix = percent === null || percent === undefined ? '' : ` (${Math.abs(percent)}%)`
  if (deltaDirection.value === 'flat') return `No change${suffix} vs last month`
  return `${deltaDirection.value === 'up' ? 'Up' : 'Down'} ${magnitude}${suffix} vs last month`
})
</script>

<template>
  <article class="card tile">
    <p class="tile__label">{{ label }}</p>

    <p v-if="loading" class="tile__value tile__value--loading" aria-hidden="true">--</p>
    <p v-else class="tile__value">{{ displayValue }}</p>

    <p v-if="delta && deltaDirection" class="tile__delta" :class="`tile__delta--${deltaTone}`">
      <!-- Arrow glyph plus wording: direction never rides on colour alone. -->
      <span aria-hidden="true">{{ deltaDirection === 'up' ? '▲' : deltaDirection === 'down' ? '▼' : '■' }}</span>
      {{ deltaLabel }}
    </p>
    <p v-else-if="caption" class="tile__caption">{{ caption }}</p>
  </article>
</template>

<style scoped>
.tile {
  padding: 16px 18px;
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.tile__label {
  margin: 0;
  font-size: 11.5px;
  font-weight: 650;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--ink-muted);
}

.tile__value {
  margin: 2px 0 0;
  font-size: 30px;
  font-weight: 640;
  line-height: 1.1;
  color: var(--ink);
}

.tile__value--loading {
  color: var(--ink-muted);
}

.tile__caption,
.tile__delta {
  margin: 2px 0 0;
  font-size: 12px;
  color: var(--ink-secondary);
  display: flex;
  align-items: center;
  gap: 5px;
}

.tile__delta {
  font-weight: 550;
}

.tile__delta--good {
  color: var(--good-text);
}

.tile__delta--bad {
  color: var(--critical);
}

.tile__delta--flat {
  color: var(--ink-muted);
}
</style>
