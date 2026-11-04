# Əsas oyunun planı

> Demo (D1–D5) bitdi və qərar verildi: əsas oyuna keçirik. Demo planı: `DEMO_PLAN.md`. Texniki vəziyyət: `PROJECT_NEEDS.md`.
> Müddətlər kobud təxminlərdir. Hər faza telefonda sınaqla bitir.

## Qaydalar (hər fazada keçərlidir)

- **Orijinallıq:** Art of War 3-ün adını, loqosunu, asset-lərini, mətnlərini, xəritələrini, interfeys düzənini kopyalamırıq. Mexanika və janr sərbəstdir. Hər şeyi özümüz yaradırıq. AI vasitələrinə onların şəkillərini mənbə kimi vermirik. Mağaza mətnində onlara istinad etmirik.
- **Commit:** yalnız istifadəçi istəyəndə.
- **Dil:** istifadəçi ilə Azərbaycan dilində.
- **Texniki əsas:** deterministik `sim-core` (oyunun beyni) + Unity (gözlər). Komandalar yeganə giriş yoludur.

## Fazalar

### Faza 0: Təməl (1–2 həftə)
Ətraflı plan: `FAZA_0_PLAN.md`.
- Oyunun adı. Mövzu (müasir müharibə, xəyali ölkələr) və art üslubu (realist low-poly) artıq seçilib. Orijinallıq qaydaları.
- Rəqəmlər (can, zərər, qiymət, vaxt) koddan data fayllarına çıxır.
- Debug düymələrini ayırmaq, paket adı, imza açarı.

### Faza 1: 1v1 şəbəkə prototipi (2–3 həftə)
- İki telefon internet üzərindən eyni matçı oynayır (lockstep).
- Sadə server, uyğunsuzluq aşkarlama, bağlantı qopması.
- **Qərar nöqtəsi:** işləyirsə davam, işləmirsə arxitekturanı burada düzəldirik.

### Faza 2: Tam matç mexanikası, kompüterə qarşı (6–8 həftə)
- 20–60 dəqiqəlik matç. Vahid limiti (20/40 unit), baza səviyyələri, tikinti vaxtı, azalan gəlir, enerji qaydalarının son halı.
- Daha çox vahid növü (piyada 3, maşın 3), yüksəltmələr, kompüter rəqibi (AI).
- Vahidlərin bir-birini itələməməsi, yol tapma və formasiyanın təkmilləşməsi.

### Faza 3: Kampaniya (4–6 həftə)
- Mərhələ xəritəsi, missiya sistemi, qələbə şərtləri, ödüllər, 8–10 ilk mərhələ.

### Faza 4: Hesab, anbar və menyular (6–8 həftə)
- Google ilə giriş, profil, silah almaq və gücləndirmək, bacarıqlar, gündəlik/aylıq ödüllər, tapşırıqlar.
- Real interfeys (IMGUI müvəqqətidir). 1.0-da yalnız İngilis dili, amma bütün mətnlər tərcümə cədvəlindən oxunur (sonra Azərbaycan, Rus, Türk əlavə etmək asan olsun).

### Faza 5: Online 1v1 (4–6 həftə)
- Rəqib tapma, reytinq, çat, tapşırıqlardan sonra 1v1-in açılması, replay, saxtakarlığa qarşı yoxlama.

### Faza 6: Pullu hissə (3–4 həftə)
- Google Play Billing, serverdə alışın təsdiqi, booster-lər, mağaza, məxfilik siyasəti, yaş reytinqi.

### Faza 7: Qrafika, səs və cila (4–8 həftə, paralel)
- Vahid animasiyaları, effektlər, musiqi, səslər, ilk dəfə girənlər üçün öyrədici hissə.

### Faza 8: Test və buraxılış (4–6 həftə)
- Zəif telefonlarda sınaq, balans, qapalı test (Google-un yeni hesab qaydaları o vaxt yoxlanır), kiçik ölkədə yumşaq buraxılış, sonra Play Store.

### Sonraya qalanlar
Gəmi və tersane, təyyarə, ikinci fraksiya, iOS.

## Buraxılış 1.0 minimumu

Faza 0–3, sadə Faza 4, Faza 5 və Faza 6. Qalanları yeniləmələrlə. Kobud təxmin: 6–9 ay.

## Əvvəlcədən məlum oyun qaydaları (istifadəçi təsdiqi)

- Matç 1 saata qədər uzana bilər. Pullu alışlar var. 2v2 və 3v3 yoxdur.
- Resurs avtomatik gəlir (toplayıcı yoxdur), təchizat mərkəzləri gəliri artırır.
- Binanın canı var. Bina enerji sərf edir, enerji binaları enerji verir. Enerji balans kimi işləyir (məs. 35/120).
- Enerji çatmayanda bina qalır, amma işləmir (elektrik işarəsi).
- Eyni növ istehsal binaları azalan gəlir verir (məs. 120 → 80 → 60). Enerji və resurs binalarının cədvəli ayrıdır.
- Başlanğıc: Baza dəqiqədə 20 resurs və 20 enerji verir. Tikmə ardıcıllığı: enerji → resurs → piyada → maşın.
- Unit limiti: piyada 1/2/2, maşın 2/3/4 unit. Baza 1-ci səviyyədə 20 unit, 2-ci səviyyədə 40.
- Döyüş: vahid hərəkət edərkən də atəş aça bilər. Hər vahidin öz atəş qövsü var.
- Su və tersane sonraya qalır. Kampaniya mərhələləri, ödüllər, silah almaq və gücləndirmək. 1v1 bəzi tapşırıqlar keçildikdən sonra açılır. Booster-lər, çat, gündəlik/aylıq ödüllər, tapşırıqlar, bacarıqlar.

## Açıq qərarlar (Faza 0-da dəqiqləşir)

| Sual | Vəziyyət |
|---|---|
| Oyunun adı | açıq |
| Mövzu / fraksiya | ✅ Müasir müharibə, xəyali ölkələr (real ölkə adları yoxdur) |
| Art üslubu | ✅ Daha realist low-poly: tünd tonlar, kölgələr. Telefonda performansı hər addımda ölçürük |
| Buraxılış dilləri | ✅ 1.0 yalnız İngilis. Mətnlər kodun içinə yazılmır, cədvəldən oxunur, sonra dil əlavə etmək asan olsun |
| Monetizasiya modeli (1.0) | ✅ Pulsuz oyun, booster və silahlar pullu. Reklam yoxdur |
| Server hosting | Faza 1-dən lazım olur |
| Play Console hesabı (25 $) | Faza 8-də |
