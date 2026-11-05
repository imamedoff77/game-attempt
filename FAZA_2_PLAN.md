# Faza 2: Tam matç mexanikası, kompüterə qarşı (ətraflı plan)

> Ümumi plan: `GAME_PLAN.md`. Əvvəlki faza: `FAZA_1_PLAN.md` (bitdi, şəbəkə təməli təsdiqləndi). Müddət: 6–8 həftə.
> Məqsəd: 20–60 dəqiqəlik tam matç kompüterə qarşı oynana bilsin: iqtisadiyyat, baza səviyyələri, vahid limiti, 6 vahid növü, yüksəltmələr, ağıllı rəqib, düzgün hərəkət. Bu faza bitəndə oyunun **özəyi** hazırdır; qalan fazalar onun ətrafına menyu, kampaniya, onlayn, mağaza əlavə edir.

## Əsas qaydalar (GAME_PLAN-dan, dəyişməyib)

- Resurs avtomatik gəlir (toplayıcı yoxdur). Enerji balans kimi işləyir, çatmayanda bina qalır amma işləmir.
- Eyni növ binalar azalan gəlir/enerji verir (cədvəllər artıq `gamedata.json`-dadır).
- Vahid limiti: **hər vahidin "yer" dəyəri var**. Piyada 1/2/2, maşın 2/3/4. Baza 1-ci səviyyədə 20, 2-ci səviyyədə 40 yer verir.
- Vahid hərəkət edərkən də atəş aça bilər (artıq var), hər vahidin öz atəş qövsü var (artıq var).
- Sim-core hələ də **deterministik və floatsız** qalır. Hər yeni qayda `gamedata.json`-dan oxunur, koda rəqəm yazılmır.
- Hər yeni qayda həm kompüterə qarşı, həm onlayn rejimdə eyni işləməlidir (lockstep). Ona görə hər addımda "iki World eyni əmrlərlə eyni hash verir" testi yenilənir.

## İşlər

Hər iş sim-core-da testlərlə başlayır, sonra Unity-də görünür, sonra telefonda yoxlanır.

### 2.1 Matç qaydaları: tikinti vaxtı, baza səviyyəsi, vahid limiti (~1 həftə)

İndi bina yerləşdirilən kimi hazırdır, limit yoxdur, baza bir səviyyəlidir.

- **Tikinti vaxtı.** Bina qoyulanda pul çıxılır, bina "tikilir" vəziyyətində olur: canı tədricən artır, enerji sərf etmir, istehsal etmir, gəlir vermir. Hazır olanda işə düşür. Vaxtlar `gamedata.json`-da (`buildSeconds`). Tikilməkdə olan bina ləğv edilə bilər (pulun 50 %-i qayıdır).
- **Vahid limiti.** Hər vahidin `pop` dəyəri (yer). İstehsal növbəsinə qoyanda yer ayrılır (limit aşılırsa növbəyə qoymaq olmur, "Limit dolub" mesajı). Vahid öləndə yer azad olur.
- **Baza səviyyəsi.** HQ səviyyə 1 → 2 → 3. Səviyyə yüksəltmə əmri (`UpgradeBaseCommand`): pul + vaxt (data-da). Səviyyə limiti artırır (20 / 40 / 60) və yeni vahid/bina növlərini açır (2.3-də).
- İnterfeys: yuxarıda "Yer 12/20", tikilən binalarda tikinti çubuğu, HQ paneldə "Yüksəlt" düyməsi, limit dolanda düymələr söndürülür və səbəb yazılır.
- Testlər: tikilməkdə olan bina enerji sərf etmir və istehsal etmir; limit aşılanda növbə rədd edilir; vahid ölən kimi yer azad olur; səviyyə yüksəlməsi limiti artırır; iki World eyni hash.

### 2.2 Hərəkət: vahidlər bir-birini itələməsin, formasiya, yol tapma (~1,5 həftə, ən çətin texniki iş)

