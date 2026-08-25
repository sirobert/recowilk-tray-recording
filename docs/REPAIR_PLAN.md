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

Status: implementacja aplikacji 1.2.6, rozszerzenia Meeting Orgniazer Gemini i testy deterministyczne zakończone; naprawiono jednoznaczne tworzenie klientów Google OAuth przez DI, zgodność maski `signedinUser` z Meet API oraz blokowanie nowych linków przez zakończone lub niedostępne (`403/404`) śledzone spotkanie; ponowny test prawdziwego linku Meet oczekuje na wykonanie scenariusza 5B.

- Dostarcz rozszerzenie Manifest V3 dla Chrome/Edge działające wyłącznie na `meet.google.com`.
- Przekazuj przez Native Messaging jedynie kod spotkania i nazwę przeglądarki; token OAuth pozostaje wyłącznie w aplikacji.
- Rejestruj per-user host `com.meetingorganizer.gemini` z allowlistą stałego ID rozszerzenia.
- Zapisuj stan atomowo w profilu użytkownika i odrzucaj dane starsze niż 90 sekund, niepoprawne kody oraz nadmiarowe wpisy.
- Traktuj link jako kandydata; faktyczny start nadal wymaga potwierdzenia obecności bieżącego konta przez Meet API.
- Sygnał rozszerzenia ma wybudzać sprawdzenie bez zwiększania częstotliwości odpytywania Calendar.
- Brak, zamknięcie lub awaria rozszerzenia nie może samodzielnie zatrzymać trwającego nagrania.
- Po zakończeniu nagrania i potwierdzonym opuszczeniu śledzonego spotkania zwalniaj je, aby nowy aktywny link mógł zostać sprawdzony bez restartu aplikacji.
- Po `403` lub `404` zwalniaj niedostępne śledzone spotkanie tylko wtedy, gdy żadne nagranie nie jest aktywne; następnie kontynuuj sprawdzanie świeżych linków rozszerzenia.
- Udostępnij pakiet z Ustawień; jawnie opisz wymagany ręczny krok `Załaduj rozpakowane`, dopóki rozszerzenie nie trafi do sklepów.

Akceptacja: otrzymany link Meet bez wpisu Calendar rozpoczyna nagrywanie dopiero po faktycznym dołączeniu połączonego konta, kolejne spotkanie uruchamia automat bez restartu po opuszczeniu poprzedniego, zamknięcie karty bez wiarygodnej odpowiedzi API nie zatrzymuje materiału, a rozszerzenie nie uzyskuje dostępu do tokenów ani historii przeglądania.

#### R-015: bezpieczne zatrzymanie WASAPI po utracie urządzenia

Status: implementacja i testy automatyczne zakończone; test fizycznego odłączenia endpointów oczekuje na wykonanie.

- Zastąp ścieżkę capture, w której wyjątek `AudioClient.Stop()` może opuścić wątek roboczy.
- Zachowaj błąd odczytu i błąd zatrzymania oraz przekaż je przez `RecordingStopped`/`CaptureError`.
- Domknij writer WAV i zachowaj manifest oraz obie ścieżki źródłowe do recovery.
- Dodaj test deterministyczny bez urządzeń dla `AUDCLNT_E_DEVICE_INVALIDATED` (`0x88890004`).
- Wykonaj manualny test odłączenia aktywnego urządzenia renderującego i mikrofonu.

Akceptacja: odłączenie endpointu podczas nagrywania kończy sesję z błędem i zachowuje materiał, ale nie kończy procesu aplikacji.

#### R-016: kończenie Native Messaging hosta po zamknięciu przeglądarki

Status: implementacja aplikacji 1.2.5 i testy deterministyczne zakończone; test z rzeczywistym restartem service workera Chrome/Edge oczekuje na wykonanie.

- Traktuj EOF na stdin jako koniec połączenia Native Messaging i zakończ proces hosta kodem 0.
- Nie przetwarzaj ponownie poprzedniego nagłówka wiadomości po zamknięciu pipe przez przeglądarkę.
- Przed aktualizacją zakończ osierocone procesy `MeetingAudioRecorder.BrowserBridge.exe` ze starszych wersji.
- Nie zatrzymuj ani nie modyfikuj głównej sesji nagrywania przy zamknięciu mostu.

Akceptacja: po zamknięciu portu Native Messaging proces hosta kończy się w ciągu 2 sekund bez aktywnej pętli CPU, a instalator może zastąpić plik bez ręcznego zamykania osieroconych instancji.

#### R-017: niezawodna wysyłka gotowych nagrań do RecoWilk

Status: implementacja lokalna R-017A–R-017G oraz automatyczna część R-017H zakończone 2026-08-25. Klient ma typowany kontrakt, szyfrowaną i migrowalną kolejkę v2 z tenant binding, trwałą maszynę stanów, obsługę `410`/`422`, walidację chunków, klasyfikację retry, transakcyjne ustawienia i idempotentny shutdown. Pełna brama: 149 testów przeszło, 1 test Media Foundation opt-in pominięty, build Release 0 ostrzeżeń. Przed wydaniem pozostaje manualny test E2E z testową instancją RecoWilk.

