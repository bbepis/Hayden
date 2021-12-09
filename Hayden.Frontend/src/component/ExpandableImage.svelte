<script lang="ts">
    export let thumbUrl: string;
    export let fullImageUrl: string;
    export let altText: string;
    export let expanded: boolean = false;

    let img : HTMLImageElement;

    export let onClick: () => void = () => {
        expanded = !expanded;
        
        if (!expanded && !isElementInViewport(img)) {
            img.scrollIntoView();
        }
    };

    function isElementInViewport (el: Element) {
        const rect = el.getBoundingClientRect();

        return rect.y >= 0;
    }

    function onClickInternal(e: Event) {
        e.preventDefault();
        onClick();
    }

    let currentUrl: string;

    $: currentUrl = expanded ? fullImageUrl : thumbUrl;
</script>

<a href={fullImageUrl} on:click={onClickInternal}>
    <img bind:this={img} src={currentUrl} alt={altText} decoding="async"/>
</a>

<style>
    img {
        cursor: pointer;
        max-width: 100%;
    }
</style>