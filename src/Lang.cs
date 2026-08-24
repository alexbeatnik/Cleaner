// Two-language string table (English default, Ukrainian alternative).
// Lang.Current selects the active language; Lang.T(key) resolves a string,
// falling back to English and then to the key itself if nothing matches.
using System;
using System.Collections.Generic;

namespace WindowsStalker
{
    static class Lang
    {
        public enum Language { English, Ukrainian }
        public static Language Current = Language.English;

        static readonly Dictionary<string, string> En = new Dictionary<string, string>();
        static readonly Dictionary<string, string> Uk = new Dictionary<string, string>();

        public static string T(string key)
        {
            var dict = Current == Language.Ukrainian ? Uk : En;
            string v;
            if (dict.TryGetValue(key, out v)) return v;
            if (En.TryGetValue(key, out v)) return v;
            return key;
        }

        // Test seams: table-wide property tests iterate every key instead of
        // hand-picked lists (tests/LangTests.cs). Raw returns null for a missing
        // entry — unlike T(), which falls back to English and then to the key.
        internal static IEnumerable<string> AllKeys() { return En.Keys; }
        internal static int CountFor(Language lang) { return (lang == Language.Ukrainian ? Uk : En).Count; }
        internal static string Raw(Language lang, string key)
        {
            string v;
            return (lang == Language.Ukrainian ? Uk : En).TryGetValue(key, out v) ? v : null;
        }

        static void A(string key, string en, string uk) { En[key] = en; Uk[key] = uk; }

