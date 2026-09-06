03 GAME — sprite list

header_icon_button.png     slice 333   home + menu
hud_card.png               slice 224   the progress strip card
hud_progress_track / _fill 1024×64, slice 32   5px bar
icon_prev / icon_next.png  chevrons in the two 44px side buttons

BOARD
board_panel.png            slice 307   the recessed board (radius 24)
board_cell.png             slice 178   one empty cell (46px, radius 8, gap 6)
board_node_empty.png       empty node
dot_red/blue/yellow/green/orange/cyan/indigo.png   pair endpoints

PATHS — all white, TINT per pair colour. One sprite per cell:
path_seg_h / path_seg_v    straight runs (stroke = 8/46 of the cell)
path_seg_corner            90° turn, rotate 90/180/270
path_cap                   round end + stub, rotate to the entry direction
path_joint                 round joint / single-cell blob

HINT BUTTON (64px circle, bottom centre)
  hint_button_glow.png behind → hint_button.png → icon_hint_on_mint.png
  (white glyph; render it dark on the mint, alpha .78) → count text
  "×3" in IBM Plex Mono 700 11px