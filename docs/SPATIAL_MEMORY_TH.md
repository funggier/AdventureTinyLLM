# Spatial Memory & Reactive Perception — Tiny v5 (Release v0.3.0)

**สถานะ:** 3 พฤศจิกายน 2026 — เผยแพร่ใน AdventureTinyLLM v0.3.0 (observation 56 ค่า) โดยยังคง Release v0.2.0 เดิมที่ใช้ 51 ค่าไว้ตามเดิม

## พฤติกรรมใหม่

ในเกม `AIAdventureRPG` ระบบ Vision มองเห็นวัตถุด้านหน้าภายในระยะ 30 หน่วย FOV 170° โดยเช็กการบังสายตา; ระบบ Combat สัมผัสการมีมอนสเตอร์รอบตัว 360° ภายใน 9 หน่วย

`AdventurePerceptionMemory` เพิ่มวงสองชั้น:
- **9 หน่วย**: เมื่อวัตถุที่ยังไม่เคยแจ้งเข้าเขตนี้ จะขอให้ AI ประเมินสถานการณ์ใหม่ และไม่ให้ Reward เพียงจากการมองเห็น
- **18 หน่วย**: วัตถุเดิมต้องออกห่างเกินวงนี้ก่อนจึงมีสิทธิ์ทำให้เกิดการแจ้งใหม่เมื่อกลับเข้า 9 หน่วย; การแกว่งระหว่าง 8.9–9.1 หน่วยไม่เกิดการเรียกโมเดลซ้ำ
- เหตุการณ์หลายวัตถุในสแกนเดียวจะรวมเป็นการขอคิดใหม่ครั้งเดียว คูลดาวน์ 0.65 วินาทีเกม
- เมื่อแจ้งใหม่ Tiny จะยกเลิก HTTP คำขอเดิม (ถ้ามี), ไม่ใช้ `action_id` เดิมในการฝึก, ขัดจังหวะ finite move และขอการตัดสินใจใหม่เมื่อพร้อม

## ความจำแผนที่

พิกัด X/Z แบ่งเป็นช่องละ 2 หน่วย เก็บ FIFO **5 Snapshot ต่อพิกัด** เท่านั้น เมื่อพบวัตถุจะอัปเดต `FirstSeen / Unchanged / Moved / MovedAway / Changed` พร้อมชนิด ชื่อ พิกัดจริง และ UTC; กรณีอยู่นิ่งจะยืนยันซ้ำทุก 8 วินาทีเกม ไม่เก็บทุกสแกน

ตำแหน่งในเกมจะบันทึกเป็น JSON ที่ `Application.persistentDataPath/AIAdventureRPG/perception-map-v1-<mapSeed>.json` รองรับสูงสุด 2048 ช่อง ค่าเริ่มต้น; ไม่มีข้อมูลโซนที่ยังไม่เคยตรวจพบ

## 56-feature protocol

52–56 ในการนับแบบมนุษย์ (index 51..55 ในโค้ด):

| Index | ชื่อ | ความหมาย |
|---|---|---|
| 51 | `recent_perception_alert` | มีการแจ้งวัตถุใหม่ใน 5 วินาทีเกมล่าสุด |
| 52 | `coordinate_history_count_norm` | จำนวน Snapshot ณ พิกัดที่กำลังเห็นหาร 5 |
| 53 | `coordinate_change_count_norm` | จำนวนการเปลี่ยนพิกัด/เปลี่ยนสิ่งของหาร 5 |
| 54 | `coordinate_stability_norm` | ความสม่ำเสมอของพิกัดจาก Snapshot ที่ผ่าน ๆ มา |
| 55 | `current_object_ever_moved` | วัตถุปัจจุบันเคยเคลื่อนที่ภายในการเล่นหรือไม่ |

โดย Tiny อ่านและสรุปประวัติ **ตอน Encode ก่อนตัดสินใจ** ไม่ดึงข้อมูลทุกพิกัดทั่วโลกเข้ามาทีเดียว (เพื่อให้โมเดลเล็กยังฝึกได้); ทั้งสองระบบเลือกพิกัดตามลำดับ: มอนสเตอร์ที่กำลังง้างโจมตี → สิ่งใหม่ที่เพิ่งพบ → มอนสเตอร์ในรัศมี → สิ่งของที่มองเห็น และ Ollama สามารถอ่านประวัติ 5 รายการของพิกัดที่เลือกได้สูงสุด 4 จุดใน Prompt

- `schema_version=5`, observation = **56**, `action_protocol=explicit-action-id-v6-spatial`
- Checkpoint format v5, `adventure-observation-v5-spatial-memory`
- แบบจำลองเดิม 51 ค่าใช้โครงสร้างอินพุตไม่ตรงกัน จึงต้องฝึกใหม่; สำรองที่ `checkpoints/legacy/tiny-unity-pre-spatial-v4-51features-2026-11-03.pt` (17,469 updates)
- API รุ่นใหม่พร้อมใช้ที่ `http://127.0.0.1:8765` แบบ local-only
- หากต้องแก้ `AdventureTinyController.Encode` ควรแก้ `src/adventure_tiny_llm/observation.py`, `config.py`, `contracts.py`, `checkpoint.py`, `api.py` และแบบทดสอบร่วมกันทุกครั้ง

## คะแนนที่ปรับ

เฉพาะโจมตีโดน: **Normal Hit +0.35**, **Skill Hit +0.40/เป้าหมาย** (สูงสุดโบนัสโดน +0.65), โบนัสฆ่า +1.45 คงเดิม การมองเห็นและจำพิกัด **ไม่ได้ Reward** จนกว่า AI จะลงมือและมีผลจริง

## วิธีทดสอบ

1. เรียก `python -m pytest` ในโปรเจกต์ Tiny
2. รัน API ผ่าน `scripts/start_api.ps1 -EnableOnline`; ตรวจ `/health` คืน observation_size = 56 และโปรโตคอลที่ถูกต้อง
3. เปิด Unity Editor `Assets/AIAdventureRPG/Scenes/AdventureValley.unity`; กด Play และเลือก `TRAIN TINY`
4. เลื่อนวัตถุใหม่เข้าวง 9 หน่วยแล้วสังเกต `Reactive replans` เพิ่มครั้งเดียว; ขยับเข้าออกแถว 9 จะไม่เพิ่มจนออกเลย 18
5. เลือกตัวละครดู HUD `SPATIAL MEMORY`, `Tracked`, `Cells`, `Coordinate history`
6. ในโหมด OLLAMA ตรวจ `LastPrompt` ว่ามี `Spatial map history for currently visible coordinates` พร้อมประวัติ 5 รายการ

**ข้อจำกัด:** ยังไม่สแกนต้นไม้ตกแต่งทุกชนิดเป็นวัตถุทางเกม และการฝึก Tiny แบบ one-step actor-critic ไม่ใช่การวางแผนเชิงสาเหตุยาวหลายเหตุการณ์ การสรุป 5 ค่าไม่เท่ากับให้ Tiny อ่านข้อความประวัติทั้ง 5 แถวได้เหมือนโมเดลภาษาขนาดใหญ่
