# abook.link fixtures

Captured post bodies used to pin the parsers. abook.link posts are written by hand
(assisted by the site's Post Formatter), so field names, units and section presence
vary by poster and by era. Every distinct shape we find gets a fixture here plus a
row in the parser theory, so a shape we have seen once never regresses.

**Payload values are scrubbed.** `Search:` and `Password:` values in these fixtures
are replaced with same-shaped dummies. Parsing behaviour is identical and no gated
content lands in the repo.

## Site contract

| Purpose | Request |
|---|---|
| Fuzzy search | `GET /book/tools/search_abook.php?search=<terms>` |
| Topic | `GET /book/index.php?topic=<id>` |
| Reveal payload | `GET /book/index.php?action=thank;msg=<msg>;member=<poster>;topic=<id>;refresh=1` |
| Recent feed | `GET /book/index.php?action=recenttopics` |
| Random book | `GET /book/index.php?action=random:book` |

SMF separates query params with `;`. The thank action carries no CSRF token; it needs
the logged-in session cookie. Thanking is public — the account appears in the post's
"The following users thanked this post" list.

Result links look like `/book/index.php?topic=<id>&r=<relevance>`.

## Boards that are not releases

`21` Requests · `39` Filled Requests · `50` Series/Collections · `25` Off Topic

Title prefixes that are not releases: `[REQUEST]`, `[FILLED]`, `[Reading Order]`,
`[SPOT]` (old-forum archive imports; the site itself warns their links may be dead).

## Known field variance

| Concept | Seen as |
|---|---|
| Series | `Series Name:` · `Series:` |
| Position | `Series Position:` · `Book Number:` — values `03`, `5`, `Book 1` |
| Duration | `27:23:45` · `13h 52m` · `6 hrs 15 mins` |
| Size | `756.47` (no unit) · `756 MB` |
| Format | `MP3` · `m4b` (case varies) |
| Bitrate | `CBR 64 kbit/s 44100 Hz Stereo` · `125 kbps 44.1 kHz Stereo` |
| Description heading | `Book Description` · `Description` |
| File Information | present · **entirely absent** |
| Audible link | present (gives an ASIN) · absent |
| Payload | `Search:` only · `Search:` + `Password:` · `Search:` + trailing prose (`in a.b.misc`) |

## Fixture index

| File | Poster | Era | Why it is here |
|---|---|---|---|
| `degaussed-mistborn-mp3.txt` | degaussed | 2025 | `Series Name`/`Series Position`, `Tags`, HH:MM:SS duration, size with no unit, search with no password |
| `stalkerama-misfit-m4b.txt` | stalkerama | 2026 | `Series`/`Book Number`, Audible link, `Release Date`, `Chapters`, search **and** password |
| `arif-taxman-no-fileinfo.txt` | Arif | 2020 | No File Information section at all; `Series Position: Book 1`; MD5-style search plus trailing newsgroup prose |
| `postbot-spot-archive.txt` | PostBot | 2015 | `[SPOT]` archive import — no NFO block whatsoever; `Subject:`/`Poster:`/`Date:` instead |
