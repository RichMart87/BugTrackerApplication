<script setup>
import { useToasts } from '../composables/useToasts.js'

const { toasts, dismiss } = useToasts()
</script>

<template>
  <Teleport to="body">
    <div class="toasts" role="status" aria-live="polite">
      <TransitionGroup name="toast">
        <div v-for="toast in toasts" :key="toast.id" class="toast" :class="`toast--${toast.tone}`">
          <span class="toast__icon" aria-hidden="true">
            {{ toast.tone === 'success' ? '✓' : toast.tone === 'error' ? '!' : 'i' }}
          </span>
          <p class="toast__message">{{ toast.message }}</p>
          <button type="button" class="toast__close" aria-label="Dismiss" @click="dismiss(toast.id)">✕</button>
        </div>
      </TransitionGroup>
    </div>
  </Teleport>
</template>

<style scoped>
.toasts {
  position: fixed;
  z-index: 200;
  right: 16px;
  bottom: calc(16px + env(safe-area-inset-bottom, 0px));
  display: flex;
  flex-direction: column;
  gap: 8px;
  max-width: min(360px, calc(100vw - 32px));
}

.toast {
  display: flex;
  align-items: flex-start;
  gap: 9px;
  padding: 10px 12px;
  border-radius: var(--radius);
  background: var(--surface-raised);
  border: 1px solid var(--hairline);
  box-shadow: var(--shadow);
}

.toast__icon {
  width: 18px;
  height: 18px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  font-size: 11px;
  font-weight: 700;
  color: #fff;
  flex: none;
  margin-top: 1px;
}

.toast--success .toast__icon {
  background: var(--good);
}

.toast--error .toast__icon {
  background: var(--critical);
}

.toast--info .toast__icon {
  background: var(--accent);
}

.toast__message {
  margin: 0;
  font-size: 12.5px;
  line-height: 1.45;
}

.toast__close {
  border: none;
  background: none;
  color: var(--ink-muted);
  cursor: pointer;
  padding: 0 2px;
  font-size: 11px;
  margin-left: auto;
}

.toast__close:hover {
  color: var(--ink);
}

.toast-enter-active,
.toast-leave-active {
  transition: opacity 180ms ease, transform 180ms ease;
}

.toast-enter-from,
.toast-leave-to {
  opacity: 0;
  transform: translateX(14px);
}

@media (prefers-reduced-motion: reduce) {
  .toast-enter-active,
  .toast-leave-active {
    transition: none;
  }
}
</style>
