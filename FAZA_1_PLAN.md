# Faza 1: 1v1 şəbəkə prototipi (ətraflı plan)

> Ümumi plan: `GAME_PLAN.md`. Müddət: 2–3 həftə. Məqsəd: iki cihaz internet (və ya yerli şəbəkə) üzərindən eyni matçı oynasın və eyni nəticəni görsün.
> Bu faza **qərar nöqtəsidir**: lockstep işləyirsə əsas oyunu üstünə qururuq, işləmirsə arxitekturanı burada düzəldirik.

## Əsas fikir (sadə dillə)

İki telefon bir-birinə oyunun vəziyyətini göndərmir. Yalnız **əmrləri** göndərir ("bu vahidləri ora göndər", "bu binanı tik"). Hər iki telefon eyni əmrləri eyni tick-də eyni qaydada tətbiq edir və `sim-core` deterministik olduğu üçün hər ikisində eyni oyun alınır. Bu, demo planında qurduğumuz əsasın məqsədi idi.

Üç hissə:
1. **Oyunun beyni** (`sim-core`) iki oyunçunu tanımalıdır: hər oyunçunun öz resursu, enerjisi, binaları, vahidləri.
2. **Lockstep qatı** (`sim-core`, Unity-dən asılı deyil): əmrləri "növbələrə" yığır, hamının əmri gəlməyincə oyunu irəli aparmır. Şəbəkəsiz test oluna bilir.
3. **Server** (yeni, .NET): yalnız relay. Oyunçulardan əmrləri alıb ikisinə də göndərir və hash-ləri müqayisə edir. Oyunu özü hesablamır (ilk mərhələdə).

## İşlər

### 1.1 İki oyunçulu dünya (~3–4 gün) ✅ bitdi

İndi `World` bir oyunçunu bilir (resurs, enerji, gəlir təkdir, əmrlər həmişə "mənim komandam" kimi qəbul olunur).

- Hər komandanın ayrıca vəziyyəti: resurs, enerji, gəlir. Köhnə `World.Resources` və s. 0-cı komanda üçün qısayol kimi qalır, UI və testlər pozulmur.
- Hər əmrin sahibi var (`Command.Team`). Əmri yalnız sahibinin vahidinə/binasına tətbiq etmək olar. Başqasının vahidinə əmr vermək qadağandır (saxtakarlığa qarşı ilk qat, serverdə də yoxlanacaq).
- İki tərəfin də binaları işləyir və vahid istehsal edir (indi yalnız oyunçunun).
- `StateHash` hər iki oyunçunun vəziyyətini əhatə edir.
- Testlər: iki oyunçu eyni vaxtda tikir/istehsal edir və bir-birinə təsir etmir; başqasının vahidinə əmr verilsə ignor olunur; eyni əmrlər ardıcıllığı iki ayrı `World`-də eyni hash verir.

### 1.2 Əmrlərin ikili formatı (~2 gün) ✅ bitdi

Şəbəkə ilə yalnız bayt göndərilir.

- Hər əmr növü üçün sıxılmış, **dəqiq müəyyən sıralı** ikili yazı və oxu (float yoxdur, endianness sabitdir).
- Pis və ya kəsilmiş məlumat çökmə yox, rədd ilə nəticələnir (serverə hücumdan qorunma).
- Testlər: hər əmr yaz→oxu→eyni; təsadüfi zibil məlumat heç vaxt istisna atmır; maksimum ölçü limiti.

### 1.3 Lockstep mühərriki (~4–5 gün) ✅ bitdi

- **Növbə (turn)** = 4 tick (saniyədə 5 növbə). Əmr indiki növbədə yox, **`gecikmə` növbə sonra** icra olunur (başlanğıc: 2 növbə = 400 ms). Beləliklə əmr şəbəkədən gəlib çatmağa vaxt tapır.
- Oyun yalnız hər iki oyunçunun həmin növbə üçün əmr paketi (boş olsa da) gəldikdə irəliləyir.
- Hər 20 tick-də state hash göndərilir. Fərq olarsa: **desync aşkarlanır**, oyun dayanır və hər iki tərəfdə hash və tick yazılır (debug üçün).
- Şəbəkəsiz test: iki mühərrik yaddaşdakı süni "kabel" ilə bağlanır; kabel gecikmə, ardıcıllığın pozulması və paket itkisini simulyasiya edir. Yüzlərlə təsadüfi oyun eyni hash verməlidir.

### 1.4 Server və protokol (~4–5 gün) ✅ bitdi

