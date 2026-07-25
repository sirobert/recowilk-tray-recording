# Meeting Audio Recorder — instrukcje dla agentów

## Cel

Rozwijaj aplikację jako lokalny, odporny na awarie rejestrator Windows. Najwyższym priorytetem jest zachowanie materiału audio, poprawna oś czasu obu źródeł i brak blokowania interfejsu.

## Kontekst techniczny

- Platforma: Windows 10/11 x64, .NET 8, WPF.
- Audio: NAudio, WASAPI Capture i WASAPI Loopback.
- Wynik: Media Foundation MP3.
- Architektura: `App` → `Core`; `Audio` i `Infrastructure` implementują interfejsy `Core`.
- Dane użytkownika pozostają lokalne. Nie dodawaj telemetrii ani sieci bez jawnej decyzji.

## Obowiązkowy workflow

1. Przed zmianą przeczytaj `docs/REPAIR_PLAN.md` i wybierz jedno zadanie.
2. Dla synchronizacji, capture, WAV, recovery lub długich nagrań użyj `$recorder-audio-reliability`.
3. Najpierw dodaj test odtwarzający błąd, potem implementację.
4. Nie łącz refaktoryzacji z naprawą zachowania, jeśli nie jest to konieczne.
5. Po zmianie użyj `$recorder-quality-gate` i uruchom `.\scripts\verify.ps1`.
6. Aktualizuj status oraz dowody w `docs/REPAIR_PLAN.md`.

## Inwarianty

- Nie usuwaj źródłowych plików tymczasowych przed potwierdzonym zapisaniem finalnego MP3.
- Publikuj wynik przez plik `.partial` i atomową zmianę nazwy na tym samym woluminie.
- Zdarzenia z warstw audio/infrastructure mogą przychodzić spoza wątku UI.
- Nie blokuj wątku UI przez `.Wait()`, `.Result` ani `GetAwaiter().GetResult()`.
- Nie opieraj synchronizacji pakietów audio wyłącznie na kolejności callbacków i timerze.
- Recovery musi obsługiwać plik po rzeczywistym przerwaniu procesu, nie tylko poprawnie zamknięty WAV.
- Każda pętla miksowania lub kodowania musi respektować anulowanie w trakcie pracy.
- Używaj urządzeń zapisanych w aktywnej sesji, nie zmiennych ustawień bieżących.

## Zakres testów

- Czystą matematykę osi czasu i naprawę nagłówków testuj bez sprzętu.
- Integracje WASAPI i Media Foundation oznaczaj jako testy Windows/sprzętowe.
- Nie uznawaj testu manualnego za wykonany bez wpisania środowiska, urządzeń i wyniku.
- Minimalna brama commita: format, build Release i wszystkie testy automatyczne.

## Git

- Stosuj Conventional Commits: `fix:`, `test:`, `docs:`, `build:`, `refactor:`, `chore:`.
- Jeden commit powinien realizować jeden spójny krok planu.
- Nie commituj `bin`, `obj`, `publish`, nagrań, logów ani danych z profilu użytkownika.
- Nie omijaj hooków przez `--no-verify`, chyba że użytkownik jawnie o to poprosi; opisz wtedy powód.

