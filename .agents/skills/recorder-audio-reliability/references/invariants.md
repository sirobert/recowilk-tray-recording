# Inwarianty audio i recovery

## Jednostki

- Ramka zawiera próbkę każdego kanału dla jednego punktu czasu.
- `BlockAlign` określa liczbę bajtów ramki.
- Pozycję na osi czasu przechowuj jako liczbę ramek lub monotoniczne ticki z jawną konwersją.
- Długość wyjścia nie może zależeć od tego, czy timer wykonał się tuż przed callbackiem.

## Capture

- Bufor WASAPI reprezentuje przedział czasu, który już upłynął.
- Ciszę wolno dodać tylko dla potwierdzonej luki między końcem ostatniego bufora a początkiem następnego.
- Początek obu źródeł musi odnosić się do tego samego zegara.

## Recovery

- Rozmiar pliku większy niż nagłówek nie dowodzi poprawności WAV.
- Sprawdź sygnatury RIFF/WAVE, format, `BlockAlign`, granice chunków i rzeczywistą długość danych.
- Odetnij niepełną końcową ramkę.
- Zachowaj oryginał, dopóki naprawiona kopia nie przejdzie walidacji.
- Manifest sesji powinien być zapisywany atomowo i wersjonowany.

## Publikacja

- Pliki źródłowe usuń dopiero po walidacji MP3 i atomowym opublikowaniu wyniku.
- Anulowanie lub błąd pozostawia materiał umożliwiający ponowne recovery.

