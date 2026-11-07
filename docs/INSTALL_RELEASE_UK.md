# Установлення Catenary DC 0.2.0 та 2WE3 bridge

Пакет призначений для стандартної карти Derail Valley.

1. Закрий гру, збережи резервну копію модів і save. Старий авторський
   `Catenary-DC` з `electric_sim.dll` перемісти **поза папку Mods**.
   Перейменування папки всередині Mods не вимикає її надійно.
2. Через Unity Mod Manager установи `Derail-Valley-Catenary-DC-0.2.0.zip`.
   Це ядро разом із прийнятою мережею GF / 6 m Visual 2.
3. Для 2WE3 додатково встанови `Derail-Valley-Catenary-DC-2WE3-Bridge-0.2.0.zip`,
   CCL та окремі моделі **2WE3CCL 2.3.7**. Моделі та CCL не входять у реліз.
   Bridge використовує авторський контролер із `e0b78cb`.
4. Стару версію bridge заміни новою; не залишай другу копію. Bridge має UMM ID
   `Catenary-DC`, щоб задовольнити залежність моделей; ядро має ID
   `DerailValleyCatenaryDC`. Оригінальний `electric_sim` одночасно не запускай.
5. При переході з наших тестових пакетів установи ядро в чисту папку. Прибери
   28 старих scenery overrides (зокрема `scenery_gf_station_transit.json`):
   їхня геометрія вже є у бандлі. Список — у
   [NETWORK_6M_RELEASE_UK.md](https://github.com/RomanButsiy/Derail-Valley-Catenary-DC/blob/v0.2.0/docs/NETWORK_6M_RELEASE_UK.md).
   Не копіюй старі `Scenery/`, `UserData/`, `network.json` та `settings.json`
   поверх чистого комплекту без свідомої потреби в overrides.
6. Запусти окремий Sandbox. У UMM обидва потрібні моди мають завантажитися
   без помилок. Спочатку перевір мережу та локомотив без состава.

Без 2WE3 достатньо ZIP ядра; звичайним дизелям він не додає пантографів.
Для відкату закрий гру, перемісти нові моди поза Mods і поверни резервну копію.

Джерело моделей:
[2WE3CCL.zip, авторський реліз v2.5.4](https://github.com/Knsgf/Derail_Valley_electric_concept/releases/download/v2.5.4/2WE3CCL.zip).
Звідти потрібні саме моделі, а не авторський `Catenary-DC.zip`.
Зберігай залежності та інструкцію CCL з пакета автора.

`SHA256SUMS.txt` дозволяє звірити завантажені файли. У PowerShell:

```powershell
Get-FileHash .\Derail-Valley-Catenary-DC-0.2.0.zip -Algorithm SHA256
Get-FileHash .\Derail-Valley-Catenary-DC-2WE3-Bridge-0.2.0.zip -Algorithm SHA256
```

GitHub Source code ZIP — не готовий мод. Double-Tracked Valley цим профілем
не підтримується. Інформацію про фактично перевірені версії гри/CCL дивись
в описі конкретного релізу.
