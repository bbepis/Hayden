# Hayden

Hayden is a reimplementation of [Asagi](https://github.com/eksopl/asagi) in .NET Core for ultra-low resource usage, both memory and performance.

### Features:
- Much smaller memory consumption than Asagi.
  - For comparison, Hayden requires 10MB of working memory to archive a single board (including all archived threads), while Asagi consumes several gigabytes to do the same. (Multiple boards seem to also only require 10MB, subjec to further benchmarking)
- Efficient usage of API calls, allowing the scraper to be API compliant while also not missing any posts.
  - It uses the `threads.json` and `archives.json` endpoints to determine which threads have been updates, instead of continously polling.
  - Once the thread cache has warmed up, it will only update threads that have been detected as changed. On /g/ for example, this amounts to about 4 threads updated per 30 seconds. There's a lot of room to cram other boards in there, as a result.
  
  
### How to run it

To be fleshed out.
Currently it's executed as `hayden <board to archive> <directory on disk to store data>`

## FAQ
### Why make it?

I needed a tool of my own to download threads from 4chan, since the only other ones that exist either don't work or are written in python.
I found out about the wasteful resource usage of Asagi and (currently in the process of creating) a backend to support Asagi to help out 4chan archive owners who are currently crumbling under having to require several tens of gigabytes to keep Asagi archiving every board.

### Why C#? Doesn't it have a GC like Java and other managed langages, and consume a lot of memory as a result?

GC? Yes. Wasteful memory usage? Obviously not.

GC slowdowns are avoided due to me keeping the object limit down by only keeping in memory data that I absolutely need to cache, and strategically declaring some data strucures as structs. This also contributes to the very low memory usage.

Not to mention C# supports asynchronous code, where-as Java does not. I believe this is where the majority of wasted memory is in Asagi, as it needs to spin up a new process thread for every imageboard thread.

### What's with the name?

I was listening to the [Doom 2016 soundtrack](https://www.youtube.com/watch?v=b2YG8DX0ees) as I was programming this.
