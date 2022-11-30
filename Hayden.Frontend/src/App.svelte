<script lang="ts">
	import Layout from "./Layout.svelte"
	import { Route, router } from 'tinro';
    import { Utility } from "./data/utility";

	import IndexPage from "./page/IndexPage.svelte";
	import ThreadPage from "./page/ThreadPage.svelte";
	import SearchPage from "./page/SearchPage.svelte";
	import AdminPage from "./page/AdminPage.svelte";
	import BoardPage from "./page/BoardPage.svelte";
	import LoginPage from "./page/LoginPage.svelte";
	import RegisterPage from "./page/RegisterPage.svelte";
    import { theme } from "./data/stores";

	theme.subscribe(currentTheme => {
		document.documentElement.className = `theme-${currentTheme}`;
	})

	let adminComponent;
</script>

<Layout>
	<Route path="/"><IndexPage /></Route>
	<Route path="/:board/thread/:threadid" let:meta><ThreadPage board={meta.params.board} threadId={Number(meta.params.threadid)} /></Route>
	<Route path="/board/:board/*" firstmatch let:meta={boardMeta}>
		<Route path="/page/:page" let:meta>
			<BoardPage board={boardMeta.params.board} initialCurrentPage={Utility.TryCastInt(meta.params.page) ?? 1} />
		</Route>
		<Route path="/page/:page/*" let:meta>
			<BoardPage board={boardMeta.params.board} initialCurrentPage={Utility.TryCastInt(meta.params.page) ?? 1} />
		</Route>
		<Route fallback>
			<BoardPage board={boardMeta.params.board} />
		</Route>
	</Route>
	<Route path="/Search"><SearchPage /></Route>
	<Route path="/Login"><LoginPage /></Route>
	<Route path="/Register"><RegisterPage /></Route>
	<Route path="/Admin"><AdminPage bind:this={adminComponent} /></Route>
</Layout>

<style>
	
</style>