# Meeting Audio Recorder

Aplikacja desktopowa dla **Windows 10 i Windows 11**, która jednocześnie nagrywa:

1. dźwięk z wybranego **mikrofonu** (WASAPI Capture),
2. dźwięk odtwarzany na wybranym **urządzeniu wyjściowym** (WASAPI Loopback),

a następnie miksuje je i zapisuje do pliku **MP3**.

Przeznaczona do nagrywania spotkań (Google Meet, Microsoft Teams, Zoom itd.) z poziomu zasobnika systemowego, ze globalnym skrótem klawiszowym.

> **Ważne:** Jesteś odpowiedzialny/a za poinformowanie uczestników i uzyskanie wymaganych zgód na nagrywanie rozmowy.

---

Kontekst dla kolejnych autorów i asystentów: [przekazanie tłumaczeń live](docs/TRANSLATION_HANDOFF.md). Opis raportów testowych: [docs/results](docs/results/README.md).

## Tłumaczenie live EN → PL (1.5.1)

Menu zasobnika → **Rozpocznij tłumaczenie** lub domyślnie **Ctrl+Alt+T**. Ten sam skrót zatrzymuje sesję. W **Ustawieniach tłumaczenia** można zmienić skrót, włączyć wyjście audio, mikrofon lub oba źródła, wybrać model oraz wygląd okna. Urządzenia są tymi samymi, które wybrano i zapisano w głównych ustawieniach Recordera. Start tłumaczenia nie rozpoczyna nagrywania MP3.

Okno napisów jest zawsze na wierzchu, przesuwalne za górny pasek i skalowalne. Domyślna czcionka ma 14 jednostek WPF. Można zmieniać krój, rozmiar, kolory i krycie tła oraz pokazać EN obok/pod PL. „Ukryj” pozostawia tłumaczenie aktywne; „Pokaż napisy” przywraca panel. „Stop” domyka frazę. Roboczy tekst zmienia się podczas mówienia; do historii trafiają wyłącznie finalne frazy. Kontekst jest dzielony po pauzie lub po 20 s ciągłego audio.

**Tłumaczenia live — historia lokalna** pokazuje sesje, EN/PL, czas i źródło. Dostępne są oddzielne filtry tytułu sesji oraz tekstu EN/PL. Zaznaczone komórki można kopiować Ctrl+C. Czas jest przybliżonym czasem fragmentu, nie wyrównaniem każdego słowa. „Mikrofon” i „Wyjście” oznaczają urządzenie, nie tożsamość osoby.

Zapis znajduje się w `%LOCALAPPDATA%\MeetingAudioRecorder\Translations`: zaszyfrowane DPAPI CurrentUser metadane i osobne atomowo publikowane fragmenty. Wersje robocze oraz audio z samego tłumacza nie są utrwalane. Historia jest niezależna od katalogu MP3 i nie jest przekazywana do RecoWilk. Włączenie nagrywania MP3 nadal uruchamia dotychczasowe zasady eksportu tych nagrań. Po awarii zachowane frazy można otworzyć; niedomknięta sesja ma oznaczenie przerwania. Pliki są związane z kontem Windows; nie są przenośnym formatem eksportu. Historia nie ma automatycznego limitu retencji.

Modele działają lokalnie przez Vulkan (Whisper small.en Q8 lub turbo Q8), Silero i Marian ONNX int8. Wymagane są zgodny GPU/sterownik i modele o przypiętych sumach kontrolnych. Instalator dostarcza silnik i testowany eksport Marian ONNX. Przy pierwszym użyciu tłumacza przycisk „Pobierz / sprawdź modele” pobiera Whisper i Silero z Hugging Face / GitHub: około 267 MB dla small.en albo 877 MB dla turbo. To samo okno jest dostępne w ustawieniach tłumaczenia. Pokazuje postęp, pozwala anulować i ponowić próbę. Poprawne modele nie są pobierane ponownie; nieukończony plik jest usuwany i pobierany od początku przy ponowieniu. Rozmiar i SHA-256 są sprawdzane przed atomowym zastąpieniem pliku. Po przygotowaniu tłumacz działa lokalnie. Można również wskazać przygotowany folder albo skopiować zweryfikowane modele:

