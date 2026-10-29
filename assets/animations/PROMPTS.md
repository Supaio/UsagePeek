# Desktop pet animation studies

These are early frame-animation studies retained as design references. The
current executable uses the original character PNGs as textures for continuous
local skeletal/mesh deformation in `PetRigAnimation.cs`; it does not embed or
play the atlases below. Render scheduling is handled by `AnimationFrameClock.cs`.

Created with the built-in imagegen tool. Existing PNG character artwork was
supplied as an identity/style reference, with true transparent output requested.
The original static artwork remains available as a fallback and continues to
provide the usage bubble's alpha-weighted average color.

## Shared prompt specification

Use case: stylized-concept. Asset: production transparent RGBA animation sprite
atlas for a Windows desktop pet. Create exactly four columns and five rows,
20 full-character frames in row-major order. Prefer a portrait 2048 × 2560 PNG.
Center each character at the same scale and baseline, with transparent padding
inside each invisible cell. No background, grid lines, labels, text, floor or
shadow plate. Preserve the reference character's identity, costume, palette,
proportions and rendering style across all frames.

Frame sequence:

- Row 1 / 0–3: relaxed open eyes, gentle inhalation and half blink, closed blink,
  then exhale/reopen. Subtle breathing and hair/accessory movement.
- Rows 2–3 / 4–11: eight successive articulated poses of one cyclic locomotion
  movement toward the right: contact, compression, passing, lift, opposite
  contact, compression, passing and settle. Change actual limb/body poses;
  do not repeat a cutout with only translation or rotation.
- Row 4 / 12–15: cheerful greeting, brighten, gesture, happy peak, then settle.
- Row 5 / 16–19: anticipate petting, pleased half-closed eyes, lowered head with
  closed pleased eyes, then recovery. Do not draw a human hand: the application
  supplies its existing petpet hand.

## Character prompt additions

### Whale maid — `whale-v1.png`

Reference: `../pet-whale-maid-curled.png`. Blue-haired whale maid, white frill
headband, navy/white dress, blue braids, golden monocle/chain, whale ears and
broad blue tail. Preserve the round, compact lying pose with chin near hands.
Move with a gentle hand/forearm-supported shuffle while lying down, chest
effort and braid/tail counter-sway. Do not stand up or invent human legs.
Match the detailed anime rendering of the reference.

### Phoebe Chibi — `phoebe-v1.png`

Reference: `../pet-phoebe-chibi.png`. Simple bold-outline seated chibi, pale
blond long hair, purple eyes, red Santa hat with white trim/pompom, white/brown
outfit, blue chest ribbon and tiny boots. Preserve the simple cartoon style.
Move with seated scoots or bunny hops, alternating feet/arm pushes, squish and
lift, with follow-through in the hat and hair. Do not turn into detailed anime.

### ChatGPT girl — `chatgpt-v1.png`

Reference: `../pet-chatgpt-girl.png`. White-haired lavender dragon girl with
pale curved horns, lavender eyes, ornamental white/lavender dress, rose-knot
ornaments, small bat wings and a scaled coiled tail. Preserve the compact
curled/perched silhouette. Her coils push and lift her body, the tail sways
and wings gently flap, with sleeves and hair following through. No standing
human pose or invented visible legs. Match the delicate anime style.

### Claude girl — `claude-v1.png`

Reference: `../pet-claude-girl.png`. Orange-haired chibi, amber eyes, sunflower
hair ornament and black bows, embroidered ivory/black/orange long dress,
cream boots and a brown decorated book held in her hands. Eight actual
rightward walking steps: alternate planted/raised boots and knees under the
shifting hem, with hair/ribbon follow-through. Keep the same book. Idle glance
at the book and blink; happy gentle wave while still holding the book. Match
the warm detailed anime style and preserve the full body in every cell.

### Milk frog — `frog-v1.png`

Reference: `../pet-milk-frog.png`. Plump yellow 3D frog, cream round belly,
green bulging eyes, tiny straight mouth, olive hands/toes, short thick legs,
hands near belly. Match the original 3D material and soft consistent lighting.
Eight successive rightward waddling poses with alternate planted/raised feet,
flexing knees, delayed belly follow-through and arm counter-swing. Real eyelid
blinks and belly breathing at rest, small arm lifts when happy, contented
closed eyes during petting. No clothing or anime rendering.

## Packing and playback

`tools/pack-pet-animation.ps1` extracts the generated cells, isolates each
character from disconnected neighbouring fragments, retains antialiased alpha,
and registers all 20 study poses at one shared scale and baseline. It does not stretch
individual poses to different sizes. Whale walking frames 8 and 9 are mirrored
to keep their facing direction consistent. The final atlases are 1280 × 1600,
with twenty 320 × 320 cells. Original generated files are kept separately by
the imagegen tool.

The initial study played walking poses at 95–110 ms intervals. That approach
was superseded by continuous joint motion to preserve character identity and
produce intermediate positions at arbitrary times.

The existing artwork credits in `THIRD_PARTY_NOTICES.md` remain applicable.

## Preview

Run `tests/run-continuous-animation.ps1` to validate the active continuous rig
and export a 60 FPS MP4 plus a PNG contact sheet. Run `tests/run-pet-mode.ps1`
for interaction, cached usage bubbles, scale and screen-boundary checks.
