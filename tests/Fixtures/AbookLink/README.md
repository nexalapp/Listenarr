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

Resolvers named by posters: **nzbindex.nl**, **binsearch**, **nzbking**. Some releases
are not a single NZB — the poster's parts must be selected and combined ("Select All",
then "Create NZB"). A resolver that expects one hit per search will fail on these.

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
| Payload label | `Search:` · `Search for:` |
| Payload value | opaque token · 32-char hex · **full human-readable subject line** |
| Payload extras | `Password:` (optional) · trailing prose (`in a.b.misc`) · multi-line instructions |
| Audio copyright | `Audio Copyright:` · `Audiobook Copyright:` |
| File section | `File Information` · `Media Information` (wholly different fields) · absent |
| Series | dedicated fields · **embedded in `Title:`** with no series fields at all |
| Title suffix | `{Price}` — narrator shorthand in braces |
| Date label | `Copyright:` · `Audio Copyright:` · `Audiobook Copyright:` · `Audiobook Release:` · `Release Date:` |
| Format field | `Media Format:` · `File Type:` (`M4B \| NMR`) · `Source Format:` |
| File count | `Total Files:` · `Number of Files:` |
| Duration field | `Duration:` in General · `Total Duration:` in File Information |
| Genre | trailing `Genre:` line after the description · absent |

## The search string is mutable

Posters edit the payload in place when a release is replaced:

- Chev, topic 115056: *"Corrected copy uploaded with new search code."*
- degaussed, topic 107230: *"I reposted it with an updated search string."*

So a cached search string goes stale silently, and the stale one may resolve to a
corrupt or superseded release. Re-read the payload at grab time rather than trusting a
value stored at search time, and treat the post's `Last Edit` timestamp as a cache key.

Replies also carry defect reports the user would want to see before grabbing
(*"After chapter 105 the rest of chapters are corrupt"*). A post with replies deserves a
flag in the UI.

## Fixture index

| File | Poster | Era | Why it is here |
|---|---|---|---|
| `degaussed-mistborn-mp3.txt` | degaussed | 2025 | `Series Name`/`Series Position`, `Tags`, HH:MM:SS duration, size with no unit, search with no password |
| `stalkerama-misfit-m4b.txt` | stalkerama | 2026 | `Series`/`Book Number`, Audible link, `Release Date`, `Chapters`, search **and** password |
| `arif-taxman-no-fileinfo.txt` | Arif | 2020 | No File Information section at all; `Series Position: Book 1`; MD5-style search plus trailing newsgroup prose |
| `postbot-spot-archive.txt` | PostBot | 2015 | `[SPOT]` archive import — no NFO block whatsoever; `Subject:`/`Poster:`/`Date:` instead |
| `chev-labyrinth-filetype.txt` | Chev | 2025 | `File Type:`/`Number of Files:`/`Total Duration:` inside File Information; `Audiobook Release:`; trailing `Genre:`; replies reporting corruption and a **replaced search code** |
| `3josh-czarzakian-media-info.txt` | 3josh | 2020 | `Media Information` section with Source/Encoded fields; no series fields (series is inside `Title:`); `Audiobook Copyright:`; duration as `8 hours, 55 minutes, 26 seconds`; `Search for:` holding a **full subject line**; prose instructions describing a **multi-part collection** that must be assembled with Select All + Create NZB |
