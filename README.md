# Hayden

Hayden is a 4chan / altchan archiver written in .NET Core for ultra-low resource usage and high performance.

It was originally writen as a drop-in alternative to [Asagi](https://github.com/eksopl/asagi), however Asagi compatibility currently has no guarantees.

Developer documentation is in `ARCHITECTURE.md`.

### Supported imageboard software

| Software                                                                                                              | Supports archives  | Example sites                 |
|-----------------------------------------------------------------------------------------------------------------------|--------------------|-------------------------------|
| [Yotsuba](https://www.4channel.org/faq#software)                                                                      | ✅                 | 4chan.org                     |
| [LynxChan](https://gitgud.io/LynxChan/LynxChan)                                                                       | ❌                 | 8chan.moe <br> endchan.org    |
| [Vichan](https://github.com/vichan-devel/vichan)/[Infinity](https://github.com/ctrlcctrlv/infinity) (not OpenIB/8kun) | ❌                 | sportschan.org <br> smuglo.li |
| [InfinityNext](https://github.com/infinity-next/infinity-next/)                                                       | ❌                 | 9chan.tw                      |
| [Meguca / shamichan](https://github.com/bakape/meguca)                                                                | ❌                 | 2chen.moe <br> shamik.ooo     |
| [FoolFuuka](https://github.com/FoolCode/FoolFuuka)                                                                    | ✅                 | desuarchive.org               |

### Features

- Much smaller memory consumption than Asagi.
  - For comparison, Hayden requires roughly 40MB of working memory to archive a single board (including all archived threads), while Asagi consumes several gigabytes to do the same.

- Uses a much more efficient algorithm to perform API calls, reducing overall network calls made considerably and eliminates cloudflare rate limit issues.

- Supports using multiple SOCKS proxies to distribute network load and allow parallel network operations.
  - Note that this feature is currently considered *very* unstable and is prone to deadlocking. For technical reasons this will remain until .NET 6 has released, which properly supports SOCKS proxies and doesn't require a hack to work.

- Supports writing to multiple types of data stores.

### Planned

- Thread ID-based scraping system. Currently the only logic for thread archival operates on a per-board basis

&nbsp;

----

&nbsp;

### Supported data stores

There are currently 3 supported data stores:

- Asagi (specifically the MySQL backend)
  - While "supported", it carries no guarantees that it's still 100% compliant and safe as it once was in this project. It's a large module to support and AFAIK no-one actually uses Hayden for it, so there's no point in me maintaining something with no demand, let alone me actually being able to constantly verify that it works. If you have a use case for this, let me know

- JSON flat file
  - Similar to what you would recieve when running something like gallery-dl. Creates a folder for each thread, and in it writes a metadata JSON file and each image (+ thumbnail).  
  This is different to just writing the returned API JSON document, as it does not keep track of deleted / modified posts. Hayden instead writes a slightly off-spec document to account for this.

- Hayden MySQL datastore
  - A prototype database schema intended for usage with the Hayden.WebServer HTTP frontend, with a similar goal of FoolFuuka of being able to display archived threads as webpages.

A table of which API frontends support which backends:

|            | Yotsuba | LynxChan | Vichan/Infinity | InfinityNext | Meguca | FoolFuuka
|------------|---------|----------|-----------------|--------------|--------|----------
| Asagi      | ✅      | ❌      | ❌              | ❌          | ❌     | ❌
| Filesystem | ✅      | ✅      | ✅              | ✅          | ✅     | ✅
| Hayden     | ✅      | ❌      | ❌              | ❌          | ❌     | ❌

&nbsp;

-----

&nbsp;

### How to run it (CLI scraper)

`Usage: hayden <config file location>`

That's pretty much it. As for the config file, it's simply a JSON file containing parameters and rules for Hayden to follow.

Here is an example:

```json
{
	"source" : {
		"type" : "4chan",
		"boards" : {
			"vg": {},
			"trash": {},
			"tg": {}
		},
		"apiDelay" : 1,
		"boardScrapeDelay" : 30,
		"readArchive": false
	},
	
	"backend" : {
		"type" : "Filesystem",
		"downloadLocation" : "C:\\my-archive-folder",
		
		"fullImagesEnabled" : true,
		"thumbnailsEnabled" : true
	}
}
```

The configuration is more or less self-explanatory, except for a few parts.

`source.type` specifies the source. Can be four types: `4chan`, `LynxChan`, `Vichan` and `InfinityNext`.

When using the latter two source types, an additional `source.imageboardWebsite` property is required containing the base URL of the imageboard. So if the website has a /v/ board at `https://8chan.moe/v/`, you should set `imageboardWebsite` to `https://8chan.moe/`.

`apiDelay` specifies the amount of seconds Hayden should wait (at minimum) inbetween making API calls. (This is specifically per connection, including proxies). Can be a decimal number

`boardScrapeDelay` is the amount of seconds Hayden should wait at minimum before attempting to scrape the board thread listings again. If a single scrape run takes longer than this time, then the next board scrape will happen immediately. Can be a decimal number.

`readArchive` specifies either `true` or `false` that Hayden should read the archives for each board on startup (only applicable to boards and imageboard software that support and have an archive). Obviously incurs a speed penalty for the initial scrape.

&nbsp;

Individual objects under `source.boards` support a small amount of filters. Here is an example of two of the currently supported filters:

```json
...
"tg": {"ThreadTitleRegexFilter": "big.+", "OPContentRegexFilter": "chungus.*"},
...
```

Hayden will only enqueue threads from /tg/ if either the title/subject line matches the regex of "`big.+`", **or** the post content of OP contains the regex "`chungus.*`". The regexes are also compiled as case-insensitive.

There is an additional `"AnyFilter"` that combines the both, i.e. it'll run the regex on both the OP content and subject fields, and succeed if any of them match.

&nbsp;

Last part is the `backend.type` stuff. There are three options:
- `Filesystem` for flat-file JSON storage
- `Asagi` for Asagi
- `Hayden` for Hayden's MySQL format

The latter two require an additional parameter in the `backend` object: `connectionString` containing the connection string used to connect to the MySQL database in question

### How to read console output

Here's an example excerpt 

```
[19/10/2021 3:47:34 AM] 4 threads have been queued total
[19/10/2021 3:47:34 AM] [Thread]  /vg/11111           +(2/4)        [2/1/4]
[19/10/2021 3:47:35 AM] [Image]   [2/0]
[19/10/2021 3:47:35 AM] [Thread]  /trash/2222         +(2/1)        [2/2/4]
[19/10/2021 3:47:35 AM] [Image]   [4/0]
[19/10/2021 3:47:35 AM] [Thread]  /tg/333333          +(0/1)        [0/3/4]
[19/10/2021 3:47:36 AM] [Thread]  /tg/444444        N +(0/0)        [0/4/4]
[19/10/2021 3:47:36 AM]
```

Hayden will periodically poll each board and determine which threads need to be re-polled and enqueue them.

Each `[Thread]` line can be read as such:

```
[Thread]  /board/00000           +(1/2)        [3/4/5]

00000: The thread ID
1: The amount of new images to download from this thread
2: The count of new posts in the thread, subtracted by the count of deleted posts
3: The total amount of images that are currently queued for downloading
4: The amount of threads that have been polled
5: The total amount of threads that need to be polled
```

Sometimes they have a letter before the `+` symbol. This indicates the status of the thread:

- `A`: Thread has been archived (and will no longer be polled)
- `D`: Thread has been deleted / pruned on an archive-less board (and will no longer be polled)
- `N`: Thread has not changed since the last time it was polled, and the returned data will be ignored
- `S`: Thread has been skipped from archival (and blacklisted) because it did not satisfy the combination of filters for the board
- `E`: Hayden has encountered an internal error attempting to process this thread, and as such will retry it on the next board scrape loop


Likewise for the `[Image]` lines:
```
[Image]   [1/2]
1: Amount of images yet to be downloaded
2: The total amount of images downloaded during this board scrape cycle 
```

&nbsp;

### How to run it (Web server)

This is currently very shoddy and is still a prototype. The Hayden.WebServer project includes a web application that will display threads archived with the `Hayden` backend.

Obviously you need to have a database set up with the appropriate schema. The creation script can be found in `Hayden.WebServer/MySQLCreateDatabase.sql`

Set the appropriate values for the connection string and file location in `appsettings.json` and it should start as-is. Do not run this unless you know your way around building a .NET Core app.

&nbsp;

------

&nbsp;

## FAQ
### Why make it?

I wanted to archive 4chan threads and display them, but didn't like what was already offered. As per usual, this turned into making a very large project to do so.

### Why C#? Doesn't it have a GC like Java and other managed languages, and consume a lot of memory as a result?

Yeah sure. Maybe you could get better performance / memory usage out of something like Rust.

But you could also just not be wasteful and be considerate of how you structure your data, and achieve very similar results?

### What's with the name?

I was listening to the [Doom 2016 soundtrack](https://www.youtube.com/watch?v=b2YG8DX0ees) as I was programming this.

I was not a fan of Eternal.