import App from './App.svelte';

const app = new App({
	target: document.body,
	props: {
		info: window.info
	}
});

export default app;