- Uruchamiaj integrację dopiero po atomowym opublikowaniu finalnego MP3.
- Zachowuj MP3 lokalnie niezależnie od wyniku wysyłki.
- Przechowuj klucz API przez DPAPI i nie zapisuj go w ustawieniach ani logach.
- Zapisuj trwały wpis kolejki przed pierwszą próbą sieciową; po restarcie wznawiaj brakujące chunki.
- Identyfikuj import przez stabilne `recordingId`, aby retry nie tworzył drugiego spotkania.
- Pobieraj opis i uczestników Calendar bez umieszczania ich w logach.
- Zezwalaj na HTTP wyłącznie dla loopback/localhost; pozostałe serwery wymagają HTTPS.

##### Jawny kontrakt klient–serwer v1

Wszystkie poniższe endpointy są względne wobec skonfigurowanego bazowego URL RecoWilk. Klient wysyła `Authorization: ApiKey <klucz>`. Poza loopback wymagany jest HTTPS. Błędy domenowe mają ciało `application/problem+json` z polami co najmniej `title`, `detail`, `status` i `traceId`; błędy uwierzytelnienia zawierają `title`, `detail` i opcjonalnie `traceId`.

**Test klucza i związanie z tenantem**

- Właściwy endpoint: `GET /api/v1/ingest/ping`.
- `200 OK` oznacza poprawny, aktywny i niewygasły klucz z zakresem `meeting.ingest` oraz aktywnego właściciela spotkań. Odpowiedź: `{ "status":"ok", "apiVersion":"v1", "organizationId", "apiKeyId", "meetingOwnerId" }`.
- `401` oznacza klucz brakujący, błędny, wygasły, unieważniony albo nieaktywnego właściciela; `403` — brak zakresu; `429` — limit wywołań. Żaden inny kod nie oznacza poprawnego testu.
- `meetingId` i `uploadId` należą do konkretnej bazy/instancji RecoWilk, organizacji oraz efektywnego `meetingOwnerId`. Nie wolno używać ich po zmianie bazowego URL, organizacji lub odpowiedzialnego użytkownika. Dostęp nie jest przywiązany do samego `apiKeyId`: po rotacji albo przez inny klucz tej samej organizacji i tego samego właściciela istniejące identyfikatory pozostają dostępne.
- Wpis kolejki powinien pamiętać bazowy URL oraz wartości zwrócone przez `ping`. Zmiana któregokolwiek z nich wymaga wyzerowania zapisanych `meetingId`/`uploadId` i ponownego rozpoczęcia od stabilnego `externalId`.

**Utworzenie lub odzyskanie spotkania**

- `POST /api/v1/ingest/meetings`; stabilne `externalId` jest właściwym kluczem idempotencji i ma 1–300 znaków. Recorder używa `recorder:<recordingId UUID>`.
- Pierwsze wywołanie zwraca `201 { "meetingId", "created":true }`. Powtórka tego samego `externalId` w tej samej organizacji zwraca `200` z tym samym `meetingId` i `created:false`.
- Nagłówek `Idempotency-Key` na tym endpointcie nie jest w wersji v1 źródłem idempotencji. Klient może go wysłać dla diagnostyki, ale musi mieć tę samą wartość co `externalId` i nie może polegać wyłącznie na nagłówku.
- Po utracie odpowiedzi klient ponawia identyczny POST. Alternatywnie może odzyskać identyfikator przez `GET /api/v1/ingest/meetings/{url-encoded externalId}`. `409 meeting_import_conflict` przy równoległym utworzeniu oznacza stan przejściowy — należy ponowić GET/POST, nie generować nowego `externalId`.
- `title` ma 1–500 znaków, `description` najwyżej 10 000 znaków. Typ spotkania i projekt pochodzą z ustawień klucza API.

**Inicjalizacja uploadu**

- `POST /api/v1/ingest/meetings/{meetingId}/uploads` z ciałem zawierającym `fileName`, `sizeBytes`, opcjonalne `sha256`, `chunkSizeBytes`, `codec`, `durationMs` i `deviceInfoJson`.
- `Idempotency-Key` identyfikuje sesję w obrębie danego `meetingId`; Recorder używa `recorder:<recordingId UUID>:audio:<numer sesji>`. Powtórzenie tego samego żądania z tym samym kluczem zwraca ten sam `uploadId`, `chunkSize`, `totalChunks` i `expiresAt`.
- Po utracie odpowiedzi klient ponawia identyczny POST z tym samym kluczem. `409 upload_init_conflict` przy równoległej inicjalizacji oznacza „ponów”.
- Sesja żyje 48 godzin. Po `410 upload_expired` klient zeruje `uploadId`, zwiększa numer sesji w `Idempotency-Key` i inicjuje nowy upload dla tego samego spotkania; ponowne użycie klucza wygasłej sesji zwróci ten sam, bezużyteczny `uploadId`.

**Chunki i wznowienie**