- Yeni layihə `server/`: .NET 10, TCP (`TCP_NODELAY`), hər mesajın əvvəlində uzunluq. Bu mərhələdə TCP kifayətdir (saniyədə 5 kiçik paket).
- Otaq sistemi: biri otaq açır (kod alır), digəri kodla qoşulur. Server hər növbə üçün hər iki oyunçunun paketini gözləyir və ikisinə də göndərir.
- Hər oyunçunun komanda nömrəsini **server** təyin edir, əmrə server damğa vurur. Oyunçu başqasının adından əmr göndərə bilməz.
- Hash müqayisəsi serverdə: fərq olarsa matç bitirilir və hər iki log faylı saxlanır.
- Heartbeat və timeout: cavab verməyən oyunçu 10 san-dən sonra "qopdu" olur.
- Testlər: iki real test klienti serverə qoşulur və 5 000 tick oynayır (avtomat).

### 1.5 Unity klienti (~4–5 gün) ✅ bitdi (telefon ↔ PC botu; iki real insanla sınaq qalır)

- Hazırkı demo bir oyunçulu və kompüter nəzarətli düşməni var. Yeni **"Online sınaq"** rejimi: iki baza, iki oyunçu, düşmən qoruyucuları yoxdur.
- Debug paneldə "Host" / "Join + kod" düymələri. Əmrlər birbaşa `World.Enqueue` əvəzinə lockstep qatından keçir.
- İnterfeys "mənim komandam" anlayışına keçir (indi sabit 0).
- Rəqibin binaları və vahidləri rənglə fərqlənir (indiki palitra mexanizmi qalır).
- İlk test: PC-də Unity Editor/ikinci proses + A34 telefon eyni Wi-Fi-da.

### 1.6 Qopma və geri qoşulma (~3 gün) ✅ bitdi

- Əmr jurnalı (hamısı) serverdə saxlanır. Geri qoşulan oyunçu bütün əmrləri alıb oyunu başdan **sürətli hesablayır** (deterministik olduğu üçün) və yetişir.
- Gecikmə yüksək olarsa oyun yavaşlayır, amma desync olmur.
- Testlər: matçın ortasında bir klient bağlanır, qayıdır, hash eynidir.

### 1.7 Ölçmələr və qərar (~2 gün) ✅ ölçüldü, qərar səndədir

- Real Wi-Fi və mobil internet üzərində: orta gecikmə, ən pis gecikmə, əmrin görünmə müddəti.
- Süni pis şərait (itki, 300 ms gecikmə): oyun necə hiss olunur.
- Sim tick xərci: iki oyunçu və 150+ vahid ilə A34-də (indi orta 1,2 ms).
- **Qərar nöqtəsi:**
  - Desync yoxdur, gecikmə hiss olunmur: davam.
  - Desync tapılsa: səbəbi tapılana qədər Faza 2-yə keçmirik (hər nasazlıq sim-core-da bir qaydanın qeyri-deterministik olduğunu göstərir).
  - Gecikmə yüksəkdirsə: giriş gecikməsini adaptiv edirik, və ya UDP-yə keçid planlayırıq.

## 1.7 nəticələri (2026-11-04)

Qurğu: Galaxy A34 (Wi-Fi `QDF-WiFi-Guest`, 5 GHz, siqnal −68 dBm) ↔ PC-dəki server ↔ PC-dəki bot. Ölçü: telefonun bir növbəni göndərməsindən həmin növbənin serverdən qayıtmasına qədər vaxt (rəqibi gözləməyi də daxildir). Giriş gecikməsi 2 növbə = 400 ms. Süni gecikmə serverdə hər istiqamətdə əlavə olunub, ona görə dövrə (RTT) təxminən 2 dəfə artır.

| Şərait | RTT orta | RTT p95 | ən pis | oyun dayanması | oyunun hissi |
|---|---|---|---|---|---|
| Real Wi-Fi (xalis) | 24–60 ms | 50–215 ms | 230 ms | 0 % | axıcı |
| +150 ms hər istiqamət (± 20) | ~340 ms | 350–400 ms | 450 ms | 0 % | axıcı (400 ms-lik tamponun içində) |
| +300 ms (± 50) | ~690 ms | 750–830 ms | 830 ms | ~40 % vaxt | oyun ~60 % sürətlə gedir, yenə də desync yoxdur |
| +500 ms (± 100) | ~1 170 ms | 1 230–1 350 ms | 1 350 ms | ~61 % | çox yavaş, amma işləyir |

