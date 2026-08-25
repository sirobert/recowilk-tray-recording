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
- Opcjonalna automatyka Google Meet: Calendar lub rozszerzenie wskazuje spotkanie, a obecność użytkownika steruje startem i stopem
- Opcjonalna integracja RecoWilk wysyłająca gotowy MP3 po bezpiecznym, wznawialnym uploadzie
- Lokalne okno **Nagrania i eksport** z metadanymi, uczestnikami, postępem, błędami i ręcznym ponawianiem eksportu

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
MeetingAudioRecorder.sln              # główne rozwiązanie Visual Studio / dotnet CLI
browser-extension/                    # Manifest V3: Meeting Orgniazer Gemini
src/
  MeetingAudioRecorder.App/            # WPF, tray, hotkey, ViewModels
  MeetingAudioRecorder.Core/           # modele, interfejsy, stan, use-case
  MeetingAudioRecorder.Audio/          # WASAPI, miks, resampling, MP3
  MeetingAudioRecorder.Infrastructure/ # JSON, Serilog, autostart, mutex, recovery
  MeetingAudioRecorder.BrowserBridge/  # lokalny host Native Messaging Chrome/Edge
tests/
  MeetingAudioRecorder.Core.Tests/
  MeetingAudioRecorder.Audio.Tests/
scripts/
  publish.ps1
  build-installer.ps1
  installer.iss
