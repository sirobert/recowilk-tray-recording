---
name: recorder-quality-gate
description: Weryfikuje zmiany w Meeting Audio Recorder przed commitem lub pushem — formatowanie, kompilację Release, testy automatyczne, architekturę warstw, bezpieczeństwo plików temp, asynchroniczność UI i wymagane testy manualne Windows. Użyj po implementacji, przy code review, przed commitem oraz gdy użytkownik prosi o sprawdzenie jakości lub gotowości wydania.
---

# Recorder Quality Gate

## Procedura

1. Sprawdź zakres zmian i przypisz go do zadania w `docs/REPAIR_PLAN.md`.
2. Uruchom `.\scripts\verify.ps1`.
3. Przejrzyj diff pod kątem inwariantów z `AGENTS.md`.
4. Sprawdź, czy zmiana zachowania ma test regresyjny.
5. Wskaż testy manualne z `docs/MANUAL_TESTS.md`, których nie da się wykonać automatycznie.
6. Nie zatwierdzaj zmiany przy błędzie build/test/format ani przy możliwej utracie plików temp.

## Kontrola zmian audio

- Potwierdź jednostki: bajty, próbki, ramki, ticki.
- Sprawdź długości dla ciszy, regularnych pakietów i luk.
- Sprawdź kanały, sample rate, `BlockAlign` i granice buforów.
- Wymagaj testu długiego czasu w postaci symulacji lub sprzętowego protokołu.

## Kontrola UI i współbieżności

- Odrzuć synchroniczne czekanie na `Task` w kodzie UI.
- Ustal wątek wywołujący event i użyj asynchronicznego dispatchera.
- Sprawdź wielokrotne start/stop/exit i ścieżki błędów semafora.

## Raport

Podaj wynik każdej bramy, liczbę testów, niewykonane testy sprzętowe i ryzyka. Nie zamieniaj ostrzeżenia w zapewnienie bez dowodu.
