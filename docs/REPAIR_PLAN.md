# Plan naprawy Meeting Audio Recorder

## Cel wydania

Doprowadzić aplikację do stanu, w którym krótkie i wielogodzinne nagrania zachowują synchronizację, awaria procesu nie powoduje utraty zarejestrowanych próbek, a operacje kończenia i przetwarzania są bezpieczne dla UI i dysku.

## Definicja ukończenia

- Wszystkie automatyczne testy przechodzą w CI na Windows.
- Każdy naprawiony defekt ma test regresyjny.
- Recovery działa po wymuszonym zabiciu procesu.
- Nagranie 60 minut nie ma słyszalnych przerw ani przesunięcia większego niż 100 ms.
- Test 4 godzin mieści się w limicie dryfu 250 ms.
- Wyjście, odłączenie urządzenia i brak miejsca nie blokują UI ani nie kasują materiału.
- Checklistę sprzętową wykonano dla urządzenia przewodowego i Bluetooth.

## Kolejność realizacji

### P0 — bezpieczeństwo i poprawność

#### R-001: deterministyczna oś czasu loopback

Status: implementacja i testy automatyczne zakończone; test sprzętowy oczekuje na wykonanie.

- Oddziel wykrywanie braku callbacków od zapisywania aktywnego pakietu.
- Modeluj pozycję zapisu w ramkach audio.
- Nie dopisuj ciszy za okres reprezentowany przez nadchodzący bufor.
- Usuń zależność wyniku od wyścigu timer–callback.
- Dodaj testy regularnych pakietów, luk, wyścigu timer–callback oraz 44,1/48 kHz.

Akceptacja: syntetyczny test 10 minut daje długość z tolerancją jednej ramki bufora.

#### R-002: recovery plików po przerwaniu procesu

Status: implementacja i testy automatyczne zakończone; test `Stop-Process -Force` oczekuje na wykonanie.

- Wprowadź wersjonowany manifest sesji z formatem, startem, urządzeniami i stanem.
- Zapewnij format tymczasowy możliwy do naprawienia po braku `Dispose`.
- Waliduj i naprawiaj nagłówek RIFF/WAV przed miksowaniem.
- Dodaj testy błędnego rozmiaru RIFF/data, jednej ścieżki, obciętej ramki i idempotencji.

Akceptacja: materiał jest odzyskiwany po `Stop-Process -Force` w trakcie nagrania.

#### R-003: asynchroniczne wyjście bez deadlocka

Status: implementacja i testy automatyczne zakończone; test interaktywny tray oczekuje na wykonanie.

- Usuń synchroniczne oczekiwanie w `TrayViewModel.Exit`.
- Zablokuj wielokrotne wyjście i pokaż stan zapisywania.
- Użyj asynchronicznego dispatchera dla zdarzeń z workerów.
- Zamknij aplikację po zakończeniu zapisu albo jawnej decyzji o zachowaniu temp.

Akceptacja: wyjście podczas nagrania nie blokuje UI i zapisuje działający MP3.

### P1 — odporność operacyjna

#### R-004: budżet i monitoring miejsca na dysku

Status: implementacja i testy automatyczne zakończone; kontrolowany test pełnego woluminu oczekuje na wykonanie.

- Użyj estymacji zależnej od formatów źródeł i czasu.
- Monitoruj wolne miejsce podczas capture.
- Zarezerwuj miejsce na miks i MP3 albo przetwarzaj strumieniowo.
- Przy krytycznie małej przestrzeni zakończ capture i zachowaj możliwe do odzyskania pliki.

#### R-005: korekcja dryfu

Status: implementacja i testy automatyczne zakończone; 4-godzinny test sprzętowy oczekuje na wykonanie.

- Mierz ramki obu źródeł względem wspólnego zegara monotonicznego.
- Zdefiniuj próg korekcji oraz ogranicz jej tempo.
- Unikaj gwałtownych insert/drop słyszalnych jako kliknięcia.

Akceptacja: dryf po 4 godzinach nie przekracza 250 ms; celem jest 100 ms.

#### R-006: niezmienny snapshot sesji

Status: implementacja i testy automatyczne zakończone; test zmiany ustawień i urządzeń w UI oczekuje na wykonanie.

- Zapisz ustawienia miksu, ścieżki i aktywne ID urządzeń w sesji przy starcie.
- Monitoring urządzeń porównuj z `CurrentSession`.
- Zmiany ustawień stosuj od następnego nagrania.

### P2 — ergonomia i utrzymanie

#### R-007: transakcyjna zmiana hotkeya

Status: implementacja i testy automatyczne zakończone; test konfliktu z rzeczywistym `RegisterHotKey` oczekuje na wykonanie.

