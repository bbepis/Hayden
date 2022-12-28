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

        <div class="my-2">
            <button class="reset-btn" on:click={Refresh}>Refresh</button>

            {#if isRefreshing}
                <div class="ml-2 spinner-border spinner-border-sm" role="status">
                    <span class="sr-only">Loading...</span>
                </div>
                <span>Refreshing...</span>
            {/if}
        </div>

        {#if thread.board.isReadOnly === false && thread.archived === false}
            <PostUploader
                isThreadUploader={false}
                board={board}
                threadId={threadId}
                on:success={() => Refresh()} />
        {/if}
    {/if}
</div>

<style>
    .reset-btn {
        border-radius: revert;
    }
</style>