- Serwer przyjmuje żądane `chunkSizeBytes` w zakresie od 256 KiB do 16 MiB, ale wartość spoza zakresu jest zaciskana, a nie odrzucana. Brak wartości oznacza 5 MiB. Odpowiedź inicjalizacji jest zawsze źródłem prawdy — klient używa zwróconego `chunkSize` i `totalChunks`.
- `GET /api/v1/ingest/uploads/{uploadId}` zwraca `{ uploadId, status, receivedChunks, missingChunks }`. Indeksy są zerowe, unikalne i mieszczą się w `0..totalChunks-1`; klient wysyła wyłącznie `missingChunks`.
- `PUT /api/v1/ingest/uploads/{uploadId}/chunks/{index}` ma ciało binarne dokładnie wielkości `chunkSize`, z wyjątkiem ostatniego fragmentu. Klient zawsze wysyła `Content-SHA256` jako 64 małe znaki hex SHA-256 fragmentu. Sukces zwraca `204`.
- Ponowienie już zapisanego indeksu jest idempotentne. Po utracie odpowiedzi klient ponownie pobiera status; jeśli indeks nadal jest brakujący, wysyła go ponownie. `409 upload_concurrency_conflict` oznacza ponowienie po pobraniu statusu. `409 chunk_checksum_mismatch` lub `422 chunk_size_mismatch` wymaga ponownego odczytania właściwego zakresu lokalnego MP3, a nie pominięcia fragmentu.

**Finalizacja i przetwarzanie**

- `POST /api/v1/ingest/uploads/{uploadId}/complete?startProcessing=true` zwraca `202 { "audioAssetId", "processingJobId" }`. Brak parametru także oznacza uruchomienie przetwarzania; tylko `startProcessing=false` je wyłącza.
- Finalizacja zakończonej sesji jest idempotentna i zwraca ten sam `audioAssetId` oraz istniejący `processingJobId`. Po utracie odpowiedzi klient ponawia ten sam POST. Nie należy uruchamiać równoległych finalizacji tej samej sesji.
- `422 upload_incomplete` oznacza powrót do GET statusu i dosłanie brakujących fragmentów. Kolejkę wolno usunąć dopiero po odpowiedzi 2xx z finalizacji; lokalnego MP3 nie usuwa się nigdy automatycznie.

**Zgoda uczestników**

- `consentConfirmed=true` ustawia wszystkim przekazanym uczestnikom stan `Informed`: potwierdza jedynie, że zostali poinformowani o nagrywaniu. Nie oznacza akceptacji prawnej ani udzielenia zgody i nie zastępuje polityki organizacji.
- `consentConfirmed=false` zachowuje jawny `consentStatus` uczestnika, a przy jego braku zapisuje `NotAsked`. Recorder nie ma wiarygodnego sygnału poinformowania wszystkich osób, dlatego zawsze wysyła `false`.

**Limity i czasy**

- Rozmiar pliku: od 1 bajtu do 4 GiB włącznie. Sesja uploadu: 48 godzin. Limit ingest: 300 żądań na minutę na klucz i instancję API; po `429` klient stosuje backoff.
- Klient ma timeout 10 minut dla pojedynczego żądania. API nie deklaruje krótszego timeoutu aplikacyjnego, ale reverse proxy lub tunel może mieć własny limit. Timeout zawsze oznacza nieznany wynik operacji, więc klient nie tworzy nowych identyfikatorów, tylko stosuje opisane wyżej odczyty statusu i idempotentne ponowienia.
- Retry dla błędów sieciowych, timeoutów, `409` wymienionych wyżej, `429` i `5xx` używa wykładniczego opóźnienia do 30 minut. `401`/`403` wymagają naprawy klucza lub członkostwa i nie mogą powodować utraty wpisu kolejki.

Akceptacja: utrata sieci i restart aplikacji nie tracą MP3 ani stanu uploadu; powtórka tworzy jedno spotkanie, a poprawny upload automatycznie uruchamia przetwarzanie.

##### Audyt stanu klienta z 2026-08-25

1. Wpis kolejki przechowuje `meetingId` i `uploadId`, ale nie przechowuje kanonicznego bazowego URL, `organizationId` ani `meetingOwnerId`. Worker używa bieżących ustawień, więc po zmianie celu może wysłać stare identyfikatory do innej instancji lub organizacji.
2. `410 upload_expired` jest obsługiwany jak zwykły błąd przejściowy. Brakuje numeru sesji, wyzerowania `uploadId` i nowego klucza `recorder:<recordingId>:audio:<numer sesji>`.
3. `422 upload_incomplete` nie wraca do odczytu statusu i dosłania brakujących fragmentów. Obecny klient może bez końca ponawiać samą finalizację.
4. Wszystkie błędy HTTP trafiają do jednego backoffu. Nie ma rozróżnienia `401/403`, błędów trwałych, konfliktów domenowych, `429`, `5xx` i nieznanego wyniku po timeoutcie.
5. `chunkSize`, `totalChunks` i `missingChunks` nie są walidowane przed obliczaniem zakresu i alokacją bufora. Jeden błędny wpis może przerwać iterację workera i opóźniać pozostałe nagrania.
6. Wpis JSON kolejki zawiera jawny `RecordingSourceContext`, w tym potencjalny opis, URL i uczestników. Jest to niespójne z deklaracją README, że listy uczestników nie są zapisywane.
7. Kolejka jest zwalniana ręcznie, a następnie ponownie przez kontener DI, podczas gdy jej `DisposeAsync` nie jest idempotentne. Błąd shutdownu jest połykany przez ogólny `catch`.
8. Klucz API jest zapisywany przed walidacją i zatwierdzeniem ustawień, a kontrolka UI pokazuje go w zwykłym `TextBox`.
9. Nie istnieją automatyczne testy utworzenia spotkania, inicjalizacji uploadu, chunków, finalizacji, restartu, zmiany tenantu ani odpowiedzi domenowych.

