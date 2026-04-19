# fase-4.12-fix3 — Profiler flicker fix + graph window optimalisatie

**Scope:** SECTIE 4 van de fix-suite directive.
**Status:** Alle gates groen. Klaar voor Windows-test (F5 + meting + commit).

---

## Doel

| Metric | Baseline (STAP 1 meting) | Doel (na FIX 3) |
|--------|--------------------------|-----------------|
| `profilerWindow_us` avg | **44 us** | **< 15 us** |
| `profilerWindow_us` peak | **5793 us** | **< 200 us** (geen spikes) |
| Y-as flicker | Zichtbaar (schaal springt bij spike entry/exit) | Visueel stabiel |
| Zero-cost path (profiler hidden) | Al geimplementeerd | Bevestigd |

---

## Gewijzigde bestanden (2)

### 1. `Engine/Profiling/ProfilerGraphPanel.cs`

| Veld | Waarde |
|------|--------|
| Bytes | 8979 |
| Lines | 245 |
| md5 | `abc0e2435d69b340642a24937832b2a8` |

Wijzigingen:

- **Eigen SKPaint instances** (paint isolation): `_fillPaint`, `_strokePaint`, `_textPaint` nu privaat owned door de panel (waren shared via Window). Elke panel kan geen state leaken naar siblings binnen een Draw cycle.
- **`_yMaxEma` field + EMA-smoothing** voor Y-as plafond:
  - Eerste frame: seed direct uit `max`.
  - Spike omhoog: `_yMaxEma = max` (instant follow zodat outliers zichtbaar blijven).
  - Decay: `_yMaxEma = _yMaxEma * 0.95f + max * 0.05f` (alpha = 0.05, consistent met FrameProfiler EMAs).
- **Nieuw Draw-parameter `bool yAxisLocked`** — bevriest de EMA update wanneer `true`. Spikes boven het bevroren plafond clippen (doelbewust, matches Pin/Lock UX).
- **Draw signature gewijzigd:** paint-parameters verwijderd (panel owns ze nu), `yAxisLocked` toegevoegd.
- **Constructor toegevoegd** om de 3 eigen paints op te zetten.
- **Dispose uitgebreid** met paint disposal.

### 2. `Engine/Profiling/ProfilerGraphWindow.cs`

| Veld | Waarde |
|------|--------|
| Bytes | 18738 |
| Lines | 484 |
| md5 | `479c73f4a5f240c60505ada9a679f433` |

Wijzigingen:

- **SKPicture render cache**: hot-path `Draw()` replayed de gecachete `SKPicture` de meeste frames. Rebuild alleen als:
  - Layout verandert (_x/_y/_w/_h/zoom/lock-state), **of**
  - Series advanced **én** 50 ms throttle verstreken.
  - Op 60 FPS: rebuild ~1-in-3 frames. 44 us × 1/3 ≈ **~15 us gemiddeld**.
- **Pin/Lock button in titlebar**: nieuwe `_lockRect` tussen zoom-out button en zoom-label. Klikken togglet `_yAxisLocked`. Amber tekst "L" wanneer locked, standaard grijs anders. Achtergrond `BgPanelActive` locked vs `BgPanelHover` unlocked.
- **`_yAxisLocked` doorgegeven** aan alle 3 `panel.Draw()` calls.
- **Zoom-label** schoven naar links van het lock-button (was zoom-out).
- **`HandleMouseDown`** uitgebreid met lock-button hit-test (vóór resize grip check).
- **`DrawFrame(SKCanvas)`** nieuwe private method — bevat de hele oude Draw body. `Draw(SKCanvas)` is nu enkel cache-check + replay of rebuild+record.
- **Zero-cost early-out** (regel 250-251): `if (!_visible) return; if (!_profiler.IsEnabled) return;` — bevestigd identiek aan vóór, runt vóór iedere andere operatie.
- **Dispose** voegt `_cachedPicture?.Dispose()` toe.

---

## Verificatie gates

| Gate | Status |
|------|--------|
| Build `-warnaserror -p:EnableWindowsTargeting=true` | ✅ 0 warnings / 0 errors |
| Harness run 1 (seed=42 map=01_open_field seconds=5) | ✅ `78ad61829002a3194554ac4681feb98e` |
| Harness run 2 | ✅ `78ad61829002a3194554ac4681feb98e` |
| Harness run 3 | ✅ `78ad61829002a3194554ac4681feb98e` |
| Grep: `_yMaxEma` in panel | ✅ 8 occurrences |
| Grep: EMA `0.95f/0.05f` in panel | ✅ regel 155 |
| Grep: `yAxisLocked` param in panel Draw | ✅ regel 94, 147 |
| Grep: 3 own SKPaint fields in panel | ✅ regels 53-55 |
| Grep: `SKPictureRecorder` / `_cachedPicture` in window | ✅ regels 103/275/282 etc. |
| Grep: `Stopwatch.Frequency / 20` throttle in window | ✅ regel 112 |
| Grep: `_yAxisLocked` / `_lockRect` in window | ✅ 18 occurrences |
| Grep: zero-cost early-out `if (!_visible) return` | ✅ regel 250-251 |
| Grep: geen stale references naar oude panel.Draw signature | ✅ none |
| Grep: `SKPictureRecorder` **niet** in panel (correct scope) | ✅ none |

Byte-identity harness verificatie bewijst dat simulation gedrag **ongewijzigd** is ondanks de UI-ingrijpende changes — profiler layer is volledig geïsoleerd van sim hot-path.

---

## Niet gewijzigd (intentioneel)

- `FrameProfiler`, `ProfilerSeries`, `ProfilerGraphConfig` — ongemoeid.
- `ProfilerUI` (de 6-row HUD panel) — buiten scope, FIX 3 richt zich op de graph window.
- `Engine.cs` — ProfilerWindow Begin/End blijft om de `_profilerGraphWindow.Draw(canvas)` call. De Begin/End meet nu effectief de cache-replay kost de meeste frames.

---

## Voorgestelde commit

```
fase-4.12-fix3: profiler flicker fix + graph window optimalisatie

- ProfilerGraphPanel: EMA Y-axis smoothing (alpha=0.05), own paints,
  yAxisLocked parameter
- ProfilerGraphWindow: SKPicture render cache, 50ms throttle,
  series-count skip, Pin/Lock button

Target: profilerWindow_us 44us avg -> <15us avg (~3x rebuild
reduction via picture replay), flicker eliminated via stable EMA
ceiling. Zero-cost path when hidden preserved.

Harness 3x byte-identical (78ad61829002a3194554ac4681feb98e).
Build 0W/0E with -warnaserror.
```

---

## Volgende stap

STOP voor Windows-test:
1. F5 build in VS → bevestig UI-compileert.
2. Open sim, enable profiler (top-bar Profile of F2), wacht op samples.
3. Observer:
   - Y-as schaal springt niet meer bij spike entry/exit.
   - Klik lock-button (L) in titlebar → schaal bevriest.
   - `profilerWindow_us` gemiddeld << vóór.
4. AFTER-data plakken in chat (avg/peak/stdev profilerWindow_us over 5 min run).
5. Commit `fase-4.12-fix3`.

Daarna → FIX 2 (overlay complete opt).