İndi vahidlər bir-birinin üstündən keçə bilir və dar yerdə sıxılır.

- **Toqquşma/itələmə:** vahidlər bir-birinin içindən keçmir; dar yerdə növbə ilə keçir. Floatsız, sabit sıralı (vahid id-sinə görə) həll: hər tick-də qonşu vahidlərdən "ayrılma" vektoru. Bu, sim-in ən bahalı hissəsi ola bilər: ölçülür (limit: 80 vahidlə tick ≤ 8 ms A34-də).
- **Formasiya:** seçilmiş qrup bir yerə gedəndə xətt/blok formasında dayanır, ən yavaş vahidin sürəti ilə gedir (qrup bölünmür).
- **Yol tapma:** hazırkı A* qalır; dəyişən bloklar (binalar, dayanmış vahidlər) üçün yolun yenidən hesablanması, "ora getmək olmur" hallarında ən yaxın əlçatan nöqtəyə getmə.
- Testlər: 40 vahid dar keçiddən keçir və ilişmir (zaman limiti); iki qrup üz-üzə gəlir və bir-birinin içindən keçmir; 100 təsadüfi hərəkət testi eyni hash.

### 2.3 Vahid və bina növləri (~1 həftə)

Ən az 3 piyada + 3 maşın. Mövcud 4-ə əlavə: yer dəyərləri ilə uyğunlaşdırılır.

- Piyada (yer 1/2/2): **Tüfəngçi** (var), **Raketçi** (var, 2 yer), yeni: **Snayper** (uzaq mənzil, zəif can, 2 yer).
- Maşın (yer 2/3/4): **Cip** (var, 2 yer), yeni: **Zirehli maşın** (3 yer, orta can/zərər), **Tank** (var, 4 yer).
- Binalar: yeni **Təmir məntəqəsi** (yaxındakı vahidləri bərpa edir) və **Qoruyucu qüllə** (müdafiə). Səviyyə 2-də açılan şeylər: Zirehli maşın, Snayper, qüllə.
- Hər növ üçün: rəqəmlər `gamedata.json`-a, ad `en.json`-a, model (sadə, mövcud üslubda) `art/` skriptinə.
- "Daş-kağız-qayçı" balansı: piyada maşına zəifdir (raket istisna), tank piyadaya güclüdür amma raketçiyə zəifdir, cip sürətli və zəifdir, snayper piyadaya güclü, maşına zəifdir. Cədvəl `art/ART_BIBLE.md` kimi bir səhifədə (`balance.md`) saxlanır.

### 2.4 Yüksəltmələr (~1 həftə)

- Hər vahid kateqoriyası üçün 2–3 yüksəltmə (zərər +10 %, can +10 %, sürət/mənzil və s.), pul + vaxtla, "Texnologiya mərkəzi" və ya HQ vasitəsilə. Sim-də `UpgradeCommand`, vahidin effektiv rəqəmi = baza + yüksəltmə (tam ədədlərlə, faiz yox "mində bir" vahidlə).
- Yüksəltmələr data-dadır. Pullu "silah gücləndirmə" (Faza 4/6) bu sistemin üstündən qurulacaq, ona görə yüksəltmə mexanizmi ümumi yazılır: "bu komandanın bu vahid növü üçün modifikator siyahısı".
- Testlər: yüksəltmə yalnız sahibinin vahidlərinə təsir edir; hash eyni.

### 2.5 Kompüter rəqibi (AI) (~1,5 həftə)

