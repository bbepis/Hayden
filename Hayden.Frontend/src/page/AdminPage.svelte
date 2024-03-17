<script lang="ts">
	import { onDestroy, onMount } from "svelte";
	import { progressStore, statusStore } from "../stores.js";

	let timeout: number;
	let persistObject = { active: true };

	async function updateProgress() {
		try {
			let response = await fetch("/admin/GetProgress");
			let responseJson = await response.json();

			$statusStore = responseJson.currentStatus;
			$progressStore = responseJson.progress * 100;
		} catch {}

		if (!persistObject.active) return;

		timeout = window.setTimeout(() => updateProgress(), 1000);
	}

	async function startRehash() {
		await fetch("/admin/StartRehash");
	}

	async function startImport() {
		await fetch("/admin/import");
	}

	async function startReindex() {
		await fetch("/admin/reindex");
	}

	onMount(() => updateProgress());

	onDestroy(() => {
		window.clearTimeout(timeout);
		persistObject.active = false;
	});
</script>

<h2>Admin</h2>

{#if $progressStore !== null && typeof $progressStore !== "undefined"}
	<div class="progress my-2">
		<div
			class="progress-bar progress-bar-striped"
			class:progress-bar-animated={$progressStore != 0 &&
				$progressStore < 100}
			style="width: {$progressStore}%"
		>
			{$progressStore.toFixed(1)}%
		</div>
	</div>
{/if}

<p>{$statusStore}</p>

<button type="button" class="btn btn-primary" on:click={startRehash}>
	Start rehash
</button>
<button type="button" class="btn btn-primary" on:click={startImport}>
	Start import
</button>
<button type="button" class="btn btn-primary" on:click={startReindex}>
	Start reindex
</button>

<style>
	.progress-bar {
		color: black;
		margin: 0px 5px;
	}
</style>
