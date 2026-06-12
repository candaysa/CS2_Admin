# Implementation Plan & Tasks

## Completed Features & Bug Fixes

- **[FIXED]** !blind command issue.
- **[FIXED]** !slap command spam delay (reduced dedup limit, removed NextTick delay, optimized velocity).
- **[ADDED]** !bury and !unbury commands.
- **[UPDATED]** New translation file format with named placeholders (e.g. {admin}, {target}) and merged prefix keys.
- **[FIXED]** HTML message translation keys and color formatting.
- **[FIXED]** Beacon issue.
- **[FIXED]** Ban database ip_address column now correctly populates on SteamID ban rows when BanType is set to oth.
- **[FIXED]** speed and gravity translation bugs caused by file encoding and double-newlines in .jsonc files.
- **[FIXED]** Main thread crashes (Rename, Give, Money, Hp, Burn, Disarm) by routing game API calls through NextTick.
- **[FIXED]** Offline player targeting using SteamID directly for punishment commands (gag, mute, silence, warn).
- **[FIXED]** Console commands (sw_ban, sw_kick) usage is now properly written to the server console.
- **[FIXED]** Discord interaction timeout/expiry bug (report_resolve timeout).
- **[FIXED]** !afk command not moving players to the spectator team properly.
- **[FIXED]** !give command weapon access restriction and give @all bug.
- **[FIXED]** !mixteam command unfair team distribution and players getting stuck.
- **[FIXED]** Discord API "Rate Limit" overload from !slap command (added 5-second cooldown for Discord logs).
- **[FIXED]** {count} and other single-argument placeholder variables failing to render properly in standalone message keys (like 	arget_multiple), resulting in unresolved {count} strings appearing in the chat.
