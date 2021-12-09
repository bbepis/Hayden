<script lang="ts">
    import type { ThreadModel, PostModel } from "../data";
	import Post from "./Post.svelte"

    export let thread: ThreadModel;

	function calculateBackquotes(post: PostModel): number[] {
		return thread.posts.filter(x => {
			if (!x.post.html) {
				return false;
			}

			return x.post.html.indexOf(`&gt;&gt;${post.post.postId}`) >= 0;
		})
		.map(x => x.post.postId);
	}
</script>

<div class="thread">
	{#each thread.posts as post, index}
		<Post post={post} subject={index === 0 ? thread.thread.title : null} backquotes={calculateBackquotes(post)}></Post>
	{/each}
</div>