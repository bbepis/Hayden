<script lang="ts">
	import Layout from "./Layout.svelte"
	import { Route, router } from 'tinro';
	import type { InfoObject } from "./data/data";
    import { Utility } from "./data/utility";

	import IndexPage from "./page/IndexPage.svelte";
	import ThreadPage from "./page/ThreadPage.svelte";
	import SearchPage from "./page/SearchPage.svelte";
	import AdminPage from "./page/AdminPage.svelte";

	export let info : InfoObject;

	let adminComponent;

	Utility.infoObject = info;
</script>

<Layout>
	<Route path="/"><IndexPage dataPromise={Utility.FetchData("/index")} /></Route>
	<Route path="/:board/thread/:threadid" let:meta><ThreadPage board={meta.params.board} threadId={Number(meta.params.threadid)} /></Route>
	<Route path="/board/:board" let:meta><IndexPage dataPromise={Utility.FetchData("/board/" + meta.params.board + "/index")} /></Route>
	<Route path="/Search"><SearchPage /></Route>
	<Route path="/Privacy"><h1>This is the privacy page</h1></Route>
	<Route path="/Admin"><AdminPage bind:this={adminComponent} /></Route>
</Layout>

<style>
	
</style>