- Rejestruj nowy skrót z możliwością rollbacku.
- Zapisz ustawienia dopiero po sukcesie.
- Przy błędzie zachowaj poprzedni aktywny skrót i konfigurację.

#### R-008: anulowanie przetwarzania

Status: implementacja i testy automatyczne zakończone; interaktywny test anulowania Media Foundation oczekuje na wykonanie.

- Sprawdzaj token w pętli zapisu miksu.
- Opisz ograniczenia anulowania Media Foundation.
- Nie publikuj częściowego MP3 po anulowaniu.

Media Foundation pozostaje API synchronicznym. Token jest sprawdzany na granicy każdego odczytu PCM oraz przed publikacją, ale finalizacja wewnętrznego kontenera może opóźnić reakcję. Po anulowaniu plik `.partial` jest usuwany, natomiast źródłowe WAV, manifest i roboczy miks pozostają do ponowienia lub recovery.

#### R-009: testy systemowe i diagnostyka

Status: implementacja zakończona; opt-in test Media Foundation wykonany pomyślnie, testy wymagające fizycznych urządzeń pozostają do wykonania.

- Dodaj testy manifestu/recovery i osi czasu bez sprzętu.
- Dodaj opt-in testy Windows dla Media Foundation.
- Rozszerz testy manualne o markery synchronizacji i protokół wyników.
- Loguj format, ramki, luki, korekty dryfu i rozmiary; nigdy próbki audio.

#### R-010: otwieranie okna ustawień

Status: poprawka i test regresyjny zakończone; wymagany test interaktywny z zainstalowanej wersji 1.0.1.

- Ustaw wiązanie właściwości tylko do odczytu `HotkeyPreview` jawnie jako `OneWay`.
- Dodaj test kontraktu XAML wykrywający ponowne użycie domyślnego wiązania `TwoWay`.

Akceptacja: kliknięcie **Ustawienia** z menu tray i dwuklik ikony otwierają okno bez wyjątku dispatchera.

#### R-011: kodowanie atomowego pliku MP3

Status: poprawka i test Media Foundation zakończone; wymagane odzyskanie zachowanych sesji i test nagrania z wersji 1.0.2.

- Koduj do roboczej nazwy zakończonej rozszerzeniem `.mp3`, wymaganym przez Media Foundation.
- Po walidacji przenieś wynik do pliku `.partial`, który koordynator publikuje atomowo.
- Przy błędzie lub anulowaniu usuń oba pliki robocze, zachowując źródłowe WAV, miks i manifest.

Akceptacja: kodowanie do ścieżki `*.mp3.partial` tworzy czytelny MP3, a źródła są usuwane dopiero po publikacji wyniku.

#### R-012: automatyczne nagrywanie spotkań Google Meet

Status: implementacja aplikacji 1.1.0, testy automatyczne i budowa instalatora zakończone; prawdziwy login OAuth oraz spotkanie Google Meet oczekują na wykonanie.

- Używaj Google Calendar wyłącznie do wskazania kandydatów zawierających link Google Meet.
- Rozpoczynaj nagrywanie dopiero po potwierdzeniu, że konto połączone z aplikacją jest aktywnym uczestnikiem konferencji.
- Zatrzymuj wyłącznie nagranie rozpoczęte przez automat i tylko po potwierdzonym wyjściu użytkownika.
- Błędy autoryzacji, limity API, timeouty i brak sieci traktuj jako stan niepewny; nie mogą samodzielnie zatrzymać nagrania.
- Nie zapisuj listy uczestników, tokenów OAuth ani opisu wydarzenia w logach.
- Token odświeżania przechowuj lokalnie, zaszyfrowany dla bieżącego użytkownika Windows.
- Zachowaj ręczny start/stop jako niezależny i nadrzędny sposób sterowania.

Akceptacja: samo rozpoczęcie wydarzenia nie uruchamia capture; wejście właściwego użytkownika uruchamia jedną sesję, potwierdzone wyjście zatrzymuje tę samą sesję, chwilowa utrata API jej nie zatrzymuje, a sesja ręczna nigdy nie jest przejmowana przez automat.

#### R-013: urządzenia audio aktywnej przeglądarki

Status: implementacja i testy deterministyczne zakończone w aplikacji 1.1.1; test z fizycznymi endpointami Chrome/Edge/Firefox oczekuje na wykonanie.

- Przy automatycznym starcie skanuj aktywne sesje Core Audio na endpointach Capture i Render.
- Obsługuj procesy Chrome, Edge, Firefox, Brave, Opera i Vivaldi.
- Użyj aktywnej sesji mikrofonowej do wyboru rodziny przeglądarki, a wyjście wybierz z tej samej rodziny.
- Przy wielu niemych sesjach preferuj zapisany endpoint, następnie domyślny komunikacyjny; przy braku detekcji zachowaj zapisane urządzenie.
- Nie nadpisuj ustawień wykrytymi endpointami i nie przełączaj urządzeń po rozpoczęciu sesji.
- Ręczny start nie używa detekcji przeglądarki.

