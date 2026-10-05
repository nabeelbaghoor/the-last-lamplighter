# DreamLayer in a real game pipeline: production notes

Notes from building *The Last Lamplighter* (Phaser/TypeScript) and a Unity importer for DreamLayer
sprite ZIPs, written the way I would file them internally: what worked, what broke, and what I'd change.

## Sprite-sheet bundles

**Works well**
- The ZIP is genuinely engine-friendly: `sheet.png` + `atlas.json` + loose `frames/` + `preview.gif`.
  `atlas.json` is TexturePacker-hash compatible, so Phaser's `load.atlas` reads it unchanged.
- `frame_durations_ms` per frame is a real upgrade over a single fps value; held poses survive import.
- `alignment: "ground_anchor"` keeps the feet on one line across every frame of a job.

**Breaks down in production**
1. **One character, several jobs, several sizes.** Each sprite job reports its own `scale`
   (e.g. `0.3134`). Idle, run and attack are separate jobs, so the same character is drawn at a
   different size in each sheet and visibly pops when the animation changes. The Unity importer
   fixes this by measuring the visible body in each sheet and choosing a per-sheet pixels-per-unit
   so every clip is the same height in world units. *Product idea: a `character_id` or
   `match_scale_of: <execution_id>` option so related jobs share scale server-side.*
2. **The pivot is not in the metadata.** `ground_anchor` says the feet share a line, but not where
   that line is. Engines default to a centred pivot, so imported characters float. The importer
   measures the lowest opaque row; DreamLayer could simply publish `anchor: {x, y}` in the atlas.
3. **`padding: 1` does not match the rects.** The sample atlas declares `padding: 1` but frame
   rects are packed edge to edge (x = 0, 128, 256). Importers that trust `padding` will slice one
   pixel off. Either pad the sheet or drop the field.
4. **Two timing fields disagree.** The sample atlas says `duration_ms: 2833`, but its
   `frame_durations_ms` sum to 2830. Engines that build a clip from the total and engines that build
   it from the per-frame list will drift apart by a few milliseconds per loop. The per-frame list
   should be the single source of truth (or `duration_ms` should be its sum).
5. **The official Unity example is a demo, not an importer.** It loads loose PNGs from a
   `Resources` folder through a custom frame player, ignores `sheet.png`, centres the pivot, and is
   marked unvalidated. Unity users expect a sliced sprite sheet and an `AnimationClip` they can drop
   into an Animator. The importer in `dreamlayer-unity-importer` does that, and its test passes 15/15
   checks in Unity 2021.3.7f1, including equal world height for two sheets imported at 63 and 48
   px/unit. One Unity gotcha it handles: Unity adds one frame to a sprite clip's length, so naive
   importers make every DreamLayer loop run 1/60 s long.

## Image editing (the lit / gloom pairs)

*(filled in as the art is generated: composition fidelity across edits, what drifted, prompt
patterns that held the layout)*

## Onboarding

- A new API account starts at zero credits and the jam's free credits need a manual review, so a
  jam entrant cannot make a single image on day one. A small automatic starter grant (even 5
  credits) would let people validate their pipeline while the review happens.
