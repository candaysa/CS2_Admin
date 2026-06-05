# CS2_Admin — Bug Fix & Refactor Planı

**Tarih:** 2026-06-03
**Hazırlayan:** opencode (MiniMax-M3)
**Plugin Sürümü:** 1.0.13
**Hedef Sürüm:** 1.0.14

---

## 📋 Özet

Bu plan, CS2_Admin plugin'i için tespit edilen bug'ları ve refactor ihtiyaçlarını içerir. Bulgular 4 ana kategoride gruplanmıştır:

| Öncelik | Kategori | Adet | Tahmini Süre |
|--------|----------|------|--------------|
| 🔴 P0 | Kritik Bug'lar | 5 | 10 dk |
| 🟡 P1 | Dil & Yerelleştirme Hataları | 6+ | 20 dk |
| 🟠 P2 | Thread Safety | 3 | 15 dk |
| 🔵 P3 | Sync-Over-Async Refactor | 28+ | 60-90 dk |
| 🟢 P4 | Sessiz Hata Yutma (Empty Catch) | 11 | 10 dk |
| **Toplam** | | **~55 madde** | **~2-2.5 saat** |

---

## 🔴 P0 — KRİTİK BUG'LAR (Acil Düzeltme)

### P0-1: `LogErrorIfEnabled` Hataları Debug Ayarına Bağlı ❌

**Dosya:** `src/Utils/LoggerExtensions.cs:24-38`

**Sorun:** `DebugSettings.LoggingEnabled == false` ise tüm exception logları sessizce yutuluyor. 80+ yerde `LogErrorIfEnabled` kullanılmış → kullanıcı hatalardan haberdar olmuyor.

**Düzeltme:** `LogError` her zaman loglanmalı, sadece `LogInformation`/`LogWarning` Debug-gated olmalı.

```csharp
public static void LogErrorIfEnabled(this ILogger logger, string message, params object?[] args)
{
    logger.LogError(message, args); // Debug check KALDIR
}

public static void LogErrorIfEnabled(this ILogger logger, Exception exception, string message, params object?[] args)
{
    logger.LogError(exception, message, args); // Debug check KALDIR
}
```

**Etki:** Tüm plugin — kullanıcı artık hataları görecek.

---

### P0-2: `tr.jsonc:75` "SENTAM SUSTURULDUNUZ" Yazım Hatası ❌

**Dosya:** `resources/language/tr.jsonc:75`

**Mevcut:**
```json
"unsilenced_personal_html": "SENTAM SUSTURULDUNUZ",
```

**Sorun:** "SENTAM" diye bir Türkçe kelime yok. Muhtemelen "SESSİZ" yazılmak istenirken harfler bozuk yazılmış. Kullanıcının "lebel" gibi okuduğu şey bu — font bozukluğu DEĞİL, düz yazım hatası.

**Düzeltme:**
```json
"unsilenced_personal_html": "SUSTURMANIZ KALDIRILDI",
```

veya alternatifler:
- `"TAM SUSTURULMANIZ KALDIRILDI"`
- `"SESSİZLEştİRMENİZ KALDIRILDI"`

**Kontrol:** UnsilenceCommand.cs:121 → `L("unsilenced_personal_html")` (parametresiz, çıktı direkt HTML)

---

### P0-3: `tr.jsonc:93` NoClip Placeholder Index Hatası ❌

**Dosya:** `resources/language/tr.jsonc:93`

**Mevcut:**
```json
"noclip_toggled_personal_html": "NOCLIP {1}",
```

**Sorun:** NoClipCommand.cs:76'da `L("noclip_toggled_personal_html", stateLabel)` çağrılıyor. C# 0-indexli → stateLabel = `{0}`. Ama tr.jsonc'de `{1}` yazılmış → parametre yok, sadece "NOCLIP " gözükür.

**EN/HU doğru:** `"NOCLIP {0}"`

**Düzeltme:**
```json
"noclip_toggled_personal_html": "NOCLIP {0}",
```

**Etki:** Türkçe kullanan oyuncular noclip mesajında durum (AÇIK/KAPALI) göremiyor.