##### Zakres i reguły realizacji

- Zadanie dotyczy wyłącznie integracji RecoWilk, trwałej kolejki, ustawień tej integracji oraz jej dokumentacji. Nie zmienia capture, miksowania, recovery WAV ani publikacji MP3.
- Każdy krok rozpoczyna się od testu odtwarzającego brakujące zachowanie. Refaktoryzacja jest dozwolona tylko wtedy, gdy jest niezbędna do testowalnej implementacji danego kroku.
- Lokalny MP3 jest źródłem prawdy i nigdy nie jest usuwany przez integrację. Wpis kolejki wolno usunąć dopiero po potwierdzonej odpowiedzi 2xx z finalizacji.
- Żaden sekret, opis wydarzenia, adres uczestnika ani ciało błędu mogące zawierać te dane nie może trafić do logu.
- Stare wpisy kolejki muszą zostać zmigrowane albo bezpiecznie zatrzymane do decyzji użytkownika; aktualizacja aplikacji nie może ich cicho usunąć.

##### Kolejność implementacji

**R-017A — testowalny klient kontraktu i modele odpowiedzi**

Zakres:

- Wydziel wywołania HTTP i parsowanie odpowiedzi od workera kolejki.
- Zastąp wynik `bool` testu połączenia typowanym wynikiem zawierającym `organizationId`, `apiKeyId`, `meetingOwnerId`, wersję API i bezpieczny komunikat błędu.
- Wprowadź typowany problem API z kodem domenowym, statusem HTTP i `traceId`; nie przenoś pełnego ciała odpowiedzi do logów ani UI.
- Zachowaj `GET /api/v1/ingest/ping`, schemat `Authorization: ApiKey` oraz wymóg HTTPS poza loopback.

Testy rozpoczynające krok:

- `200` z kompletną odpowiedzią `ping`; brak wymaganych pól w odpowiedzi `200`; `401`, `403`, `429` i `5xx`.
- Odrzucenie HTTP poza loopback, niepoprawnego URL i odpowiedzi o niezgodnym `apiVersion`.
- Parsowanie `application/problem+json` bez ujawnienia sekretu i treści prywatnych.

Akceptacja: kod wywołujący otrzymuje zweryfikowaną tożsamość celu albo typowany błąd; nie interpretuje dowolnej odpowiedzi 2xx jako kompletnej konfiguracji.

**R-017B — wersjonowany i prywatny wpis kolejki v2**

Zakres:

- Wprowadź `schemaVersion`, stabilny `recordingId`/`externalId`, kanoniczny `baseUrl`, `organizationId`, `meetingOwnerId`, informacyjny `apiKeyId`, etap operacji, `meetingId`, `uploadId`, `uploadSessionNumber`, `chunkSize`, `totalChunks`, `expiresAt`, retry i ostatnią bezpieczną kategorię błędu.
- Zapisuj techniczny stan atomowo przez unikalny plik tymczasowy na tym samym woluminie. Po restarcie obsłuż pozostały plik tymczasowy i uszkodzony JSON przez kwarantannę, bez blokowania innych wpisów.
- Zaszyfruj przez DPAPI część zawierającą tytuł, opis, URL i uczestników albo zaszyfruj cały wpis. Nie zapisuj klucza API we wpisie.
- Dodaj migrację istniejącego wpisu v1. Ponieważ v1 nie zna tenantu, przed użyciem zapisanych zdalnych identyfikatorów wykonaj `ping`; jeśli nie da się potwierdzić zgodności, wyzeruj `meetingId` i `uploadId`, zachowując `externalId` oraz MP3.

Testy rozpoczynające krok:

- Round-trip v2, migracja v1, przerwany zapis, uszkodzony JSON i niezależne przetwarzanie kolejnego poprawnego wpisu.
- Potwierdzenie, że jawny plik nie zawiera e-maila, opisu, URL ani klucza.
- Brak MP3, pusty plik, plik ponad 4 GiB oraz brak możliwości odczytu.

Akceptacja: restart zachowuje potrzebny stan, dane prywatne nie występują jawnie, a uszkodzony wpis nie zatrzymuje całej kolejki.

**R-017C — związanie wpisu z instancją i tenantem**

Zakres:

- Przed użyciem zdalnych identyfikatorów porównaj kanoniczny URL, `organizationId` i `meetingOwnerId` z bieżącym wynikiem `ping`.
- Po zmianie któregokolwiek z tych trzech pól wyzeruj `meetingId`, `uploadId` i parametry chunków, następnie rozpocznij od tego samego stabilnego `externalId` w nowym celu.
- Zmiana samego `apiKeyId` przy niezmienionej organizacji i właścicielu nie resetuje identyfikatorów.
- Snapshot celu zapisuj przed pierwszym żądaniem tworzącym spotkanie.