- `sim-core`-da `AiPlayer`: hər 10 tick-də dünyanı oxuyur, **əmrlər** verir (insanın verə bildiyi eyni əmrlər, saxtakarlıq yoxdur, "gizli məlumat" görmür: yalnız görmə sahəsindəki düşmənləri bilir).
- Davranış: tikinti ardıcıllığı (enerji → resurs → piyada → maşın), kifayət qədər qüvvə yığılanda hücum dalğası, canı az vahidi geri çəkmək, baza hücum altındadırsa müdafiə, səviyyə yüksəltmə, vahid növlərini rəqibin tərkibinə görə seçmək.
- 3 çətinlik: Asan (yavaş, səhv edir), Orta, Çətin (tam iqtisadiyyat, daha ağıllı hədəf seçimi). Çətinlik = qərar tezliyi + səhv ehtimalı (deterministik təsadüfi ilə) + başlanğıc iqtisadiyyat (heç vaxt "oğurluq" yoxdur).
- AI **bir əmr mənbəyidir**: onlayn rejimdə AI olmur, kompüterə qarşı rejimdə AI əmrləri eyni əmr formatında (`Command`) ötürür. Beləliklə replay və gələcək "AI ilə kooperativ" mümkün olur.
- Test: AI vs AI başsız matçlar (yüzlərlə, saniyələr içində): heç biri donub qalmır, heç biri 60 dəqiqədən sonra bitməmiş qalmır, çətin AI asan AI-ni udur (≥ 90 %), eyni toxumla eyni nəticə.

### 2.6 Kompüterə qarşı oyun rejimi Unity-də (~1 həftə)

- Menyu (sadə, müvəqqəti): "Kompüterə qarşı" → çətinlik seçimi → xəritə → başla. Debug panel (onlayn) qalır.
- Matç: iki baza (OnlineScenario-nun mənimsənilmiş versiyası: ya mövcud səhra xəritəsi, ya sadə simmetrik xəritə), qələbə/məğlubiyyət (HQ məhv), "təslim ol" və "pauza" düymələri, matç vaxtı.
- Matç sonu ekranı: nəticə, vaxt, istehsal edilən/itirilən vahid sayı.
- Tək oyunçu rejimində oyun sürətini dəyişmək (x1/x2) qalır.

### 2.7 Balans və uzun matç testləri (~1 həftə)

- Başsız AI vs AI simulyasiyaları: hər çətinlik, hər fraksiya üzrə qalib faizi, orta matç vaxtı (hədəf 20–40 dəqiqə), ən çox istehsal edilən vahid. Nəticə cədvəli `balance.md`-ə yazılır, rəqəmlər `gamedata.json`-da düzəldilir.
- 60 dəqiqəlik matçda yaddaş sızması və tick xərci artımı yoxlanır (A34-də 1 saatlıq matç).
- Hash testi: eyni əmrlərlə 60 dəqiqəlik iki World eyni nəticə.

### 2.8 Telefon sınağı və qərar (~½ həftə)

- A34-də ən azı 3 tam matç (asan, orta, çətin) və fikirlər: oyun maraqlıdır? Çox uzundur/qısadır? Qaydalar aydındır?
- Performans: 80 vahid + binalar, 60 FPS, ən pis kadr, istilik (15 dəqiqə sonra).
- **Qərar nöqtəsi:** əsas oyun hissi yaxşıdırsa Faza 3-ə (kampaniya), deyilsə Faza 2-də balansı və hissi düzəldirik.

## Faza 2 bitdi deyək, əgər

