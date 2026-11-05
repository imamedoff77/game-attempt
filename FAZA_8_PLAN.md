# Faza 8: Test və buraxılış (plan və yoxlama siyahısı)

> Ümumi plan: `GAME_PLAN.md`. Mağaza: `STORE_SETUP.md`. Məxfilik: `PRIVACY_POLICY.md`. Hesab serveri: `server/ACCOUNTS.md`. İstifadəçinin etməli olduğu addımlar **[İSTİFADƏÇİ]** ilə işarələnib; qalanını Claude edə bilər.

## Hazır olanlar (2026-11-05)

- [x] Release build: `GameBuild.BuildAndroidBundle` → `unity-client/Builds/Android/IronDoctrine.aab` (imzalı, release açarı `~/.config/game-keys/`, 32 MB). Play yeni tətbiqlər üçün yalnız .aab qəbul edir. APK üçün `GameBuild.BuildAndroid`.
- [x] Release qurulmasında debug panel, dev alışı, açıq `http` yoxdur (`insecureHttpOption = DevelopmentOnly`, `Debug.isDebugBuild` yoxlamaları).
- [x] Avtomatik testlər: sim-core 320, GameServer 42, AccountServer 46.

## Buraxılışdan əvvəl (Claude)

- [ ] Server ünvanları: `AccountSettings.ReleaseUrl` və oyun serveri ünvanı (hazırda yer tutucu `https://accounts.invalid`); hostinq seçiləndən sonra doldurulur, HTTPS (Caddy/nginx) ilə.
- [ ] Hostinq sənədi: hesab serveri + oyun serveri + PostgreSQL üçün systemd/Docker qaydası, yedəkləmə (`pg_dump` gündəlik), monitorinq. Server ayda ~5–10 USD VPS-də başlaya bilər.
- [ ] Təhqiramiz displayName üçün söyüş filtri və "report" (FAZA_5 5.5c-2).
- [ ] Zəif telefonda (A34-dən zəif) performans ölçməsi, FPS/RAM; lazım gələrsə grafika səviyyəsi.
- [ ] Ekran görüntüləri və mağaza mətni (Claude yazır, görüntüləri telefondan çəkir): qısa təsvir (80 simvol), tam təsvir, 512×512 ikon, 1024×500 qrafik. İkon və qrafik üçün real art lazımdır (Faza 7).
- [ ] Real art/animasiya/səs (Faza 7): hazırda prosedur modellər və səslər. İstifadəçi "normal qrafikalar olanda" sınaqdan keçirəcək.

## [İSTİFADƏÇİ] addımları

1. **Google Play Console hesabı** (25 USD bir dəfəlik). Yeni *şəxsi* hesablar üçün Google qaydası: tətbiq ictimaiyyətə açılmazdan əvvəl **qapalı testdə ən azı 12 test istifadəçisi 14 gün ardıcıl** olmalıdır (qaydalar dəyişə bilər, o vaxt yoxlanmalıdır). Təşkilat hesabı bu şərtdən azad ola bilər. Test istifadəçilərinin siyahısını əvvəlcədən toplamaq lazımdır.
2. Play Console-da tətbiq yaratmaq (paket adı `com.imamedoff.irondoctrine`, ad sonra dəyişsə `game.json` + `tools/sync_identity.py`), **Play App Signing**-i qəbul etmək (yüklənən .aab-ı bizim açarla imzalayırıq, Google istifadəçilərə öz açarı ilə paylayır; bizim açar yükləmə açarı olur).
3. `STORE_SETUP.md`: məhsulları yaratmaq, service account, mühit dəyişənləri, lisenziya testçisi ilə real alış.
4. `server/ACCOUNTS.md`: Google Cloud-da OAuth client ID-lər (Web + Android, SHA-1 hər iki açar üçün: yükləmə açarı və Play App Signing açarı), SMTP, hostinq/domen. Real Google girişini telefonda sınamaq.
5. Məxfilik siyasətini (`PRIVACY_POLICY.md`, `[...]` yerlərini doldurub) ictimai ünvanda yerləşdirmək (məsələn GitHub Pages), Play Console-a link vermək.
6. **Data safety formu** (aşağıdakı cavablar). **Yaş reytinqi anketi (IARC)**: döyüş (cizgi tipli, qansız, real olmayan ölkələr), alış var, reklam yoxdur, istifadəçi məzmunu yoxdur (çat yalnız hazır ifadələr), yer məlumatı yoxdur. Gözlənilən nəticə təxminən 12+ / PEGI 12.
7. Hesabın silinməsi: Play Console-da hesabı silmə linki tələb olunur: tətbiqdə var (Account → Delete account); veb-səhifədə də bir izah səhifəsi lazımdır (məxfilik səhifəsi ilə eyni yerdə).
8. Daxili test → qapalı test (12 nəfər, 14 gün) → istehsal; yumşaq buraxılış üçün Play Console-da yalnız bir kiçik ölkəni seçmək.

## Data safety formu: cavablar (koda görə)

| Sual | Cavab |
|---|---|
| Məlumat toplayırsınız? | Bəli |
| Şəxsi məlumat | E-poçt, ad (hesab üçün), istifadəçi ID (Google hesabı ID-si, ixtiyari) |
| Maliyyə məlumatı | Alış tarixçəsi (məhsul, Google sifariş nömrəsi); kart məlumatı **yoxdur**, Google işləyir |
| Tətbiq fəaliyyəti | Oyun irəliləyişi, reytinq, matç komandaları |
| Cihaz/digər ID, yer, əlaqə, foto, mikrofon | Toplanmır |
| Üçüncü tərəflərlə paylaşım | Yox (Google Play ödənişi və giriş Google-un öz xidmətidir, "paylaşım" sayılmır) |
| Şifrələnmə | Hərəkətdə şifrələnir (HTTPS) |
| Silmə tələbi | Bəli, tətbiqdən və e-poçtla |
| Reklam | Yoxdur |

## Test planı (qapalı testdən əvvəl)

- [ ] Təmiz telefonda: ilk açılış, öyrədici, 10 missiya, Armory, gündəlik, hesab yaratma, Google girişi, profil sinxronu iki telefonda.
- [ ] Onlayn: iki real telefon, mobil internet (VPS-ə qoşulma), reytinqli matç, təslim, qopma/qayıdış, dostla otaq.
- [ ] Alış: lisenziya testçisi ilə 4 məhsul; internet kəsiləndə alış, tətbiqi bağlayıb açma, ikinci telefonda bərpa.
- [ ] Ekran növləri: kiçik/böyük en/boy nisbəti (notch, 16:9, 20:9), planşet.
- [ ] Arxa plana keçmə, zəng, ekran kilidi, şəbəkənin dəyişməsi (Wi-Fi ↔ mobil).