---

### P0-4: `tr.jsonc` Zero-Width Space Karakterleri ❌

**Dosya:** `resources/language/tr.jsonc`

**Sorun:** 3 satırda görünmez U+200B (zero-width space) karakteri var. CS2'de bunlar bozuk font kutusu olarak gözükebilir.

**Tespit Edilen Yerler:**

| Satır | Key | Mevcut |
|------|-----|--------|
| 119 | `cvar_value` | `"{0} \u200B\u200B= {1}"` |
| 173 | `listgroups_entry` | `"{0} \u200B\u200B\| imm={1} \| yetkiler={2}"` |
| 178 | `players_list_entry` | `"#{0} \u200B\u200B\| {1} \| {2}"` |

**Düzeltme:** U+200B karakterlerini sil, normal boşluk kullan.

```json
"cvar_value": "{0} = {1}",
"listgroups_entry": "{0} | imm={1} | yetkiler={2}",
"players_list_entry": "#{0} | {1} | {2}",
```

---

### P0-5: `CS2_Admin.cs:803` Komut Kayıt Hataları Yutuluyor ❌

**Dosya:** `src/CS2_Admin.cs:803`

**Sorun:** `EnsureCommandsRegistered` exception'ı yutuluyor. 60+ komut kaydı var; biri bile başarısız olsa sessizce geçiyor.

**Mevcut:**
```csharp
catch
{
}
```

**Düzeltme:**
```csharp
catch (Exception ex)
{
    Core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] Failed to register commands");
}
```

**Etki:** Hangi komutun neden kayıt edilemediğini artık loglayacak.

---

## 🟡 P1 — DİL & YERELLEŞTİRME HATALARI

### P1-1: `tr.jsonc:368` "Ateşe Vertı" Yazım Hatası

**Mevcut:**
```json
"burn_notification": "{0}, {1} oyuncuyu {2} saniye Ateşe Vertı ({3} hasar/tick).",
```

**Sorun:** "Vertı" yazım hatası. "Verdi" olmalı.

**Düzeltme:**
```json
"burn_notification": "{0}, {1} oyuncuyu {2} saniye Ateşe Verdi ({3} hasar/tick).",
```

---

### P1-2: `tr.jsonc:357, 367` Büyük-Küçük Harf Karışmış

**Satır 357:**
```json
"restart_notification": "{0} oyunu {1} saniye içinde yeniden başlAtıyor...",
```
**Düzeltme:** `"yeniden başlatıyor..."` (A küçük olmalı)

**Satır 367:**
```json
"beacon_stopped": "{0} oyuncu için İşaretle (Beacon) kapAtıldı.",
```
**Düzeltme:** `"kapatıldı"` (A küçük olmalı)

---

### P1-3: `tr.jsonc:62, 329` "Tıkalı" Yanlış Anlam

**Satır 62:**
```json
"player_already_gagged": "Oyuncu {0} zaten tıkalı.",
```
**Sorun:** "Tıkalı" burun tıkanıklığı anlamında. Gag bağlamında "yazı engeli var" demek daha doğru.
**Düzeltme:** `"Oyuncu {0} zaten yazı engelli."`

**Satır 329:**
```json
"gagged_chat_warning_permanent": "Kalıcı olarak ağzınız tıkalı ve sohbet edemiyorsunuz.",
```
**Düzeltme:** `"Kalıcı olarak yazılı sohbetten engellendiniz."`

---

### P1-4: `tr.jsonc:368` Yarı İngilizce Yarı Türkçe

```json
"burn_notification": "...({3} hasar/tick).",
```

**Düzeltme:** `"...({3} hasar/saniye)."` veya sadece `"...({3} hasar)."`

---

### P1-5: `tr.jsonc:365-366` Beacon Tutarsızlığı

```json
"beacon_usage": "Kullanım: !İşaretle (Beacon) <hedef> [saniye|off]",
"beacon_started": "{0}, {1} oyuncuda İşaretle (Beacon) efektini {2} saniye açtı.",
```

