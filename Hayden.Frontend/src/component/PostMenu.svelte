<script lang="ts">
	import { getContext } from "svelte";

	interface Props {
		boardId: number;
		postId: number;
		moderator: boolean;
	}

	let { boardId, postId, moderator }: Props = $props();

	const reportPost: (boardId: number, postId: number) => void = getContext("reportPost");
	const deletePost: (boardId: number, postId: number) => void = getContext("deletePost");
	const banUser: (boardId: number, postId: number) => void = getContext("banUser");

	function showDeletePostModal() {
		deletePost(boardId, postId);
	}

	function showBanIpModal() {
		banUser(boardId, postId);
	}

	function showReportModal() {
		reportPost(boardId, postId);
	}
</script>

{#snippet menuItem(text: string, callback: () => void)}
	<!-- svelte-ignore a11y_click_events_have_key_events -->
	<!-- svelte-ignore a11y_no_static_element_interactions -->
	<div class="postborder cursor-pointer select-none transition-colors hover:bg-box-header hover:border-highlight p-1" onclick={callback}>{text}</div>
{/snippet}

<div class="menu">
	{@render menuItem("Report", showReportModal)}
	{#if moderator}
		{@render menuItem("Delete post", showDeletePostModal)}
		{@render menuItem("Ban poster IP", showBanIpModal)}
	{/if}
</div>

<style>
	.menu {
		width: 150px;
		background-color: var(--post-background-color);
	}
</style>
