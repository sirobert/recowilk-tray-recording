# Testy manualne / integracyjne

## Środowisko

- Windows 10 (1903+) lub Windows 11, x64
- Mikrofon i urządzenie wyjściowe
- Aplikacja uruchomiona bez uprawnień administratora

---

## 1. Mikrofon USB

1. Podłącz mikrofon USB, odśwież listę w Ustawieniach.
2. Wybierz mikrofon, kliknij **Testuj mikrofon** — wskaźnik poziomu reaguje na mowę.
3. Nagraj 30 s (skrót Ctrl+Alt+R), zatrzymaj.
4. Otwórz MP3 — słychać głos z mikrofonu.

## 2. Słuchawki przewodowe (jack)

1. Ustaw słuchawki jako urządzenie wyjściowe w Windows i w aplikacji.
2. **Testuj przechwytywanie dźwięku** — odtwórz muzykę; wskaźnik rośnie.
3. Nagraj z muzyką + mową do mikrofonu — w MP3 słychać oba źródła.

## 3. Słuchawki USB

1. Wybierz endpoint USB Headphones/Speakers (nie Hands-Free, jeśli nie jest to intencja).
2. Powtórz test loopback i krótkie nagranie.

## 4. Słuchawki Bluetooth

1. Połącz BT, odśwież listę — widać osobne endpointy (Stereo, Hands-Free AG Audio itd.).
2. Wybierz endpoint używany przez Meet/Teams (często Hands-Free podczas rozmowy).
3. Rozpocznij nagranie, potem dołącz do Meet — obserwuj, czy Windows nie przełącza profilu.
4. Jeśli endpoint znika: aplikacja kończy nagranie bezpiecznie i pokazuje komunikat (nie crashuje).

## 5. Google Meet

1. Dołącz do Meet ze znanym mikrofonem i słuchawkami.
2. Uruchom nagrywanie skrótem.
3. Mów i poproś drugą osobę o mówienie.
4. Zatrzymaj — w pliku słychać Ciebie i drugą stronę (loopback ze słuchawek).

## 6. Microsoft Teams

1. Jak wyżej dla Teams (szczególnie profil BT Hands-Free).
2. Sprawdź, że skrót działa, gdy okno Teams ma fokus.

## 7. Długie nagranie

1. Nagraj ≥ 30–60 minut (docelowo weryfikacja 4 h).
2. Sprawdź brak dryfu (głos i loopback zsynchronizowane na końcu).
3. UI pozostaje responsywne; tray pokazuje czas.

### 7A. R-001 — luki loopback i granica callbacka

1. Uruchom nagranie i odtwarzaj sygnał z krótkim markerem co 1 s przez 60 s.
2. Zatrzymaj odtwarzanie na 10 s, nie zatrzymując nagrania.
3. Wznów sygnał na kolejne 60 s i zakończ nagranie.
4. Oczekiwane: dokładnie około 10 s ciszy, bez dodatkowych bloków ciszy przed markerami.
5. Porównaj czas WAV loopback/MP3 z czasem ściennym; różnica nie powinna przekroczyć jednego bufora capture.
6. Powtórz dla 44,1 kHz i 48 kHz, zapisując urządzenie, format i zmierzoną różnicę.

## 8. Odłączenie urządzenia podczas nagrywania

1. Start nagrywania.
2. Odłącz mikrofon USB lub słuchawki.
3. Oczekiwane: bezpieczne zatrzymanie, zachowanie temp, komunikat, brak crasha.
4. Możliwość odzyskania / zapis części materiału.

## 9. Brak miejsca na dysku

1. Wskaż folder na dysku z bardzo małą ilością miejsca (lub użyj małej partycji testowej).
2. Start powinien zostać zablokowany z czytelnym komunikatem **albo** zapis kończy się błędem bez utraty temp.

## 10. Wybudzenie ze snu

1. Start nagrywania.
2. Uśpij komputer na 1–2 minuty, wybudź.
3. Zatrzymaj nagranie — sprawdź spójność pliku (cisze w trakcie snu są akceptowalne).

## 11. Pojedyncza instancja

1. Uruchom aplikację.
2. Uruchom ponownie — druga instancja nie startuje; komunikat o już działającej aplikacji.

## 12. Autostart

1. Włącz w ustawieniach, wyloguj/zaloguj — aplikacja w tray.
2. Wyłącz — po restarcie sesji nie startuje.

## 13. Odzyskiwanie po awarii

1. Start nagrywania, zabij proces w Menedżerze zadań.
2. Uruchom ponownie — dialog odzyskiwania z plikami temp.
3. Odzyskaj lub zachowaj folder Temp (brak auto-usuwania).

### 13A. R-002 — protokół twardego przerwania

1. Włącz osobne ścieżki, rozpocznij nagranie i odtwarzaj rozpoznawalny sygnał przez co najmniej 30 s.
2. Zapisz identyfikator procesu i wykonaj `Stop-Process -Id <PID> -Force`.
3. Przed ponownym uruchomieniem skopiuj katalog `%LOCALAPPDATA%\MeetingAudioRecorder\Temp` jako dowód.
4. Uruchom aplikację, wybierz wykrytą sesję i wykonaj recovery.
5. Oczekiwane: poprawny MP3, czas zbliżony do nagranego odcinka, obie dostępne ścieżki słyszalne.
6. Potwierdź, że manifest zawiera właściwy czas startu i formaty, a po sukcesie pliki sesji zostały usunięte.
7. Powtórz z dostępną tylko jedną ścieżką; recovery nadal powinno utworzyć MP3.

## 14. Konflikt skrótu

1. Ustaw skrót zajęty przez inną aplikację.
2. Komunikat o niedostępności; aplikacja działa dalej.

## Checklist kryteriów odbioru

| # | Kryterium | OK? |
|---|-----------|-----|
| 1 | Win10/11 | |
| 2 | Bez admina | |
| 3 | Tray | |
| 4 | Wybór urządzeń | |
| 5 | Globalny skrót | |
| 6 | Mic + loopback | |
| 7 | Poprawny MP3 | |
| 8 | Oba źródła słyszalne | |
| 9 | Cisza nie desynchronizuje | |
| 10 | Różne sample rate | |
| 11 | UI nie zawisa przy encode | |
| 12 | Awaria nie kasuje temp | |
| 13 | Recovery po restarcie | |
| 14 | Odłączenie urządzenia | |
| 15 | Single instance | |
| 16 | Settings persist | |
| 17 | Autostart on/off | |
| 18 | Build bez ręcznych poprawek | |
