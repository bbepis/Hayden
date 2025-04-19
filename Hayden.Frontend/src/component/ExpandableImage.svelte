<script lang="ts">

    let img : HTMLImageElement = $state();

    let loading: boolean = $state(false);

    interface Props {
        thumbUrl: string;
        fullImageUrl: string;
        altText: string;
        expanded?: boolean;
        onClick?: () => void;
    }

    let {
        thumbUrl,
        fullImageUrl,
        altText,
        expanded = $bindable(false),
        onClick = () => {
        const newValue = !expanded;

        if (!newValue && !isElementInViewport(img)) {
            img.scrollIntoView();
        }
        else if (newValue) {
            loading = true;
        }

        expanded = newValue;
    }
    }: Props = $props();

    function isElementInViewport (el: Element) {
        const rect = el.getBoundingClientRect();

        return rect.y >= 0;
    }

    function onClickInternal(e: Event) {
        e.preventDefault();
        onClick();
    }
</script>

<a href={fullImageUrl} onclick={onClickInternal}>
    <img
        bind:this={img}
        onload={() => loading = false}
        src={expanded ? fullImageUrl : thumbUrl}
        alt={altText}
        class:loading={loading}
        decoding="async"/>
</a>

<style>
    img {
        cursor: pointer;
        max-width: 100%;
    }

    .loading {
        opacity: 50%;
    }
</style>