Testy rozpoczynające krok:

- Zmiana URL, organizacji i właściciela osobno; rotacja klucza w tym samym kontekście; restart między `ping` a `POST meetings`.
- Kanonizacja URL obejmująca końcowy slash, wielkość hosta i domyślne porty bez zmiany ścieżki bazowej.

Akceptacja: żaden `meetingId` ani `uploadId` nie jest wysyłany do innego celu, a rotacja klucza w tym samym kontekście poprawnie wznawia upload.

**R-017D — trwała maszyna stanów i idempotencja**

Zakres:

- Wprowadź etapy co najmniej: `WaitingForCredentials`, `CreatingMeeting`, `InitializingUpload`, `UploadingChunks`, `Completing`, `Completed`, `PermanentFailure`.
- Zapisuj stan po każdej potwierdzonej zmianie identyfikatora i przed przejściem do następnej operacji sieciowej.
- Twórz lub odzyskuj spotkanie przez stabilne `externalId`; po timeoutcie lub `409 meeting_import_conflict` ponawiaj ten sam POST albo wykonaj GET po `externalId`.
- Inicjalizuj upload kluczem `recorder:<recordingId>:audio:<uploadSessionNumber>`. Po utracie odpowiedzi ponawiaj identyczne żądanie.
- Po `410 upload_expired` wyzeruj wyłącznie stan uploadu, zwiększ numer sesji, zapisz wpis i utwórz nową sesję dla istniejącego spotkania.

Testy rozpoczynające krok:

- Restart i timeout przed oraz po każdej odpowiedzi: create, init, status, chunk i complete.
- `201`/`200` dla spotkania, `409 meeting_import_conflict`, `409 upload_init_conflict` oraz `410 upload_expired`.
- Dwie próby workera nie mogą równolegle obsługiwać tego samego wpisu.

Akceptacja: dowolny restart lub nieznany wynik żądania nie tworzy duplikatu spotkania, a wygaśnięta sesja jest zastępowana sesją o kolejnym numerze.

**R-017E — bezpieczne chunki i finalizacja**

Zakres:

- Żądaj domyślnie 5 MiB i traktuj odpowiedź serwera jako źródło prawdy, ale odrzucaj `chunkSize` poza 256 KiB–16 MiB, niezgodne `totalChunks`, obcy `uploadId`, duplikaty oraz indeksy poza zakresem.
- Obliczaj offset i długość z kontrolą przepełnienia; używaj ograniczonego, ponownie wykorzystywanego bufora i respektuj anulowanie odczytu, hashowania i wysyłania.
- Po timeoutcie PUT ponownie pobieraj status. `409 upload_concurrency_conflict` wraca do statusu, a checksum/size mismatch ponownie odczytuje właściwy zakres pliku.
- Po `422 upload_incomplete` z finalizacji wracaj do statusu i dosyłaj braki. Usuwaj wpis dopiero po 2xx z `complete`.

Testy rozpoczynające krok:

- Pliki jedno- i wielochunkowe, ostatni krótszy fragment, hash małymi znakami hex i dokładne rozmiary ciał.
- Brakujące, powtórzone, ujemne i zbyt duże indeksy; błędny rozmiar i liczba chunków.
- Timeout PUT, konflikt, checksum mismatch, size mismatch, `422 upload_incomplete` i idempotentna finalizacja.

Akceptacja: klient wysyła wyłącznie poprawne brakujące zakresy, nie wykonuje niekontrolowanych alokacji i nie usuwa wpisu przed potwierdzoną finalizacją.

**R-017F — polityka retry, izolacja wpisów i diagnostyka**

Zakres:

- Retry z jitterem i limitem 30 minut stosuj tylko dla sieci, timeoutów, obsługiwanych `409`, `429` i `5xx`; respektuj poprawny `Retry-After` bez przekroczenia bezpiecznego limitu.
- `401/403` ustawiają `WaitingForCredentials` i pozostawiają wpis bez aktywnej pętli żądań. Trwałe `4xx`, niezgodny kontrakt i błędny lokalny plik przechodzą do `PermanentFailure` z bezpiecznym komunikatem.
- Awaria jednego wpisu nie może przerywać iteracji pozostałych. Wprowadź pojedynczego właściciela pliku wpisu i blokadę przed równoległym workerem.
- Loguj tylko `recordingId`, etap, kategorię błędu, status HTTP, `traceId`, numer próby i termin następnej próby; nie loguj nagłówka Authorization, payloadu spotkania ani pełnego ciała błędu.

Testy rozpoczynające krok:

- Macierz statusów HTTP i kodów domenowych, `Retry-After`, limit backoffu, anulowanie opóźnienia i ponowne wybudzenie po zmianie ustawień.
- Pierwszy uszkodzony wpis oraz drugi poprawny; oba muszą otrzymać niezależny wynik.

Akceptacja: błędy trwałe nie generują nieskończonego ruchu, błędy przejściowe są wznawiane, a pojedynczy wpis nie blokuje kolejki.

