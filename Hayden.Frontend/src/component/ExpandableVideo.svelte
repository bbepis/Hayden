<script lang="ts">
    export let thumbUrl: string;
    export let videoUrl: string;
    export let altText: string;
    export let expanded: boolean = false;

    let img : HTMLImageElement;

    export let onClick: () => void = () => {
        const newValue = !expanded;
        
        if (!newValue && !isElementInViewport(img)) {
            img.scrollIntoView();
        }

        expanded = newValue;
    };

    function isElementInViewport (el: Element) {
        const rect = el.getBoundingClientRect();

        return rect.y >= 0;
    }

    function onClickInternal(e: Event) {
        e.preventDefault();
        onClick();
    }

    function onClickClose(e: Event) {
        e.preventDefault();
        expanded = false;
    }
</script>

{#if expanded}
    <a on:click={onClickClose}>[Close]</a>
    <br/>
    <!-- svelte-ignore a11y-media-has-caption -->
    <video controls>
        <source src={videoUrl} />
    </video>
{:else}
    <a href={videoUrl} on:click={onClickInternal} tinro-ignore>
        <img bind:this={img} src={thumbUrl} alt={altText} decoding="async"/>
    </a>
{/if}

<style>
    img {
        cursor: pointer;
        max-width: 100%;
    }
</style>