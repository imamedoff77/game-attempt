# Mobil RTS (iş adı)

Android üçün 3D low-poly real vaxt strategiyası. Hədəf: 1v1 online (deterministik lockstep), kampaniya, Play Store. Ad hələ seçilməyib.

## Planlar

| Fayl | Nə üçündür |
|---|---|
| `GAME_PLAN.md` | Əsas oyunun fazaları (0–8), 1.0 minimumu |
| `FAZA_0_PLAN.md` | Cari fazanın ətraflı planı |
| `DEMO_PLAN.md` | Bitmiş demonun planı və qaydaları (D1–D5) |
| `PROJECT_NEEDS.md` | Lazım olan alətlər və quraşdırma |
| `ORIGINALITY.md` | Orijinallıq qaydaları |

## Qovluqlar

| Qovluq | Nə var |
|---|---|
| `sim-core/` | Oyunun beyni: saf C#, floatsız, deterministik. Unity-dən asılı deyil. Testləri də burada |
| `sim-core/src/SimCore/Resources/gamedata.json` | **Bütün balans rəqəmləri** (can, zərər, qiymət, vaxt, gəlir cədvəlləri) |
| `unity-client/` | Unity layihəsi: görüntü, toxunuş idarəsi, interfeys |
| `unity-client/Assets/Resources/Strings/en.json` | Ekrandakı bütün mətnlər (tərcümə cədvəli) |
| `art/blender/` | Blender skripti ilə modellərin yaradılması |
| `art/ART_BIBLE.md`, `art/reference/` | Art istiqaməti, üçbucaq büdcələri, sınaq nəticələri və şəkillər |
| `server/`, `backend/` | Boşdur. Faza 1 (oyun serveri) və Faza 4 (hesab, mağaza) üçün ayrılıb |

## Əsas qayda

Oyunun bütün qərarlarını `sim-core` verir. Unity yalnız göstərir və oyunçunun əmrlərini (`Command`) göndərir. Simulyasiyada `float`, `Random`, `DateTime` yoxdur ki, iki telefon eyni nəticəni versin.

## Testlər (sim-core)

```bash
cd sim-core && dotnet test
```

Testlər `gamedata.json` və `en.json` faylını da yoxlayır: səhv rəqəm və ya çatışmayan mətn açarı testi qırır.

## Rəqəmi dəyişmək (balans)

`gamedata.json` faylını aç, rəqəmi dəyiş, `dotnet test` işlət. Kod dəyişməsi lazım deyil. Yalnız tam ədəd yazılır. Sürət mində bir vahidlə yazılır (`2500` = 2.5). Səhv yazsan, proqram aydın xəta mesajı verir və oyun açılmır.

## APK qurmaq (Android)

```bash
cd unity-client
# sınaq build-i (developer paneli ilə): Builds/Android/GameAttempt-dev.apk
~/Unity/Hub/Editor/6000.3.27f2/Editor/Unity -batchmode -nographics -quit -projectPath . \
  -buildTarget Android -executeMethod GameBuild.BuildAndroidDev -logFile build.log
# buraxılış build-i (debug alətləri yoxdur): Builds/Android/GameAttempt.apk
~/Unity/Hub/Editor/6000.3.27f2/Editor/Unity -batchmode -nographics -quit -projectPath . \
  -buildTarget Android -executeMethod GameBuild.BuildAndroid -logFile build.log
adb install -r Builds/Android/GameAttempt-dev.apk
```

Developer paneli: yuxarı sol küncdəki resurs yazısına tez 3 dəfə toxun (yalnız sınaq build-lərində var).

## İmza açarı

Build `~/.config/game-keys/release.properties` faylından oxuyur (sətirlər: `keystore=`, `alias=`, `password=`). Fayl repoya düşmür. Fayl yoxdursa Unity-nin debug açarı ilə imzalanır. **Açar itərsə Play Store-dakı oyunu yeniləmək olmur**, ona görə `~/.config/game-keys/` qovluğunun surətini ən azı iki yerdə saxla. Açarı dəyişəndə telefonda köhnə tətbiqi əvvəlcə silmək lazımdır.