**R-017G — ustawienia, prywatność i cykl życia aplikacji**

Zakres:

- Waliduj URL i testuj nowy klucz przed zapisaniem; zatwierdzaj ustawienia oraz sekret w kontrolowanej kolejności z rollbackiem przy błędzie.
- Zastąp jawne pole klucza kontrolką maskującą i dodaj osobną akcję usunięcia klucza, która nie usuwa oczekujących wpisów ani MP3.
- W UI wyjaśnij, że po włączeniu wysyłane są MP3 oraz dostępne metadane: tytuł, opis, terminy, źródło/link i uczestnicy; `consentConfirmed` pozostaje `false`.
- Usuń ręczne podwójne zwalnianie albo zrób start i `DisposeAsync` idempotentne. Shutdown ma anulować aktywne żądanie, zapisać stan i pozwolić kontenerowi zwolnić wszystkie usługi.
- Uzgodnij README, ekran ustawień oraz `docs/MANUAL_TESTS.md` z rzeczywistą retencją i szyfrowaniem kolejki.

Testy rozpoczynające krok:

- Niepoprawne ustawienia z nowym kluczem nie zmieniają poprzedniej konfiguracji; błąd zapisu ustawień nie pozostawia przypadkowo nowego sekretu.
- Wielokrotne `Start`/`DisposeAsync`, zamknięcie podczas statusu i PUT oraz wznowienie po ponownym uruchomieniu.
- Kontrakt XAML pola maskowanego i tekstu ujawniającego zakres wysyłanych danych.

Akceptacja: zapis konfiguracji jest spójny, sekret nie jest widoczny ani logowany, a zamknięcie aplikacji nie gubi stanu i nie przerywa zwalniania kontenera DI.

**R-017H — odbiór E2E i wydanie**

Zakres automatyczny:

- Uruchom pełne `./scripts/verify.ps1`, sprawdź diff, warstwy architektury i brak danych użytkownika w artefaktach repozytorium.
- Dodaj test kontraktowy pełnej ścieżki z podstawionym HTTP oraz test restartu używający rzeczywistych plików kolejki w izolowanym katalogu.

Zakres manualny z testową instancją RecoWilk:

- Poprawny `ping`, utworzenie jednego spotkania, pełne metadane, upload MP3, finalny asset i uruchomiony job.
- Utrata sieci w połowie uploadu, zakończenie procesu, restart i wysłanie tylko brakujących fragmentów.
- Rotacja klucza w tej samej organizacji, zmiana właściciela/organizacji, `401/403`, `429`, wymuszone `410 upload_expired` i `422 upload_incomplete`.
- Potwierdzenie, że MP3 pozostaje lokalnie, wpis znika dopiero po finalizacji, a logi i jawne pliki nie zawierają klucza, opisu, URL ani danych uczestników.

Informacje potrzebne dopiero do E2E: bazowy URL testowej instancji, testowy klucz z zakresem `meeting.ingest`, dostęp do weryfikacji spotkania/assetu oraz sposób wymuszenia lub zasymulowania `410` i `422`.

Akceptacja końcowa R-017: wszystkie testy automatyczne przechodzą, wykonano i zapisano scenariusze manualne sekcji 17, nie powstają duplikaty spotkania ani assetu, zmiana celu nie używa obcych identyfikatorów, restart nie traci postępu, a lokalny MP3 pozostaje zachowany.

##### Kolejność commitów R-017

1. `test: cover recowilk v1 api contract`
2. `fix: add typed recowilk api client`
3. `test: cover durable recowilk queue migration`
4. `fix: persist tenant-bound encrypted upload state`
5. `test: cover recowilk upload resume states`
6. `fix: implement idempotent recowilk upload state machine`
7. `test: cover recowilk chunk validation and retries`
8. `fix: validate chunks and classify recowilk failures`
9. `test: cover recowilk settings and shutdown lifecycle`
10. `fix: make recowilk settings and shutdown transactional`
11. `docs: document recowilk privacy and e2e validation`

Po każdym commicie należy uruchomić `./scripts/verify.ps1`. Commity `test:` mają wykazać niepowodzenie przed odpowiadającym im commitem `fix:` i przejść po naprawie.

### R-018 — lokalny katalog nagrań i kontrola eksportu

Status: implementacja lokalna; wymagany odbiór manualny Windows i E2E RecoWilk.

Zakres:

- Oddziel trwałą historię nagrań od wykonawczej kolejki uploadu, której wpis jest usuwany po potwierdzonej finalizacji.
- Utrwalaj finalny MP3, metadane Calendar/Meet, uczestników, identyfikatory zdalne, postęp, próby, bezpieczną kategorię błędu, HTTP status, `traceId` i terminy retry.
- Szyfruj każdy wpis katalogu DPAPI `CurrentUser`, publikuj go atomowo i izoluj uszkodzone wpisy w kwarantannie.
- Migruj oczekujące wpisy kolejki nawet przy wyłączonym eksporcie; istniejące MP3 importuj bez wymyślania utraconych metadanych.
- Dodaj okno `Nagrania i eksport` z filtrowaniem, wyszukiwaniem, szczegółami, uczestnikami, diagnostyką i akcjami otwarcia pliku/folderu, kopiowania `traceId` oraz ponowienia.
- Ręczne ponowienie zachowuje `recordingId`, `externalId` i poprawne istniejące identyfikatory sesji. Nie pozwala ponownie wysyłać wpisu już potwierdzonego jako wyeksportowany.
- Zdarzenia katalogu mogą przychodzić z workera; ViewModel przełącza aktualizację na Dispatcher i wykonuje operacje plikowe/retry poza wątkiem UI.
- `Wysłano do RecoWilk` oznacza potwierdzone `complete` i zachowane identyfikatory assetu/joba, nie zakończenie pipeline transkrypcji.

