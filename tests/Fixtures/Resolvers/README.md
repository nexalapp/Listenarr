# NZB resolver contracts

A search string from an abook.link post is resolved into an NZB by one of these. They are
tried in order; the first that returns an NZB wins.

## Binsearch — verified 2026-08-29

No API key, no CAPTCHA, no token allowance.

| Purpose | Request |
|---|---|
| Search | `GET https://binsearch.info/search?q=<term>&max=25` |
| Build an NZB | `GET https://binsearch.info/nzb?<id>=on[&<id>=on…]&q=<term>` |
| Result detail | `GET https://binsearch.info/details/<id>` |
| By poster | `GET https://binsearch.info/search?poster=<address>` |

`https://binsearch.info/?q=…` redirects to `/search?q=…`.

Each result row carries a checkbox whose **name** is the result id — a base64-encoded
UUID such as `MTZmODU0MGUtMTkyYS0zMThhLTljZGQtNTRiNTgzZjdjNDA1` — with value `on`. The
same id appears in the row's `/details/<id>` link, so it can be read from either.

Ticking several ids and submitting to `/nzb` returns **one NZB assembled from those
parts**. This is what makes multi-part releases resolvable: 3josh's posts explicitly say
his uploads "don't show up as one collection" and instruct the reader to Select All then
Create NZB. A resolver that only ever takes a single hit cannot fetch those.

A row also carries size, file count (with an `incomplete` class when parts are missing),
poster address and newsgroup — enough to reject an incomplete release before spending a
download.

### Fixtures

| File | Why |
|---|---|
| `binsearch-result-row.html` | One real result row: id in the checkbox name, `/details/<id>` link, size, `28 Files` marked incomplete, obfuscated poster, newsgroup |

## NZBKing

Metered. See the token ledger — 100 tokens, one per query, one returned per hour,
deleted at zero. Kept as a fallback rather than the primary path for that reason.

| Purpose | Request |
|---|---|
| Search feed | `GET https://nzbking.com/rss/search/?q=<term>&key=<apikey>` |
| NZB | `GET https://nzbking.com/nzb:<24-hex-id>/` |

## nzbindex.nl

Named by posters (3josh: "don't show up as one collection in nzbindex.nl"), **not yet
verified** — the browser tooling has no permission for that domain. Contract unknown.
