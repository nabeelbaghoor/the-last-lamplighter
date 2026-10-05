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

6. **Exported timing is the source video's sampling, not game speed.** The 12-frame run came
   back at 500 ms per frame (a 6 s cycle; a game run cycle is about 0.7 s). The importer now
   takes a target clip length and rescales DreamLayer's per-frame durations to it, keeping their
   relative timing.
7. **The first frames are an intro, not part of the loop.** Frames 1 to 3 of the run are the
   reference pose easing into the action, although the atlas says `sampling: "cycle"`. Looping
   all 12 frames hitches once per cycle. The importer takes a first frame and frame count.
8. **Sampling aliases the motion.** Sampling every 500 ms is slower than half a running stride,
   so frames 4 to 12 alternate between just two poses: the mean alpha difference between
   frames of the same pose is 0.9 to 2.5%, between the two poses 5.9 to 7.9%. Twelve frames are
   billed, two poses are delivered. *Product idea: sample across one detected motion period (or
   expose the sampling rate / clip length) so N frames means N distinct phases of one cycle.*
9. Same small metadata drift as the sample: `padding: 2` while rects are packed edge to edge,
   and `duration_ms: 6042` against `frame_durations_ms` summing to 6040.

## Image editing (the lit / gloom pairs)

The core mechanic needs each gloom painting to sit exactly on its lit painting, so this was the
most demanding use of image-to-image in the project.

**Works well**
- One instruction held up across very different subjects: *"Keep this exact scene and
  composition, every shape in the same place, same framing. Drain all warmth and colour..."*.
  The street kept every cobblestone in place; the sky kept its moon and clouds; the far town
  kept its skyline, spire and canal.
- Edits read as the same painting under different light, not a new picture, which is exactly
  what a reveal mechanic needs.

**Breaks down**
- **Edits redraw, they do not recolour.** Small drift is normal (a roof edge moves, a cast
  shadow appears that was not there), and an input on a busy backdrop (a house painted on a
  paper-texture rectangle) came back as a different, larger drawing of the house. Fix in the
  pipeline: the gloom edit takes the lit cutout's alpha, so silhouettes always match and only
  interior detail can drift, which the soft light falloff hides. Feeding the cutout on a flat
  background into the edit, instead of the original, keeps the model on the same drawing.
- *Product idea: a "preserve structure" or "recolour only" edit mode (or return the edit's
  alignment to the input) for game pipelines that layer an edit over its source.*

## Reliability notes from a full asset run (about 40 jobs)

- With about ten jobs polling every 3 s, the API answered `429 RATE_LIMITED` on polls, uploads
  and new executions. Backing off and resuming by execution id worked; SSE would avoid polling.
- A handful of executions created during that burst stayed `queued` for over an hour while
  later jobs finished in about a minute. Image jobs could be cancelled and resubmitted; queued
  sprite-sheet jobs answer `409 CONFLICT` to cancel.
- One prompt (a street lamp at 2:3) failed twice with a non-retryable `generation_failed`; the
  same subject at 3:4 worked.
- Every sprite job with a custom `animation_prompt` (a jump, and two attempts at a slow-motion
  single-stride run to work around point 8) failed with `generation_failed` or never left the
  queue; the preset `run` action worked. Later preset `idle` jobs also stayed queued for hours.
  The shipped character therefore comes from one sprite sheet: frame 1 (the reference pose) as
  the idle, frames 2 to 5 as the run, frame 2 as the jump, all cut by the importer.

## Credits used

47.9 of the 100 jam credits: 17 text-to-image, 13 background removals, 8 image edits and one
12-frame sprite sheet (9.9). Failed and cancelled jobs were not charged.

## Onboarding

- A new API account starts at zero credits and the jam's free credits need a manual review, so a
  jam entrant cannot make a single image on day one. A small automatic starter grant (even 5
  credits) would let people validate their pipeline while the review happens.
