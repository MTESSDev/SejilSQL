import { reactive } from 'vue';

export interface Toast {
  id: number;
  kind: 'info' | 'error';
  text: string;
}

export const toasts = reactive<Toast[]>([]);
let next = 1;

export function notify(text: string, kind: Toast['kind'] = 'info', ms = 4000) {
  const toast: Toast = { id: next++, kind, text };
  toasts.push(toast);
  window.setTimeout(() => dismiss(toast.id), kind === 'error' ? ms * 2 : ms);
}

export function dismiss(id: number) {
  const i = toasts.findIndex(t => t.id === id);
  if (i >= 0) toasts.splice(i, 1);
}
