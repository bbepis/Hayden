<script lang="ts">
    import Thread from "../component/Thread.svelte";
    import type { ThreadModel } from "../data/data";
    import { Utility } from "../data/utility";
    import PostUploader from "../component/PostUploader.svelte";


    interface Props {
        board: string;
        threadId: number;
    }

    let { board, threadId }: Props = $props();

    let thread: ThreadModel | undefined = $state(undefined);
    let errorOccurred: Boolean = $state(false);

    let isRefreshing: Boolean = $state(false);
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
    {:else if !thread}
        <p>Loading...</p>
    {:else}
        <Thread {thread} jumpToHash={true} />

        <div class="my-2">
            <button class="reset-btn" onclick={Refresh}>Refresh</button>

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