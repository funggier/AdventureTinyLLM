# Magic Points, Combat และเอฟเฟกต์ (2026-11-02)

## ระบบ MP

- เปลี่ยนค่าสถานะ Knowledge เป็น **Magic Points (MP)** ทั้งใน Unity Runtime, HUD, Outcome Journal, ข้อมูล AI, scene และ CSV ของการเล่นใหม่
- เริ่ม HP 72 และ MP 40/100; MP ลดเมื่อใช้ท่าพิเศษและสกิลเท่านั้น; MP ไม่พอจะปฏิเสธการใช้งาน **โดยไม่เสีย MP ไม่ทำดาเมจ และไม่เล่น VFX**
- LoreStone เปลี่ยนเป็นแหล่งเติม MP +18; NPC Good/Bad เพิ่ม/ลด MP ตามผลจริงแทนการได้ Knowledge; เมื่อ Boredom เต็ม MP ยังลดตามเวลา
- เมื่อเสียชีวิต เหลือครึ่งหนึ่งของ MP ที่มี เพื่อไม่ให้ตายแล้วได้ MP ฟรี
- ไฟล์ CSV ใหม่ `Application.persistentDataPath/AIAdventureRPG/events-magic-v3.csv` แยกจาก Knowledge CSV เดิม

## การต่อสู้

| การกระทำ | ปุ่มคน | Damage/ผล | ค่า MP | เงื่อนไขและ VFX |
| --- | --- | --- | --- | --- |
| Normal Attack | E (เมื่อมอนสเตอร์ใกล้และหันถูกทิศ) | 14 | 0 | ประกาย/ฝุ่นเบา ๆ สีหม่น ไม่มีแสงเวท ไม่มีวงเวท |
| Special Attack | F | 32 ต่อเป้าหมาย | 12 | ระยะประชิด + หันถูกทิศ; ประกายม่วง/น้ำเงิน แสงและควัน |
| Arc Pulse | Q | 26 ต่อมอนสเตอร์ในรัศมี 5.3 หน่วย | 20 | แสงฟ้าระเบิดจากตัวละคร + อนุภาคและหมอกควัน; ไม่มีเป้าหมายก็ใช้ MP |
| Heal | R | ฟื้น HP +30 (ไม่เกิน 100) | 25 | แสงและอนุภาคสีเขียว; HP เต็มจะไม่ใช้ MP |

ค่าตัวเลขตั้งได้ใน `AdventureAgent` Inspector. ระบบใช้ `AdventureStats.TrySpendMP` แบบตรวจสอบก่อนหัก, คูลดาวน์ร่วมกับโจมตีปกติ และ `AdventureStats.ResolveAbility` บันทึกผลดาเมจและ MP ที่ใช้จากเหตุการณ์ **ครั้งเดียว** โดยไม่รายงานโจมตีซ้ำ

`AdventureCombatVFX` สร้าง ParticleSystem + Light + วงเวทด้วย LineRenderer อัตโนมัติจาก shader/material ทั่วไป มี soft particle texture สร้างโดยโค้ด ไม่ต้องดาวน์โหลด Asset/ใช้ Blender เพื่อเริ่มทดสอบ เอฟเฟกต์ถูกทำลายเมื่อหมดอายุ ไม่มีการแต่งฉากอย่างถาวร โดย **Normal Attack ไม่มีวงเวทหรือแสง** แต่ Special มีวงม่วงขนาดย่อม, Arc Pulse มีวงฟ้าขยายจากตัวละครรัศมีสูงสุด 5.3 หน่วย, Heal มีวงเขียว แสงชั่วคราวจะค่อย ๆ จางพร้อมการหายไปของอนุภาค

## AI

- Ollama ได้ `MP`, เงื่อนไขและค่า cost ของท่าแต่ละชนิดใน prompt และคืน `ability` = `none|special|arc|heal` เพิ่มจากการเดิน/โต้ตอบเดิม
- Tiny AI แยก head 4 ตัวเลือกการใช้สกิล; feature 40–42 เก็บความพร้อมใช้ Special / Arc / Heal ตาม **MP จริง** และ HP; รวม observation 43 ค่า
- API จะ mask skill ที่ใช้ไม่ได้ก่อน sampling / argmax; การฝึก head จะใช้ mask เดิมจากตอนออก action_id เพื่อคงความถูกต้องของ gradient
- สกิลที่เกิดผลจริงจะให้เครดิต `ability` โดยดู MP ที่จ่าย HP ที่ฟื้น และผลโจมตี; ไม่มีการให้รางวัลแค่เล่นเอฟเฟกต์
- สกิลที่ใช้ไม่ได้เพราะไม่มีเป้าหมาย หันไม่ตรง MP ไม่พอ HP เต็ม หรือคูลดาวน์ ยังบันทึก `AbilityRejected` (ไม่มีดาเมจ/MP cost/VFX) เพื่อให้ Tiny เรียนรู้จากผลลัพธ์ติดลบ -0.08; สำหรับ AI ที่สั่งหันหน้าและโจมตีขณะยืนนิ่ง ระบบจะหันตามคำสั่งก่อนตรวจการโจมตีในเฟรมเดียวกัน
- Tiny schema v3 + action protocol v4-magic ใช้ไม่ได้โดยตรงกับ checkpoint 40 features เดิม จึงเก็บน้ำหนักเดิมใน `checkpoints/legacy/tiny-unity-pre-magic-v2-final-2026-11-02.pt` และเริ่มโมเดลใหม่เมื่อเปิด API

## ผลการตรวจสอบ

- C# Runtime/Test assembly build ผ่าน 0 Errors
- Unity EditMode ล่าสุดผ่าน **56/56** หลังเพิ่มทดสอบการหัก MP, การใช้สกิลที่ไม่สำเร็จ, การหันหน้าก่อนโจมตี และสถานะ Stats ที่ต้องมีเพียงตัวเดียว
- Python pytest 29/29 ผ่าน
- Play Mode: Normal Attack สร้าง 14 damage / 0 MP; Special 32 damage / 12 MP; MP 0 ห้ามใช้ Special และไม่เกิดดาเมจ/VFX; Arc Pulse โจมตีมอนสเตอร์ 2 ตัว ตัวละ 26 damage ใช้ 20 MP; Heal เพิ่ม 30 HP ใช้ 25 MP
- Play Mode ยืนยันมี `AdventureCombatVFX` ติดกับตัวละครและมี ParticleSystem ที่กำลังทำงาน (Arc: 75+28 อนุภาค) พร้อม URP particle shader และวงเวท LineRenderer ใหม่ ตรวจภาพจริงใน Game View ที่ `Captures/AIAdventureRPG-arc-ring-qa.png`
- แก้ `AdventureSceneBuilder` ไม่ให้เพิ่ม `AdventureStats` ซ้ำ; ตรวจฉาก `AdventureValley.unity` เหลือ Stats ของตัวละคร **1 ตัว**
- ฝึก Tiny ใน Play Mode ได้ **29 online updates** จริงและทดสอบ `USE TRAINED` **94 คำสั่ง ไม่พบ API action failure**, เมื่อกลับโหมด MANUAL ไม่มีการฝึกต่อ
- แก้ Unity ParticleSystem startup warning โดยหยุดก่อนปรับ `duration` และทดสอบ Play Mode หลังแก้แล้ว Console มี **0 warnings/errors**
- ปรับระยะเวลาอนุภาคและเพิ่มวงแสงขยาย/จางเพื่อให้เอฟเฟกต์อ่านออกง่ายขึ้น; สีสันและความสวยงามยังปรับต่อได้ตาม art direction โดยไม่แตะระบบดาเมจ
