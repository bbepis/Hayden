<script lang="ts">
	import type { BoardModel, FileModel, PostModel } from "../data/data";
	import dayjs from "dayjs";
	import ExpandableImage from "./ExpandableImage.svelte";
	import { Utility } from "../data/utility";
	import { RenderRawPostYotsuba } from "../data/postrender";
	import PostMenu from "./PostMenu.svelte";
	import { moderatorUserStore, postHoverStore } from "../data/stores";
	import ExpandableVideo from "./ExpandableVideo.svelte";
	import ImageOff from "@lucide/svelte/icons/image-off";
	import Trash2 from "@lucide/svelte/icons/trash-2";
	import { link } from "svelte-spa-router";

	interface Props {
		post: PostModel;
		board: BoardModel;
		subject?: string;
		backquotes?: number[];
	}

	let {
		post,
		board,
		subject = undefined,
		backquotes = undefined,
	}: Props = $props();

	function getDateTime() {
		if (post.dateTime.endsWith("Z")) {
			return post.dateTime;
		}

		return post.dateTime + "Z";
	}

	const time = dayjs(getDateTime());

	let showDropdown: boolean = $state(false);
	let menu: HTMLElement | undefined = $state();

	function toggleMenu(value: boolean | null) {
		showDropdown = value ?? !showDropdown;

		if (showDropdown) {
			//setImmediate(() => {menu.focus();});
			setTimeout(() => {
				menu?.focus();
			}, 0);
		}
	}

	function menuKeyDown(e: KeyboardEvent) {
		if (e.keyCode === 27) {
			menu?.blur();
			e.preventDefault();
		}
	}

	function getFilename(file: FileModel): string {
		if (file.extension) return `${file.filename}${file.extension}`;

		return file.filename;
	}

	let postHighlighted = $derived.by(() => {
		let hoveredPost = $postHoverStore;
		if (hoveredPost && hoveredPost.boardId === board.id && hoveredPost.postId === post.postId)
			return true;

		return false;
	});
</script>

<div id="p{post.postId}" data-boardid={board.id} data-postid={post.postId} class="post reply {postHighlighted ? "border-highlight! bg-selected!" : ""}">
	<div id="pi{post.postId}" class="postInfo">
		{#if subject}
			<span class="subject">{subject}</span>
		{/if}
		<span class="nameBlock">
			<span class="name">{post.author ?? "Anonymous"} {post.tripcode ?? ""}</span>
		</span>
		<span title={time.fromNow()}>
			{time.local().format("ddd DD/MM/yy h:mm:ss A")}
		</span>
		<span>
			<a href="/{board.shortName}/thread/{post.threadId}?postid={post.postId}" use:link>
				No. {post.postId}
			</a>
		</span>

		{#if post.deleted}
			<span class="inline-block translate-y-[1px] text-highlight" title="Deleted">
				<Trash2 size="1em" />
			</span>
		{/if}

		<!-- svelte-ignore a11y_click_events_have_key_events -->
		<!-- svelte-ignore a11y_no_static_element_interactions -->
		<span class="relative">
			<button class="px-1! py-1.5! leading-0" onclick={() => toggleMenu(!showDropdown)}><span class="font-bold flex translate-y-[-3px]">...</span></button>
			<div
				tabindex="-1"
				class="menu"
				class:hidden={!showDropdown}
				onblur={() => {
					toggleMenu(false);
				}}
				onkeydown={menuKeyDown}
				onclick={(e) => {
					e.stopPropagation();
				}}
				bind:this={menu}
			>
				<PostMenu
					boardId={board.id}
					postId={post.postId}
					moderator={!!$moderatorUserStore}
				/>
			</div>
		</span>
		{#if backquotes}
			{#each backquotes as backquoteId}
				<div class="backquote">
					<a data-postid={backquoteId} href="#p{backquoteId}" class="quoteLink"
						>&gt;&gt;{backquoteId}</a
					>
				</div>
			{/each}
		{/if}
	</div>
	{#if post.files.length === 1}
		{@const file = post.files[0]}
		<div class="file">
			<div class="fileText">
				<a href={file.imageUrl}
					>{getFilename(file)}</a
				>
				({Utility.ToHumanReadableSize(file.fileSize)}{post.files[0]
					.imageWidth !== null
					? `, ${file.imageWidth} x ${file.imageHeight}`
					: ""})
			</div>
			{#if !file.thumbnailUrl && !file.imageUrl}
				<div title="Missing file" class="inline fileThumb">
					<ImageOff size={"125px"} color="currentColor"  />
				</div>
			{:else}
				<a class="fileThumb" href={file.imageUrl}>
					<!-- <img src={post.thumbnailUrl} alt={post.post.mediaFilename}/> -->
					{#if file.extension === "webm"}
						<ExpandableVideo
							videoUrl={file.imageUrl}
							thumbUrl={file.thumbnailUrl}
							altText={file.filename}
						/>
					{:else}
						<ExpandableImage
							fullImageUrl={file.imageUrl}
							thumbUrl={file.thumbnailUrl}
							altText={file.filename}
						/>
					{/if}
				</a>
			{/if}
		</div>
	{:else if post.files.length > 1}
		<div class="panelUploads multipleUploads">
			{#each post.files as file}
				<figure class="uploadCell">
					<div class="uploadDetails">
						<span class="hideMobile">(</span><span class="sizeLabel"
							>{Utility.ToHumanReadableSize(file.fileSize)}</span
						>
						<span class="dimensionLabel"
							>{file.imageWidth}x{file.imageHeight}</span
						>
						<a
							class="originalNameLink"
							href={file.imageUrl}
							download="{getFilename(file)}"
							>{getFilename(file)}</a
						><span class="hideMobile">)</span>
					</div>

					<div></div>

					{#if !file.thumbnailUrl && !file.imageUrl}
						<div title="Missing file" class="inline fileThumb">
							<ImageOff size={"125px"} color="currentColor"  />
						</div>
					{:else}
						{#if file.extension === "webm"}
							<ExpandableVideo
								videoUrl={file.imageUrl}
								thumbUrl={file.thumbnailUrl}
								altText={file.filename}
							/>
						{:else}
							<ExpandableImage
								fullImageUrl={file.imageUrl}
								thumbUrl={file.thumbnailUrl}
								altText={file.filename}
							/>
						{/if}
					{/if}
				</figure>
			{/each}
		</div>
	{/if}
	<blockquote class="post-contents">
		{#if post.contentRaw}
			{@html RenderRawPostYotsuba(post)}
		{:else if post.contentHtml}
			{@html post.contentHtml.replace("\n", "<br/>")}
		{/if}
	</blockquote>
</div>

<style>
	.backquote {
		display: inline-block;
		padding-left: 3px;
		padding-right: 3px;
	}

	.hidden {
		display: none;
	}

	.menu {
		position: absolute;
		top: 0%;
		left: 100%;
		cursor: initial;
	}

	.post {
		overflow: initial;
	}
</style>