```powershell
./scripts/prepare-translation-models.ps1 -SourceDirectory 'D:\Modele' -IncludeTurbo
```

Domyślne miejsce: `%LOCALAPPDATA%\MeetingAudioRecorder\Models`. Skrypt przyjmuje tylko znane pliki i weryfikuje hash przed i po kopiowaniu. Modele pochodzą z prototypu; jakość pozostaje eksperymentalna (zwłaszcza liczby, negacje i niepełne frazy).

Każde włączone źródło ma osobny proces tłumacza i własny kontekst. Dwa źródła wymagają więcej pamięci i mocy obliczeniowej. Awaria procesu pomocniczego nie kończy procesu nagrywania, lecz obciążenie GPU/CPU nadal jest wspólne dla komputera. Program zatrzymuje przeciążoną sesję zamiast po cichu odrzucać końcowe frazy. Modele są ładowane na początku sesji, przed otwarciem źródła audio.

## Funkcje

- Praca w **system tray** (osobne statusy nagrywania, tłumaczenia i wysyłki)
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
- Lokalne okno **Lista nagrań** lub **Kolejka i błędy eksportu** z metadanymi, uczestnikami, postępem, błędami i ręcznym ponawianiem eksportu

---

## Wymagania systemowe

| Wymaganie | Wartość |
|-----------|---------|
| System | Windows 11; Windows 10 wyłącznie w edycjach/wersjach wspieranych przez .NET 10 |
| Architektura | x64 |
| Uprawnienia | Standardowy użytkownik (bez admina) |
| Runtime | .NET 10 (przy self-contained — nie jest wymagany osobno) |
| Audio | Sterowniki WASAPI (standard Windows) |
| Kodowanie MP3 | Windows Media Foundation (wbudowane) |

**Nie wymagamy FFmpeg ani innych zewnętrznych programów.**

Do kompilacji wymagane jest SDK .NET 10 (wybierane przez `global.json`, bez wersji preview). Publikacja self-contained dołącza runtime do programu. Zakres wsparcia systemów: [oficjalna lista .NET 10](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md). Migrację sprawdzono automatycznie na Windows 11; starsze systemy wymagają oddzielnego testu.

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
  MeetingAudioRecorder.TranslationWorker/ # lokalny silnik EN→PL, osobny proces i prywatny named pipe
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
6. Naciśnij **Ctrl+Alt+R** (lub menu tray) — start nagrywania (na ikonie pojawia się czerwona kropka).
7. Wybierz z tray **Lista nagrań** lub **Kolejka i błędy eksportu**, aby zobaczyć lokalną historię, metadane i stan wysyłania do RecoWilk.
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

Budowanie instalatora wymaga sprawdzonego eksportu Marian: domyślnie Models\marian w profilu użytkownika albo folder wskazany zmienną RECORDER_MARIAN_MODELS. Skrypt publikacji sprawdza wszystkie hashe i dołącza pliki w TranslationModels\marian. Wagi nie są przechowywane w Git.

## Statusy w zasobniku — od wersji 1.5.5

Ikona ma stałe niebieskie tło. Czerwona kropka oznacza nagrywanie, turkusowe T — uruchamianie, pracę lub kończenie tłumacza. Oba znaczniki mogą być widoczne równocześnie. Żółty wykrzyknik sygnalizuje problem nagrywania, tłumaczenia lub wysyłki; nie zastępuje znaczników aktywności. Po zapisaniu nagrania aplikacja wraca do gotowości zamiast pozostawać zielona; potwierdzenie zapisu pozostaje powiadomieniem.

Najedź na ikonę, aby zobaczyć wszystkie statusy, czas nagrania i źródła aktywnego tłumacza. Menu ma sekcje Nagrywanie, Tłumaczenie, Wysyłka i Aplikacja. Wysyłka pokazuje liczbę rzeczywiście obsługiwanych plików i postęp chunków, oczekujących oraz wymagających uwagi. Stary etap eksportu zapisany przed restartem nie jest traktowany jako bieżąca transmisja. Kolejka i błędy eksportu otwiera istniejące centrum nagrań/eksportu. Nie zmienia to działania skrótów ani lokalnego zapisu danych.
