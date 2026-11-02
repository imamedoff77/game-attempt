# Art of War 3 tipli mobil RTS — Lazım olanlar və kompüter statusu

Tarix: 2026-11-02 · Maşın: CachyOS Linux

**Status açarı:** ✅ Quraşdırılıb · ⚠️ Var, amma qeyd var · ❌ Yoxdur (qurulmalıdır) · ⏸️ Hələlik lazım deyil (sonrakı mərhələ)

---

## 0. Qəbul edilmiş qərarlar

| Mövzu | Qərar |
|---|---|
| Platforma | **Mobil: Android birinci. iOS sonraya saxlanıldı** (desktop yox) |
| İstifadəçi səviyyəsi | Unity təcrübəsi **yoxdur**. Mümkün qədər hər şeyi Claude edir, istifadəçi yalnız GUI/hesab/şifrə tələb edən addımları edir |
| Test cihazı | **Real Android cihaz var** (model hələ qeyd edilməyib) |
| Məqsəd | **Real buraxılış (Google Play)** |
| Engine | **Unity** (C#) |
| Multiplayer | **Başlanğıcdan 1v1** (deterministic lockstep) |
| Qrafika | **Low-poly 3D** |

### Bu seçimlərin birləşməsinin riskləri (açıq yazıram)

1. **Başlanğıcdan multiplayer + real buraxılış** ən riskli kombinasiyadır. Lockstep-də simulyasiya bütün cihazlarda **bit-bit eyni** nəticə verməlidir. Bu o deməkdir ki, `float` əvəzinə fixed-point riyaziyyat, deterministic pathfinding və Unity physics-dən imtina lazımdır. Bunu sonradan əlavə etmək demək olar ki, mümkün deyil, ona görə **arxitektura birinci gündən buna uyğun qurulmalıdır**.
2. **Low-poly 3D + Intel Iris Xe + 15 GB RAM:** Editor işləyəcək, amma build və test yavaş olacaq. Vahid sayını və poligon büdcəsini ciddi məhdudlaşdırmaq lazımdır.
3. **iOS:** Linux-da build etmək mümkün deyil, **Mac + Xcode + Apple Developer hesabı** lazımdır. Plan: kod platformadan asılı olmayaraq yazılır, Android birinci buraxılır, iOS sonra (Mac tapıldıqda və ya bulud Mac build xidməti ilə).
4. **Real buraxılış** tək developer üçün uzun işdir: balans, UI, səs, monetizasiya, anti-cheat, serverlər, dəstək.

### Təklif olunan mərhələ ardıcıllığı

| # | Mərhələ | Nəticə |
|---|---|---|
| 0 | Mühit qurulumu | Unity, Android SDK, Blender, Docker hazır |
| 1 | **Deterministic simulyasiya nüvəsi** (UI-dan ayrı) | Fixed-point, komanda sırası, tick sistemi, replay yazma/oxuma. Testlər eyni nəticəni verir |
| 2 | Single-player vertical slice | Xəritə, HQ, 2–3 bina, 4–5 vahid, seçmə/hərəkət/hücum, kamera, touch idarəetmə |
| 3 | Pathfinding və döyüş | Flow field/A*, dəstə hərəkəti, atəş radiusu, sadə AI |
| 4 | 1v1 online | Lockstep relay server, lobby, matchmaking, reconnect, desync aşkarlama |
| 5 | Meta-hissə | Hesab, profil, leaderboard, klan (Laravel API) |
| 6 | Kampaniya, balans, UI/səs cilası | |
| 7 | Closed testing → Play Store | |

---

## 1. Kompüterin hardware-i

| Parametr | Dəyər | Qiymətləndirmə |
|---|---|---|
| CPU | Intel i5-1135G7 (4 nüvə / 8 thread) | ⚠️ Prototip üçün kifayətdir, Unity/Android build-ləri yavaş olacaq |
| RAM | 15 GB (≈9 GB boş) | ⚠️ Unity Editor + Android Studio + Docker eyni anda ağır olar |
| Disk | 154 GB boş (`/home`) | ✅ Unity (~10–15 GB) + Android SDK (~10 GB) üçün yetərlidir |
| GPU | Intel Iris Xe (Vulkan 1.4, OpenGL 4.6) | ⚠️ Low-poly üçün işləyər, amma Editor-da zəif performans gözlənilir |
| Drayverlər | mesa, vulkan-intel | ✅ |

---

## 2. Engine və client

| Lazım olan | Status | Qeyd |
|---|---|---|
| **Unity Hub + Unity Editor (LTS)** | ❌ Yoxdur | Linux-da AUR-dan (`unityhub`) qurulur. Editor Hub vasitəsilə yüklənir. Android Build Support modulu da seçilməlidir |
| **.NET SDK** | ✅ 10.0.114 (2026-11-02 quraşdırıldı) | Simulyasiya nüvəsi və testlər üçün işləyir |
| **Kod redaktoru** | ✅ VS Code 1.138, PhpStorm | Unity C# üçün VS Code + C# Dev Kit və ya Rider |
| **Git** | ✅ 2.56.0 | |
| **Git LFS** | ✅ 3.8.1, repoda aktivləşdirilib | `.gitattributes` qaydaları yazılıb |
| **Unity-nin .gitignore** | ✅ Yaradılıb | Repo `main` branch-i ilə başladılıb, hələ commit yoxdur |

---

## 3. Mobil build (Android / iOS)

| Lazım olan | Status | Qeyd |
|---|---|---|
| **Android SDK + NDK + OpenJDK** | ❌ Yoxdur | Unity Hub modul kimi yükləyə bilər, bu daha asandır |
| **adb** | ✅ 1.0.41 (`android-tools`) | Telefonda test üçün. Telefonda USB debugging açılmalıdır |
| **Android cihaz** (real) | ✅ Var | Developer options və USB debugging açılmalıdır |
| **Google Play Developer hesabı** | ❓ Yoxlanmayıb | Birdəfəlik qeydiyyat haqqı var. Buraxılış üçün mütləqdir |
| **Mac + Xcode** (iOS) | ⏸️ Təxirə salındı | Linux-da iOS build mümkün deyil |
| **Apple Developer hesabı** | ⏸️ Təxirə salındı | İllik ödənişlidir, iOS üçün lazım olacaq |

---

## 4. Real-time server (1v1 multiplayer)

| Lazım olan | Status | Qeyd |
|---|---|---|
| **Java JDK** | ✅ OpenJDK 25 (+ jdk21) | Orijinal oyun serveri Java-dır. LTS üçün 21 daha təhlükəsizdir |
| **Maven** | ✅ 3.10.0 | |
| **Gradle** | ❌ Yoxdur | İstəyə bağlı |
| **Go** | ✅ 1.27.3 | Lockstep relay üçün yüngül alternativ |
| **Node.js / pnpm / bun** | ✅ 24.22.2 / 12.16.1 / 1.4.4 | Alternativ relay və ya alətlər |
| **Redis (Valkey 9.1)** | ✅ Quraşdırılıb | Matchmaking növbəsi, sessiyalar |
| **Kafka, Tarantool, OpenSearch** | ⏸️ Lazım deyil | Orijinal oyun istifadə edir, MVP üçün artıqdır |

---

## 5. Meta-backend (hesab, klan, leaderboard)

| Lazım olan | Status | Qeyd |
|---|---|---|
| **PHP + Composer** | ✅ 8.5.12 / 2.11.1 | |
| **Laravel starter kit** | ✅ `~/PhpstormProjects/Laravel-Nuxt-Starter-Kit` | Ayrı repo kimi kopyalanacaq |
| **PostgreSQL client** | ✅ psql 18.6 | Server işləməsi yoxlanmayıb |
| **MySQL/MariaDB, SQLite** | ✅ | |
| **Docker + Compose** | ✅ 29.9.2 / 5.6.1, servis işləyir və boot-da avtomatik başlayır | Reboot-dan sonra (kernel 7.3.1-1) problem aradan qalxdı. `docker` qrupundasınız, `sudo` lazım deyil |

---

## 6. Qrafika və səs (low-poly 3D)

| Lazım olan | Status | Qeyd |
|---|---|---|
| **Blender** | ✅ 5.2.3 LTS | Low-poly model və animasiya üçün |
| **Krita və ya GIMP** | ❌ Yoxdur | Tekstur, UI, ikonlar |
| **Inkscape** | ❌ Yoxdur | Vektor ikonlar |
| **Audacity və ya LMMS** | ❌ Yoxlanmayıb | Səs effektləri |
| **FFmpeg** | ✅ 9.0.2 | |
| **Hazır asset** (Kenney.nl, Quaternius, OpenGameArt) | ⏸️ Mərhələ 2-də | İlkin mərhələdə pulsuz low-poly paketlər. Lisenziyaları real buraxılışdan əvvəl yoxlamaq lazımdır |

---

## 7. Real buraxılış üçün əlavə şeylər

| Maddə | Status |
|---|---|
| Oyun adı və loqo (Art of War 3-ün adı/brendi **istifadə olunmamalıdır**, öz adınız lazımdır) | ❌ Hələ yoxdur |
| Orijinal asset, fraksiya, vahid dizaynı (kopyalama yoxdur, yalnız janr ilhamı) | ❌ |
| Privacy policy və Terms səhifəsi | ❌ |
| Crash reporting və analitika (Unity Analytics, Firebase) | ⏸️ |
| Monetizasiya modeli (reklam, IAP) | ⏸️ Qərar verilməyib |
| Anti-cheat (server tərəfində komanda validasiyası) | ⏸️ Mərhələ 4 ilə birgə |

---

## 8. Köməkçi alətlər

| Alət | Status |
|---|---|
| curl | ✅ 8.23.0 |
| jq | ✅ 1.8.2 |
| Python 3 | ✅ 3.14.8 |
| pip | ❌ Yoxdur |
| Rust/Cargo, Yarn | ❌ Yoxdur, lazım deyil |

---

## 9. Hələ açıq qalan suallar

1. **Real Android cihazınız** varmı? Hansı model?
2. **Mac-a çıxışınız** varmı (iOS üçün, indi və ya gələcəkdə)?
3. **Unity ilə təcrübəniz** varmı?
4. **Zaman çərçivəsi:** gündə/həftədə nə qədər vaxt ayıra bilərsiniz?
5. **Oyun adı və hekayə/setting:** modern müharibə, sci-fi, fantasy?

---

## 10. Mərhələ 0–1 irəliləyişi (2026-11-02)

- ✅ .NET SDK, Git LFS, adb, Blender quraşdırıldı
- ✅ Git repo (`main`), `.gitignore`, `.gitattributes`
- ✅ `sim-core/`: `Fix64` (Q31.32), `Vec2`, `DeterministicRandom` (xorshift64*), `StateHasher` (FNV-1a), 24 unit test keçir
- ✅ Docker: reboot-dan sonra işləyir
- ⏳ Unity Hub (AUR `unityhub 3.21.0`): PKGBUILD yoxlanıldı, təmizdir. İstifadəçi `paru -S unityhub` işlətməlidir
- ⏳ Unity Editor LTS: Hub-dan, Unity hesabı ilə giriş tələb olunur
- ⏳ Android cihaz: USB debugging

### Yeniləmə: ilk Android build (2026-11-02)

- ✅ Unity Hub 3.21.0, Unity 6000.3.27f2 (LTS), Android Build Support (OpenJDK 17, SDK 34–37, NDK) quraşdırıldı
- ✅ `unity-client/` yaradıldı, `sim-core` `com.game.simcore` paketi kimi bağlandı
- ✅ Komanda xəttindən build: `GameBuild.BuildAndroid` (IL2CPP, ARM64, min API 24, landscape). Build ~4 dəq, APK ~15 MB
- ✅ Samsung Galaxy A34 (SM-A346E, Android 16) üzərində işləyir: 60 FPS, SimCore determinism testi cihazda OK (ARM64 + IL2CPP, .NET x64 ilə eyni nəticə)
- ⚠️ Paket adı müvəqqətidir: `com.imamedoff.gameattempt`. Play Store-a yükləmədən əvvəl dəyişdirilməlidir, sonradan dəyişmək olmur
- ⚠️ Unity lisenziya xəbərdarlığı (`Access token is unavailable`) headless build-i dayandırmadı

## 11. Növbəti addımlar

1. Reboot (Docker + yeni kernel üçün).
2. `paru -S unityhub`, Hub-da giriş, Editor LTS + Android Build Support modulları.
3. Sim-core: tick sistemi, komanda növbəsi, entity/vahid, hərəkət, state hash, replay.
4. Unity layihəsini yaradıb sim-core-u ona bağlamaq (render yalnız simulyasiyanı oxuyur).
