# Item Traits — mod description

**Mod ID:** `itemtraits` · **Name:** Item Traits · **Game:** Vintage Story 1.22.x

---

## What it is

**Item Traits** is a code mod built around a single, cohesive “Dragoon” fantasy: a matched outfit of blackguard-styled gear, a dedicated blade, and **three interlocking systems** the whole design revolves around:

1. **Item → stat** — Each piece of the clothing set applies a *specific, visible bonus* to your seraph (movement, healing, max health) when it is **equipped and functional** (see quirks below). Nothing is a vague “trait”; it is wired through the game’s stats and health so you *feel* the item on the bar and in play.

2. **Item → inventory** — The **Dragoon Belt** is not just +stats: it opens **three extra, persistent personal slots** that belong to the *belt* (not your backpack’s row count in the usual sense). You manage them through a custom panel alongside your character, with its own open/close behavior and a dedicated hotkey.

3. **Set bonus → power fantasy** — When *everything* the mod cares about is online at once—**all four clothing pieces, each doing its job**, plus the **Dragoon sword in hand**—you earn **Survival flight** (creative-style free movement, not noclip): the set’s *intent* is “earn the sky by committing to the loadout *and* the cursed blade.”

The mod also gives you a **status panel** (Dragoon set status) so you can see, at a glance, which parts are *active* vs *inactive* and whether the full set and flight are live—bridging “casual” readability and the mod’s technical truth.

---

## Purpose

**Vision:** One themed gear chase that isn’t just stat soup: *each* slot matters, the belt changes how you manage *items*, the sword is the price and proof of the fantasy, and **flight** is the emotional payoff for assembling and maintaining the set under Vintage Story’s survival rules.

**Intent:**

- Reward **wearing the full identity** of the set—not only numbers, but an extra “layer” of inventory tied to a single piece, and a **set bonus** that redefines moment-to-moment play.
- Make **item ↔ stat** mapping **obvious** (shoes = speed, shirt = life pool, helm = sustain, belt = those three slots *and* a bridge piece for the set).
- Tether **flight** to **all four** armor slots **and** the sword **held**, so the fantasy is: *you* carry the draconic burden in both hands and cloth.
- **Casuals:** “Get the Dragoon look, read what each thing does, pop the panel with E or the hotkey, and aim for the big flying prize.”
- **Modders:** Clear hooks: watched attributes, stat layers, a custom `InventoryBase` class, client `GuiDialog`, and server tick logic you can read as a *pattern* (item → entity tree → client UI).

---

## For casual players

### The Dragoon set (clothing + what they do)

| Piece | What it does (when active) |
|--------|----------------------------|
| **Dragoon shoes** | **+100%** walk and sprint speed (on top of vanilla). Needs **durability above 0%** to count as “on.” |
| **Dragoon shirt** | **+4 max HP** (flat), applied as a mod-friendly max-health bonus. Needs **durability above 0%** to count. |
| **Dragoon helm** | **+25%** health regeneration (healing effectiveness layer). Needs **durability above 0%** to count. |
| **Dragoon belt** | Unlocks the **Dragoon Belt inventory: 3 dedicated slots** stored on your character, shown in the Dragoon panel. The belt is considered **on** when it is in the waist slot. **You cannot unequip the belt while any of those three slots still hold an item**—empty them first, or the mod will try to keep the belt on and warn you. This is a **feature**, not a bug: the inventory is *part of* the item. |

### The Dragoon sword

- Tuned as a **blackguard-style blade** with **9 attack power** in the mod’s configuration; the recipe is built around a **powdered sulfur** ingredient to flavor the craft.
- **Quirk (cost):** While you **hold** it, it **continuously self-bleeds** you (internal piercing damage on a server tick) until you put it away. That is the narrative and mechanical “cost” of the weapon you must hold to complete the set fantasy.

### Set bonus — **Survival flight**

- **All four** Dragoon clothing pieces must be **“active”** in the mod’s sense: shoes, shirt, and helm need to be **worn and not at 0% durability**; the **belt** must be worn (the belt is **not** gated the same way as the others for that “active” check in the set logic for slots).
- The **Dragoon sword** must be **in your hand** and **not broken** (has durability if the item has durability) for the “sword” part of the set check.
- When that full condition is true, the mod grants **FreeMove** in **Survival** (fly like creative flight, not noclip). If **any** of those conditions slip—durability, unequip, stowing the sword—flight drops until you fix it.

### UI & controls (belt panel and status)