**Sorun:** "İşaretle" Türkçe parantez içinde İngilizce isim — tutarsız.

**Düzeltme:** İkisini de `"İşaretle"` yap veya parantezi kaldır.

---

### P1-6: EN/HU Karşılaştırması — Eksik Key Kontrolü

Yapılacak: Tüm 3 dil dosyasındaki key'leri karşılaştır, eksik key'leri tespit et. Özellikle:
- `tr.jsonc:281-285` — `"speed_usage", "gravity_usage", "rename_usage", "hp_usage", "money_usage"` — bunlar dead code mu kontrol et
- `tr.jsonc:310, 313-316, 359` — `"süre"`, `"süre_permanent"` — bunlar C# tarafında `L("duration_...")` olarak mı kullanılıyor yoksa ölü key mi?

---

## 🟠 P2 — THREAD SAFETY (Race Condition Riski)

### P2-1: `EventRegistrar.cs:42` `_lastKnownAdminTags`

**Mevcut:**
```csharp
private readonly Dictionary<ulong, string> _lastKnownAdminTags = new();
```

**Düzeltme:**
```csharp
private readonly ConcurrentDictionary<ulong, string> _lastKnownAdminTags = new();
```

**Kullanım:** `EventRegistrar.cs:261, 284` (TryGetValue, indexer set)

---

### P2-2: `CS2_Admin.cs` `_connectedPlayersCache`

**Mevcut:**
```csharp
private readonly Dictionary<ulong, CachedPlayerInfo> _connectedPlayersCache = new();
```

**Düzeltme:**
```csharp
private readonly ConcurrentDictionary<ulong, CachedPlayerInfo> _connectedPlayersCache = new();
```

---

### P2-3: `EventRegistrar.cs` `_gagWarnTimestamps`

**Mevcut:**
```csharp
private readonly Dictionary<ulong, DateTime> _gagWarnTimestamps = new();
```

**Düzeltme:**
```csharp
private readonly ConcurrentDictionary<ulong, DateTime> _gagWarnTimestamps = new();
```

---

## 🔵 P3 — SYNC-OVER-ASYNC REFACTOR (BÜYÜK)

### P3-A: Swiftly API Araştırması (Tamamlandı)

- **`ICommandService.CommandListener`**: `void (ICommandContext context)` — sync void
- **Async void handler pattern'i çalışıyor** — Swiftly'nin orijinal admin plugin'i `public async void Handler(ICommandContext context)` kullanıyor
- **Context üzerinde `ReplyAsync` mevcut** — `await context.ReplyAsync(localizer[...])`

### P3-B: Refactor Stratejisi

1. Her komut `Execute` metodu: `void` → `async void`
2. Tüm `.GetAwaiter().GetResult()` → `await`
3. Her handler'ı top-level try-catch ile sarmala (unhandled exception koruması)
4. `ConfigureAwait(false)` KULLANMA — main thread davranışı korunmalı

### P3-C: Etkilenen Dosyalar

