# Przekazanie projektu: tłumaczenia live

Stan: 2026-11-02, wersja 1.5.1. Ten dokument wyjaśnia decyzje i pułapki; kod oraz przypięte manifesty są źródłem aktualnych parametrów. Aktualizuj go przy zmianie architektury lub zachowania. Nie traktuj historycznych raportów jako nowych pomiarów.

## Od czego zacząć

1. Przeczytaj [AGENTS.md](../AGENTS.md), ten dokument i odpowiednie zadanie w [REPAIR_PLAN.md](REPAIR_PLAN.md).
2. Sprawdź `git status`: migracja i integracja mogą zawierać niezacommitowane zmiany. Nie resetuj ich ani nie nadpisuj prac innych autorów.
3. Przeczytaj [README](../README.md) dla obsługi programu oraz [MANUAL_TESTS](MANUAL_TESTS.md), sekcje 19–20, dla zakresu kontroli interaktywnych.
4. [results/README.md](results/README.md) wyjaśnia zapisane wyniki testów. Nie trzeba zaczynać od surowych JSON-ów.

Główna aplikacja: `C:\pr\Recorder\Windows`. Prototyp i wcześniejsze badania: `C:\temp\grok\recorder`. Działający Recorder nie wymaga katalogu prototypu. Nie przenoś jego historycznej specyfikacji wprost do aplikacji: część opisuje zamiary, a nie wdrożone zachowanie.

## Uzgodnienia z użytkownikiem

- Priorytetem jest tłumaczenie innych rozmówców z wyjścia audio komputera. Mikrofon jest dodatkowym, niezależnie włączanym źródłem.
- Używamy urządzeń już wybranych w Recorderze. Osobne menu i konfigurowalny skrót uruchamiają tłumaczenie; domyślnie Ctrl+Alt+T.
- Napisy mają się poprawiać podczas mówienia. Czekanie wyłącznie na długą pauzę było dla użytkownika niepraktyczne. Nie obiecujemy dosłownego tłumaczenia każdego słowa bez opóźnienia.
- Małe, przesuwalne okno zawsze na wierzchu ma konfigurowalną czcionkę, kolory, tło i widoczność oryginału.
- Historia przechowuje finalny EN i PL lokalnie. Tłumaczenia nie trafiają do RecoWilk ani kolejki uploadu nagrań. Sam tłumacz nie zapisuje audio. Dotychczasowy eksport nagrań pozostaje osobną funkcją.
- Wszystkie projekty przenieśliśmy do .NET 10 na wyraźną prośbę użytkownika. Nie wracaj do wcześniejszego pomysłu App na .NET 8 i Worker na .NET 10.
- Dostarczone transkrypcje przyjęto za wzorzec roboczy za zgodą użytkownika. Nie wymagaj od niego ręcznego przepisywania całego zbioru. To nie oznacza, że wzorzec zweryfikowano odsłuchem.
- 2026-11-02 użytkownik zgłosił „wszystko działa”. To potwierdzenie użytkowe; nie zastępuje protokołu wszystkich scenariuszy sprzętowych ani wielogodzinnego benchmarku.

## Decyzje i uzasadnienie

| Decyzja | Dlaczego / ograniczenia |
|---|---|
| Osobny proces tłumaczący na źródło | Awaria natywnego silnika/GPU nie powinna zamknąć nagrywania. Procesy mają osobny kontekst, ale nadal współdzielą zasoby komputera. Jedna aplikacja i jeden instalator z punktu widzenia użytkownika. |
| Lokalny Whisper small.en Q8, opcjonalnie large-v3-turbo Q8; Vulkan | Profile już sprawdzone w prototypie. Turbo kosztuje więcej pamięci i obliczeń. Nie zmieniaj checkpointu ani kwantyzacji tylko na podstawie nazwy lub ogólnego rankingu. |
| Marian ONNX int8 na CPU i Silero VAD | Zachowujemy przetestowany potok. „Marian” to także nazwa architektury; konkretny checkpoint to `jproboszcz/opus-mt-en-pl`, a nie model Allegro BiDi. |
| Pozostawienie dotychczasowego Mariana | Porównanie z BiDi nie wykazało jednoznacznej przewagi uzasadniającej zmianę. To nie dowód, że Marian jest najlepszy albo bezbłędny. Szczegóły porównania poniżej. |
| Rosnący fragment audio, odświeżane drafty i finalizacja frazy | Kolejne słowa mogą zmienić wcześniejsze tłumaczenie. Wyświetlamy poprawiany draft; utrwalamy wyłącznie finał. Nie doklejamy wszystkich wersji do historii. |
| Własny katalog tłumaczeń, DPAPI CurrentUser | Oddzielenie historii od uploadu i od MP3. Każda finalna fraza jest zapisana atomowo; awaria nie powinna usuwać wcześniejszych fraz. DPAPI wiąże pliki z kontem Windows. |
| Marian w instalatorze, Whisper/Silero pobierane przy pierwszym użyciu | Testowany Marian ONNX to lokalny eksport, nie pliki dostępne pod URL upstream. Nie wymyślaj adresu pobierania tego eksportu i nie podmieniaj go plikami PyTorch. |
| Rozmiary i SHA-256 przypięte w kodzie/manifestach | Nie uruchamiamy niepełnego lub innego modelu pod tą samą nazwą. Downloader pomija poprawne pliki i publikuje nowe dopiero po weryfikacji. |

