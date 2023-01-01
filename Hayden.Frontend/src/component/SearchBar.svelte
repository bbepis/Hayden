<script lang="ts">
    import { clickOutside } from "./clickOutside";
	import {router} from 'tinro';

	let expanded = false;

	let searchBoxText = "";

	function enterHandler(event: KeyboardEvent) {
		if (event.key === "Enter") {
			event.preventDefault();
			Search();
		}
	}

	function Search() {
		router.goto(`/search?query=${encodeURI(searchBoxText)}`);
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
				<button type="button" class="search-button" on:click={Search}>Search</button>
				<button type="button" class="search-button mx-2">Search on all boards</button>
				<button type="button" class="search-button ml-auto">Go to post number</button>
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
		width: 350px;
	}

	.search-container {
		margin: -3px 0;
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