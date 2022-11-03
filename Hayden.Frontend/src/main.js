import App from './App.svelte';
import "./styles/site.css";

const app = new App({
	target: document.body,
	props: {
		info: window.info
	}
});

export default app;