Akceptacja: automatyczna sesja używa endpointów aktywnej przeglądarki, zapisuje je w snapshotcie i pokazuje w powiadomieniu; częściowy lub całkowity brak detekcji bezpiecznie korzysta z zapisanych ustawień.

#### R-014: linki Google Meet bez wydarzenia Calendar

Status: implementacja aplikacji 1.2.1, rozszerzenia Meeting Orgniazer Gemini i testy deterministyczne zakończone; naprawiono jednoznaczne tworzenie klientów Google OAuth przez DI; instalacja rozszerzenia oraz prawdziwy link Meet oczekują na test manualny 5B.

- Dostarcz rozszerzenie Manifest V3 dla Chrome/Edge działające wyłącznie na `meet.google.com`.
- Przekazuj przez Native Messaging jedynie kod spotkania i nazwę przeglądarki; token OAuth pozostaje wyłącznie w aplikacji.
- Rejestruj per-user host `com.meetingorganizer.gemini` z allowlistą stałego ID rozszerzenia.
- Zapisuj stan atomowo w profilu użytkownika i odrzucaj dane starsze niż 90 sekund, niepoprawne kody oraz nadmiarowe wpisy.
- Traktuj link jako kandydata; faktyczny start nadal wymaga potwierdzenia obecności bieżącego konta przez Meet API.
- Sygnał rozszerzenia ma wybudzać sprawdzenie bez zwiększania częstotliwości odpytywania Calendar.
- Brak, zamknięcie lub awaria rozszerzenia nie może samodzielnie zatrzymać trwającego nagrania.
- Udostępnij pakiet z Ustawień; jawnie opisz wymagany ręczny krok `Załaduj rozpakowane`, dopóki rozszerzenie nie trafi do sklepów.

Akceptacja: otrzymany link Meet bez wpisu Calendar rozpoczyna nagrywanie dopiero po faktycznym dołączeniu połączonego konta, zamknięcie karty bez wiarygodnej odpowiedzi API nie zatrzymuje materiału, a rozszerzenie nie uzyskuje dostępu do tokenów ani historii przeglądania.

## Strategia commitów

1. `test: reproduce loopback timeline duplication`
2. `fix: make loopback timeline frame-based`
3. `test: cover interrupted wav recovery`
4. `fix: add crash-safe session recovery`
5. `fix: make recording shutdown asynchronous`
6. Osobne commity dla kolejnych zadań P1/P2.

Każdy commit musi przejść `.\scripts\verify.ps1`. Testy sprzętowe zapisuj w dzienniku walidacji.

## Dziennik walidacji

