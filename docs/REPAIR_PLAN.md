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

- Użyj estymacji zależnej od formatów źródeł i czasu.
- Monitoruj wolne miejsce podczas capture.
- Zarezerwuj miejsce na miks i MP3 albo przetwarzaj strumieniowo.
- Przy krytycznie małej przestrzeni zakończ capture i zachowaj możliwe do odzyskania pliki.

#### R-005: korekcja dryfu

- Mierz ramki obu źródeł względem wspólnego zegara monotonicznego.
- Zdefiniuj próg korekcji oraz ogranicz jej tempo.
- Unikaj gwałtownych insert/drop słyszalnych jako kliknięcia.

Akceptacja: dryf po 4 godzinach nie przekracza 250 ms; celem jest 100 ms.

#### R-006: niezmienny snapshot sesji

- Zapisz ustawienia miksu, ścieżki i aktywne ID urządzeń w sesji przy starcie.
- Monitoring urządzeń porównuj z `CurrentSession`.
- Zmiany ustawień stosuj od następnego nagrania.

### P2 — ergonomia i utrzymanie

#### R-007: transakcyjna zmiana hotkeya

- Rejestruj nowy skrót z możliwością rollbacku.
- Zapisz ustawienia dopiero po sukcesie.
- Przy błędzie zachowaj poprzedni aktywny skrót i konfigurację.

#### R-008: anulowanie przetwarzania

- Sprawdzaj token w pętli zapisu miksu.
- Opisz ograniczenia anulowania Media Foundation.
- Nie publikuj częściowego MP3 po anulowaniu.

#### R-009: testy systemowe i diagnostyka

- Dodaj testy manifestu/recovery i osi czasu bez sprzętu.
- Dodaj opt-in testy Windows dla Media Foundation.
- Rozszerz testy manualne o markery synchronizacji i protokół wyników.
- Loguj format, ramki, luki, korekty dryfu i rozmiary; nigdy próbki audio.

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