- Open the **main inventory / character screen (E)** with the belt equipped: the mod can show the **Dragoon** panel (status text + the three slot grid). You can also **toggle** the belt panel with **Ctrl+Shift+B** (default; rebindable in **Settings → Controls**). If the panel annoys you while E is up, you can close it; opening **E** again or using the hotkey is how you bring it back—*see in-game behavior for the exact “dismissed while inventory open” feel*.
- The panel summarizes **shoes / belt / helm / shirt** active state and the **set flight** line, plus a hint line for the belt slots.

**Short summary line:** *Collect the set → each item changes stats or space → hold the cursed sword → earn flight.*  

---

## Quirks (honest list)

- **Belt lock:** Storing things in the Dragoon slots means the **waist slot can’t be cleared** until those slots are empty. Plan for that when swapping builds.
- **Sword price:** The Dragoon sword **hurts you while held**. Flight wants it **held**; you’re always trading life for the sky when you go all-in.
- **Durability:** If shoes, shirt, or helm **break to 0%**, their **individual** bonuses and **set flight** fall off until repaired or replaced. The belt’s special rule is the **lock**, not a durability check for the 3 slots in the same way as the other pieces.
- **Solo / dedicated server:** Logic is **server-side**; one player’s set does not give flight to another. (Obvious, but it matters for how you read “active” on the client.)
- **Patched assets:** The mod **adds** Dragoon item variants and recipes; it leans on survival’s clothing/blade types—**keep game version in sync** with `modinfo` (1.22.0+ as specified).

---

## For modders

**Why this mod exists as a reference:** It demonstrates a **full vertical slice**:

1. **Item → stat**  
   - Server: `EntityPlayer.Stats` and custom max-HP path for shirt.  
   - Identifiable `StatLayer` names in code (`ItemTraitsConstants`) for shoes/helm.  
   - Ticks on a server `ModSystem` to apply/remove layers when conditions change.

2. **Item → inventory (extra slots)**  
   - `DragoonBeltInventory` : `InventoryBasePlayer`, class name `dragoonbelt`, `Count == 3`.  
   - Serialized under `WatchedAttributes` key `dragoonBeltInventory` (tree), saved from server each processing pass.  
   - **Client** mirrors the tree into a client inventory instance, opens/closes with `IPlayerInventoryManager` and `Network` packets like vanilla satellite inventories.  
   - **Gui:** `GuiDialog` subclass with richtext + `AddItemSlotGrid`.

3. **Set → global bonus (flight)**  
   - `IServerPlayer.WorldData.FreeMove` (and not `NoClip`) when the combined condition is met; reverted when not.  
   - State flags exposed on `WatchedAttributes` under tree key `itemtraits` (`dragoonShoes`, `dragoonBelt`, `dragoonHelm`, `dragoonShirt`, `dragoonSword`, `fullSet`, etc.) for the client HUD/GUI.  
   - **Sword** tick: `DamageSource` internal piercing on an interval (constants: damage per tick, period in ms).

4. **Extra behaviors**  
   - Belt **force re-equip** if the player has items in the Dragoon slots but the waist slot is empty: scans inventories (excluding the Dragoon inv itself) to put a belt back and notifies the player on a cadence.  
   - **Client-only** `ModSystem` for the belt dialog, hotkey registration, and `RegisterHotKey` with correct modifier order **(alt, ctrl, shift)** in the API.

**Useful string constants (code):** `CodeShoes`, `CodeBelt`, `CodeShirt`, `CodeHelm`, `CodeSword` in `ItemTraitsConstants`; `TreeKey`, `BeltInventoryTreeKey`, `BeltInventoryClassName`.

**Dependencies:** `game` 1.22.0 as in `modinfo.json`.

**Assets:** `assets/itemtraits/` (lang, patches, recipes, shapes, etc.); the mod is **“code + assets”** type.

If you are extending the mod, treat **flight** and **bleed** as *negotiated costs* and **watched attribute** and **tree** keys as *part of the save*—change them with migration in mind.

---

## Flavor

*They said the order rode dragons—not because dragons were tamed, but because they grew wings out of need.*

The **Dragoon** set is a single story in five pieces: boots that devour the earth’s drag, a shirt that dares the heart to beat once more, a helm that teaches skin to close its wounds, a belt with **a nest of small secrets** in three pockets no satchel can claim, and a blade that **tastes the bearer** until the sky mistakes you for one of its own. **Fly** not because the gods allowed it, but because you strapped the whole cursed loadout on and *refused* to set the sword down.

**Tagline (pick one for the workshop):**  
*Five pieces. Three hidden pockets. One sky.*

---

*Document generated for the Item Traits mod. Numbers and rules match the shipped constants and server/client systems as of the mod version in `modinfo.json`; if you rebalance, update this file alongside the code.*