- Gecikmə normaya qayıdanda oyun dərhal yetişdi (bufer 3 növbəyə qayıtdı). **Desync heç bir mərhələdə olmadı**, server log yazmadı.
- Qayda: dövrə vaxtı ~400 ms-ə qədər oyun axıcıdır. Bundan sonra dayanmalar başlayır. Əmr gecikməsi: həmişə sabit ~400 ms (giriş gecikməsi) + yarım RTT-dən az.
- Sim tick xərci A34-də 135 vahid ilə: orta 3,7 ms, ən yüksək pəncərə 6,4 ms (döyüş zamanı, 20 tick/s). 60 FPS saxlanıb, ən pis kadr 16,8 ms. Oyunda vahid limiti 40+40 olacaq, ehtiyat çoxdur.
- Mobil internet və uzaq VPS üzərində ölçmə hələ edilməyib (server yalnız yerli kompüterdə idi). Faza 5 əvvəlində VPS ilə təkrarlanmalıdır.

**Tövsiyə:** arxitektura təsdiqlənir (lockstep + relay + qayıtma işləyir). Giriş gecikməsini adaptiv etmək (RTT p95 > 350 ms olanda 3–4 növbəyə qaldırmaq) Faza 5-ə (Online 1v1) əlavə olunur: bu, yüksək gecikmədə dayanmaları azaldır.

## Faza 1 bitdi deyək, əgər

- [x] İki oyunçulu dünya, hər tərəfin öz iqtisadiyyatı, sahiblik yoxlaması, testlər yaşıl (12 yeni test: `TwoPlayerTests`).
- [x] Əmrlərin ikili formatı, zibil məlumata davamlı (`CommandCodec`, 50 000 təsadüfi zibil məlumat testi, 15 test).
- [x] Lockstep mühərriki süni şəbəkədə desync-siz (`LockstepClient`, `LockstepRelay`, `MessageCodec`; 70 təsadüfi oyun + 5 dəqiqəlik oyun, gecikmə 0–600 ms, 1,5 san dayanmalar).
- [x] Server işləyir, iki klient 5 000 tick oynayır (`server/GameServer`, 13 test real soketlərdə: 5 000 tick, saxta dünya → desync aşkarlanır və log saxlanır, qaydasız növbə göndərən klient atılır, 8 paralel matç, zibil məlumat, timeout).
- [x] Unity-də telefon (A34) serverlə PC-dəki bot-klientə (`server/BotClient`) qarşı oynadı: 1 600+ tick, telefonun əmrləri (istehsal) işləyir, server hər saniyə hash müqayisə edir, desync yoxdur. Qalır: iki real oyunçu eyni Wi-Fi-da (aşağıya bax).
- [x] Qopub qayıdan oyunçu yetişir (server matçı 60 san saxlayır; qayıdan şəxsi açarla (token) girir, server bütün növbə jurnalını göndərir, klient dünyanı başdan sürətlə hesablayır). 5 yeni server testi, hash eynidir; telefonda `drop` ilə qoparılıb: 1 saniyədən az müddətdə qayıdıb və desync olmadan davam etdi.
- [x] Gecikmə və tick ölçmələri yazılıb (aşağıda "1.7 nəticələri"). Qərar: istifadəçidə.

## Sənin payın

| Nə | Nə vaxt |
|---|---|
| Server üçün yer: ilk mərhələdə **sənin kompüterin** (yerli şəbəkə), pul tələb olunmur. İnternetdən sınaq üçün ayda ~5–10 $ VPS lazım olacaq (1.7-də) | 1.7-dən əvvəl |
| Telefonla birgə sınaq (iki telefon olsa daha yaxşı: ikincisi lazım deyil, PC də oynaya bilər) | 1.5-dən sonra |

## Risklər

- **Desync.** Ən böyük risk. Deterministik əsas (floatsız, sıralı) bu riski azaldır, amma `Dictionary` sıralaması, `HashSet` dövr sırası və s. kimi gizli qeyri-determinizm yarana bilər. Hash yoxlaması və yüzlərlə təsadüfi oyun testi bunu tutur.
- **TCP gecikməsi.** İtki olan şəbəkədə TCP növbəni gecikdirir. İlk mərhələdə qəbul edirik, 1.7-də ölçürük.
- **Mobil şəbəkənin qəfil dəyişməsi** (Wi-Fi ↔ 4G): bağlantı qopur. 1.6 geri qoşulmaya ehtiyacı bununla əsaslandırılır.
