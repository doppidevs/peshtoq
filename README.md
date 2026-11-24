# Peshtoq

Windows uchun doimiy yuqori panel (AppBar) va tepadan tushadigan multi-tasking panel.

- **Header (AppBar, 34 px):** ekranning tepasida doim turadi; soat, batareya, ovoz, Wi-Fi, bildirishnomalar, hozir chalinayotgan media.
- **Island panel:** sichqoncha ekranning tepa markaziga tekkanda pill kattalashib, pastga tushadi. Tablar: Media, Eslatmalar, Clipboard, Tizim. G'ildirak bilan tab almashtiriladi.

## Texnologiya

- C# / WPF, .NET 8
- Win32: `SHAppBarMessage` (AppBar), `GetCursorPos` (trigger)
- WinRT: `GlobalSystemMediaTransportControls` (media), `Battery`, `UserNotificationListener` (v2)

## Ishga tushirish

```powershell
winget install Microsoft.DotNet.SDK.8
dotnet run --project src/Peshtoq
```

## Holati

Boshlang'ich skelet. Dizayn va animatsiya parametrlari: [docs/design.md](docs/design.md).
