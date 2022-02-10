<script lang="ts">
    import type { BoardModel, PostModel } from "../data/data";
    import moment from "moment";
    import ExpandableImage from "./ExpandableImage.svelte";
    import { onMount } from "svelte";
    import { Utility } from "../data/utility";
    import { RenderRawPost } from "../data/postrender";

    export let threadId: number;
    export let post: PostModel;
    export let board: BoardModel;
    export let subject: string = null;
    export let backquotes: number[] = null;

    const time = moment(post.dateTime + "Z");

    onMount(() => {
        jQuery(".post-contents a").attr("tinro-ignore", "true");
    });
</script>

<div id="p{post.postId}" class="post reply">
    <div id="pi{post.postId}" class="postInfo">
        {#if subject}
            <span class="subject">{subject}</span>
        {/if}
        <span class="nameBlock">
            <span class="name">{post.author ?? "Anonymous"}</span>
        </span>
        <span title={time.fromNow()}>
            {time.local().format("dd/MM/yy h:mm:ss A")}
        </span>
        <span>
            <a href="/{board.shortName}/thread/{threadId}#p{post.postId}"
                >No. {post.postId}</a
            >
        </span>
        {#if backquotes}
            {#each backquotes as backquoteId (backquoteId)}
                <div class="backquote">
                    <a href="#p{backquoteId}" class="quotelink" tinro-ignore
                        >&gt;&gt;{backquoteId}</a
                    >
                </div>
            {/each}
        {/if}
    </div>
    {#if post.files.length === 1}
        <div class="file">
            <div class="fileText">
                <a href={post.files[0].imageUrl}
                    >{post.files[0].filename}.{post.files[0].extension}</a
                >
                ({Utility.ToHumanReadableSize(post.files[0].fileSize)}{post
                    .files[0].imageWidth !== null
                    ? `, ${post.files[0].imageWidth} x ${post.files[0].imageHeight}`
                    : ""})
            </div>
            <a class="fileThumb" href={post.files[0].imageUrl}>
                <!-- <img src={post.thumbnailUrl} alt={post.post.mediaFilename}/> -->
                <ExpandableImage
                    fullImageUrl={post.files[0].imageUrl}
                    thumbUrl={post.files[0].thumbnailUrl}
                    altText={post.files[0].filename}
                />
            </a>
        </div>
    {:else if post.files.length > 1}
        <div class="panelUploads multipleUploads">
            {#each post.files as file}
                    <figure class="uploadCell">
                        <div class="uploadDetails">
                            <span class="hideMobile">(</span><span class="sizeLabel"
                                >{Utility.ToHumanReadableSize(file.fileSize)}</span
                            > <span class="dimensionLabel">{file.imageWidth}x{file.imageHeight}</span>
                            <a
                                class="originalNameLink"
                                href={file.imageUrl}
                                download="{file.filename}.{file.extension}">{file.filename}.{file.extension}</a
                            ><span class="hideMobile">)</span>
                        </div>

                        <div />
                        <ExpandableImage
                            fullImageUrl={file.imageUrl}
                            thumbUrl={file.thumbnailUrl}
                            altText={file.filename}
                        />
                    </figure>
            {/each}
        </div>
    {/if}
    <blockquote class="post-contents">
        {#if post.contentHtml && !post.contentRaw}
            {@html post.contentHtml.replace("\n", "<br/>")}
        {/if}
        {#if post.contentRaw}
            {@html RenderRawPost(post.contentRaw)}
        {/if}
    </blockquote>
</div>

<style>
    .backquote {
        display: inline-block;
        padding-left: 3px;
        padding-right: 3px;
    }
</style>
