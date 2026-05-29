[🇹🇷 Türkçe Okumak Icin Tiklayin (Turkish)](README_TR.md)

# CS2_ADMIN

Plugin Version: `1.0.6`

## Features

- Admin management
- Admin toptime
- Report system
- Calladmin system
- Admin playtime system
- Auto tag system

## Config Versioning

Main config files use schema versioning with `Version: 1`:

- `config.json`
- `commands.json`
- `permissions.json`
- `maps.json`

If a config file has missing/wrong version (or invalid JSON), plugin deletes and regenerates it on next load.

## 🎯 Target Selection (IMPORTANT: #id Usage)
When applying commands to players, **using the player's ID (`#id`) instead of their name** is highly recommended to prevent name confusion and accidental actions on the wrong player.
You can view player IDs by typing `!listplayers` in chat or `status` in the server console.

**Correct Usage Example:**
If Ali's ID is 12, instead of using `!kick Ali`, you should use `!kick #12`. In this guide, targets are indicated as `<#id>`.

**Other Mass Targeting Options:**
- `@all`: Everyone on the server.
- `@t`: All Terrorists.
- `@ct`: All Counter-Terrorists.
- `@alive`: All alive players.
- `@dead`: All dead players.
*(Ex: `!slay @t` kills all terrorists.)*

---

## 🗣️ General & Communication Commands
Used for general server actions and announcements.

* `!admin` - Opens the Admin Menu.
* `!warn <#id> <reason>` - Warns the player with a reason.
* `!unwarn <#id>` - Removes the warning from the player.
* `!who <#id>` - Shows detailed information about the target player.
* `!asay <message>` - Sends a message visible only to admins (Admin chat).
* `!say <message>` - Sends a System/Server message to everyone.
* `!csay <message>` - Displays a large center message (Announcement).
* `!hsay <message>` - Displays a hint message at the top of the screen.
* `!admintime` - Shows your total active admin time on the server.
* `!listplayers` - Shows the player list and their IDs.
* `!vote "<question>" "<answer1>" "<answer2>"` - Starts a vote. (Ex: `!vote "Change map?" "Yes" "No"`)

---

## 🗺️ Map & Game Management
Basic operations like changing maps or restarting the round.

* `!map <map_name>` - Changes the map (Ex: `!map de_dust2`).
* `!wsmap <workshop_id|name>` - Loads a Workshop map.
* `!rr [seconds]` - Restarts the game after specified seconds.

---

## ⚖️ Punishment Commands (Kick, Ban, Mute)
Used to penalize rule breakers.

