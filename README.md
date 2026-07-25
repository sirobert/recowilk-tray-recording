# Meeting Audio Recorder

Aplikacja desktopowa dla **Windows 10 i Windows 11**, która jednocześnie nagrywa:

1. dźwięk z wybranego **mikrofonu** (WASAPI Capture),
2. dźwięk odtwarzany na wybranym **urządzeniu wyjściowym** (WASAPI Loopback),

a następnie miksuje je i zapisuje do pliku **MP3**.

Przeznaczona do nagrywania spotkań (Google Meet, Microsoft Teams, Zoom itd.) z poziomu zasobnika systemowego, ze globalnym skrótem klawiszowym.

> **Ważne:** Jesteś odpowiedzialny/a za poinformowanie uczestników i uzyskanie wymaganych zgód na nagrywanie rozmowy.

---

## Funkcje

- Praca w **system tray** (ikona stanu: gotowość / nagrywanie / przetwarzanie / błąd)
- Globalny skrót (domyślnie **Ctrl+Alt+R**) przez `RegisterHotKey`
- Wybór mikrofonu i urządzenia wyjściowego po stabilnym **ID endpointu**
- Równoległy zapis do tymczasowych WAV → miks → MP3 (Media Foundation)
- Odzyskiwanie niedokończonych nagrań po awarii
- Autostart (HKCU Run, bez administratora)
- Pojedyncza instancja (named Mutex)
- Konfiguracja JSON w profilu użytkownika
- Logi z rotacją (bez treści audio)
- Działa **wyłącznie lokalnie** — brak telemetrii, chmury i połączeń sieciowych

---

## Wymagania systemowe

| Wymaganie | Wartość |
|-----------|---------|
| System | Windows 10 (1903+) lub Windows 11 |
| Architektura | x64 |
| Uprawnienia | Standardowy użytkownik (bez admina) |
| Runtime | .NET 8 (przy self-contained — nie jest wymagany osobno) |
| Audio | Sterowniki WASAPI (standard Windows) |
| Kodowanie MP3 | Windows Media Foundation (wbudowane) |

**Nie wymagamy FFmpeg ani innych zewnętrznych programów.**

---

## Architektura rozwiązania

```
MeetingAudioRecorder.slnx
src/
  MeetingAudioRecorder.App/            # WPF, tray, hotkey, ViewModels
  MeetingAudioRecorder.Core/           # modele, interfejsy, stan, use-case
  MeetingAudioRecorder.Audio/          # WASAPI, miks, resampling, MP3
  MeetingAudioRecorder.Infrastructure/ # JSON, Serilog, autostart, mutex, recovery
tests/
  MeetingAudioRecorder.Core.Tests/
  MeetingAudioRecorder.Audio.Tests/
scripts/
  publish.ps1
  build-installer.ps1
  installer.iss
docs/
  MANUAL_TESTS.md
```

### Przepływ nagrania

```
[Start] → WasapiCapture (mic) ──► recording-id_microphone.tmp.wav
       → WasapiLoopbackCapture ─► recording-id_loopback.tmp.wav
[Stop]  → wyrównanie startu / resampling 48 kHz / stereo float
       → miks + soft limiter
       → Media Foundation → nazwa.mp3.partial → rename → nazwa.mp3
       → usunięcie plików tymczasowych (tylko po sukcesie)
```

### Decyzje techniczne

| Temat | Decyzja |
|-------|---------|
| Capture | NAudio WASAPI shared mode |
| Loopback | `WasapiLoopbackCapture` na konkretnym `MMDevice` |
| Cisza loopback | Timer uzupełnia zerowe próbki do osi czasu ściennej |
| Sync | Wspólne ticki startu + cisza wiodąca na opóźnionym źródle |
| Resampling | `WdlResamplingSampleProvider` |
| MP3 | `MediaFoundationEncoder` (bez FFmpeg) |
| Hotkey | WinAPI `RegisterHotKey` (bez global hooka) |
| DI | `Microsoft.Extensions.DependencyInjection` |
| Logi | Serilog → `%LOCALAPPDATA%\MeetingAudioRecorder\Logs` |

### Ryzyka

1. **WASAPI Loopback** — przy braku odtwarzania callback może milczeć; aplikacja wstawia ciszę.
2. **Bluetooth** — Meet/Teams mogą przełączyć profil (Stereo → Hands-Free); endpoint zmienia ID/stan. Aplikacja nie przełącza automatycznie źródła w trakcie nagrania.
3. **Synchronizacja** — różne zegary urządzeń; długie nagrania (≥4 h) wymagają konsekwentnego uzupełniania ciszy i wspólnego t0.
4. **Media Foundation** — koder MP3 musi być dostępny w systemie (standard na Win10/11).

---

## Uruchomienie projektu (deweloperskie)

### 1. Otwórz projekt

```powershell
cd C:\pr\Recorder\Windows
# Visual Studio 2022 / Rider / VS Code
start MeetingAudioRecorder.slnx
```

### 2. Przywróć pakiety

```powershell
dotnet restore MeetingAudioRecorder.slnx
```

### 3. Skompiluj

```powershell
dotnet build MeetingAudioRecorder.slnx -c Release
```

### 4. Uruchom

```powershell
dotnet run --project src\MeetingAudioRecorder.App\MeetingAudioRecorder.App.csproj -c Release
```

Aplikacja pojawi się w **zasobniku systemowym** (obok zegara).

### 5. Testy jednostkowe

```powershell
dotnet test MeetingAudioRecorder.slnx -c Release
```

---

## Pierwsze użycie

