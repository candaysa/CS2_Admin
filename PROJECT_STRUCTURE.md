# CS2_Admin2 Project Structure

```
CS2_Admin2/
│
├── src/
│   ├── CS2_Admin.cs                          # Plugin entry point (~200 lines)
│   │
│   ├── Commands/
│   │   ├── ICommand.cs                       # Command interface
│   │   ├── CommandBase.cs                    # Base class (permission, reply, localization)
│   │   │
│   │   ├── BanCommand.cs                     # !ban
│   │   ├── UnbanCommand.cs                   # !unban
│   │   ├── AddBanCommand.cs                  # !addban
│   │   ├── IpBanCommand.cs                   # !ipban
│   │   ├── LastBanCommand.cs                 # !lastban
│   │   │
│   │   ├── MuteCommand.cs                    # !mute
│   │   ├── UnmuteCommand.cs                  # !unmute
│   │   ├── GagCommand.cs                     # !gag
│   │   ├── UngagCommand.cs                   # !ungag
│   │   ├── SilenceCommand.cs                 # !silence
│   │   ├── UnsilenceCommand.cs               # !unsilence
│   │   │
│   │   ├── WarnCommand.cs                    # !warn
│   │   ├── UnwarnCommand.cs                  # !unwarn
│   │   │
│   │   ├── KickCommand.cs                    # !kick
│   │   ├── SlapCommand.cs                    # !slap
│   │   ├── SlayCommand.cs                    # !slay
│   │   ├── RespawnCommand.cs                 # !respawn / !revive
│   │   ├── TeamCommand.cs                    # !team / !swap
│   │   ├── MixTeamCommand.cs                 # !mixteam
│   │   ├── NoClipCommand.cs                  # !noclip
│   │   ├── GotoCommand.cs                    # !goto
│   │   ├── BringCommand.cs                   # !bring
│   │   ├── FreezeCommand.cs                  # !freeze
│   │   ├── UnfreezeCommand.cs                # !unfreeze
│   │   ├── ResizeCommand.cs                  # !resize
│   │   ├── DrugCommand.cs                    # !drug
│   │   ├── BurnCommand.cs                    # !burn
│   │   ├── BeaconCommand.cs                  # !beacon
│   │   ├── BlindCommand.cs                   # !blind
│   │   ├── GlowCommand.cs                    # !glow
│   │   ├── DisarmCommand.cs                  # !disarm
│   │   ├── SpeedCommand.cs                   # !speed
│   │   ├── GravityCommand.cs                 # !gravity
│   │   ├── RenameCommand.cs                  # !rename
│   │   ├── UnrenameCommand.cs                # !unrename
│   │   ├── HpCommand.cs                      # !hp
│   │   ├── MoneyCommand.cs                   # !money
│   │   ├── GiveCommand.cs                    # !give
│   │   ├── ListPlayersCommand.cs             # !listplayers
│   │   ├── WhoCommand.cs                     # !who
│   │   │
│   │   ├── AsayCommand.cs                    # !asay
│   │   ├── SayCommand.cs                     # !say
│   │   ├── PsayCommand.cs                    # !psay
│   │   ├── CsayCommand.cs                    # !csay
│   │   ├── HsayCommand.cs                    # !hsay
│   │   ├── CallAdminCommand.cs               # !calladmin
│   │   ├── ReportCommand.cs                  # !report
│   │   │
│   │   ├── MapCommand.cs                     # !map
│   │   ├── WsMapCommand.cs                   # !wsmap
│   │   ├── RestartCommand.cs                 # !restart / !rr
│   │   ├── RconCommand.cs                    # !rcon
│   │   ├── CvarCommand.cs                    # !cvar
│   │   ├── VoteCommand.cs                    # !vote
│   │   │
│   │   ├── HsToggleCommand.cs                # !hson / !hsoff
│   │   ├── BunnyToggleCommand.cs             # !bunnyon / !bunnyoff
│   │   ├── RespawnToggleCommand.cs           # !respawnon / !respawnoff
│   │   │
│   │   ├── AdminMenuCommand.cs               # !admin
│   │   ├── AddAdminCommand.cs                # !addadmin
│   │   ├── EditAdminCommand.cs               # !editadmin
│   │   ├── RemoveAdminCommand.cs             # !removeadmin
│   │   ├── ListAdminsCommand.cs              # !listadmins / !admins
│   │   ├── AddGroupCommand.cs                # !addgroup
│   │   ├── EditGroupCommand.cs               # !editgroup
│   │   ├── RemoveGroupCommand.cs             # !removegroup
│   │   ├── ListGroupsCommand.cs              # !listgroups
│   │   ├── AdminReloadCommand.cs             # !adminreload
│   │   │
│   │   ├── AdminTimeCommand.cs               # !admintime
│   │   └── AdminTimeSendCommand.cs           # !admintimesend
│   │
│   ├── Services/
│   │   ├── PlayerService.cs                  # Player lookup, #id parsing, targeting
│   │   ├── PermissionService.cs              # Permission checking logic
│   │   ├── CommandRegistrationService.cs     # Command/alias registration
│   │   ├── SanctionCheckService.cs           # Ban/mute/gag check on connect
│   │   ├── ChatTagService.cs                 # Chat tag assignment
│   │   └── LocalizerService.cs               # Localization helper
│   │
│   ├── Events/
│   │   ├── EventRegistrar.cs                 # Event hook registration/dispatch
│   │   ├── PlayerConnectEvents.cs            # OnClientPutInServer, SteamAuthorize, Disconnect
│   │   ├── GameRoundEvents.cs                # RoundStart, RoundEnd
│   │   └── ChatEvents.cs                    # Chat message interception
│   │
│   ├── Discord/
│   │   ├── DiscordBotService.cs              # Bot connection, send message, event loop
│   │   ├── DiscordEmbeds.cs                  # Embed message builders
│   │   ├── DiscordChannels.cs                # Channel ID management
│   │   └── DiscordServerStatus.cs            # Server status auto-update
│   │
│   ├── Database/
│   │   ├── ConnectionFactory.cs              # DB connection factory (extracted)
│   │   ├── AdminDbManager.cs                 # Admin CRUD
│   │   ├── BanManager.cs                     # Ban CRUD + queries
│   │   ├── MuteManager.cs                    # Mute CRUD
│   │   ├── GagManager.cs                     # Gag CRUD
│   │   ├── WarnManager.cs                    # Warn CRUD
│   │   ├── GroupDbManager.cs                 # Group CRUD
│   │   ├── PlayerSessionManager.cs           # Session tracking
│   │   ├── PlayerIpDbManager.cs              # IP history
│   │   ├── PlayerNameHistoryManager.cs       # Name history
│   │   ├── AdminLogManager.cs                # Action logging
│   │   ├── AdminPlaytimeDbManager.cs         # Playtime tracking
│   │   ├── ServerInfoDbManager.cs            # Server info
│   │   ├── DiscordServerStatusDbManager.cs   # Discord status state
│   │   ├── DiscordMessageStateDbManager.cs   # Discord message dedup
│   │   ├── RankLeaderboardDbManager.cs       # Leaderboard
│   │   ├── MigrationRunner.cs                # Migration orchestration
│   │   ├── PunishmentQueryCompat.cs          # Query compatibility
│   │   └── Migrations/
│   │       ├── AddAdminsTable.cs
│   │       ├── AddGroupsTable.cs
│   │       ├── AddBansTable.cs
│   │       ├── AddMutesTable.cs
│   │       ├── AddGagsTable.cs
│   │       ├── AddWarnsTable.cs
│   │       ├── AddAdminLogsTable.cs
│   │       ├── AddAdminActionsLogTable.cs
│   │       ├── AddPlayerIpsTable.cs
│   │       ├── AddPlayerIpHistoryTable.cs
│   │       ├── AddPlayerNamesHistoryTable.cs
│   │       ├── AddPlayerCustomNamesTable.cs
│   │       ├── AddPlayerSessionsTable.cs
│   │       ├── AddServersTable.cs
│   │       ├── AddAdminPlaytimeTable.cs
│   │       ├── AddDiscordMessageStateTable.cs
│   │       ├── AddDiscordServerStatusTable.cs
│   │       └── EnsureAdminAdminsColumns.cs
│   │
│   ├── Config/
│   │   └── PluginConfig.cs                   # Config models (unchanged)
│   │
│   ├── Models/                               # Entity models (unchanged)
│   │   ├── Admin.cs
│   │   ├── AdminActionLogRecord.cs
│   │   ├── AdminContext.cs
│   │   ├── AdminGroup.cs
│   │   ├── AdminLog.cs
│   │   ├── AdminPlaytime.cs
│   │   ├── Ban.cs
│   │   ├── DiscordServerStatus.cs
│   │   ├── DiscordSharedMessageState.cs
│   │   ├── Gag.cs
│   │   ├── Mute.cs
│   │   ├── PlayerCustomNameRecord.cs
│   │   ├── PlayerIpHistoryRecord.cs
│   │   ├── PlayerIpRecord.cs
│   │   ├── PlayerNameHistoryRecord.cs
│   │   ├── PlayerSessionRecord.cs
│   │   ├── ServerInfo.cs
│   │   └── Warn.cs
│   │
│   ├── Menu/
│   │   ├── AdminMenuManager.cs               # Menu builder
│   │   ├── IAdminMenuHandler.cs              # Menu handler interface
│   │   └── Handlers/
│   │       ├── AdminManagementHandler.cs     # Admin CRUD menu
│   │       ├── FunCommandsMenuHandler.cs     # Fun commands menu
│   │       ├── PlayerManagementHandler.cs    # Player management menu
│   │       └── ServerManagementHandler.cs    # Server management menu
│   │
│   └── Utils/
│       ├── AfkManagerService.cs              # AFK detection
│       ├── ChatTagConfigManager.cs           # Tag configuration
│       ├── JsonFileLocalizer.cs              # JSON translation loader
│       ├── PluginLocalizer.cs                # Localization service
│       ├── LoggerExtensions.cs               # Logging helpers
│       ├── ServerIdentity.cs                 # Server identification
│       ├── SanctionDurationParser.cs         # Duration string parser
│       ├── RecentPlayersTracker.cs           # Recent player tracking
│       └── DebugSettings.cs                  # Debug flags
│
├── resources/
│   ├── gamedata/
│   │   ├── offsets.jsonc
│   │   ├── patches.jsonc
│   │   └── signatures.jsonc
│   ├── language/
│   │   └── (bg, de, el, en, fr, hu, it, ru, tr).jsonc
│   └── translations/
│       └── (bg, de, el, en, fr, hu, it, ru, tr).jsonc
│
├── commands.json                              # Command alias definitions
├── config.json                                # Main plugin configuration
├── permissions.json                           # Permission mappings
├── discord.json                               # Discord bot configuration
├── maps.json                                  # Map list
├── tags.json                                  # Chat tag configuration
├── CS2_Admin2.csproj                          # Project file
└── CS2_Admin2.sln                             # Solution file
```

## File Size Targets

| Category | Files | Avg Size | Max Size |
|----------|-------|----------|----------|
| Commands (each) | 55+ | 40-80 lines | 120 lines |
| Services | 6 | 100-200 lines | 300 lines |
| Events | 4 | 200-350 lines | 400 lines |
| Discord | 4 | 100-500 lines | 600 lines |
| Database managers | 16 | 50-300 lines | 700 lines |
| Menu | 5 | 50-300 lines | 600 lines |
| Models | 18 | 10-80 lines | 80 lines |
| Utils | 9 | 30-400 lines | 400 lines |
| **CS2_Admin.cs** | 1 | **~200 lines** | **200 lines** |
