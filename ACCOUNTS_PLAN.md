# Hesab sistemi (Faza 4.5): plan və vəziyyət

> `FAZA_4_PLAN.md` 4.5-in genişləndirilmiş planı. Qərar (istifadəçi, 2026-11-05): **parol ilə giriş də, Google ilə giriş də olsun; backend və e-poçt serverdə olacaq; Google açarlarını layihə hazır olanda istifadəçi özü qeydiyyatdan keçirib yoxlayacaq** (kodu əvvəlcədən yazırıq, test üçün saxta yoxlayıcı ilə).

## Dizayn

- **Yeni layihə `server/AccountServer`**: ASP.NET Core (.NET 10) minimal API, JSON, **PostgreSQL** (`Npgsql`, əl ilə yazılmış SQL, sxem `server/AccountServer/Schema.cs` ilə avtomatik yaradılır; **qərar: istifadəçi, 2026-11-05**). Matç relay-i (`GameServer`) ayrı qalır; sonra eyni hesab açarı ilə birləşəcək.
- **Parollar**: PBKDF2-SHA256 (600 000 dövr, 16 bayt duz), sabit vaxtlı müqayisə. Heç vaxt açıq saxlanmır, loga düşmür.
- **Sessiya açarı (token)**: 32 təsadüfi bayt; bazada yalnız SHA-256 hash-i. Müddət 90 gün, istənilən vaxt ləğv olunur (logout, parol dəyişmə, parol bərpası bütün sessiyaları bağlayır).
- **Giriş qorunması**: 5 səhv parol → hesab 15 dəqiqə bağlı; IP üzrə sorğu limiti (dəqiqədə 30). Parol bərpa kodu 5 yanlış cəhddən sonra ləğv olunur.
- **Google**: telefon Google ID token göndərir (`POST /v1/auth/google`), server `Google.Apis.Auth` ilə imzanı və `aud` (client ID) yoxlayır. Hesab `sub` ilə tapılır. Eyni e-poçtlu təsdiqlənməmiş parol hesabı varsa, onun parolu silinir (hesabı əvvəlcədən ələ keçirmənin qarşısı).
- **E-poçt**: `IEmailSender`; SMTP (MailKit) və inkişaf üçün fayla yazan göndərici. Təsdiq: link (`GET /verify-email?token=`). Parol bərpası: 8 simvollu kod oyunda yazılır (telefonda dərin link lazım olmasın).
- **Profil**: serverdə hesaba bağlı kiçik JSON (kampaniya, Armory, gündəlik, öyrədici sətirləri) + `revision`. Yazanda `baseRevision` uyğun gəlməsə 409 və serverdəki versiya qaytarılır (iki telefon bir-birini ezməsin). Qonaq kimi oynayan sonra qeydiyyat edəndə yerli irəliləyiş yüklənir.
- **Hələlik güvənilən məlumat**: kreditlər və Armory profil sətri olaraq saxlanır, yəni oyunçu öz telefonunda dəyişə bilər. Server tərəfində həqiqi yoxlama (missiyanın replay ilə təsdiqi, xərcin serverdə edilməsi) Faza 5/6-dadır, pullu alışlardan əvvəl mütləq.
- **Baza**: bağlantı sətri `Account:ConnectionString` (mühit dəyişəni `Account__ConnectionString`). Testlər real PostgreSQL tələb edir (hər test özünün təmiz sxemində işləyir); test bazası `ACCOUNTS_TEST_DB` mühit dəyişəni ilə verilir.
- **Hosting vaxtı**: HTTPS məcburidir (reverse proxy: Caddy/nginx), `Account:PublicBaseUrl` və SMTP/Google açarları mühit dəyişənləri ilə verilir. Fayllarda açar saxlanmır.

## İşlər

- [x] A1 Server: layihə, baza, parol/token, qeydiyyat, giriş, çıxış, `/v1/me`.
- [x] A2 E-poçt təsdiqi, parol unutma/bərpa, parol dəyişmə.
- [x] A3 Google ID token girişi (saxta yoxlayıcı ilə testlər).
- [x] A4 Profil saxlama (revision, 409).
- [x] A5 Testlər (`AccountServer.Tests`).
- [x] A6 Unity: `AccountClient`, giriş/qeydiyyat ekranı (qonaq da qalır), profil sinxronu.
- [x] A7 Unity: Google düyməsi kodu yazılıb (`Plugins/Android/GoogleSignInBridge.java`, `mainTemplate.gradle`, `GoogleSignIn.cs`). APK qurulur və açılır; **real Google girişi açarlar olmadan yoxlanmayıb** (istifadəçi sonda edəcək).
- [x] A8 Quraşdırma sənədi (`server/ACCOUNTS.md`): mühit dəyişənləri, Google Cloud addımları, SMTP, HTTPS.

Server tərəfi hazırdır (2026-11-05): `server/AccountServer`, 34 test (`dotnet test server/AccountServer.Tests`, test bazası `server/tools/test-db.sh start`), sənəd `server/ACCOUNTS.md`. Hesabı silmə (`POST /v1/me/delete`) də var, çünki Google Play tətbiqdaxili hesab silməni tələb edir.

Unity tərəfi: server ünvanı `Assets/Resources/accountserver.txt`, Google Web client ID `Assets/Resources/googleclient.txt` (ikisi də git-də yoxdur). `googleclient.txt` boşdursa Google düyməsi “not set up” mesajı verir.
