# Demo planı

Tarix: 2026-11-02 · Status: **əsas qərarlar verildi, iqtisadiyyat detalları (3 sual) təsdiq gözləyir**

Əsas plan (fazalar, texniki mühit) saxlanılır: `PROJECT_NEEDS.md`. Bu fayl yalnız **demo** haqqındadır.

---

## 1. Demonun məqsədi

Oyunun məntiqini, 1v1-i, ssenarini qurmadan, **sadəcə oyuna girmiş kimi** bir hiss yoxlayırıq:

> Telefonda baza qurmaq, resurs və enerji idarə etmək, əsgər və maşın istehsal etmək, onları hərəkət etdirmək və düşmən hədəfə saldırtmaq necə hiss olunur?

Demo **atıla bilən** təcrübədir: nəticə "bu istiqamət işləyir, ya yox" cavabıdır. Çoxlu kod yazmırıq, hər addım telefonda sınanır.

---

## 2. Qəbul edilmiş dizayn qərarları (uzunmüddətli, demo üçün yox)

| Mövzu | Qərar |
|---|---|
| Matç uzunluğu | Art of War 3 kimi, **maksimum ~1 saata qədər** |
| Resurs | Art of War 3 kimi: **avtomatik gəlir**, toplayıcı yoxdur. Təchizat mərkəzləri gəliri artırır |
| Pullu alışlar | **Var** (yeniləmələr, booster-lər, valyuta və s.) |
| Rejimlər | **2v2 və 3v3 yoxdur.** Əsas yol: mərhələlər (kampaniya tipli), ödüllər, silah almaq və gücləndirmək |
| 1v1 | Var, amma **bəzi tapşırıqlar keçildikdən sonra açılır** |
| Vahidlər | Piyada, maşınlar, tanklar, təyyarələr və s. |
| Oyun içi | Yüksəltmələr, pulla alınan resurs gəlirini artıran booster-lər, çat |
| Meta | Gündəlik və aylıq ödüllər, tapşırıqlar (vəzifələr), bacarıqlar (yeteneklər) |
| Su və sahil | Su-ya və quru ilə suyun sahilinə **gələcəkdə** bina tikilə biləcək (tersane, gəmi istehsalı). **İndi lazım deyil**, su keçilməz və tikilməzdir |
| Unit (əhali limiti) | Hər vahid növü limitdən müəyyən sayda **unit** tutur. Piyada: 1, 2 və 2 unit (3 növ). Maşın: 2, 3 və 4 unit (3 növ). Baza 1-ci səviyyədə **20 unit** limiti verir. Baza yüksəldikcə limit artır (məs. 2-ci səviyyədə 40) |
| Baza səviyyəsi | Baza 1-ci səviyyədən başlayır, yüksəldilə bilir |

*Unit limiti və baza səviyyəsi məlumat üçün qeyd olundu. Demoda yoxdur.*

Bunların hamısı **demodan sonra**dır. Demoda heç biri yoxdur.

---

## 3. Demoda nələr var / yoxdur

**Var:**
- Bir düz xəritə, yuxarıdan maili sabit kamera (sürüşdürmə, yaxınlaşdırma)
- Hazır HQ ilə oyunçu bazası
- Resurs: avtomatik gəlir, təchizat mərkəzi gəliri artırır
- Enerji: elektrik stansiyası verir, binalar sərf edir
- Can (HP) və can göstəricisi
- 4 bina: Təchizat mərkəzi, Elektrik stansiyası, Kazarma, Texnika zavodu
- 3 vahid: Əsgər, Cip, Tank
- İstehsal növbəsi (vaxt və qiymət)
- Seçmə, hərəkət, hücum əmri, məhv olma
- Düşmən hədəflər: bir neçə hərəkətsiz bina və əsgər

**Yoxdur (qəsdən):**
1v1, ssenari, menyu, hesab, mağaza, pullu alış, booster, çat, tapşırıq, ödüllər, bacarıqlar, fog of war, təyyarə, səs, cila, multiplayer.

---

## 4. Demo addımları (hər addımın sonunda telefonda APK)

