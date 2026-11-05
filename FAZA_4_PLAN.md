# Faza 4: Hesab, anbar və menyular (plan və vəziyyət)

> Ümumi plan: `GAME_PLAN.md`. Bu fazanın bir hissəsi (Google ilə giriş, server hesabı) istifadəçinin hesablarını tələb edir, ona görə **yerli birinci**: hesab olmadan işləyən hissələr əvvəl, server hissəsi sonra.

## Qərarlar

- **Armory (anbar) = daimi vahid gücləndirmələri.** Hər vahid növü 0–5 səviyyə; hər səviyyə vahidin zərər və canına +4 % verir. Qiymət: piyada 30, kəskin atıcı 45, maşın 60 × (səviyyə+1) kredit. Kreditlər kampaniyada ulduz qazanmaqdan gəlir. Hamısını almaq mümkün deyil (~3600 kredit, kampaniya ~1500 verir), seçim vacibdir.
- **Sim-core:** `World.SetKitBonus(team, kind, milli)` (ədədi sabit, hash-ə yalnız istifadə olunanda daxildir); zərər bonusu kəsr olduğu üçün hər atışdan atışa daşınır (`WithKit`, tüfəng 6 zərər vurur, +4 % yoxa çıxmasın). `Armory` sinfi səviyyə/qiymət/yadda saxlama (`PlayerPrefs`), `CampaignProgress.Spend/Add`.
- **Onlayn qayda:** onlayn matçda hər iki oyunçu bonusları eyni dəyərlərlə qurmalıdır; server qiymətləri yoxlamalıdır. Bu Faza 5-də (matç başlanğıc məlumatına əlavə olunur). İndi yalnız kampaniya və kompüterə qarşı.

## İşlər

- [x] 4.1 Armory sim-core + testlər (`ArmoryTests`: qiymətlər, alma, yadda saxlama, bonus yalnız həmin komandaya, kəsr zərər, hash, determinizm).
- [x] 4.2 Unity: ana menyuda "Armory", 6 kartlı ekran (səviyyə, bonus, Upgrade qiyməti / MAX), kampaniya missiyalarına və kompüterə qarşı matça tətbiq olunur. Dev build-də debug paneldə "+500 credits".
- [x] 4.3 Armory ekranı telefonda yoxlandı (2026-11-05): açılır, kreditlə alış işləyir, səviyyə/qiymət yenilənir, yenidən başladanda qalır. Debug panel indi hər ekranın üstündədir.
- [x] 4.4 (yerli hissə, 2026-11-05) Gündəlik giriş mükafatı (7 günlük seriya: 20/30/40/50/60/80/120) və 3 gündəlik tapşırıq (matç bitir, kampaniya missiyası qazan, kompüterə qarşı qazan; 20/30/40 kredit). `Daily.cs` + `DailyTests` (6), Unity "Daily rewards" ekranı (menyuda (!) işarəsi), telefonda yoxlanıb. Yerli saata güvənir (aldatmaq asandır); real versiya server vaxtı ilə, Faza 5. Həftəlik tapşırıqlar hələ yoxdur. İlk dəqiqədə təslim olmaq "matç bitir"ə sayılmır.
- [x] 4.5 Hesab sistemi (kod hazırdır, commit `3fe037a`): `ACCOUNTS_PLAN.md`. **İstifadəçidən qalanlar:** Google Cloud OAuth açarları və server hosting (sonda).
- [~] 4.6 Profil ekranı hazırdır (2026-11-05, kodda; telefonda yoxlanıb): `PlayerStats` (oynanan/qazanılan matç, missiya, vahid, düşmən; hesaba sinxronlaşır, `stats` açarı), `DemoHud.Profile.cs`, menyuda Account düyməsinin altında. **Bacarıqlar** üçün hələ dizayn qərarı yoxdur.
- [x] 4.7 (Faza 7-dən irəli çəkildi, 2026-11-05) Öyrədici: 1-ci missiyada 4 addımlıq göstəriş lenti (vahid seç → yerə toxun/hücum et → Barracks-da əsgər hazırla → düşmən qərargahını məhv et), "Skip tutorial" düyməsi, missiya qazanılanda `tutorial.done` yazılır (təkrar göstərilmir). Telefonda yoxlanıb. İlk dəfə girənlər üçün tam öyrədici (quraşdırma, enerji, HQ yüksəltmə) hələ yoxdur.
