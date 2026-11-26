# Peshtoq

Windows uchun doimiy yuqori panel (AppBar) va tepadan tushadigan multi-tasking panel.

## v0.1 imkoniyatlari

**Header (AppBar, 34 px)** ekranning tepasida doim turadi. Windows bu joyni unga ajratadi, shuning uchun maximize qilingan oynalar header ostidan boshlanadi.

- Start menyusi tugmasi, hozir faol ilova va oyna nomi
- Bildirishnomalar markazi tugmasi
- Wi-Fi / Ethernet holati (bosilsa tarmoqlar ro'yxati ochiladi)
- Ovoz: bosilsa o'chadi/yoqiladi, g'ildirak bilan balandligi o'zgaradi
- Batareya (noutbukda) va soat
- To'liq ekranli ilova (o'yin, video) ochilganda header va island yashirinadi

**Island panel.** Sichqoncha ekranning tepa markaziga tekkanda (±150 px, 150 ms kutib) pill kattalashib, panelga aylanadi. Sichqoncha chiqib ketganda yoki `Esc` bosilganda yopiladi.

| Tab | Nima qiladi |
|---|---|
| Media | Tizimdagi istalgan pleyer (Spotify, YouTube, Yandex Music va h.k.): muqova, nom, progress, play/pause, oldingi/keyingi, progress bosilsa o'sha joyga o'tadi, bir nechta manba orasida almashish |
| Eslatmalar | Qo'shish (Enter), belgilash, o'chirish. `%AppData%\Peshtoq\notes.json` da saqlanadi |
| Clipboard | Oxirgi 20 ta nusxalangan matn; bosilsa qayta nusxalanadi. Faqat xotirada turadi, diskka yozilmaydi |
| Tizim | CPU, RAM, batareya, kompyuter qancha vaqtdan beri yoniqligi |

- G'ildirak bilan tablar almashadi
- Panel o'lchami har bir tabning kontentiga moslashadi (balandlik 170–420 px)
- Tablar tepada yoki chapda
- Faol tab ikonkasi accent rangда

**Sozlamalar oynasi** (tray menyusi yoki ikonaga ikki marta bosish): tablar joylashuvi, trigger kengligi, ochilish tezligi, accent rang, qaysi tablar ko'rinishi, Windows bilan ishga tushirish. O'zgarishlar jonli qo'llanadi.

**Tray menyusi:** Sozlamalar, chiqish.

## Texnologiya

- C# / WPF, .NET 8
- Win32: `SHAppBarMessage` (AppBar), `GetCursorPos` (trigger), `AddClipboardFormatListener`
- WinRT: `GlobalSystemMediaTransportControls` (media), `NetworkInformation`
- [NAudio](https://github.com/naudio/NAudio) (ovoz balandligi)

## Ishga tushirish

```powershell
winget install Microsoft.DotNet.SDK.8
dotnet run --project src/Peshtoq
```

Bitta exe qilib chiqarish:

```powershell
dotnet publish src/Peshtoq -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

## Link — qurilmalararo (eksperimental)

Bir LANdagi ikki Peshtoq bir-birini topadi va **juftlanadi**: o'zaro TLS, va
ikki ekranda bir xil 6 xonali kod (SAS) solishtiriladi — mos bo'lsa tasdiqlanadi.
Sozlamalar → *Link* dan yoqiladi (standart holatda o'chiq). Hozircha faqat
ulanish/juftlash tayyor; clipboard sinxronizatsiyasi va sichqoncha/klaviatura
uzatish keyingi bosqichlarda. Dizayn: [docs/mouse-without-borders.md](docs/mouse-without-borders.md),
[docs/link-pairing.md](docs/link-pairing.md).

## Rejada

- **Auto-update** (keyinchalik): hozir har versiya GitHub Releases'dan qo'lda
  yuklanadi. Tavsiya — [Velopack](https://github.com/velopack/velopack): delta
  update (bir necha MB, to'liq 179MB emas) + bir martalik `Setup.exe` installer,
  manba sifatida GitHub Releases. Muqobil: oddiy ichki updater (single-exe'ni
  saqlaydi, lekin har safar to'liq yuklaydi).
- Link 2b: **edge-handoff** (kursor ekran chetiga yetganda avtomatik o'tish) →
  fayl ko'chirish
- Windows bildirishnomalarini header'da ko'rsatish (`UserNotificationListener`)
- Bir nechta monitor
- Island'ga haqiqiy acrylic (oynani shaklga moslash)

Dizayn va animatsiya parametrlari: [docs/design.md](docs/design.md).