| Addım | Ad | Oyunçu nə görür və edir | Bitmə yoxlaması |
|---|---|---|---|
| **D1** ✅ | Meydança | Xəritə, kamera. Əsgər seçmək və hərəkət etdirmək | **Bitdi (2026-11-02).** Galaxy A34-də 60 FPS, toxunuşla seçmə, çərçivə ilə seçmə, hərəkət əmri və kamera sürüşdürməsi işləyir. Pinch-zoom hələ sınanmayıb |
| **D1.5** ✅ | Səhra və modellər | Səhra xəritəsi, qum/qaya/su sahələri, 3D vahid və bina modelləri | **Bitdi (2026-11-02).** Səhra xəritəsi, 5 bina və 4 vahid modeli telefonda 60 FPS, vahidlər qaya/su/binanı dolanır, "Tikinti sahələri" qatı işləyir |
| **D2** ✅ | Baza | HQ, resurs sayğacı, enerji göstəricisi. Menyudan bina seçib yerləşdirmək | **Bitdi (2026-11-03).** Tikinti menyusu, xəritədə yer seçmə (yaşıl/qırmızı), təsdiq. Resurs azalır, enerji balansı dəyişir, enerji çatmayanda binada elektrik işarəsi. Sürət x1/x5/x10. Bina ani tikilir (tikinti vaxtı yoxdur), baza ətrafında məsafə məhdudiyyəti yoxdur |
| **D3** ✅ | İstehsal | Kazarma və zavoddan əsgər/cip/tank istehsalı, növbə, çıxış nöqtəsi | **Bitdi (2026-11-03).** Binaya toxunub seçmək, növbəyə vahid qoymaq (qiymət ödənilir, ləğv edəndə qaytarılır), irəliləyiş çubuğu, toplanma nöqtəsi. Enerji çatmayanda istehsal dayanır. Əsgər 50/4s, Raketçi 80/6s, Cip 150/8s, Tank 300/15s (müvəqqəti). Növbə max 5. Vahid limiti hələ yoxdur |
| **D4** ✅ | Döyüş | Düşmən bina və əsgərlər. Hücum əmri, can azalması, məhv olma | **Bitdi (2026-11-03).** Düşmən bazası (5 bina) və 9 düşmən vahidi (8 mühafizəçi + patrul edən cip, qırmızı rəng). Düşmənə toxunmaq = hücum əmri. Can çubuqları, atəş izləri, partlayış, qələbə/məğlubiyyət yazısı. Telefonda başlanğıc dəstə (12 vahid) ilə bazanı məhv etmək mümkün oldu, 8 vahid sağ qaldı |
| **D5** | Qiymətləndirmə | 50 və 100 vahidlə ölçmə, hiss qeydləri | FPS, batareya/qızma, idarəetmə rahatlığı, "davam edirik" və ya "dəyişirik" qərarı |

---

## 5. Texniki yanaşma (qısa, ətraflı sonra)

- **Render:** URP (mobil üçün standart), D1-də qurulur. Sonra dəyişmək çətindir, ona görə indi.
- **Simulyasiya:** `sim-core`-un nazik versiyası üzərində. Hərəkət düz xətlə və sadə sıxışmadan qaçma ilə. Pathfinding, replay və şəbəkə **yoxdur**.
- **Qrafika:** D1–D3-də rənglənmiş primitivlər (kapsul = əsgər, kub = cip, kub + silindr = tank). Asset yükləmək lazım deyil. D4–D5-də istəyə görə pulsuz low-poly paket.
- **Data:** Qiymətlər, can, sürət ayrı data faylında, kodda yox. Balans sonra asan dəyişir.

---

## 6. Demo iqtisadiyyat qaydaları

İstifadəçinin təsvirinə görə (Art of War 3 modeli):

- Hər binanın **canı** var.
- **Kaynak = pul.** Bina tikmək və vahid almaq üçün lazımdır, qiymət birdəfəlik ödənir. Kaynak istehsal edən binalar var və kaynak **dəqiqədə** yığılır.
- **Enerji:** Tikilən bina işləmək üçün enerji sərf edir. Enerji istehsal edən binalar var. Enerji dəqiqədə yığılan şey deyil, **mövcud güc balansıdır** (məs. 35 / 120). ✅ təsdiqləndi
- **Əsas Baza:** başlanğıcda dəqiqədə 20 kaynak və 20 enerji verir. Oyunçunun başlanğıcda bir qədər hazır kaynak və enerjisi var.
- **Tikmə ardıcıllığı:** enerji binası → kaynak binası → piyada binası → maşın binası.
- **Azalan gəlir:** eyni növ istehsal binaları sabit dəyər vermir. Neçənci bina olmasına görə cədvəl: məs. 120 → 80 → 60 → 50 → 40. Enerji və kaynak binalarının cədvəli ayrıdır. ✅ təsdiqləndi. Dəqiq azalma qaydası əsas oyun yazılanda qərarlaşdırılacaq, demoda sadə cədvəl.
- **Enerji çatmayanda:** bina **tikilə bilir, amma işləmir**, üzərində elektrik işarəsi çıxır. Kaynak binası kaynak vermir, istehsal binası vahid istehsal etmir. ✅ təsdiqləndi. *(Hansı binaların söndürüləcəyi hələ qərarlaşmayıb. Demo üçün: balans mənfi olanda enerji istehlak edən bütün binalar söndürülür.)*
- **Sürət düyməsi:** demoda sınaq üçün x1 / x5 / x10, çünki real matç 1 saata qədər uzanır.

