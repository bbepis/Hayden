<script lang="ts">
	import Textbox from "../../component/form/Textbox.svelte";

	type DataConfig = {
		schemaType: "" | "hayden" | "asagi";
		databaseType: "" | "mysql" | "sqlite";
		connectionString: string;
	}

	type SearchConfig = {
		enabled: boolean;
		serverType: "" | "elasticsearch" | "lnx";
		esShardCount: number;
		indexPrefix: string;
		username: string;
		password: string;
		url: string;
	}

	type SiteConfig = {
		siteName: string;
	}

	let dataConfig: DataConfig = $state({
		schemaType: "",
		databaseType: "",
		connectionString: ""
	});

	let searchConfig: SearchConfig = $state({
		enabled: false,
		serverType: "",
		esShardCount: 1,
		indexPrefix: "hayden",
		username: "",
		password: "",
		url: "",
	});

	let siteConfig: SiteConfig = $state({
		siteName: "Hayden"
	});

	$effect(() => {
		if (dataConfig.schemaType === "asagi" && dataConfig.databaseType === "sqlite")
			dataConfig.databaseType = "";
	});
</script>

<div class="flex justify-center">
	<div class="max-w-[800px]">
		<b class="text-2xl block -mb-2">Setup</b><br/>
		Welcome! This is the first launch setup page.
		<br/><br/>
		This is the web-serving software for datasets in Hayden and Asagi formats. This is <i>not</i> for scraping threads; that's the other command line tool that you want.

		To get started, fill in the required info below.

		<hr class="my-4"/>

		<div class="grid grid-cols-[0.5fr_1.5fr] gap-x-4 gap-y-6">
			<div class="text-right border-r-2 border-r-post-border pr-2">
				<b>Data source</b>
				<br/>
				Configuration relating to where to load thread data from
			</div>
			<div class="grid grid-cols-[max-content_1fr] gap-2 text-right items-center">
				Schema type:
				<select class="grow" bind:value={dataConfig.schemaType}>
					<option selected hidden value=""></option>
					<option value="hayden">Hayden</option>
					<option value="asagi">Asagi</option>
				</select>
				Database type:
				<select class="grow" bind:value={dataConfig.databaseType}>
					<option selected hidden value=""></option>
					<option value="mysql">MySQL</option>
					<option disabled={dataConfig.schemaType === "asagi"} value="sqlite">Sqlite</option>
				</select>
				<div class="shrink-0">{dataConfig.databaseType !== "sqlite" ? "Connection string" : "Database path"}:</div>
				<div class="flex gap-x-2">
					<Textbox contents class="grow" />
					<button>Test</button>
				</div>
			</div>
			<div class="text-right border-r-2 border-r-post-border pr-2">
				<b>Search</b>
				<br/>
				Configuration for searching & search server
			</div>
			<div class="grid grid-cols-[max-content_1fr] gap-2 text-right items-center">
				<span></span>
				<div class="text-left flex items-center gap-x-2">
					<input
						type="checkbox"
						id="search-enabled-input"
						bind:checked={searchConfig.enabled}
					/>
					<label for="search-enabled-input">Enable searching</label>
				</div>
				Search server type:
				<select class="grow" bind:value={searchConfig.serverType} disabled={!searchConfig.enabled}>
					<option selected hidden value=""></option>
					<option value="elasticsearch">ElasticSearch</option>
					<option value="lnx">lnx-search</option>
				</select>
				<div class="shrink-0">Shard count:</div>
				<input
					type="number"
					class="textbox-container"
					min="1"
					bind:value={searchConfig.esShardCount}
					disabled={!searchConfig.enabled || searchConfig.serverType !== "elasticsearch"}
				/>
				Server URL:
				<Textbox class="grow" bind:value={searchConfig.url} disabled={!searchConfig.enabled}/>
				Username:
				<Textbox class="grow" bind:value={searchConfig.username} disabled={!searchConfig.enabled}/>
				Password:
				<Textbox class="grow" bind:value={searchConfig.password} disabled={!searchConfig.enabled}/>
				Index prefix:
				<Textbox class="grow" bind:value={searchConfig.indexPrefix} disabled={!searchConfig.enabled}/>
			</div>
			<div class="text-right border-r-2 border-r-post-border pr-2">
				<b>Site</b>
				<br/>
				General website configuration
			</div>
			<div class="grid grid-cols-[max-content_1fr] gap-2 text-right items-center">
				Website name:
				<Textbox class="grow" bind:value={siteConfig.siteName}/>
			</div>
		</div>

		<hr class="my-4"/>

		<p class="mt-6 mb-2">
			Cool, that's everything you need. Click the button below to restart the webserver with these configuration values.
		</p>
		<button>Apply & restart</button>
	</div>
</div>