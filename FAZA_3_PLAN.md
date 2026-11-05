# Faza 3: Kampaniya (plan və vəziyyət)

> Ümumi plan: `GAME_PLAN.md`. Əvvəlki faza: `FAZA_2_PLAN.md` (2.8 telefon sınağı açıq qalıb, istifadəçi real qrafika olanda oynayacaq).
> Məqsəd: 10 missiyalı kampaniya, ulduz sistemi, kredit mükafatı, irəliləyiş yaddaşı. Bütün məntiq sim-core-dadır (deterministik, testlənir), Unity yalnız göstərir.

## Qərarlar

- **Missiya = başlanğıc dünya + rəqib + şərtlər.** `Mission` (sim-core/Campaign.cs) dünyanı qurur; `MissionRun` hər tick-də rəqibi (AiPlayer), ssenari dalğalarını və şərtləri idarə edir. Unity `World.Step()` əvəzinə `Mission.Advance()` çağırır.
- **Şərt növləri:** düşmən HQ-nu məhv et, bütün düşmən binalarını məhv et, X saniyə sağ qal, N bina tik, ordu ölçüsü N, N düşmən vahidi öldür. Hər missiyada bir neçə şərt ola bilər (hamısı lazımdır). Vaxt limiti dəstəklənir (hələ heç bir missiya istifadə etmir).
- **Ulduzlar:** 1 = qələbə, 2 = "par" vaxtından tez, 3 = az itki (`MaxLosses`). Kredit hər ulduza yalnız ilk dəfə qazananda verilir (`CampaignProgress.Record`). Kreditlər Faza 4 mağazasında xərclənəcək.
- **Irəliləyiş:** `CampaignProgress.Serialize()` qısa mətn ("3,2,0,...|kredit"), indi `PlayerPrefs`-də, sonra server hesabında. Pozulmuş mətn boş irəliləyiş verir, xəta atmır. Növbəti missiya əvvəlkini udandan sonra açılır.
- **Mətnlər:** `mission.N.name/brief`, `objective.*`, `campaign.*` açarları `en.json`-da; testlər hər açarın mövcudluğunu yoxlayır.
- Missiya xəritələri 64x64 (`Campaign.cs`): 1 Outpost (rəqibsiz sınaq), 2 iqtisadiyyat (sülh), 3 müdafiə (dalğalar), 4 asan AI, 5 maşınlarla qala, 6 kanyon, 7 orta AI, 8 qüllələrlə qala, 9 son müdafiə (güclü dalğalar), 10 çətin AI.

## İşlər

- [x] 3.1 Sim-core: `Mission`, `MissionRun`, `Campaign` (10 missiya), `CampaignProgress`, `AttackWave`, `SecondsToNextWave`.
- [x] 3.2 Testlər (`CampaignTests`): 10 missiya qurulur, hər birində iki HQ və aralarında yol var, eyni toxum eyni hash, qələbə/məğlubiyyət/vaxt limiti/ulduzlar/dalğalar/şərtlər, irəliləyişin yadda saxlanması və pozulmuş mətn, mətn açarları, kompüter oyunçu 1/2/5-ci missiyaları udur.
- [x] 3.3 Balans ölçmə aləti: `CAMPAIGN=1` rejimi (hər missiya, hər çətinlikdə kompüter oyunçu, 4 toxum). İlk nəticə: 1–5 asan, 6 sıçrayış, 7 Medium oyunçu udur Easy uduzur, 10 yalnız Hard oyunçu udur. İstifadəçi sınağından sonra dəqiqləşdirilir.
- [x] 3.4 Unity: Kampaniya düyməsi, missiya siyahısı (kilid, ulduzlar), brifinq (mətn, şərtlər, ulduz tələbləri), oyun içi şərt siyahısı + növbəti hücum sayğacı, nəticə kartı (ulduzlar, vaxt, itki, kredit; Növbəti / Yenidən / Menyu), irəliləyiş `PlayerPrefs`-də. Dev build-də debug paneldə "Win mission".
- [x] 3.5 Telefonda yoxlandı: menyu → siyahı → brifinq → missiya → fasilə/təslim → nəticə kartı → qələbə kartı (ulduz, kredit) → menyu → proqramı bağla/aç → irəliləyiş qalır, 2-ci missiya açılır. Telefonun datası təmizləndi (sınaq irəliləyişi qalmasın).
- [ ] 3.6 Real oyun sınağı (istifadəçi): missiyaların çətinliyi, brifinq mətnləri, ulduz hədləri. Nəticəyə görə `Campaign.cs` rəqəmləri düzəldilir.
- [ ] 3.7 Missiya xəritələri üçün real görünüş və hekayə (Faza 7 ilə birlikdə): hazırda hamısı eyni səhra, mətnlər qısa.

## Məlum məhdudiyyətlər

- Kampaniyada yalnız bir çətinlik; ulduz hədləri kompüter oyunçuya görə təxmin edilib, insan oyunçu üçün ayrıca ölçülməyib.
- Missiyalar arasında vahid/ordu daşınmır (hər missiya sıfırdan başlayır); gələcəkdə mağazadan alınan silahlar burada tətbiq olunacaq (Faza 4).
- Dil: yalnız İngilis (1.0 planı), mətnlər tərcümə cədvəlindədir.
