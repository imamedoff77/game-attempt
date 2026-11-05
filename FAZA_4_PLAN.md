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
- [ ] 4.4 Gündəlik/həftəlik tapşırıqlar və gündəlik mükafat (yerli saat ilə aldatmaq asandır; real versiya server vaxtı ilə, Faza 5 serveri).
- [ ] 4.5 Google ilə giriş və server hesabı: **istifadəçidən lazım olanlar**: Google Cloud layihəsi/OAuth client, server hosting qərarı. Ondan sonra irəliləyiş/kreditlər serverə köçürülür (hazırda `Serialize` mətni hazırdır).
- [ ] 4.6 Profil ekranı, bacarıqlar (hələ dizayn qərarı yoxdur).