| Öncelik | Dosya | Not |
|--------|------|-----|
| 🔴 Yüksek | `src/Menu/Handlers/PlayerManagementHandler.cs:95` | Menu içinde deadlock riski |
| 🔴 Yüksek | `src/Menu/Handlers/AdminManagementHandler.cs:90, 119, 231` | Aynı risk |
| 🟡 Orta | `src/Commands/BanCommand.cs:287, 309, 339` | Zaman kritik |
| 🟡 Orta | `src/Commands/MuteCommand.cs` | Aynı |
| 🟡 Orta | `src/Commands/GagCommand.cs` | Aynı |
| 🟡 Orta | `src/Commands/SilenceCommand.cs` | Aynı |
| 🟡 Orta | `src/Commands/IpBanCommand.cs` | Aynı |
| 🟡 Orta | `src/Commands/WarnCommand.cs` | Aynı |
| 🟡 Orta | `src/Commands/KickCommand.cs` | Aynı |
| 🟡 Orta | `src/Commands/UnbanCommand.cs` | Aynı |
| 🟢 Düşük | `src/Commands/GravityCommand.cs:58, 91, 137` | Toggle pattern |
| 🟢 Düşük | `src/Commands/SpeedCommand.cs:58, 91, 134` | Toggle pattern |
| 🟢 Düşük | `src/Commands/ResizeCommand.cs:59, 111` | Toggle pattern |
| 🟢 Düşük | `src/Commands/BlindCommand.cs:120, 146` | Blind uygula |
| 🟢 Düşük | `src/Commands/RenameCommand.cs:57, 66` | Rename yap |
| 🟢 Düşük | `src/Commands/UnrenameCommand.cs:57, 64, 72` | Unrename yap |
| 🟢 Düşük | `src/Commands/GiveCommand.cs:55` | Item ver |
| 🟢 Düşük | `src/Commands/RespawnCommand.cs:54` | Respawn |
| 🟢 Düşük | `src/Commands/SlayCommand.cs:54` | Slay |
| 🟢 Düşük | `src/Commands/TeamCommand.cs:54` | Team değiştir |
| 🟢 Düşük | `src/Commands/SlapCommand.cs` | Slap (zaten async banlattırıldı) |
| 🟢 Düşük | `src/Commands/GodCommand.cs` | God mode toggle |
| 🟢 Düşük | `src/Commands/NoClipCommand.cs` | Noclip toggle |
| 🟢 Düşük | `src/Commands/FreezeCommand.cs` | Freeze toggle |
| 🟢 Düşük | `src/Commands/UnfreezeCommand.cs` | Unfreeze toggle |
| 🟢 Düşük | `src/Commands/HpCommand.cs` | HP set |
| 🟢 Düşük | `src/Commands/MoneyCommand.cs:79` | Money set |
| 🟢 Düşük | `src/Commands/MapCommand.cs` | Map değiştir |
| 🟢 Düşük | `src/Commands/WsMapCommand.cs:141, 147` | Workshop map (HTTP sync) |
| 🟢 Düşük | `src/Commands/RestartCommand.cs` | Restart |
| 🟢 Düşük | `src/Commands/VoteCommand.cs` | Vote |
| 🟢 Düşük | `src/Commands/MixTeamCommand.cs` | Mix team |
| 🟢 Düşük | `src/Commands/RconCommand.cs` | Rcon |
| 🟢 Düşük | `src/Commands/CvarCommand.cs` | Cvar |
| 🟢 Düşük | `src/Commands/ListPlayersCommand.cs` | List players |
| 🟢 Düşük | `src/Commands/AddBanCommand.cs` | Add ban |
| 🟢 Düşük | `src/Commands/LastBanCommand.cs` | Last ban |

### P3-D: Örnek Refactor (BanCommand.cs:69)

**Önce:**
```csharp
public override void Execute(ICommandContext context) => HandleOnlineBan(context, false);

private void HandleOnlineBan(ICommandContext context, bool ipMode)
{
    // ...
    _ = Task.Run(async () =>
    {
        var existingSteam = await _banManager.GetActiveBanAsync(...);
        // ...
    });
}
```

**Sonra:**
```csharp
public override async void Execute(ICommandContext context)
{
    try
    {
        await HandleOnlineBanAsync(context, false);
    }
    catch (Exception ex)
    {
        Core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] Ban command failed");
    }
}

private async Task HandleOnlineBanAsync(ICommandContext context, bool ipMode)
{
    // ... (Task.Run wrapper kaldırıldı, doğrudan await)
    var existingSteam = await _banManager.GetActiveBanAsync(...);
    // ...
}
```

---

## 🟢 P4 — BOŞ CATCH DÜZELTMELERİ

### P4-1: 11 Boş Catch Bloğu

