<script lang="ts">
    import type { PostModel } from "../data";
    import moment from "moment";
    import ExpandableImage from "./ExpandableImage.svelte";
    import { onMount } from "svelte";

    export let post: PostModel;
    export let subject: string = null;
    export let backquotes: number[] = null;

    const time = moment(post.post.dateTime + "Z");

    onMount(() => {
        let $ = (<any>window).$;
        $(".post-contents a").attr("tinro-ignore", "true");
    });
</script>

<style>
    .backquote {
        display: inline-block;
        padding-left:  3px;
        padding-right: 3px;
    }
</style>

<div id="p{post.post.postId}" class="post reply">
	<div id="pi{post.post.postId}" class="postInfo">
        {#if subject}
            <span class="subject">{subject}</span>
        {/if}
		<span class="nameBlock">
			<span class="name">{post.post.author ?? "Anonymous"}</span>
		</span>
		<span title={time.fromNow()}>
			{time.local().format("dd/MM/yy h:mm:ss A")}
		</span>
		<span>
            <a href="/{post.post.board}/thread/{post.post.threadId}#p{post.post.postId}">No. {post.post.postId}</a>
		</span>
        {#if backquotes}
            {#each backquotes as backquoteId (backquoteId)}
                <div class="backquote">
                    <a href="#p{backquoteId}" class="quotelink" tinro-ignore>&gt;&gt;{backquoteId}</a>
                </div>
            {/each}
        {/if}
	</div>
	{#if post.hasFile}
		<div class="file">
			<div class="fileText">
				<a href={post.imageUrl}>{post.post.mediaFilename}</a> (69 KB, 123 x 456)
			</div>
			<a class="fileThumb" href={post.imageUrl}>
				<!-- <img src={post.thumbnailUrl} alt={post.post.mediaFilename}/> -->
                <ExpandableImage fullImageUrl={post.imageUrl} thumbUrl={post.thumbnailUrl} altText={post.post.mediaFilename} />
			</a>
		</div>
	{/if}
	<blockquote class="post-contents">
        {#if post.post.html}
		    {@html post.post.html}
        {/if}
	</blockquote>
</div>