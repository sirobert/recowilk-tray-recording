---
name: recorder-audio-reliability
description: Naprawia niezawodność Meeting Audio Recorder w obszarach WASAPI capture/loopback, osi czasu i synchronizacji, miksowania, dryfu, crash-safe WAV, manifestów sesji, recovery, miejsca na dysku oraz bezpiecznego zatrzymywania. Użyj przy implementacji zadań R-001–R-006, diagnozie braków lub przesunięć audio, utracie nagrania po awarii albo zmianach w RecordingCoordinator i warstwie Audio.
---

# Recorder Audio Reliability

## Workflow

1. Przeczytaj `docs/REPAIR_PLAN.md` i wybierz dokładnie jedno zadanie.
2. Przeczytaj [references/invariants.md](references/invariants.md).
3. Zmapuj format źródła, jednostkę czasu, właściciela bufora i wątek każdego callbacka.
4. Dodaj deterministyczny test regresyjny bez urządzeń, jeśli logikę da się wydzielić.
5. Zaimplementuj minimalną zmianę zachowującą pliki źródłowe przy błędzie.
6. Uruchom `$recorder-quality-gate`.
7. Dla zmian WASAPI przygotuj konkretny test z `docs/MANUAL_TESTS.md`; nie deklaruj go jako wykonany bez sprzętu.
8. Zaktualizuj zadanie i dziennik walidacji w planie.

## Reguły projektowe

- Operuj ramkami audio; bajty i próbki przeliczaj tylko na granicach API.
- Używaj `Stopwatch` jako zegara monotonicznego, a `DateTimeOffset` tylko do metadanych.
- Nie dopisuj ciszy, dopóki nie wiadomo, że dany okres nie jest reprezentowany przez bufor.
- Oddziel obliczenie osi czasu od I/O, aby testować je deterministycznie.
- Przechowuj snapshot ustawień w sesji.
- Traktuj przerwanie procesu jako normalny scenariusz recovery.
- Nigdy nie naprawiaj uszkodzonego pliku w miejscu bez kopii lub operacji atomowej.
- Nie wprowadzaj korekcji dryfu bez limitu szybkości i testu braku skoków.
- Propaguj anulowanie przez długie pętle.

## Dowody wymagane w wyniku

- test, który nie przechodził przed poprawką,
- wynik `scripts/verify.ps1`,
- różnica długości/osi czasu przed i po zmianie,
- lista testów manualnych wymagających urządzeń,
- informacja, które pliki temp pozostają po każdej ścieżce błędu.
