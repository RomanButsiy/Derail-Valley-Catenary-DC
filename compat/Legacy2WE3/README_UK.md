# Складання bridge 0.3.0 для 2WE3 і WE-4U

Генератор перевіряє 52 вхідні файли з коміту `03d9de65c874797718accae865295862ab588d62` (тег `v2.8.0`). Прилади, ослаблення поля, тягове коло та порядок викликів розрахунку взято з цього коміту. Два запити контакту 2WE3 і події життєвого циклу підключено до API ядра. [Джерело 2WE3](https://github.com/Knsgf/Derail_Valley_electric_concept/commit/03d9de65c874797718accae865295862ab588d62)

Для WE-4U bridge надає інтерфейс, через який штатний CatenaryForCCL отримує висоту проводу й напругу та передає струм пантографа. Обидва локомотиви використовують одну мережу. Схеми та прилади WE-4U обслуговує його власний мод `EU07Electrics`.

| Компонент | UMM ID | DLL |
|---|---|---|
| Ядро мережі | `DerailValleyCatenaryDC` | `CatenaryDC.dll` |
| Bridge | `Catenary-DC` | `electric_sim.dll` |
| Підтримка пантографів CCL (для WE-4U) | `CatenaryForCCL` | `catenary_for_CCL.importer.dll` |

## Збирання та пакування у PowerShell

Потрібні Python 3.10+, .NET SDK 10+, DLL установленої гри та експорт **Catenary → Export OCS** в Unity 2019.4.41f2. Обидві релізні DLL мають цільову платформу net48. Виконуй команди з кореня репозиторію. [Збирання ядра](../../docs/BUILD_UK.md)

```powershell
git clone --depth 1 --branch v2.8.0 https://github.com/Knsgf/Derail_Valley_electric_concept.git ../2WE3-v2.8.0
$game = 'C:\Program Files (x86)\Steam\steamapps\common\Derail Valley'
.\tools\Build.ps1 -GameDirectory $game -Configuration Release
```

Присвой `$core` шлях із рядка **Ready folder** цієї збірки. Присвой `$bridge` шлях із рядка **OPTIONAL BRIDGE BUILT** наступної команди. Обидві змінні мають вказувати на папки з `info.json`.

```powershell
$core = Read-Host 'Ready folder'
.\tools\Build-Legacy2WE3.ps1 -OriginalSourceDirectory ../2WE3-v2.8.0 -CoreReadyDirectory $core -GameDirectory $game -Configuration Release
$bridge = Read-Host 'OPTIONAL BRIDGE BUILT'
python tools/package_release.py --core $core --bridge $bridge --output artifacts/release-0.3.0
```

Пакувальник звіряє версії, хеш DLL ядра, хеші ресурсів і ревізію контролера. Результат містить два ZIP для UMM, `INSTALL_UK.md`, `SHA256SUMS.txt` і `release.json`. Для відтворення попередніх контролерів доступні параметри `-UpstreamRevision e0b78cb` та `4d1a6a6` із відповідними джерелами. Релізний пакет 0.3.0 використовує `03d9de6`.

## Перевірка

Команда `python tools/test_legacy_bridge.py --source ../2WE3-v2.8.0` звіряє контролер з авторськими файлами. Тести CatenaryForCCL запускають штатний код пантографа з двома версіями імпортера. [Запуск тестів](../../tests/README.md#ccl-pantograph-contact)

Після складання перевір у грі обидві секції 2WE3, покази вольтметра й амперметра під тягою та їх повернення після зняття навантаження. На WE-4U перевір підняття пантографів, появу лінійної напруги, проїзд нейтральної вставки та повторне завантаження збереження. [Встановлення для гравців](../../docs/INSTALL_RELEASE_UK.md)