| Data | Zadanie | Środowisko | Wynik | Dowód/uwagi |
|---|---|---|---|---|
| 2026-07-26 | Baseline | .NET 8, Windows | 36/36 testów, build 0 ostrzeżeń | Analiza statyczna; bez testu urządzeń |
| 2026-07-26 | R-001 | Testy syntetyczne 44,1/48 kHz | 41/41 testów; 10 min z dokładnością do ramki | Usunięto spekulacyjny timer; wymagany test WASAPI na urządzeniu |
| 2026-07-26 | R-002 | Zerwany WAV float 48 kHz stereo | 45/45 testów; odzysk 250 ms, idempotencja | Oryginał niezmieniony; wymagany test zabicia procesu |
| 2026-07-26 | R-003 | Polityka zamknięcia i dispose | 55/55 testów; build bez ostrzeżeń | Brak synchronicznego wait/dispatcher; wymagany test tray |
| 2026-07-26 | R-004 | Budżet capture/processing | 59/59 testów; 48 kHz, MP3, osobne WAV | Monitoring co 5 s; wymagany test małego woluminu |
| 2026-07-26 | R-005 | Dryf zegara i ułamkowy resampling | 67/67 testów; próg 50 ms, limit 1000 ppm | Korekcja płynna względem monotonicznego czasu sesji; wymagany test 4 h |
| 2026-07-26 | R-006 | Snapshot ustawień sesji i manifest v2 | 69/69 testów; build bez ostrzeżeń | Miks, zapis, monitoring dysku i recovery używają snapshotu; wymagany test UI/urządzeń |
| 2026-07-26 | R-007 | Transakcja ustawień i hotkeya | 73/73 testy; rollback błędu rejestracji/zapisu | Poprzedni skrót i konfiguracja pozostają aktywne; wymagany test WinAPI |
| 2026-07-26 | R-008 | Anulowanie miksu i kodowania | 76/76 testów; granice buforów WAV/PCM | Brak publikacji częściowego MP3, źródła zachowane; wymagany test Media Foundation |
| 2026-07-26 | R-009 | Diagnostyka i test Windows Media Foundation | 77 testów domyślnych + 1 opt-in MF; build bez ostrzeżeń | Opt-in MF przeszedł; audyt NuGet bez znanych podatności; testy WASAPI/BT/4 h nadal manualne |
| 2026-07-26 | R-010 | Kontrakt XAML okna ustawień | Test regresyjny wymusza `Mode=OneWay` dla `HotkeyPreview` | Wymagane potwierdzenie otwarcia ustawień z tray po instalacji 1.0.1 |
| 2026-07-26 | R-011 | Windows 11, Media Foundation, `*.mp3.partial` | Test przed poprawką odtwarzał wyjątek sink writera; po poprawce tworzy czytelny MP3 | Zachowane źródła dwóch sesji użytkownika; wymagany test recovery i nagrania w 1.0.2 |
| 2026-08-08 | R-012, aplikacja 1.1.0 | .NET 8, WPF, Windows DPAPI, OAuth PKCE, podstawiony HTTP bez sieci i urządzeń | 7/7 kontrolera, 4/4 usługi tła, 2/2 kontraktu UI oraz 15/15 token/OAuth/Calendar/Meet; build Release 0 ostrzeżeń; instalator 1.1.0 zbudowany | Pełna brama: 102 testy przeszły, 1 test MF opt-in pominięty, 3 istniejące testy koordynatora niewykonalne w sandboxie z powodu zakazu zapisu do `%LocalAppData%`; `MeetingAudioRecorder-Setup-1.1.0.exe`, SHA-256 `424CD1D2FBA815371C9841693EBEE56DDB017E41BA741D81C85A3CF42608B279`; wymagany prawdziwy login OAuth i Meet |
| 2026-08-08 | R-013, aplikacja 1.1.1 | .NET 8, Windows Core Audio modelowane bez sprzętu | 5/5 selektora i snapshotu oraz 5/5 usługi automatyzacji; build Release 0 ostrzeżeń; instalator 1.1.1 zbudowany | Pełna brama: 108 testów przeszło, 1 test MF opt-in pominięty, te same 3 istniejące testy koordynatora niewykonalne w sandboxie z powodu zakazu zapisu do `%LocalAppData%`; `MeetingAudioRecorder-Setup-1.1.1.exe`, SHA-256 `2B9AE8805878946499787888246B38EB15C14225DEB18E6C6CB9B8AFE019FAA0`; wymagany test 5A z fizycznymi endpointami Chrome/Edge/Firefox |
| 2026-08-08 | R-014, aplikacja 1.2.0 | .NET 8, Manifest V3, Native Messaging, Chrome/Edge modelowane bez prawdziwego konta Meet | 15/15 testów automatyzacji, parsera, manifestów i UI; składnia JS poprawna; build Release 0 ostrzeżeń; instalator Inno Setup zbudowany | Pełna brama: 116 testów przeszło, 1 test MF opt-in pominięty, te same 3 istniejące testy koordynatora niewykonalne w sandboxie z powodu zakazu zapisu do `%LocalAppData%`; `MeetingAudioRecorder-Setup-1.2.0.exe`, 73 708 519 B, SHA-256 `C39A387C08DF680B02615964EF9FC04FE7E4B24D0B1B4B9EC46A09CD5D515B3B`; wymagany test manualny 5B z instalacją rozszerzenia i prawdziwym Meet |
| 2026-08-08 | R-014 hotfix, aplikacja 1.2.1 | .NET 8, typowani klienci `HttpClient` i kontener DI | 2/2 nowe testy regresyjne odtwarzające wyjątki konstruktorów; 9/9 testów OAuth/token; build Release 0 ostrzeżeń; instalator Inno Setup zbudowany | Pełna brama: 118 testów przeszło, 1 test MF opt-in pominięty, te same 3 istniejące testy koordynatora niewykonalne w sandboxie; `MeetingAudioRecorder-Setup-1.2.1.exe`, 73 724 728 B, SHA-256 `570112B723FBEAF9DEEE41664D6837FF584D567988A9A0749F425AFC3975A808`; oba produkcyjne konstruktory Google wskazane jednoznacznie dla DI |
| 2026-08-08 | Audyt dokumentacji i rozwiązania | `.sln`, README, workflow AI, testy manualne | 7/7 projektów obecnych w `MeetingAudioRecorder.sln`; brak odwołań do usuniętego alternatywnego formatu i uszkodzonych lokalnych linków Markdown; wersje App/instalatora zgodne: 1.2.1 | Usunięto pusty alternatywny plik rozwiązania; pełna brama: 118 testów przeszło, 1 test MF opt-in pominięty, te same 3 testy koordynatora zablokowane przez zakaz zapisu sandboxa do `%LocalAppData%`; bez zmian kodu i audio |
