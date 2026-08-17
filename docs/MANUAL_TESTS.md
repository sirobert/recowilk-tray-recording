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

## 5. Google Meet — automatyczne nagrywanie R-012

Zapisz w raporcie: wersję Windows i aplikacji, typ konta Google, identyfikatory urządzeń audio, czas zdarzeń start/stop oraz wynik. Nie zapisuj tokenu OAuth ani listy uczestników.

1. Utwórz wydarzenie Calendar z linkiem Meet rozpoczynające się w ciągu 15 minut. Połącz konto Google w aplikacji, włącz automatykę i zapisz ustawienia.
2. Nie dołączaj do Meet. Oczekiwane: samo rozpoczęcie wydarzenia nie uruchamia nagrania.
3. Dołącz tym samym kontem Google. Oczekiwane: start jednej sesji w ciągu około 5–10 s, zmiana ikony tray i powiadomienie.
4. Mów i odtwórz głos drugiej osoby. Opuść spotkanie. Oczekiwane: zatrzymanie po około 15–20 s oraz poprawny MP3 z mikrofonem i loopback.
5. Dołącz ponownie i podczas nagrania rozłącz się na mniej niż 15 s, po czym wróć. Oczekiwane: jedno ciągłe nagranie bez automatycznego stopu.
6. Podczas automatycznego nagrania odłącz Internet na co najmniej 30 s. Oczekiwane: nagranie trwa; po odzyskaniu sieci monitoring wraca bez drugiego startu.
7. Rozpocznij nagranie ręcznie, następnie dołącz i opuść wydarzenie Meet. Oczekiwane: automat nie przejmuje ani nie zatrzymuje ręcznej sesji.
8. Rozpocznij sesję automatycznie, a następnie zatrzymaj ją ręcznie, pozostając w Meet. Oczekiwane: automat nie uruchamia jej ponownie aż do opuszczenia spotkania.
9. Powtórz, wchodząc innym kontem lub anonimowo. Oczekiwane: brak automatycznego startu.
10. Cofnij zgodę aplikacji na koncie Google podczas nagrania. Oczekiwane: komunikat o wymaganym logowaniu, ale brak automatycznego stopu i brak utraty materiału.

### 5A. R-013 — automatyczny wybór urządzeń przeglądarki

1. W ustawieniach aplikacji zapisz mikrofon A i wyjście A. W Google Meet w Chrome wybierz inne urządzenia: mikrofon B i wyjście B.
2. Dołącz do wydarzenia objętego automatyką, upewnij się, że mikrofon Meet jest aktywny i odtwórz głos drugiej osoby.
3. Oczekiwane: powiadomienie startowe wskazuje Chrome oraz urządzenia B; manifest sesji zawiera endpointy B, a zapisane ustawienia nadal zawierają A.
4. Zatrzymaj spotkanie i odsłuchaj MP3. Oczekiwane: słychać mikrofon B i dźwięk odtwarzany na wyjściu B.
5. Powtórz dla Edge lub Firefox. Oczekiwane: wybór endpointów aktywnej przeglądarki, bez przejęcia aktywnej sesji innej obsługiwanej przeglądarki, jeżeli sesja mikrofonowa jednoznacznie wskazuje Meet.
6. Zablokuj dostęp przeglądarki do mikrofonu albo zamknij jej sesję audio przed startem. Oczekiwane: brak błędu startu; brakujący endpoint pochodzi z ustawień aplikacji.
7. Uruchom ręczne nagrywanie podczas aktywnego Meet. Oczekiwane: ręczny start nadal używa urządzeń A i nie wykonuje automatycznej detekcji.
8. Podczas aktywnego nagrania zmień urządzenia w Meet. Oczekiwane: bieżąca sesja zachowuje endpointy zapisane w swoim snapshotcie; zmiana nie przełącza capture w locie.

### 5B. R-014 — Meeting Orgniazer Gemini i link bez Calendar

