# Peshtoq Link — Input relay (2-bosqich) dizayni

> Holati: **TAKLIF (kod yo'q)**. Sichqoncha + klaviatura uzatishni koddan oldin
> mixlab qo'yadi. [mouse-without-borders.md](mouse-without-borders.md) 2-bosqich.
> 0–1 bosqich (transport, pairing, clipboard) tayyor va ishlaydi.

## Maqsad va atamalar

Bir mashinaning jismoniy sichqoncha/klaviaturasi bilan **ikkinchi** mashinani
boshqarish.

- **Controller** — jismoniy sichqoncha/klaviatura shu mashinada. Inputni ushlaydi,
  mahalliy bajarilishini **bloklaydi** va peer'ga uzatadi.
- **Target** — uzatilgan inputni `SendInput` bilan qayta chiqaradi.

Rol dinamik: boshqaruv qaysi tomonda bo'lsa, o'sha Controller.

## Komponentlar

| Fayl (taxminiy) | Vazifasi |
|---|---|
| `Native/InputCapture.cs` | `SetWindowsHookEx(WH_MOUSE_LL / WH_KEYBOARD_LL)` — global ushlash + bloklash |
| `Native/InputInjector.cs` | `SendInput` — sichqoncha/klavishni qayta chiqarish |
| `Services/Link/InputController.cs` | Faollashtirish holati (kim kimni boshqaradi), kill-switch, avto-chiqish |
| `Native/EdgeMonitor.cs` | (2b) kursor ekran chetiga yetishini aniqlash |

Protokolga: `MouseMove(dx,dy)`, `MouseButton(btn,down)`, `Wheel(delta,horizontal)`,
`Key(vk,scan,down,extended)`, `ControlAcquire`, `ControlRelease`.

## Ushlash (capture) mexanikasi

- Low-level hook callback **tez** bo'lishi shart: Windows sekin callback'ni
  o'chiradi (`LowLevelHooksTimeout`, ~300 ms). Shuning uchun callback faqat
  hodisani **navbatga qo'yadi**; tarmoqqa yuborish boshqa thread'da.
- Boshqarayotganda (Controller, control=peer) hook mahalliy yetkazishni
  **bloklaydi**: callback `1` qaytaradi (`CallNextHookEx` chaqirilmaydi) —
  kursor shu mashinada qimirlamaydi, klavish shu mashinaga tushmaydi.
- Sichqoncha harakati **nisbiy (dx, dy)** sifatida uzatiladi — DPI/ekran
  o'lchami farqidan qat'i nazar ishlaydi (absolyut koordinata 2c da).
- Klaviatura: `vk` + **scancode** + `extended` bayrog'i uzatiladi; Target
  scancode bilan chiqaradi (til/layout mustaqilligi uchun).

## Chiqarish (injection) mexanikasi

- `SendInput` bilan:
  - Move: `MOUSEEVENTF_MOVE` (nisbiy dx,dy).
  - Tugma: `MOUSEEVENTF_LEFTDOWN/UP`, `RIGHTDOWN/UP`, `MIDDLEDOWN/UP`, `XDOWN/UP`.
  - G'ildirak: `MOUSEEVENTF_WHEEL` / `HWHEEL`, `mouseData=delta`.
  - Klavish: `KEYBDINPUT` `KEYEVENTF_SCANCODE` (+`EXTENDEDKEY`, +`KEYUP`).
- Target'da **modifikator holati** controller'ga ergashadi: har bir key up/down
  aynan uzatiladi, shuning uchun Ctrl/Shift/Alt holati tabiiy saqlanadi.

## Faollashtirish modeli

Ikki bosqichda:

**2a — Hotkey toggle (avval).** Belgilangan chord (masalan `Ctrl+Ctrl` yoki
`Scroll Lock` ×2) boshqaruvni peer'ga **beradi** / **qaytaradi**. Geometriya/DPI
muammosi yo'q — darhol ishlaydi va xavfsiz sinaladi. Boshqaruv yoqilganda
header'da aniq indikator.

**2b — Edge-handoff (keyin).** `EdgeMonitor` kursor ekran chetiga (masalan o'ng
chet) yetishini aniqlaydi → avtomatik peer'ga o'tadi; peer chap chetga qaytsa —
qaytadi. Buning uchun:
- Mashinalar joylashuvi (A chapda, B o'ngda) — foydalanuvchi belgilaydi.
- Chetga chiqish nuqtasi ekran balandligining **foizi** sifatida uzatiladi
  (turli balandlik/DPI uchun normalizatsiya).
- "Yopishqoq chet" (sticky edge) — tasodifiy o'tishni oldini olish uchun qisqa
  kechikish.

## Xavfsizlik va xavfsiz chiqish — MAJBURIY

Input bloklash + tarmoq = xato bo'lsa sichqoncha/klaviatura qotishi mumkin.
Quyidagilar opsiya emas:

1. **Kill-switch.** Doimiy chord (masalan `Ctrl+Alt+Pause/Break`) boshqaruvni
   **darhol** uzadi va hooklarni olib tashlaydi. U suppression'dan **oldin**
   tekshiriladi va **hech qachon bloklanmaydi**.
2. **Avto-chiqish.** Peer uzilsa, heartbeat yo'qolsa, ilova fokusni yo'qotsa yoki
   qulasa → hooklar olib tashlanadi, boshqaruv bekor qilinadi. Hech qachon
   "osilib qolgan" bloklash qolmaydi.
3. **Hook sog'lig'i.** Callback hech narsa qilmaydi, faqat navbatga qo'yadi.
4. **Majburiy pin.** Faqat juftlangan (pinlangan) peer input yubora/qabul qila
   oladi. Notanish paketdagi `MouseMove`/`Key` e'tiborsiz qoldiriladi.
5. **Ko'rinadigan holat.** Boshqarilayotganda header'da "⇄ B boshqaryapti",
   boshqarayotganda "→ B ga" indikator. Jim rejim yo'q.
6. **Qulaganda tozalash.** `UnhookWindowsHookEx` har qanday chiqishda chaqiriladi
   (finally/AppDomain exit).

## Texnik cheklovlar (hujjatlashtiramiz, yashirmaymiz)

- **UAC / xavfsiz ish stoli / login ekrani** — `SendInput` u yerga ta'sir
  qilmaydi (Windows bloklaydi). Kutilgan cheklov.
- **Elevated (administrator) oynalar** — UIPI tufayli oddiy jarayondan input
  qabul qilmaydi. Target'ni elevated ishlatish kerak bo'lishi mumkin.
- **Antivirus** — global keyboard hook keylogger signaturasiga o'xshaydi;
  imzolanmagan build bayroq olishi mumkin.
- **N-key rollover / o'yin anti-cheat** — ba'zi o'yinlar injected inputni rad
  etadi (kutilgan).

## Protokol qo'shimchalari

```
MouseMove     : int16 dx, int16 dy            (nisbiy)
MouseButton   : byte button, byte down         (0=L 1=R 2=M 3=X1 4=X2)
Wheel         : int16 delta, byte horizontal
Key           : uint16 vk, uint16 scan, byte flags  (bit0=down, bit1=extended)
ControlAcquire: byte reason                     (hotkey / edge)
ControlRelease: byte reason
```
Hammasi TLS kanali ichida, pinlangan peer bilan.

## Yo'l xaritasi (2-bosqich ichida)

| Qism | Nima | Sinov |
|---|---|---|
| **2a** | Hotkey toggle + capture(suppress) + inject (nisbiy mouse + klaviatura) | Loopback: inject paketlari Target'da `SendInput` bilan chiqishi; qo'lda ikki mashina |
| **2b** | Edge-handoff + koordinata normalizatsiyasi + sticky edge | Qo'lda: chetga borib o'tish |
| **2c** | Absolyut kursor, ko'p monitor, joylashuv sozlamasi | Qo'lda |

**Tavsiya:** 2a dan boshlaymiz — hotkey bilan, geometriyasiz. Ishonchli
kill-switch va avto-chiqish bilan. Ishlagach, 2b (edge) qo'shiladi.

## Ochiq qaror (2a dan oldin)

- Kill-switch chordi: `Ctrl+Alt+Break` (tavsiya) yoki boshqa?
- Toggle chordi: nima bo'lsin (`Scroll Lock` ×2 / `Ctrl` ×2 / sozlanadigan)?

Bu ikkisi 2a kodidan oldin tasdiqlanadi.
