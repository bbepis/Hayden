<script lang="ts">
	import { getContext } from "svelte";

	interface Props {
		boardId: number;
		postId: number;
		moderator: boolean;
	}

	let { boardId, postId, moderator }: Props = $props();

	const reportPost: (boardId: number, postId: number) => void = getContext("reportPost");

	function showDeletePostModal() {
		dispatch("postaction", {
			action: "delete-post",
			boardId: boardId,
			postId: postId,
		});
	}

	function showBanIpModal() {
		dispatch("postaction", {
			action: "ban-ip",
			boardId: boardId,
			postId: postId,
		});
	}

	function showReportModal() {
		reportPost(boardId, postId);
	}
</script>

<div class="menu">
	<div class="menu-item p-1" onclick={showReportModal}>Report</div>
	{#if moderator}
		<div class="menu-item" onclick={showDeletePostModal}>Delete post</div>
		<!-- <div class="menu-item">
        Delete image
    </div> -->
		<div class="menu-item" onclick={showBanIpModal}>Ban poster IP</div>
	{/if}
</div>

<style>
	.menu {
		width: 150px;
		background-color: var(--post-background-color);
	}

	.menu-item {
		border: 1px solid var(--post-border-color);
		cursor: pointer;
		user-select: none;
	}

	.menu-item:hover {
		background-color: var(--box-background-color);
	}
</style>
