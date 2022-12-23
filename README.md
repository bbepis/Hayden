# Hayden

Hayden is a 4chan / altchan archiver written in .NET Core for ultra-low resource usage and high performance.

It also doubles as imageboard software.

It was originally writen as a drop-in alternative to [Asagi](https://github.com/eksopl/asagi), however Asagi compatibility currently has no guarantees.

Developer documentation is in `ARCHITECTURE.md`.

[**README for running the scraper**](doc/scraper.md)

[**README for running the webserver / imageboard**](doc/webserver.md)

&nbsp;

## Supported imageboard software

[**Detailed information**](doc/imageboards.md)

| Software                                                                                                              | API-less fallback | Example sites                 |
| --------------------------------------------------------------------------------------------------------------------- | ----------------- | ----------------------------- |
| [Yotsuba](https://www.4channel.org/faq#software)                                                                      | ❌                 | 4chan.org                     |
| [LynxChan](https://gitgud.io/LynxChan/LynxChan)                                                                       | ❌                 | 8chan.moe <br> endchan.org    |
| [Vichan](https://github.com/vichan-devel/vichan)/[Infinity](https://github.com/ctrlcctrlv/infinity) (not OpenIB/8kun) | ❌                 | sportschan.org <br> smuglo.li |
| [InfinityNext](https://github.com/infinity-next/infinity-next/)                                                       | ❌                 | 9chan.tw                      |
| [Meguca / shamichan](https://github.com/bakape/meguca)                                                                | ✅                 | 2chen.moe <br> shamik.ooo     |
| [FoolFuuka](https://github.com/FoolCode/FoolFuuka)                                                                    | ❌                 | desuarchive.org               |
| [Ponychan](https://bitbucket.org/ponychan/ponychan-tinyboard/src/master/)                                             | ❌                 | ponychan.net                  |
| ASPNetChan                                                                                                            | ✅                 | mlpol.net                     |

### Features

- Much smaller memory consumption than Asagi.
  - For comparison, Hayden requires roughly 40MB of working memory to archive a single board (including all archived threads), while Asagi consumes several gigabytes to do the same.

- Uses a much more efficient algorithm to perform API calls, reducing overall network calls made considerably and eliminates cloudflare rate limit issues.

- Supports using multiple SOCKS proxies to distribute network load and allow parallel network operations.

- Supports writing to multiple types of data stores.

### Planned

- Thread ID-based scraping system. Currently the only logic for thread archival operates on a per-board basis

&nbsp;

----

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