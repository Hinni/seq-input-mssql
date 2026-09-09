# Projekt-Review: Seq.Input.MSSql

**Stand:** 2026-09-09 · **Basis:** `master` @ `5c8d84a` · **Version:** 2.0.0 · **Target:** .NET 10.0

Statische Analyse des Codestands. Build und Tests konnten in der Review-Umgebung nicht
ausgeführt werden (kein .NET SDK verfügbar), alle Aussagen stammen aus dem Quelltext.

---

## 1. Überblick

Seq.Input.MSSql ist ein **Custom Input Plugin für [Seq](https://datalust.co/seq)**. Es pollt in
einem festen Intervall eine MSSQL-Tabelle oder -View, wandelt jede neue Zeile in ein
Serilog-`LogEvent` um und schreibt es als Compact-JSON auf den von Seq bereitgestellten
`TextWriter`. Seq übernimmt die Ingestion.

Das Projekt ist eine schlanke Bibliothek: **6 Quelldateien, ca. 1.060 Zeilen Produktivcode**,
drei NuGet-Abhängigkeiten (`Microsoft.Data.SqlClient`, `Seq.Apps`, `Serilog.Formatting.Compact`),
keine eigene Infrastruktur. Erste Commits stammen aus 10/2019, die Weiterentwicklung erfolgt
seit 2023 überwiegend über Dependency-Updates.

### Komponenten

| Datei | LOC | Aufgabe |
|---|---|---|
| `MSSQLInput.cs` | 474 | Seq-App-Einstiegspunkt: Deklaration aller UI-Settings, Aufbau des ConnectionStrings, Lifecycle (`Start`/`Stop`/`Dispose`/`OnAttached`) |
| `Executor.cs` | 368 | Ein Poll-Durchlauf: Zeitfenster ermitteln, Query bauen, ausführen, Zeilen → `LogEvent` mappen |
| `SqlConfig.cs` | 103 | Statischer Konfigurations-Container + Parser für Key/Value-Mappings |
| `ExecutorTask.cs` | 62 | Hintergrund-Task mit Intervall-Schleife, Zeitfensterprüfung und Re-Entrancy-Lock |
| `TextWriterSink.cs` | 32 | Serilog-Sink, der `LogEvent`s als Compact-JSON serialisiert (thread-safe via `lock`) |
| `TimePeriodHelper.cs` | 25 | Validierung und Prüfung des Zeitfensters `HH:mm-HH:mm` |

### Ablauf

```
Seq startet Instanz
  → OnAttached()      Settings validieren + in statisches SqlConfig übertragen
  → Start(writer)     ConnectionString bauen, Query "SELECT * FROM <Table>", ExecutorTask starten
      └─ Schleife (alle QueryEverySeconds):
           Zeitfenster gültig? und Lock frei?
             → Executor.Start()
                  lastScan.txt lesen        (letzter Zeitstempel)
                  runTime = Now - SecondsDelay
                  WHERE <TS> > last AND <TS> <= runTime [AND <Filter>]
                  ExecuteReader
                  lastScan.txt = runTime    (vor dem Auslesen der Zeilen!)
                  je Zeile → LogEvent → CompactJson → TextWriter
  → Stop()            CancellationToken auslösen, auf Task warten
```

---

## 2. Vorhandene Funktionalitäten

### 2.1 Verbindung und Abfrage

- **Serverangabe** inkl. optionalem Port (`SERVERNAME,1433`) und Initial Catalog.
- **Authentifizierung** wahlweise über Windows Integrated Security oder SQL-Benutzer/Passwort.
- **Verschlüsselung**: Checkbox `Encrypt` plus `TrustServerCertificate` für selbstsignierte Zertifikate.
- **Timeouts**: Connect-Timeout (1–120 s, Default 15) und Command-Timeout (1–300 s, Default 60),
  beide über den ConnectionStringBuilder gesetzt und beim Einlesen auf gültige Bereiche geklemmt.
- **Application Name** wird auch im ConnectionString gesetzt, sodass die Instanz serverseitig
  (z. B. in `sys.dm_exec_sessions`) identifizierbar ist.
- **Quelle** ist eine Tabelle oder View (`SELECT * FROM <TableOrViewName>`), optional ergänzt um
  eine frei formulierbare **zusätzliche Filter-Klausel**.

### 2.2 Inkrementelles Polling

- **Intervall** frei konfigurierbar (`QueryEverySeconds`, Default 15 s).
- **Fortschrittsspeicherung** in `lastScan.txt` im Seq-Storage-Pfad der Instanz
  (Roundtrip-Format `"O"`).
- **Query-Delay** (`SecondsDelay`, 1–86400 s, Default 1): das obere Fensterende wird zurückversetzt,
  damit verspätet eingefügte Zeilen und Zeitstempel ohne Millisekunden-Auflösung nicht verloren gehen.
- **Erststart-Schutz**: existiert keine oder eine leere `lastScan.txt`, wird das Fenster auf
  **die letzten 24 Stunden** begrenzt, statt die gesamte Tabelle zu ingestieren.
- **Kompensation von Zeitsprüngen** (z. B. Sommer-/Winterzeit): liegt der gespeicherte Zeitstempel
  in der Zukunft, wird er auf `runTime - 1s` zurückgesetzt. Ebenso wird ein nachträglich erhöhter
  `SecondsDelay` abgefangen, der sonst ein leeres oder invalides Fenster erzeugen würde.
- **Zeitfenster** (`TimePeriod`, Format `HH:mm-HH:mm`, Default `00:00-23:59`): Abfragen laufen nur
  innerhalb der lokalen Tageszeitspanne. Ungültige Eingaben werden per Regex erkannt, protokolliert
  und durch den Default ersetzt.
- **Re-Entrancy-Schutz** über `Interlocked.CompareExchange`, damit ein langsamer Durchlauf nicht
  parallel erneut startet.
- **Sauberes Beenden** über `CancellationTokenSource`; `OperationCanceledException` wird als
  Information, alle übrigen Ausnahmen als `Fatal` protokolliert.

### 2.3 Event-Mapping

- **Zeitstempel** und **Message** aus konfigurierbaren Spalten (Pflichtfelder); NULL-Messages werden
  zu einem leeren String.
- **Message-Template-Binding** über `ILogger.BindMessageTemplate`, d. h. `{Platzhalter}` im
  Nachrichtentext werden von Seq als Template erkannt.
- **Event-Level**: fester Default (0–5 numerisch) plus optionale Ableitung aus einer Spalte über ein
  Mapping (`Highest=Fatal,Error=Error,…`). Greift das Mapping nicht, gilt der Default.
- **Zusätzliche Properties**: kommaseparierte Spaltenliste, deren Werte als strukturierte
  Eigenschaften an das Event gehängt werden.
- **Application-Property**: Name und Wert frei wählbar; ohne Angabe wird der Instanztitel unter
  `Application` gesetzt.
- **Tags**: entweder eine feste Liste (`a,b,c`) für alle Events oder ein Mapping aus einer Spalte.
- **Interop-Properties für nachgelagerte Seq-Apps** (OpsGenie, Atlassian Jira) — jeweils aus einer
  Spalte, jeweils optional über ein Key/Value-Mapping übersetzbar, sonst wird der Rohwert übernommen:
  `Priority`, `Responders`, `ProjectKey`, `InitialEstimate`, `RemainingEstimate`, `DueDate`.
- **Mapping-Lookups** sind durchgängig case-insensitiv.
- **Ausgabe** als Compact-JSON, serialisiert unter `lock` und nach jedem Event geflusht.

### 2.4 Diagnose

- Schaltbares **Debug-Logging**: Beim Attach werden sämtliche übernommenen Einstellungen einzeln
  protokolliert; zur Laufzeit zusätzlich der generierte Query-String, das abgefragte Zeitfenster
  und der Zustand von `lastScan.txt`.

### 2.5 Build und Wartung

- **GitHub-Actions-Pipeline** (`build.yml`): Job `test` (restore/build/test) als Gate für Job
  `build` (publish → pack → Upload des `.nupkg` als Artefakt). Alle Actions sind auf SHA gepinnt,
  `permissions: contents: read`.
- **Dependabot** für beide Projekte, tägliche Prüfung mit Cooldown (6 Tage, Patches 3 Tage).
- `TreatWarningsAsErrors` ist im Hauptprojekt aktiv.
- **Tests**: xUnit, zwei Klassen, 11 Fälle — Zeitfenster-Validierung und Priority-Mapping-Parsing.

---

## 3. Befunde

Sortiert nach Schwere. Zeilenangaben beziehen sich auf `5c8d84a`.

### 3.1 Kritisch

**F-1 · Globaler statischer Zustand verhindert mehrere Instanzen** — `SqlConfig.cs`

`SqlConfig` ist eine rein statische Klasse, und `OnAttached()` schreibt sämtliche Settings dort
hinein. Legt ein Anwender in Seq zwei MSSQL-Input-Instanzen an (zwei Datenbanken, zwei Tabellen),
überschreiben sie sich gegenseitig: Die zuletzt gestartete Instanz bestimmt Spaltennamen, Mappings
und Filter **für alle** — der `Executor` liest ausschließlich aus `SqlConfig`, nie aus den ihm
übergebenen Werten. Das Ergebnis sind stillschweigend falsch gemappte Events, kein Fehler.

Dasselbe gilt für `ExecutorTask._lockState` (`ExecutorTask.cs:13`): Auch dieses Feld ist `static`,
das Re-Entrancy-Lock ist also prozessweit statt pro Instanz.

*Empfehlung:* `SqlConfig` in eine Instanzklasse überführen, in `Start()` einmal befüllen und dem
`Executor` per Konstruktor übergeben. Das ist die Voraussetzung für alle weiteren Aufräumarbeiten
und betrifft im Wesentlichen nur `OnAttached()` und die Zugriffe im `Executor`.

**F-2 · Lock wird auch von der Instanz freigegeben, die es nicht hält** — `ExecutorTask.cs:34-43`

```csharp
var currentState = Interlocked.CompareExchange(ref _lockState, Locked, Available);
if (IsValidTimePeriod(...) && currentState == Available) { await executor.Start(); }
currentState = Interlocked.CompareExchange(ref _lockState, Available, Locked);
```

Die Freigabe erfolgt unbedingt — auch dann, wenn der Durchlauf gerade übersprungen wurde, weil ein
anderer läuft. Zusammen mit F-1 hebt der Skip-Pfad eines Tasks das Lock des tatsächlich laufenden
auf. Zudem bleibt das Lock bei einer Exception in `executor.Start()` zwar nicht hängen (die
Freigabe steht außerhalb eines `try`, wird aber durch das umschließende `catch` übersprungen — der
Task endet dann ohnehin). Die Freigabe gehört in ein `finally` und nur in den Zweig, der das Lock
erworben hat.

**F-3 · Fensterende wird vor dem Auslesen der Zeilen persistiert** — `Executor.cs:106-112`

`lastScan.txt` wird unmittelbar nach `ExecuteReaderAsync()` geschrieben, bevor die erste Zeile
gelesen wurde. Bricht das Auslesen ab (Netzwerkfehler, Command-Timeout, Prozessende, siehe F-5),
gilt das Fenster als verarbeitet und **die Events sind dauerhaft verloren**. Der Zeitstempel sollte
erst nach der Verarbeitungsschleife geschrieben werden.

### 3.2 Hoch

**F-4 · Passwort landet im Klartext im Debug-Log** — `Executor.cs:98-102`

```csharp
_logger.ForContext("ConnectionString", _connectionString) … .Debug(...)
```

`_connectionString` enthält bei SQL-Authentifizierung `Password=…`. Bei aktiviertem Debug-Logging
wird es in jedem Poll-Durchlauf nach Seq geschrieben und ist dort für jeden Leseberechtigten
sichtbar und durchsuchbar. Das Passwort muss vor dem Logging entfernt werden (z. B. über einen
zweiten `SqlConnectionStringBuilder` mit geleertem `Password`).

**F-5 · Nur `SqlException` wird gefangen; alles andere beendet den Input** — `Executor.cs:327`

`Executor.Start()` fängt ausschließlich `SqlException`. Realistische andere Fehler:

- `InvalidCastException` — `GetString()` auf einer nicht-`string`-Spalte (Priority, Tags, Responder
  usw. sind in der Praxis häufig `int` oder `tinyint`);
- `IndexOutOfRangeException` / `ArgumentOutOfRangeException` — `GetDateTime(timeStampIndex)` bei
  `timeStampIndex == -1`, wenn der konfigurierte Zeitstempel-Spaltenname nicht in der View existiert
  (die gefundenen Indizes werden **nirgends** validiert, `Executor.cs:115-121`);
- `FormatException` — `DateTime.Parse` auf einer beschädigten `lastScan.txt` (`Executor.cs:62`);
- `IOException` / `UnauthorizedAccessException` beim Zugriff auf `lastScan.txt`.

Jede davon propagiert in `ExecutorTask.Run`, wird dort als `Fatal` geloggt und **beendet die
Schleife endgültig**. Der Input ist bis zum Neustart der Instanz tot — für den Anwender sichtbar
nur als eine einzelne Fatal-Meldung. Empfehlung: Pro Durchlauf breit fangen und weiterlaufen, dazu
`timeStampIndex`/`messageIndex` direkt nach dem Schema-Abruf prüfen und mit klarer Meldung abbrechen.

**F-6 · Zeitfenster über Mitternacht wird akzeptiert, funktioniert aber nie** — `TimePeriodHelper.cs`

`IsStringValid("20:00-06:00")` liefert `true` (der Test `TimePeriodHelperTest.cs:38` zementiert das
sogar für `20:00-12:50`), aber `IsValidTimePeriod` bildet beide Grenzen auf **denselben Tag** ab und
prüft `dateTime >= start && dateTime <= end`. Für ein Fenster über Mitternacht ist das nie erfüllt —
der Input pollt schweigend nie. Genau dieses Fenster ("nachts, außerhalb der Geschäftszeiten") ist
der plausibelste Anwendungsfall der Einstellung.

Nebenbefund: Der Regex `[0-2][0-9]` akzeptiert Stunden bis `29`.

*Empfehlung:* `if (end < start) end = end.AddDays(1)` plus entsprechende Behandlung im Vergleich,
und den irreführenden Testfall korrigieren.

**F-7 · SQL-Injection über die Konfiguration** — `MSSQLInput.cs:312`, `Executor.cs:44-94`

`TableOrViewName` und `AdditionalFilterClause` werden ungeprüft in den Query-String konkateniert,
ebenso `ColumnNameTimeStamp` und die formatierten Zeitstempel. Der Angriffsvektor ist begrenzt —
nur Seq-Administratoren konfigurieren Apps, und wer das darf, hat ohnehin die DB-Zugangsdaten in
derselben Maske —, aber es bleibt eine Privilege-Escalation-Fläche und ein Robustheitsproblem
(Tabellennamen mit Sonderzeichen, Schema-qualifizierte Namen). Zumindest die Zeitstempel-Grenzen
sollten als `SqlParameter` übergeben und Bezeichner über `QUOTENAME`-Logik gequotet werden.

### 3.3 Mittel

**F-8 · `DbDataReader` wird nicht disposed** — `Executor.cs:106`

`var dataReader = await command.ExecuteReaderAsync();` steht ohne `using`. Connection und Command
werden zwar entsorgt, was den Reader mitschließt, aber die explizite Freigabe fehlt.

**F-9 · `Dispose()` auf einem laufenden Task** — `ExecutorTask.cs:20-24`

`Task.Dispose()` wirft `InvalidOperationException`, wenn der Task noch läuft. Ruft Seq `Dispose()`
ohne vorheriges `Stop()` auf (`MSSQLInput.Dispose()` delegiert direkt weiter, ohne zu stoppen),
fliegt die Ausnahme. Zusätzlich ist `Task.Dispose()` seit .NET Core generell überflüssig. Empfehlung:
in `Dispose()` erst `Cancel()`/`Wait()`, dann nur die `CancellationTokenSource` entsorgen.

**F-10 · Die `Encrypt`-Checkbox schaltet nichts ab** — `MSSQLInput.cs:322-326`

`stringBuilder.Encrypt` wird nur gesetzt, **wenn** `SqlConfig.Encrypt == true`. Ab
Microsoft.Data.SqlClient 4.0 ist der Default aber bereits `Encrypt=True`. Eine nicht angehakte
Checkbox bedeutet also nicht "unverschlüsselt", sondern "Default" — was mit dem Hilfetext
("Use encryption on this connection") kollidiert und bei Servern ohne Zertifikat zu
Verbindungsfehlern führt, die der Anwender genau hier zu beheben versucht. Der Wert sollte
unbedingt gesetzt (`stringBuilder.Encrypt = SqlConfig.Encrypt`) und der Hilfetext angepasst werden.

**F-11 · `ColumnNamesInclude` ist Pflichtfeld ohne Null-Schutz** — `MSSQLInput.cs:397`

`SqlConfig.SplitAndTrim(',', ColumnNamesInclude)` ruft `setting.Split(...)` ohne Null-Prüfung.
Das Feld ist zwar `IsOptional = false`, aber der Anwendungsfall "nur Zeitstempel und Message, keine
weiteren Properties" ist legitim und endet in einer `NullReferenceException` beim Attach.
`SplitAndTrim` sollte bei `null`/leer eine leere Sequenz liefern.

**F-12 · Keine Zeitzonenbehandlung** — `Executor.cs:48, 50, 83`

Fensterberechnung und `LogEvent`-Zeitstempel arbeiten durchgängig mit `DateTime.Now` bzw. der
impliziten `DateTime → DateTimeOffset`-Konvertierung, also mit der lokalen Zone des Seq-Servers.
Enthält die Quellspalte UTC-Werte (bei `datetime2`/`sysutcdatetime()` der Normalfall), sind sowohl
Filterfenster als auch die in Seq angezeigten Zeitpunkte um den Zonenoffset verschoben. Es gibt
keine Einstellung, um die Quelle als UTC zu deklarieren.

**F-13 · `Tags` aus Spalte und feste Tag-Liste schließen sich aus** — `Executor.cs:180-195`

Ist `ColumnNameTags` gesetzt, wird die feste Tag-Liste ignoriert; greift das `TagMappings`-Lookup
für einen Zeilenwert nicht, bekommt das Event **gar keine** Tags. Die Kombination "Basis-Tags für
alle Events plus ein Tag aus der Spalte" ist nicht ausdrückbar, obwohl `tags` als `List<string>`
genau dafür angelegt ist.

**F-14 · Ergebnis von `BindProperty` beim Application-Property ignoriert** — `Executor.cs:306-316`

Als einzige Stelle im `Executor` prüft dieser Aufruf den Rückgabewert nicht und übergibt
`applicationProperty` direkt an `AddOrUpdateProperty`. Bei `false` wäre das eine
`NullReferenceException`. Für konstante Strings praktisch unkritisch, aber inkonsistent zum
restlichen Code.

**F-15 · Keine Sortierung der Ergebnismenge** — `MSSQLInput.cs:312`

`SELECT * FROM <Table>` ohne `ORDER BY`. Für die Ingestion selbst unkritisch (Seq sortiert nach
Zeitstempel), aber die Reihenfolge der geschriebenen Events ist nicht deterministisch — bei einem
Abbruch mitten im Durchlauf (F-3) ist damit auch nicht bestimmbar, was verarbeitet wurde.

### 3.4 Niedrig / Aufräumarbeiten

- **Uneinheitliche Namespaces**: `Seq.Input.MsSql` (Executor, ExecutorTask, MSSQLInput,
  TextWriterSink) vs. `Seq.Input.MSSql` (SqlConfig, TimePeriodHelper). Jede Datei braucht dadurch
  beide `using`s. Auf einen Namespace vereinheitlichen.
- **Tote Solution-Referenz**: `Seq.Input.MSSql.sln` listet `azure-pipelines.yml` unter
  SolutionItems, die Datei existiert seit dem Umzug auf GitHub Actions nicht mehr.
- **Massive Duplikation in `Executor.cs`**: Sechs Property-Blöcke (Priority, Responder, ProjectKey,
  InitialEstimate, RemainingEstimate, DueDate) sind zeichengleich bis auf drei Bezeichner —
  ca. 100 der 368 Zeilen. Eine Hilfsmethode
  `MapOptional(reader, index, mapping, propertyName, logEvent)` reduziert das auf sechs Aufrufe.
  Analog in `OnAttached()`, wo derselbe `if (Debug) Log.Debug(...)`-Zweig 30-mal steht.
- **`Enum.Parse` statt Cast**: `Executor.cs:162` erzeugt den Default-Level über
  `Enum.Parse(typeof(LogEventLevel), SqlConfig.LogEventLevel.ToString())` — ein
  `(LogEventLevel)SqlConfig.LogEventLevel` genügt. Zudem wird der konfigurierte Wert nirgends gegen
  den gültigen Bereich 0–5 geprüft, anders als alle anderen numerischen Settings.
- **Konstante Neuallokation**: `new TextWriterSink(_textWriter)` wird pro Zeile erzeugt
  (`Executor.cs:320`); eine Instanz je `Executor` reicht — dann greift auch das interne `lock`
  tatsächlich über alle Events.
- **`_query`/`SqlConfig.TableOrViewName` doppelt geführt**: Der Query wird in `Start()` gebaut und
  übergeben, der Tabellenname parallel in `SqlConfig` abgelegt und dort nie verwendet.
- **`DateTime.Parse` ohne Kultur**: geschrieben wird mit `"O"`, gelesen mit
  `DateTime.Parse(content)` (`Executor.cs:62`) — kulturabhängig. `DateTime.ParseExact` mit
  `"O"`/`InvariantCulture` und `RoundtripKind` ist robuster.
- **`ParseKeyPairList` ist nicht fehlertolerant**: Ein einzelnes ungültiges Paar lässt die gesamte
  Mapping-Liste verwerfen (`return false`), ohne dass der Anwender erfährt, welcher Eintrag schuld
  war — und `OnAttached()` loggt in diesem Fall gar nichts. Zudem wirft `mappings.Add` bei
  doppeltem Key eine `ArgumentException`, die den Attach abbricht.
- **`SqlConfig.Available`/`Locked`** sind Lock-Konstanten und gehören zu `ExecutorTask`, nicht in
  den Konfigurations-Container.

---

## 4. Tests

11 Testfälle in zwei Klassen. Bewertung:

- `TimePeriodHelperTest` deckt `IsValidTimePeriod` (drei Fälle) und `IsStringValid` (sechs Fälle)
  ab. Der Fall `("20:00-12:50", true)` dokumentiert den Regex korrekt, verdeckt aber F-6.
- `ParseTest` testet `SqlConfig.ParseKeyPairList` **indirekt** — die Lookup-Methode
  `TryGetPropertyValueCI` ist im Test als private Kopie der Produktivmethode aus `Executor.cs`
  dupliziert. Ändert sich das Original, bleibt der Test grün. Die Methode gehört nach `SqlConfig`
  (oder in eine Mapping-Klasse) und sollte von beiden Seiten genutzt werden.

Ungetestet bleiben: der gesamte `Executor` (Fensterberechnung, `lastScan.txt`-Handling,
Spaltenauflösung, Property-Mapping), `ExecutorTask` (Lock, Cancellation), `TextWriterSink`,
`ParseEventKeyPairList` und die Settings-Validierung in `OnAttached`.

Die Fensterlogik in `Executor` ist reine Zeitarithmetik ohne DB-Bezug und ließe sich nach einer
Extraktion in eine eigene Methode (`CalculateWindow(lastScan, now, secondsDelay)`) ohne Mocking und
ohne zusätzliche Abhängigkeit testen — der lohnendste nächste Testschritt.

---

## 5. Build und Release

Die Pipeline ist sauber aufgesetzt: SHA-gepinnte Actions, minimale `permissions`, Test-Job als Gate.

Lücken:

- **Kein Release-Pfad.** Das `.nupkg` wird als Artefakt mit **1 Tag Retention** hochgeladen, aber
  nirgends nach NuGet gepusht. Der Weg von einem grünen `master`-Build zum veröffentlichten Paket
  ist manuell und undokumentiert.
- **Keine Tags im Repository.** `VersionPrefix` steht auf `2.0.0`, es existiert kein einziges
  Git-Tag — welcher Commit welchem veröffentlichten Paket entspricht, ist nicht nachvollziehbar.
- **Kein CHANGELOG.** `PackageReleaseNotes` in der `.csproj` beschreibt seit mehreren Versionen
  unverändert dasselbe Feature.
- **Kein `dotnet format`/Analyzer-Schritt**, obwohl `TreatWarningsAsErrors` gesetzt ist.
- Der `build`-Job wiederholt `restore`, statt die Artefakte des `test`-Jobs zu übernehmen.

Ein `release.yml`, das auf `push: tags: v*` ein `dotnet nuget push` gegen einen
`NUGET_API_KEY`-Secret ausführt, schließt die größte Lücke mit wenigen Zeilen.

---

## 6. Dokumentation

Die `README.md` umfasst 13 Zeilen: Installationshinweis, Update-Notiz, Contributors. Die App bringt
**33 Konfigurationseinstellungen** mit — davon 25 optional, mit teils nicht offensichtlicher
Semantik (Key/Value-Mappings, `SecondsDelay`, Zeitfenster, das Zusammenspiel von `Tags` und
`ColumnNameTags`). Nichts davon ist außerhalb der `HelpText`-Attribute dokumentiert.

Fehlend und mit überschaubarem Aufwand nachzureichen:

- Ein durchgerechnetes Beispiel: View-Definition + passende Einstellungen + resultierendes Event.
- Erklärung von `lastScan.txt` (Ort, Format, was ein Löschen bewirkt).
- Hinweise zum Zeitzonenverhalten (siehe F-12) und zur Erstlauf-Begrenzung auf 24 Stunden.
- Die Interop-Properties und wofür OpsGenie/Jira sie erwarten.
- Kompatibilitätsangabe: welche Seq-Version wird für ein `net10.0`-Plugin benötigt.

---

## 7. Gesamtbewertung

Das Projekt erfüllt seinen Zweck und ist für seinen Funktionsumfang bemerkenswert schlank —
drei Abhängigkeiten, keine überflüssigen Abstraktionen, ein klar erkennbarer Ablauf. Die
Abhängigkeiten sind über Dependabot aktuell gehalten, die Pipeline ist ordentlich abgesichert.

Die Substanz der Kritik liegt in drei Punkten:

1. **Der statische Zustand (F-1/F-2)** ist der schwerwiegendste Konstruktionsfehler. Er bricht
   still — mehrere Instanzen liefern falsche Ergebnisse, ohne einen Fehler zu erzeugen. Die
   Umstellung auf Instanzzustand ist die Voraussetzung für fast alles andere.
2. **Zwei Datenverlust-/Ausfallpfade (F-3/F-5)**: Das Fensterende wird zu früh persistiert, und
   jede nicht-`SqlException` beendet den Input dauerhaft. Beides sind kleine, lokale Korrekturen
   mit großer Wirkung auf die Betriebssicherheit.
3. **Das Passwort im Debug-Log (F-4)** ist eine Ein-Zeilen-Korrektur und sollte sofort erfolgen.

Die Testabdeckung (11 Fälle für ~1.060 Zeilen, der Kern ungetestet) und die dünne Dokumentation
sind die beiden Bereiche, in denen zusätzlicher Aufwand am meisten zurückzahlt.

### Empfohlene Reihenfolge

| Priorität | Maßnahmen |
|---|---|
| **Sofort** | F-4 (Passwort-Maskierung), F-3 (`lastScan.txt` erst nach der Schleife schreiben) |
| **Kurzfristig** | F-5 (Exception-Handling + Spaltenindex-Validierung), F-6 (Mitternachts-Fenster), F-10 (`Encrypt`), F-11 (Null-Schutz) |
| **Mittelfristig** | F-1/F-2 (Instanzzustand statt `static`), F-7 (parametrisierte Query), Tests für die Fensterlogik |
| **Laufend** | Duplikation in `Executor`/`OnAttached` reduzieren, Namespaces vereinheitlichen, README ausbauen, Release-Workflow + Tags |
