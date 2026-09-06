PATHZA — CONNECT THE PATH · light theme · game-ready assets

AUDIT: every file below was re-rendered and visually checked. Every square
sprite is 1024×1024. Only genuinely linear sprites are not square (bars,
pills, toggle tracks, divider) — their sizes are listed. Nothing is
pixelated (all vector-drawn at 1024), nothing has a baked drop shadow, and
every sprite is a transparent PNG with straight (unpremultiplied) alpha.

START HERE: screens_sprites/
  One folder per screen (01_menu … 07_levels). Each folder holds every
  sprite that screen needs, named by its role on that screen, plus the
  1080×1920 reference image and a README.txt mapping element → sprite →
  9-slice border → design size. sprites/ below is the same art organised by
  type (one master copy per sprite) if you prefer a shared atlas.

ICON COLOUR: icons/ is INK (#0F1E2E) because this is the light theme — that
  is what the references show, drawn at alpha 0.45–0.85 depending on the row.
  icons_white/ has the same glyphs in white, for use on mint buttons (render
  them dark there, as the references do) or if you build a dark theme.

GAME NAME: branding/wordmark_ink_transparent.png is the exact artwork from
  the menu reference — ink letterforms with the red and cyan accent dots,
  transparent background, drawn at 286×68 design px.

HOW TO USE
· Rounded sprites are 9-SLICE. Set the slice border to the RADIUS listed and
  stretch only the middle — the sprite then works at any size with no
  distortion and no softening.
· Circles, dots and glyphs are NOT 9-slice: scale them uniformly.
· Shadows and glows are separate sprites (textures/shadow_card.png,
  textures/glow_mint_radial.png) so you can place them behind at any size,
  or skip them and use your engine's own shadow.
· White sprites (path pieces, bar_fill_white) are meant to be TINTED at
  runtime — multiply by the pair colour.
· Text is never baked in. Draw labels with the fonts listed at the bottom.

──────────────────────────────────────────────────────────────
screens/                7 references, exactly 1080×1920
  01_menu · 02_level_select · 03_game · 04_level_complete
  05_settings · 06_daily_challenge · 07_levels

backgrounds/
  bg_light_1080x1920.png   Captured from the live screens, so it matches
                           them exactly. Full-screen, opaque.

branding/
  wordmark_ink_transparent.png  630×150  transparent, accent dots intact
  glyph_transparent_2048.png    2048²    logo glyph alone, no plate
  app_icon_light_1024.png       1024²    square store icon (opaque)

icons/                  20 glyphs, 512×512, transparent, ink #0F1E2E

──────────────────────────────────────────────────────────────
SPRITES — all 1024×1024 unless noted

panels/   (9-slice)
  panel_card.png            slice 224   white card, hairline ring
  panel_card_selected.png   slice 224   same + mint ring (selected)
  panel_board_recess.png    slice 307   board well
  panel_sheet_popup.png     slice 299   level-complete sheet
  panel_faint.png           slice 224   faint fill + ring (empty rows)
  pill_outline.png     1024×448, slice 224   outlined pill / level chip
  pill_mint_tint.png   1024×448, slice 224   mint-tint pill
  pill_on_mint.png     1024×448, slice 224   dark pill for use ON mint

buttons/  (9-slice unless noted)
  btn_play_base.png         slice 284   MAIN MENU PLAY card, mint gradient.
        Compose: glow_mint_radial behind → btn_play_base → dot texture
        (tex_dots_white_tileable, clipped to the button, faded in from ~50%
        of the width) → your PLAY / CLASSIC text and progress bar on top.
  btn_primary.png           slice 284   general mint primary (r20 look)
  btn_secondary.png         slice 240   white card button
  btn_icon_square.png       slice 333   40px header icon button
  tab_mode_on.png           slice 276   Classic tab (selected, mint ring)
  tab_mode_off.png          slice 276   Advanced tab
  tab_stage_on.png          slice 201   30-level stage tab, selected
  tab_stage_off.png         slice 201   stage tab, idle
  btn_pack_play.png         slice 334   pack-card action, mint
  btn_pack_done.png         slice 334   pack finished (green tint)
  btn_pack_locked.png       slice 334   pack locked (faint)
  btn_hint_circle.png       CIRCLE      gameplay hint button (mint + top
                                        highlight). Put the hint icon and
                                        the count on top.

tiles/    (9-slice)
  tile_level_done.png       slice 256   completed level
  tile_level_current.png    slice 256   current level (outlined mint)
  tile_level_locked.png     slice 256   locked level
  tile_daily_done.png       slice 228   daily button, solved
  tile_daily_current.png    slice 228   daily button, current
  tile_daily_locked.png     slice 228   daily button, locked
  cell_board_empty.png      slice 178   empty board cell

dots/     (scale uniformly)
  dot_endpoint_red / blue / yellow / green / orange / cyan / indigo
  dot_node_empty.png        empty node: white well + ink ring
  dot_mode_on.png           mint mode dot (menu "Classic" marker)
  dot_mode_off.png          idle mode dot
  star_pip_on.png / star_pip_off.png    level-tile star pips

path/     WHITE — tint per pair colour at runtime. One sprite = one cell.
  seg_straight_h.png        horizontal run (stroke = 178/1024 of the cell)
  seg_straight_v.png        vertical run
  seg_corner.png            90° turn — rotate 90/180/270 for the others
  seg_cap.png               round end + stub running down — rotate as needed
  seg_joint_round.png       round joint / single-cell blob

progress/
  bar_track.png       1024×64, slice 32   bar background
  bar_fill_mint.png   1024×64, slice 32   bar fill
  bar_fill_white.png  1024×64, slice 32   bar fill for use ON mint
  knob_ink.png        CIRCLE              slider knob, ink
  knob_white.png      CIRCLE              slider knob, white
  toggle_track_on.png   1024×601, slice 300   settings toggle, on
  toggle_track_off.png  1024×601, slice 300   settings toggle, off
  (toggle knob = knob_white.png)

week/     Daily-challenge streak chain
  day_solved.png / day_today.png / day_future.png   CIRCLES
  chain_track.png   1024×64, slice 32   connector behind the circles
  chain_fill.png    1024×64, slice 32   filled up to today

textures/
  glow_mint_radial.png          soft mint glow — place behind mint buttons
  shadow_card.png     slice 260 soft ink shadow — place behind cards
  tex_dots_white_tileable.png   seamless white dot texture (PLAY button)

ui/
  ui_lock.png         padlock (locked levels / packs)
  ui_check_mint.png   mint checkmark (completed)
  divider.png   1024×8   1px hairline row divider

──────────────────────────────────────────────────────────────
PALETTE (light)
  page #EEF3F9 · card #FFFFFF · recess #EEF3F9
  text #0F1E2E · mid #33465C · secondary #5F7385 · muted #67788D
  hairline rgba(16,24,40,.07) · faint fill rgba(16,24,40,.05)
  accent #0F9E88 · primary gradient #57ECD0 → #2FD7BB → #25C3AB (135°)
  label on mint #08201D
  board red #FF3B30 · blue #007AFF · yellow #FFCC00 · green #34C759
        orange #FF9500 · cyan #00C7BE · indigo #5856D6

TYPE
  Space Grotesk 600/700 — titles, buttons, level numbers
  IBM Plex Mono 400/500/600 — counters, labels, percentages

REFERENCE SIZES (design px, at 1080×1920 multiply by 2)
  screen padding 22 · card radius 14 · card height 46–72
  header icon button 40 · hint circle 64 · level tile 56 · daily tile 72
  board cell 46 with 6 gap · progress bar 6 high, knob 16
  toggle 46×27 with 21 knob · week day circle 39, chain 3 high
