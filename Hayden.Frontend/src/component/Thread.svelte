<script lang="ts">
	import { onMount } from "svelte";
    import type { ThreadModel, PostModel } from "../data/data";
	import Post from "./Post.svelte"

    export let thread: ThreadModel;
    export let jumpToHash: boolean = false;

	function calculateBackquotes(post: PostModel): number[] {
		return thread.posts.filter(x => {
			if (!x.post.html) {
				return false;
			}

			return x.post.html.indexOf(`&gt;&gt;${post.post.postId}`) >= 0;
		})
		.map(x => x.post.postId);
	}

	onMount(() => {
		if (jumpToHash) {
			window.location.hash = window.location.hash;
		}
	});
</script>

<div class="thread">
	{#each thread.posts as post, index (post.post.postId)}
		<Post post={post} subject={index === 0 ? thread.thread.title : null} backquotes={calculateBackquotes(post)}></Post>
	{/each}
</div>