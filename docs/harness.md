# Characterization Harness

Deze harness is het vangnet voor elke refactor-commit. Het runt een vast scenario
headless, schrijft een SHA256-digest per seconde sim-tijd, en moet na elke refactor
byte-identiek blijven met de baseline.

## Wat de harness doet

- Laadt `Maps/01_open_field.png` (fixture).
- Construeert een `World` met seed `42` (deterministisch).
- Voegt kolonies toe in dezelfde volgorde als `MapLoader.ColonySeeds`.
- Draait 60 seconden sim-tijd = 3600 ticks @ 60 Hz.
- Schrijft elke seconde een snapshot + SHA256 naar disk.

## Uitvoeren

Vanuit de projectroot:

```
dotnet run -- --harness
```

Of met expliciete parameters:

```
dotnet run -- --harness seed=42 map=01_open_field seconds=60
```

## Output

Twee bestanden in `bin\Debug\net10.0-windows\CharacterizationOutput\` (of waar het exe
staat, via `AppContext.BaseDirectory`):

| Bestand | Inhoud | Vergelijking |
|---|---|---|
| `actual.txt` | Header + `t=<tick> sha256=<hex>` per seconde | Moet byte-identiek zijn met `expected.txt` |
| `actual_detail.txt` | Hetzelfde + volledige snapshot per lijn | Voor diagnostiek wanneer digests divergeren |

## Baseline vaststellen (eerste run)

1. Zorg dat `Simulation/World.cs`, `Program.cs`, `.editorconfig`, en
   `Tests/Characterization/CharacterizationHarness.cs` in de repo staan.
2. Bouw en draai:
   ```
   dotnet build
   dotnet run -- --harness
   ```
3. Controleer dat het exitcode 0 teruggeeft.
4. Open `bin\Debug\net10.0-windows\CharacterizationOutput\actual.txt`.
5. Kopieer dat bestand naar `Tests/Characterization/expected.txt` in de repo en commit.
6. Draai de harness **nog een keer** om determinisme te bewijzen — de twee `actual.txt`
   bestanden moeten identiek zijn. Als ze verschillen, is er ergens een RNG of iteratie-
   volgorde die niet aan de seed hangt. Stop dan; dat is een FASE-0 bug die eerst
   gefixt moet worden voor enige refactor.

## Regressie-test na refactor

Na elke refactor-commit:

```
dotnet build
dotnet run -- --harness
fc /b bin\Debug\net10.0-windows\CharacterizationOutput\actual.txt Tests\Characterization\expected.txt
```

`fc /b` moet "no differences encountered" melden. Zo niet: de refactor heeft gedrag
veranderd en moet gereverteerd worden.

## Wat in de digest zit (per colony, gesorteerd op `Id`)

- `ants` — levende ant-count
- `scout` / `forager` / `def` / `att` — rol-counts
- `nestFood` — integer nest-voorraad
- `nestHP` — `NestHealth * 100` (int) — 0-10000
- `defense` / `offense` — `* 10000` (int) — 0..10000
- `protR` — `ProtectedRadius * 100` (int)
- `carryingSum` — totaal gedragen voedsel over alle levende mieren
- `sumX` / `sumY` — `X * 100` resp. `Y * 100` gesommeerd (int) — posities aggregeren
- `sumH` — `Heading * 1000` gesommeerd (int) — oriëntaties aggregeren
- `alive` — 1 of 0

Plus wereld-brede velden:

- `tick`, `simtime` (in ms), `foodcells`, `foodver`

Alle floats worden vóór hashing naar integers omgezet op vaste resoluties. Dit
beschermt tegen platform-afhankelijke laatste-bit drift in `double`/`float`-
arithmetiek. De resoluties zijn ruim genoeg om echte gedragsveranderingen te
vangen maar nauw genoeg om numerieke ruis weg te filteren.

## Wat niet in de digest zit (bewust)

- **Ant-id / per-ant details** — ant-volgorde in `Colony.Ants` kan schuiven door
  `RemoveDeadAnts` swap-compaction. Aggregaten (sum) zijn volgorde-onafhankelijk.
- **Pheromone-grid cellen** — intern detail dat we nog gaan refactoren (FASE 5).
  Ant-posities en role counts zijn de proxy die het extern gedrag vastlegt.
- **Stats circular buffer** — afgeleid van bovenstaande velden.

## Als de digest niet deterministisch is

Mogelijke oorzaken (gerangschikt naar waarschijnlijkheid):

1. Een systeem gebruikt zijn eigen `Random()`-instance i.p.v. `world.NextRandomFloat()`.
2. `Dictionary<,>` iteratie in `PheromoneGrid` voor enemy trails — .NET Dictionary
   preserveert insertie-volgorde maar sommige operaties kunnen rehashen. Verify.
3. Een float-vergelijking met NaN of denormals triggert CPU-afhankelijk gedrag.
4. `DateTime.Now`, `Environment.TickCount` of een tijdbron lekt binnen.

Wanneer de tweede harness-run een andere digest geeft: de eerste herstel-stap is
een repo-wide `Grep` op `new Random(` en `DateTime.` in `Simulation/`.
