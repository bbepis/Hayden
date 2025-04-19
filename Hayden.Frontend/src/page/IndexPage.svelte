<script lang="ts">
    import { boardInfoStore } from "../data/stores";
    import { Utility } from "../data/utility";
	import BriefcaseBusiness from "@lucide/svelte/icons/briefcase-business";
	import BannerSvg from "../asset/default-logo.svg?raw";
	import { link } from "svelte-spa-router";

	function formatStatNumber(value: number | undefined) {
		if (value === undefined || value === null)
			return "?";

		return value.toLocaleString("en-us");
	}
</script>

<div class="flex justify-center">
	<div class="container-margin main-container w-full">
		<div class="w-full mt-3 mb-6">
			<!-- <img src="/logo.png" class="logo" alt="logo" /> -->
			<div class="logo">{@html BannerSvg}</div>
		</div>

		<div class="w-full">

			<div class="flex flex-col">
				<fieldset>
					<legend style="font-weight: bold;">Navigation</legend>
					<div id="divNavigation" class="navigation title">
						<ul>
							<!-- <li><a href="news">News</a></li> -->
							<!-- <li><a href="rules">Rules</a></li> -->
							<li><a href="/pages/info" use:link>Info & Contact</a></li>
							<!-- <li>
								<a href="mailto:22chan@disroot.org">Contact</a>
							</li> -->
							<!-- <li><a href="legal">Legal</a></li> -->
						</ul>
					</div>
				</fieldset>

				{#if Utility.infoObject.shiftJisArt}
				<fieldset class="flex-grow-1">
					<legend style="font-weight: bold;">Shift-JIS Art</legend>
					<div class="shiftjis-container">
						<span style="display: inline-block"
							>{Utility.infoObject.shiftJisArt}</span
						>
					</div>
				</fieldset>
				{/if}
			</div>

			<fieldset>
				<legend style="font-weight: bold;">Boards</legend>
				<div class="top-boards title leading-4.5 grid grid-cols-[1fr_0fr_repeat(3,max-content)]">
					{#if $boardInfoStore}
						{#await $boardInfoStore}
							<div>Loading boards...</div>
						{:then boardInfo}
							{@const uniqueCategories = [...new Set(boardInfo.map(x => x.category))]}

							{#each uniqueCategories as category, categoryIndex}
								{#if uniqueCategories.length > 0}
									<div class="font-bold text-center col-span-5 bg-box-header mb-1" class:mt-2={categoryIndex > 0}>{category}</div>
								{/if}

								{@const filteredBoards = boardInfo.filter(x => x.category === category)}

								{#each filteredBoards as board}
									<a class="text-color-link contents" href={`/${board.shortName}/`} use:link>
										<div class="flex pl-2">
											<b>/{board.shortName}/</b>&nbsp;- {board.longName}
										</div>
										<div class="text-right pr-2">
											{#if !board.isNSFW}
												<div title="Safe for work"><BriefcaseBusiness size={"1rem"} /></div>
											{/if}
										</div>
										<div class="text-right pr-2">{formatStatNumber(board.threadCount)} <span class="text-neutral-500">threads</span></div>
										<div class="text-right pr-2">{formatStatNumber(board.imageCount)} <span class="text-neutral-500">images</span></div>
										<div class="text-right pr-2">{formatStatNumber(board.postCount)} <span class="text-neutral-500">posts</span></div>
									</a>
								{/each}
							{/each}
						{:catch}
							<div>Unable to load boards</div>
						{/await}
					{/if}
				</div>
			</fieldset>
		</div>
	</div>
</div>

<style>
    .text-color-link {
        color: var(--text-color) !important;
    }

    .text-color-link:hover {
        color: var(--link-hover-color) !important;
    }

    .main-container {
        max-width: 840px;
    }

    @font-face {
        font-family: "mona";
        src: url("/submona.woff") format("woff");
        /* src: url("/aahub_light4.woff2") format("woff"); */
    }

    .shiftjis-container {
        display: flex;
        width: 100%;
        height: 100%;
        min-height: 75px;
        font-size: 14px;
        line-height: 15px;
        /* font-weight: bold; */
        /* text-align: center; */
        align-items: center;
        justify-content: center;
        font-family: "mona";
        white-space: pre;
    }

    legend {
        width: unset;
        max-width: unset;
        padding: 0 2px;
        margin: 0;
        font-size: 13.3333px;
    }

    fieldset {
        min-width: min-content;
        padding: 4.66px 10px 10px 10px;
        margin-top: 3px;
        border: 1px solid var(--post-border-color);
        background-color: var(--box-background-color);
    }

    .logo {
        height: auto;
        box-sizing: border-box;
        width: 550px;
        margin: auto;
        display: block;
    }

    .logo :global(.text) {
        fill: var(--logo-color);
    }
</style>
