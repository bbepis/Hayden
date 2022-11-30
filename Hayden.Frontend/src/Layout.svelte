<script lang="ts">
    import type { BoardModel } from "./data/data";
    import { Utility } from "./data/utility";
    import { moderatorUserStore, theme } from "./data/stores"
    import { Api } from "./data/api";
    import { theme as themeStore } from "./data/stores"

    let boardInfoPromise = <Promise<BoardModel[]>>Utility.FetchData("/board/all/info");

    const themes = [
        { key: "yotsuba", text: "Yotsuba" },
        { key: "tomorrow", text: "Tomorrow" },
    ]

    let selectedTheme : string = $themeStore;
</script>

<style>
    .theme-select {
        max-width: 150px;
    }
</style>

<header>
    <nav class="navbar navbar-expand-sm navbar-toggleable-sm navbar-light box-shadow mb-3">
        <div class="container">
            <a class="navbar-brand" href="/">22chan</a>
            <button class="navbar-toggler" type="button" data-toggle="collapse" data-target=".navbar-collapse" aria-controls="navbarSupportedContent"
                    aria-expanded="false" aria-label="Toggle navigation">
                <span class="navbar-toggler-icon"></span>
            </button>
            <div class="navbar-collapse collapse d-sm-inline-flex flex-sm-row-reverse">
                <ul class="navbar-nav flex-grow-1">
                    <!-- <li class="nav-item">
                        <a class="nav-link" href="/">Home</a>
                    </li> -->
                    <li class="nav-item dropdown">
                        <!-- svelte-ignore a11y-invalid-attribute -->
                        <a class="nav-link dropdown-toggle" href="#" role="button" data-toggle="dropdown" aria-haspopup="true" aria-expanded="false">
                            Boards
                        </a>
                        <div class="dropdown-menu" aria-labelledby="navbarDropdown">
                            {#await boardInfoPromise}
                                <div class="dropdown-item">Loading...</div>
                            {:then boardInfo}
                                {#each boardInfo as board}
                                    <a class="dropdown-item" href="/board/{board.shortName}">{board.longName}</a>
                                {/each}
                            {/await}
                        </div>
                    </li>
                    {#if $moderatorUserStore}
                        <li class="nav-item">
                            <a class="nav-link" href="/Admin">Admin</a>
                        </li>
                        <li class="nav-item">
                            <a class="nav-link" href="#" on:click={() => { $moderatorUserStore = null; Api.UserLogoutAsync(); } }>Logout</a>
                        </li>
                    {/if}
                    <!-- <li class="nav-item">
                        <a class="nav-link" href="/Search">Search</a>
                    </li> -->
                </ul>
            </div>
        </div>
    </nav>
</header>
<div class="mx-4">
    <main role="main" class="pb-3">
        <slot></slot>
    </main>
</div>

<footer class="border-top footer text-muted">
    <div class="container d-flex align-items-center">
        <span>Hayden.WebServer 1.0</span>
        <div class="flex-grow-1"></div>
        <select class="form-control theme-select"
            bind:value={selectedTheme}
            on:change={() => $themeStore = selectedTheme}
        >
            {#each themes as theme}
                <option selected={selectedTheme === theme.key} value={theme.key}>{theme.text}</option>
            {/each}
        </select>
    </div>
</footer>