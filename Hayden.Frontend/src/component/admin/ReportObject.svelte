<script lang="ts">
	import dayjs from "dayjs";
	import { Api } from "../../data/api";
	import type { ReportModel, ReportedPostModel } from "../../data/data";
	import Post from "../Post.svelte";

	interface Props {
		reportedPost: ReportedPostModel;
	}

	let { reportedPost = $bindable() }: Props = $props();

	function getSeverity(severity: number) : { text: string, class: string } {
		if (severity == 4) // immediate
			return { text: "Immediate", class: "severity-immediate" };
		if (severity == 3) // high
			return { text: "High", class: "severity-high" };
		if (severity == 2) // medium
			return { text: "Medium", class: "severity-medium" };
		//if (severity == 1) // low
			return { text: "Low", class: "severity-low" };
	}

	async function markResolved(report: ReportModel) {
		report.resolved = true;
		// force an update
		reportedPost = reportedPost;

		await Api.MarkReportResolvedAsync(report.id);
	}
</script>

{#each reportedPost.reports as report, i}
	{@const severity = getSeverity(4)}
	<div>
		<span class="{severity.class}">{severity.text}</span>
	</div>
	<div>{dayjs().toLocaleString()}</div>
	<blockquote class="whitespace-pre-line">{report.reason}</blockquote>

	{#if i === 0}
		<div style="grid-row: span 1 / span 1">
			<Post post={reportedPost.post} board={reportedPost.board} />
		</div>
	{/if}
	<!-- <div>{}</div>
	<div class="report-block {getSeverityClass(report.severity)}" class:resolved={report.resolved}>
		<span class="ip-address">{report.ipAddress}</span><br/>
		{report.reason}<br/>
		<button onclick={() => { markResolved(report) }}>Mark resolved</button>
	</div> -->
{/each}

<style>
	.report-block {
		background-color: var(--post-background-color);
		border: 1px solid var(--post-border-color);
		padding: 2px 6px;
		margin: 2px 0;
		white-space: pre-line;
	}

	.resolved {
		opacity: 50%;
	}

	.ip-address {
		font-weight: bold;
		color: var(--nav-text-color);
	}

	.severity-immediate {
		font-weight: bold;
		background-color: var(--color-highlight);
	}

	.severity-high {
		font-weight: bold;
		color: var(--color-highlight);
	}

	.severity-medium {
		font-weight: bold;
	}

	.severity-low {
		opacity: 70%;
	}
</style>