### İlkin rəqəmlər (müvəqqəti, data faylında dəyişdirilə bilər)

| Element | Kaynak qiyməti | Enerji | Gəlir |
|---|---|---|---|
| Əsas Baza | hazır | +20 | +20 kaynak/dəq |
| Elektrik stansiyası | 100 | +120 (sonrakılar 80, 60, 50, 40) | |
| Təchizat mərkəzi (kaynak) | 150 | −20 | +60 kaynak/dəq (sonrakılar azalır) |
| Kazarma | 200 | −30 | |
| Texnika zavodu | 300 | −50 | |
| Əsgər | 50 | | |
| Cip | 150 | | |
| Tank | 300 | | |

Başlanğıc kaynak: 500.

---

## 7. Qərarlar

| Sual | Qərar |
|---|---|
| Enerji modeli | Yuxarıdakı kimi (Art of War 3 tipli). Detallar təsdiq gözləyir |
| Sim-core üzərində yoxsa atılan prototip | **Sim-core.** İstifadəçi texniki detala girmir, qərarı Claude verir |
| Düşmən hədəflər | **Hərəkətsiz və hərəkətli ola bilər.** D4: hərəkətsiz hədəflər. D4-dən sonra: hərəkət edən və cavab atan düşmən əsgərlər |
| Setting/ad | Hələlik neytral modern |

---

## 8. Yeni tələblər (D1-dən sonra)

**Xəritə:** Səhra kimi real görünüşlü yer. Bəzi sahələrə **bina tikmək olmaz**.

**Binalar:** Görünüşləri fərqlidir və **tutduqları sahə fərqlidir** (hər binanın öz ölçüsü var).

**Vahidlər (hələlik):** 2 növ piyada, 2 növ maşın.

**3D qrafika:** Nano Banana-ya Claude-un çıxışı yoxdur (və o 2D şəkil modelidir). Claude Blender-i Python skripti ilə idarə edib low-poly modelləri özü yaradır və FBX kimi layihəyə əlavə edir. İstifadəçi istəsə, Nano Banana ilə konsept, ikon və ya tekstur yaradıb faylı verə bilər.

### Təklif olunan xəritə sahələri

| Sahə | Vahid keçə bilər? | Bina tikmək olar? |
|---|---|---|
| Qum | Bəli | Bəli |
| Daşlı yer | Bəli | **Xeyr** |
| Qaya / uçurum | **Xeyr** | **Xeyr** |
| Su (vahə) | **Xeyr** | **Xeyr** |

### Təklif olunan binalar və ölçülər (xanalarla, müvəqqəti)

| Bina | Ölçü | Görünüş ideyası |
|---|---|---|
| Əsas Baza (HQ) | 4×4 | Böyük möhkəm bina, antena |
| Elektrik stansiyası | 2×2 | Kiçik, generator və boru |
| Təchizat mərkəzi | 3×3 | Anbar, konteynerlər |
| Kazarma | 3×2 | Uzun bina, bayraq |
| Texnika zavodu | 4×3 | Geniş, qapılı anqar |

### Təklif olunan vahidlər (adlar müvəqqəti)

| Növ | Ad | Xüsusiyyət |
|---|---|---|
| Piyada | Əsgər | Ucuz, çevik |
| Piyada | Raketçi | Texnikaya qarşı, yavaş |
| Maşın | Cip | Sürətli, zəif zireh |
| Maşın | Tank | Yavaş, güclü |

### Texniki nəticə (vacib)

Qaya, su və binalar **yolu bağlayır**. Vahid onların içindən keçə bilməz, ona görə **yol tapma (pathfinding) artıq D1.5/D2-də lazımdır**. Əsas planda bu 3-cü faza idi. Demo üçün sadə, deterministic grid A* yazılır. Çoxlu vahid üçün təkmilləşdirmə (flow field və s.) sonraya qalır.
