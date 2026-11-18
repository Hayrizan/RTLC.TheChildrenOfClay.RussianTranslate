# Русификатор для *The Children of Clay* (BepInEx 5)

Полный русификатор игры **The Children of Clay** (включая DLC «История создания») на базе **BepInEx 5 (x64 Mono)** и **Harmony** без модификации оригинальных файлов ресурсов игры.

## Возможности

- **Регистрация полноценного русского языка** (`Русский`, индекс `8`) в системе локализации Adventure Creator (`SpeechManager` и `RuntimeLanguages`), включая выбор языка в меню **Настройки**.
- **Полный перевод всего текста (149 строк SpeechManager + 45 fallback-строк)**:
  - Реплики и мысли рассказчика (с сохранением всех тегов пауз `[wait:1]`, `[wait:2]`).
  - Главное меню, меню паузы, настройки графики, звука и языка.
  - Все статьи энциклопедии (руны, письмо роваш, Урдунг, Югорская табличка, талтош, мумуш, железо, холодное железо, металлы, глина, Урал, гвозди).
  - Тексты DLC «История создания» (`The making of The Children of Clay`).
  - Исправление нетрекнутых (`lineID = -1`) реплик и несвязанных UI-кнопок (`ОК`, `Назад` и др.), которые в ванильной версии игры не переводились на другие языки.
- **Поддержка кириллицы в шрифтах**:
  - Настройка fallback-таблиц для статических `TMP_FontAsset` (`HTOWERT SDF`, `JMH Typewriter`) с использованием `NotoSerifJP-Regular SDF` и `JMH Typewriter SDF`.
  - Автоматическая подмена некириллических `UnityEngine.Font` (`HTOWERT`, `Chunkfive`) в элементах меню при активном русском языке с восстановлением при переключении обратно.
- **Поддержка поиска ключевых слов в энциклопедии**:
  - Поиск работает как на **русском** (включая синонимы, падежи и варианты написания: `Урдунг`, `Руны`, `Роваш`, `Письмо роваш`, `Югорская табличка`, `Холодное железо`, `Талтош`, `Мумуш`, `Гнудру` и т. д.), так и на **латинице** (`Urdung`, `Gnudru`, `Rovas`, `Runes` и др.).
- **Локализация текстур и спрайтов (14 PNG)**:
  - Таблица расшифровки венгерских рун роваш (`rovasiras` — соответствие рун русскому алфавиту, позволяющее расшифровать слово `УРДУНГ` справа налево).
  - Карта Урала и спичечный коробок (`ural`, `matchbox_outside`, `matchbox_outside_dark`).
  - Финальный экран и титры (`endingscreenlogo`, `createdby`, `musicby`, `jam`, а также титры локализаторов без перевода имён: `translation_pr_br`, `translation_sp_ch`, `translation_ko`, `translation_es`, `translation_jp`, `translation_cat`).
  - Создаются `FullRect`-спрайты, чтобы `Tight`-сетка оригинальных спрайтов не обрезала русские буквы.

## Структура репозитория

- `src/RTLC.TheChildrenOfClay.RussianTranslate/` — исходный код плагина (`.csproj`, `Plugin.cs`, `TranslationManager.cs`, `FontPatcher.cs`, `TextureLocalizer.cs`, `HarmonyPatches.cs`, `SimpleJson.cs`).
- `Assets/rtlc.bundle` — единый Unity AssetBundle (UnityFS LZ4), содержащий `ru.json` (`TextAsset "ru"`) и все 14 локализованных текстур (`Texture2D`).
- `Assets/Translations/ru.json` — исходный файл переводов и синонимов ключевых слов.
- `Assets/Textures/*.png` — исходные локализованные текстуры.
- `tools/generate_textures.py` — скрипт генерации текстур на базе Pillow.
- `tools/build_bundle.py` — скрипт сборки `Assets/rtlc.bundle` на базе `UnityPy`.
- `tools/package_release.py` — скрипт создания ZIP плагина и отдельного ZIP текстур.

## Установка

1. Установите **BepInEx 5 x64 Mono** в папку игры. Плагин использует Harmony, который поставляется с BepInEx.
2. Скачайте ZIP плагина из [последнего релиза](https://github.com/Hayrizan/RTLC.TheChildrenOfClay.RussianTranslate/releases/latest).
3. Распакуйте архив в корневую папку игры. Внутри архива уже есть путь `BepInEx/plugins/RTLC.TheChildrenOfClay.RussianTranslate/`.
4. Запустите игру. Русский язык будет выбран при первом запуске; позже его можно переключить в меню настроек.

Для удаления удалите папку `BepInEx/plugins/RTLC.TheChildrenOfClay.RussianTranslate/`. Файл настроек находится в `BepInEx/config/rtlc.thechildrenofclay.russiantranslate.cfg`, журнал BepInEx — в `BepInEx/LogOutput.log`.

В релиз также включён отдельный ZIP с 14 PNG-текстурами из `Assets/Textures/`. Для работы плагина он не нужен: те же текстуры входят в `rtlc.bundle`.

## Сборка

Для сборки DLL нужны установленная игра и BepInEx 5 x64 Mono: проект ссылается на игровые сборки из `TheChildrenOfClay_Data/Managed` и библиотеки из `BepInEx/core`. Передайте путь к корню игры:

```powershell
dotnet build -c Release -p:GameDir="C:\Games\The Children of Clay"
```

Если указать переменную среды `THE_CHILDREN_OF_CLAY_GAME_DIR`, параметр `-p:GameDir` можно опустить. При сборке DLL автоматически копируется в папку плагина указанной игры вместе с `rtlc.bundle`.

Для пересборки AssetBundle установите Python-пакеты `UnityPy`, `Pillow` и `numpy`, затем укажите файл `resources.assets` из установленной игры:

```powershell
python -m pip install UnityPy Pillow numpy
python tools/build_bundle.py --game-resources "C:\Games\The Children of Clay\TheChildrenOfClay_Data\resources.assets"
```

Скрипт генерации PNG ожидает оригинальные текстуры игры в `tools/source_textures/` (эта папка исключена из Git). Их можно указать отдельно через `--source-textures`; шрифт — через `--font`. Изменённые PNG записываются в `Assets/Textures/`.

После сборки DLL создайте релизные архивы командой `python tools/package_release.py`. Версия берётся из `.csproj`, а ZIP-файлы создаются в исключённой из Git папке `dist/`.

## Лицензия и сторонние компоненты

Для исходного кода и локализованных текстур отдельная лицензия пока не указана. BepInEx, Harmony, UnityPy и материалы игры The Children of Clay сохраняют собственные лицензии и права соответствующих авторов. Этот репозиторий содержит код русификатора, перевод, локализованные текстуры и собранный AssetBundle; файлы игры и BepInEx в релизный ZIP не входят.

## Настройки (`BepInEx/config/rtlc.thechildrenofclay.russiantranslate.cfg`)

- `General.Enabled` (`true`) — включение/отключение плагина.
- `General.ReplaceTextures` (`true`) — замена текстур на русскоязычные.
- `General.AutoSelectRussianOnFirstRun` (`true`) — автоматический выбор русского языка при первом запуске игры.
