01 MAIN MENU — sprite list (all 1024² unless noted; 9-slice border = radius)

level_chip_pill.png        1024×448, slice 224   "Level 38" chip (outlined)
header_icon_button.png     slice 333             the two 40px header buttons
  icon_player / icon_rules / icon_settings — 512² ink glyphs, draw at 40-44%
  of the button, alpha ~0.85

game_name_wordmark.png     630×150               THE GAME NAME. Same artwork
  as the reference: ink letterforms, red + cyan accent dots, transparent.
  Drawn at 286×68 design px (572×136 at 1080 wide).

logo_seg_h / _v / _corner.png   white — TINT them: red #FF3B30 for the outer
  path, cyan #0FB8A4 for the inner path (see the reference).
logo_dot_red / logo_dot_cyan.png   the 4 endpoint dots (17px design)
logo_node_empty.png        the 5 empty nodes (11px design)

tab_classic_selected.png   slice 276   Classic tab (mint ring)
tab_advanced.png           slice 276   Advanced tab
tab_dot_on / tab_dot_off.png   the 7px dot in each tab

PLAY BUTTON — compose in this order (488×140 design px, radius 20):
  1. play_button_glow.png     behind, ~1.4× the button, mint glow
  2. play_button_base.png     9-slice, slice 284
  3. play_button_dot_texture.png  tiled, clipped to the button, faded in
     from ~50% of the width to the right edge, alpha 0.6
  4. text "PLAY" (Space Grotesk 700, 30px, #08201D) + "CLASSIC" pill
  5. play_progress_track / _fill (white) / _knob (white) — bar 5px high

daily_card.png             slice 224   daily-challenge row
daily_card_icon_well.png   slice 224   46px icon well inside it (icon_missions)
daily_streak_pill.png      1024×448, slice 224   "4-DAY" streak pill
icon_chevron_right.png     row affordance, alpha ~0.55