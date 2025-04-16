import App from './App.svelte';
import { Utility } from './data/utility'
import { initStores } from './data/stores'
import { mount } from "svelte";

import "./styles/tailwind.css";
import "./styles/site.css";
import "./styles/4chan.css";
import "./styles/themes.css";

Utility.infoObject = (<any>window).info;
initStores();

const app = mount(App, {
	target: document.body,
	props: {}
});

export default app;