# Look format (v1)

A look is a small JSON object that names a character and what it wears, by game IDs only. The web app saves it to a file and puts it in share links. The code is `web/src/look/look.ts`.

```json
{
  "v": 1,
  "game": "forever",
  "build": "1.60.1.70009",
  "models": "hd",
  "race": 2,
  "sex": 0,
  "custom": { "20": 390 },
  "items": { "head": 12640, "mainhand": 19019 },
  "hide": ["head"],
  "cam": { "view": "side" },
  "compare": [{ "race": 5, "sex": 1, "models": "sd" }]
}
```

| Field | Meaning |
| --- | --- |
| `v` | Schema version. Always 1. |
| `game` | Always `"forever"`. |
| `build` | Game build the look was made with. A different build shows a notice, not an error. |
| `models` | `"hd"` or `"sd"`, as the game's graphics setting switches the character bodies. |
| `race`, `sex` | ChrRaces ID, and 0 male or 1 female. |
| `custom` | Customization option ID to choice ID, only for choices that differ from the defaults. |
| `items` | Slot name to item ID. Slots: `head neck shoulder shirt chest waist legs feet wrist hands back mainhand offhand tabard`. |
| `hide` | Slots whose item is equipped but not drawn. |
| `cam.view` | `front`, `side`, `back` or `head`. |
| `compare` | Up to three more characters shown side by side with the same outfit, in screen order. |

## Share links

A share link is the app URL plus `#look=` and the base64url encoding of the canonical JSON. The fragment never reaches a server. Canonical JSON sorts keys at every level and leaves out empty or default fields: `custom` choices equal to the defaults, empty `items`, `hide` and `compare`, and a `front` camera. The same look always encodes to the same link.

## Reading a look

Reading is forgiving. Unknown fields are ignored, bad entries are dropped, and each drop is reported as a notice in the app. Only a look that cannot be drawn at all fails: not an object, a `v` below 1 or not an integer, another `game`, or no valid race and sex. A look with no `v` is read as v1, and a look with a higher `v` is read as v1 with its unknown fields ignored.

Against the loaded game data, the app also drops items it has no data for, items in a slot their inventory type does not fit (a one-hander may go in `offhand`), and customization options the character does not have. Non-default choices are kept but not drawn yet, and the app says so.

## Changes since the spike

`compare` is new. It is optional, so every look the spike wrote reads the same and encodes to the same link, and the version stays at 1. A viewer that does not know `compare` ignores it and shows only the main character. Characters in `compare` use their default customizations, because customization option IDs belong to one body model.
