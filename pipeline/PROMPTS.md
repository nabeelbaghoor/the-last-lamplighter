# The Last Lamplighter: DreamLayer art plan

Budget: 100 free API credits. Images cost 1 credit; sprite sheets cost 5.8 (7 frames) or 9.9 (12 frames).

## Style anchor (appended to every prompt)

> painterly storybook illustration, gouache and soft ink linework, hand-painted texture,
> cozy European canal town of crooked timber houses and lanterns, side-on 2D game art,
> rich but restrained palette

Warm "lit" world: amber lantern light, glowing windows, deep indigo night sky, rose and ochre walls.
Cold "gloom" world: the same scene edited with DreamLayer, drained of colour, blue-grey fog, lamps dark.

## The core trick: every scene painted twice

Every backdrop is generated once in its **lit** state, then passed back to DreamLayer as the
reference with an edit: *"Keep this exact scene and composition. Drain it of all warmth..."*.
In game, the lantern's light reveals the lit painting through the gloom painting.
That pair (reference + edit) is the before/after shown on the itch page.

## Assets

| # | Asset | Op | Credits |
|---|-------|----|---------|
| 1 | Hero reference: Wren the lamplighter, full body side view, plain background | gen x2-3 | 3 |
| 2 | Hero idle loop, 7 frames | sprite (idle preset) | 5.8 |
| 3 | Hero run cycle, 12 frames | sprite (run preset) | 9.9 |
| 4 | Hero jump pose: "keep this exact character, mid-jump" | edit + cutout | 2 |
| 5 | Sky + distant town panorama (lit) | gen 16:9 | 1-2 |
| 6 | Sky + distant town (gloom) | edit of 5 | 1 |
| 7 | Mid-ground houses x3 (lit), cut out | gen + cutout | 6 |
| 8 | Mid-ground houses x3 (gloom) | edit of each lit house + cutout | 6 |
| 9 | Cobblestone street strip | gen + cutout | 2 |
| 10 | Platforms: stone ledge, wooden walkway, crate, rope bridge plank | gen + cutout each | 8 |
| 11 | Street lamp unlit, then lit (edit of unlit) | gen + edit + 2 cutouts | 4 |
| 12 | Ember pickup | gen + cutout | 2 |
| 13 | Shadow wisp | gen + cutout | 2 |
| 14 | Title key art (lamplighter on a bridge, half the town lit) | gen 16:9 | 1-2 |

Planned spend: about 55 credits, leaving ~45 for retries and extras (e.g. a wisp loop sprite).
Prop gloom versions that are not worth a credit are derived locally (desaturate + cool tint).
