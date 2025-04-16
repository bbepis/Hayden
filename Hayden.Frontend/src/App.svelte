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

	let adminComponent = $state();
</script>

<Layout>
	<Route path="/"><IndexPage /></Route>
	<Route path="/:board/thread/:threadid" >{#snippet children({ meta })}
				<ThreadPage board={meta.params.board} threadId={Number(meta.params.threadid)} />			{/snippet}
		</Route>
	<Route path="/board/:board/*" firstmatch >
		{#snippet children({ meta: boardMeta })}
				<Route path="/page/:page" >
				{#snippet children({ meta })}
						{#key Utility.TryCastInt(meta.params.page) ?? 1}
						<BoardPage board={boardMeta.params.board} initialCurrentPage={Utility.TryCastInt(meta.params.page) ?? 1} />
					{/key}
									{/snippet}
				</Route>
			<Route path="/page/:page/*" >
				{#snippet children({ meta })}
						<BoardPage board={boardMeta.params.board} initialCurrentPage={Utility.TryCastInt(meta.params.page) ?? 1} />
									{/snippet}
				</Route>
			<Route fallback>
				<BoardPage board={boardMeta.params.board} />
			</Route>
					{/snippet}
		</Route>
	<Route path="/search"><SearchPage /></Route>
	<Route path="/Login"><LoginPage /></Route>
	<Route path="/Register"><RegisterPage /></Route>
	<Route path="/Admin"><AdminPage bind:this={adminComponent} /></Route>
</Layout>

<style>
	
</style>