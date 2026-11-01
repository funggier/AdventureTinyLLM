# AdventureTinyLLM — ฝึกผ่านเกมจริง

## ทางเลือกฝึก

**Synthetic Training** รันไฟล์ scripts/train.ps1 ฝึก Transformer จากสถานการณ์จำลอง 2D บน Python/PyTorch แล้วบันทึก checkpoints/tiny-v1.pt

**Train in Game** รัน scripts/start_api.ps1 -EnableOnline แล้วเปิด Unity ฉาก AdventureValley จากนั้นกด TAB จนเป็น Tiny และกด **Train in Game** ใน HUD ตัวละครจะใช้ Neural Policy เดินในเกมจริง และส่งผลจากการเก็บไอเทมหรือปฏิสัมพันธ์ไปฝึกเมื่อเกิดเหตุการณ์จริง

**Evaluate (freeze)** เป็นค่าเริ่มต้น ไม่ส่ง Reward ไปฝึก แม้ API จะอนุญาต Online Training เพื่อไม่ให้ทดลองเล่นแล้วแก้น้ำหนักโดยไม่ตั้งใจ

## วิธีใช้งาน

~~~powershell
cd T:\Space\Projects\ProjectsAI\AdventureTinyLLM
.\scripts\train.ps1
.\scripts\start_api.ps1 -EnableOnline
~~~

ใน Unity เปิด Assets/AIAdventureRPG/Scenes/AdventureValley.unity, Play, TAB สองครั้งจาก Manual เป็น Tiny แล้วเลือก Train in Game / Evaluate (freeze) ด้วยปุ่ม HUD มุมล่างขวา กล้องยังเป็นบุคคลที่สามเหมือนเดิม

start_api.ps1 เลือก checkpoints/tiny-unity-online.pt ก่อน หากมี แล้วจึง fallback ไป checkpoints/tiny-v1.pt; เมื่อเปิด -EnableOnline จะบันทึก Checkpoint จากเหตุการณ์เกมจริงที่ checkpoints/tiny-unity-online.pt เพื่อ Resume ได้

## Action attribution

- /act คืน action_id เฉพาะแต่ละคำสั่ง
- /experience ต้องมี action_id, credit ประเภท move/interact/answer และ reward ที่มาจากผลจริงหลังเหตุการณ์
- เซิร์ฟเวอร์ปฏิเสธ ID ซ้ำ, ใช้แล้ว, หมดอายุ, agent ผิด, หรือการกระทำที่ Navigation Assistant เปลี่ยนทิศแทนโมเดล
- Unity ตรวจความใกล้ของแหล่งเหตุการณ์ก่อนส่ง เพื่อไม่เผลอฝึกจากเหตุการณ์ทดสอบที่เรียกจากระยะไกล
- เปิด Train in Game จะปิดตัวช่วยเลี้ยวหลบสิ่งกีดขวาง เพื่อให้คะแนนไม่ปะปนกับการควบคุมภายนอก; Evaluate จะเปิดตัวช่วยได้
- ทุกผลที่ได้รับการยอมรับปรับน้ำหนัก Tiny Transformer ด้วย One-step Actor-Critic แล้วบันทึก Neural Weights และ Optimizer

## ข้อจำกัด

เป็นการฝึกผ่านผลลัพธ์ของการเล่นจริงแบบ Event-based ไม่ใช่ PPO หรือ Multi-step RL ที่เสร็จแล้ว ยังมีปัญหา delayed reward, กฎการเคลื่อนที่ kinematic และการสำรวจโดยบังเอิญ ต้องทดสอบแผนที่และ Seed อื่นๆ ต่อไป
Ollama ไม่ใช่ครู และไม่ส่ง Prompt, Memory, ข้อมูลฝึกหรือ Neural Weights ให้ Tiny AI
