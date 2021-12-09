import App from './App.svelte';

var info = {
	endpoint: "https://localhost:5001/api"
}

const app = new App({
	target: document.body,
	props: {
		info: info
	}
});

export default app;