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
        	search(m);
        //}
    });

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
    {:catch}
        <p>Error 2</p>
    {/await}
{/if}