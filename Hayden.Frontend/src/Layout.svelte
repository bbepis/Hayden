<script lang="ts">
    import { BoardModel, InfoObject } from "./data/data";
    import { Utility } from "./data/utility";
    import { moderatorUserStore, boardInfoStore, theme as themeStore } from "./data/stores"
    import { Api } from "./data/api";
    import SearchBar from "./component/SearchBar.svelte";
    interface Props {
        children?: import('svelte').Snippet;
    }

    let { children }: Props = $props();

    const themes = [
        { key: "yotsuba", text: "Yotsuba" },
        { key: "eclipse", text: "Eclipse" },
        { key: "niniba", text: "Niniba" },
    ]

    let selectedTheme: string = $state($themeStore);

    let loadedBoardInfo: BoardModel[] | null = $state(null);

    (async function() {
        loadedBoardInfo = await $boardInfoStore;
    })();
</script>

<header>
    <nav class="bg-post-bg mb-3 py-2 px-8 text">
        <div class="flex gap-x-3">
            <a class="font-bold text! content-center" href="/">{Utility.infoObject.siteName}</a>
			<div class="separator"></div>
			<div class="flex content-center gap-x-1.5">
				{#if $boardInfoStore}
					{#await $boardInfoStore}
						<div class="content-center">Loading...</div>
					{:then boardInfo}
						{#if Utility.infoObject.compactBoardMode}
							{@const groupedBoards = Utility.groupByArray(boardInfo, b => b.category)}

							{#each groupedBoards as groupedBoard}
								<div class="nav-item dropdown">
									<a class="nav-link dropdown-toggle" href="#" role="button" data-toggle="dropdown" aria-expanded="false">
										{groupedBoard.key}
									</a>
									<div class="dropdown-menu">

									{#each groupedBoard.values as board, index}
										<div class="nav-item"><a class="nav-link board-nav-link" href="/board/{board.shortName}" title={board.longName}>/{board.shortName}/</a></div>
									{/each}

								</div></div>
							{/each}

						{:else}

							{#each boardInfo as board, index}
								<a class="content-center not-hover:text!" href="/board/{board.shortName}" title={board.longName}>/{board.shortName}/</a>
							{/each}

						{/if}
					{:catch}
						<div class="content-center">Unable to load boards</div>
					{/await}
				{/if}
			</div>
			<!-- <li class="nav-item dropdown">
				<a class="nav-link dropdown-toggle" href="#" role="button" data-toggle="dropdown" aria-haspopup="true" aria-expanded="false">
					Boards
				</a>
				<div class="dropdown-menu" aria-labelledby="navbarDropdown">

				</div>
			</li> -->
			{#if $moderatorUserStore}
				<div class="separator"></div>
				<div class="nav-item">
					<a class="nav-link" href="/Admin">Admin</a>
				</div>
				<div class="nav-item">
					<button type="button" class="btn btn-link nav-link nav-button" onclick={() => { $moderatorUserStore = null; Api.UserLogoutAsync(); }}>Logout</button>
					<!-- <a class="nav-link" href="#" on:click={() => { $moderatorUserStore = null; Api.UserLogoutAsync(); } }>Logout</a> -->
				</div>
			{/if}
			<!-- <li class="nav-item">
				<a class="nav-link" href="/Search">Search</a>
			</li> -->
			{#if Utility.infoObject.searchEnabled}
				<div class="ml-auto">
					<SearchBar boardInfo={loadedBoardInfo} />
				</div>
			{/if}
        </div>
    </nav>
</header>
<div class="mx-4">
    <main class="pb-3">
        {#if Utility.infoObject.bannerFilename}
            <img src={`/${Utility.infoObject.bannerFilename}`} class="logo mb-4" alt="banner" />
        {/if}
        {@render children?.()}
    </main>
</div>

<footer class="bg-post-bg py-2 px-8 text footer">
    <div class="flex">
        <div class="content-center"><a href="https://github.com/bbepis/Hayden" tinro-ignore>Hayden</a> 2.0</div>
        <!-- <a href="/legal" class="legal-link">Legal</a> -->
        <div class="flex-grow-1"></div>
        <select class="p-1 h-7 border-0! theme-select"
            bind:value={selectedTheme}
            onchange={() => $themeStore = selectedTheme}
        >
            {#each themes as theme}
                <option selected={selectedTheme === theme.key} value={theme.key}>{theme.text}</option>
            {/each}
        </select>
    </div>
</footer>

<style>
	nav a,
	nav a:visited {
		color: var(--nav-text-color);
		text-decoration: none;
	}

    .nav-link {
        padding: 0;
        padding-right: 0 !important;
    }

    .board-nav-link {
        color: var(--text-color) !important;
    }

    .board-nav-link:hover {
        color: var(--link-hover-color) !important;
        text-decoration: underline;
    }

    .theme-select {
        
		background-color: var(--box-background-color);
    }

    .logo {
        height: auto;
        box-sizing: border-box;
        width: 350px;
        margin: auto;
        display: block;
        float: none;
    }

    .nav-button {
        border: 0px transparent;
        font-size: unset;
        line-height: unset;
        color: var(--link-text-color) !important;
        transition: none;
    }

    .nav-button:hover {
        color: var(--link-hover-color) !important;
    }

    .separator {
        border-left: 1px solid var(--text-color);
        opacity: 50%;
    }

	.footer {
		position: absolute;
		bottom: 0;
		width: 100%;
		white-space: nowrap;
		line-height: 20px;
		/* Vertically center the text there */
	}
</style>