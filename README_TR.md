[🇬🇧 Read in English](README.md)

# CS2_ADMIN

Eklenti Surumu: `1.0.9`

## Ozellikler

- Admin management
- Admin toptime
- Report system
- Calladmin system
- Admin playtime system
- Auto tag system

## Config Versionlama

Ana config dosyalari `Version: 1` ile surumlenir:

- `config.json`
- `commands.json`
- `permissions.json`
- `maps.json`

Bir dosyada surum eksik/yanlis ise (veya JSON bozuksa), eklenti dosyayi silip bir sonraki yuklemede yeniden olusturur.

## 🎯 Hedef Belirme (ÖNEMLİ: #id Kullanımı)
Komutları oyuncular üzerinde uygularken **isim yerine oyuncunun ID'sini (#id) kullanmanız** isim karışıklıklarını ve yanlış işlem yapılmasını önlemek adına çok daha güvenlidir.
Oyuncuların ID numaralarını `!listplayers` yazarak veya konsola `status` yazarak görebilirsiniz.

**Örnek doğru kullanım:**
Eğer Ali'nin ID'si 12 ise, Ali'ye işlem yaparken `!kick Ali` yerine doğrudan `!kick #12` komutunu kullanmalısınız. Tüm komut rehberinde hedef belirtilen yerler `<#id>` olarak gösterilmiştir.

**Diğer Toplu Hedef Seçenekleri:**
- `@all`: Sunucudaki herkes.
- `@t`: Tüm Terörist (T) oyuncular.
- `@ct`: Tüm Anti-Terörist (CT) oyuncular.
- `@alive`: Yaşayan tüm oyuncular.
- `@dead`: Ölü olan tüm oyuncular.
*(Örn: `!slay @t` tüm teröristleri öldürür.)*

---

## 🗣️ Genel ve İletişim Komutları
Sunucu içi genel işlemler ve duyurular için kullanılır.

* `!admin` - Admin menüsünü açar.
* `!warn <#id> <sebep>` - Oyuncuyu belirttiğiniz sebeple uyarır.
* `!unwarn <#id>` - Oyuncunun üzerindeki uyarıyı kaldırır.
* `!who <#id>` - Hedef oyuncu hakkında detaylı bilgi gösterir.
* `!asay <mesaj>` - Sadece adminlerin görebileceği şekilde mesaj gönderir (Admin sohbeti).
* `!say <mesaj>` - Sistem/Sunucu mesajı olarak herkese gönderir.
* `!csay <mesaj>` - Ekranın ortasına büyük harflerle mesaj yazar (Duyuru).
* `!hsay <mesaj>` - Ekranın üst tarafında bilgi mesajı (hint) olarak yazar.
* `!admintime` - Sunucudaki toplam aktif adminlik sürenizi gösterir.
* `!listplayers` - Oyuncu listesi ve oyuncu ID'lerini gösterir.
* `!vote "<soru>" "<cevap1>" "<cevap2>"` - Oylama başlatır. (Örn: `!vote "Harita değişsin mi?" "Evet" "Hayır"`)

---

## 🗺️ Harita ve Oyun Yönetimi
Harita değiştirme veya raunt yenileme gibi temel işlemler.

* `!map <harita_adi>` - Haritayı değiştirir (Örn: `!map de_dust2`).
* `!wsmap <workshop_id|isim>` - Atölye (Workshop) haritasını açar.
* `!rr [saniye]` - Oyunu belirtilen saniye sonra yeniden başlatır.

---

## ⚖️ Ceza Komutları (Kick, Ban, Mute)
Kurallara uymayan oyuncuları cezalandırmak için kullanılır.

* `!kick <#id> [sebep]` - Oyuncuyu sunucudan atar. (Örn: `!kick #12 Küfür`)
* `!ban <#id> <dakika> [sebep]` - Oyuncuya SteamID üzerinden ban atar. (Sınırsız için dakika yerine `-1` yazın).
* `!ipban <#id> <dakika> [sebep]` - Oyuncuya IP üzerinden ban atar.
* `!addban <steamid> <dakika> [sebep]` - Sunucuda aktif olmayan (çevrimdışı) bir oyuncuya SteamID üzerinden ban atar. (Bu komutta ID yerine SteamID kullanılır).
* `!unban <steamid|ip> [sebep]` - Ban cezasını kaldırır.
* `!mute <#id> <dakika> [sebep]` - Oyuncunun sesli sohbetini (mikrofonunu) engeller. (Sınırsız için `-1`).
* `!unmute <#id>` - Sesli sohbet engelini kaldırır.
* `!gag <#id> <dakika> [sebep]` - Oyuncunun yazılı sohbetini (yazışmasını) engeller.
* `!ungag <#id>` - Yazılı sohbet engelini kaldırır.
* `!silence <#id> <dakika> [sebep]` - Oyuncunun hem sesli hem yazılı sohbetini engeller (Mute + Gag).
* `!unsilence <#id>` - Silence (sessizlik) cezasını kaldırır.

---

## 🎭 Eğlence ve Etkileşim Komutları (Cheats)
Oyuncular üzerinde eğlenceli veya oyun içi etkileşimler yaratan komutlardır.

