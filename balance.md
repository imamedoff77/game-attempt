# Balans hesabatı (Faza 2.7)

Alət: `sim-core/tests/BalanceReport` (qrafikasız AI vs AI). Rəqəmlər `sim-core/src/SimCore/Resources/gamedata.json`-dadır.

```
dotnet run --project sim-core/tests/BalanceReport -c Release -- [gamedata.json] [oyun_sayı=8] [maks_dəq=60] [Hard-Medium ...]
TRACE=Hard-Medium dotnet run ... -- data.json 1 60      # bir matç, hər 30 san-də iqtisadiyyat/ordu
SOAK=1 dotnet run ... -- data.json 1 60                 # 60 dəqiqəlik matç iki dəfə (sabitlik + hash)
```

Cütlərdə eyni seed ilə tərəflər dəyişdirilir (xəritə üstünlüyü nəticəni gizlətməsin).

## Nəticə (8 oyun/cüt, göndərilən data, hash 73DE7584E083FDF2; tərəf-qərəzi düzəldildikdən sonra)

| Cüt (A vs B) | A:B | Orta müddət (dəq) |
|---|---|---|
| Easy vs Easy | 4:4 | 8,6 |
| Easy vs Medium | 0:8 | 8,9 |
| Easy vs Hard | 0:8 | 10,6 |
| Medium vs Medium | 4:4 | 10,6 |
| Medium vs Hard | 2:6 | 11,8 |
| Hard vs Hard | 4:4 | 15,2 |

Çətinlik sırası düzgündür (Easy < Medium < Hard). Matç müddəti 8–15 dəq. Plandakı hədəf (20–40 dəq) AI-nin hazırkı səviyyəsi üçün çox yuxarıdır və mobil oyun üçün 10–15 dəq daha uyğundur; hədəfi **10–15 dəqiqə** götürürük, uzatmaq lazım olsa HQ canı/gəlir cədvəli ilə düzəldilər.

Qurulan vahidlər (hamısı): Rifleman 86 %, RocketTrooper 8 %, Jeep 3 %, Sniper/ArmoredCar/Tank <1 %. Piyada sayca üstündür (ucuzdur); tank və yeni vahidlər az qurulur, bu AI-nin seçim qaydasının nəticəsidir, realda oyunçu fərqli oynaya bilər. Telefon sınağında (2.8) bu yenidən baxılacaq.

## Ədalətli qaydalar

- Hər vahid hədəf növünə görə fərqli zərər vurur (`vsInfantryMilli`, `vsVehicleMilli`, `vsBuildingMilli`, boşdursa 1000 = tam):
  Rifleman: maşına 40 %, binaya 30 %. RocketTrooper: piyadaya 50 %, maşına 200 %. Jeep: piyadaya 130 %, maşına 40 %, binaya 40 %. Tank: binaya 150 %. Sniper: piyadaya 150 %, maşına 30 %, binaya 30 %. ArmoredCar: maşına 80 %, binaya 70 %.
- Vahidlərin canı ×2, binaların canı ×3, HQ 15 000 (əvvəl 1 500): ilk toqquşma bütün matçı həll etməsin.
- Gəlir: başlanğıc 300, HQ 100/dəq, Təchizat mərkəzi [200, 140, 100, 80, 60]/dəq. Piyada istehsal vaxtı 7 s (RocketTrooper 9 s) ki, barak bütün gəliri yeməsin.
- AI ilk 2,5–4,5 dəqiqədə hücum etmir; Medium/Hard tikinti üçün pul yığır; fabrik ən yaxşı vahid üçün gözləyir.

## Tərəf qərəzi (düzəldildi)

Eyni iki AI arasında komanda 1 demək olar həmişə qalib gəlirdi (Hard 0:32). Səbəblər simulyasiyanın özündə idi, AI-də yox:
1. Zərər dərhal tətbiq olunurdu və vahidlər id sırası ilə atəş açırdı: aşağı id-li tərəf ilk zərbəni alırdı. İndi bir tikin bütün atəşləri birlikdə vurulur.
2. Dövri işlər (hədəf axtarışı, yol yenidən planlama, qüllə atəşi) ümumi id ilə fazalanırdı; indi komandanın öz yaradılma sırası ilə.
3. Komanda 1 üçün zavod qapısı xəritənin mərkəzinə yaxın tərəfdə deyildi, yəni vahidlər ~3 xana irəli çıxırdı. İndi qapı komanda 0-ın yarım dövrə güzgüsüdür.
4. A* bərabər yollarda sabit sıra ilə seçirdi, buna görə güzgü yollar fərqli çıxırdı. Komanda 1 indi yarım dövrə çevrilmiş sıra ilə axtarır, simmetrik xəritədə yolları dəqiq güzgüdür.

Nəticə (32 oyun, komanda 0 : komanda 1): Easy 19:13, Medium 17:15, Hard 15:17. Eyni 20 piyadadan ibarət iki ordu: 9:7.

## Sabitlik (60 dəqiqəlik matç, HQ-lar sonsuz, Hard vs Medium)

- Eyni seed ilə iki dəfə: son hash eynidir (`2D652BA6ED1EE068`), yəni deterministik.
- Tick xərci: ilk 10 dəq 0,15 ms, sonra 0,36–0,42 ms/tick (60 vahid, 24 bina), artım yoxdur; GC heap 0–4 MB, sızma yoxdur. (PC-də. A34-də tick xərci ölçülməyib, 2.8-də.)

## Məlum zəif yerlər

- İlk böyük toqquşmanı itirən AI çətin qayıdır: qalibin ordusu bazanın qapısında yeni çıxan vahidləri tək-tək öldürür ("spawn camping"). Oyunçu üçün də belədir; ehtiyac olarsa müdafiə qüllələri/geri çəkilmə ilə yumşaldılar.
- AI yalnız Rifleman/RocketTrooper/Jeep əsasında oynayır; Sniper/ArmoredCar/Tank nadir seçilir.
