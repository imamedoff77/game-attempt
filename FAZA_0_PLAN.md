# Faza 0: Təməl (ətraflı plan)

> Ümumi plan: `GAME_PLAN.md`. Hər faza öz faylında yazılır, yalnız cari faza ətraflı olur.
> Müddət: 1–2 həftə. Məqsəd: əsas oyunun üzərinə tikiləcəyi təməli düzgün qurmaq. Yeni oyun mexanikası əlavə edilmir.

## Qərarlar (artıq verilib)

- Mövzu: müasir müharibə, xəyali ölkələr.
- Qrafika: daha realist low-poly, tünd tonlar, kölgələr.
- Dil: 1.0 yalnız İngilis. Mətnlər cədvəldən oxunur.
- Pul: pulsuz oyun, booster və silahlar pullu, reklam yoxdur.

## İşlər

Hər iş ayrıca commit olur (istifadəçi "commit at" deyəndə). Hər işdən sonra 85+ test yaşıl qalmalıdır.

### 0.1 Rəqəmləri data fayllarına çıxarmaq (əsas iş, ~3–4 gün)

İndi can, zərər, qiymət, tikinti vaxtı, gəlir cədvəlləri `sim-core/src/SimCore/Defs.cs`-in içindədir. Balans dəyişmək üçün kod yazmaq lazım olmasın.

- `data/units.json`, `data/buildings.json`, `data/economy.json` yaradılır (hər vahid: can, zərər, mənzil, atəş sürəti, görmə, atəş qövsü, qiymət, istehsal vaxtı, istehsal edən bina).
- **Yalnız tam ədədlər.** Onluq kəsr yoxdur (sürət və məsafə mində bir vahidlə, məs. `1500` = 1.5). Səbəb: deterministik lockstep-də iki telefon eyni nəticəni verməlidir.
- `sim-core` içində `GameData` sinfi: mətni oxuyur, yoxlayır (mənfi ədəd, çatışmayan sahə → aydın xəta). Xarici kitabxana olmadan, kiçik öz JSON oxuyucumuz (netstandard2.1, Unity-də də işləsin).
- `Defs` indi `GameData`-dan oxuyur. Cari rəqəmlər JSON-a **eyni qiymətlə** köçürülür, davranış dəyişmir.
- Data faylının **hash-i** (məs. `StateHasher` ilə). Faza 1-də matç başlayanda iki telefon hash-i müqayisə edəcək: fərqli balans versiyası olsa, matç başlamır.
- Unity faylları `StreamingAssets` və ya `Resources` vasitəsilə yükləyir. Testlər birbaşa `data/` qovluğundan oxuyur.
- Yeni testlər: JSON düzgün oxunur, pis fayl xəta verir, hash eyni məlumat üçün eynidir, mövcud ssenari testi (`StartingSquad_AttackingTheBaseTogether_BeatsTheGuards`) dəyişməz keçir.

Hazırdır: `Defs.cs`-də heç bir rəqəm qalmayıb, JSON-da bir rəqəmi dəyişib testi işlədəndə nəticə dəyişir.

### 0.2 Mətnlər üçün tərcümə cədvəli (~1 gün)

- `Strings` sinfi və `en.json` (açar → mətn). HUD-dakı bütün Azərbaycanca sətirlər ingilis açarlarına çevrilir ("Tikinti" → `ui.build`).
- Boş açar çıxsa ekranda açarın özü görünür və log-a yazılır, tətbiq çökmür.
- Yoxlama: Unity-nin standart şrifti ingilis simvollarını göstərir. Sonra dil əlavə etməyin yolu: yeni `az.json`, kod dəyişmir.

### 0.3 Debug alətlərini ayırmaq (~1 gün)

- İndi sınaq düymələri (`Sınaq: +25 mənim +25 düşmən`, tikinti sahələri, çərçivə) əsas HUD-dadır.
- `DebugPanel` ayrı sinif olur. Yalnız development build-də və ya gizli jestlə (məs. küncə 3 toxunuş) açılır. Release build-də kod daxil edilmir (`#if DEVELOPMENT_BUILD || UNITY_EDITOR`).
- Performans sətri (sim ms, FPS) də buraya köçür.

### 0.4 Layihə kimliyi: ad, paket adı, ikon (~½ gün, **oyunun adı lazımdır**)

- Oyunun adı seçilir. Ad seçiləndən sonra yoxlayıram: Play Store-da eyni/çox oxşar ad, `.com` domen, ticarət nişanı bazasında açıq ziddiyyət (bu hüquqi məsləhət deyil, yalnız ilkin yoxlamadır).
- Paket adı `com.imamedoff.gameattempt` əvəzinə yekun ada dəyişir (`ProjectSettings.asset`). **Play Store-a yükləndikdən sonra paket adını dəyişmək mümkün deyil**, ona görə bunu indi düzgün etmək vacibdir.
- Tətbiqin görünən adı, müvəqqəti ikon (sonra Faza 7-də dəyişir), `versionName` / `versionCode` qaydası.

### 0.5 İmza açarı (keystore) (~½ gün)

