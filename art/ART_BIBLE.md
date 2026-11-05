# Art istiqaməti (Faza 0 sınağı)

**Seçim:** daha realist low-poly: tünd, solğun tonlar, real kölgələr, qumlu/səhra xəritə. Rəng parlaq çəhrayı-mavi yox, hərbi (zeytun, xaki, boz, yanmış qum).

## Sınağın nəticəsi (Samsung Galaxy A34, ~100 vahid döyüşdə)

Əvvəlki demo: sadə modellər, kölgə yoxdur. Yeni sınaq: 4–6 dəfə çox üçbucaq və kölgələr. Hər halda **60 FPS saxlanıldı**. Daha dəqiq ölçü üçün GPU-nun hər kadra sərf etdiyi vaxt (kadr büdcəsi 60 FPS-də 16,7 ms):

| Konfiqurasiya | GPU, ms | Qeyd |
|---|---|---|
| Sadə modellər, kölgə yox (əvvəlki demo) | 7,6 | |
| Sadə modellər, sərt kölgə 1024 | 8,6 | +1 ms |
| Sadə modellər, yumşaq kölgə 2048 | 13,8 | +6 ms, baha |
| Detallı modellər, kölgə yox | 7,7 | modellər demək olar ki, pulsuzdur |
| Detallı modellər, sərt kölgə 1024 | 10,1 | **seçilən yol** |
| Detallı modellər, sərt kölgə 2048 | 10,2 | 1024-dən fərqi yox |
| Detallı modellər, yumşaq kölgə 2048 | 14,4 | büdcənin yarısından çoxu, rədd |

Batareya temperaturu bütün testlər boyu 29–34 °C qaldı (demo vaxtı 28 °C idi).

**Qərar: üslub təsdiqlənir.** Sərt (yumşaq olmayan) kölgə + detallı modellər A34-də rahat sığır. Yumşaq kölgə istifadə etmirik. Zəif telefonlarda (A34-dən 2–3 dəfə zəif) ehtiyat çox deyil, bunu Faza 8-də ayrıca yoxlayırıq. Ehtiyat plan: vahidlər üçün saxta (blob) kölgə, yalnız binalar üçün real kölgə.

Şəkillər: `art/reference/faza0-simple-vs-detailed.png` (sol sadə, sağ detallı + kölgə), `art/reference/faza0-dusk-hard-shadows.png` (tam ekran, axşam işığı).

## Büdcələr (hər model üçün yuxarı hədd)

| Nə | Üçbucaq |
|---|---|
| Piyada | 700 |
| Yüngül texnika (cip) | 1 500 |
| Ağır texnika (tank) | 2 500 |
| Bina | 3 500 |
| Ağac, qaya, bəzək | 300 |

- Bütün oyun **1 material + 1 palitra teksturası** istifadə edir (rəngi UV ilə seçirik). Bu, çəkiliş çağırışlarını az saxlayır. Yeni material yalnız bütün komanda ilə razılaşdırıb əlavə olunur.
- Kölgəni yalnız **əsas işıq** verir: sərt kölgə, 1 kaskad, 1024 xəritə, məsafə 40. Başqa dinamik işıq yoxdur.
- Bir kadrda 100–150 vahid + bütün binalar bu büdcəyə sığmalıdır. Hər yeni vahid modeli sınaq düyməsi ilə ölçülür.

## Qaydalar

1. **Komanda rəngi oxunmalıdır.** Sınaqda detallı piyada bütün bədəni zeytun rəngdə idi və komandanı ayırmaq çətinləşdi. Final modellərdə komanda rəngi (mavi / qırmızı) qol sarğısında, kürək çantasında, texnikanın panelində və bayraqda olur, tünd tonlarla itmir.
2. **Silueti oxunaqlı saxla.** Kamera yuxarıdan baxır. Hər vahidin silueti uzaqdan fərqlənməlidir (tank: uzun lülə, raket atan: çiyində boru).
3. Rənglər tünddür, amma **qara yox**: ən tünd ton `#151518`-dən açıq olmalıdır, yoxsa kölgədə itir.
4. Xəyali ölkələrin rəng əsasları (təklif, final yox): birinci tərəf zeytun + mavi detal, ikinci tərəf qum-boz + qırmızı detal. Real bayraqlara oxşamır.

## Modelləri yenidən yaratmaq

