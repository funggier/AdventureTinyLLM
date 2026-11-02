# AdventureTinyLLM — Magic Combat Policy v3 (2026-11-02)

Tiny API โปรโตคอล `explicit-action-id-v4-magic`, numeric observation **43 features**, Transformer **96,856 parameters**, checkpoint `format_version=3` และ `schema_version=3`.

- Features 0–39 คงตำแหน่งเดิมจากระบบเดินแบบมีระยะ, แต่ feature 4 = MP/100 แทน Knowledge และ feature 21 = ผลการเปลี่ยน MP จากเหตุการณ์ก่อนหน้า
- Features 40,41,42 = canSpecial, canArc, canHeal เป็น 0/1 ตามค่า MP และสภาพ HP ที่ Unity ตรวจได้จริง
- นอกจาก direction, distance, interact, answer ยังมี ability head ใหม่ 4 ตัวเลือก: none, special, arc, heal
- ราคา MP ใน Unity: special 12, arc 20, heal 25; API mask ตัวเลือกสกิลที่ MP/HP ไม่พร้อมก่อน sampling/argmax และบันทึก mask เดิมกับ action_id
- `/act` คืน field `ability`; `/experience` รับ `credit="ability"` เพื่อฝึก head เลือกสกิลที่เกิดผลจริง; update_from_outcome ใช้ mask เดิมเพื่อไม่ให้ gradient ปนกับการตัดสินใจที่เป็นไปไม่ได้
- โมเดลเก่า observation 40 / format 2 เข้ากันไม่ได้ กับโมเดลใหม่; เก็บ archive ไว้ใน `checkpoints/legacy/tiny-unity-pre-magic-v2-final-2026-11-02.pt` (อย่าลบ/overwrite)
- `scripts/start_api.ps1 -EnableOnline` เริ่ม model ใหม่และอนุญาตให้ฝึกจาก Unity หลังเลือกระบบ TRAIN TINY
- ใช้ MP-based rewards จากผล HP/MP จริง; ไม่ให้ reward จากการเล่น ParticleSystem เพียงอย่างเดียว

ผลตรวจ: Python `pytest` 29/29 ผ่าน, `/health` มี observation_size=43 และ action_protocol ใหม่, `/act` ใช้ MP 0 แล้วตอบ ability=none ในการทดสอบ
