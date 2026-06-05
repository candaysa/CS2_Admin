# Changelog

## [1.0.15] - 2026-06-05

### Added
- **MuteManager trace logging parity with GagManager** (`src/Database/MuteManager.cs`): 5 noktaya detaylı trace log eklendi (GagManager standardı ile aynı seviyeye getirildi). `config.json`'da `Debug: true` iken sunucu konsolunda görünür:
  - `[CS2_Admin][Trace][Mute] add steamid=… muteId=… admin=… expiresAt=… reason=…` (INSERT sonrası)
  - `[CS2_Admin][Trace][Mute] unmute steamid=… muteId=… admin=… reason=…` (UPDATE sonrası)
  - `[CS2_Admin][Trace][Mute] cache-hit steamid=… muteId=… expiresAt=… cachedAt=…` (30 saniyelik in-memory cache hit)
  - `[CS2_Admin][Trace][Mute] db-load-active steamid=… muteId=… admin=… createdAt=… expiresAt=… reason=…` (DB'den aktif mute çekildi)
  - `[CS2_Admin][Trace][Mute] db-load-none steamid=…` (DB'de aktif mute yok, yeni mute INSERT'i için yol açık)
- **BanManager trace logging parity with MuteManager** (`src/Database/BanManager.cs`): 4 noktaya detaylı trace log eklendi. `Debug: true` iken sunucu konsolunda görünür:
  - `[CS2_Admin][Trace][Ban] add steamid=… target=… admin=… expiresAt=… isGlobal=…` (INSERT sonrası)
  - `[CS2_Admin][Trace][Ban] cache-hit steamid=… banId=… expiresAt=… cachedAt=…` (30 saniyelik in-memory cache hit)
  - `[CS2_Admin][Trace][Ban] db-load-active steamid=… banId=… admin=… createdAt=… expiresAt=… reason=…` (DB'den aktif ban çekildi)
  - `[CS2_Admin][Trace][Ban] db-load-none steamid=…` (DB'de aktif ban yok, yeni ban INSERT'i için yol açık)
  - `[CS2_Admin][Trace][Ban] enforcement-load-active steamid=… banId=…` (enforcement sırasında DB'den aktif ban çekildi — bağlantı anında kontrol)
  - `[CS2_Admin][Trace][Ban] enforcement-load-none steamid=…` (enforcement sırasında DB'de ban yok, oyuncu bağlanabilir)

### Fixed
- **Ban command no longer trusts stale cache for duplicate checks** (`src/Database/BanManager.cs`, `src/Commands/BanCommand.cs`, `IpBanCommand.cs`, `AddBanCommand.cs`, `LastBanCommand.cs`): `!ban` / `!ipban` / offline ban duplicate checks now bypass the in-memory cache and query MySQL directly before deciding "already banned". This fixes the case where a player can be online and enforceable as not banned, but the admin command still returns "already banned" from stale process-local cache. Ban enforcement fallback was also aligned with `target_type`, so Steam bans match SteamID and IP bans match IP only.
- **Mute/Gag/Silence duplicate checks no longer trust stale cache** (`src/Database/MuteManager.cs`, `GagManager.cs`, `src/Commands/MuteCommand.cs`, `GagCommand.cs`, `SilenceCommand.cs`, `LastBanCommand.cs`): `!mute`, `!gag`, `!silence`, and last-player mute/gag actions now bypass in-memory cache and query MySQL directly before deciding "already muted/gagged/silenced". `!silence` DB failures now use a silence-specific translation key instead of `ban_db_error`.
- **Stale in-memory cache causing false "already muted/banned/gagged/warned" in multi-server setups** (`src/Database/MuteManager.cs`, `GagManager.cs`, `WarnManager.cs`, `BanManager.cs` + 5 komut): 5-6 sunucu aynı MySQL DB'ye bağlıyken her sunucunun kendi process-lokal cache'i vardı ve 5 dakika boyunca stale data dönüyordu. Bir sunucuda mute ekleyip diğer sunucuda aynı oyuncuya mute atmaya çalışırken 5 dakika boyunca "already muted" yanıltıcı cevabı alınıyordu. Aynı sorun `unmute` exception durumunda da ortaya çıkıyordu: DB UPDATE başarısız olursa cache temizlenmiyor, 5 dakika boyunca stale kalıyordu. Çözüm: (1) Tüm 4 manager'da cache TTL `5 dakika` → `30 saniye` (multi-server senkronizasyon penceresi 10× kısaldı), (2) Her manager'a `InvalidateCache(...)` public method eklendi (Mute/Gag/Warn: `InvalidateCache(ulong steamId)`, Ban: `InvalidateCache(ulong steamId, string? ipAddress)` hem steam hem IP key), (3) MuteCommand, GagCommand, WarnCommand, BanCommand, IpBanCommand INSERT öncesi ilgili cache key'leri zorla temizliyor, böylece `AddXAsync` sonrası cache miss zorla → yeni sanction cache'e yazılıyor → eski stale data "already X" yanıltıcı cevabına yol açmıyor.
- **"Already banned" false positive when oyuncu oyuna bağlanabiliyor** (`src/Database/BanManager.cs` + 4 noktaya trace log eklendi): Kullanıcı bildirimine göre oyuncu daha önce hiç sunucuya girmemiş veya banlanmamış olmasına rağmen `!ban` komutu "already banned" dönüyordu, oysa enforcement oyuncuyu bağlantıda kontrol ettiğinde ban görmüyordu (oyuncu oyuna bağlanabiliyor, küfür edebiliyor). Bu mantıksal çelişki BanManager'ın enforcement ile BanCommand için aynı kod yolunu izlemesi gerektiğini gösteriyordu. BanManager'a enforcement ve command için ayrı trace log'lar eklendi (`db-load-active` vs `enforcement-load-active`), böylece production'da iki kod yolunun ne döndüğü karşılaştırılabilir ve kök neden (Dapper null davranışı, multiServerEnabled filter tutarsızlığı, veya DI scope problemi) net tespit edilip kalıcı fix uygulanabilir.

### Changed
- **All 4 manager cache TTL unified to 30 seconds** (`src/Database/MuteManager.cs`, `GagManager.cs`, `WarnManager.cs`, `BanManager.cs`): Multi-server deployment (5-6 sunucu ortak DB) için stale data penceresi 10× kısaldı.
- **Plugin version rolled back to 1.0.15** (`src/CS2_Admin.cs`): GitHub'da henüz v1.0.15 paylaşılmadığı ve test edilmemiş değişiklikler (ban trace logs) içerdiği için v1.0.16 release adımı atlandı, tüm değişiklikler v1.0.15 olarak yayınlanacak.

### Notes
- **Bu release v1.0.14'ten hemen sonra geliyor, GitHub'a henüz pushlanmadı.** Kullanıcı 5-6 sunuculu multi-server setup kullanıyor, tüm değişiklikler bu senaryo için kritik.
- Multi-server deployment (5-6 sunucu ortak DB) için kritik fix. Tek sunuculu kurulumlarda da unmute exception edge case'ini kapatıyor.
- Cache invalidation trace log'ları (`[CS2_Admin][Trace][Mute/Gag/Warn/Ban] cache-invalidate ...`) `Debug: true` iken görünür, sorun debug'ı için bilgi sağlar.
- BanManager trace log'ları "already banned" senaryosu için gözlem aletini sağlıyor: enforcement ve command iki farklı kod yolu olduğundan, `enforcement-load-active` vs `db-load-active` trace'leri karşılaştırılarak hangi kod yolunun yanlış veri döndüğü tespit edilir.
- Performans etkisi: 30 saniye cache TTL ile DB sorgu sayısı 10× artar, ama mute/gag/warn/ban komutları zaten nadir kullanılır (saatlik birkaç kez), pratikte ihmal edilebilir.

---

## [1.0.14] - 2026-06-04

### Added
- **Auto-Updater config toggle** (`src/Config/PluginConfig.cs` + `src/CS2_Admin.cs`): `config.json` `AutoUpdate` alanı (default `true`). `false` yapılırsa ilk yükleme ve 1-saatlik periyodik güncelleme kontrolü tamamen atlanır, konsola bilgi logu yazılır. Sunucu sahipleri artık auto-updater'ı config dosyasından disable edebilir.

### Fixed
- **Hungarian `??` mojibake in Discord server status fields** (`resources/language/hu.jsonc`): 8 anahtardaki (`discord_server_status_online/offline/map_field/players_field/ip_field/status_field/connect_field`) BMP-ötesi emoji karakterleri UTF-8'de `??` olarak bozulmuştu. Sade çeviri metni bırakıldı (emoji kod tarafında gömülü olmadığı için, çeviri metni temiz).
- **Headshot-only mode (`!hson`) silently no-op** (`src/Commands/HsToggleCommand.cs`): `sv_headshot_only 1/0` Valve tarafından bilinmeyen sahte bir concommand'dı, broadcast mesajı çıkıyor ama oyun mantığı tetiklenmiyordu. CS2'nin yerleşik `mp_damage_headshot_only` cvar'ına geçildi (1=on, 0=off).
- **`!warn` no-arg opens menu instead of usage** (`src/Commands/WarnCommand.cs`): `args.Length == 0` durumunda `OpenWarnTargetMenu` açılıyordu, diğer komutlar gibi usage göstermesi bekleniyordu. Menü mantığı kaldırıldı, artık `!warn` yazıldığında `warn_usage` mesajı görünüyor.
- **`!unrename` "SendMessage must be called from main thread" exception** (`src/Commands/UnrenameCommand.cs`): `Execute` async void, `await GetOriginalNameAsync()` sonrası `BroadcastNotification` farklı thread'de kalıyor, `Player.SendChat` native call'ı patlatıyordu. `Core.Scheduler.NextTick(() => BroadcastNotification(...))` ile main thread'e ertelendi. Console'da `Unrename command failed` hatası + chat mesajı kaybolması giderildi.
- **Discord chat `!` / `/` prefix filter too aggressive** (`src/Utils/DiscordNotificationService.cs` + `src/Utils/DiscordBotService.cs`): `!cool!` veya `!good` gibi günlük yazışmalar da skip ediliyordu. Akıllı filter eklendi: mesajın ilk kelimesi `!` veya `/` strip edildikten sonra `CommandsConfig`'deki bilinen tüm alias listesinde yoksa Discord'a gönderilir. Reflection ile `List<string>` property'lerden komut alias set'i oluşturuluyor (`CollectCommandAliases`).
- **Chat tag skipped when message starts with `!` or `/`** (`src/Events/EventRegistrar.cs` + `src/Utils/CommandAliasResolver.cs`): `!cool!` veya `!korkma` gibi günlük yazışmalarda oyuncu tag'i gözükmüyordu, sadece `$S0nic: !korkma` şeklinde ham mesaj yayınlanıyordu. Sebep: `OnClientChat` `!` veya `/` ile başlayan her mesajda `HookResult.Handled` dönüyor, tag ekleme mantığına hiç girilmiyordu. Akıllı kontrol eklendi: prefix `!` veya `/` VE ilk kelime bilinen plugin/CS2 komutuysa → Handled (oyunun default mesajı gözüksün, tag ekleme), aksi halde → tag ekle ve `HookResult.Stop` ile oyunun default mesajını engelle. Alias set'i `CommandAliasResolver.BuildSet` ile merkezi olarak hesaplanıp hem `EventRegistrar` hem `DiscordNotificationService` arasında paylaşılıyor.

### Changed
- **All config versions unified to 7** (`src/Config/PluginConfig.cs`): 7 config sınıfı (`PluginConfig`, `ChatTagsFileConfig`, `DiscordFileConfig`, `MessagesConfig`, `AfkFileConfig`, `CommandsConfig`, `PermissionsConfig`, `MapsFileConfig`) `CurrentVersion = 6` → `7`. `MessagesConfig`'e yeni `Version` field eklendi (diğerleriyle tutarlı olsun diye). Eski config dosyaları otomatik migrate olur (ConfigMigrator generic, kullanıcı özel değerlerini korur).
- **Plugin version bumped to 1.0.14** (`src/CS2_Admin.cs`).

---

## [1.0.13] - 2026-06-04

### Added
- **Auto-Updater system** (`src/Utils/AutoUpdater.cs`): GitHub releases üzerinden otomatik güncelleme.
  - İlk yüklemede + her 1 saatte periyodik kontrol
  - Stream-based download (büyük dosyaları belleğe yüklemeden)
  - Semver-aware versiyon karşılaştırma (prerelease desteği)
  - Kullanıcı dosyalarını koruma (config, permissions vb. asla üzerine yazılmaz)
  - Swiftly auto-reload uyumlu (DLL lock sorunu yok)
- **Chat tag debug logging** (`src/Events/EventRegistrar.cs`): `config.json` `Debug: true` ile chat tag çözümleme adımları loglanır. Tag gözükmeme nedenini tespit etmek için.
- **Chat tag preset renkler** (`src/Utils/ChatTagConfigManager.cs`): Bilinen 8 grup adı için hazır renk paleti (owner=kırmızı, admin=koyu kırmızı, moderator=mavi, vip=altın vb.).
- **AdminTimeSend auto-reset** (`src/Commands/AdminTimeSendCommand.cs`): `!admintimesend` komutu gönderim sonrası tüm admin playtime sayaçlarını sıfırlar.

### Fixed
- **JSON deserialization bug** (`src/Utils/AutoUpdater.cs`): GitHub API snake_case alanlar (`tag_name`, `browser_download_url`, `assets`) PascalCase property'lere bağlanmıyordu. `[JsonPropertyName]` attribute'ları eklendi.
- **HttpClient timeout** (`src/Utils/AutoUpdater.cs`): 1 dakikalık timeout eklendi (sonsuz bekleme riski kalktı).
- **Memory exhaustion on download** (`src/Utils/AutoUpdater.cs`): `GetByteArrayAsync` (tüm dosyayı belleğe yükler) → `CopyToAsync` ile stream-based yazma.
- **Error logging severity** (`src/Utils/AutoUpdater.cs`): Exception catch'leri `LogWarning` → `LogError` + stack trace.
- **Version comparison** (`src/Utils/AutoUpdater.cs`): `1.0.0-beta` vs `1.0.0` gibi semver senaryoları doğru handle ediliyor (prerelease < release).
- **Legacy `Enabled` field bug** (`src/Utils/ChatTagConfigManager.cs`): Eski `Enabled: false` alanı `ChatEnabled: true`'yu eziyordu. Artık legacy alan sadece temizleniyor, `ChatEnabled`'a dokunulmuyor.
- **Chat tag duplicate messages** (`src/Events/EventRegistrar.cs`): `HookResult.Handled` oyunun default chat mesajını engellemiyordu → `HookResult.Stop`'a değiştirildi. Artık tek mesaj gözüküyor.
- **Default tag styles** (`src/Utils/ChatTagConfigManager.cs`): `CreateDefaultStyle()` artık boş string yerine yeşil/beyaz/default renkler döner.
- **Language files path** (`src/CS2_Admin.cs`): Çeviri dosyaları `configs/plugins/CS2_Admin/language` → `plugins/CS2_Admin/resources/language`. AutoUpdater ile birlikte plugin ile birlikte gelir, kullanıcı üzerine yazma riski yok.
- **MySQL utf8mb4 4-byte encoding crash** (`src/Database/Migrations/ConvertStringColumnsToUtf8Mb4.cs` + `src/Utils/SafeName.cs`): Emoji, matematiksel sembol (U+1D668 gibi Supplementary Multilingual Plane) veya diğer 4-byte UTF-8 karakterler içeren oyuncu isimleri (`admin_bans.target_name`, `admin_player_sessions` vb. 17 tablo) utf8 (3-byte) kolonlara INSERT olurken `Incorrect string value: '\xF0\x9D...'` hatası fırlatıyor, ban/mute/gag/warn sessizce başarısız oluyordu. Root cause: kolon karakter seti + `SafeName` regex'i surrogate pair'leri strip etmiyordu (`[^\u0000-\uFFFF]` UTF-16 code unit olarak çalışır). Çözüm: FluentMigrator ile 17 tablo `utf8mb4` + `utf8mb4_unicode_ci`'ye migrate edildi, `SafeName.ForPlayer` artık doğru surrogate strip (`[\uD800-\uDBFF\uDC00-\uDFFF]`) yapıyor, `BanManager` ve `AdminLogManager` da ilk defa `target_name`/`admin_name`/`reason` sanitize ediyor.
- **Misleading "already banned" message on DB error** (`src/Commands/BanCommand.cs`, `src/Commands/IpBanCommand.cs`, `src/Commands/MuteCommand.cs`, `src/Commands/GagCommand.cs`, `src/Commands/WarnCommand.cs`, `src/Commands/SilenceCommand.cs`): INSERT başarısız olduğunda admin'e `player_already_banned` / `player_already_gagged` gösterilip oyuncu aslında yasaklanmıyordu. Artık `steamDbError` / `ipDbError` / `muteOk` / `gagOk` / `ok` flag'leri ile DB hatası "already X" durumundan ayrıştırılıyor, ayrı `ban_db_error` / `mute_db_error` / `gag_db_error` / `warn_db_error` çeviri anahtarları eklendi (en/tr/hu).

### Changed
- **All config versions unified to 6** (`src/Config/PluginConfig.cs`): `PluginConfig`, `ChatTagsFileConfig`, `DiscordFileConfig`, `AfkFileConfig`, `CommandsConfig`, `PermissionsConfig`, `MapsFileConfig` hepsi `CurrentVersion = 6`. Eski config dosyaları otomatik migrate olur.
- **AutoUpdater useless `Task.Run` wrappers removed** (`src/CS2_Admin.cs`): Async metotlar zaten async, gereksiz thread pool sarmalaması kaldırıldı.
- **Example config cleaned** (`example_configs/tags.json`): Legacy `Enabled: false` kaldırıldı, `ChatEnabled: true` yapıldı (default değerle uyumlu).

### Notes
- Pterodactyl panel uyumlu: `.tmp` uzantısı kullanılmıyor, sadece `cs2admin_update_temp.zip` ve `cs2admin_update_extracted/` (parent dizinde, otomatik temizlenir).
- Kullanıcı restart gerekmez: Swiftly DLL değişikliğini algılayıp otomatik reload yapar.
- Test adımı: `PluginMetadata.Version` geçici olarak `0.5.0` yapıldığında updater `v1.0.12`'yi indirip auto-reload yapıyor. Test sonrası `1.0.13`'e geri alındı.
