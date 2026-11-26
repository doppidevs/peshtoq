# Peshtoq Link — "Mouse Without Borders" dizayn rejasi

> Holati: **TAKLIF (hali kod yozilmagan)**. Bu hujjat koddan oldin keladi.
> Maqsad: bir nechta Windows mashinasini bitta sichqoncha/klaviatura bilan
> boshqarish, clipboard va fayllarni ular orasida ulashish — Microsoft
> "Mouse Without Borders" / Barrier / Deskflow kabi, lekin Peshtoq ichida.

## 1. Qamrov (nima qiladi, nima qilmaydi)

**Qiladi (yakuniy maqsad):**
- LAN'dagi 2–4 mashinani juftlash (pairing)
- Clipboard (matn, keyin rasm/fayl) sinxronizatsiyasi
- Kursorni ekran chetidan qo'shni mashinaga "topshirish" (edge handoff)
- Klaviatura inputini faol mashinaga uzatish
- Fayllarni drag-and-drop bilan ko'chirish

**Qilmaydi (loyihadan tashqari):**
- Internet orqali (NAT traversal, relay serverlar) — faqat bir xil LAN/subnet
- Ekranni ko'rsatish/translatsiya (bu RDP/VNC ishi, KVM emas)
- Windows'dan boshqa OS (macOS/Linux) — hozircha yo'q

## 2. Arxitektura