        static Lang()
        {
            // ---------- chrome ----------
            A("app.tagline", "Clean up. Speed up.", "Очищення. Прискорення.");
            A("status.ready", "Ready.", "Готово.");
            A("tray.open", "Open", "Відкрити");
            A("tray.analyze", "Analyze now", "Проаналізувати");
            A("tray.exit", "Exit", "Вихід");

            A("nav.dashboard", "Dashboard", "Огляд");
            A("nav.cleaner", "Cleaner", "Очищення");
            A("nav.registry", "Registry", "Реєстр");
            A("nav.startup", "Startup", "Автозапуск");
            A("nav.apps", "Apps", "Програми");
            A("nav.space", "Space", "Місце");
            A("nav.settings", "Settings", "Налаштування");

            // ---------- shared ----------
            A("btn.analyze", "ANALYZE", "АНАЛІЗ");
            A("btn.clean", "CLEAN", "ОЧИСТИТИ");
            A("btn.stop", "Stop", "Зупинити");
            A("btn.stopSub", "Cancel the job in progress", "Скасувати поточне завдання");
            A("btn.refresh", "Refresh", "Оновити");
            A("btn.selectAll", "Select all", "Вибрати все");
            A("btn.selectNone", "Select none", "Зняти вибір");
            A("btn.recommended", "Recommended", "Рекомендовані");
            A("btn.close", "Close", "Закрити");
            A("btn.cancel", "Cancel", "Скасувати");
            A("col.item", "Item", "Елемент");
            A("col.size", "Size", "Розмір");
            A("col.files", "Files", "Файлів");
            A("col.name", "Name", "Назва");
            A("col.detail", "Details", "Деталі");
            A("col.status", "Status", "Стан");
            A("col.location", "Location", "Розташування");
            A("col.publisher", "Publisher", "Видавець");
            A("col.version", "Version", "Версія");
            A("col.installed", "Installed", "Встановлено");
            A("col.path", "Path", "Шлях");
            A("col.time", "Time", "Час");
            A("col.event", "Event", "Подія");
            A("drive.freeOf", "free of", "вільно з");
            A("common.never", "never", "ніколи");
            A("common.cancelled", "Cancelled.", "Скасовано.");
            A("common.selected", "selected", "вибрано");
            A("common.of", "of", "з");

            // ---------- dashboard ----------
            A("dash.idleHead", "Ready to clean", "Готово до очищення");
            A("dash.idleSub", "Run an analysis to see what can be freed", "Запустіть аналіз, щоб побачити, що можна звільнити");
            A("dash.busyHead", "Analyzing", "Аналізую");
            A("dash.busySub", "Looking through caches, temp files and logs", "Переглядаю кеші, тимчасові файли та логи");
            A("dash.foundHead", "{0} can be freed", "Можна звільнити {0}");
            A("dash.foundSub", "{0} files across {1} locations", "{0} файлів у {1} розташуваннях");
            A("dash.cleanHead", "Nothing to clean", "Нічого очищати");
            A("dash.cleanSub", "This machine is already tidy", "Цей комп'ютер уже чистий");
            A("dash.gaugeIdle", "not analyzed yet", "аналіз ще не робився");
            A("dash.gaugeFound", "reclaimable", "можна звільнити");
            A("dash.gaugeClean", "all clean", "все чисто");
            A("btn.smartScan", "SMART SCAN", "РОЗУМНИЙ АНАЛІЗ");
            A("btn.smartScanSub", "Find junk in every known location", "Пошук сміття в усіх відомих місцях");
            A("card.storage", "Storage", "Накопичувачі");
            A("card.activity", "Recent activity", "Останні дії");
            A("stat.lastClean", "Last clean", "Останнє очищення");
            A("stat.freedTotal", "Freed in total", "Звільнено всього");
            A("stat.junkFound", "Junk found", "Знайдено сміття");
            A("stat.startupItems", "Startup items", "Елементів автозапуску");

            // Dashboard quick-action tiles
            A("tile.clean", "CLEAN NOW", "ОЧИСТИТИ");
            A("tile.cleanSub", "Remove what the analysis found", "Видалити знайдене аналізом");
            A("tile.registry", "REGISTRY", "РЕЄСТР");
            A("tile.registrySub", "Find broken entries", "Знайти биті записи");
            A("tile.big", "LARGE FILES", "ВЕЛИКІ ФАЙЛИ");
            A("tile.bigSub", "See what is taking the space", "Побачити, що займає місце");
            A("tile.dupes", "DUPLICATES", "ДУБЛІКАТИ");
            A("tile.dupesSub", "Byte-for-byte identical copies", "Побайтово однакові копії");

            // Page strips
            A("stat.categories", "Categories", "Категорій");
            A("stat.selected", "Selected", "Вибрано");
            A("stat.ready", "Ready to remove", "До видалення");
            A("stat.issues", "Issues", "Проблем");
            A("stat.backups", "Backups", "Резервних копій");
            A("stat.entries", "Entries", "Записів");
            A("stat.enabled", "Enabled", "Увімкнено");
            A("stat.disabled", "Disabled", "Вимкнено");
            A("stat.broken", "Broken", "Битих");
            A("stat.programs", "Programs", "Програм");
            A("stat.shown", "Shown", "Показано");
            A("stat.totalSize", "Total size", "Загальний розмір");
            A("stat.found", "Found", "Знайдено");
            A("stat.freeable", "Can be freed", "Можна звільнити");
            A("stat.folder", "Folder", "Папка");

            // ---------- cleaner ----------
            A("card.cleaner", "What to clean", "Що очищати");
            A("clean.notAnalyzed", "Tick the categories to include, then run an analysis.",
                                   "Позначте категорії й запустіть аналіз.");
            A("clean.analyzing", "Analyzing {0}…", "Аналізую {0}…");
            A("clean.found", "{0} in {1} files ready to remove", "{0} у {1} файлах готові до видалення");
            A("clean.nothing", "Nothing found — everything here is already clean.",
                               "Нічого не знайдено — тут уже все чисто.");
            A("clean.confirm", "Permanently delete {0} in {1} files?\r\n\r\nThis cannot be undone.",
                               "Остаточно видалити {0} у {1} файлах?\r\n\r\nЦю дію неможливо скасувати.");
            A("clean.running", "Cleaning…", "Очищаю…");
            A("clean.done", "Freed {0} in {1} files.", "Звільнено {0} у {1} файлах.");
            A("clean.doneFailed", "Freed {0}. {1} items were locked and left in place.",
                                  "Звільнено {0}. {1} елементів були зайняті й залишились на місці.");
            A("clean.blocked", "close {0} first", "спочатку закрийте {0}");
            A("clean.needsAdmin", "needs administrator", "потрібні права адміністратора");
            A("clean.riskyTag", "logs you out", "виходить з акаунтів");
            A("clean.nothingSelected", "Nothing is selected.", "Нічого не вибрано.");
            A("clean.emptyRow", "empty", "порожньо");

            // ---------- rule groups and names ----------
            A("grp.windows", "Windows", "Windows");
            A("grp.browsers", "Browsers", "Браузери");
            A("grp.apps", "Applications", "Програми");
            A("grp.dev", "Developer tools", "Інструменти розробника");

            A("rule.userTemp", "Temporary files", "Тимчасові файли");
            A("rule.winTemp", "Windows temporary files", "Тимчасові файли Windows");
            A("rule.recycleBin", "Recycle Bin", "Кошик");
            A("rule.crashDumps", "Crash dumps", "Дампи збоїв");
            A("rule.errorReports", "Error reports", "Звіти про помилки");
            A("rule.errorReportsSystem", "System error reports", "Системні звіти про помилки");
            A("rule.thumbnailCache", "Thumbnail and icon cache", "Кеш ескізів та іконок");
            A("rule.recentDocs", "Recent documents and jump lists", "Недавні документи та списки переходів");
            A("rule.inetCache", "Internet Explorer cache", "Кеш Internet Explorer");
            A("rule.shaderCache", "Shader cache", "Кеш шейдерів");
            A("rule.fontCache", "Font cache", "Кеш шрифтів");
            A("rule.updateCache", "Windows Update cache", "Кеш оновлень Windows");
            A("rule.deliveryOptimization", "Delivery Optimization files", "Файли оптимізації доставки");
            A("rule.prefetch", "Prefetch data", "Дані Prefetch");
            A("rule.memoryDumps", "Memory dumps", "Дампи пам'яті");
            A("rule.windowsLogs", "Windows logs", "Журнали Windows");
            A("rule.browserCache", "Cache", "Кеш");
            A("rule.browserCookies", "Cookies", "Файли cookie");
            A("rule.browserHistory", "Browsing history", "Історія перегляду");
            A("rule.appCache", "Cache", "Кеш");
            A("rule.mediaCache", "Media cache", "Медіакеш");
            A("rule.documentCache", "Document cache", "Кеш документів");
            A("rule.packageCache", "Package cache", "Кеш пакетів");
            A("rule.buildCache", "Build cache", "Кеш збірок");

            // ---------- registry ----------
            A("card.registry", "Registry issues", "Проблеми реєстру");
            A("reg.intro", "Scans for entries that point at files and keys which no longer exist.",
                           "Шукає записи, що вказують на неіснуючі файли та ключі.");
            A("btn.regScan", "SCAN REGISTRY", "СКАНУВАТИ РЕЄСТР");
            A("btn.regFix", "FIX SELECTED", "ВИПРАВИТИ");
            A("btn.regBackups", "Open backups", "Відкрити резервні копії");
            A("reg.scanning", "Scanning the registry…", "Сканую реєстр…");
            A("reg.found", "{0} issues found", "Знайдено {0} проблем");
            A("reg.none", "No problems found in the registry.", "У реєстрі проблем не знайдено.");
            A("reg.confirm", "Remove {0} registry entries?\r\n\r\nA .reg backup is saved first, so the change can be undone by running it.",
                             "Видалити {0} записів реєстру?\r\n\r\nСпершу буде збережено резервну копію .reg, тож зміну можна скасувати, запустивши її.");
            A("reg.done", "Removed {0} entries. Backup: {1}", "Видалено {0} записів. Резервна копія: {1}");
            A("reg.backupFailed", "The backup could not be written, so nothing was removed.",
                                  "Не вдалося записати резервну копію, тому нічого не видалено.");
            A("reg.kind.appPaths", "Application path", "Шлях застосунку");
            A("reg.kind.sharedDll", "Shared DLL", "Спільна DLL");
            A("reg.kind.startup", "Startup entry", "Запис автозапуску");
            A("reg.kind.fileExt", "File extension", "Розширення файлу");
            A("reg.kind.muiCache", "MUI cache", "Кеш MUI");
            A("reg.kind.uninstall", "Uninstall entry", "Запис видалення");
            A("reg.missingFile", "target is missing: {0}", "цільовий файл відсутній: {0}");
            A("reg.missingClass", "no handler class: {0}", "немає класу-обробника: {0}");

            // ---------- startup ----------
            A("card.startup", "Startup programs", "Програми автозапуску");
            A("startup.intro", "Programs that launch when you sign in. Disabling one does not uninstall it.",
                               "Програми, що запускаються під час входу. Вимкнення не видаляє програму.");
            A("btn.toggle", "Enable / disable", "Увімкнути / вимкнути");
            A("btn.openLocation", "Open location", "Відкрити розташування");
            A("btn.delete", "Delete entry", "Видалити запис");
            A("startup.enabled", "Enabled", "Увімкнено");
            A("startup.disabled", "Disabled", "Вимкнено");
            A("startup.summary", "{0} entries, {1} enabled", "{0} записів, {1} увімкнено");
            A("startup.confirmDelete", "Remove the startup entry \"{0}\"?\r\n\r\nThe program itself stays installed.",
                                       "Видалити запис автозапуску «{0}»?\r\n\r\nСама програма залишиться встановленою.");
            A("startup.adminOnly", "This entry belongs to the whole machine and needs administrator rights to change.",
                                   "Цей запис належить усьому комп'ютеру й потребує прав адміністратора.");
            A("startup.locHkcu", "Registry (this user)", "Реєстр (цей користувач)");
            A("startup.locHklm", "Registry (all users)", "Реєстр (усі користувачі)");
            A("startup.locFolderUser", "Startup folder (this user)", "Папка автозапуску (цей користувач)");
            A("startup.locFolderCommon", "Startup folder (all users)", "Папка автозапуску (усі користувачі)");
            A("startup.broken", "file is missing", "файл відсутній");

            // ---------- apps ----------
            A("card.apps", "Installed programs", "Встановлені програми");
            A("btn.uninstall", "Uninstall", "Видалити");
            A("btn.openFolder", "Open folder", "Відкрити папку");
            A("apps.searchHint", "Search by name or publisher", "Пошук за назвою або видавцем");
            A("apps.summary", "{0} programs, {1} shown", "{0} програм, показано {1}");
            A("apps.confirm", "Run the uninstaller for \"{0}\"?", "Запустити видалення «{0}»?");
            A("apps.noUninstaller", "This entry has no uninstall command registered.",
                                    "Для цього запису не зареєстровано команду видалення.");

            // Activity-log lines. These land in clean.log AND in the dashboard's
            // activity card, so they are user-visible text like any other.
            A("log.startupEnabled", "Enabled at sign-in: {0}", "Увімкнено в автозапуску: {0}");
            A("log.startupDisabled", "Disabled at sign-in: {0}", "Вимкнено в автозапуску: {0}");
            A("log.startupRemoved", "Removed startup entry: {0}", "Видалено запис автозапуску: {0}");
            A("log.uninstallStarted", "Started the uninstaller for {0}", "Запущено видалення {0}");

            // ---------- space ----------
            A("card.space", "Large files and duplicates", "Великі файли та дублікати");
            A("space.intro", "Searches a folder you choose. Deleted items go to the Recycle Bin.",
                             "Шукає у вибраній вами папці. Видалене потрапляє до кошика.");
            A("btn.bigFiles", "LARGE FILES", "ВЕЛИКІ ФАЙЛИ");
            A("btn.duplicates", "DUPLICATES", "ДУБЛІКАТИ");
            A("btn.pickFolder", "Choose folder", "Вибрати папку");
            A("btn.deleteSelected", "Delete to Recycle Bin", "Видалити до кошика");
            A("btn.open", "Show in Explorer", "Показати в Провіднику");
            A("space.folder", "Folder: {0}", "Папка: {0}");
            A("space.scanning", "Scanning {0}…", "Сканую {0}…");
            A("space.hashing", "Comparing {0} candidates…", "Порівнюю {0} кандидатів…");
            A("space.hashingProgress", "Comparing {0} of {1} candidates… press STOP to cancel",
                                       "Порівнюю {0} з {1} кандидатів… натисніть «ЗУПИНИТИ», щоб скасувати");
            A("space.bigFound", "{0} files over {1}, {2} in total", "{0} файлів понад {1}, разом {2}");
            A("space.dupFound", "{0} duplicate groups, {1} can be freed", "{0} груп дублікатів, можна звільнити {1}");
            A("space.dupGroup", "{0} copies × {1}", "{0} копій × {1}");
            A("space.none", "Nothing found in this folder.", "У цій папці нічого не знайдено.");
            A("space.confirm", "Move {0} files ({1}) to the Recycle Bin?",
                               "Перемістити {0} файлів ({1}) до кошика?");
            A("space.recycled", "Moved {0} files to the Recycle Bin.", "Переміщено {0} файлів до кошика.");
            A("space.keepOne", "The first copy in each group is kept and cannot be ticked.",
                               "Перша копія в кожній групі зберігається й не позначається.");

            // ---------- settings ----------
            A("card.general", "General", "Загальні");
            A("card.language", "Language", "Мова");
            A("card.schedule", "Automatic clean", "Автоматичне очищення");
            A("card.about", "About", "Про програму");
            A("card.status", "Status", "Стан");
            A("set.autostart", "Start with Windows (in the tray)", "Запускати разом із Windows (у треї)");
            A("set.confirm", "Ask before cleaning", "Питати перед очищенням");
            A("set.closeToTray", "Closing the window hides it to the tray", "Закриття вікна згортає його в трей");
            A("sched.off", "Off", "Вимкнено");
            A("sched.daily", "Daily", "Щодня");
            A("sched.weekly", "Weekly", "Щотижня");
            A("sched.hint", "An automatic clean uses the recommended categories only and never touches the ones marked as risky.",
                            "Автоматичне очищення використовує лише рекомендовані категорії й ніколи не чіпає позначені як ризиковані.");
            A("sched.last", "Last automatic clean: {0}", "Останнє автоматичне очищення: {0}");
            A("set.mode", "Mode", "Режим");
            A("set.installed", "Installed", "Встановлено");
            A("set.portable", "Portable", "Портативний");
            A("set.version", "Version", "Версія");
            A("set.rules", "Known locations", "Відомих розташувань");
            A("set.autoClean", "Auto clean", "Автоочищення");
            A("btn.installApp", "Install for this user", "Встановити для цього користувача");
            A("btn.uninstallApp", "Uninstall WindowsStalker", "Видалити WindowsStalker");
            A("btn.openLog", "Open the log", "Відкрити журнал");
            A("badge.installed", "✓ Installed", "✓ Встановлено");
            A("about.text",
                "WindowsStalker {0}\r\nDisk cleanup and system tune-up for Windows.\r\nCopyright 2026 Oleksii Poliakov — Apache License 2.0",
                "WindowsStalker {0}\r\nОчищення диска та налаштування системи для Windows.\r\nCopyright 2026 Oleksii Poliakov — Apache License 2.0");

            // ---------- automatic updates ----------
            A("set.autoUpdate", "Update automatically from GitHub", "Оновлювати автоматично з GitHub");
            A("btn.checkUpdate", "Check for updates", "Перевірити оновлення");
            A("update.off", "Automatic updates are off — check by hand any time.",
                            "Автооновлення вимкнено — можна перевіряти вручну будь-коли.");
            A("update.lastCheck", "Checked once a day. Last check: {0}",
                                  "Перевірка раз на добу. Востаннє: {0}");
            A("update.checking", "Checking GitHub for a newer version…",
                                 "Перевіряю GitHub на новішу версію…");
            A("update.upToDate", "Version {0} is the latest one.", "Версія {0} — найновіша.");
            A("update.failed", "Could not check for updates. Check the connection and try again.",
                               "Не вдалося перевірити оновлення. Перевірте з'єднання і спробуйте ще раз.");
            A("update.busy", "Finish the job in progress first, then check again.",
                             "Спершу завершіть поточне завдання, потім перевірте ще раз.");
            A("update.installing", "Updating to {0} — the app will restart in a few seconds…",
                                   "Оновлюю до {0} — програма перезапуститься за кілька секунд…");

            // ---------- about dialog ----------
            A("btn.about", "ABOUT WINDOWSSTALKER", "ПРО WINDOWSSTALKER");
            A("about.title", "About WindowsStalker", "Про WindowsStalker");
            // no "&" in these: a Label eats it as a mnemonic marker
            A("about.version", "Version {0} — free, open source, Apache 2.0 license",
                               "Версія {0} — безкоштовна, відкритий код, ліцензія Apache 2.0");
            A("about.desc",
                "Disk cleanup and system tune-up for Windows: caches, temporary files, logs and crash "
                + "dumps, broken registry entries, startup programs, installed apps, large files and "
                + "byte-for-byte duplicates. One portable executable, no dependencies, no ads, no "
                + "background service.",
                "Очищення диска та налаштування системи для Windows: кеші, тимчасові файли, журнали та "
                + "дампи збоїв, биті записи реєстру, програми автозапуску, встановлені програми, великі "
                + "файли та побайтові дублікати. Один портативний виконуваний файл, без залежностей, "
                + "реклами та фонових служб.");
            A("about.quickStart", "Quick start", "Швидкий старт");
            A("about.howTo",
                "1. SMART SCAN on the dashboard measures every known junk location — nothing is deleted yet.\r\n"
                + "2. The Cleaner page lists what it found; untick anything you want to keep, then CLEAN.\r\n"
                + "3. Registry finds entries pointing at files that are gone. A .reg backup is written before every fix.\r\n"
                + "4. Startup and Apps turn off what launches with Windows and remove what you no longer use.\r\n"
                + "5. Space finds large files and duplicates in a folder you pick — those go to the Recycle Bin, not straight out.\r\n"
                + "6. Long scans can be called off at any point with STOP.\r\n"
                + "7. Runs portable, or installed per-user with no administrator rights — Settings has the button either way.",
                "1. «РОЗУМНИЙ АНАЛІЗ» на панелі огляду вимірює всі відомі місця зі сміттям — поки що нічого не видаляється.\r\n"
                + "2. Сторінка «Очищення» показує знайдене; зніміть позначки з потрібного і натисніть «ОЧИСТИТИ».\r\n"
                + "3. «Реєстр» шукає записи, що вказують на зниклі файли. Перед кожним виправленням пишеться резервна копія .reg.\r\n"
                + "4. «Автозапуск» і «Програми» вимикають те, що стартує з Windows, і видаляють непотрібне.\r\n"
                + "5. «Місце» шукає великі файли та дублікати у вибраній папці — вони йдуть до кошика, а не одразу назавжди.\r\n"
                + "6. Довгі сканування можна будь-коли скасувати кнопкою «ЗУПИНИТИ».\r\n"
                + "7. Працює портативно або встановленою для користувача без прав адміністратора — кнопка є в налаштуваннях.");
            A("about.star", "★  Star this project on GitHub", "★  Постав зірку проєкту на GitHub");
            A("about.releases", "↓  All releases — download the latest version",
                                "↓  Усі релізи — завантажити найновішу версію");
            A("about.follow", "+  Follow the author on GitHub", "+  Підписатися на автора на GitHub");
            A("about.license", "§  Apache 2.0 license — free to use, modify and share",
                               "§  Ліцензія Apache 2.0 — вільно використовуй, змінюй і поширюй");
            A("about.author",
                "Written by Oleksii Poliakov. Copyright 2026 — Apache License 2.0. "
                + "Built by the C# compiler that ships inside Windows: no toolchain, no NuGet, no binary assets.",
                "Автор — Олексій Поляков. Copyright 2026 — ліцензія Apache 2.0. "
                + "Збирається компілятором C#, що входить до складу Windows: без тулчейну, NuGet і бінарних ресурсів.");

            // ---------- elevation, install ----------
            A("admin.offer", "{0} of the selected items live in system folders and need administrator rights.\r\n\r\nRun the elevated helper for those?",
                             "{0} із вибраних елементів розташовані в системних папках і потребують прав адміністратора.\r\n\r\nЗапустити помічник з підвищеними правами для них?");
            A("admin.failed", "The elevated step did not complete — those items were left in place.",
                              "Крок з підвищеними правами не завершився — ці елементи залишились на місці.");
            A("admin.declined", "Skipped the items that need administrator rights.",
                                "Пропущено елементи, що потребують прав адміністратора.");
            A("install.title", "WindowsStalker — Setup", "WindowsStalker — встановлення");
            A("install.installing", "Installing WindowsStalker…", "Встановлюю WindowsStalker…");
            A("install.failed", "Installation failed:\r\n", "Не вдалося встановити:\r\n");
            A("uninstall.confirm", "Remove WindowsStalker from this user account?", "Видалити WindowsStalker з цього облікового запису?");
            A("uninstall.done", "WindowsStalker has been removed.", "WindowsStalker видалено.");
            A("uninstall.error", "Removal error: ", "Помилка видалення: ");
            A("msg.firstRunMode",
                "Run WindowsStalker from where it is now, or install it for this user?\r\n\r\nNow: {0}\r\nInstalled: {1}\r\n\r\nInstalling adds a Start menu entry and lets it start with Windows. No administrator rights are needed either way.",
                "Запускати WindowsStalker звідси чи встановити його для цього користувача?\r\n\r\nЗараз: {0}\r\nПісля встановлення: {1}\r\n\r\nВстановлення додає пункт у меню «Пуск» і дозволяє запуск разом із Windows. Права адміністратора не потрібні в жодному разі.");
            A("msg.installedRestart", "WindowsStalker is installed. The installed copy is starting now.",
                                      "WindowsStalker встановлено. Зараз запуститься встановлена копія.");
        }
    }
}
