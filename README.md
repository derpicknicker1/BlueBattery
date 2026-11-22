# BlueBattery

Kleine Windows-Tray-Anwendung (.NET 10, WinForms), die die Akkustände der gekoppelten Bluetooth-Geräte anzeigt.

- Das Tray-Icon zeigt den **niedrigsten** Ladezustand aller verbundenen Geräte – oder wahlweise den eines bestimmten Geräts.
- Tooltip und Menü (Rechts- oder Linksklick) listen alle Geräte mit Akkustand auf.
- Aktualisierung in einstellbarem Intervall (Standard 60 s) oder über „Jetzt aktualisieren“.

## Einstellungen

Über „Einstellungen …“ im Tray-Menü; gespeichert in `%APPDATA%\BlueBattery\settings.json`.

- **Akkustand-Meldung**: Windows-Benachrichtigung, sobald ein verbundenes Gerät den Grenzwert erreicht (Standard 25 %);
  optional in einem Intervall (Minuten) wiederholt, solange das Gerät unter dem Grenzwert bleibt.
- **Akkustandsangabe im Symbol**: Zahl im Icon oder stattdessen ein Batteriesymbol mit Füllstand.
- **Kritischer Grenzwert / Niedriggrenzwert** mit frei wählbaren Farben (Klick auf die Vorschau).
- **Getrennte Geräte**: im Menü grau mit zuletzt bekanntem Akkustand anzeigen oder ausblenden. Für das Tray-Icon zählen nur verbundene Geräte.
  Da Windows den Akkustand beim Trennen verwirft, merkt sich BlueBattery die Werte in `%APPDATA%\BlueBattery\devices.json`;
  „Getrennte Geräte vergessen“ im Menü leert diese Liste.
- **Aktualisierungsintervall** und **Autostart** mit Windows.

## Woher kommen die Werte?

Windows legt den Akkustand von Bluetooth-Geräten in der Geräteeigenschaft `DEVPKEY_Bluetooth_Battery`
(`{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2`) ab – für klassische Geräte über das Hands-Free-Profil
und für Bluetooth-LE-Geräte über den GATT Battery Service. Es werden also genau die Geräte angezeigt,
für die auch die Windows-Einstellungen einen Akkustand zeigen.

## Voraussetzungen

- Windows 10 (1809) oder neuer
- .NET 10 SDK

## Bauen und starten

```powershell
dotnet run --project src/BlueBattery
```

Eigenständige EXE erzeugen:

```powershell
dotnet publish src/BlueBattery -c Release -r win-x64 --self-contained false -o publish
```