## Błędy, których nie należy odtwarzać

- **Draft blokował finał.** Commit anuluje trwający preview, a preview ma budżet czasu (3 s small / 5 s turbo w 1.5.1). Oczekujący draft można zastąpić nowszym; finału nie wolno po cichu zgubić.
- **Anulowanie natywnego ASR nie oznacza natychmiastowego końca pracy.** Procesor Whisper jest domykany przez `await using` przed kolejnym wywołaniem. Nie uruchamiaj następnej inferencji na nadal pracującym zasobie.
- **Loopback może nie dostarczać pakietów podczas ciszy.** `CaptureIdleBoundary` domyka frazę po bezczynności bez dopisywania fikcyjnych próbek PCM i wydłużania osi audio.
- **Wielozdaniowy EN może powodować pominięcia MT.** Zachowaj `EnglishSentenceSplitter` i przypadki regresji. Nie dopisuj globalnie kropek: wcześniejsze próby dawały też regresje.
- **Dobry PL może wiernie tłumaczyć błędny EN.** Oceniaj oddzielnie ASR i MT. Sprawdzaj zwłaszcza negacje, liczby, nazwy i pominięcia. Płynność tekstu nie jest miarą zgodności z rozmową.
- **Beam 1/5/10 to ustawienie przeszukiwania dekodera, nie inne modele.** Większy beam nie gwarantuje poprawniejszego wyniku i może zwiększać opóźnienie; zmiany wymagają pomiaru na tym samym materiale.
- **Publikacja do starego folderu mieszała artefakty.** `publish.ps1` zachowuje poprzedni folder osobno i tworzy nowy. Natywne zależności silnika pozostają w `TranslationWorker`; referencja App → Worker nie kopiuje jego zależności do katalogu głównego App.
- **Ścieżka modeli zakończona separatorem była błędnie odrzucana.** Jest test regresyjny. Nie zastępuj normalizacji prostym doklejaniem kolejnego separatora.
- **Anulowanie pobierania nie jest wznawianiem bajtowym.** Ukończone modele pozostają; przy ponowieniu nieukończony plik jest pobierany od początku. Nie opisuj tego jako HTTP resume.

## Mapa kodu

Ścieżki względem katalogu repozytorium:

| Obszar | Główne pliki |
|---|---|
| Start/stop, źródła, hotkey, sterowanie sesją | `src/MeetingAudioRecorder.App/Services/TranslationController.cs` |
| Okna napisów, konfiguracja, historia, pobieranie | `src/MeetingAudioRecorder.App/Views/Translation*.xaml*`, `TranslationsWindow.xaml*` |
| Ustawienia i kontrakty | `src/MeetingAudioRecorder.Core/Models/TranslationModels.cs` |
| Lokalny protokół named pipe i proces potomny | `src/MeetingAudioRecorder.Infrastructure/Translation/LocalTranslationWorker.cs` |
| Szyfrowany zapis finałów i odczyt po awarii | `src/MeetingAudioRecorder.Infrastructure/Translation/TranslationStore.cs` |
| Pobieranie i przygotowanie paczki Mariana | `TranslationModelDownloader.cs`, `BundledTranslationModels.cs` w tym samym folderze |
| Audio → VAD → Whisper → Marian | `src/MeetingAudioRecorder.TranslationWorker/LiveSession.cs`, `EndpointCapture.cs`, `MarianProbe.cs` i klasy segmentera/kolejek |
| Przypięte pliki | `src/MeetingAudioRecorder.TranslationWorker/marian-pins.json`, `ModelFiles.cs` oraz manifest downloadera |
| Izolowane próby bez produkcyjnego startu/uploadu | `src/MeetingAudioRecorder.App/Services/TranslationDiagnostics.cs` |
| Testy regresji | `tests/MeetingAudioRecorder.Core.Tests/Translation*`, `tests/MeetingAudioRecorder.Audio.Tests/Translation*` |

Dane użytkownika: `%LOCALAPPDATA%\MeetingAudioRecorder\Models` i `Translations`. Nie zapisuj treści prywatnych rozmów do raportów repozytorium ani logów procesu pomocniczego.

## Budowanie i kontrola

