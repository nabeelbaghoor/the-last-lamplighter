# DreamLayer art pipeline

How every image in the game was made. These scripts ran in the companion browser build of the game
(same level, Phaser), which shares its art with this Unity project through `tools/sync_art.py`.

| File | What it is |
|---|---|
| `dl.py` | A small DreamLayer Agent API client: text-to-image, image-to-image edits, background removal, sprite sheets, balance. Idempotency keys come from the request, so re-running a command replays the job instead of paying twice; it backs off on 429s and can resume a job by execution id. Reads the key from `DREAMLAYER_API_KEY` or `~/.dreamlayer_key`. |
| `PROMPTS.md` | The art plan and the shared style anchor. |
| `log.jsonl` | Every DreamLayer job the game used: prompt, operation, execution id, output. |
| `assets.json` | How raw outputs become game assets (sizes, crops, which edit pairs with which painting). |
| `build_assets.py` | Processing: trims cutouts, masks each gloom edit with its lit painting's alpha so the two always line up, makes the street strip tile seamlessly, aligns platform walking surfaces, drops a floor shadow under the floating wisp, and cuts hero clips from the sprite sheet. |

The hero's sprite-sheet ZIP is imported into Unity untouched by
[dreamlayer-unity-importer](https://github.com/nabeelbaghoor/dreamlayer-unity-importer); `art/sprites/clips.json`
picks the frames and timing for idle, run and jump.