| Dosya | Satır | İçerik |
|------|------|--------|
| `src/Utils/JsonFileLocalizer.cs` | 116 | Language load hatası |
| `src/Commands/MoneyCommand.cs` | 79 | Money set hatası |
| `src/Commands/BlindCommand.cs` | 120, 146 | Blind apply hatası |
| `src/CS2_Admin.cs` | 283 | Culture set hatası |
| `src/CS2_Admin.cs` | 803 | Komut kayıt (P0-5) |
| `src/Commands/RenameCommand.cs` | 66 | Rename zaten var kontrolü |
| `src/Commands/UnrenameCommand.cs` | 64, 72 | Unrename hataları |
| `src/Commands/GodCommand.cs` | (varsa) | God mode toggle |
| `src/Commands/NoClipCommand.cs` | (varsa) | NoClip toggle |
| `src/Commands/SpeedCommand.cs` | (varsa) | Speed set/restore |
| `src/Commands/GravityCommand.cs` | (varsa) | Gravity set/restore |

**Düzeltme pattern'i:**
```csharp
catch (Exception ex)
{
    Core.Logger.LogErrorIfEnabled(ex, "[CS2_Admin] <operation> failed: {PlayerId}", playerId);
}
```

---

## 📊 Uygulama Sırası

| Adım | Açıklama | Tahmini Süre |
|------|----------|--------------|
| 1 | P0-1: LoggerExtensions düzelt | 2 dk |
| 2 | P0-5: CS2_Admin.cs:803 catch düzelt | 1 dk |
| 3 | P2: 3 Dictionary → ConcurrentDictionary | 15 dk |
| 4 | P4: 11 boş catch düzelt | 10 dk |
| 5 | **BUILD & DOĞRULA** | 5 dk |
| 6 | P0-2, P0-3, P0-4: tr.jsonc düzelt | 3 dk |
| 7 | P1-1, P1-2, P1-3, P1-4, P1-5: tr.jsonc diğer | 10 dk |
| 8 | P1-6: EN/HU/TR key karşılaştırma | 10 dk |
| 9 | **BUILD & DOĞRULA** | 5 dk |
| 10 | P3-C: Menu handler'lar (kritik) | 30 dk |
| 11 | **BUILD & DOĞRULA** | 5 dk |
| 12 | P3-C: Diğer komutlar (5'li gruplar) | 60 dk |
| 13 | **BUILD & DOĞRULA** | 5 dk |
| **Toplam** | | **~2.5-3 saat** |

---

## ⚠️ Risk Analizi

1. **Async void exception handling** — handler exception fırlatırsa Swiftly bunu loglamayabilir. Çözüm: her handler'ı top-level try-catch ile sarmala ✅
2. **Database context lifecycle** — async/await'e geçerken using/await pattern'ine dikkat
3. **Thread context** — `ConfigureAwait(false)` KULLANILMAYACAK, default behavior korunacak
4. **ConcurrentDictionary API farkları** — `TryGetValue` aynı, ama `Add`/`Remove` pattern değişebilir
5. **Build modunda sync test zor** — her komutun çalışması server gerektirir, smoke test ile doğrulanmalı

---

## ✅ Doğrulama Kontrol Listesi

Her faz sonrası:
- [ ] Build başarılı (0 Error)
- [ ] Warning sayısı önceki ile aynı veya az
- [ ] Dil dosyaları valid JSON
- [ ] tr.jsonc'de U+200B kalmamış
- [ ] tr.jsonc'de "SENTAM" kalmamış
- [ ] tr.jsonc'de "Vertı" kalmamış
- [ ] LoggerExtensions test: hata her zaman loglanmalı
- [ ] CS2_Admin.cs:803 catch test: log basmalı
- [ ] Dictionary → ConcurrentDictionary: compile geçmeli
- [ ] Boş catch → loglu catch: compile geçmeli

---

## 📝 Notlar

- **Working directory:** `C:\Users\CanDaysa\Desktop\eklentiler\Counter-Strike 2\CS2_Admin2`
- **GitHub:** `candaysa/CS2_Admin`
- **Son release:** `v1.0.12`
- **Plugin metadata version:** `1.0.13`
- **Hedef release:** `v1.0.14`
- **Kullanıcı push'u manuel yapıyor** — `create-pr-request.bat` ile
- **CHANGELOG.md ve PR_BODY.md silindi** (kullanıcı kararı) — yeni release'te yeniden oluşturulacak
