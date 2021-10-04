# Architecture

In this document I go over the different concepts that Hayden uses for its code.

-----

## Modularization

Hayden is designed to be a modular system, where certain components can be mixed and matched via the config.

There are three abstract layers that can be swapped out. Their definitions are located in `Hayden/Contract`.

(Note that as of writing the Api layer can't be swapped out, read below)

-----

### Api

AKA. Frontend

This is the API source that Hayden pulls data from. Source files are located in `Hayden/Api`

Right now there is only a single implementation: `YotsubaApi` for 4chan (because C# can't have identifiers starting with numbers, and "FourChan" looked weird)

While in theory this should be able to be swapped out, there's no code to support this and it would likely require a wider scale restructure as everything is based off of 4chan's JSON models (located in `Hayden/Models`)

Vichan/8chan/8kun are likely going to have the first alternative implementation, but I have no idea what their JSON API looks like because there's fuck-all documentation on it. Not to mention there's zero demand for it, because I've yet to have either any demand for it or interest from myself.

-----

### Cache

The cache layer is used by Hayden to ensure that unsaved state is at least kept somewhere in the chance that Hayden is forcibly closed by either an error or other means.

The goal is to prevent loss of data, *not* so that Hayden can continue exactly where it left off. If it did, it would require writing to the database (and ensuring that it's done in a 100% safe way) every time Hayden's internal state changes.

So as a result, the only data (as of writing) that is committed to the cache layer is images that were queued to be downloaded.

It's entirely possible to remove the need for this by getting the Consumer layer to determine which threads don't have all images downloaded, however this was not feasible with Asagi's schema (something to do with not being able to determine if an image was downloaded or not, due to issues with previous data).

So if Asagi was dropped, then this layer could be removed. Maybe with a large performance impact as a result, but I have done zero tests with this theory.

Source is available for these in `Hayden/Cache`.

-----

### Consumer

AKA. Backend

This is where the most variety exists. The backend is what accepts thread metadata and does *something* with it, while telling Hayden where it wants specific images downloaded.

If Hayden detects a thread has been archived or pruned, it will notify the Consumer about this as well.

The backend is also expected to keep track of which files and threads should be & have already been downloaded, however Hayden will skip downloading any files that already exist.

Source is available for these in `Hayden/Consumers`.


-----

## Board archival loop

Hayden is an overtly complicated while loop. This is the general structure of the logic it uses to download threads:

1. If cancellation has been requested (i.e. user hitting "Q"), exit the program.
   - Note that checks like these are sprinkled throughout Hayden. However here is the safest place to do so, as every other location requires the downloaded image queue to be safely cached.
2. For each board, spawn an asynchronous task that does the following:
   1. Get all thread IDs and last modified times from that board. Remove any thread IDs that have been blacklisted (from filtering), and any that have not changed since the last time this board was scraped.
   2. If this is the first run, submit this list of IDs to the Backend and retrieve a listing of which threads exist in the backend, and what time they were last modified.
   3. If this is the first run and the "grab archived threads" config option is enabled, also add every archived thread ID (Not possible to filter in the same way as above, as archived threads do not have last modified time information)
   4. Coalesce these IDs into a main "thread pointer" list.
3. Save the current time as the next scrape reference time, to prevent boards from being scraped too quickly.
4. On each worker thread (based on how many proxy / HTTP connections are available):
   1. Wait until a proxy connection can be reserved
   2. If this worker thread has been marked as an "image" thread (roughly 1 in 3 threads), check if there are any images queued and download one. If successful, loop back to 1.
   3. If there are any threads available, download one
      1. Pass the changed state of a thread (modified, deleted or archived) to the backend
      2. Retrieve a list of image queue items to add to the image queue
      3. If successful, exit inner loop and go back to 1.
   4. Repeat step 2 but unconditionally
   5. If any of the above errored, requeue them to be downloaded next board loop
5. Restart back at 1.

This logic has some drawbacks, mainly that it does not support detecting whether or not a thread has been archived or deleted. It was determined a long time ago that this functionality was removed for the sake of better performance, memory usage and less API calls, however due to the scope of Hayden being reduced this may be reimplemented.

The main source file for this loop is in `Hayden/BoardArchiver.cs`.

Supporting only individual, predetermined threads requires its own individual loop implementation that has not been created yet.

-----

## Proxies

Hayden was designed to support multiple proxy connections to alleviate load from a single connection (because at high enough volumes, if you're not scraping fast enough you will lose data)

However this functionality is currently disabled as existing attempts to use SOCKS proxy connections in .NET Core has been really unstable. .NET 6 will officially support SOCKS proxies (and other custom proxy implementations), however as of writing it hasn't been released yet.

Proxy related code is in `Hayden/Proxy`