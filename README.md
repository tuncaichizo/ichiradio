# Ichi Radyo

> **© 2026 [Kripto Ichizo](https://www.youtube.com/@kriptoichizo) — Tüm hakları saklıdır.**
> Bu proje Kripto Ichizo'ya aittir. Kod yalnızca incelemek için açıktır; izinsiz kopyalanamaz, dağıtılamaz, değiştirilemez ve ticari amaçla kullanılamaz. Ayrıntılar: [LICENSE](LICENSE)

**Kripto Ichizo** tarafından tasarlanıp geliştirildi. RAM yemeyen, işlemci yormayan, cyberpunk tasarımlı Windows internet radyosu.

- Radyo çalarken yaklaşık **30–40 MB RAM** kullanır, boşta beklerken işlemci kullanımı neredeyse sıfırdır.
- Electron ya da tarayıcı kullanmaz. Windows'la gelen .NET Framework ve Windows Media Player motoru üzerinde çalışır.
- Exe yaklaşık 200 KB'tır ve kurulum gerektirmez.

## Özellikler

- **KEŞFET:** [radio-browser.info](https://www.radio-browser.info) veritabanındaki 50.000'den fazla istasyon arasında arama yapar.
  - Arama kutusu boşsa Türkiye'nin popüler istasyonlarını gösterir.
  - Tür araması için `#pop`, `#jazz` gibi yazılır.
- **Dünya haritası:** Radyonun sağında açılır.
  - Ülkeler istasyon sayısına göre renklenir.
  - Bir ülkeye tıklayınca o ülkenin istasyonları listelenir.
  - İstasyonlar şehirlerinin üzerinde nokta olarak görünür, noktaya tıklayınca çalar.
- **Favoriler:** Ekleme, silme ve sıralama.
- **Hassas ses ayarı:** Ses kulağın algısına uygun şekilde artıp azalır (%50 ≈ −12 dB, %4 ≈ −56 dB). %1'lik adımlarla ayarlanabilir, yanında dB göstergesi ve sessize alma var.
- **Çalan şarkı:** Yayın şarkı adını gönderiyorsa ekranda gösterilir.
- **Otomatik yeniden bağlanma:** Bağlantı koparsa en fazla 5 kez yeniden dener.
- **Uyku zamanlayıcı:** 15 ile 90 dakika arası seçilebilir.
- **Tepsi simgesi:** Menüsünden favoriler çalınabilir. Orta tık çal/durdur yapar.

## Kısayollar

| Tuş | İşlev |
|---|---|
| `Space` | Çal / durdur |
| `←` `→` | Ses ±1 |
| `+` `−` | Ses ±5 |
| `M` | Sessize al |
| `Ctrl+F` | Ara |
| `Esc` | Tepsiye gizle |

## Derleme

Ek kurulum gerekmez. Windows'un kendi C# derleyicisi kullanılır:

```powershell
.\build.ps1
```

Çıktı: `IchiRadyo.exe`. Favoriler ve ayarlar `%APPDATA%\IchiRadyo` klasöründe tutulur.

### Harita verisi

`harita.bin`, [Natural Earth](https://www.naturalearthdata.com) 1:50m ülke sınırlarından (kamu malı) üretilmiştir ve derlemede exe'nin içine gömülür. Yeniden üretmek için:

```powershell
node harita-hazirla.js ne_50m_admin_0_countries.geojson harita.bin
```

## Dosyalar

| Dosya | Açıklama |
|---|---|
| `IchiRadyo.cs` | Uygulamanın tamamı (C# 5, WinForms) |
| `build.ps1` | Derleme betiği |
| `harita-hazirla.js` | Harita verisini sadeleştirip ikili dosyaya çevirir |
| `harita.bin` | Gömülü ülke sınırları (113 KB) |
| `ikon.ico` | Uygulama ikonu |
| `LICENSE` | Lisans: tüm hakları saklıdır |

## Geliştirici

**Kripto Ichizo**, Ichi Radyo'nun fikir sahibi, tasarımcısı ve geliştiricisi.

- YouTube: [@kriptoichizo](https://www.youtube.com/@kriptoichizo)
- X: [@TuncaIchizo](https://x.com/TuncaIchizo)
- GitHub: [tuncaichizo](https://github.com/tuncaichizo)

Uygulamanın başlık çubuğunda **"by Kripto Ichizo"** imzası ve tepsi menüsünde kanal bağlantısı bulunur. Exe'nin dosya özelliklerinde de telif bilgisi yer alır.

## Lisans

**Tüm hakları saklıdır.** Bu depo herkese açık olsa da açık kaynak **değildir**. Kopyalama, dağıtma, değiştirme, başka projeye dahil etme, ticari kullanım ve imza/telif bildirimlerini kaldırma, Kripto Ichizo'nun yazılı izni olmadan yasaktır. Tam metin: [LICENSE](LICENSE)

İzin talepleri için X üzerinden ulaşabilirsin: [@TuncaIchizo](https://x.com/TuncaIchizo)

---

<sub>Ichi Radyo © 2026 Kripto Ichizo · Harita verisi: Natural Earth (kamu malı) · İstasyon verisi: radio-browser.info</sub>
