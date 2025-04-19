<script lang="ts">
    import Thread from "../component/Thread.svelte";
    import type { ThreadModel } from "../data/data";
    import { Utility } from "../data/utility";
    import PostUploader from "../component/PostUploader.svelte";
	import { querystring } from "svelte-spa-router";
	import { postHoverStore } from "../data/stores";
	import cash from "cash-dom";

	interface Props {
		params: {
			threadid: string;
			board: string;
		};
	}

	let { params }: Props = $props();

	let query = Object.fromEntries(new URLSearchParams($querystring));

    let thread: ThreadModel | undefined = $state(undefined);
    let errorOccurred: Boolean = $state(false);

    let isRefreshing: Boolean = $state(false);

    async function FetchThread() {
        try {
            thread = <ThreadModel>(await Utility.FetchData(`/${params.board}/thread/${params.threadid}`));

			if (thread && query.postid)
			{
				setTimeout(() => {
					const parsedId = parseInt(query.postid);
					const targetPost = cash(document).find(`div[data-postid="${parsedId}"][data-boardid="${thread!.board.id}"]`);

					if (targetPost.length > 0)
					{
						targetPost[0]?.scrollIntoView({ behavior: "instant" });
						postHoverStore.set({ boardId: thread!.board.id, postId: parsedId });
					}
				}, 50);
			}

            errorOccurred = false;
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
                board={params.board}
                threadId={parseInt(params.threadid)}
                on:success={() => Refresh()} />
        {/if}
    {/if}
</div>

<style>
    .reset-btn {
        border-radius: revert;
    }
</style>