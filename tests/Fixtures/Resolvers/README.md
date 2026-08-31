# NZB resolver contracts

A search string from an abook.link post is resolved into an NZB by one of these.

## Binsearch and NZBIndex are the same index

Verified 2026-08-29. A Binsearch result's checkbox name is the base64 encoding of the
NZBIndex result id for the same article:

```
Binsearch  MTZmODU0MGUtMTkyYS0zMThhLTljZGQtNTRiNTgzZjdjNDA1
  base64 → 16f8540e-192a-318a-9cdd-54b583f7c405
NZBIndex   16f8540e-192a-318a-9cdd-54b583f7c405
```

Same article, same id, same result set. **They are two frontends over one index, not two
resolvers.** Treating one as a fallback for the other buys no additional coverage — if a
release is missing from one it is missing from both. The only genuinely independent index
we have found is NZBKing, which is why the metered path cannot simply be dropped.

## NZBIndex — verified

No key, no CAPTCHA. JSON, so no HTML parsing.

| Purpose | Request |
|---|---|
| Search | `GET https://nzbindex.nl/api/search?q=<term>&size=<n>` |
| Download one NZB | `GET https://nzbindex.nl/download/<id>.nzb` |
| Groups | `GET https://nzbindex.nl/api/groups` |
| Session | `GET https://nzbindex.nl/api/auth/session` |

The download path has no `/api` prefix and the `.nzb` suffix is required —
`/api/download/<id>`, `/api/nzb/<id>` and `/api/nzb?ids=<id>` all 404.

`size` is required in practice: without it the response comes back with
`page.size: 0` and an empty `content` array rather than a default page.

```json
{"data":{"content":[{
  "id":"16f8540e-192a-318a-9cdd-54b583f7c405",
  "name":"[02/29] - \"Godfather.Audiobook.Collection.mp3.m4b.part01.rar\" yEnc 209715200",
  "poster":"0GUfeAdF07me@2FFFNabO.4SU",
  "posted":1788037605,
  "size":2726786312,
  "fileCount":28,
  "complete":false,
  "groups":["alt.binaries.sounds.music"]
}],"page":{"size":25,"number":0,"totalElements":802,"totalPages":33}},
 "error":false,"errorMessage":""}
```

`complete` is explicit, which lets an incomplete release be rejected before a download is
spent rather than at extraction. `size` is bytes and `posted` is epoch seconds, so neither
needs the unit-guessing the abook.link NFO does.

The search page also exposes filters worth using rather than reimplementing: poster,
groups, min/max size, min/max age, sort order, and a **Complete collections** toggle that
does server-side what our own `complete` check would do client-side.

Its **bulk** download (Select all + Download) is JS-driven, so its request shape was not
captured. Multi-part assembly therefore goes through Binsearch's `/nzb`, which is
verified and takes the same ids base64-encoded.

## Binsearch — verified

No key, no CAPTCHA.

| Purpose | Request |
|---|---|
| Search | `GET https://binsearch.info/search?q=<term>&max=25` |
| Build an NZB | `GET https://binsearch.info/nzb?<base64 id>=on[&…]&q=<term>` |
| Detail | `GET https://binsearch.info/details/<base64 id>` |
| By poster | `GET https://binsearch.info/search?poster=<address>` |

Selecting several ids and submitting to `/nzb` returns **one NZB assembled from those
parts** — the "Select All then Create NZB" flow posters describe for releases that do not
appear as a single collection. A resolver that only ever takes a single hit cannot fetch
those at all, on any backend.

Because the ids are shared, an article found through NZBIndex's JSON API can be retrieved
through Binsearch's `/nzb` by base64-encoding its id.

### Fixtures

| File | Why |
|---|---|
| `binsearch-result-row.html` | A real row: base64 id in the checkbox name, `/details/<id>` link, size, `28 Files` marked incomplete, obfuscated poster, newsgroup |
| `nzbindex-search-response.json` | A real API response: the same article, with `complete: false`, byte size, groups and the page envelope |

## NZBKing

Metered: 100 tokens, one per query, one returned per hour, key deleted at zero. See the
token ledger. Independent of the Binsearch/NZBIndex index, which is its value — it is a
genuine second opinion rather than the same answer twice.

| Purpose | Request |
|---|---|
| Search feed | `GET https://nzbking.com/rss/search/?q=<term>&key=<apikey>` |
| NZB | `GET https://nzbking.com/nzb:<24-hex-id>/` |
