### Additional capabilities available from each imageboard

&nbsp;

|                         | Yotsuba     | LynxChan                  | Vichan/Infinity | InfinityNext | Meguca            | FoolFuuka   | Ponychan    | ASPNetChan |
| ----------------------- | ----------- | ------------------------- | --------------- | ------------ | ----------------- | ----------- | ----------- | ---------- |
| API available           | ✅           | ⚠️ [4]                     | ⚠️ [4]           | ✅            | ❌                 | ✅           | ✅           | ❌          |
| Efficient hash checks   | ⚠️ (md5) [1] | ❌ (broken md5/sha256) [2] | ⚠️ (md5) [1]     | ✅ (sha256)   | ⚠️ (md5, sha1) [1] | ⚠️ (md5) [1] | ⚠️ (md5) [1] | ❌          |
| Multiple files per post | ❌           | ✅                         | ✅               | ✅            | ❌                 | ❌           | ❌           | ❌          |
| Spoilered image status  | ✅           | ❌ [3]                     | ❌ [3]           | ✅            | ✅                 | ✅           | ❌           | ?          |
| Image deletion status   | ✅           | ❌ [3]                     | ✅               | ✅            | ✅                 | ✅           | ❌           | ?          |
| Raw comment text        | ❌           | ✅                         | ❌               | ✅            | ✅                 | ✅           | ✅           | ❌          |
| Tripcode support        | ✅           | ? [3]                     | ✅               | ✅            | ✅                 | ✅           | ✅           | ✅          |
| Websocket support       | ❌           | ❌                         | ❌               | ✅            | ✅                 | ❌           | ❌           | ❌          |
| Has an archive          | ✅           | ❌                         | ❌               | ❌            | ❌                 | ❌           | ❌           | ✅          |

#### **[1]**

Quick primer: A hash is a unique number that is calculated from a file. Having this number is really good from a scraper point-of-view, because it means that if the scraper recognizes that hash, then it knows that it has that file and can skip it.

However due to pesky laws of information, this number is not guaranteed to be unique. Some algorithms are better at this than others, and hence considered more secure. MD5 and SHA1 have known collisions, but SHA256 is yet to have any.

Hayden uses SHA256 internally when storing files and when determining if they exist. (This used to be MD5, to parallel 4chan/Asagi)

However not all imageboard software supplies SHA256 hashes, and as such Hayden can do two things (depending on a configuration setting):

1. Use the hash provided by the site, plus any other information to determine uniqueness (such as file size & other provided hash algorithms).  
Has the chance of causing a conflict, meaning that Hayden will attach a different image to the post than what was actually there.  
(Hayden also stores MD5 and SHA1 hashes for this purpose, but doesn't use them elsewhere)
2. Always download the file and calculate the SHA256 hash on our end, discarding if we already have it.  
While it's the most secure & reliable method, it also significantly increases bandwidth required for scraping (typically 50% of images uploaded to 4chan have already been on 4chan, and you likely already have it downloaded)

Pretty much all public archives opt for the first option. For example, all 4chan archives using FoolFuuka (i.e every 4chan archive) only care about the MD5 hash, and as such any conflicts are unknowingly included. The conflicts themselves are incredibly rare though, unless someone is intentionally trying to cause one to happen.

If no hash is available, then Hayden cannot skip downloading known images as it can't actually figure out what has already been downloaded.

#### **[2]**

(Add-on to [1])

Because Stephen Lynx is a fucking idiot, there's no way of getting a reliable hash from LynxChan.

There's no hash provided by LynxChan's HTML or very limited API; instead [it can only determined by the URL of the images](https://gitgud.io/LynxChan/LynxChan/-/issues/72).

This is unreliable for 2 reasons:

1. Depending on the version of LynxChan, it can either be using MD5 or SHA256 hashes. (And yes, this is an issue in the real world. Endchan uses MD5, while 8chan.moe uses SHA256)

2. The hash itself can be wrong. If the EXIF cleaning module is enabled, then the hash in the filename is the hash of the file BEFORE it got cleaned, and as such the hash is not the hash of the file you actually download. Fucking useless.

As a result, with LynxChan every file *has* to be redownloaded, even if you already have a copy of it.

#### **[3]**

Exists on the website, but is not provided via the API.

#### **[4]**

It's shit and might as well not have one. These imageboards also have the *option* to disable the API for whatever possible reason, meaning that some of these sites might actually not be scrapeable out of the box.