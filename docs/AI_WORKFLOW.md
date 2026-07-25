# Praca nad projektem z AI

## Rozpoczęcie zadania

1. Wybierz pojedynczy identyfikator z `REPAIR_PLAN.md`.
2. Poproś agenta o właściwy skill, np.:

   `Użyj $recorder-audio-reliability i zrealizuj R-001 wraz z testami.`

3. Oczekuj testu odtwarzającego problem, następnie minimalnej poprawki.
4. Przed commitem uruchom:

   ```powershell
   .\scripts\verify.ps1
   ```

## Review

Agent powinien raportować zmienione zachowanie, test regresyjny, wynik automatycznej weryfikacji, niewykonane testy sprzętowe i pozostałe ryzyka.

Nie akceptuj samego „testy przechodzą” dla zmian WASAPI. Wymagaj scenariusza manualnego odpowiadającego zmienionemu przepływowi.

## Hooki

Repo używa `core.hooksPath=.githooks`.

- `pre-commit` sprawdza format i szybki build.
- `pre-push` uruchamia pełne testy Release.
- Ostateczną bramą jest workflow CI.

