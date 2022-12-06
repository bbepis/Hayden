<script lang="ts">
    import Thread from "../component/Thread.svelte";
    import type { ThreadModel } from "../data/data";
    import { Utility } from "../data/utility";
    import PostUploader from "../component/PostUploader.svelte";


    export let board: string;
    export let threadId: number;

    let thread: ThreadModel = null;
    let errorOccurred: Boolean = false;

    let isRefreshing: Boolean = false;
    let hasLoadedSuccessfullyOnce: Boolean = false;

    async function FetchThread() {
        try {
            thread = <ThreadModel>(await Utility.FetchData(`/${board}/thread/${threadId}`));
            errorOccurred = false;

            hasLoadedSuccessfullyOnce = true;
        }
        catch {
            errorOccurred = true;
        }
    }

    async function Refresh() {
        isRefreshing = true;
        await FetchThread();
        isRefreshing = false;
    }

    FetchThread();
</script>

<div class="container-margin">
    {#if errorOccurred}
        <p>Error</p>
    {:else if thread === null}
        <p>Loading...</p>
    {:else}
        <Thread {thread} jumpToHash={true} />

        {#if thread.board.isReadOnly === false && thread.archived === false}
            <PostUploader
                isThreadUploader={false}
                board={board}
                threadId={threadId}
                on:success={() => Refresh()} />
        {/if}

        {#if isRefreshing}
            <p>Refreshing...</p>
        {/if}

        <button class="btn btn-outline-secondary" on:click={Refresh}>Refresh</button>
    {/if}
</div>