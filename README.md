# OpenUtau (DELTA SYNTH Edition)

**OpenUtau** คือโปรแกรมแก้ไขเสียงร้อง (Editor) แบบโอเพนซอร์สและใช้งานฟรี ที่ถูกสร้างขึ้นมาเพื่อยกระดับประสบการณ์ของผู้ใช้งานในชุมชน UTAU ให้ทันสมัยและมีประสิทธิภาพสูงสุด

[![สถานะการสร้าง](https://img.shields.io/github/actions/workflow/status/stakira/OpenUtau/build.yml?style=for-the-badge)](https://github.com/stakira/OpenUtau/actions/workflows/build.yml)
[![Discord](https://img.shields.io/discord/551606189386104834?style=for-the-badge&label=discord&logo=discord&logoColor=ffffff&color=7389D8&labelColor=6A7EC2)](https://discord.gg/UfpMnqMmEM)
[![Made And Checked By](https://img.shields.io/badge/Made%20And%20Checked%20By-DELTA%20SYNTH%20%26%20Gemini%20AI-blueviolet?style=for-the-badge)](https://github.com/stakira/OpenUtau)

## การเริ่มต้นใช้งาน (Getting Started)

คุณสามารถดาวน์โหลดเวอร์ชันล่าสุดสำหรับระบบปฏิบัติการต่างๆ ได้ที่นี่:

*   **Windows:** [ดาวน์โหลด x64]
*   **macOS:** [ดาวน์โหลด DMG (x64)]
*   **Linux:** [ดาวน์โหลด tar.gz (x64)]

> **ข้อแนะนำสำคัญ:** เพื่อการใช้งานที่สมบูรณ์แบบที่สุด ผมขอแนะนำให้คุณศึกษาคู่มือผ่าน [Github Wiki](https://github.com/stakira/OpenUtau/wiki) โดยเฉพาะหัวใจหลักอย่าง **Phonemizers** และ **Resamplers** ครับ

---

## คุณสมบัติหลักที่น่าสนใจ (Main Features)

*   **ประสบการณ์การใช้งานที่ทันสมัย:** อินเทอร์เฟซถูกออกแบบมาให้ลื่นไหล รองรับการซูมและเคลื่อนที่ด้วยเมาส์และ Scroll Wheel อย่างเป็นธรรมชาติ
*   **เครื่องมือปรับแต่งเสียงที่ครบครัน:** 
    *   **MIDI Editor:** จัดการโน้ตและนำเข้าไฟล์ VSQX (Vocaloid 4) ได้อย่างง่ายดาย
    *   **Vibrato Editor:** สร้างลูกเอื้อนที่มีเอกลักษณ์และถ่ายทอดอารมณ์ได้ชัดเจน
    *   **Expressions:** ใช้ระบบการปรับแต่งแทนที่ "Flags" แบบเดิม ช่วยให้การจูนเสียงมีความละเอียดสูง (คล้ายกับโปรแกรมระดับอาชีพอย่าง WORLDLINE-R)
*   **ระบบ Phonemizer อัจฉริยะ:** รองรับระบบการร้องหลากหลายรูปแบบ เช่น **VCCV**, CVVC, และ Arpasing ครอบคลุมหลายภาษา รวมถึงภาษาไทยที่เรากำลังพัฒนากันอยู่
*   **เทคโนโลยี Pre-rendering:** ระบบจะทำการประมวลผลเสียงล่วงหน้า ทำให้คุณสามารถฟังพรีวิวระหว่างการปรับจูนได้ทันทีโดยไม่ต้องรอ Render นานๆ
*   **การรองรับ AI:** รองรับนักร้องระบบ **ENUNU (NNSVS)** และเทคโนโลยีใหม่ๆ ในอนาคต

---

## สรุปภาพรวมความสามารถ (All Features)

| คุณสมบัติ | รายละเอียด |
| :--- | :--- |
| **การรองรับภาษา** | UI ภาษาไทยและภาษาอื่นๆ ทั่วโลก ไม่ต้องเปลี่ยน System Locale |
| **ความเข้ากันได้** | รองรับการใช้งานร่วมกับ Resampler ของ UTAU เกือบทั้งหมด |
| **ระบบปลั๊กอิน** | มี API ที่เปิดกว้างสำหรับนักพัฒนาในการสร้าง Macros และ Phonemizers |
| **การทำงานข้ามแพลตฟอร์ม** | ทำงานได้สมบูรณ์แบบทั้งบน Windows, macOS และ Linux |

---

## การมีส่วนร่วมและการพัฒนา (How to Contribute)

หากคุณต้องการเป็นส่วนหนึ่งของการพัฒนาโปรเจกต์นี้:
1.  **รายงานปัญหา:** แจ้งบั๊กผ่านทาง Discord หรือ GitHub Issues
2.  **เสนอแนะฟีเจอร์:** ร่วมพูดคุยแนวทางการพัฒนากับทีมงาน
3.  **เขียนโค้ด:** คุณสามารถส่ง Pull Requests เพื่อแก้ไขหรือปรับปรุงประสิทธิภาพของโปรแกรมได้เสมอ

---

### ไฟล์คำสั่งรันระบบตรวจสอบโปรเจกต์ (.bat)

สำหรับผู้ใช้ที่ต้องการเปิดดูไฟล์เอกสารหรือตรวจสอบความพร้อมของ README ในโฟลเดอร์งานครับ:
```batch
@echo off
:: Made And Checked By DELTA SYNTH & Gemini AI
title OpenUtau Document Viewer v1.0

:loop
echo =======================================================
echo    ระบบตรวจสอบเอกสารโปรเจกต์ (DELTA SYNTH Tools)
echo =======================================================
echo [ลากโฟลเดอร์โปรเจกต์ OpenUtau มาที่นี่เพื่อเปิด README]
set /p folder=Path: 

set folder=%folder:"=%

if exist "%folder%\README.md" (
    echo [สถานะ] ตรวจพบไฟล์เอกสาร กำลังเปิดด้วยโปรแกรมแก้ไข...
    start "" "%folder%\README.md"
) else (
    echo [ข้อผิดพลาด] ไม่พบไฟล์ README.md ในที่อยู่ที่ระบุ
)

echo.
echo -------------------------------------------------------
goto loop
