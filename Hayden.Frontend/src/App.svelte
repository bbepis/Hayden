<script lang="ts">
	import Layout from "./Layout.svelte"
	import { Route, router } from 'tinro'; 
	import { InfoObject, Utility } from "./data";
	import IndexPage from "./page/IndexPage.svelte";
	import ThreadPage from "./page/ThreadPage.svelte";
import SearchPage from "./page/SearchPage.svelte";

	export let info : InfoObject;

	Utility.infoObject = info;
</script>

<Layout>
	<Route path="/"><IndexPage /></Route>
	<Route path="/:board/thread/:threadid" let:meta><ThreadPage board={meta.params.board} threadId={Number(meta.params.threadid)} /></Route>
	<Route path="/Search"><SearchPage /></Route>
	<Route path="/Privacy"><h1>This is the privacy page</h1></Route>
</Layout>

<style>
	
</style>