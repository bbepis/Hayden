<script lang="ts">
    import Thread from "../component/Thread.svelte";
    import type { BoardPageModel, ThreadModel } from "../data/data";
    import { Utility } from "../data/utility";

    // const urlSearchParams = new URLSearchParams(window.location.search);
    // console.log(urlSearchParams);
    // let query: string = urlSearchParams.get("query");
    // console.log(query);

    import { meta } from 'tinro';
    import { onDestroy } from "svelte";
	import { searchParamStore } from "../data/stores";
	import PageSelector from "../component/PageSelector.svelte";

    const route = meta();

	let dataPromise: Promise<BoardPageModel> | false = false;

	function search(query: Record<string, string>) {
		dataPromise = Utility.FetchData("/search", query);
	}

    const unsubscribe = searchParamStore.subscribe(m => {
        // if (m && m.query["query"] !== query
        //     && Utility.IsNotEmpty(m.query["query"]))
		// {
        //    query = m.query["query"];

		if (m != null && Object.keys(m).length > 0)
		{
			search(m);
		}
        //}
    });

	function getPageNumber(): number {
		return Number($searchParamStore ? ($searchParamStore["page"] ?? 1) : 1);
	}

	function navigatePage(pageNumber: number) {
		if (pageNumber <= 1) {
			searchParamStore.update(x => {
				if (!!x)
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

    onDestroy(unsubscribe);

    if (route.query != null && Object.keys(route.query).length > 0) {
        search(route.query);
    }
</script>

<!-- <input bind:value={query} on:keyup={enterHandler}>
<button on:click={search}>Search</button> -->

{#if dataPromise}
    {#await dataPromise}
        <p>Loading...</p>
    {:then data}
        {#each data.threads as thread ([thread.board.id, thread.threadId]) }

            <br/>
            <hr/>

            <Thread {thread} />
        {/each}

		<PageSelector currentPage={getPageNumber()} maxPage={Math.ceil(data.totalThreadCount / 40)} on:page={event => navigatePage(event.detail.page)} />
    {:catch}
        <p>Error 2</p>
    {/await}
{/if}