- `scripts/verify.ps1`: format, Release, testy. Ostatnia brama implementacji 1.5.1: 213 przeszło, 1 test Media Foundation opt-in pominięty. To wynik konkretnego uruchomienia, nie stała oczekiwana liczba testów.
- `scripts/build-installer.ps1`: czysta publikacja self-contained i Inno Setup. Nie trzeba instalować SDK .NET na komputerze odbiorcy. Potrzebny jest zgodny GPU/sterownik dla wybranego silnika.
- Build instalatora wymaga pięciu plików eksportu Mariana zgodnych z manifestem: domyślnie `%LOCALAPPDATA%\MeetingAudioRecorder\Models\marian`, alternatywnie zmienna `RECORDER_MARIAN_MODELS`. Same źródła Git nie zawierają wag. Brak tych plików ma zatrzymać publikację.
- Instalator 1.5.1 ma około 355 MB. Pierwsze przygotowanie pobiera do około 267 MB (small) albo 877 MB (turbo); istniejące poprawne modele są pomijane. Po przygotowaniu tłumaczenie działa offline.
- Diagnostyki `--translation-smoke`, `--translation-ui-smoke`, `--translation-model-smoke` omijają produkcyjny startup. Argumenty sprawdź w `TranslationDiagnostics.cs`; używaj osobnego folderu `out/`.
- Raport dwóch workerów z WAV nie jest testem dwóch fizycznych urządzeń. Nadal rozróżniaj testy automatyczne, ocenę użytkownika i udokumentowane próby sprzętowe.
- Przed uruchomieniem instalatora lub kończeniem procesów sprawdź, czy Recorder nagrywa lub wysyła materiał. Nie kończ wszystkich procesów po samej nazwie na potrzeby testu.

## Wcześniejsze badania — gdzie szukać zamiast powtarzać

Na tym komputerze, w `C:\temp\grok\recorder`:

- `docs/research/marian-bidi-2026-11-01.md`: porównanie dwóch checkpointów na tych samych 64 wejściach EN, 128 ocenionych tłumaczeń segmented. Ocena asystenta była niezaślepiona, bez niezależnego polskiego wzorca; runtime float32 nie jest dowodem zgodności z ONNX int8 ani pomiarem opóźnienia aplikacji.
- `docs/research/porownanie-modeli-i-plan.md` i `wyniki-rundy-1.md`: kandydaci i wcześniejsze porównania.
- `docs/research/potok-replay-2026-10-31.md`: potok i ograniczenia replay.
- `tools/export-marian/` i `docs/results/marian-export-manifest.json`: pochodzenie lokalnego eksportu, narzędzia i manifest. Nie zakładaj, że ponowny eksport inną wersją bibliotek da identyczne hashe. Pochodzenie/licencję modelu oceniaj na podstawie tych materiałów; uruchomienie eksportu nie jest ich potwierdzeniem.
- Materiał użytkownika: `C:\temp\grok\recorder-live-nagrania\export-en-meetings`. Tylko do lokalnych testów zgodnie z uzgodnieniem; nie do publikacji w repozytorium.

Jeśli tych katalogów nie ma na innym komputerze, zaznacz brak materiałów źródłowych. Nie dopisuj fikcyjnych wyników. Nowe badanie uzasadniaj konkretnym problemem: np. utrata negacji albo opóźnienie finału, a nie samym pojawieniem się kolejnego modelu.

## Zamykanie napisów — poprawka R-023 (2026-11-07)

Stop natychmiast chowa okno i zleca zatrzymanie tłumaczenia; zakończenie zapisu pozostaje asynchroniczne. Po zakończeniu lub błędzie przycisk zmienia się na Zamknij. Alt+F4 zatrzymuje aktywne tłumaczenie, a Ukryj nadal tylko chowa napisy. Testy WPF odtwarzają zachowanie przed poprawką. Zgłoszonego błędu źródła audio nie odtworzono w krótkiej próbie obu urządzeń WH-1000XM4 z silnikiem zainstalowanej wersji 1.5.0; nie uznawaj go za naprawiony bez pełnego komunikatu i diagnozy.

Uzupełnienie R-023: użytkownik podał HRESULT 0x88890004 (AUDCLNT_E_DEVICE_INVALIDATED). Dodano maksymalnie dwa ponowienia tego samego endpointu co 500 ms, tylko przed pierwszym odebranym audio, oraz opis błędu z nazwą źródła i wskazówką ponownego uruchomienia. Nie wznawiamy w środku frazy ani nie przełączamy urządzenia automatycznie. Kod nie rozstrzyga, czy przyczyną było odłączenie czy rekonfiguracja Bluetooth. Pełna brama: 223 testy przeszło, 1 MF opt-in pominięty; fizyczne odłączenie pozostaje do testu. Instalacja nadal 1.5.0.

Wydanie 1.5.2 z poprawkami R-023 zbudowane 2026-11-07; instalator w publish/installer. Pełna weryfikacja i suma kontrolna w REPAIR_PLAN.md. Budowa pakietu nie oznacza aktualizacji działającej instalacji.

## Widoczność pracy — R-024, wersja 1.5.3

Zastępuje zachowanie Stop z R-023: okno pozostaje widoczne podczas kończenia, pokazuje komunikat i animację; Zamknij jest dostępne po zakończeniu. Ukryj nadal chowa okno na żądanie. Przygotowanie jest widoczne już podczas sprawdzania modeli. Powtórny skrót/menu podczas przygotowania lub kończenia przywołuje stan bieżącej sesji. Nie uruchamia nowej sesji ani nie kolejkuje restartu; po komunikacie zakończenia można uruchomić tłumaczenie ponownie. Nie skrócono czasu pracy natywnego silnika.
