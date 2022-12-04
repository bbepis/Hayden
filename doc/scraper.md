# How to run it (CLI scraper)

`Usage: hayden <config file location>`

That's pretty much it. As for the config file, it's simply a JSON file containing parameters and rules for Hayden to follow.

An example config can be found here: [example-config-scraper.json](example-config-scraper.json)

&nbsp;

# Config file specification

Config keys are case insensitive.

## `source`

This contains configuration relating to an external website to scrape from.

### `source.type`

Specifies the imageboard software of the website you're scraping from. Can be these types:
- `4chan`
- `LynxChan`
- `Vichan`
- `InfinityNext`
- `FoolFuuka`
- `Meguca`
- `PonyChan`
- `ASPNetChan`

### `source.imageboardSite`

The root URL of the imageboard you wish to scrape from. An example would be `https://8chan.moe/`

This is ignored and not required for the `4chan` type; it always scrapes from `4chan.org`

### `source.boards`

An array of the boards you wish to scrape from the site; Hayden cannot automatically detect which boards exist on an imageboard.

It's keyed with the short name of the board you wish to scrape, and the value is an object containing more specific rules around the board.

All regexes are applied on an OR basis; any regex matching will cause the thread to be archived.

Available properties to apply to each board:

- `ThreadTitleRegex`  
A regex for filtering on the title/subject line of a thread.

- `OPContentRegex`  
A regex for filtering on the opening post content of a thread.

- `AnyFilter`  
A regex for filtering on the title/subject OR opening post content of a thread.

### `source.apiDelay`
The amount of seconds Hayden should wait (at minimum) inbetween making API calls. (This is specifically per connection, including proxies). Can be a decimal number

### `source.boardScrapeDelay`
The amount of seconds Hayden should wait at minimum before attempting to scrape the board thread listings again. If a single scrape run takes longer than this time, then the next board scrape will happen immediately. Can be a decimal number.

### `readArchive`
Specifies either `true` or `false` that Hayden should read the archives for each board on startup (only applicable to boards and imageboard software that support and have an archive). Obviously incurs a speed penalty for the initial scrape.

&nbsp;

## `proxies`

An array of proxies that can be used to allow parallelized scraping without incurring ban risk from polling too much from a single IP address.

Proxy objects look like this:

```json
{
	"url": "socks5://1.2.3.4:8000",
	"username": "user",
	"password": "pass"
}
```

May support HTTP proxies; I've only ever tested SOCKS5.

&nbsp;

## `consumer`

This contains configuration relating to where data should be saved to. See below for more detailed information about each consumer type.

### `consumer.type`

The type of consumer that will be used to store data.

- `Filesystem` for flat-file JSON storage
- `Asagi` for Asagi
- `Hayden` for Hayden's database format

### `consumer.downloadLocation`

The directory to where files should be downloaded to. The result directory structure is specific to each consumer type.

### `consumer.DatabaseType`

The type of database to connect to. Only applicable to `Asagi` and `Hayden` consumer types.

Can be these options:

- `MySQL` (this is also the case for MariaDB; Hayden will automatically determine if it's MySQL or MariaDB)
- `Sqlite`

### `consumer.ConnectionString`

The connection string to use to connect to the database. Format is specific to the database type; check the sample config for examples

### `consumer.fullImagesEnabled`

Specifies if full-size images should be downloaded.

### `consumer.thumbnailsEnabled`

Specifies if thumbnail images should be downloaded. As of writing, `Hayden` consumer type will not allow only downloading thumbnails without downloading full images.

&nbsp;

--------

&nbsp;

# Supported data stores

There are currently 3 supported data stores:

- Hayden
  - A custom database schema intended to support exotic data structures from altchans (such as multiple image support) while also retaining support for 4chan. This is currently the only datastore that the Hayden webserver can read from. Should be considered stable, however there will likely be some schema changes / improvements in the future that will be forwards compatible.

- Asagi
  - The database schema used by [Asagi](https://github.com/bibanon/asagi) and [FoolFuuka](https://github.com/pleebe/FoolFuuka). This may or may not be fully 100% functional anymore; it's been a long time since I've tested it but from the lack of issues I've heard about it, it seems to be working just fine. This uses the extended-length schema as of 2022 that supports the newer 4chan `tim` fields, and as such may have issues with older versions that don't have the same changes. While Asagi theoretically supports PostgreSQL, this only supports MySQL/MariaDB.

- JSON flat file
  - Similar to what you would recieve when running something like gallery-dl. Creates a folder for each thread, and in it writes a metadata JSON file and each image (+ thumbnail).  
  This is different to just writing the returned API JSON document, as it does not keep track of deleted / modified posts. Hayden instead writes a custom format that it uses internally, with the original JSON document as a property.

A table of which API frontends support which backends:

|            | Yotsuba | LynxChan | Vichan/Infinity | InfinityNext | Meguca | FoolFuuka | Ponychan | ASPNetChan |
| ---------- | ------- | -------- | --------------- | ------------ | ------ | --------- | -------- | ---------- |
| Asagi      | ✅       | ❌        | ❌               | ❌            | ❌      | ❌         | ❌        | ❌          |
| Filesystem | ✅       | ✅        | ✅               | ✅            | ✅      | ✅         | ✅        | ✅          |
| Hayden     | ✅       | ✅        | ✅               | ✅            | ✅      | ✅         | ✅        | ✅          |

For Hayden and Asagi data stores, they require a database to be able to operate. Hayden will create the tables for you, but not the database. If required, a specific database collation will be listed in the table below.

Supported DBMS' for each store:

|        | MySQL 8.x                    | MariaDB                      | SQLite | PostgreSQL |
| ------ | ---------------------------- | ---------------------------- | ------ | ---------- |
| Asagi  | ⚠️ (`utf8mb4_general_ci`) [1] | ✅ (`utf8mb4_general_ci`)     | ❌      | ❌          |
| Hayden | ✅ (`utf8mb4_0900_ai_ci`)     | ⚠️ (`utf8mb4_general_ci`) [2] | ✅      | ❌          |

**[1]:** Asagi databases are scaffolded using a schema designed for MariaDB. This may or may not work for MySQL; YMMV and you might have to modify it a bit. Let me know how you go

**[2]:** Hayden datastore was designed for MySQL and may not work for MariaDB. `utf8mb4_general_ci` is listed as a generic recommendation and is untested; Hayden does not perform any text searches and as such `utf8mb4_bin` may be more suitable. You'll have to figure out what works best for you

&nbsp;

--------

&nbsp;

# How to read console output

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