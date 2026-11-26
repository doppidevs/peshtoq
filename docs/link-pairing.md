# Peshtoq Link — Pairing (juftlash) dizayni

> Holati: **TAKLIF (kod yo'q)**. [mouse-without-borders.md](mouse-without-borders.md)
> ning 1-ochiq savolini (pairing/kalit almashinuvi) mixlab qo'yadi.
> Qolgan savollar (edge-handoff geometriyasi, fokus) 2-bosqichga qoldiriladi.

## Maqsad

Ikki mashina o'rtasida **o'zaro autentifikatsiyalangan, shifrlangan** kanal
o'rnatish, bir LAN'dagi MITM (o'rtadagi odam) hujumiga qarshi himoyalangan holda,
foydalanuvchi ko'z bilan solishtiradigan **qisqa kod** orqali.

Muhim g'oya: **kod maxfiy emas.** U parol emas — u ikki mashinaning ochiq
kalitlaridan hisoblangan **barmoq izi** (Signal "safety number" yoki Bluetooth
"numeric comparison" kabi). Foydalanuvchi ikki ekrandagi kod **bir xilligini**
tasdiqlaydi. MITM bo'lsa kodlar mos kelmaydi — shu bilan fosh bo'ladi.

## Nega SAS (Short Authentication String), PAKE emas?

| Yondashuv | Plus | Minus |
|---|---|---|
| **SAS / numeric comparison** (tanlandi) | .NET'da tayyor primitivlar bilan quriladi (ECDsa, SslStream). Kod maxfiy emas → entropiya muammosi yo'q. Oddiy. | Foydalanuvchi ikki ekranni ko'rishi va solishtirishi kerak |
| PAKE (SPAKE2) | Kod maxfiy, bitta ekran kifoya | .NET'da standart implementatsiya yo'q; tashqi kripto kutubxona kerak; xato qilish oson |

LAN KVM uchun ikkala mashina ham qo'l ostida bo'ladi, shuning uchun ikki ekranni
solishtirish muammo emas. SAS'ni tanlaymiz.

## Mashina identifikatori

Har bir Peshtoq **o'rnatilganda bir marta** barqaror self-signed sertifikat
yaratadi (ECDSA P-256) va saqlaydi. Uning **ochiq kaliti (SPKI)** = o'sha
mashinaning doimiy identifikatori. Pairing shu kalitni "pin" qiladi.

```
Yaratish (.NET):
  var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
  var req = new CertificateRequest("CN=Peshtoq-<hostname>", ec, HashAlgorithmName.SHA256);
  var cert = req.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(20));
  // PFX eksport → DPAPI bilan shifrlab saqlash:
  //   %AppData%\Peshtoq\identity.pfx   (ProtectedData.Protect, CurrentUser)
```

Shaxsiy kalit hech qachon tarmoqqa chiqmaydi. Faqat sertifikat (ochiq kalit)
TLS handshake'da ko'rsatiladi.

## Oqim (sequence)

```
  CLIENT (B)                                      HOST (A)
     │                                                │
     │  1. discovery: A'ni topdi (mDNS/UDP)           │
     │───────────── TLS connect (TCP 8787) ──────────►│
     │                                                │
     │  2. o'zaro TLS: ikki tomon ham self-signed     │
     │     sertifikatini ko'rsatadi.                  │
     │     RemoteCertificateValidationCallback = true │
     │     (CA zanjirini TEKSHIRMAYMIZ — SAS bilan    │
     │      tekshiramiz). Kanal shifrlangan, lekin    │
     │      hali autentifikatsiyalanmagan.            │
     │◄═══════════ shifrlangan kanal ═════════════════│
     │                                                │
     │  3. ikki tomon ham peer sertifikatining ochiq  │
     │     kalitini (SPKI) oladi va SAS hisoblaydi:   │
     │                                                │
     │     a = SPKI(A), b = SPKI(B)                    │
     │     (x,y) = sort(a, b)   // bayt bo'yicha       │
     │     h = SHA256( x || y )                        │
     │     SAS = (h[0..3] -> uint32) % 1_000_000       │
     │           -> 6 xonali, "042 918" ko'rinishida  │
     │                                                │
     │  4. HAR IKKI ekranda SAS ko'rsatiladi:         │
     │     A: "Yangi qurilma: B — kod 042 918         │
     │         [Tasdiqlash] [Rad etish]"              │
     │     B: "042 918 — A ekranidagi kod bilan bir   │
     │         xilmi? [Ha] [Yo'q]"                     │
     │                                                │
     │  5. FOYDALANUVCHI ikki kodni solishtiradi.     │
     │     Mos → ikkala tomonda ham tasdiqlaydi.      │
     │     Mos emas (MITM!) → rad etadi.              │
     │                                                │
     │  6. tasdiqlangach, har tomon peer'ning ochiq   │
     │     kalitini oq ro'yxatga PIN qiladi:          │
     │       %AppData%\Peshtoq\paired.json (DPAPI)    │
     │                                                │
     │  ✓ juftlandi. Kanal endi ishonchli.            │
```

### Nega MITM fosh bo'ladi
MITM (M) ikkala tomonga o'z sertifikatini ko'rsatadi:
- A ko'radi: `{SPKI(A), SPKI(M)}` → SAS_A = hash(A, M)
- B ko'radi: `{SPKI(M), SPKI(B)}` → SAS_B = hash(M, B)

`SAS_A ≠ SAS_B` → ikki ekranda **har xil** kod → foydalanuvchi mos emasligini
ko'radi va rad etadi. Halol holatda ikkalasi ham `hash(A, B)` → bir xil kod.

## Keyingi ulanishlar (juftlangandan so'ng)

Kod qayta kerak emas. Har ulanishda:
1. O'zaro TLS handshake.
2. `RemoteCertificateValidationCallback` ichida peer sertifikatining ochiq
   kalitini olib, `paired.json` dagi pinlangan kalit bilan **aynan** solishtirish.
3. Mos → ulanish davom etadi. Mos emas (kalit o'zgargan / soxta) → **darhol uziladi**.

Juftlanmagan, notanish qurilmadan ulanish so'rovi → host'da yana pairing oqimi
(4-qadam) ko'rsatiladi, avtomatik qabul yo'q.

## Saqlanadigan ma'lumot

`%AppData%\Peshtoq\paired.json` (butunlay DPAPI bilan shifrlangan):
```jsonc
{
  "devices": [
    {
      "name": "DESKTOP-OFFICE",      // peer hostname (do'stona ko'rsatish uchun)
      "spkiSha256": "9f86d08...",    // pinlangan ochiq kalit hash (hex)
      "pairedAt": "2026-11-26T12:00:00Z",
      "lastSeen": "2026-11-26T18:30:00Z"
    }
  ]
}
```
Faqat **hash** saqlanadi — to'liq kalit TLS handshake'da baribir keladi,
solishtirish uchun hash kifoya.

## .NET primitivlari (hammasi standart kutubxonada)

| Vazifa | API |
|---|---|
| ECDSA kalit + self-signed sertifikat | `ECDsa.Create(nistP256)`, `CertificateRequest.CreateSelfSigned` |
| O'zaro TLS | `SslStream` — server: `ClientCertificateRequired=true`; client: `ClientCertificates` |
| CA zanjirini chetlab o'tish | `RemoteCertificateValidationCallback` — pairing'da `true`, keyin pin tekshiruvi |
| Peer ochiq kaliti | `cert.GetPublicKey()` yoki `cert.PublicKey.ExportSubjectPublicKeyInfo()` |
| SAS hash | `SHA256.HashData(...)` |
| Diskda shifrlash | `ProtectedData.Protect/Unprotect` (DPAPI, `CurrentUser`) |

Tashqi kripto kutubxona **kerak emas**.

## Xavfsizlik tekshiruv ro'yxati (pairing uchun)

- [ ] Shaxsiy kalit DPAPI bilan shifrlangan, tarmoqqa chiqmaydi
- [ ] Pairing'da CA zanjiri emas, SAS tekshiriladi; keyingi ulanishlarda pin
- [ ] SAS ikkala ekranda ham ko'rsatiladi (bitta ekran yetarli emas)
- [ ] Notanish qurilma avtomatik qabul qilinmaydi — foydalanuvchi tasdig'i shart
- [ ] Pinlangan kalit o'zgarsa ulanish darhol uziladi (MITM/kalit almashtirish)
- [ ] Oq ro'yxatdan qurilmani o'chirish mumkin (sozlamalarda "unpair")

## Qamrovdan tashqari (bu hujjatda hal qilinmaydi)

- Edge-handoff geometriyasi (DPI, ko'p monitor, joylashuv) → 2-bosqich
- Fokus/input yo'naltirish → 2-bosqich
- Fayl ko'chirish → 4-bosqich
