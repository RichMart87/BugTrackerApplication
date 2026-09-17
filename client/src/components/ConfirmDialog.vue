<script setup>
defineProps({
  open: { type: Boolean, default: false },
  title: { type: String, default: 'Are you sure?' },
  message: { type: String, default: '' },
  confirmLabel: { type: String, default: 'Delete' },
  busy: { type: Boolean, default: false }
})

const emit = defineEmits(['confirm', 'cancel'])
</script>

<template>
  <Teleport to="body">
    <Transition name="confirm">
      <div v-if="open" class="overlay" role="alertdialog" aria-modal="true" :aria-label="title" @click.self="emit('cancel')">
        <div class="confirm card">
          <h2 class="confirm__title">{{ title }}</h2>
          <p class="confirm__message">{{ message }}</p>
          <div class="confirm__actions">
            <button type="button" class="btn" :disabled="busy" @click="emit('cancel')">Cancel</button>
            <button type="button" class="btn btn--danger" :disabled="busy" @click="emit('confirm')">
              {{ busy ? 'Working…' : confirmLabel }}
            </button>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.overlay {
  position: fixed;
  inset: 0;
  z-index: 150;
  background: rgba(11, 11, 11, 0.45);
  display: grid;
  place-items: center;
  padding: 16px;
}

.confirm {
  width: 100%;
  max-width: 400px;
  padding: 20px;
  background: var(--surface);
  box-shadow: var(--shadow-lg);
}

.confirm__title {
  margin: 0;
  font-size: 15px;
  font-weight: 640;
}

.confirm__message {
  margin: 6px 0 16px;
  font-size: 13px;
  color: var(--ink-secondary);
  line-height: 1.5;
}

.confirm__actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}

.confirm-enter-active,
.confirm-leave-active {
  transition: opacity 140ms ease;
}

.confirm-enter-from,
.confirm-leave-to {
  opacity: 0;
}
</style>
