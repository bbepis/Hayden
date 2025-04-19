<script lang="ts">
    import Thread from "../component/Thread.svelte";
    import type { BoardPageModel } from "../data/data";
    import { Utility } from "../data/utility";
    import { onDestroy } from "svelte";
	import { searchParamStore } from "../data/stores";
	import PageSelector from "../component/PageSelector.svelte";
	import { querystring } from 'svelte-spa-router';
	import equal from "fast-deep-equal";

	interface Props {
		params?: Record<string, unknown>;
	}

	let { params = {} }: Props = $props();

	let dataPromise: Promise<BoardPageModel> | false = $state(false);

	let lastSearch: Record<string, string> | undefined;
	function search(query: Record<string, string>) {
		if (equal(lastSearch, query))
			return;

		lastSearch = {...query};
		dataPromise = Utility.FetchData("/search", query);
	}

	function getPageNumber(): number {
		return Number($searchParamStore ? ($searchParamStore["page"] ?? 1) : 1);
	}

	function navigatePage(pageNumber: number) {
		if (pageNumber <= 1) {
			searchParamStore.update(x => {
				if (!x)
					return x;

				delete x["page"];
				return x;
			})
		}
		else {
			if (!$searchParamStore)
				$searchParamStore = { ["page"]: String(pageNumber) }
			else
				$searchParamStore["page"] = String(pageNumber);
		}
	}

    onDestroy(searchParamStore.subscribe(m => {
		if (m != null && Object.keys(m).length > 0)
		{
			search(m);
		}
    }));

	querystring.subscribe(q => {
		let query = Object.fromEntries(new URLSearchParams(q));
		if (query != null && Object.keys(query).length > 0) {
			searchParamStore.set(query);
		}
	});
</script>

<!-- <input bind:value={query} on:keyup={enterHandler}>
<button on:click={search}>Search</button> -->

{#if dataPromise}
    {#await dataPromise}
        <p>Loading...</p>
    {:then data}
		<div class="board-title">
			Search
		</div>

        {#each data.threads as thread ([thread.board.id, thread.threadId]) }

            <Thread {thread} />
        {/each}

		<PageSelector currentPage={getPageNumber()} maxPage={Math.ceil(data.totalThreadCount / 40)} pageCallback={navigatePage} />
    {:catch}
        <p>Error 2</p>
    {/await}
{/if}

<style>
    .board-title {
        font-size: 30px;
        text-align: center;
        margin-bottom: 10px;
    }
</style>