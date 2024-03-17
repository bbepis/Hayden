<script lang="ts">
    import { boardInfoStore } from "../data/stores";
    import { Utility } from "../data/utility";

    function getQuote() {
        return Utility.infoObject.quoteList[Math.floor(Math.random() * Utility.infoObject.quoteList.length)]
    }
</script>

<div class="container-margin main-container mx-auto">
    <div class="row">
        <div class="w-100">
            <img src="/logo.png" class="logo mb-2" alt="logo" />
            {#if Utility.infoObject.quoteList}
                <div class="subtitle">"{getQuote()}"</div>
            {/if}
        </div>
    </div>

    {#if Utility.infoObject.newsItems && Utility.infoObject.newsItems.length > 0}
        {@const newsItem = Utility.infoObject.newsItems[0]}
        <div class="row">
            <!-- This can be static but if you want you can create a script that creates these in the administrator tab -->
            <div class="newshead">
                <h2 id="60">
                    {newsItem.Title}
                    <span class="bywho">
                        — by Anonymous at {newsItem.DateString}
                    </span>
                    <span class="more_news" style="float:right;"
                        >See all <a href="/news">news</a> posts.</span
                    >
                </h2>
                <p>
                    {newsItem.Content}
                </p>
            </div>
        </div>
    {/if}

    <div class="row mt-1">
        <div class="col-sm-6 p-0 pr-sm-1">
            <fieldset>
                <legend style="font-weight: bold;">Boards</legend>
                <table>
                    <tbody
                        ><tr>
                            <div id="divBoards" class="top-boards title">
                                <ul>
                                    {#await $boardInfoStore}
                                        <div>Loading boards...</div>
                                    {:then boardInfo}
                                        {@const uniqueCategories = [...new Set(boardInfo.map(x => x.category))]}

                                        {#each uniqueCategories as category, categoryIndex}
                                            {#if uniqueCategories.length > 1}
                                                <div class="font-weight-bold" class:mt-2={categoryIndex > 0}>{category}</div>
                                            {/if}

                                            {@const filteredBoards = boardInfo.filter(x => x.category === category)}

                                            {#each filteredBoards as board}
                                                <li><a class="text-color-link" href={`/board/${board.shortName}/`}>/{board.shortName}/ - {board.longName}</a></li>
                                            {/each}
                                        {/each}
                                    {:catch}
                                        <div>Unable to load boards</div>
                                    {/await}
                                </ul>
                            </div>
                        </tr>
                    </tbody>
                </table>
            </fieldset>
        </div>

        <div class="col-sm-6 p-0 pl-sm-1 d-flex flex-column">
            <fieldset>
                <legend style="font-weight: bold;">Navigation</legend>
                <table>
                    <tbody
                        ><tr>
                            <div id="divNavigation" class="navigation title">
                                <ul>
                                    <!-- <li><a href="news">News</a></li> -->
                                    <!-- <li><a href="rules">Rules</a></li> -->
                                    <li><a href="info">Info & Contact</a></li>
                                    <!-- <li>
                                        <a href="mailto:22chan@disroot.org">Contact</a>
                                    </li> -->
                                    <!-- <li><a href="legal">Legal</a></li> -->
                                </ul>
                            </div>
                        </tr>
                    </tbody>
                </table>
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

    .newshead {
        background-color: var(--box-background-color);
        border: 1px solid var(--post-border-color);
        font-size: 12px;
        margin: auto;
        width: 100%;
    }

    .newshead p,
    .newshead h2 {
        padding: 6px 8px;
        margin: auto;
    }

    .newshead h2 {
        background-color: var(--box-header-background-color);
        background-size: 1px 14px;
        font-size: 12pt;
        padding: 3px 7px;
        font-weight: bold;
    }

    .bywho {
        font-size: 10px;
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

    .top-boards a,
    .top-boards a:visited {
        display: inline-block;
        padding: 0.25rem 0.75rem;
        margin-top: 3px;
        background-color: var(--box-header-background-color);
        border: 1px solid var(--post-border-color);
        break-after: column;
    }

    .top-boards ul, .navigation ul {
        padding: 0;
        list-style-type: none;
        margin: auto;
        margin-top: 5px;
    }

    .logo {
        height: auto;
        box-sizing: border-box;
        width: 350px;
        margin: auto;
        display: block;
    }

    .subtitle {
        color: var(--text-color);
        text-align: center;
        -webkit-text-stroke: 0.1px black;
        margin-bottom: 10px;
    }
</style>
