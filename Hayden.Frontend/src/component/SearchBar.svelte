<script lang="ts">
    import { clickOutside } from "./clickOutside";
	import { router } from 'tinro';
    import type { BoardModel } from "../data/data";
	import { Utility } from "../data/utility";
	import { searchParamStore } from "../data/stores";
	import CryptoES from "crypto-es";

    export let boardInfo: BoardModel[] | null = null;

	let expanded = false;

	let searchBoxText = "";
	let selectedBoard = "";
	let subjectText = "";
	let nameText = "";
	let tripText = "";
	let posterIdText = "";
	let filenameText = "";
	let fileMd5Hash = "";
	let dateStartText = "";
	let dateEndText = "";
	let postType = "";
	let orderType = "";

	function enterHandler(event: KeyboardEvent) {
		if (event.key === "Enter") {
			event.preventDefault();
			Search(true);
		}
	}

	function Search(allBoards: boolean) {
		const params = {};

		if (Utility.IsNotEmpty(searchBoxText))
			params["query"] = searchBoxText;

		if (Utility.IsNotEmpty(selectedBoard) && !allBoards)
			params["boards"] = selectedBoard;

		if (Utility.IsNotEmpty(subjectText))
			params["subject"] = subjectText;

		if (Utility.IsNotEmpty(nameText))
			params["name"] = nameText;

		if (Utility.IsNotEmpty(tripText))
			params["trip"] = tripText;

		if (Utility.IsNotEmpty(posterIdText))
			params["posterId"] = posterIdText;

		if (Utility.IsNotEmpty(fileMd5Hash))
			params["md5hash"] = fileMd5Hash;

		if (Utility.IsNotEmpty(filenameText))
			params["filename"] = filenameText;

		if (Utility.IsNotEmpty(dateStartText))
			params["dateStart"] = dateStartText;

		if (Utility.IsNotEmpty(dateEndText))
			params["dateEnd"] = dateEndText;

		if (Utility.IsNotEmpty(postType))
			params["postType"] = postType;

		if (Utility.IsNotEmpty(orderType))
			params["orderType"] = orderType;

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

		if (fileList.length == 0) {
			fileMd5Hash = null;
			return;
		}

		const reader = new FileReader();

		reader.onload = function(event) {
			const data = <ArrayBuffer>event.target.result;

			fileMd5Hash = CryptoES.MD5(CryptoES.lib.WordArray.create(data)).toString();
		};

		reader.readAsArrayBuffer(fileList[0]);
	}
</script>

