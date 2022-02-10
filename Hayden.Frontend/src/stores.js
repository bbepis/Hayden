import { writable } from 'svelte/store';

const statusStore = writable("Idle");
const progressStore = writable(0);

export { statusStore, progressStore }