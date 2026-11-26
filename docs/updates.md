# Peshtoq — Installer + auto-update dizayni

> Holati: **TAKLIF (kod yo'q)**. Vosita: [Velopack](https://github.com/velopack/velopack).
> Maqsad: bir martalik installer va ilovaning o'zidan ishlaydigan, **delta**
> (faqat o'zgargan qism) yuklaydigan avto-yangilanish.

## Nega Velopack

- Bitta vosita **installer + updater** ikkalasini beradi.
- **Delta update**: versiyalar orasidagi farqgina yuklanadi (bir necha MB), har
  safar to'liq ~179MB emas.
- Manba sifatida **GitHub Releases** (bizning `doppidevs/peshtoq`).
- Per-user o'rnatish (`%LocalAppData%\Peshtoq`) — admin huquqi shart emas, SAC/UAC
  muammosi kamroq.
- Rollback, versiya boshqaruvi, uninstall — hammasi tayyor.

Narxi: distribusiya "portable exe" dan "o'rnatiladigan ilova" ga o'zgaradi —
bu aslida asl maqsadga ("ilova Windows qismi bo'lsin") mos.

## Arxitektura

```
  GitHub Releases (channel)
   ├─ Peshtoq-<ver>-full.nupkg     (to'liq)
   ├─ Peshtoq-<ver>-delta.nupkg    (oldingi versiyadan farq)
   ├─ Setup.exe                    (bir martalik installer)
   └─ RELEASES / releases.<ch>.json (Velopack manifesti)
            │
            ▼
   UpdateManager (ilova ichida)
   • CheckForUpdatesAsync()  → yangi versiya bormi?
   • DownloadUpdatesAsync()  → delta'ni fonda yuklaydi
   • ApplyUpdatesAndRestart()→ keyingi ishga tushishda almashtiradi
```

O'rnatilgan joy: `%LocalAppData%\Peshtoq\current\Peshtoq.exe` (Velopack boshqaradi).
Startup (Windows bilan) shu yo'lga qayta yo'naltiriladi.

## WPF integratsiyasi (muhim nozik nuqta)

Velopack hook'lari (install/update/uninstall) ilovani maxsus argumentlar bilan
ishga tushiradi, shuning uchun `VelopackApp.Build().Run()` **eng birinchi**, WPF
ishga tushishidan oldin chaqirilishi shart. Buning uchun o'z `Program.Main`
kerak:

```csharp
// Program.cs
[STAThread]
static void Main(string[] args)
{
    VelopackApp.Build().Run();   // hook argumentlarini ushlaydi, kerak bo'lsa shu yerda chiqadi
    var app = new App();
    app.InitializeComponent();
    app.Run();
}
```
csproj da auto-generated Main o'chiriladi: App.xaml `Build Action = Page`, va
`<StartupObject>Peshtoq.Program</StartupObject>` beriladi (yoki ApplicationDefinition
o'rniga Page).

## Kanal (channel)

- Hozir preview bosqichida: **`prerelease` kanali** — `UpdateManager` ga
  `prerelease: true` beriladi, GitHub prerelease'lardan yangilanadi.
- Barqaror reliz bo'lganda: **`stable`** kanalga o'tamiz (prerelease = false).
- Ikki kanalni ajratish: Velopack `--channel` bilan (masalan `win-prerelease`,
  `win-stable`). Sozlamada "Beta yangilanishlar" belgisi bo'lishi mumkin (keyin).

## Versiya

- **Toza SemVer**: `0.3.0`, `0.3.1`, … (hozirgi `v0.2.3-link-fix` kabi teglar
  Velopack uchun yaroqsiz). csproj `<Version>` har relizda oshiriladi; `vpk` shuni
  ishlatadi.
- Taqqoslashni Velopack o'zi qiladi (nupkg versiyasi bo'yicha).

## UI oqimi

1. **Ishga tushganda** (fonda, jim): `CheckForUpdatesAsync`. Bor bo'lsa →
   `DownloadUpdatesAsync` fonda.
2. Yuklangach: tray toast + Sozlamalarda "Yangi versiya tayyor (vX) — qayta
   ishga tushirish" tugmasi. Majburlamaymiz.
3. Foydalanuvchi bosganda: `ApplyUpdatesAndRestart()`.
4. **Qo'lda**: Sozlamalar → "Yangilanishni tekshirish" tugmasi.

Yangilanish hech qachon ish paytini bo'lmaydi — faqat qayta ishga tushishda
qo'llanadi.

## Release jarayoni (yangi)

```powershell
# 1. publish (self-contained — .NET kerak emas)
dotnet publish src/Peshtoq -c Release -r win-x64 --self-contained true -o pub

# 2. Velopack paketi (installer + full + delta)
vpk pack --packId Peshtoq --packVersion 0.3.0 --packDir pub --mainExe Peshtoq.exe `
         --packTitle "Peshtoq" --channel win-prerelease

# 3. GitHub Releases'ga yuklash
vpk upload github --repoUrl https://github.com/doppidevs/peshtoq `
         --publish --releaseName "Peshtoq 0.3.0" --tag v0.3.0 --channel win-prerelease
```
`vpk` global dotnet tool: `dotnet tool install -g vpk`.

## Xavfsizlik / cheklovlar

- **Imzolanmagan**: Setup.exe birinchi ishga tushганда SmartScreen ogohlantiradi
  (More info → Run anyway). Kod imzolash (code-signing sertifikat) — kelajakda.
- Self-contained: birinchi o'rnatish bir marta katta (~179MB) yuklaydi; keyingi
  **update'lar delta** (kichik).
- Per-user o'rnatish: boshqa foydalanuvchiga ta'sir qilmaydi.

## Migratsiya (hozirgi portable foydalanuvchilar)

Ikki sinov mashinasi hozir portable `.exe` ishlatyapti. Bir marta `Setup.exe` ni
ishga tushirib, o'rnatilgan versiyaga o'tishadi; shundan keyin avto-update. Eski
portable exe'ni o'chirish mumkin (sozlamalar `%AppData%\Peshtoq` da qoladi).

## Bosqichlar

| Qism | Nima |
|---|---|
| **1** | `Program.Main` + Velopack NuGet, `VelopackApp.Build().Run()`; csproj StartupObject |
| **2** | `UpdateService`: check/download/apply, prerelease kanal |
| **3** | UI: tray toast + Sozlamalarda "Tekshirish" / "Qayta ishga tushirish" |
| **4** | `vpk pack` + `vpk upload` release jarayoni, SemVer 0.3.0 |

**Tavsiya:** 1→2→3→4 tartibida. Avval Velopack'ni ulab, bitta 0.3.0 relizini
`vpk` bilan chiqaramiz, keyin ikkinchi mashinada Setup.exe → auto-update'ni
real sinab ko'ramiz.
