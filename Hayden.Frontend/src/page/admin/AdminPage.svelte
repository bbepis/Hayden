<script lang="ts">
	import ReportObject from "../../component/admin/ReportObject.svelte";
	import { Api } from "../../data/api";

	let reportsTask = $state(Api.GetReportsAsync(1));
</script>

<div class="grid grid-cols-4 bg-post-bg postborder py-1 px-2">
	<div class="contents border-b-1 border-b-post-border">
		<div class="font-bold">Severity</div>
		<div class="font-bold">Time</div>
		<div class="font-bold">Additional info</div>
		<div class="font-bold">Post</div>
	</div>

	{#await reportsTask}
		Loading...
	{:then reports}
	{#each reports as reportedPost}
		<div class="contents border-b-1 border-b-post-border">
			<ReportObject reportedPost={reportedPost} />
		</div>
	{/each}
	{/await}
</div>


<style>
</style>