- Release imza açarı yaradıram. Repoya **düşmür** (`.gitignore`), parol repoda saxlanmır.
- **Sənin işin:** açarın surətini və parolu ən azı 2 yerdə saxlamaq (USB + parol meneceri). Açar itsə, Play Store-dakı oyunu yeniləmək olmur. Play App Signing istifadə edəcəyik, bu da riski azaldır, amma upload açarını da qorumaq lazımdır.
- Build skripti (`GameBuild.cs`) debug və release üçün ayrı rejim alır.

### 0.6 Art istiqaməti və performans sınağı (~3–4 gün, ən böyük risk)

Realist low-poly + kölgələr telefonu ən çox yoruşan seçimdir. Buna indi baxırıq ki, sonradan çox iş atılmasın.

- **Art bible (1 səhifə):** iki xəyali ölkənin rəng palitrası (tünd yaşıl/qum/boz kimi), vahid üçün üçbucaq limiti, material sayı limiti, kölgə qaydası.
- **Sınaq:** bir piyada, bir tank və bir bina yeni üslubda modelləşdirilir (Blender skripti ilə), URP kölgələri və tünd işıqlandırma qoşulur.
- **Ölçmə:** Galaxy A34-də `Sınaq` düyməsi ilə ~100 vahid + kölgələr. İndiki nəticə: 60 FPS, ən pis kadr 17 ms.
- **Qərar nöqtəsi:**
  - FPS ≥ 55 və telefon qızmırsa: üslub təsdiqlənir.
  - 40–55 arası: vahidlər üçün saxta (blob) kölgə, yalnız binalar üçün real kölgə.
  - 40-dan aşağı: kölgə azaldılır və ya üslub daha sadə istiqamətə çəkilir. Mən nəticəni şəkillə göstərib sənə seçim verirəm.
- Sənin işin: bir neçə ekran görüntüsünə baxıb "bu üslub xoşuma gəlir / gəlmir" demək.

### 0.7 Orijinallıq qaydaları (~½ gün)

- Qısa `ORIGINALITY.md`: Art of War 3-ün və başqa oyunların adı, loqosu, modeli, səsi, mətni, xəritəsi, interfeys düzəni kopyalanmır; vahid adları və fraksiya adları özümüzündür; AI alətlərinə başqa oyunun skrinşotları mənbə kimi verilmir; mağaza mətnində başqa oyuna istinad olmur.
- Bu hüquqi sənəd deyil, öz iş qaydamızdır. Buraxılışdan əvvəl (Faza 8) istəsən, mütəxəssis vəkilə göstərmək olar.

### 0.8 Repo təmizliyi və sənədlər (~½ gün)

- `README.md`: layihə nədir, qovluqlar (`sim-core`, `unity-client`, `art`, `data`, boş `server`/`backend` nə üçündür), necə test və build edilir.
- `.gitignore` yoxlanır (keystore, Unity `Library`, `Builds`).
- Yaddaş qeydləri və `PROJECT_NEEDS.md` yenilənir.
- Gözləyən commit-lər: atəş qövsü işləri və plan faylları.

## Ardıcıllıq

1. 0.1 data faylları. Ən böyük texniki iş, qalanlarının çoxu ondan asılı deyil, amma Faza 1 ona söykənir.
2. 0.2 tərcümə cədvəli və 0.3 debug panel (UI faylları ilə birlikdə).
3. 0.6 art sınağı (adın seçilməsini gözləmir, paralel gedə bilər).
4. 0.4 ad və paket adı, 0.5 imza açarı (ad seçiləndən sonra).
5. 0.7, 0.8 sənədlər.

## Faza 0 bitdi deyək, əgər

- [x] Bütün rəqəmlər `sim-core/src/SimCore/Resources/gamedata.json`-dadır, testlər yaşıldır (113), davranış dəyişməyib. (Üç fayl əvəzinə bir fayl: Unity paketi yalnız öz qovluğunu görür və hash bir yerdən hesablanır.)
- [x] HUD mətnləri tərcümə cədvəlindən gəlir (`Assets/Resources/Strings/en.json`, hər açarın mövcudluğu testlə yoxlanır).
- [x] Debug düymələri release build-də yoxdur (telefonda yoxlandı).
- [ ] Oyunun adı seçilib, paket adı yekundur. **Ad gözlənilir.**
- [x] Release imza açarı yaradılıb (`~/.config/game-keys/`, repoda yoxdur). **Sənin işin qalıb:** ehtiyat surət (aşağıya bax).
- [x] Art üslubu A34-də ölçülüb və təsdiqlənib (`art/ART_BIBLE.md`). Sənin baxışın qalıb.
- [x] `ORIGINALITY.md` və `README.md` var.
- [x] Yeni APK telefonda işləyir, demo əvvəlki kimi oynanır.

## Sənin payın

| Nə | Nə vaxt |
|---|---|
| Oyunun adını seçmək (mən variant verirəm) | əvvəlcə |
| Art sınağının ekran görüntülərinə baxıb fikir demək | 0.6 sonunda |
| Keystore ehtiyat surətini saxlamaq | 0.5-dən sonra |
| Telefonda yeni APK-nı sınamaq | Faza sonunda |

## Riskler

- **Kölgələr yavaş çıxa bilər.** 0.6-da aşkarlanır, plan B var (blob kölgə).
- **Data fayllarına keçid balansı səhvən dəyişə bilər.** Mövcud ssenari testi və 85 test bunu tutur.
- **Ad tutulmuş ola bilər.** Ona görə bir neçə variant seçirik.