Har bir mashinada bitta Peshtoq ishlaydi. Biri **Server (host)**, qolganlari
**Client**. Rollarni foydalanuvchi sozlamadan tanlaydi (yoki birinchi bo'lgan
mashina host bo'ladi).

```
   [Mashina A — HOST]                         [Mashina B — CLIENT]
   ┌──────────────────────┐                   ┌──────────────────────┐
   │ LinkService          │   TLS (TCP 8787)  │ LinkService          │
   │  • discovery (mDNS)  │◄─────────────────►│  • discovery         │
   │  • pairing           │    shifrlangan    │  • pairing           │
   │  • transport         │   input/clip oqimi│  • transport         │
   ├──────────────────────┤                   ├──────────────────────┤
   │ InputCapture         │                   │ InputInjector        │
   │  WH_MOUSE_LL /        │   harakat →       │  SendInput           │
   │  WH_KEYBOARD_LL hook  │   paket           │  (qabul qilib        │
   │  + edge detection     │                   │   qayta chiqaradi)   │
   ├──────────────────────┤                   ├──────────────────────┤
   │ ClipboardService      │◄── sync ──►       │ ClipboardService     │
   │  (allaqachon bor)     │                   │  (allaqachon bor)    │
   └──────────────────────┘                   └──────────────────────┘
```

### Yangi komponentlar
| Fayl (taxminiy) | Vazifasi |
|---|---|
| `Services/Link/LinkService.cs` | Ulanish hayotiy sikli: discovery, pairing, transport, qayta ulanish |
| `Services/Link/Discovery.cs` | mDNS/UDP broadcast bilan LAN'dagi boshqa Peshtoq'larni topish |
| `Services/Link/LinkTransport.cs` | TLS stream ustida paket yuborish/olish (uzunlik-prefiksli freymlar) |
| `Services/Link/Protocol.cs` | Paket turlari: `Hello`, `PairRequest`, `MouseMove`, `MouseButton`, `Wheel`, `Key`, `ClipText`, `ClipBlob`, `Heartbeat` |
| `Native/InputCapture.cs` | `SetWindowsHookEx(WH_MOUSE_LL/WH_KEYBOARD_LL)` — global ushlash |
| `Native/InputInjector.cs` | `SendInput` — qabul qilingan harakatni qayta chiqarish |
| `Native/EdgeMonitor.cs` | Kursor ekran chetiga yetganini aniqlab, handoff'ni boshlash |
| `Views/LinkTab` (yoki island'da yangi tab) | Qurilmalar ro'yxati, pairing kodi, ulanish holati |

### Mavjud komponentlarga ta'sir
- `ClipboardService` — tarmoqqa uzatish uchun hodisa (event) qo'shiladi; hozir
  faqat xotirada. Tarmoq clipboard'i opt-in bo'ladi.
- `Settings` — yangi bo'lim: `LinkEnabled`, `LinkRole`, `PairedDevices[]`,
  `LinkClipboard`, `LinkInput` bayroqlari.
- Island'ga 5-tab yoki Tizim tab ichida "Link" bo'limi.

## 3. Protokol (paket oqimi)

Transport: **TCP + TLS 1.3**, uzunlik-prefiksli binar freymlar. UDP emas —
input tartibi va ishonchliligi muhim (tushib qolgan klavish = yomon).
Kechikishni kamaytirish uchun `TCP_NODELAY` (Nagle o'chiq).

```
Freym = [uint16 length][byte type][payload]

MouseMove   : int16 dx, int16 dy          (nisbiy harakat)
MouseButton : byte button, byte down
Wheel       : int16 delta, byte horizontal
Key         : uint16 vk, byte down, byte extended
ClipText    : uint32 len, utf8 bytes
ClipBlob    : byte format, uint32 len, bytes   (rasm/fayl — keyinroq)
Heartbeat   : (bo'sh)                       (har 1 s; 3 o'tkazib yuborilsa — uzildi)
```

## 4. Xavfsizlik — ENG MUHIM QISM

Low-level input hook + tarmoq = noto'g'ri yozilsa **masofaviy boshqaruv teshigi**.
Shuning uchun quyidagilar majburiy, opsiya emas:

1. **Faqat LAN.** Server faqat shaxsiy diapazonlarda (10/8, 172.16/12,
   192.168/16, link-local) tinglaydi. Internet interfeysida umuman ochilmaydi.
2. **Majburiy juftlash (pairing).** Birinchi ulanishda host ekranda 6 raqamli
   kod ko'rsatadi; client o'sha kodni kiritadi. Kodsiz hech kim ulanolmaydi.
3. **TLS + sertifikat pinning.** Pairing paytida ikkala tomon bir-birining
   ochiq kalitini saqlaydi (TOFU — trust on first use). Keyingi ulanishlarda
   kalit mos kelmasa — rad etiladi (MITM himoyasi).
4. **Oq ro'yxat (allow-list).** Faqat avval juftlangan qurilmalar ulana oladi.
   Juftlanmagan ulanish so'rovi — host'da ko'rinadigan tasdiq bilan.
5. **Ko'rinadigan holat.** Qachonki boshqa mashina ushbu mashinani boshqarayotgan
   bo'lsa, header'da aniq indikator (masalan, "⇄ B boshqaryapti"). Jim rejim yo'q.
6. **Kill-switch.** Istalgan vaqtda `Ctrl+Alt+Break` (yoki tray) — barcha
   ulanishni darhol uzadi va hooklarni olib tashlaydi.
7. **Zudlik bilan hook'ni bo'shatish.** Ilova qulasa yoki yopilsa, global hook
   albatta `UnhookWindowsHookEx` bilan tozalanadi (osilib qolgan hook — xavf).

**Tahdid modeli:** bir LAN'dagi yovuz qo'shni. Pairing kodi + TLS pinning +
allow-list buni to'xtatadi. Internet hujumi qamrovdan tashqari (server
internetda tinglamaydi).

## 5. Texnik xavflar / ochiq savollar

- **UAC / himoyalangan ish stoli.** Global hook UAC oynasi yoki login ekraniga
  input yubora olmaydi (Windows bloklaydi). Bu kutilgan cheklov — hujjatlashtiramiz.
- **Hook ishonchliligi.** `WH_*_LL` hook'i sekin ishlansa, Windows uni o'chiradi
  (`LowLevelHooksTimeout`). Shuning uchun hook callback'i **hech narsa qilmaydi**,
  faqat paketni navbatga qo'yadi; tarmoq boshqa thread'da.
- **DPI / ko'p monitor.** Mashinalarda turli DPI va ekran joylashuvi. Edge
  handoff koordinatalarini normalizatsiya qilish kerak.
- **Administrator huquqi.** Elevated oynaga input yuborish uchun Peshtoq ham
  elevated bo'lishi kerak bo'lishi mumkin. Buni sinab ko'rish shart.
- **Antivirus / SmartScreen.** Global keyboard hook = keylogger signaturasi.
  Imzolanmagan build'ni AV bayroq qilishi mumkin (bizda SAC muammosi bo'lgan).

## 6. Bosqichli yo'l xaritasi

Har bosqich o'zicha tugallangan va foydali — bir zarbada hammasi emas.

| Bosqich | Nima | Og'irligi | Mustaqil foyda |
|---|---|---|---|
| **0. Transport asos** | Discovery + TLS pairing + heartbeat, hali input yo'q | O'rta | Qurilmalar bir-birini ko'radi, ishonchli ulanadi |
| **1. Clipboard sync** | `ClipText` ikki yo'nalishda | Kichik | Darhol foydali; MWB'ning eng ko'p ishlatiladigan qismi |
| **2. Kursor handoff** | `InputCapture` + `InputInjector` + edge | Katta | Haqiqiy KVM: sichqoncha mashinalar orasida o'tadi |
| **3. Klaviatura** | `Key` paketlari, fokus boshqaruvi | O'rta | To'liq boshqaruv |
| **4. Fayl ko'chirish** | `ClipBlob`, drag-drop | O'rta | Fayllarni sudrab o'tkazish |

**Tavsiya:** 0 → 1 dan boshlaymiz. Transport va xavfsizlik to'g'ri bo'lsa,
qolgan bosqichlar shu poydevor ustiga quriladi.

## 7. Nega qayta yozamiz (tayyor vosita bor-ku)?

Microsoft MWB, Barrier, Deskflow bepul va ishlaydi. Peshtoq'da qilishning yagona
sababi: hammasini **bitta "Windows'ning bir qismi" bo'lgan ilovada** jamlash —
header, island, clipboard va link bitta joyda, bitta UI, bitta sozlama. Agar bu
jamlash qiymat bermasa, tayyor vositadan foydalanish to'g'riroq.
