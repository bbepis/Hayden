import App from './App.svelte';
import { Utility } from './data/utility'
import { initStores } from './data/stores'
import "./styles/site.css";

Utility.infoObject = info;
initStores();

const app = new App({
	target: document.body,
	props: {}
});

export default app;