<div class="anchor">
	<div class="search-container" class:expanded={expanded} use:clickOutside on:click_outside={() => expanded = false} >
		<div class="textbox-container">
			<input type="text" class="main-searchbox"
				on:focus={() => expanded = true}
				on:keyup={enterHandler}
				bind:value={searchBoxText}/>
		</div>
		<div class="hidden-container">
			<div class="d-flex">
				<button type="button" class="search-button" on:click={() => Search(false)}>Search</button>
				<button type="button" class="search-button mx-2" on:click={() => Search(true)}>Search on all boards</button>
				<button type="button" class="search-button ml-auto" on:click={() => GoToPostNumber()}>Go to post number</button>
			</div>

			<div class="d-flex mt-2">
				<span class="px-2 text-right align-middle d-flex" style="width: 90px; background-color: var(--box-header-background-color); justify-content: end; align-items: center;">
					Boards
				</span>
				<select class="w-100" bind:value={selectedBoard}>
					{#if boardInfo != null}
						{#each boardInfo as board}
							<option value={board.shortName}>/{board.shortName}/</option>
						{/each}
					{/if}
				</select>
			</div>

			<div class="d-flex mt-2">
				<span class="px-2 text-right align-middle d-flex" style="width: 90px; background-color: var(--box-header-background-color); justify-content: end; align-items: center;">
					Subject
				</span>
				<input type="text" class="w-100" bind:value={subjectText}>
			</div>

			<div class="d-flex mt-2">
				<div class="d-flex" style="width: 50%">
					<span class="px-2 text-right align-middle d-flex" style="width: 80px; background-color: var(--box-header-background-color); justify-content: end; align-items: center;">
						Name
					</span>
					<input type="text" class="flex-grow-1" bind:value={nameText}>
				</div>
				<div class="d-flex" style="width: 50%">
					<span class="px-2 text-right align-middle d-flex" style="width: 80px; background-color: var(--box-header-background-color); justify-content: end; align-items: center;">
						Tripcode
					</span>
					<input type="text" class="flex-grow-1" bind:value={tripText}>
				</div>
			</div>

			<div class="d-flex mt-2">
				<span class="px-2 text-right align-middle d-flex" style="width: 90px; background-color: var(--box-header-background-color); justify-content: end; align-items: center;">
					Poster ID
				</span>
				<input type="text" class="w-100" bind:value={posterIdText}>
			</div>

			<div class="d-flex mt-2">
				<div class="d-flex" style="width: 50%">
					<span class="px-2 text-right align-middle d-flex" style="width: 80px; background-color: var(--box-header-background-color); justify-content: end; align-items: center;">
						Filename
					</span>
					<input disabled class="flex-grow-1" type="text" bind:value={filenameText}/>
				</div>
				<div class="d-flex" style="width: 50%">
					<span class="px-2 text-right align-middle d-flex" style="width: 170px; background-color: var(--box-header-background-color); justify-content: end; align-items: center;">
						File MD5
					</span>
					<input disabled class="flex-grow-1" type="file" on:change={HashedFileSelection} />
				</div>
			</div>

			<div class="d-flex mt-2">
				<div class="d-flex" style="width: 50%">
					<span class="px-2 text-right align-middle d-flex" style="width: 80px; background-color: var(--box-header-background-color); justify-content: end; align-items: center;">
						Date start
					</span>
					<input class="flex-grow-1" type="date" bind:value={dateStartText}/>
				</div>
				<div class="d-flex" style="width: 50%">
					<span class="px-2 text-right align-middle d-flex" style="width: 80px; background-color: var(--box-header-background-color); justify-content: end; align-items: center;">
						Date end
					</span>
					<input class="flex-grow-1" type="date" bind:value={dateEndText} />
				</div>
			</div>

			<div class="d-flex mt-2">
				<div class="d-flex" style="width: 50%">
					<span class="px-2 text-right align-middle d-flex" style="width: 80px; background-color: var(--box-header-background-color); justify-content: end; align-items: center;">
						Post type
					</span>
					<select class="flex-grow-1" bind:value={postType}>
						<option value="" selected>Any</option>
						<option value="op">OP only</option>
						<option value="replies">Replies only</option>
					</select>
				</div>
				<div class="d-flex" style="width: 50%">
					<span class="px-2 text-right align-middle d-flex" style="width: 80px; background-color: var(--box-header-background-color); justify-content: end; align-items: center;">
						Order
					</span>
					<select class="flex-grow-1" bind:value={orderType}>
						<option value="">Most recent</option>
						<option value="asc">Least recent</option>
					</select>
				</div>
			</div>

		</div>
	</div>
</div>

<style>
	.anchor {
        position: relative;
	}

	.search-button {
		background-color: var(--box-header-background-color);
        border: 1px solid var(--post-border-color);
		color: var(--text-color);
		outline: none;
	}

	.search-button:active:hover {
		background-color: var(--box-background-color);
	}

	.main-searchbox {
		width: 100%;
	}

	.search-container {
		margin: -3px 0;
		min-width: 350px;
	}

	.expanded.search-container {
		position: absolute;
		top: 0%;
		right: 0%;
		margin: -8px -5px;
	}

	.expanded .textbox-container {
		padding: 5px;
		/* padding: 5px;
		margin-top: 0px;
		margin-right: calc(1rem + 15px); */
		background-color: var(--box-background-color);
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