```bash
blender -b -P art/blender/generate_assets.py -- unity-client/Assets/Models unity-client/Assets/Textures/palette.png <previews_qovluğu>
# yalnız detallı modellər: sona  --only=_hd  əlavə et
```

Sınaq modelləri (`*_hd`) `generate_assets.py` içindədir: piyada, raket atan, tank, kazarma. Final art üçün onlar əsas götürülüb təkmilləşdiriləcək (Faza 7).

## Performans sınağını təkrar etmək

Sınaq build-i (`GameBuild.BuildAndroidDev`) telefona qoyulur, yuxarı soldakı resurs yazısına 3 dəfə toxun, debug panel açılır: **Shadows**, **Models**, **Mood**, **FPS limit** düymələri. Nəticələr `adb logcat -s Unity | grep Perf` ilə oxunur (hər 5 saniyədə bir sətir: FPS, GPU ms, vahid sayı).

## Hazırkı vəziyyət (Faza 2 sonu, 2026-11-05)

Göndərilən görünüş: **detallı modellər + sərt kölgə (1024) + axşam əhval-ruhiyyəsi**. Bütün 6 vahid və 7 bina üçün `*_hd` modelləri var (`generate_assets.py`: jeep_hd, sniper_hd, armored_car_hd, hq_hd, power_plant_hd, supply_center_hd, vehicle_factory_hd, repair_station_hd, tower_hd + əvvəlki rifleman/rocket/tank/barracks). Sadə modellər ehtiyat kimi qalır (debug paneldə `Models` düyməsi). Üçbucaq sayları büdcədən xeyli aşağıdır (bina 340–1140, büdcə 3500), yəni final art üçün yer var. Bu hələ proseduralı "blok" üslubudur; real rəssam işi, animasiya, effektlər Faza 7-dədir.

## Qrafika keçidi (2026-11-05): kod ilə edilən cila

Rəssam işi olmadan, yalnız kodla əlavə olunanlar (hamısı telefonda yoxlandı, 46 vahidlik döyüşdə 60 FPS, GPU ~10 ms):

- **Effektlər** (`Fx.cs`, hovuzlu, şəffaflıq yoxdur, kölgə atmır): lülə alovu, partlayış (od topu + zərbə halqası + yüksələn tüstü), texnikanın arxasında toz, zədəli binadan tüstü (can < 50 %) və alov (< 25 %) (`BuildingFx.cs`). Eyni anda ən çox 220 effekt.
- **Vahid animasiyası** (`UnitView.Animate`): piyada yeriyəndə yellənir və qalxıb enir, texnika yırğalanır, atəş açanda geri təpir. Model hərəkət edir, sim vahidi yox.
- **İnterfeys örtüyü** (`UiSkin.cs`): bütün düymə və panellər yuvarlaq tünd-mavi üslubda (prosedural 9-hissəli teksturalar), resurs və missiya panelləri fonlu.
- **Xəritə kənarı**: yer rəngi xəritədən 24 xana kənara davam edir (ən yaxın kənar xananı izləyir, çay davam edir), 12 xana enində seyrək qaya/kaktus/kol; qara boşluq yoxdur.

Hələ yoxdur (Faza 7): real modellər/animasiya klipləri, səs və musiqi, soft şəffaf tüstü, minimap, bina məhv olma anımasiyası (dağıntı).

## Səs (2026-11-05)

Bütün səslər sıfırdan, kodla yaradılır (`art/audio/generate_sounds.py` → `unity-client/Assets/Resources/Audio/*.wav`, lisenziya problemi yoxdur): 3 tüfəng səsi, top, raket, kiçik/böyük partlayış, klik, bina qoyma, vahid hazır, qələbə, məğlubiyyət, xəbərdarlıq (hələ istifadə olunmur) və 48 saniyəlik tünd ambient musiqi dövrəsi. Unity tərəfi `Sfx.cs`: 14 səs mənbəsi növbə ilə, eyni səs üçün minimum fasilə, ekrandan kənar səs 0,3 qat. Menyuda və pauzada "Sound: ON/OFF" (PlayerPrefs). Quraşdırıldı, çökmə və xəta yoxdur; səsin keyfiyyətini mən eşidə bilmirəm, rəy lazımdır. Yenidən yaratmaq: `python3 art/audio/generate_sounds.py unity-client/Assets/Resources/Audio`.
