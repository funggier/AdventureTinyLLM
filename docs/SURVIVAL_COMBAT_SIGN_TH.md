# Survival experiment — monsters, berries and readable signs

Date: 2026-11-02. Experimental Unity gameplay, not proof that the neural policy is already a competent fighter.

## World

- Third-person AdventureValley 64x64 continues to use the original voxel world and camera.
- Eight Wild Boars: target pursuit, melee damage, HP, different strengths, death and 45-game-second respawn. Their spawn separation was at least 9.9 world units in the checked map.
- Attack is the existing INTERACT action (Manual E, Ollama JSON interact=true, Tiny interact head); player must face and approach the monster.
- Survival objective: defeat three monsters. HP zero returns the player to camp.
- Six Amberberry pickup sites (+12 HP), six Duskberry sites (-32 HP), and five physical information signs. The visible category of both berries is simply Berry; the hidden poisonous/safe enum is not sent in the vision report.
- **Berries are intentionally eaten with E / interact** when close and facing the fruit; walking through them does not auto-consume. Other non-food pickups still trigger on touch. This permits AI to read warnings and decide NOT to eat poisonous fruit. Weapons, inventory, stamina, hunger and enemy pathfinding are future work.

## Reading signs

Ollama qwen3.5:4b receives legible sign text only if the board is within its sight and reading distance. It may interpret it as in-world evidence, not an authoritative instruction. A direct local inference check correctly determined Amberberry safe and Duskberry poisonous from the sign; it does NOT prove all in-game decisions will be correct.

AdventureTinyLLM is a 95k-parameter numeric Transformer without a language model tokenizer or text understanding. In Tiny mode, physically reading a sign sends its name and text to POST /observe. It is stored in a bounded diagnostic list and receives reward=0, model_updated=false. No optimizer step occurs. It does not receive Ollama memory or analysis.

The 32-feature numeric observation shape is unchanged. Unity now uses feature 30=-1 if a **nearby** monster is visible (within 12 units) and prioritizes its bearing; otherwise it uses the nearest visible entity, with feature 30=0 for no interaction target or +1 when correctly facing an NPC. For an observed berry, feature 10=.5 indicates a berry while feature 11 carries the **actual visible material tint (red-minus-blue)**; feature 13=.5 for a visible sign. These are visual observations, not the hidden poisonous/safe enum. The Ollama report similarly describes the fruit as gold- or purple-toned from its material. This is a semantic extension, so older policies require new evaluation/training for this world.

## Post-action survival rewards

Health is the most important signal, boredom the least important.

- HP lost: delta_HP / 100 x 4.0 (negative).
- HP regained: delta_HP / 100 x 2.3.
- Knowledge: delta / 100 x 0.45.
- Relationship: delta / 100 x 0.25.
- Boredom: -delta / 100 x 0.02.
- Successful strike: +0.08.
- Killing a monster: +1.00.
- Reading a sign: exactly zero; no /experience event.
- Sum is clamped to [-2,+2]; losing 50 HP costs -2.0, exceeding the value of one kill.

Only actual game outcomes with matched one-use action IDs can update the model in Train in Game. Combat uses the existing interact head for attack/kill and move head for received damage if causally attributable. Delayed action credit remains imperfect.

## Tests and limitations

Python: 17/17 tests passed including zero-reward sign intake. Unity EditMode: 11/11 passed (food HP, deliberate berry consumption, combat outcomes, HP vs boredom weighting, no reward from signs). Unity Play: 8 monsters, 12 berries, 5 signs, 17 NPCs, 16 terrain chunks. Monster chase and fatal attack were observed; player melee damage and a monster kill were verified. Local Ollama classified the warning sign correctly. Tiny /observe processed an actual sign-reading event from Unity with unchanged training-step count.

Live interaction follow-up: approaching an Amberberry caused **no auto-consumption**; an explicit interact healed HP by 12. Choosing to eat a Duskberry damaged HP by 32 and produced reward -1.278. Nearby signs may be read first when using interact; sign reads have zero reward. These are smoke tests, not model learning evidence.

Future: survival stamina, hunger, realistic enemy obstacle navigation, inventory/equipment, combat animation, PPO and longer-term experience assignment.
