# Tracker fixtures

CI and local tests use the files in this directory. They are committed.

- `event.db` — sanitized native Tracker `Event` database (one bot-vs-bot match)
- `event.json` — ids and expected opening hands for that database

Tests copy `event.db` from here into a temp data dir. They do not read the
Wine volume, and CI does not need Docker.

Do not commit raw Wine prefixes, unsanitized Tracker databases, MTGO logs, or
exported client data.

## Refreshing the committed Event.db

Wine/Docker is only used when someone wants to regenerate the files above.
From a machine that has the `tracker_tracker-wine-data` volume:

```bash
pnpm run fixtures:capture:wine
```

That copies the volume's `Event.db`, keeps the largest match, replaces player
names with `PlayerA` / `PlayerB`, remaps account ids, deletes chat or whisper
logs, and overwrites `event.db` and `event.json` in this directory. Review
both files, then commit them. Set `TRACKER_WINE_VOLUME` if the Compose project
uses a different volume name.
