<script lang="ts">
	import { Api } from "../../data/api";
	import type { ReportModel, ReportedPostModel } from "../../data/data";
	import Post from "../Post.svelte";

	export let reportedPost : ReportedPostModel;

	function getSeverityClass(severity: number) {
		if (severity == 4) // immediate
			return "severity-immediate"
		if (severity == 3) // high
			return "severity-high"
		if (severity == 2) // medium
			return "severity-medium"
		if (severity == 1) // low
			return "severity-low"

		return "";
	}

	async function markResolved(report: ReportModel) {
		report.resolved = true;
		// force an update
		reportedPost = reportedPost;

		await Api.MarkReportResolvedAsync(report.id);
	}
</script>

<div>
	{#each reportedPost.reports as report}
		<div class="report-block {getSeverityClass(report.severity)}" class:resolved={report.resolved}>
			<span class="ip-address">{report.ipAddress}</span><br/>
			{report.reason}<br/>
			<button on:click={() => { markResolved(report) }}>Mark resolved</button>
		</div>
	{/each}

	<Post post={reportedPost.post} board={reportedPost.board} />
</div>

<style>
	.report-block {
		background-color: var(--post-background-color);
		border: 1px solid var(--post-border-color);
		padding: 2px 6px;
		margin: 2px 0;
		white-space: pre-line;
	}

	.report-block button {
		background-color: var(--box-header-background-color);
		border: solid 1px var(--post-border-color);
		color: var(--text-color);
		border-radius: 4px;
		margin: 2px 0;
	}

	.resolved {
		opacity: 50%;
	}

	.ip-address {
		font-weight: bold;
		color: var(--nav-text-color);
	}

	.severity-immediate {
		border-left: 2px solid red;
	}

	.severity-high {
		border-left: 2px solid orange;
	}

	.severity-medium {
		border-left: 2px solid yellow;
	}

	.severity-low {
		border-left: 2px solid blue;
	}
</style>