1. Zainstaluj aplikację 1.2.2. W Ustawieniach kliknij instalację rozszerzenia dla Chrome; potwierdź, że otwierają się `chrome://extensions` i folder pakietu, a ścieżka jest w schowku.
2. Włącz Tryb dewelopera, wybierz **Załaduj rozpakowane** i wskaż folder `MeetingOrgniazerGemini`. Oczekiwane ID: `eljjpmlmlnjjpjlnhiilfclkhoecdlij`; brak błędu Native Messaging.
3. Połącz w aplikacji konto Google, włącz automatykę i upewnij się, że w Calendar nie ma testowego spotkania.
4. Otwórz otrzymany link `https://meet.google.com/tcu-ysxp-tvw?...`, ale nie klikaj **Dołącz teraz**. Oczekiwane: brak nagrywania.
5. Dołącz tym samym kontem, które połączono z aplikacją. Oczekiwane: rozszerzenie zapisuje świeży kod lokalnie, Meet API potwierdza obecność i jedna sesja rozpoczyna się bez oczekiwania na 30-sekundowy polling Calendar.
6. Opuść spotkanie, pozostawiając kartę otwartą. Oczekiwane: zatrzymanie następuje dopiero po potwierdzonym braku aktywnej sesji przez Meet API.
7. Zamknij kartę lub wyłącz rozszerzenie. Oczekiwane: stan linku wygasa najpóźniej po 90 sekundach; samo zniknięcie rozszerzenia nie zatrzymuje trwającego nagrania przy błędzie API.
8. Powtórz dla Edge. Następnie otwórz dwa różne linki Meet i dołącz tylko do jednego; oczekiwane: aplikacja sprawdza oba kody, ale uruchamia jedną sesję dla konferencji z aktywnym użytkownikiem.
9. Przejrzyj `%LOCALAPPDATA%\MeetingAudioRecorder\Browser\active-meet.json`. Oczekiwane: wyłącznie wersja, czas, kod spotkania i nazwa przeglądarki; brak URL query, tokenów, e-maili, tytułów kart i list uczestników.
10. Po automatycznym starcie zatrzymaj nagranie ręcznie, opuść pierwsze spotkanie i dołącz do innego aktywnego linku bez restartowania aplikacji. Oczekiwane: po potwierdzeniu nieobecności w poprzednim Meet aplikacja sprawdza nowy kod i uruchamia drugie nagranie automatycznie; podczas aktywnego nagrania nie przełącza śledzonego spotkania.

### 5C. R-016 — cykl życia Native Messaging hosta

1. Zainstaluj aplikację 1.2.5 nad wersją zawierającą osierocone procesy `MeetingAudioRecorder.BrowserBridge.exe`. Oczekiwane: instalator kończy stare hosty i zastępuje pliki bez żądania ich ręcznego zamknięcia.
2. Otwórz Meet z aktywnym rozszerzeniem i potwierdź powstanie jednego procesu hosta dla aktywnego portu Native Messaging.
3. Zamknij kartę Meet, wyłącz rozszerzenie albo zamknij Chrome/Edge. Oczekiwane: odpowiadający proces hosta kończy się w ciągu 2 sekund.
4. Powtórz uruchomienie i zamknięcie service workera co najmniej 10 razy. Oczekiwane: liczba hostów nie narasta, a po zamknięciu połączeń żaden proces nie zużywa stale rdzenia CPU.
5. Podczas aktywnego nagrania wymuś restart service workera rozszerzenia. Oczekiwane: nagrywanie trwa bez zmian; po ponownym połączeniu świeży stan linku znów jest publikowany.

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

### 7B. R-005 — dryf zegarów urządzeń

1. Przygotuj wspólny, łatwy do rozpoznania marker słyszalny jednocześnie przez mikrofon i loopback.
2. Zarejestruj marker na początku, następnie nagrywaj 4 godziny i zarejestruj taki sam marker przed zatrzymaniem.
3. Zmierz przesunięcie markerów obu ścieżek na początku i końcu; dryf końcowy nie może przekroczyć 250 ms, celem jest 100 ms.
4. Sprawdź odsłuch całego materiału w kilku miejscach; korekcja nie może powodować kliknięć ani gwałtownych przeskoków.
5. Zapisz z logu dla obu ścieżek: liczbę ramek źródłowych/docelowych, zmierzony dryf ppm, zastosowaną korekcję ppm i informację o ograniczeniu.
6. Powtórz dla co najmniej pary urządzeń przewodowych i Bluetooth oraz kombinacji 44,1/48 kHz.

