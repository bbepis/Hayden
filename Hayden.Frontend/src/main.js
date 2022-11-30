import App from './App.svelte';
import { Utility } from './data/utility'
import "./styles/site.css";

Utility.infoObject = info;

const app = new App({
	target: document.body,
	props: {}
});

export default app;