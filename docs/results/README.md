# Co jest w results?

To zapisane wyniki konkretnych testów, a nie nagrania, modele AI ani pliki wymagane przez aplikację. JSON pozwala kolejnym osobom i narzędziom sprawdzić liczby oraz identyfikator zbudowanego instalatora. Wyjaśnienie decyzji znajduje się w [TRANSLATION_HANDOFF.md](../TRANSLATION_HANDOFF.md).

| Plik | Co potwierdza |
|---|---|
| [translation-live-2026-11-01.json](translation-live-2026-11-01.json) | Próby potoku 1.5.0 na WAV, drafty i finały, odzyskanie zapisanych fraz, wymuszoną awarię pomocnika, widoki i identyfikator instalatora. |
| [model-download-2026-11-01.json](model-download-2026-11-01.json) | Testy pobierania 1.5.1, rzeczywiste pobranie Silero, przygotowanie pięciu plików Mariana i identyfikator instalatora. |

`passed` / `verified` oznacza powodzenie opisanej próby. `drafts` to robocze wersje napisów, `finals` — zatwierdzone frazy, `reloadedFinals` — frazy odczytane ponownie z dysku. SHA-256 identyfikuje dokładne bajty instalatora; nie jest podpisem wydawcy. Liczba testów nie jest procentem jakości tłumaczenia.

Wyniki są historyczne. Test WAV nie dowodzi poprawności wszystkich urządzeń, a poprawna suma kontrolna modelu nie dowodzi jakości jego tłumaczeń. Nowszy raport nie unieważnia starszego — dotyczy innej wersji lub zakresu. Nie zmieniaj dawnych wyników tylko po to, żeby pasowały do aktualnego stanu.

Szczegółowe lokalne artefakty są w ignorowanym `out/` i mogą nie być dostępne po sklonowaniu repozytorium. Do tego katalogu zapisuj małe raporty bez transkrypcji, audio, kluczy, tokenów i danych kont użytkownika. Potwierdzenie użytkownika „wszystko działa” z 2026-11-02 zapisano osobno w dokumencie przekazania; nie dopisano go jako wykonania całej listy testów manualnych.
