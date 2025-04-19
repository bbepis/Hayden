<script lang="ts">
	import { replace } from "svelte-spa-router";
    import type { PostModel } from "../data/data";
    import { Utility } from "../data/utility";

	interface Props {
		params: {
			postid: string;
			board: string;
		};
	}

	let { params }: Props = $props();

    let errorOccurred: Boolean = $state(false);

    async function NavigateToPost() {
        try {
            const post = <PostModel>(await Utility.FetchData(`/${params.board}/post/${params.postid}`));

			replace(`/${params.board}/thread/${post.threadId}?postid=${post.postId}`);
        }
        catch {
            errorOccurred = true;
        }
    }

	NavigateToPost();
</script>

<div class="container-margin">
    {#if errorOccurred}
        <p>Could not load post</p>
    {:else}
        <p>Loading...</p>
    {/if}
</div>