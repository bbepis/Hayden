<script lang="ts">
    import { clickOutside } from "./clickOutside";
	import { router } from 'tinro';
    import type { BoardModel } from "../data/data";
	import { Utility } from "../data/utility";
	import { searchParamStore } from "../data/stores";
	import CryptoES from "crypto-es";
	import Textbox from "./form/Textbox.svelte";

	interface Props {
		boardInfo?: BoardModel[] | null;
	}

	let { boardInfo = null }: Props = $props();

	let expanded = $state(false);

	interface SearchParams {
		query?: string;
		boards?: string;
		subject?: string;
		name?: string;
		trip?: string;
		posterId?: string;
		md5hash?: string;
		filename?: string;
		dateStart?: string;
		dateEnd?: string;
		postType?: string;
		orderType?: string;
	}

	let searchParams: SearchParams = $state({});

	let isLnx = false;

	function enterHandler(event: KeyboardEvent) {
		if (event.key === "Enter") {
			event.preventDefault();
			performSearch(true);
		}
	}

	function performSearch(allBoards: boolean) {
		const params: Record<string, any> = {};

		for (const [name, value] of Object.entries(searchParams)) {
			if (name == "boards" && allBoards)
				continue;

			if (Utility.IsNotEmpty(value))
				params[name] = value;
		}

		if (Object.keys(params).length == 0)
			return;

		router.goto(`/search?${new URLSearchParams(params).toString()}`);
		searchParamStore.set(params)
	}

	function GoToPostNumber() {
		//router.goto(`/search?query=${encodeURI(searchBoxText)}`);
	}

	function HashedFileSelection(e: Event) {
		const fileList = (<HTMLInputElement>e.target).files;

		if (!fileList || fileList.length == 0) {
			searchParams.md5hash = undefined;
			return;
		}

		const reader = new FileReader();

		reader.onload = function(event) {
			if (!event.target)
				return;

			const data = <ArrayBuffer>event.target.result;

			searchParams.md5hash = CryptoES.MD5(CryptoES.lib.WordArray.create(data)).toString();
		};

		reader.readAsArrayBuffer(fileList[0]);
	}
</script>

<div class="relative">
	{#if expanded}
		<!-- prevents the navbar from sliding when the original textbox is made absolute -->
		<div>
			<input type="text" class="search-container textbox-container py-1 pl-3 invisible"/>
		</div>
	{/if}
	<div class="search-container" class:expanded={expanded} use:clickOutside onclick_outside={() => expanded = false} style="z-index:1000;" >
		<Textbox showSearch bind:value={searchParams.query} onkeyup={enterHandler} onfocus={e => { expanded = true; }} />
		<div class="hidden-container">
			<div class="flex">
				<button type="button" class="search-button mr-auto" onclick={() => GoToPostNumber()}>Go to post number</button>
				<button type="button" class="search-button" onclick={() => performSearch(false)}>Search</button>
				<button type="button" class="search-button mx-2" onclick={() => performSearch(true)}>Search on all boards</button>
			</div>

			{#snippet header(text: string)}
				<div class="mr-2 px-2 py-1 bg-box-header text-right">{text}</div>
			{/snippet}

			<div class="mt-2 grid grid-cols-[max\-content_1fr] gap-y-1">

				{@render header("Board")}
				<select class="w-full h-full rounded textbox-container px-1 focus:border-highlight!" bind:value={searchParams.boards}>
					{#if boardInfo != null}
						{#each boardInfo as board}
							<option value={board.shortName}>/{board.shortName}/</option>
						{/each}
					{/if}
				</select>

				{@render header("Subject")}
				<Textbox bind:value={searchParams.subject} />

				{@render header("Name")}
				<Textbox bind:value={searchParams.name} />

				{@render header("Tripcode")}
				<Textbox bind:value={searchParams.trip} />

				{@render header("Poster ID")}
				<Textbox bind:value={searchParams.posterId} />

				{@render header("Filename")}
				<Textbox bind:value={searchParams.filename} />

				{@render header("File MD5")}
				<div class="flex items-center">
					<input disabled={isLnx} class="flex-grow-1" type="file" onchange={HashedFileSelection} />
				</div>

				{@render header("Date start")}
				<input disabled={isLnx} class="flex-grow-1" type="date" bind:value={searchParams.dateStart}/>

				{@render header("Date end")}
				<input disabled={isLnx} class="flex-grow-1" type="date" bind:value={searchParams.dateEnd}/>

				{@render header("Post type")}
				<select disabled={isLnx} class="w-full h-full rounded textbox-container px-1 focus:border-highlight!" bind:value={searchParams.postType}>
					<option value="" selected>Any</option>
					<option value="op">OP only</option>
					<option value="replies">Replies only</option>
				</select>

				{@render header("Order")}
				<select class="w-full h-full rounded textbox-container px-1 focus:border-highlight!" bind:value={searchParams.orderType}>
					<option value="">Most recent</option>
					<option value="asc">Least recent</option>
				</select>
			</div>

		</div>
	</div>
</div>

<style>
	:global(.search-container) {
		margin: -5px 0;
		min-width: 350px;
	}

	.textbox-container {
		background-color: var(--box-background-color);
        border: 1px solid #666;
	}

	.expanded.search-container {
		position: absolute;
		top: 0%;
		right: 0%;
		min-width: 450px;
	}

	.hidden-container {
		display: none;
		background-color: var(--box-background-color);
		color: var(--text-color);
		padding: 5px;
	}

	.expanded .hidden-container {
		display: block;
	}
</style>