* `!kick <#id> [reason]` - Kicks the player from the server. (Ex: `!kick #12 Language`)
* `!ban <#id> <minutes> [reason]` - Bans the player via SteamID. (Use `-1` for permanent).
* `!ipban <#id> <minutes> [reason]` - Bans the player via IP.
* `!addban <steamid> <minutes> [reason]` - Bans an offline player via SteamID. (Use SteamID instead of #id).
* `!unban <steamid|ip> [reason]` - Removes a ban.
* `!mute <#id> <minutes> [reason]` - Blocks the player's voice chat (microphone). (Use `-1` for permanent).
* `!unmute <#id>` - Removes voice chat block.
* `!gag <#id> <minutes> [reason]` - Blocks the player's text chat.
* `!ungag <#id>` - Removes text chat block.
* `!silence <#id> <minutes> [reason]` - Blocks both voice and text chat (Mute + Gag).
* `!unsilence <#id>` - Removes the silence punishment.

---

## 🎭 Fun & Interaction Commands (Cheats)
Commands for fun or game interactions.

* `!slap <#id> [damage]` - Slaps the player (damages them if specified).
* `!slay <#id>` - Kills the player instantly.
* `!respawn <#id>` - Respawns the player.
* `!team <#id> <t|ct|spec>` - Changes the player's team. (Ex: `!team #12 spec`)
* `!noclip <#id>` - Toggles noclip (fly through walls) for the player.
* `!goto <#id>` - Teleports you to the target player.
* `!bring <#id>` - Teleports the target player to you.
* `!freeze <#id> [seconds]` - Freezes the player, preventing movement.
* `!unfreeze <#id>` - Unfreezes the player.
* `!resize <#id> <scale>` - Enlarges or shrinks the player.
* `!drug <#id> [seconds]` - Gives the player a drug effect (screen wobble).
* `!burn <#id> [seconds] [damage_per_tick]` - Ignites the player.
* `!disarm <#id>` - Drops/removes the player's weapons.
* `!speed <#id> <multiplier>` - Changes the player's speed (Ex: `!speed #12 1.5`).
* `!gravity <#id> <multiplier>` - Changes the player's gravity.
* `!rename <#id> <new_name>` - Changes the player's name.
* `!hp <#id> <health>` - Sets the player's health.
* `!money <#id> <amount>` - Sets the player's money.
* `!give <#id> <item>` - Gives a weapon or item to the player (Ex: `!give #12 weapon_ak47`).

---

## ⚙️ Server Toggles
Enable/disable server-wide modes.

* `!hson` / `!hsoff` - Toggles Only Headshot mode.
* `!bunnyon` / `!bunnyoff` - Toggles Bunny Hop (Bhop) mode.
* `!respawnon` / `!respawnoff` - Toggles infinite auto-respawn.
* `!cvar <cvar> [value]` - Changes a server console variable (cvar).

> **Note:** All `!` chat commands have console equivalents using the `sw_` prefix. For example, `sw_ban` in console is the same as `!ban` in chat. Administrative management commands (`sw_addadmin`, `sw_addgroup`, etc.) must be run in the server console or by players with `admin.root` permission.

---

## 🔐 Permissions Overview

Default core permissions:

- `admin.root` -> full access, admin/group management (`sw_addadmin`, etc.), root bypass (`admin.*`, `*`).
- `admin.generic` -> `admin` menu, `warn`, `unwarn`, `who`, `asay/say/psay/csay/hsay`, `admintime`, `map`, `wsmap`, `rr/restart`, `calladmin`, `listplayers`, `vote`.
- `admin.ban` -> `ban`, `ipban`, `addban`, `unban`, `lastban`.
- `admin.kick` -> `kick`.
- `admin.mute` -> `mute/unmute`, `gag/ungag`, `silence/unsilence`.
- `admin.cheats` -> `slap`, `slay`, `respawn`, `team`, `noclip`, `goto`, `bring`, `freeze`, `unfreeze`, `resize`, `drug`, `burn`, `disarm`, `speed`, `gravity`, `rename`, `hp`, `money`, `give`.
- `admin.rcon` -> server toggles (`hson/hsoff`, `bhopon/bhopoff`, `respawnon/respawnoff`).
- `admin.cvar` -> `cvar`.

*`Report` permission is empty by default (`""`), meaning `!report` is open to everyone unless changed.*

## ⚙️ Recommended Setup Flow (Server Console)

**1. Create a group first:**
```text
sw_addgroup Owner admin.root 100
sw_addgroup Moderator admin.generic,admin.kick,admin.mute 50
```

**2. Assign an admin to the group:**
```text
sw_addadmin 76561198255550637 SSonic @Owner
sw_addadmin 7656119XXXXXXXXXX PlayerX Moderator,Helper 30
```
*(Optional last value is the duration in days).*

**3. Verify:**
```text
sw_listgroups
sw_listadmins
sw_adminreload
```
*Online target players receive their permissions and tags immediately.*

## 📁 File Notes

- `config.json` -> general settings & language.
- `commands.json` -> editable chat aliases.
- `permissions.json` -> permission mapping.
- `maps.json` -> normal & workshop map lists.
- `resources/translations/*.jsonc` -> localization files (`en`, `tr`, `de`, `fr`, `it`, `el`, `ru`, `bg`, `hu`).
