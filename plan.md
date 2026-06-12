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

---


---

## 9. Scoreboard/Chat Tags Missing or Delayed (Tag Manager Issue)

**Status:** Investigated and root cause identified.

**Reason:**
The CS2_Admin plugin assigns player score tags (via SetScoreTagReliable) when a player first connects (OnClientPutInServer) by trying to set it at 0.2, 1, and 3 seconds.
However, due to Counter-Strike 2 engine mechanics, whenever a player **chooses a team or spawns for the first time**, the game engine automatically resets/clears their Clan tag.
If a player stays in the team selection menu for more than 3 seconds before spawning, the plugin's attempts expire and the engine successfully clears the tag on spawn.
The reason it "fixes itself after 1 round" is because CS2_Admin.cs has a hook for OnRoundStart which triggers RefreshAdminStateForAllOnlinePlayersAsync(). This method re-calculates permissions and re-applies tags for everyone at the start of every round.

**To-Do:**
1. To avoid querying the database constantly, cache the calculated tags in memory per-player (e.g. ConcurrentDictionary<int, string> _cachedPlayerTags).
2. Register an OnPlayerSpawn event hook in the plugin (or the CS2 equivalent).
3. Whenever a player spawns, immediately re-apply their cached tag using PlayerUtils.SetScoreTagReliable. This ensures that even if the engine clears the tag upon spawn, the plugin instantly restores it without waiting for the next round.

---


---


---


---

## 10. Rename (!rename) Command Names Resetting/Not Updating Properly

**Status:** Identified and planned.

**Reason:**
Upon inspecting the original plugin (Admins-1.0.0-b8), it was discovered that after a name change, the method player.Controller.PlayerNameUpdated() is explicitly called to notify the game engine.
In CS2_Admin's RenameCommand.cs, the name is changed and saved to the database, but the critical PlayerNameUpdated() call was forgotten. Because the engine wasn't explicitly notified of the network string table update, it fails to fully register the change, causing the name to revert back to the Steam profile name upon death or at the earliest opportunity. (Unlike Clan tags, PlayerNames generally do not reset on spawn if the engine is properly notified).

**To-Do:**
1. Add liveTarget.Controller.PlayerNameUpdated(); immediately after changing the name in RenameCommand.cs.
2. (Optional) In the future OnPlayerSpawn hook planned for Tag Manager, briefly check if the player has a custom name (GetCustomNameAsync) and enforce it with PlayerNameUpdated() just as an extra layer of protection against engine overrides.