Testy rozpoczynające krok:

- Szyfrowany roundtrip katalogu, brak jawnego e-maila/opisu, kwarantanna uszkodzonego wpisu i import istniejącego MP3.
- Produkcyjny przypadek `ping=200`, `POST meeting=500`: katalog pokazuje HTTP 500 i `traceId`, a retry po naprawie zachowuje to samo `externalId`.
- Migracja istniejącej kolejki przy wyłączonej integracji, zachowanie MP3 po sukcesie i kontrakt XAML metadanych, uczestników, statusu i retry.

Akceptacja: historia pozostaje po sukcesie i restarcie, użytkownik widzi faktyczny etap/błąd, może bezpiecznie wznowić właściwe nagranie, dane osobowe pozostają zaszyfrowane lokalnie, a katalog nie może blokować zachowania ani wysłania kolejki.

Kolejność commitów:

1. `test: cover persistent recording catalog and retry`
2. `feat: add encrypted recording catalog and export control`
3. `feat: add recordings and export window`
4. `docs: document recording catalog and manual validation`

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
| 2026-08-08 | R-014 hotfix, aplikacja 1.2.2 | Rzeczywisty Chrome/Meet użytkownika, log HTTP oraz kontrakt Google Meet API | Rozszerzenie i `spaces.get`: poprawne; `participants.list`: 400 dla błędnej maski `signedInUser`; test regresyjny przed poprawką odtworzył błąd, po zmianie 5/5 testów klientów Workspace przechodzi | Użyto kontraktowego pola `signedinUser` i jawnego mapowania JSON; pełna brama: 118 testów przeszło, 1 test MF opt-in pominięty, te same 3 testy koordynatora zablokowane przez sandbox; `MeetingAudioRecorder-Setup-1.2.2.exe`, 73 723 174 B, SHA-256 `4FB1D4531B580E72F8C55AB9641CB0DF26D7DDEEC6FF38102E8C9853BEE39C2D`; wymagany ponowny test manualny 5B |
| 2026-08-11 | R-015, aplikacja 1.2.3 | Windows 11, rzeczywista awaria 1.2.2 oraz deterministyczna symulacja `0x88890004` bez urządzeń | Przed poprawką test nie kompilował się z powodu braku bezpiecznej polityki zakończenia; po poprawce 3/3 testy regresyjne i pełna brama 124 testów przeszły, 1 test MF opt-in pominięty; build Release 0 ostrzeżeń | Lokalny adapter przechwytuje osobno błąd pętli i `AudioClient.Stop`, przekazuje oba przez `RecordingStopped` i nie wypuszcza wyjątku z wątku capture. Matematyka ramek, format i długość nie zostały zmienione. Przy błędzie writer jest domykany, a oba WAV i manifest pozostają do recovery. Instalator `MeetingAudioRecorder-Setup-1.2.3.exe`, 73 720 602 B, SHA-256 `AA606354FD500875944099814AF01394848E598BD85A370142D555A0734BA72D`; wymagany manualny test 8A dla render i mikrofonu. |
| 2026-08-17 | R-014 hotfix, aplikacja 1.2.4 | Windows 11, rzeczywiste rozszerzenie Chrome i Meet `authuser=2`; deterministyczny test dwóch kolejnych spotkań bez restartu | Przed poprawką nowy kod Meet miał 0 wywołań, a stary 3; po poprawce test regresyjny i pełna brama 125 testów przeszły, 1 test MF opt-in pominięty; build Release 0 ostrzeżeń | Zakończone śledzone spotkanie jest zwalniane dopiero po potwierdzonej nieobecności i tylko gdy żadne nagranie nie jest aktywne. Aktywne nagranie nadal zachowuje właściciela i procedurę bezpiecznego stopu. Instalator `MeetingAudioRecorder-Setup-1.2.4.exe`, 73 719 261 B, SHA-256 `0842826987E35D4F817E1825CE3A677DB0435442DBA70130794D8E6D5FC6D201`; wymagany manualny test 5B.10 z dwoma prawdziwymi spotkaniami. |
| 2026-08-17 | R-016, aplikacja 1.2.5 | Windows 11, dwie osierocone instancje hosta z nieistniejącymi rodzicami; syntetyczny stdin i proces potomny bez przeglądarki | Przed poprawką czysty EOF zwracał `true`, a każda osierocona instancja zużywała około jednego rdzenia; po poprawce host kończy się kodem 0 poniżej 2 s. Pełna brama: 129 testów przeszło, 1 test MF opt-in pominięty; build Release 0 ostrzeżeń | Instalator 1.2.5 kończy stare hosty przez dokładną nazwę procesu przed kontrolą plików w użyciu; skrypt przeszedł kompilację Inno Setup 6.7.3. `MeetingAudioRecorder-Setup-1.2.5.exe`, 73 718 575 B, SHA-256 `24BBD228C13C5288A748A46F438E2720D15F6C69A2D38033AC2C5229F5E3E5BD`; wymagany manualny test 5C z Chrome/Edge. |
| 2026-08-21 | R-014 hotfix, aplikacja 1.2.6 | Windows 11, rzeczywisty stan rozszerzenia dla Meet `dkp-fimx-hxd`, log produkcyjny starego spotkania zwracającego `403` oraz deterministyczna symulacja `403/404` | Przed poprawką test wykazał 0 sprawdzeń nowego kodu i 3 sprawdzenia starego; po poprawce oba warianty przechodzą. Pełna brama: 131 testów przeszło, 1 test MF opt-in pominięty; build Release 0 ostrzeżeń | Niedostępne śledzone spotkanie jest zwalniane po `403/404` wyłącznie bez aktywnego nagrania, po czym w tej samej kontroli sprawdzany jest świeży link rozszerzenia. Błąd API podczas nagrywania nadal go nie zatrzymuje. Inno Setup 6.7.3: `MeetingAudioRecorder-Setup-1.2.6.exe`, 73 717 491 B, SHA-256 `4E966961A59BB28265D688C10CA16FA1D783CBBC16063C4BF0CFD43DEDE43163`; wymagany manualny test 5B.11 z prawdziwymi spotkaniami. |
| 2026-08-25 | R-017 | .NET 8, podstawiony HTTP, bez urządzeń audio | Build Release bez ostrzeżeń; klient sprawdza schemat `ApiKey`, wymusza HTTPS poza localhost, Calendar mapuje opis i pełną listę uczestników | Trwała kolejka powstaje dopiero po finalnym MP3, używa statusu brakujących chunków i nie usuwa lokalnego nagrania; wymagany test E2E z serwerem RecoWilk. |
| 2026-08-25 | R-017 — ponowny audyt i kompletny plan naprawczy | Dokumentacja oraz analiza statyczna klienta; bez zmian zachowania | `./scripts/verify.ps1`: 134 testy przeszły, 1 test Media Foundation opt-in pominięty; build Release 0 ostrzeżeń | Plan R-017A–R-017H obejmuje kontrakt, migrację kolejki v2, tenant binding, maszynę stanów, chunki, retry, prywatność, shutdown i E2E. Testy RecoWilk nadal obejmują tylko trzy przypadki połączenia; implementacja planu nie została rozpoczęta. |
| 2026-08-25 | R-017A–R-017H — implementacja lokalna | .NET 8, podstawiony HTTP, izolowane pliki kolejki, bez produkcyjnego API i urządzeń audio | `./scripts/verify.ps1`: 149 testów przeszło, 1 test Media Foundation opt-in pominięty; build Release 0 ostrzeżeń | Testy potwierdzają wyłącznie `200` dla ping, tenant binding, rotację celu, pełny upload, DPAPI-ready queue v2, migrację v1, `410`, `422`, walidację geometrii i indeksów, izolację uszkodzonego wpisu, pozostawienie MP3, idempotentny dispose, maskowanie klucza i rollback ustawień. Oczekuje manualny scenariusz 17 z testową instancją RecoWilk. |
| 2026-08-25 | R-017 — artefakt 1.3.0 | Windows 11 x64, .NET 8 self-contained, Inno Setup 6.7.3 | Instalator zbudowany po przejściu bramy automatycznej | `MeetingAudioRecorder-Setup-1.3.0.exe`, 73 748 822 B, SHA-256 `FEDBBE20B4D5D2898389EC86428284EA829CE17A814B565ADEE45366E3A5ECB1`; plik nie ma podpisu Authenticode. Przed wdrożeniem produkcyjnym nadal wymagany manualny scenariusz 17 z testową instancją RecoWilk. |
| 2026-08-25 | R-018, aplikacja 1.4.0 | .NET 8, Windows DPAPI, podstawiony HTTP, izolowane pliki katalogu i kolejki, kontrakt XAML bez uruchamiania prawdziwego okna | `./scripts/verify.ps1`: 156 testów przeszło, 1 test Media Foundation opt-in pominięty; build Release 0 ostrzeżeń | Testy potwierdzają szyfrowany roundtrip i kwarantannę katalogu, import MP3 i oczekującej kolejki, przypadek `ping=200`/`POST meeting=500`, zachowanie HTTP/trace, idempotentny retry, postęp i trwałą historię po `complete`. Inno Setup 6.7.3: `MeetingAudioRecorder-Setup-1.4.0.exe`, 73 762 052 B, SHA-256 `C1282B559509D583196BB3F805AF11CF38DA4017AD3E01EEABF90E0E1E76A2A7`; brak podpisu Authenticode. Wymagany manualny scenariusz 18 na Windows z prawdziwym Meet i RecoWilk. |
