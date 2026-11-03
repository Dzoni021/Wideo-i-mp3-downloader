# OmniFetch

Jednostavan i pristupačan desktop program za preuzimanje javno dostupnog videa
ili zvuka. Koristi [yt-dlp](https://github.com/yt-dlp/yt-dlp), pa podržava velik
broj sajtova koje ta biblioteka podržava, uključujući javne stranice za video.

## Windows portabilna verzija

Windows aplikacija je nativni WinForms program sa Windows UI Automation
podrškom. Kontrole imaju imena i opise za NVDA, JAWS i Narrator, a status
preuzimanja se objavljuje čitaču ekrana. Nije potrebna instalacija: preuzmite
paket za svoju arhitekturu i pokrenite `OmniFetch.exe`.

Paket već sadrži `yt-dlp.exe`, `ffmpeg.exe`, `ffprobe.exe` i manifest verzija.
Pri pokretanju program proverava i po potrebi ažurira aplikaciju, yt-dlp i
FFmpeg preko HTTPS-a sa njihovih GitHub release stranica. Nova verzija same
aplikacije se preuzima zajedno sa svim alatima i instalira nakon potvrde
korisnika.

Licencne informacije za uključene binarne komponente nalaze se u
`THIRD-PARTY-NOTICES.md`.

Podržani su Windows 10 i Windows 11 za x64, x86 i ARM64. Jedan program ne može
biti kompatibilan sa baš svim istorijskim verzijama Windowsa; Windows 7/8 nisu
cilj ovog .NET 8 paketa.

Za lokalni build, u PowerShell-u pokrenite:

```powershell
.\build-windows.ps1
```

Paketi se nalaze u `publish\win-x64`, `publish\win-x86` i `publish\win-arm64`.

## Razvojna Python verzija

Python verzija ostaje dostupna za razvoj i testiranje. Potrebni su Python 3.10+
i FFmpeg u sistemskom `PATH`-u.

```bash
python -m venv .venv
# Windows:
.venv\Scripts\activate
# macOS/Linux:
# source .venv/bin/activate
python -m pip install -r requirements.txt
python app.py
```

## Korišćenje

1. Nalepite javni URL.
2. Izaberite video ili MP3, kvalitet i odredišni folder.
3. Pritisnite **Preuzmi** ili Enter.

Aplikacija preuzima jednu stavku po URL-u i prikazuje procenat, brzinu i
preostalo vreme. Dugme **Otkaži** prekida aktivno preuzimanje. Zvučni signal i
statusna poruka najavljuju početak i kraj. Ako preuzimanje nije moguće,
prikazuje se kratko objašnjenje za privatni sadržaj, DRM, regionalnu
nedostupnost, nepodržan sajt/format ili problem sa alatima.

Podrška zavisi od yt-dlp ekstraktora i promena na konkretnom sajtu; nijedan
program ne može pouzdano podržati baš svaki sajt. Koristite program samo za
sadržaj za koji imate dozvolu. Aplikacija ne zaobilazi DRM, privatni sadržaj,
prijave ili druge tehničke zaštite.
