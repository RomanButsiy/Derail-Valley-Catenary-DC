# Необов'язковий окремий контролер 2WE3

## Навіщо він потрібен

Авторський `Catenary-DC` — це одночасно OCS та контролер 2WE3. Чисте ядро
`CatenaryDC.dll` містить тільки OCS. Пакет 2WE3CCL дає модель локомотива, а не
заміщення вилученого контролера. Тому без адаптера не слід очікувати тягу.

Інструмент локально копіює `devices`, `circuit`, `unit_A`, `unit_B` та потрібні
utilities із **твого оригінального архіву**, перенаправляє два запити пантографів
у `CatenaryApi` й підключає simulate_all_units до одного tick нового ядра.
Він не копіює OCS або редактор і не змінює оригінального джерела.

**Контролер не включено в основне складання і не збережено в src/.** Його
реалізація існуватиме тільки в ігнорованій робочій папці `artifacts/`.
Генератор перевіряє хеші 49 файлів наданого snapshot `4d1a6a6` і відмовляється
непомітно патчити іншу/відредаговану версію.

## Складання

Спочатку успішно збери нове ядро за docs/BUILD_UK.md. Потрібні Python 3.10+ і
.NET SDK. Запусти команду, замінивши **два шляхи-приклади** своїми:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File 'C:\DV-Work\Derail-Valley-Catenary-DC\tools\Build-Legacy2WE3.ps1' `
  -OriginalSourceDirectory 'C:\DV-Work\AuthorSource\Derail_Valley_electric_concept-full_circuit' `
  -CoreReadyDirectory 'C:\DV-Work\Derail-Valley-Catenary-DC\artifacts\<ідентифікатор-збірки>\ready-to-install\Derail-Valley-Catenary-DC' `
  -GameDirectory 'C:\Program Files (x86)\Steam\steamapps\common\Derail Valley' `
  -Configuration Release
```

OriginalSourceDirectory — розпакований **оригінал останнього наданого ZIP**, не
новий очищений проєкт і не старий інтегрований форк. CoreReadyDirectory — готова
папка **того самого нового ядра**, яке встановлюватимеш. Там мають бути DLL та
`build-info.json`. Скрипт перевіряє їхній хеш. Якщо SDK/assembly API гри несумісні,
компіляція має зупинитися; не підкладай випадкові DLL.

Результат: `artifacts/legacy2we3-.../ready-to-install/Derail-Valley-Catenary-DC-2WE3-Bridge`.
Поряд `build.log`. Нічого не встановлюється автоматично.

## Важлива деталь UMM ID

Папка адаптера називається `Derail-Valley-Catenary-DC-2WE3-Bridge`, але всередині
`info.json` має **Id = Catenary-DC**. Це навмисна сумісність із наявним CCL-пакетом:
`WE6_pack.asset` автора вказує `AdditionalDependencies: Catenary-DC`.

Ядро має власний ID **DerailValleyCatenaryDC**, і адаптер залежить від нього.
**Не міняй ID вручну і не вмикай оригінальний Catenary-DC одночасно.**
Старого `electric_sim.dll` у готовому адаптері немає; його DLL —
`CatenaryDC.Legacy2WE3.dll`. Автооновлення з репозиторію автора не задано.

## Тестовий набір у грі

При закритій грі старий Catenary-DC перемістити поза Mods. Установити:

- нове ядро `Derail-Valley-Catenary-DC`;
- готовий окремий `Derail-Valley-Catenary-DC-2WE3-Bridge`;
- уже встановлені CCL і пакет моделей 2WE3CCL.

Новий World/CarSpawner session підключає контролер; не гаряче перемикай DLL.
Sandbox, спочатку без состава. Перевір обидві секції A/B, струмоприймачі,
відсутність подвійних опор, струм/напругу, втрату живлення на вставці, вихід
у меню й нове завантаження. Потім невеликий состав. Через виправлену відстань
падіння напруги результат під навантаженням може відрізнятися від автора.

**Статус:** підготовку адаптера виконано тут на точному оригіналі; хеші входу,
переадресації та відсутність другої OCS перевірено. Компіляцію, CCL-перевірку
залежностей та тягу у грі тут НЕ виконано. Це допоміжний тестовий шлях, не
сертифікована сумісність із будь-яким майбутнім 2WE3CCL.