docs/
  AI_WORKFLOW.md
  MANUAL_TESTS.md
  REPAIR_PLAN.md
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
| Cisza loopback | Potwierdzone luki są uzupełniane na deterministycznej osi ramek |
| Sync | Wspólne ticki startu, cisza wiodąca i limitowana korekcja dryfu |
| Resampling | Ułamkowy WDL z korekcją maks. 1000 ppm |
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
start MeetingAudioRecorder.sln
```

### 2. Przywróć pakiety

```powershell
dotnet restore MeetingAudioRecorder.sln
```

### 3. Skompiluj

```powershell
dotnet build MeetingAudioRecorder.sln -c Release
```

### 4. Uruchom

```powershell
dotnet run --project src\MeetingAudioRecorder.App\MeetingAudioRecorder.App.csproj -c Release
```

Aplikacja pojawi się w **zasobniku systemowym** (obok zegara).

### 5. Testy jednostkowe

```powershell
dotnet test MeetingAudioRecorder.sln -c Release
```

---

## Pierwsze użycie

1. Kliknij prawym przyciskiem ikonę w tray → **Ustawienia**.
2. Wybierz **mikrofon** i **urządzenie wyjściowe** (te same co w Meet/Teams).
3. **Testuj mikrofon** — mów, obserwuj pasek poziomu.
4. **Testuj przechwytywanie dźwięku** — odtwórz dźwięk na słuchawkach, obserwuj poziom.
5. Zapisz ustawienia.
6. Naciśnij **Ctrl+Alt+R** (lub menu tray) — start nagrywania (ikona zmienia kolor).
7. Wybierz z tray **Nagrania i eksport**, aby zobaczyć lokalną historię, metadane i stan wysyłania do RecoWilk.
7. Ponownie **Ctrl+Alt+R** — stop, przetwarzanie, plik MP3 w folderze nagrań  
   (domyślnie `Dokumenty\Nagrania spotkań`).

### Opcjonalne automatyczne nagrywanie Google Meet

1. W Google Cloud utwórz projekt i klienta OAuth typu **Desktop app**.
2. Włącz Google Calendar API, Google Meet REST API oraz dostęp OpenID Connect.
3. Pobierz plik `client_secret*.json`; nie dodawaj go do Git ani nie udostępniaj publicznie.
4. W aplikacji otwórz **Ustawienia → Google Meet → Połącz z Google…** i wybierz pobrany JSON.
5. Zaloguj się w systemowej przeglądarce oraz zaakceptuj dostęp tylko do odczytu Calendar/Meet.
6. Zaznacz **Automatycznie nagrywaj, gdy dołączę do Google Meet** i zapisz ustawienia.

Samo rozpoczęcie wydarzenia ani otwarcie strony Meet nie uruchamia nagrania. Aplikacja zaczyna nagrywać dopiero po wykryciu połączonego konta w aktywnej konferencji i zatrzymuje własną sesję po trzech potwierdzeniach wyjścia w czasie co najmniej 15 sekund. Do Meet trzeba wejść tym samym kontem, które połączono z rejestratorem.

#### Meeting Orgniazer Gemini — spotkania bez Calendar

Rozszerzenie jest dołączone do aplikacji 1.2.2 i obsługuje linki Meet przesłane przez e-mail, komunikator lub czat:

1. Otwórz **Ustawienia → Google Meet**.
2. Kliknij **Pobierz / zainstaluj dla Chrome** albo **dla Edge**.
3. Aplikacja przygotuje pakiet, skopiuje jego ścieżkę do schowka oraz otworzy folder i stronę rozszerzeń.
4. Włącz **Tryb dewelopera**, wybierz **Załaduj rozpakowane** i wskaż otwarty folder `MeetingOrgniazerGemini`.

Chrome i Edge nie pozwalają zwykłej aplikacji desktopowej instalować rozszerzenia po cichu. Po publikacji w Chrome Web Store/Edge Add-ons ten etap będzie można zastąpić pojedynczym przyciskiem prowadzącym do sklepu. Rozszerzenie ma stałe ID `eljjpmlmlnjjpjlnhiilfclkhoecdlij` i łączy się wyłącznie z lokalnym hostem `com.meetingorganizer.gemini`.

Przy automatycznym starcie aplikacja przegląda aktywne sesje Windows Core Audio procesów Chrome, Edge, Firefox, Brave, Opera i Vivaldi. Aktywna sesja mikrofonowa wskazuje rodzinę przeglądarki, a wyjście jest wybierane z aktywnych sesji tej samej przeglądarki. Jeśli wykrycie jednej ze stron się nie powiedzie, używane jest odpowiednie urządzenie zapisane w ustawieniach. Wykrycie nie nadpisuje ustawień i nie zmienia urządzeń w trakcie nagrania. Powiadomienie startowe pokazuje zastosowane urządzenia.

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
| `googleMeetAutomationEnabled` | Automatyczny start/stop dla Google Meet | false |
| `recowilkUploadEnabled` | Automatyczne wysłanie gotowego MP3 do RecoWilk | false |
| `recowilkBaseUrl` | Bazowy adres HTTPS serwera RecoWilk | — |

Przy uszkodzonym JSON: kopia `.corrupt.*.bak`, domyślne wartości, aplikacja działa dalej.

---

## WASAPI Loopback — jak to działa

- Loopback przechwytuje **miks systemowy** wysyłany do wybranego urządzenia **Render** (słuchawki/głośniki). Przy automatycznym starcie Google Meet może to być endpoint wykryty z aktywnej sesji przeglądarki.
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
| Kolejka RecoWilk | `%LOCALAPPDATA%\MeetingAudioRecorder\Uploads` — wpisy zaszyfrowane DPAPI |
| Katalog nagrań | `%LOCALAPPDATA%\MeetingAudioRecorder\Recordings` — historia i metadane zaszyfrowane DPAPI |
| Automatyka Google nie startuje | To samo konto w aplikacji i Meet; dla spotkania bez Calendar sprawdź, czy Meeting Orgniazer Gemini jest włączone |
| Google prosi o ponowne logowanie | Połącz konto ponownie; dostęp mógł zostać cofnięty lub token wygasł |

---

## Prywatność

- Brak telemetrii, analityki i reklam.
- Domyślnie brak wysyłania nagrań i logów na zewnątrz. Po jawnym włączeniu integracji RecoWilk wysyłany jest gotowy MP3 i metadane spotkania; lokalny plik pozostaje zachowany.
- Treść audio **nie** jest logowana.
- Nagrywanie jest **zawsze widoczne** na ikonie tray.
- Bez włączonej automatyki aplikacja nie łączy się z Google.
- Po włączeniu automatyki pobierane są metadane Calendar/Meet potrzebne do wykrycia wydarzenia, obecności i opisania nagrania. Tytuł, opis, link, terminy i uczestnicy są przechowywani w lokalnym katalogu szyfrowanym DPAPI. Po włączeniu RecoWilk trafiają także do szyfrowanej kolejki i są wysyłane do wskazanego serwera; nie trafiają do logów.
- Meeting Orgniazer Gemini działa tylko na `meet.google.com`; przekazuje lokalnie kod spotkania i nazwę przeglądarki, bez tokenów, tytułów kart i historii. Stan w `%LOCALAPPDATA%\MeetingAudioRecorder\Browser` wygasa po 90 sekundach.
- Token OAuth jest szyfrowany przez Windows DPAPI dla bieżącego użytkownika.
- Klucz API RecoWilk jest szyfrowany osobno przez Windows DPAPI, nigdy nie trafia do `settings.json` ani logów.
- Dane lokalne: MP3, ustawienia, zaszyfrowany katalog nagrań, token Google, klucz i kolejka RecoWilk oraz logi w profilu użytkownika.

---

## Publikacja (self-contained x64)

```powershell
.\scripts\publish.ps1
```

Skrypt publikuje aplikację jako self-contained multi-file oraz lokalny most przeglądarki jako self-contained single-file. Nie zastępuj go samym `dotnet publish` projektu App, ponieważ instalator nie zawierałby wtedy aktualnego `MeetingAudioRecorder.BrowserBridge.exe`.

**Single-file aplikacji** jest celowo wyłączony: Media Foundation i natywne zależności działają stabilniej w trybie multi-file.

Wyniki:

- `publish\win-x64\MeetingAudioRecorder.exe`
- `publish\win-x64\MeetingAudioRecorder.BrowserBridge.exe`
- `publish\win-x64\BrowserExtension\`

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
- Pełna bramka lokalna: `.\scripts\verify.ps1`
- Media Foundation opt-in:

  ```powershell
  $env:MAR_RUN_WINDOWS_INTEGRATION = "1"
  dotnet test tests\MeetingAudioRecorder.Audio.Tests\MeetingAudioRecorder.Audio.Tests.csproj `
    -c Release --filter "Category=WindowsIntegration"
  ```

- Integracyjne (ręcznie): [docs/MANUAL_TESTS.md](docs/MANUAL_TESTS.md)

Logi diagnostyczne zawierają wyłącznie metadane techniczne: format i `BlockAlign`,
liczbę ramek, uzupełnione luki, korektę dryfu oraz rozmiary plików. Próbki i treść
audio nie są zapisywane w logach.

---

## Licencja i odpowiedzialność

Użytkownik odpowiada za zgodność nagrywania z prawem i regulaminami platform (Meet, Teams, Zoom) oraz za uzyskanie zgód uczestników.