- [x] Tikinti vaxtı, baza səviyyəsi, vahid limiti işləyir, testlər yaşıl (2.1): sim-core 196 test, Unity HUD (vahid sayı, tikinti zolağı, ləğv, baza yüksəltmə) yazıldı, telefonda (Galaxy A34) yoxlandı: tikinti, ləğv, baza yüksəltmə, limit 20→40.
- [x] Vahidlər bir-birini itələmir, formasiya və yol tapma təkmilləşib (2.2, sim-core 205 test). PC-də 80 vahidlə tick 0,3 ms; A34-də ölçmək qalıb.
- [x] 3 piyada və 3 maşın, yeni binalar (2.3): Snayper, Zirehli maşın, Təmir məntəqəsi, Qüllə (baza səviyyəsi 2 tələb edir). Sim + HUD hazırdır, testlər yaşıl. Modellər müvəqqətidir (mövcud modeldən götürülür), real modellər art mərhələsində. `balance.md` 2.7-də.
- [x] Yüksəltmələr işləyir (2.4): piyada/maşın üçün zərər və can, hər biri 3 pillə, HQ paneldən; `gamedata.json`-da `upgrades`, telefonda yoxlandı. Sim 228 test.
- [x] AI üç çətinliklə oynayır, AI vs AI testləri keçir (2.5): `AiPlayer` (adi `Command`-lar verir, görmə sahəsindən kənarı bilmir), simmetrik `SkirmishScenario`, 242 test. Hard Easy-ni 8:0, Medium Easy-ni qalib gəlir, Hard Medium-u ~65-75 % udur. Qalan: balans (2.7) və matç müddəti (hazırda 3-6 dəq, hədəf 15+).
- [x] Unity-də menyudan kompüterə qarşı matç (2.6): əsas menyu, çətinlik seçimi, pauza/davam/təslim, nəticə kartı (statistika, Yenidən oyna, Menyu), matç saatı, x1/x2, Sandbox (dev). Telefonda yoxlandı (2026-11-04); tam matçın sonuna qədər oynanışı 2.8-də yoxlanacaq.
- [x] Balans cədvəli və 60 dəqiqəlik sabitlik testi (2.7): `balance.md`, `sim-core/tests/BalanceReport` (AI vs AI, TRACE, SOAK). Matç 8–15 dəq (hədəf 10–15 dəq götürüldü), çətinlik sırası düzgün, 60 dəq sabit və deterministik. Hədəf vahid zərər çarpanları (`vs*Milli`) əlavə olundu. 245 test.
- [ ] A34-də 3 tam matç oynanıb, sənin rəyin alınıb (2.8).

## Sənin payın

| Nə | Nə vaxt |
|---|---|
| Qaydalar sualları (aşağıya bax): "bəli/xeyr/dəyiş" demək | 2.1 başlamazdan əvvəl və 2.3-də |
| Hər addımdan sonra telefonda oynayıb "hissiyyat" rəyi vermək (xüsusilə 2.2 hərəkət, 2.5 AI, 2.8) | hər 1–2 həftədən bir |
| Vahid növlərinin adlarını bəyənmək (xəyali ölkə mövzusu üçün) | 2.3 |

## Açıq suallar (mən təklif edirəm, sən dəyişə bilərsən)

1. Tikinti vaxtları: Enerji 12 san, Gəlir 20 san, Kazarma 25 san, Zavod 35 san. Baza yüksəltmə: səviyyə 2 = 400 pul/40 san, səviyyə 3 = 800 pul/70 san. OK?
2. Ləğv olunan tikintidə pulun 50 %-i qayıdır. Hazır bina satılmır (sonraya).
3. Bir oyunçu eyni vaxtda neçə bina tikə bilər? Təklif: **sərhədsiz** (pulu və yeri çatdıqca), çünki bu janrda adətən belədir.
4. Bir fraksiya (hamı eyni vahidlərlə). İkinci fraksiya "sonraya qalanlar"dadır.

## Risklər

- **Toqquşma (2.2) deterministik qalmalıdır.** Hər vahid cütü üçün sıralı, floatsız hesab. Sim tick xərcini artıra bilər: erkən ölçürük, çox bahalıdırsa "yalnız yaxın qonşular" (şəbəkə xanaları) optimizasiyası.
- **AI çox güclü və ya çox axmaq ola bilər.** Başsız simulyasiyalar bunu saniyələr içində göstərir.
- **Balans sonsuz iş ola bilər.** 2.7-də məqsəd "kifayət qədər yaxşı"dır, mükəmməl yox. Daha dərin balans Faza 8-də real test oyunçularıyla.
- **Yüksəltmə mexanizmi sonradan pullu silah gücləndirməyə uyğun olmalıdır.** 2.4 bunu nəzərə alır (ümumi modifikator siyahısı).