### 7C. R-006 — snapshot ustawień aktywnej sesji

1. Ustaw folder A, bitrate 128 kb/s, 44,1 kHz, rozpoznawalne poziomy miksu i wyłącz osobne ścieżki.
2. Rozpocznij nagrywanie, następnie w ustawieniach wybierz folder B, 320 kb/s, 48 kHz, inne poziomy i włącz osobne ścieżki.
3. Zatrzymaj nagranie; wynik bieżącej sesji ma trafić do folderu A i używać parametrów ustawionych przed startem.
4. Rozpocznij drugie nagranie; dopiero ono ma użyć folderu B i nowych parametrów, w tym osobnych ścieżek.
5. Podczas nagrania zmień wybrane urządzenie w ustawieniach, a następnie odłącz urządzenie faktycznie użyte przy starcie.
6. Oczekiwane: monitoring reaguje na utratę urządzenia z `CurrentSession`, bez względu na nowy wybór zapisany w ustawieniach.
7. Powtórz scenariusz z wymuszonym zamknięciem procesu; recovery z manifestu v2 ma użyć folderu i parametrów pierwotnej sesji.

## 8. Odłączenie urządzenia podczas nagrywania

1. Start nagrywania.
2. Odłącz mikrofon USB lub słuchawki.
3. Oczekiwane: bezpieczne zatrzymanie, zachowanie temp, komunikat, brak crasha.
4. Możliwość odzyskania / zapis części materiału.

### 8A. R-015 — unieważnienie endpointu podczas capture

1. Zapisz wersję Windows, wersję aplikacji, nazwy i identyfikatory aktywnego mikrofonu oraz urządzenia renderującego.
2. Rozpocznij nagrywanie i odtwarzaj rozpoznawalny dźwięk przez co najmniej 30 s.
3. Fizycznie odłącz aktywne urządzenie renderujące albo wyłącz jego adapter Bluetooth; nie zatrzymuj wcześniej nagrania z UI.
4. Oczekiwane: aplikacja pozostaje uruchomiona i responsywna, sesja przechodzi przez kontrolowany błąd capture, a log nie zawiera `UnhandledException` ani zdarzenia Windows `.NET Runtime 1026`.
5. Potwierdź, że manifest oraz oba źródłowe WAV pozostają do recovery, jeśli nie opublikowano poprawnego MP3. Zanotuj rozmiary plików, ale nie dołączaj próbek audio.
6. Uruchom recovery i sprawdź czytelność odzyskanego materiału oraz różnicę jego długości względem czasu od startu do odłączenia.
7. Powtórz kroki 2–6 dla fizycznego odłączenia aktywnego mikrofonu.

## 9. Brak miejsca na dysku

1. Wskaż folder na dysku z bardzo małą ilością miejsca (lub użyj małej partycji testowej).
2. Start powinien zostać zablokowany z czytelnym komunikatem **albo** zapis kończy się błędem bez utraty temp.

### 9A. R-004 — monitoring w trakcie

1. Użyj kontrolowanego małego woluminu; nie zapełniaj dysku systemowego.
2. Rozpocznij nagranie przy przestrzeni większej od budżetu startowego.
3. W trakcie nagrania zmniejsz wolną przestrzeń poniżej wartości potrzebnej na kolejną minutę i processing.
4. Oczekiwane w ciągu około 5 s: jedno ostrzeżenie, pojedynczy automatyczny stop, brak zawieszenia.
5. Jeśli miks jest możliwy, sprawdź MP3. Jeśli nie, potwierdź zachowanie obu WAV i manifestu do recovery.
6. Powtórz z Temp i folderem nagrań na różnych woluminach oraz na tym samym woluminie.
7. Powtórz z `keepSeparateTracks=true`; wymagany budżet powinien uwzględniać dwa dodatkowe WAV.

## 10. Wybudzenie ze snu

1. Start nagrywania.
2. Uśpij komputer na 1–2 minuty, wybudź.
3. Zatrzymaj nagranie — sprawdź spójność pliku (cisze w trakcie snu są akceptowalne).

## 11. Pojedyncza instancja

1. Uruchom aplikację.
2. Uruchom ponownie — druga instancja nie startuje; komunikat o już działającej aplikacji.