* `!slap <#id> [hasar]` - Oyuncuyu tokatlar (belirtirseniz canını yakar).
* `!slay <#id>` - Oyuncuyu anında öldürür.
* `!respawn <#id>` - Oyuncuyu yeniden canlandırır.
* `!team <#id> <t|ct|spec>` - Oyuncunun takımını değiştirir. (Örn: `!team #12 spec`)
* `!noclip <#id>` - Oyuncunun duvarlardan geçmesini sağlar (Açıp/Kapatmalı).
* `!goto <#id>` - Sizi hedef oyuncunun yanına ışınlar.
* `!bring <#id>` - Hedef oyuncuyu sizin yanınıza ışınlar.
* `!freeze <#id> [saniye]` - Oyuncuyu dondurur, hareket etmesini engeller.
* `!unfreeze <#id>` - Oyuncunun dondurulmasını çözer.
* `!resize <#id> <boyut>` - Oyuncunun boyutunu büyütür veya küçültür.
* `!drug <#id> [saniye]` - Oyuncuya uyuşturucu efekti (ekran dalgalanması) verir.
* `!burn <#id> [saniye] [saniyedeki_hasar]` - Oyuncuyu yakar.
* `!disarm <#id>` - Oyuncunun elindeki silahları düşürür/siler.
* `!speed <#id> <çarpan>` - Oyuncunun hızını değiştirir (Örn: `!speed #12 1.5`).
* `!gravity <#id> <çarpan>` - Oyuncunun yer çekimini değiştirir.
* `!rename <#id> <yeni_isim>` - Oyuncunun ismini değiştirir.
* `!hp <#id> <sağlık>` - Oyuncunun can değerini ayarlar.
* `!money <#id> <miktar>` - Oyuncunun parasını ayarlar.
* `!give <#id> <eşya>` - Oyuncuya silah veya eşya verir (Örn: `!give #12 weapon_ak47`).

---

## ⚙️ Sunucu Ayarları
Sunucu geneli modları açıp kapatmaya yarar.

* `!hson` / `!hsoff` - Yalnızca kafadan vuruş (Only Headshot) modunu açar/kapatır.
* `!bunnyon` / `!bunnyoff` - Tavşan zıplamasını (Bhop) açar/kapatır.
* `!respawnon` / `!respawnoff` - Oyuncuların öldükten sonra sürekli yeniden canlanmasını açar/kapatır.
* `!cvar <cvar> [değer]` - Sunucu değişkenlerini (cvar) değiştirir.

> **Not:** Oyundaki tüm `!` komutlarının, sunucu konsolunda `sw_` önekiyle kullanılan bir karşılığı vardır (Örneğin sohbette `!ban` yazmakla, konsola `sw_ban` yazmak aynıdır). Yönetimsel komutların (`sw_addadmin`, `sw_addgroup` vb.) sunucu konsolundan veya `admin.root` yetkisine sahip oyuncular tarafından kullanılması zorunludur.

---

## 🔐 Permissions (Yetkiler)

Varsayilan (Default) ana yetkiler:

- `admin.root` -> tam erisim, admin/grup yonetimi (`sw_addadmin` vb.), root bypass (`admin.*`, `*`).
- `admin.generic` -> `admin` menusu, `warn`, `unwarn`, `who`, `asay/say/psay/csay/hsay`, `admintime`, `map`, `wsmap`, `rr/restart`, `calladmin`, `listplayers`, `vote`.
- `admin.ban` -> `ban`, `ipban`, `addban`, `unban`, `lastban`.
- `admin.kick` -> `kick`.
- `admin.mute` -> `mute/unmute`, `gag/ungag`, `silence/unsilence`.
- `admin.cheats` -> `slap`, `slay`, `respawn`, `team`, `noclip`, `goto`, `bring`, `freeze`, `unfreeze`, `resize`, `drug`, `burn`, `disarm`, `speed`, `gravity`, `rename`, `hp`, `money`, `give`.
- `admin.rcon` -> sunucu modu degistirme komutlari (`hson/hsoff`, `bhopon/bhopoff`, `respawnon/respawnoff`).
- `admin.cvar` -> `cvar`.

*`Report` izni varsayilan olarak bos (`""`) oldugu icin `!report` herkese aciktir (degistirilmezse).*

## ⚙️ Onerilen Kurulum Sirasi (Sunucu Konsolundan)

**1. Önce grupları oluşturun:**
```text
sw_addgroup Owner admin.root 100
sw_addgroup Moderator admin.generic,admin.kick,admin.mute 50
```

**2. Grubu oluşturduktan sonra admin ekleyin:**
```text
sw_addadmin 76561198255550637 SSonic @Owner
sw_addadmin 7656119XXXXXXXXXX PlayerX Moderator,Helper 30
```
*(En sondaki rakam süreyi belirtir (gün).*

**3. Doğrulama:**
```text
sw_listgroups
sw_listadmins
sw_adminreload
```
*Oyunda olan oyuncular yetkilerini ve taglarını anında alırlar.*

## 📁 Dosya Notlari

- `config.json` -> genel ayarlar ve dil ayarlari.
- `commands.json` -> degistirilebilir chat takma adlari (alias).
- `permissions.json` -> komutlari yetkilere baglama.
- `maps.json` -> normal ve atolye (workshop) harita listeleri.
- `resources/translations/*.jsonc` -> dil dosyalari (`en`, `tr`, `de`, `fr`, `it`, `el`, `ru`, `bg`, `hu`).
