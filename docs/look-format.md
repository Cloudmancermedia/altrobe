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
| `items` | Slot name to item ID. Slots: `head neck shoulder shirt chest waist legs feet wrist hands back mainhand offhand ranged tabard`. A character holds hand weapons or a ranged one: equipping a bow, gun, crossbow, thrown weapon or wand in `ranged` takes off `mainhand` and `offhand`, and the reverse. |
| `hide` | Slots whose item is equipped but not drawn. |
| `cam.view` | `front`, `side`, `back` or `head`. |
| `anim` | Optional animation name every character plays, such as `"Run"`; absent means `"Stand"`. A character whose body model lacks it stands, with a notice. |
| `compare` | Up to five more characters shown side by side, in screen order. Each wears the main outfit unless it has its own `items`; see below. |

### Side-by-side characters

Each `compare` entry has `race`, `sex` and `models`, and may also have:

| Field | Meaning |
| --- | --- |
| `label` | Shown above the character, up to 40 characters, such as `"Level 30"`. |
| `items` | The character's own outfit, slot name to item ID. Present, even empty, means "wears this"; absent means "wears the main outfit". |
| `hide` | Hidden slots of the character's own outfit. |
| `custom` | Customization choices for this character's body model, only those that differ from its defaults. |

```json
"compare": [{ "race": 2, "sex": 0, "models": "hd", "label": "Level 30", "items": { "chest": 7418, "legs": 7919 } }]
```

## Share links

A share link is the app URL plus `#look=` and the base64url encoding of the canonical JSON. The fragment never reaches a server. Canonical JSON sorts keys at every level and leaves out empty or default fields: `custom` choices equal to the defaults, empty `items`, `hide` and `compare`, and a `front` camera. The same look always encodes to the same link.

## Sheathed weapons

`sheathed: true` puts the weapons away, each by its sheath type: a one-hander or a two-hander on the
back, a dagger or other hip weapon on the hip, a shield on the back. A pair crosses. A ranged weapon
isn't shown when sheathed, as in Classic. Left out of the link when false.

## Identity addition

A look may have an `identity` object: who the character is, shown over its head and in its tooltip.
Every field is optional, and the look stays version 1. A viewer from before the addition ignores it
and still draws the outfit.

| Field | Holds |
| --- | --- |
| `firstName`, `lastName` | up to 24 characters each, trimmed. Forever characters have both. |
| `titleId` | a `CharTitles` ID. The title's male or female wording follows the look's `sex`. |
| `guild` | up to 24 characters, trimmed |
| `level` | 1 to 60 |
| `classId` | a `ChrClasses` ID |
| `pvp` | `true` when flagged for PvP; left out otherwise |

Faction is not stored: it follows from the race. A generated name is stored as the name itself, not
the seed that picked it, so a link shows the same name whatever later builds do to the name lists.
Blank text, `pvp: false` and an empty `identity` are left out of the canonical JSON. A field that fails
these rules is dropped with a notice, like any other bad entry.

## Reading a look

Reading is forgiving. Unknown fields are ignored, bad entries are dropped, and each drop is reported as a notice in the app. Only a look that cannot be drawn at all fails: not an object, a `v` below 1 or not an integer, another `game`, or no valid race and sex. A look with no `v` is read as v1, and a look with a higher `v` is read as v1 with its unknown fields ignored.

Against the loaded game data, the app also drops items it has no data for, items in a slot their inventory type does not fit (a one-hander may go in `offhand`), and customization options or choices the character does not have. Each drop comes with a notice.

## Compatibility

Every field added after the first version (`compare` and its entries' `label`, `items`, `hide` and
`custom`, `anim`, `sheathed`, `identity`) is optional, so older links read the same and the version
stays 1. A viewer that predates a field ignores it, and one that predates the five-character limit
drops `compare` entries past the third.
