<script lang="ts">
	import { onMount } from "svelte";
    import type { ThreadModel, PostModel } from "../data/data";
	import Post from "./Post.svelte"

    export let thread: ThreadModel;
    export let jumpToHash: boolean = false;

	function calculateBackquotes(post: PostModel): number[] {
		return thread.posts.filter(x => {
			if (!x.contentHtml) {
				return false;
			}

			return x.contentHtml.indexOf(`&gt;&gt;${post.postId}`) >= 0;
		})
		.map(x => x.postId);
	}

	onMount(() => {
		if (jumpToHash) {
			window.location.hash = window.location.hash;
		}
	});
</script>

<div class="thread">
	{#each thread.posts as post, index (post.postId)}
		{#if index !== 0}

		<div style="margin: 0px 25px;">
			<Post post={post} threadId={thread.threadId} board={thread.board} subject={index === 0 ? thread.subject : null} backquotes={calculateBackquotes(post)}></Post>
		</div>

		{:else}
		
		<Post post={post} threadId={thread.threadId} board={thread.board} subject={index === 0 ? thread.subject : null} backquotes={calculateBackquotes(post)}></Post>
		
		{/if}
	{/each}
</div>