### 11A. R-003 — wyjście podczas nagrywania

1. Rozpocznij nagrywanie i wybierz z tray `Wyjście`.
2. Wybierz `Tak`; oczekiwane: UI pokazuje zapisywanie, pozostaje responsywne, powstaje MP3 i proces się kończy.
3. Powtórz i wybierz `Nie`; oczekiwane: proces się kończy, pliki temp oraz manifest pozostają do recovery.
4. Powtórz i wybierz `Anuluj`; oczekiwane: nagrywanie trwa dalej.
5. Kliknij `Wyjście` podczas przetwarzania; oczekiwane: komunikat „Proszę czekać”, bez zawieszenia i bez drugiego stopu.
6. Wymuś błąd zapisu; oczekiwane: aplikacja pyta, czy wyjść z zachowaniem temp, albo pozwala pozostać uruchomiona.

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

### 14A. R-007 — transakcyjna zmiana skrótu

1. Potwierdź, że zapisany skrót A uruchamia i zatrzymuje nagrywanie.
2. Otwórz ustawienia, zmień również dowolny parametr miksu i wybierz skrót B zajęty przez inną aplikację.
3. Zapisz; oczekiwane: czytelny błąd, skrót A nadal działa, a po ponownym otwarciu ustawień parametr miksu i skrót nadal mają poprzednie wartości.
4. Zwolnij skrót B i zapisz ponownie; oczekiwane: B działa, A przestaje działać, a nowe ustawienia są zapisane.
5. Zamknij i uruchom aplikację; oczekiwane: działa skrót B.
6. Powtórz kilka zmian A → B → C, sprawdzając, że pojedyncze naciśnięcie wywołuje dokładnie jedną akcję.

## 15. R-008 — anulowanie przetwarzania

1. Przygotuj dłuższe nagranie, aby miksowanie i kodowanie trwało zauważalnie długo.
2. Rozpocznij zatrzymywanie, a następnie anuluj token operacji z testowego hosta podczas miksowania.
3. Oczekiwane: zapis zatrzymuje się na granicy bufora, nie powstaje finalny MP3 ani plik osobnej ścieżki z rozszerzeniem `.partial`.
4. Potwierdź, że źródłowe WAV, manifest i ewentualny roboczy miks pozostają w Temp i można wykonać recovery.
5. Powtórz anulowanie podczas kodowania Media Foundation.
6. Oczekiwane: reakcja może nastąpić dopiero przy kolejnym odczycie PCM lub po krótkiej finalizacji MF, ale MP3 `.partial` zostaje usunięty i nie jest publikowany pod nazwą finalną.

## 16. R-009 — diagnostyka sesji

1. Wykonaj krótkie nagranie z ciszą loopback, odcinkiem aktywnego dźwięku i co najmniej jedną potwierdzoną luką.
2. Otwórz log sesji w `%LOCALAPPDATA%\MeetingAudioRecorder\Logs`.
3. Potwierdź obecność formatu, `BlockAlign`, liczby ramek mikrofonu/loopbacku, liczby ramek ciszy, korekty dryfu oraz rozmiarów plików.
4. Porównaj liczby ramek z czasem plików przy 44,1 i 48 kHz.
5. Przejrzyj log i potwierdź, że nie zawiera wartości próbek, fragmentów audio ani transkrypcji.
6. Do raportu sprzętowego dołącz wersję Windows, urządzenia, endpointy, formaty i wynik checklisty — bez dołączania treści nagrania, jeśli nie jest potrzebna.

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
| 19 | Calendar bez dołączenia nie uruchamia nagrania | |
| 20 | Obecność właściwego konta uruchamia jedną sesję | |
| 21 | Potwierdzone wyjście zatrzymuje tylko sesję automatyczną | |
| 22 | Brak sieci/API nie zatrzymuje aktywnego nagrania | |
| 23 | Automatyczny start wybiera endpointy aktywnej przeglądarki | |
| 24 | Brak detekcji używa zapisanych urządzeń bez zmiany ustawień | |
| 25 | Link Meet bez Calendar uruchamia nagranie dopiero po faktycznym dołączeniu | |
| 26 | Rozszerzenie komunikuje się wyłącznie przez dozwolony lokalny Native Host | |
