# Tiny AI / Ollama — คำสั่งเดินกำหนดระยะและการรับรู้เหตุการณ์ (2026-11-02)

## วัตถุประสงค์

เปลี่ยนการเดินของ AI จากคำสั่งทิศทางที่เคลื่อนที่ต่อเนื่องเป็น **Finite-distance Action**: ทิศทาง (X,Z) + ระยะเดิน (world units) + คำสั่งโต้ตอบ/ตอบคำถาม เมื่อเดินครบระยะหรือพบสิ่งกีดขวาง ตัวละครต้องหยุด แล้ว AI จึงรับผลและวางแผนคำสั่งถัดไป **MANUAL/WASD ยังคงเดินต่อเนื่องตามเดิม** ไม่ใช้ระยะเดินนี้

## โปรโตคอลการกระทำ

- Unity `AdventureAction.moveDistance` จำกัด 0–12 หน่วย; 0 คือหยุด/หมุน/โต้ตอบ ทิศทาง X/Z จะถูกปรับเป็นเวกเตอร์หน่วย (diagonal ไม่เดินเร็วกว่าแกนตรง)
- Rigidbody เคลื่อนใน FixedUpdate โดย `AdventureMoveBudget` จำกัด displacement ต่อ physics step ตาม **ระยะที่คงเหลือ**; นับเฉพาะระยะเคลื่อนจริงบน XZ (ไม่นับความสูงชัน) ไม่ใช่ timer
- เมื่อติดขอบหรือเส้นทางแคบ จะหยุดหลังไม่มีความคืบหน้าเป็นเวลา ~0.35–0.45 เกมวินาที แจ้งสาเหตุและพิกัด การหลบเส้นทางโดย Navigation จะแยก flag intervention ไม่ให้ฝึกว่าเป็นความสามารถที่โมเดลเลือกเอง
- เมื่อ HP เสียหายจากผลจริงระหว่างเดิน คำสั่งถูกตัดก่อนระยะครบเพื่อให้ AI ประเมินอันตรายใหม่
- `AdventureEventJournal` เก็บ bounded timeline ของสิ่งที่เห็น (ไม่เปิดเผย safe/poison berry enum), พิกัดที่พบ, ผลจากปฏิสัมพันธ์, ระยะเดินที่ขอ/เดินได้จริง, จุดที่หยุด และสิ่งกีดขวาง บันทึกหนึ่งรายการต่อสิ่งที่เพิ่งพบเพื่อลด spam
- Ollama รับเหตุการณ์ล่าสุดและตำแหน่ง X/Z ใน prompt; คืน JSON `move_x`, `move_z`, `move_distance`, `face_x`, `face_z`, `interact`, `answer`; การเรียกเป็น async และเริ่มคำสั่งใหม่หลังการเดินก่อนหน้าสิ้นสุด

## Tiny Transformer รุ่นใหม่ (v2 distance schema / API v3)

**ไม่ compatible กับ checkpoint รุ่นเก่า** เนื่องจากข้อมูลสังเกตและ head เปลี่ยน:
- Input 40 features = 32 ค่าจากเดิม + 8 ค่า:
  - 32: last_requested_distance_norm (ระยะที่ขอ /12)
  - 33: last_travelled_distance_norm (ระยะที่เดินได้จริง /12)
  - 34: last_move_status (+1 ถึงระยะ, -1 ถูกบล็อก, 0 อื่น ๆ)
  - 35–36: last_stop_x_norm, last_stop_z_norm
  - 37: last_seen_kind (ชนิดที่ตรวจพบ: มอนสเตอร์/หนาม/สมุนไพร/NPC ฯลฯ)
  - 38–39: last_seen_x_norm, last_seen_z_norm
- Move head 9 ทิศทาง (รวม idle), **Distance head 5 ตัวเลือก: 1, 2, 4, 6, 10 หน่วย**; move=idle บังคับระยะเป็น 0
- รางวัลที่มาจากการเดินจริงและผูกกับ `action_id` จะอัปเดตทั้ง Move head และ Distance head โดยใช้ผลจากการกระทำเดียวกัน การเดินแล้วถูกบล็อกจริงอาจได้รับ -0.12 เพียงหนึ่งครั้ง แม้ Navigation จะพยายามหาทางแล้วแต่ไม่สำเร็จ (ไม่ให้รางวัลแก่การหลบหลีกที่ระบบช่วย) ส่วนการตาย -2.0 ตามนโยบายเดิม
- `/health` รายงาน `observation_size=40`, `action_protocol=explicit-action-id-v3-distance`, `distance_options`, `trained`, `training_steps`; Unity ไม่ยอมใช้บริการที่ยังเป็น 32-feature
- `/act` ต้องส่ง observation 40 ตัว คืน `move_distance` เพิ่มเติมจาก v2; `/experience` ยังคงมี action_id แบบใช้ครั้งเดียวและป้องกันเครดิตซ้ำ
- โมเดล 96,404 parameters, random seed 1234; การเรียนรู้ใหม่จะเริ่มจากศูนย์

## Checkpoint และการฝึกใหม่

น้ำหนักแบบเก่า 490 updates ถูกย้ายจาก `checkpoints/tiny-unity-online.pt` ไปที่
`checkpoints/legacy/tiny-unity-pre-finite-distance-2026-11-02.pt` โดยไม่แก้ไขไฟล์เดิมและไม่โหลดอัตโนมัติ

เมื่อ API รุ่นใหม่ไม่พบ checkpoint ที่เข้ากันได้ จะเริ่ม `trained=false`, `training_steps=0`. เปิด `scripts/start_api.ps1 -EnableOnline` และกด **TRAIN TINY** จาก HUD หากเกิด outcome ที่ผูกกับ action_id จริง จะบันทึก `checkpoints/tiny-unity-online.pt` ใหม่ภายใต้ schema รุ่นใหม่

**USE TRAINED** ปิดไว้จนมีผลฝึกใหม่ยืนยันแล้ว; **OLLAMA** ไม่อัปเดตโมเดล Tiny; การควบคุมคนยังเหมือนเดิม

## ตรวจสอบ

- Python pytest รวม API, distance head, RL gradients และเครดิต action_id ผ่าน 26 รายการ
- Unity EditMode **46/46 ผ่าน** รวม distance budget, event journal, Manual preservation, Zone, Survival และการแยกโหมด
- Runtime/Test assembly ผ่าน 0 Errors; Unity Play Mode ทดสอบ Tiny **79 คำสั่ง / 0 API action failures**, รายงานเดินสำเร็จ **10.00/10.00 หน่วย** และมี Reward ฝึกจริงจนสร้าง checkpoint v2 จำนวน **22 steps** ตอนที่หยุดทดสอบ
- Unity Play Mode ทดสอบ Ollama ตอบกลับจริง **2 คำสั่ง** โดยมีรายงานเดินครบ **3.00/3.00** และ **4.00/4.00 หน่วย**; prompt มี `move_distance` และพิกัดเหตุการณ์
- Unity Console ในช่วงตรวจ Tiny รายงาน 0 Errors; โมเดล Ollama บนเครื่องใช้เวลาประมวลผลค่อนข้างมาก จึงปรับ request timeout เป็น 50 วินาทีและลดข้อความบริบทล่าสุดให้พอดี
