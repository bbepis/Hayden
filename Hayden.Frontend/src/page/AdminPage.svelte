<script lang="ts">
	import ReportObject from "../component/admin/ReportObject.svelte";
	import type { ReportedPostModel } from "../data/data.js";

	let reportPost : ReportedPostModel = {
		board: {
			id: 1,
			shortName: "test",
			longName: "",
			category: "",
			isNSFW: false,
			isReadOnly: false
		},
		post: {
			postId: 5,
			threadId: 1,
			author: null,
			contentHtml: "test post",
			contentRaw: null,
			dateTime: "2015-03-25T12:00:00Z",
			deleted: false,
			files: [],
			tripcode: null
		},
		reports: [
			{
				ipAddress: "1.2.3.4",
				severity: 4,
				reason: "category\nsevere"
			},
			{
				ipAddress: "1.2.3.4",
				severity: 3,
				reason: "category\nhigh"
			},
			{
				ipAddress: "1.2.3.4",
				severity: 2,
				reason: "category\nmedium"
			},
			{
				ipAddress: "1.2.3.4",
				severity: 1,
				reason: "category\nlow"
			},
		]
	}
</script>

<h2>Admin</h2>

<ReportObject reportedPost={reportPost} />

<style>
</style>