1. Kliknij prawym przyciskiem ikonę w tray → **Ustawienia**.
2. Wybierz **mikrofon** i **urządzenie wyjściowe** (te same co w Meet/Teams).
3. **Testuj mikrofon** — mów, obserwuj pasek poziomu.
4. **Testuj przechwytywanie dźwięku** — odtwórz dźwięk na słuchawkach, obserwuj poziom.
5. Zapisz ustawienia.
6. Naciśnij **Ctrl+Alt+R** (lub menu tray) — start nagrywania (ikona zmienia kolor).
7. Ponownie **Ctrl+Alt+R** — stop, przetwarzanie, plik MP3 w folderze nagrań  
   (domyślnie `Dokumenty\Nagrania spotkań`).

---

## Ustawienia

Plik: `%LOCALAPPDATA%\MeetingAudioRecorder\settings.json`

| Pole | Opis | Domyślnie |
|------|------|-----------|
| `microphoneDeviceId` | ID endpointu mikrofonu | — |
| `outputDeviceId` | ID endpointu render | — |
| `recordingsDirectory` | Folder MP3 | Dokumenty\Nagrania spotkań |
| `startWithWindows` | Autostart HKCU | true |
| `hotkey` | Skrót globalny | Ctrl+Alt+R |
| `mp3BitrateKbps` | 128 / 192 / 256 / 320 | 192 |
| `targetSampleRate` | 44100 / 48000 | 48000 |
| `microphoneVolume` | Waga w miksie | 1.0 |
| `loopbackVolume` | Waga w miksie | 0.85 |
| `keepSeparateTracks` | Dodatkowe WAV | false |
| `openFolderAfterRecording` | Otwórz folder po zapisie | false |
| `fileNameFormat` | Wzorzec nazwy | `Nagranie_yyyy-MM-dd_HH-mm-ss.mp3` |

Przy uszkodzonym JSON: kopia `.corrupt.*.bak`, domyślne wartości, aplikacja działa dalej.

---

## WASAPI Loopback — jak to działa

- Loopback przechwytuje **miks systemowy** wysyłany do wybranego urządzenia **Render** (słuchawki/głośniki).
- To **nie** jest nagranie „tylko z Chrome/Teams” — w pierwszej wersji to **cały dźwięk** na tym urządzeniu (powiadomienia, muzyka, inne aplikacje).
- Architektura (`ILoopbackCaptureService`) pozwala w przyszłości dodać process loopback (Windows 10 2004+).
- Gdy nic nie jest odtwarzane, system może nie wywoływać callbacka — aplikacja **uzupełnia ciszę**, aby ścieżka nie skracała się względem mikrofonu.

---

## Ograniczenia Bluetooth

- Jedno urządzenie BT może mieć wiele endpointów (Stereo, Headset, Hands-Free AG Audio).
- Aplikacja listuje je **osobno** i zapisuje wybrane **ID**.
- Podczas rozmowy Windows często przełącza na profil Hands-Free (niższa jakość, inny endpoint).
- Zalecenie: w Meet/Teams i w aplikacji wybierz ten sam endpoint; przy zmianie profilu aplikacja powiadomi i bezpiecznie zakończy nagranie, jeśli endpoint zniknie.

---

## Rozwiązywanie problemów

| Problem | Co sprawdzić |
|---------|----------------|
| Brak dźwięku z mikrofonu | Ustawienia Windows → Prywatność → Mikrofon → dostęp dla aplikacji desktop |
| Brak dźwięku drugiej strony | Czy wybrano właściwe urządzenie wyjściowe (to samo co w Meet)? Test loopback |
| Skrót nie działa | Konflikt z inną aplikacją — zmień skrót w ustawieniach |
| Błąd kodowania MP3 | Zainstalowane funkcje multimedialne Windows / Media Feature Pack (N editions) |
| Urządzenie zniknęło | Odśwież listę; wybierz ponownie; sprawdź BT |
| Ikona nie widać | Tray → „Pokaż ukryte ikony” |
| Logi | `%LOCALAPPDATA%\MeetingAudioRecorder\Logs` |
| Temp / odzyskiwanie | `%LOCALAPPDATA%\MeetingAudioRecorder\Temp` |

---

## Prywatność

- Brak telemetrii, analityki, reklam, chmury.
- Brak wysyłania nagrań i logów na zewnątrz.
- Treść audio **nie** jest logowana.
- Nagrywanie jest **zawsze widoczne** na ikonie tray.
- Dane: lokalne pliki MP3 + settings + logi w profilu użytkownika.

---

## Publikacja (self-contained x64)

```powershell
.\scripts\publish.ps1
```

Równoważnie:

```powershell
dotnet publish src\MeetingAudioRecorder.App\MeetingAudioRecorder.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false `
  -o publish\win-x64
```

**Single-file** jest celowo wyłączony: Media Foundation / natywne zależności bywają problematyczne przy single-file extract.

Wynik: `publish\win-x64\MeetingAudioRecorder.exe`

### Instalator Inno Setup

1. Zainstaluj [Inno Setup 6](https://jrsoftware.org/isinfo.php).
2. Uruchom:

```powershell
.\scripts\build-installer.ps1
```

Instalator:

- instaluje w `%LOCALAPPDATA%\Programs\MeetingAudioRecorder` (bez admina),
- tworzy skrót w menu Start,
- opcjonalnie autostart,
- przy deinstalacji **nie usuwa** nagrań ani folderu danych użytkownika.

---

## Testy

- Jednostkowe: `dotnet test`
- Integracyjne (ręcznie): [docs/MANUAL_TESTS.md](docs/MANUAL_TESTS.md)

---

## Licencja i odpowiedzialność

Użytkownik odpowiada za zgodność nagrywania z prawem i regulaminami platform (Meet, Teams, Zoom) oraz za uzyskanie zgód uczestników.
