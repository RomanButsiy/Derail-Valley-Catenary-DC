# Патч: завершена GF і мережа 6 м · Visual 2

База: `ea4636819112d694c966f76fa345f8ce2ffd0bca` з
[RomanButsiy/Derail-Valley-Catenary-DC](https://github.com/RomanButsiy/Derail-Valley-Catenary-DC).
Еталон: прийнятий користувачем `Catenary_6m_Visual2.zip`.

## Що інтегровано

- Завершена станція GF: прийняті тролейні ділянки, бокові рейки,
  проходи під переходами, знаки, парк D з GF1100 і вихід до трикутника.
- Відкриті ділянки HB–CS–CMS–SM, HB–GF–шахти й погоджені підходи GF:
  висота контактного дроту 6 м із прийнятими переходами до тунелів.
- Останнє підняття 161 опори з рамковими консолями на HB–CS–CMS–SM.
- Перенесена N01, живлення лінії до FF від GF1500 та деелектрифікація
  погоджених гілок і нафтової свердловини.

Під час інтеграції жодний об’єкт не пересувався. Усі 14 545 об’єктів
збережено з тією самою кратністю. Станційний моноліт із 1030 об’єктів
розподілено: 375 у `scenery_GF-HB[GF].json`, 332 у `scenery_GF.json`,
305 у новий `scenery_GF-SM[GF].json` і 18 у новий `scenery_[GF]![SM].json`.
Загалом у бандлі 48 JSON. Назви й GUID існуючих ресурсів збережені.

## Застосування

Збережи свої незакомічені зміни. У корені робочої копії на вказаній базі:

```powershell
git apply --check Catenary_GF_6m_Visual2.patch
git apply Catenary_GF_6m_Visual2.patch
python tools/verify_network_release.py
python tools/validate_project.py
python tools/test_network_release.py
python tools/test_authoring_tools.py
```

Якщо `--check` повідомить про конфлікт із новішими локальними змінами,
узгодь його перед застосуванням. Патч не виконує commit, push або встановлення.

Для незалежного звіряння з попереднім прийнятим тестовим архівом:

```powershell
python tools/verify_network_release.py --accepted-zip Catenary_6m_Visual2.zip
```

Перевірка відновлює всі 28 вхідних накладок у початковому порядку та порівнює
кожне значення. Також перевіряються суми всіх ресурсів і `.meta`, унікальність
GUID, кількість об’єктів, визначення підстанцій і посилання проводів на них.
Старі `verify_phase1_release.py` і `verify_phase2_release.py` без параметрів
тепер перевіряють поточну версію. Історичні маніфести збережені для довідки.

## Збирання і перехід з тестового пакета

1. Відкрий `unity_project` у Unity **2019.4.41f2** і виконай
   **Catenary → Export OCS**. Попередній бандл не містить цього патча.
2. Запусти штатний `BUILD.cmd` або `tools/Build.ps1`.
3. Закрий гру. Збережи попередню збірку та перенеси перелічені нижче зовнішні
   накладки з папки `Scenery` встановленого мода в резервну папку поза модом.
   Вони тепер вбудовані; моноліт GF інакше додасть повторну геометрію та GF1100.
4. Встанови результат із `ready-to-install`. Збережи штатне
   `included_scenery: null`, щоб завантажилися всі розстановки й джерела.
5. Повтори проїзд на зібраній версії: переходи та кран GF, бокові рейки,
   вихідний трикутник, N01/N02/N07/N08/N09/N04, тунельні переходи висоти
   та опори HB–CS–CMS–SM.

Назви зовнішніх файлів, які замінює новий бандл (регістр не має значення):

```text
scenery_[cms]![sm].json
scenery_[ff]![cme-ime].json
scenery_[ff]![sm].json
scenery_[gf]![cme-ime].json
scenery_[hb]![cms].json
scenery_[hb]![gf].json
scenery_[hb]![sm].json
scenery_cms-sm[cms].json
scenery_cms.json
scenery_cs-cms.json
scenery_cs-hb[cms].json
scenery_cs.json
scenery_ff-ime[ff].json
scenery_ff-sm[ff].json
scenery_gf-hb[gf].json
scenery_gf.json
scenery_gf_station_transit.json
scenery_hb-cs[hb].json
scenery_hb-gf[hb].json
scenery_hb-sm[hb].json
scenery_ime-ff[cme-ime].json
scenery_ime-gf[cme-ime].json
scenery_own.json
scenery_phase1_hb_cs_cms_visual1.json
scenery_phase2_gf_visual1.json
scenery_sm-cms[sm].json
scenery_sm-ff[sm].json
scenery_sm-hb[sm].json
```

## Межі перевірки

Виконано точне порівняння з прийнятим архівом, 65 перевірок проєкту,
50 тестів інструментів і 7 тестів перевірки інтеграції. Пряме застосування
патча перевірено на чистій копії базового коміту.
C#-збірка, Unity-експорт, PowerShell і проїзд нової збірки тут не виконувалися.
Код симуляції, моделі, CSV та контролер локомотива цим патчем не змінюються.
