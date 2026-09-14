using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// Localization. The base language is English, also the fallback for anything untranslated.
    ///
    /// Translations live directly in code: the launcher ships as a single file, and satellite
    /// .resx assemblies would only complicate distribution. Key → dictionary by language code,
    /// so every variant of one string sits side by side and is edited in one glance.
    ///
    /// Nothing in a non-English language (or any human-facing text at all) should exist outside
    /// this file — otherwise the string won't make it into translation.
    ///
    /// To add an 11th language: add its code to <see cref="Supported"/> and its native name to
    /// <see cref="DisplayNames"/>, then add one more parameter to <see cref="L"/> (give it a
    /// default of <c>null</c> so every existing call site keeps compiling unchanged) and pass it
    /// through into the returned dictionary. Existing keys don't need to be touched all at once —
    /// <see cref="Lookup"/> already falls back to English for any key missing the new language, so
    /// translations can be filled in key by key, over time, without ever leaving a blank string.
    /// </summary>
    public static class Loc
    {
        public const string Fallback = "en";

        /// <summary>Languages that have translations.</summary>
        public static readonly IReadOnlyList<string> Supported =
            new[] { "en", "de", "es", "fr", "pl", "pt", "ru", "sv", "zh", "uk" };

        /// <summary>
        /// Language names in their own language — for a future dropdown in settings.
        /// Names must not be translated in the language picker: a person looks for their
        /// own script, not a translation into a language they don't understand.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> DisplayNames =
            new Dictionary<string, string>
            {
                ["en"] = "English",
                ["de"] = "Deutsch",
                ["es"] = "Español",
                ["fr"] = "Français",
                ["pl"] = "Polski",
                ["pt"] = "Português",
                ["ru"] = "Русский",
                ["sv"] = "Svenska",
                ["zh"] = "中文",
                ["uk"] = "Українська"
            };

        private static string _language = Fallback;

        /// <summary>Current language, two-letter code.</summary>
        public static string Language => _language;

        static Loc() => UseSystemLanguage();

        /// <summary>Normalizes a code to two-letter lowercase: "ru-RU" -> "ru".</summary>
        public static string Normalize(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode)) return null;

            string code = languageCode.Trim().ToLowerInvariant();
            return code.Length > 2 ? code.Substring(0, 2) : code;
        }

        public static bool IsSupported(string languageCode)
        {
            string code = Normalize(languageCode);
            return code is not null && Supported.Contains(code);
        }

        /// <summary>Takes the language from the system; falls back to English if unsupported.</summary>
        public static void UseSystemLanguage() =>
            Use(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

        /// <summary>
        /// Language from a user setting, otherwise the system one.
        ///
        /// There's no settings UI yet — this method exists ahead of time so that adding one
        /// comes down to reading a single key from config.ini and calling this, with no
        /// changes needed in the core.
        /// </summary>
        public static void UsePreferredOrSystem(string preferred)
        {
            if (IsSupported(preferred)) Use(preferred);
            else UseSystemLanguage();
        }

        public static void Use(string languageCode)
        {
            string code = Normalize(languageCode);
            if (code is null) return;

            _language = Supported.Contains(code) ? code : Fallback;
        }

        /// <summary>String by key, with argument substitution.</summary>
        public static string T(string key, params object[] args)
        {
            string text = Lookup(key);
            return args is { Length: > 0 }
                ? string.Format(CultureInfo.CurrentCulture, text, args)
                : text;
        }

        private static string Lookup(string key)
        {
            if (!Table.TryGetValue(key, out Dictionary<string, string> byLanguage))
                return key; // no such key — show the key itself, easy to spot while debugging

            if (byLanguage.TryGetValue(_language, out string text) && !string.IsNullOrEmpty(text))
                return text;

            return byLanguage.TryGetValue(Fallback, out string fallback) ? fallback : key;
        }

        private static Dictionary<string, string> L(
            string en, string de, string es, string fr, string pl, string pt, string ru, string sv, string zh,
            string uk = null) =>
            new()
            {
                ["en"] = en, ["de"] = de, ["es"] = es, ["fr"] = fr, ["pl"] = pl,
                ["pt"] = pt, ["ru"] = ru, ["sv"] = sv, ["zh"] = zh, ["uk"] = uk
            };

        private static readonly Dictionary<string, Dictionary<string, string>> Table = new()
        {
            // ---------- ClientFolderGuard ----------

            ["guard.noPath"] = L(
                "the client folder path is not set",
                "der Pfad zum Client-Ordner ist nicht angegeben",
                "no se ha indicado la ruta de la carpeta del cliente",
                "le chemin du dossier client n'est pas défini",
                "nie podano ścieżki do folderu klienta",
                "o caminho da pasta do cliente não foi definido",
                "путь к папке клиента не задан",
                "sökvägen till klientmappen är inte angiven",
                "未指定客户端文件夹路径",
                "шлях до папки клієнта не вказано"),

            ["guard.specifyFolder"] = L(
                "Specify the client folder.",
                "Geben Sie den Client-Ordner an.",
                "Indique la carpeta del cliente.",
                "Indiquez le dossier client.",
                "Wskaż folder klienta.",
                "Indique a pasta do cliente.",
                "Укажите каталог клиента.",
                "Ange klientmappen.",
                "请指定客户端文件夹。",
                "Вкажіть папку клієнта."),

            ["guard.badPath"] = L(
                "invalid path: {0}",
                "ungültiger Pfad: {0}",
                "ruta no válida: {0}",
                "chemin invalide : {0}",
                "nieprawidłowa ścieżka: {0}",
                "caminho inválido: {0}",
                "некорректный путь: {0}",
                "ogiltig sökväg: {0}",
                "路径无效：{0}",
                "недійсний шлях: {0}"),

            ["guard.checkSpelling"] = L(
                "Check the spelling of the path.",
                "Überprüfen Sie die Schreibweise des Pfades.",
                "Compruebe que la ruta esté bien escrita.",
                "Vérifiez l'orthographe du chemin.",
                "Sprawdź poprawność ścieżki.",
                "Verifique a grafia do caminho.",
                "Проверьте написание пути.",
                "Kontrollera att sökvägen är rätt stavad.",
                "请检查路径拼写。",
                "Перевірте правильність написання шляху."),

            ["guard.unreadable"] = L(
                "could not read the folder contents: {0}",
                "der Ordnerinhalt konnte nicht gelesen werden: {0}",
                "no se pudo leer el contenido de la carpeta: {0}",
                "impossible de lire le contenu du dossier : {0}",
                "nie udało się odczytać zawartości folderu: {0}",
                "não foi possível ler o conteúdo da pasta: {0}",
                "не удалось прочитать содержимое папки: {0}",
                "kunde inte läsa mappens innehåll: {0}",
                "无法读取文件夹内容：{0}",
                "не вдалося прочитати вміст папки: {0}"),

            ["guard.notClient"] = L(
                "the folder is not empty and does not look like a Valheim client — none of {0} were found. Unrelated contents: {1}",
                "der Ordner ist nicht leer und sieht nicht nach einem Valheim-Client aus — nichts von {0} gefunden. Fremder Inhalt: {1}",
                "la carpeta no está vacía y no parece un cliente de Valheim: no se encontró nada de {0}. Contenido ajeno: {1}",
                "le dossier n'est pas vide et ne ressemble pas à un client Valheim — rien de {0} n'a été trouvé. Contenu étranger : {1}",
                "folder nie jest pusty i nie wygląda na klienta Valheim — nie znaleziono żadnego z {0}. Obca zawartość: {1}",
                "a pasta não está vazia e não parece um cliente do Valheim — nada de {0} foi encontrado. Conteúdo alheio: {1}",
                "папка не пуста и не похожа на клиент Valheim — не найдено ничего из {0}. Постороннее содержимое: {1}",
                "mappen är inte tom och ser inte ut som en Valheim-klient — inget av {0} hittades. Främmande innehåll: {1}",
                "该文件夹非空且不像 Valheim 客户端——未找到 {0} 中的任何内容。无关内容：{1}",
                "папка не порожня і не схожа на клієнт Valheim — не знайдено жодного з {0}. Сторонній вміст: {1}"),

            ["guard.andMore"] = L(
                "and {0} more",
                "und {0} weitere",
                "y {0} más",
                "et {0} de plus",
                "i jeszcze {0}",
                "e mais {0}",
                "и ещё {0}",
                "och {0} till",
                "还有 {0} 项",
                "і ще {0}"),

            ["guard.noParent"] = L(
                "no existing parent folder for «{0}»",
                "kein vorhandener übergeordneter Ordner für «{0}»",
                "no existe ninguna carpeta superior para «{0}»",
                "aucun dossier parent existant pour «{0}»",
                "brak istniejącego folderu nadrzędnego dla «{0}»",
                "não existe nenhuma pasta superior para «{0}»",
                "не существует ни одной родительской папки для «{0}»",
                "ingen befintlig överordnad mapp för «{0}»",
                "找不到 «{0}» 的任何上级文件夹",
                "не існує жодної батьківської папки для «{0}»"),

            ["guard.checkDrive"] = L(
                "Check that the drive is connected and the path is correct.",
                "Prüfen Sie, ob das Laufwerk verbunden und der Pfad korrekt ist.",
                "Compruebe que la unidad esté conectada y la ruta sea correcta.",
                "Vérifiez que le lecteur est connecté et que le chemin est correct.",
                "Sprawdź, czy dysk jest podłączony, a ścieżka poprawna.",
                "Verifique se a unidade está conectada e o caminho está correto.",
                "Проверьте, подключён ли диск и верен ли путь.",
                "Kontrollera att enheten är ansluten och att sökvägen stämmer.",
                "请检查磁盘是否已连接、路径是否正确。",
                "Перевірте, чи підключено диск і чи правильний шлях."),

            ["guard.notWritable"] = L(
                "no write access to «{0}» ({1}: {2})",
                "kein Schreibzugriff auf «{0}» ({1}: {2})",
                "sin permiso de escritura en «{0}» ({1}: {2})",
                "aucun accès en écriture à «{0}» ({1} : {2})",
                "brak uprawnień do zapisu w «{0}» ({1}: {2})",
                "sem permissão de escrita em «{0}» ({1}: {2})",
                "нет прав на запись в «{0}» ({1}: {2})",
                "ingen skrivbehörighet till «{0}» ({1}: {2})",
                "对 «{0}» 没有写入权限（{1}：{2}）",
                "немає прав на запис до «{0}» ({1}: {2})"),

            ["guard.advice.windows"] = L(
                "Windows: clear the «Read-only» attribute on the folder, or move the client into a folder inside your user profile — writing to Program Files or a drive root requires administrator rights. Also check that antivirus is not blocking the folder and that the game is not currently running.",
                "Windows: Entfernen Sie das Attribut «Schreibgeschützt» vom Ordner oder verschieben Sie den Client in einen Ordner in Ihrem Benutzerprofil — Schreiben in Programme oder im Laufwerksstamm erfordert Administratorrechte. Prüfen Sie außerdem, ob ein Virenscanner den Ordner blockiert und ob das Spiel gerade läuft.",
                "Windows: quite el atributo «Solo lectura» de la carpeta o mueva el cliente a una carpeta dentro de su perfil de usuario: escribir en Archivos de programa o en la raíz del disco requiere permisos de administrador. Compruebe también que el antivirus no bloquee la carpeta y que el juego no esté en ejecución.",
                "Windows : retirez l'attribut «Lecture seule» du dossier, ou déplacez le client dans un dossier de votre profil utilisateur — écrire dans Program Files ou à la racine du disque nécessite les droits administrateur. Vérifiez aussi que l'antivirus ne bloque pas le dossier et que le jeu n'est pas en cours d'exécution.",
                "Windows: usuń atrybut «Tylko do odczytu» z folderu lub przenieś klienta do folderu w profilu użytkownika — zapis w Program Files lub w katalogu głównym dysku wymaga uprawnień administratora. Sprawdź też, czy antywirus nie blokuje folderu i czy gra nie jest uruchomiona.",
                "Windows: remova o atributo «Somente leitura» da pasta ou mova o cliente para uma pasta dentro do seu perfil de usuário — gravar em Arquivos de Programas ou na raiz do disco exige permissões de administrador. Verifique também se o antivírus não está bloqueando a pasta e se o jogo não está em execução.",
                "Windows: снимите атрибут «Только чтение» с папки, либо перенесите клиент в каталог внутри профиля пользователя — в Program Files и корне диска запись требует прав администратора. Также проверьте, не блокирует ли папку антивирус и не запущена ли игра прямо сейчас.",
                "Windows: ta bort attributet «Skrivskyddad» från mappen, eller flytta klienten till en mapp i din användarprofil — att skriva till Program Files eller enhetens rot kräver administratörsbehörighet. Kontrollera även att antivirus inte blockerar mappen och att spelet inte körs.",
                "Windows：请取消该文件夹的“只读”属性，或将客户端移动到用户配置文件下的文件夹——写入 Program Files 或磁盘根目录需要管理员权限。另请确认杀毒软件没有拦截该文件夹，且游戏当前没有在运行。",
                "Windows: зніміть атрибут «Тільки читання» з папки, або перенесіть клієнт у папку всередині свого профілю користувача — запис у Program Files чи в корінь диска потребує прав адміністратора. Також перевірте, що антивірус не блокує папку і що гра наразі не запущена."),

            ["guard.advice.macos"] = L(
                "macOS: grant write access with  chmod -R u+w \"{0}\"  or change the owner:  sudo chown -R $(whoami) \"{0}\"  . If the folder is in Desktop, Documents or Downloads, also allow access in System Settings → Privacy & Security → Files and Folders.",
                "macOS: Erteilen Sie Schreibrechte mit  chmod -R u+w \"{0}\"  oder ändern Sie den Eigentümer:  sudo chown -R $(whoami) \"{0}\"  . Liegt der Ordner in Schreibtisch, Dokumente oder Downloads, erlauben Sie den Zugriff zusätzlich unter Systemeinstellungen → Datenschutz & Sicherheit → Dateien und Ordner.",
                "macOS: conceda permisos de escritura con  chmod -R u+w \"{0}\"  o cambie el propietario:  sudo chown -R $(whoami) \"{0}\"  . Si la carpeta está en Escritorio, Documentos o Descargas, permita además el acceso en Ajustes del Sistema → Privacidad y seguridad → Archivos y carpetas.",
                "macOS : accordez les droits d'écriture avec  chmod -R u+w \"{0}\"  ou changez le propriétaire :  sudo chown -R $(whoami) \"{0}\"  . Si le dossier se trouve dans Bureau, Documents ou Téléchargements, autorisez aussi l'accès dans Réglages Système → Confidentialité et sécurité → Fichiers et dossiers.",
                "macOS: nadaj prawa zapisu poleceniem  chmod -R u+w \"{0}\"  lub zmień właściciela:  sudo chown -R $(whoami) \"{0}\"  . Jeśli folder znajduje się w Biurko, Dokumenty lub Pobrane, zezwól też na dostęp w Ustawieniach systemowych → Prywatność i ochrona → Pliki i foldery.",
                "macOS: conceda permissão de escrita com  chmod -R u+w \"{0}\"  ou altere o proprietário:  sudo chown -R $(whoami) \"{0}\"  . Se a pasta estiver em Mesa, Documentos ou Transferências, permita também o acesso em Ajustes do Sistema → Privacidade e Segurança → Ficheiros e pastas.",
                "macOS: выдайте права командой  chmod -R u+w \"{0}\"  или смените владельца:  sudo chown -R $(whoami) \"{0}\"  . Если папка находится в «Рабочий стол», «Документы» или «Загрузки», разрешите доступ в Системных настройках → Конфиденциальность и безопасность → Доступ к файлам и папкам.",
                "macOS: ge skrivbehörighet med  chmod -R u+w \"{0}\"  eller byt ägare:  sudo chown -R $(whoami) \"{0}\"  . Ligger mappen i Skrivbord, Dokument eller Hämtade filer, tillåt även åtkomst i Systeminställningar → Integritet och säkerhet → Filer och mappar.",
                "macOS：使用  chmod -R u+w \"{0}\"  授予写入权限，或更改所有者：  sudo chown -R $(whoami) \"{0}\"  。如果该文件夹位于“桌面”“文稿”或“下载”中，还需在“系统设置 → 隐私与安全性 → 文件和文件夹”中允许访问。",
                "macOS: надайте права на запис командою  chmod -R u+w \"{0}\"  або змініть власника:  sudo chown -R $(whoami) \"{0}\"  . Якщо папка знаходиться в «Робочому столі», «Документах» або «Завантаженнях», також дозвольте доступ у Системних налаштуваннях → Конфіденційність і безпека → Файли і папки."),

            ["guard.advice.linux"] = L(
                "Linux: grant write access with  chmod -R u+w \"{0}\"  or change the owner:  sudo chown -R $USER \"{0}\"  . Also make sure the partition is not mounted read-only (check  mount | grep ro ).",
                "Linux: Erteilen Sie Schreibrechte mit  chmod -R u+w \"{0}\"  oder ändern Sie den Eigentümer:  sudo chown -R $USER \"{0}\"  . Stellen Sie außerdem sicher, dass die Partition nicht schreibgeschützt eingehängt ist (prüfen Sie  mount | grep ro ).",
                "Linux: conceda permisos de escritura con  chmod -R u+w \"{0}\"  o cambie el propietario:  sudo chown -R $USER \"{0}\"  . Asegúrese también de que la partición no esté montada como solo lectura (compruebe  mount | grep ro ).",
                "Linux : accordez les droits d'écriture avec  chmod -R u+w \"{0}\"  ou changez le propriétaire :  sudo chown -R $USER \"{0}\"  . Vérifiez aussi que la partition n'est pas montée en lecture seule (voir  mount | grep ro ).",
                "Linux: nadaj prawa zapisu poleceniem  chmod -R u+w \"{0}\"  lub zmień właściciela:  sudo chown -R $USER \"{0}\"  . Upewnij się też, że partycja nie jest zamontowana tylko do odczytu (sprawdź  mount | grep ro ).",
                "Linux: conceda permissão de escrita com  chmod -R u+w \"{0}\"  ou altere o proprietário:  sudo chown -R $USER \"{0}\"  . Confirme também que a partição não está montada como somente leitura (verifique  mount | grep ro ).",
                "Linux: выдайте права командой  chmod -R u+w \"{0}\"  или смените владельца:  sudo chown -R $USER \"{0}\"  . Убедитесь также, что раздел не смонтирован только для чтения (проверьте  mount | grep ro ).",
                "Linux: ge skrivbehörighet med  chmod -R u+w \"{0}\"  eller byt ägare:  sudo chown -R $USER \"{0}\"  . Kontrollera även att partitionen inte är monterad skrivskyddad (se  mount | grep ro ).",
                "Linux：使用  chmod -R u+w \"{0}\"  授予写入权限，或更改所有者：  sudo chown -R $USER \"{0}\"  。另请确认分区未以只读方式挂载（检查  mount | grep ro ）。",
                "Linux: надайте права на запис командою  chmod -R u+w \"{0}\"  або змініть власника:  sudo chown -R $USER \"{0}\"  . Також переконайтеся, що розділ не змонтовано лише для читання (перевірте  mount | grep ro )."),

            // ---------- UpdateSession ----------

            ["session.busy"] = L(
                "this folder is already being updated ({0})",
                "dieser Ordner wird bereits aktualisiert ({0})",
                "esta carpeta ya se está actualizando ({0})",
                "ce dossier est déjà en cours de mise à jour ({0})",
                "ten folder jest już aktualizowany ({0})",
                "esta pasta já está a ser atualizada ({0})",
                "обновление этой папки уже выполняется ({0})",
                "den här mappen uppdateras redan ({0})",
                "该文件夹正在被更新（{0}）",
                "ця папка вже оновлюється ({0})"),

            ["session.unknownProcess"] = L(
                "unknown process",
                "unbekannter Prozess",
                "proceso desconocido",
                "processus inconnu",
                "nieznany proces",
                "processo desconhecido",
                "неизвестный процесс",
                "okänd process",
                "未知进程",
                "невідомий процес"),

            ["session.owner"] = L(
                "PID {0}, {1}, started {2}",
                "PID {0}, {1}, gestartet {2}",
                "PID {0}, {1}, iniciado {2}",
                "PID {0}, {1}, démarré {2}",
                "PID {0}, {1}, uruchomiono {2}",
                "PID {0}, {1}, iniciado {2}",
                "PID {0}, {1}, запущено {2}",
                "PID {0}, {1}, startad {2}",
                "PID {0}，{1}，启动于 {2}",
                "PID {0}, {1}, запущено {2}"),

            ["session.staleLockFailed"] = L(
                "could not clear the stale lock {0}: {1}",
                "die verwaiste Sperre {0} konnte nicht entfernt werden: {1}",
                "no se pudo eliminar el bloqueo obsoleto {0}: {1}",
                "impossible de supprimer le verrou obsolète {0} : {1}",
                "nie udało się usunąć nieaktualnej blokady {0}: {1}",
                "não foi possível remover o bloqueio obsoleto {0}: {1}",
                "не удалось снять зависшую блокировку {0}: {1}",
                "kunde inte ta bort det inaktuella låset {0}: {1}",
                "无法清除失效的锁 {0}：{1}",
                "не вдалося прибрати застаріле блокування {0}: {1}"),

            ["session.lockCreateFailed"] = L(
                "could not create the lock file {0}: {1}",
                "die Sperrdatei {0} konnte nicht erstellt werden: {1}",
                "no se pudo crear el archivo de bloqueo {0}: {1}",
                "impossible de créer le fichier de verrou {0} : {1}",
                "nie udało się utworzyć pliku blokady {0}: {1}",
                "não foi possível criar o ficheiro de bloqueio {0}: {1}",
                "не удалось создать файл блокировки {0}: {1}",
                "kunde inte skapa låsfilen {0}: {1}",
                "无法创建锁文件 {0}：{1}",
                "не вдалося створити файл блокування {0}: {1}"),

            ["session.lockFailed"] = L(
                "could not acquire a lock on the client folder",
                "die Sperre für den Client-Ordner konnte nicht erlangt werden",
                "no se pudo bloquear la carpeta del cliente",
                "impossible de verrouiller le dossier client",
                "nie udało się zablokować folderu klienta",
                "não foi possível bloquear a pasta do cliente",
                "не удалось получить блокировку папки клиента",
                "kunde inte låsa klientmappen",
                "无法锁定客户端文件夹",
                "не вдалося отримати блокування папки клієнта"),

            // ---------- GameFolderInspector ----------

            ["backup.readmeFileName"] = L(
                "HOW TO RESTORE.txt",
                "WIEDERHERSTELLEN.txt",
                "COMO RESTAURAR.txt",
                "COMMENT RESTAURER.txt",
                "JAK PRZYWROCIC.txt",
                "COMO RESTAURAR.txt",
                "КАК ВЕРНУТЬ.txt",
                "SA HAR ATERSTALLER DU.txt",
                "如何还原.txt",
                "ЯК ВІДНОВИТИ.txt"),

            ["backup.readme.title"] = L(
                "These files were moved out of the game folder by the launcher.",
                "Diese Dateien wurden vom Launcher aus dem Spielordner verschoben.",
                "El launcher movió estos archivos fuera de la carpeta del juego.",
                "Ces fichiers ont été déplacés hors du dossier du jeu par le launcher.",
                "Te pliki zostały przeniesione z folderu gry przez launcher.",
                "Estes ficheiros foram movidos da pasta do jogo pelo launcher.",
                "Эти файлы перенесены лаунчером из папки игры.",
                "Dessa filer flyttades ut ur spelmappen av launchern.",
                "这些文件已被启动器从游戏文件夹中移出。",
                "Ці файли перенесено лаунчером з папки гри."),

            ["backup.readme.why"] = L(
                "Why: the launcher runs the game with its own set of mods and cannot do that\r\nwhile another BepInEx installation sits next to the game — foreign mods would load.",
                "Warum: Der Launcher startet das Spiel mit einem eigenen Mod-Satz und kann das nicht,\r\nsolange eine andere BepInEx-Installation neben dem Spiel liegt — es würden fremde Mods geladen.",
                "Motivo: el launcher ejecuta el juego con su propio conjunto de mods y no puede hacerlo\r\nmientras haya otra instalación de BepInEx junto al juego: se cargarían mods ajenos.",
                "Pourquoi : le launcher lance le jeu avec son propre ensemble de mods et ne peut pas le faire\r\ntant qu'une autre installation de BepInEx se trouve à côté du jeu — des mods étrangers seraient chargés.",
                "Dlaczego: launcher uruchamia grę z własnym zestawem modów i nie może tego zrobić,\r\ndopóki obok gry znajduje się inna instalacja BepInEx — załadowałyby się obce mody.",
                "Motivo: o launcher executa o jogo com o seu próprio conjunto de mods e não o consegue fazer\r\nenquanto houver outra instalação do BepInEx junto ao jogo — seriam carregados mods alheios.",
                "Зачем: лаунчер запускает игру со своим набором модов и не может этого сделать,\r\nпока рядом с игрой лежит другая установка BepInEx — загрузились бы чужие моды.",
                "Varför: launchern startar spelet med sin egen uppsättning mods och kan inte göra det\r\nså länge en annan BepInEx-installation ligger bredvid spelet — främmande mods skulle laddas.",
                "原因：启动器使用自己的模组集合运行游戏，而当游戏旁边存在另一个 BepInEx 安装时无法做到\r\n——那样会加载别的模组。",
                "Чому: лаунчер запускає гру зі своїм набором модів і не може зробити це,\r\nпоки поруч із грою лежить інша інсталяція BepInEx — завантажилися б чужі моди."),

            ["backup.readme.nothingDeleted"] = L(
                "Nothing was deleted. To restore everything, move the contents back:",
                "Es wurde nichts gelöscht. Um alles wiederherzustellen, verschieben Sie den Inhalt zurück:",
                "No se ha eliminado nada. Para restaurar todo, mueva el contenido de vuelta:",
                "Rien n'a été supprimé. Pour tout restaurer, remettez le contenu en place :",
                "Nic nie zostało usunięte. Aby przywrócić wszystko, przenieś zawartość z powrotem:",
                "Nada foi eliminado. Para restaurar tudo, mova o conteúdo de volta:",
                "Ничего не удалено. Чтобы вернуть всё как было, перенесите содержимое обратно:",
                "Inget har raderats. Flytta tillbaka innehållet för att återställa allt:",
                "没有删除任何内容。要恢复原状，请将内容移回：",
                "Нічого не було видалено. Щоб відновити все, перенесіть вміст назад:"),

            ["backup.readme.from"] = L(
                "  from : {0}", "  von  : {0}", "  desde: {0}", "  de   : {0}", "  z    : {0}",
                "  de   : {0}", "  из   : {0}", "  från : {0}", "  从   ：{0}", "  з    : {0}"),

            ["backup.readme.to"] = L(
                "  to   : {0}", "  nach : {0}", "  a    : {0}", "  vers : {0}", "  do   : {0}",
                "  para : {0}", "  в    : {0}", "  till : {0}", "  到   ：{0}", "  до   : {0}"),

            ["backup.readme.date"] = L(
                "Moved on: {0}",
                "Verschoben am: {0}",
                "Movido el: {0}",
                "Déplacé le : {0}",
                "Przeniesiono: {0}",
                "Movido em: {0}",
                "Дата переноса: {0}",
                "Flyttat: {0}",
                "移动时间：{0}",
                "Дата перенесення: {0}"),

            ["backup.moveFailed"] = L(
                "could not move to backup: {0}",
                "Verschieben in die Sicherung fehlgeschlagen: {0}",
                "no se pudo mover a la copia de seguridad: {0}",
                "impossible de déplacer vers la sauvegarde : {0}",
                "nie udało się przenieść do kopii zapasowej: {0}",
                "não foi possível mover para a cópia de segurança: {0}",
                "не удалось перенести в резерв: {0}",
                "kunde inte flytta till säkerhetskopian: {0}",
                "无法移动到备份：{0}",
                "не вдалося перенести в резервну копію: {0}"),

            // ---------- SteamLocator ----------

            ["steam.notInstalled"] = L(
                "no Steam installation found",
                "keine Steam-Installation gefunden",
                "no se encontró ninguna instalación de Steam",
                "aucune installation de Steam trouvée",
                "nie znaleziono instalacji Steam",
                "não foi encontrada nenhuma instalação do Steam",
                "не найдена установка Steam",
                "ingen Steam-installation hittades",
                "未找到 Steam 安装",
                "не знайдено встановлення Steam"),

            ["steam.librariesUnreadable"] = L(
                "could not read the list of Steam libraries: {0}",
                "die Liste der Steam-Bibliotheken konnte nicht gelesen werden: {0}",
                "no se pudo leer la lista de bibliotecas de Steam: {0}",
                "impossible de lire la liste des bibliothèques Steam : {0}",
                "nie udało się odczytać listy bibliotek Steam: {0}",
                "não foi possível ler a lista de bibliotecas do Steam: {0}",
                "не удалось прочитать список библиотек Steam: {0}",
                "kunde inte läsa listan över Steam-bibliotek: {0}",
                "无法读取 Steam 库列表：{0}",
                "не вдалося прочитати список бібліотек Steam: {0}"),

            ["steam.gameNotFound"] = L(
                "the game was not found in any Steam library",
                "das Spiel wurde in keiner Steam-Bibliothek gefunden",
                "no se encontró el juego en ninguna biblioteca de Steam",
                "le jeu n'a été trouvé dans aucune bibliothèque Steam",
                "nie znaleziono gry w żadnej bibliotece Steam",
                "o jogo não foi encontrado em nenhuma biblioteca do Steam",
                "игра не найдена ни в одной библиотеке Steam",
                "spelet hittades inte i något Steam-bibliotek",
                "在任何 Steam 库中都未找到该游戏",
                "гру не знайдено в жодній бібліотеці Steam"),

            // ---------- FileDownloader: status line ----------

            ["dl.initializing"] = L(
                "Initializing…", "Initialisierung…", "Inicializando…", "Initialisation…", "Inicjalizacja…",
                "A inicializar…", "Инициализация…", "Initierar…", "正在初始化…", "Ініціалізація…"),

            ["dl.preparing"] = L(
                "Preparing…", "Vorbereitung…", "Preparando…", "Préparation…", "Przygotowanie…",
                "A preparar…", "Подготовка…", "Förbereder…", "正在准备…", "Підготовка…"),

            ["dl.checkingFiles"] = L(
                "Checking files…", "Dateien werden geprüft…", "Comprobando archivos…", "Vérification des fichiers…",
                "Sprawdzanie plików…", "A verificar ficheiros…", "Проверка файлов…", "Kontrollerar filer…", "正在检查文件…",
                "Перевірка файлів…"),

            // ---------- FileDownloader: grouped step list ----------

            ["dl.group.check"] = L(
                "Checking files", "Dateien prüfen", "Comprobación de archivos", "Vérification des fichiers",
                "Sprawdzanie plików", "Verificação de ficheiros", "Проверка файлов", "Kontrollerar filer", "检查文件",
                "Перевірка файлів"),

            ["dl.group.download"] = L(
                "Downloading content", "Inhalte herunterladen", "Descarga de contenido", "Téléchargement du contenu",
                "Pobieranie zawartości", "Transferência de conteúdo", "Загрузка контента", "Laddar ner innehåll", "下载内容",
                "Завантаження вмісту"),

            ["dl.group.finalize"] = L(
                "Finishing up", "Abschluss", "Finalización", "Finalisation",
                "Kończenie", "Finalização", "Завершение", "Slutför", "正在完成",
                "Завершення"),

            ["dl.step.steamCheck"] = L(
                "Verifying Steam install", "Steam-Installation wird geprüft", "Comprobando la instalación de Steam",
                "Vérification de l'installation Steam", "Weryfikacja instalacji Steam", "A verificar a instalação Steam",
                "Проверка установки Steam", "Verifierar Steam-installationen", "正在验证 Steam 安装",
                "Перевірка встановлення Steam"),

            ["dl.step.clientCheck"] = L(
                "Checking game files", "Spieldateien werden geprüft", "Comprobando los archivos del juego",
                "Vérification des fichiers du jeu", "Sprawdzanie plików gry", "A verificar os ficheiros do jogo",
                "Проверка файлов игры", "Kontrollerar spelfiler", "正在检查游戏文件",
                "Перевірка файлів гри"),

            ["dl.step.optionalCheck"] = L(
                "Checking optional mods", "Optionale Mods werden geprüft", "Comprobando mods opcionales",
                "Vérification des mods optionnels", "Sprawdzanie opcjonalnych modów", "A verificar mods opcionais",
                "Проверка опциональных модов", "Kontrollerar valfria mods", "正在检查可选模组",
                "Перевірка опціональних модів"),

            ["dl.step.download"] = L(
                "Downloading files", "Dateien werden heruntergeladen", "Descargando archivos",
                "Téléchargement des fichiers", "Pobieranie plików", "A transferir ficheiros",
                "Загрузка файлов", "Laddar ner filer", "正在下载文件",
                "Завантаження файлів"),

            ["dl.step.finalize"] = L(
                "Finishing up", "Wird abgeschlossen", "Finalizando", "Finalisation", "Kończenie",
                "A finalizar", "Завершение", "Slutför", "正在完成",
                "Завершення"),

            ["dl.stepCounter"] = L(
                "Step {0} of {1}", "Schritt {0} von {1}", "Paso {0} de {1}", "Étape {0} sur {1}",
                "Krok {0} z {1}", "Passo {0} de {1}", "Шаг {0} из {1}", "Steg {0} av {1}", "第 {0} 步，共 {1} 步",
                "Крок {0} з {1}"),

            // Download detail line: {0} groups done, {1} groups total, {2} MB done, {3} MB total, {4} speed.
            ["dl.detail.headline"] = L(
                "Mods: {0} / {1}   ·   {2} / {3}   ·   {4}",
                "Mods: {0} / {1}   ·   {2} / {3}   ·   {4}",
                "Mods: {0} / {1}   ·   {2} / {3}   ·   {4}",
                "Mods : {0} / {1}   ·   {2} / {3}   ·   {4}",
                "Mody: {0} / {1}   ·   {2} / {3}   ·   {4}",
                "Mods: {0} / {1}   ·   {2} / {3}   ·   {4}",
                "Моды: {0} / {1}   ·   {2} / {3}   ·   {4}",
                "Mods: {0} / {1}   ·   {2} / {3}   ·   {4}",
                "模组：{0} / {1}   ·   {2} / {3}   ·   {4}",
                "Моди: {0} / {1}   ·   {2} / {3}   ·   {4}"),

            ["dl.detail.misc"] = L(
                "Other files", "Weitere Dateien", "Otros archivos", "Autres fichiers",
                "Inne pliki", "Outros ficheiros", "Прочие файлы", "Övriga filer", "其他文件",
                "Інші файли"),

            // "Stop", not "Cancel": clicking this does NOT roll back whatever already
            // installed/downloaded — it only stops the run where it stands (see FileDownloader's
            // cancellation handling). "Cancel" reads as "undo", which would misrepresent that.
            ["dl.cancelChecking"] = L(
                "Stop checking", "Prüfung stoppen", "Detener comprobación", "Arrêter la vérification",
                "Zatrzymaj sprawdzanie", "Parar verificação", "Остановить проверку", "Stoppa kontrollen", "停止检查",
                "Зупинити перевірку"),

            ["dl.cancelDownloading"] = L(
                "Stop downloading", "Download stoppen", "Detener descarga", "Arrêter le téléchargement",
                "Zatrzymaj pobieranie", "Parar transferência", "Остановить загрузку", "Stoppa nedladdningen", "停止下载",
                "Зупинити завантаження"),

            // Shown on the Stop button itself the instant it's clicked, before the cancellation
            // actually lands — the parallel file-check loops can take a moment to notice
            // CancellationPending, and without this the button just sits there looking like it
            // did nothing (or like the whole app hung) for that gap.
            ["dl.stopping"] = L(
                "Stopping…", "Wird gestoppt…", "Deteniendo…", "Arrêt en cours…",
                "Zatrzymywanie…", "A parar…", "Останавливаем…", "Stoppar…", "正在停止…",
                "Зупиняємо…"),

            // Shown on the Install tab in place of the live step label once a run has been
            // interrupted (worker.CancellationPending was true when it finished) — the step
            // list and progress bar stay exactly as they were, this just relabels the header
            // so "why did it stop moving" has an answer instead of the panel just vanishing.
            ["dl.interrupted"] = L(
                "Stopped", "Gestoppt", "Detenido", "Arrêté",
                "Zatrzymano", "Parado", "Остановлено", "Stoppad", "已停止",
                "Зупинено"),

            ["dl.interruptedPercent"] = L(
                "{0}% complete", "{0}% abgeschlossen", "{0}% completado", "{0}% terminé",
                "Ukończono {0}%", "{0}% concluído", "Выполнено {0}%", "{0}% klart", "已完成 {0}%",
                "Виконано {0}%"),

            ["dl.deletingExtra"] = L(
                "Removing extra files", "Überzählige Dateien werden entfernt", "Eliminando archivos sobrantes",
                "Suppression des fichiers superflus", "Usuwanie zbędnych plików", "A remover ficheiros supérfluos",
                "Удаление лишних файлов", "Tar bort överflödiga filer", "正在删除多余文件",
                "Видалення зайвих файлів"),

            ["dl.deletingOne"] = L(
                "Removing {0}", "Entferne {0}", "Eliminando {0}", "Suppression de {0}", "Usuwanie {0}",
                "A remover {0}", "Удаление {0}", "Tar bort {0}", "正在删除 {0}",
                "Видалення {0}"),

            ["dl.startingDownload"] = L(
                "Starting download…", "Download wird gestartet…", "Iniciando la descarga…", "Démarrage du téléchargement…",
                "Rozpoczynanie pobierania…", "A iniciar a transferência…", "Начало загрузки…", "Startar nedladdning…", "开始下载…",
                "Початок завантаження…"),

            ["dl.downloading"] = L(
                "Downloading…", "Wird heruntergeladen…", "Descargando…", "Téléchargement…", "Pobieranie…",
                "A transferir…", "Загрузка…", "Laddar ner…", "正在下载…",
                "Завантаження…"),

            ["dl.speed"] = L(
                "Download speed: {0}", "Downloadgeschwindigkeit: {0}", "Velocidad de descarga: {0}",
                "Vitesse de téléchargement : {0}", "Prędkość pobierania: {0}", "Velocidade de transferência: {0}",
                "Скорость загрузки: {0}", "Nedladdningshastighet: {0}", "下载速度：{0}",
                "Швидкість завантаження: {0}"),

            // Units separated by | — order: bytes, kilo, mega, giga.
            ["dl.sizeUnits"] = L(
                "B|KB|MB|GB", "B|KB|MB|GB", "B|KB|MB|GB", "o|Ko|Mo|Go", "B|KB|MB|GB",
                "B|KB|MB|GB", "Б|КБ|МБ|ГБ", "B|kB|MB|GB", "B|KB|MB|GB", "Б|КБ|МБ|ГБ"),

            ["dl.speedUnits"] = L(
                "B/s|KB/s|MB/s|GB/s", "B/s|KB/s|MB/s|GB/s", "B/s|KB/s|MB/s|GB/s", "o/s|Ko/s|Mo/s|Go/s",
                "B/s|KB/s|MB/s|GB/s", "B/s|KB/s|MB/s|GB/s", "Б/с|КБ/с|МБ/с|ГБ/с", "B/s|kB/s|MB/s|GB/s",
                "B/s|KB/s|MB/s|GB/s", "Б/с|КБ/с|МБ/с|ГБ/с"),

            // ---------- FileDownloader: messages ----------

            ["dl.title.error"] = L(
                "Error", "Fehler", "Error", "Erreur", "Błąd", "Erro", "Ошибка", "Fel", "错误", "Помилка"),

            ["dl.title.warning"] = L(
                "Warning", "Warnung", "Advertencia", "Avertissement", "Ostrzeżenie", "Aviso",
                "Предупреждение", "Varning", "警告", "Попередження"),

            ["dl.error.updateInfo"] = L(
                "Failed to download update.info: {0}",
                "update.info konnte nicht heruntergeladen werden: {0}",
                "No se pudo descargar update.info: {0}",
                "Échec du téléchargement de update.info : {0}",
                "Nie udało się pobrać update.info: {0}",
                "Não foi possível transferir update.info: {0}",
                "Ошибка загрузки update.info: {0}",
                "Kunde inte ladda ner update.info: {0}",
                "无法下载 update.info：{0}",
                "Не вдалося завантажити update.info: {0}"),

            ["dl.error.processing"] = L(
                "Error while processing files: {0}",
                "Fehler bei der Dateiverarbeitung: {0}",
                "Error al procesar los archivos: {0}",
                "Erreur lors du traitement des fichiers : {0}",
                "Błąd podczas przetwarzania plików: {0}",
                "Erro ao processar os ficheiros: {0}",
                "Ошибка при обработке файлов: {0}",
                "Fel vid bearbetning av filer: {0}",
                "处理文件时出错：{0}",
                "Помилка під час обробки файлів: {0}"),

            ["dl.error.downloadFile"] = L(
                "Error downloading file {0}: {1}",
                "Fehler beim Herunterladen der Datei {0}: {1}",
                "Error al descargar el archivo {0}: {1}",
                "Erreur lors du téléchargement du fichier {0} : {1}",
                "Błąd pobierania pliku {0}: {1}",
                "Erro ao transferir o ficheiro {0}: {1}",
                "Ошибка при скачивании файла {0}: {1}",
                "Fel vid nedladdning av filen {0}: {1}",
                "下载文件 {0} 时出错：{1}",
                "Помилка завантаження файлу {0}: {1}"),

            ["dl.warn.optionalUnreadable"] = L(
                "Could not read the list of optional mods: {0}\n\nThe update will continue, but optional mods may be removed as extraneous.",
                "Die Liste optionaler Mods konnte nicht gelesen werden: {0}\n\nDie Aktualisierung wird fortgesetzt, optionale Mods könnten jedoch als überzählig entfernt werden.",
                "No se pudo leer la lista de mods opcionales: {0}\n\nLa actualización continuará, pero los mods opcionales podrían eliminarse por considerarse sobrantes.",
                "Impossible de lire la liste des mods optionnels : {0}\n\nLa mise à jour va continuer, mais les mods optionnels risquent d'être supprimés comme superflus.",
                "Nie udało się odczytać listy modów opcjonalnych: {0}\n\nAktualizacja będzie kontynuowana, ale mody opcjonalne mogą zostać usunięte jako zbędne.",
                "Não foi possível ler a lista de mods opcionais: {0}\n\nA atualização vai continuar, mas os mods opcionais podem ser removidos por serem considerados supérfluos.",
                "Не удалось прочитать список необязательных модов: {0}\n\nОбновление продолжится, но необязательные моды могут быть удалены как лишние.",
                "Kunde inte läsa listan över valfria mods: {0}\n\nUppdateringen fortsätter, men valfria mods kan tas bort som överflödiga.",
                "无法读取可选模组列表：{0}\n\n更新将继续，但可选模组可能会被当作多余文件删除。",
                "Не вдалося прочитати список опціональних модів: {0}\n\nОновлення продовжиться, але опціональні моди можуть бути видалені як зайві."),

            ["dl.warn.suspiciousRedownload"] = L(
                "{0} file(s) had to be re-downloaded even though they were fine on the last check. This can happen if antivirus software or missing write permission is affecting the game folder. Check launcher_log.txt for the file list.",
                "{0} Datei(en) mussten erneut heruntergeladen werden, obwohl sie bei der letzten Prüfung in Ordnung waren. Das kann passieren, wenn ein Virenschutz oder fehlende Schreibrechte den Spieleordner betreffen. Die Dateiliste steht in launcher_log.txt.",
                "{0} archivo(s) tuvieron que descargarse de nuevo aunque estaban correctos en la última comprobación. Esto puede deberse a un antivirus o a falta de permisos de escritura en la carpeta del juego. La lista de archivos está en launcher_log.txt.",
                "{0} fichier(s) ont dû être retéléchargés alors qu'ils étaient corrects lors de la dernière vérification. Cela peut venir d'un antivirus ou d'un manque de droits d'écriture sur le dossier du jeu. La liste des fichiers est dans launcher_log.txt.",
                "{0} plik(i) trzeba było pobrać ponownie, mimo że przy poprzednim sprawdzeniu były w porządku. Może to powodować antywirus albo brak uprawnień do zapisu w folderze gry. Lista plików jest w launcher_log.txt.",
                "{0} arquivo(s) precisaram ser baixados novamente mesmo estando corretos na última verificação. Isso pode acontecer por causa de um antivírus ou falta de permissão de escrita na pasta do jogo. A lista de arquivos está em launcher_log.txt.",
                "{0} файл(ов) пришлось скачать заново, хотя при прошлой проверке они были в порядке. Так бывает, если антивирус или отсутствие прав на запись мешают папке игры. Список файлов — в launcher_log.txt.",
                "{0} fil(er) behövde laddas ner igen trots att de var korrekta vid senaste kontrollen. Det kan bero på ett antivirusprogram eller saknad skrivbehörighet i spelmappen. Fillistan finns i launcher_log.txt.",
                "有 {0} 个文件需要重新下载，尽管上次检查时它们是正常的。这可能是杀毒软件或游戏文件夹缺少写入权限导致的。文件列表见 launcher_log.txt。",
                "{0} файл(ів) довелося завантажити повторно, хоча під час минулої перевірки вони були в порядку. Так буває, якщо антивірус або відсутність прав на запис заважають папці гри. Список файлів — у launcher_log.txt."),

            ["dl.warn.deleteFailedTitle"] = L(
                "Could not remove files",
                "Dateien konnten nicht entfernt werden",
                "No se pudieron eliminar archivos",
                "Impossible de supprimer des fichiers",
                "Nie udało się usunąć plików",
                "Não foi possível remover ficheiros",
                "Не удалось удалить файлы",
                "Kunde inte ta bort filer",
                "无法删除文件",
                "Не вдалося видалити файли"),

            ["dl.warn.deleteFailed"] = L(
                "Could not remove {0} extra file(s). The modpack may not work correctly.\n\n{1}\n\nThe usual causes are a running game, antivirus, or missing folder permissions. Close the game and run the update again; details are in launcher_log.txt.",
                "{0} überzählige Datei(en) konnten nicht entfernt werden. Das Modpack funktioniert möglicherweise nicht korrekt.\n\n{1}\n\nMeist liegt es an einem laufenden Spiel, an Antivirensoftware oder an fehlenden Ordnerrechten. Schließen Sie das Spiel und starten Sie die Aktualisierung erneut; Details stehen in launcher_log.txt.",
                "No se pudieron eliminar {0} archivo(s) sobrante(s). El modpack podría no funcionar correctamente.\n\n{1}\n\nLo habitual es que el juego esté en ejecución, que lo bloquee el antivirus o que falten permisos sobre la carpeta. Cierre el juego y repita la actualización; los detalles están en launcher_log.txt.",
                "Impossible de supprimer {0} fichier(s) superflu(s). Le pack de mods risque de ne pas fonctionner correctement.\n\n{1}\n\nLes causes habituelles : le jeu est lancé, l'antivirus bloque, ou les droits sur le dossier manquent. Fermez le jeu et relancez la mise à jour ; les détails sont dans launcher_log.txt.",
                "Nie udało się usunąć {0} zbędnych plików. Modpack może działać nieprawidłowo.\n\n{1}\n\nNajczęstsze przyczyny to uruchomiona gra, antywirus lub brak uprawnień do folderu. Zamknij grę i powtórz aktualizację; szczegóły w launcher_log.txt.",
                "Não foi possível remover {0} ficheiro(s) supérfluo(s). O modpack pode não funcionar corretamente.\n\n{1}\n\nAs causas habituais são o jogo estar em execução, o antivírus ou falta de permissões na pasta. Feche o jogo e repita a atualização; os detalhes estão em launcher_log.txt.",
                "Не удалось удалить лишних файлов: {0}. Сборка может работать неправильно.\n\n{1}\n\nЧаще всего причина — запущенная игра, антивирус или нехватка прав на папку. Закройте игру и повторите обновление; подробности в launcher_log.txt.",
                "Kunde inte ta bort {0} överflödig(a) fil(er). Modpacket kanske inte fungerar korrekt.\n\n{1}\n\nVanliga orsaker är att spelet körs, antivirus eller saknade mapprättigheter. Stäng spelet och kör uppdateringen igen; detaljer finns i launcher_log.txt.",
                "无法删除 {0} 个多余文件。整合包可能无法正常运行。\n\n{1}\n\n常见原因是游戏正在运行、杀毒软件拦截或文件夹权限不足。请关闭游戏后重新更新；详情见 launcher_log.txt。",
                "Не вдалося видалити зайві файли: {0}. Збірка може працювати неправильно.\n\n{1}\n\nЗазвичай причина — запущена гра, антивірус або нестача прав на папку. Закрийте гру і повторіть оновлення; подробиці в launcher_log.txt."),

            ["dl.retrying"] = L(
                "Retrying {0} ({1} of {2})…",
                "Erneuter Versuch für {0} ({1} von {2})…",
                "Reintentando {0} ({1} de {2})…",
                "Nouvelle tentative pour {0} ({1} sur {2})…",
                "Ponowna próba {0} ({1} z {2})…",
                "A tentar novamente {0} ({1} de {2})…",
                "Повтор {0} ({1} из {2})…",
                "Försöker igen med {0} ({1} av {2})…",
                "正在重试 {0}（第 {1}/{2} 次）…",
                "Повтор {0} ({1} з {2})…"),

            ["dl.error.stalled"] = L(
                "Download of {0} stalled: no data for {1:0} seconds. Check your connection and try again.",
                "Der Download von {0} stockt: seit {1:0} Sekunden keine Daten. Prüfen Sie die Verbindung und versuchen Sie es erneut.",
                "La descarga de {0} se detuvo: sin datos durante {1:0} segundos. Compruebe la conexión e inténtelo de nuevo.",
                "Le téléchargement de {0} est bloqué : aucune donnée depuis {1:0} secondes. Vérifiez la connexion et réessayez.",
                "Pobieranie {0} utknęło: brak danych od {1:0} sekund. Sprawdź połączenie i spróbuj ponownie.",
                "A transferência de {0} parou: sem dados há {1:0} segundos. Verifique a ligação e tente novamente.",
                "Загрузка {0} зависла: данные не идут {1:0} секунд. Проверьте соединение и повторите.",
                "Nedladdningen av {0} har fastnat: inga data på {1:0} sekunder. Kontrollera anslutningen och försök igen.",
                "{0} 的下载已停滞：{1:0} 秒没有数据。请检查网络后重试。",
                "Завантаження {0} зупинилося: немає даних протягом {1:0} секунд. Перевірте з'єднання і спробуйте ще раз."),

            ["dl.andMore"] = L(
                "… and {0} more", "… und {0} weitere", "… y {0} más", "… et {0} de plus", "… i jeszcze {0}",
                "… e mais {0}", "… и ещё {0}", "… och {0} till", "…还有 {0} 项", "… і ще {0}"),

            // ---------- Console updater ----------

            ["cli.noMirror"] = L(
                "Could not reach any mirror.", "Kein Spiegelserver erreichbar.", "No se pudo contactar con ningún espejo.",
                "Impossible de joindre un miroir.", "Nie udało się połączyć z żadnym serwerem lustrzanym.",
                "Não foi possível contactar nenhum espelho.", "Не удалось связаться ни с одним зеркалом.",
                "Kunde inte nå någon spegel.", "无法连接到任何镜像。", "Не вдалося зв'язатися з жодним дзеркалом."),

            ["cli.mirror"] = L(
                "Mirror: {0}", "Spiegel: {0}", "Espejo: {0}", "Miroir : {0}", "Serwer lustrzany: {0}",
                "Espelho: {0}", "Зеркало: {0}", "Spegel: {0}", "镜像：{0}", "Дзеркало: {0}"),

            ["cli.noServers"] = L(
                "The server list is empty.", "Die Serverliste ist leer.", "La lista de servidores está vacía.",
                "La liste des serveurs est vide.", "Lista serwerów jest pusta.", "A lista de servidores está vazia.",
                "Список серверов пуст.", "Serverlistan är tom.", "服务器列表为空。", "Список серверів порожній."),

            ["cli.hidden"] = L(
                "(hidden)", "(versteckt)", "(oculto)", "(masqué)", "(ukryty)",
                "(oculto)", "(скрытый)", "(dold)", "（隐藏）", "(схований)"),

            ["cli.server"] = L(
                "Server: {0}", "Server: {0}", "Servidor: {0}", "Serveur : {0}", "Serwer: {0}",
                "Servidor: {0}", "Сервер: {0}", "Server: {0}", "服务器：{0}", "Сервер: {0}"),

            ["cli.clientFolder"] = L(
                "Client: {0}", "Client: {0}", "Cliente: {0}", "Client : {0}", "Klient: {0}",
                "Cliente: {0}", "Клиент: {0}", "Klient: {0}", "客户端：{0}", "Клієнт: {0}"),

            ["cli.modeFull"] = L(
                "Mode: full check", "Modus: vollständige Prüfung", "Modo: comprobación completa",
                "Mode : vérification complète", "Tryb: pełne sprawdzenie", "Modo: verificação completa",
                "Режим: полная проверка", "Läge: fullständig kontroll", "模式：完整校验",
                "Режим: повна перевірка"),

            ["cli.modeNormal"] = L(
                "Mode: normal check", "Modus: normale Prüfung", "Modo: comprobación normal",
                "Mode : vérification normale", "Tryb: zwykłe sprawdzenie", "Modo: verificação normal",
                "Режим: обычная проверка", "Läge: normal kontroll", "模式：常规校验",
                "Режим: звичайна перевірка"),

            ["cli.interruptedFullCheck"] = L(
                "The previous update did not finish — running a full check.",
                "Die vorherige Aktualisierung wurde nicht abgeschlossen — es wird vollständig geprüft.",
                "La actualización anterior no terminó: se hará una comprobación completa.",
                "La mise à jour précédente ne s'est pas terminée — vérification complète en cours.",
                "Poprzednia aktualizacja nie została ukończona — wykonywane jest pełne sprawdzenie.",
                "A atualização anterior não terminou — será feita uma verificação completa.",
                "Прошлое обновление не завершилось — выполняется полная проверка.",
                "Föregående uppdatering slutfördes inte — en fullständig kontroll körs.",
                "上次更新未完成——正在执行完整校验。",
                "Попереднє оновлення не завершилося — виконується повна перевірка."),

            ["cli.cancelling"] = L(
                "Cancelling…", "Wird abgebrochen…", "Cancelando…", "Annulation…", "Anulowanie…",
                "A cancelar…", "Отмена…", "Avbryter…", "正在取消…", "Скасування…"),

            ["cli.refused"] = L(
                "Refused: {0}", "Abgelehnt: {0}", "Rechazado: {0}", "Refusé : {0}", "Odmowa: {0}",
                "Recusado: {0}", "Отказ: {0}", "Nekad: {0}", "已拒绝：{0}", "Відмова: {0}"),

            ["cli.howToFix"] = L(
                "How to fix:", "So beheben Sie das:", "Cómo solucionarlo:", "Comment corriger :",
                "Jak to naprawić:", "Como resolver:", "Как исправить:", "Så här åtgärdar du:", "如何解决：",
                "Як виправити:"),

            ["cli.deletesWarning"] = L(
                "The update removes everything that is not in the manifest,\nso it will not run in an unrelated folder. There is no override.\nSpecify an empty folder or an existing Valheim client.",
                "Die Aktualisierung entfernt alles, was nicht im Manifest steht,\ndaher läuft sie nicht in einem fremden Ordner. Es gibt keine Umgehung.\nGeben Sie einen leeren Ordner oder einen vorhandenen Valheim-Client an.",
                "La actualización elimina todo lo que no esté en el manifiesto,\npor eso no se ejecuta en una carpeta ajena. No hay forma de omitirlo.\nIndique una carpeta vacía o un cliente de Valheim existente.",
                "La mise à jour supprime tout ce qui n'est pas dans le manifeste,\nelle ne s'exécute donc pas dans un dossier étranger. Aucun contournement.\nIndiquez un dossier vide ou un client Valheim existant.",
                "Aktualizacja usuwa wszystko, czego nie ma w manifeście,\ndlatego nie uruchomi się w obcym folderze. Nie da się tego pominąć.\nWskaż pusty folder lub istniejącego klienta Valheim.",
                "A atualização remove tudo o que não está no manifesto,\npor isso não é executada numa pasta alheia. Não existe forma de contornar.\nIndique uma pasta vazia ou um cliente do Valheim existente.",
                "Обновление удаляет из папки всё, чего нет в манифесте,\nпоэтому в постороннюю папку оно не запускается. Обхода нет.\nУкажите пустую папку либо существующий клиент Valheim.",
                "Uppdateringen tar bort allt som inte finns i manifestet,\nså den körs inte i en främmande mapp. Det går inte att kringgå.\nAnge en tom mapp eller en befintlig Valheim-klient.",
                "更新会删除清单之外的所有内容，\n因此不会在无关文件夹中运行，且无法绕过。\n请指定空文件夹或已有的 Valheim 客户端。",
                "Оновлення видаляє з папки все, чого немає в маніфесті,\nтому в сторонню папку воно не запускається. Обходу немає.\nВкажіть порожню папку або наявний клієнт Valheim."),

            ["cli.waitOrClose"] = L(
                "Wait for it to finish or close the other launcher.",
                "Warten Sie, bis sie fertig ist, oder schließen Sie den anderen Launcher.",
                "Espere a que termine o cierre el otro launcher.",
                "Attendez la fin ou fermez l'autre launcher.",
                "Poczekaj na zakończenie lub zamknij drugi launcher.",
                "Aguarde que termine ou feche o outro launcher.",
                "Дождитесь окончания или закройте другой лаунчер.",
                "Vänta tills den är klar eller stäng den andra launchern.",
                "请等待其完成或关闭另一个启动器。",
                "Дочекайтеся завершення або закрийте інший лаунчер."),

            ["cli.unexpectedError"] = L(
                "Unexpected error: {0}", "Unerwarteter Fehler: {0}", "Error inesperado: {0}",
                "Erreur inattendue : {0}", "Nieoczekiwany błąd: {0}", "Erro inesperado: {0}",
                "Непредвиденная ошибка: {0}", "Oväntat fel: {0}", "意外错误：{0}", "Неочікувана помилка: {0}"),

            ["cli.unknownOption"] = L(
                "Unknown option: {0}", "Unbekannte Option: {0}", "Opción desconocida: {0}",
                "Option inconnue : {0}", "Nieznana opcja: {0}", "Opção desconhecida: {0}",
                "Неизвестный параметр: {0}", "Okänd flagga: {0}", "未知参数：{0}", "Невідомий параметр: {0}"),

            ["cli.parallelRange"] = L(
                "--parallel expects a number from 1 to 16.",
                "--parallel erwartet eine Zahl von 1 bis 16.",
                "--parallel espera un número del 1 al 16.",
                "--parallel attend un nombre de 1 à 16.",
                "--parallel oczekuje liczby od 1 do 16.",
                "--parallel espera um número de 1 a 16.",
                "--parallel ожидает число от 1 до 16.",
                "--parallel förväntar sig ett tal mellan 1 och 16.",
                "--parallel 需要 1 到 16 之间的数字。",
                "--parallel очікує число від 1 до 16."),

            ["cli.optionNeedsValue"] = L(
                "{0} requires a value.", "{0} erfordert einen Wert.", "{0} requiere un valor.",
                "{0} nécessite une valeur.", "{0} wymaga wartości.", "{0} requer um valor.",
                "{0} требует значение.", "{0} kräver ett värde.", "{0} 需要一个值。", "{0} потребує значення."),

            ["cli.folderTwice"] = L(
                "The client folder path is given twice: '{0}' and '{1}'.",
                "Der Pfad zum Client-Ordner wurde zweimal angegeben: '{0}' und '{1}'.",
                "La ruta de la carpeta del cliente se indicó dos veces: '{0}' y '{1}'.",
                "Le chemin du dossier client est indiqué deux fois : '{0}' et '{1}'.",
                "Ścieżka folderu klienta podana dwukrotnie: '{0}' i '{1}'.",
                "O caminho da pasta do cliente foi indicado duas vezes: '{0}' e '{1}'.",
                "Путь к папке клиента указан дважды: '{0}' и '{1}'.",
                "Sökvägen till klientmappen anges två gånger: '{0}' och '{1}'.",
                "客户端文件夹路径重复指定：'{0}' 和 '{1}'。",
                "Шлях до папки клієнта вказано двічі: '{0}' і '{1}'."),

            ["cli.serverNotFound"] = L(
                "Server '{0}' not found. Available:", "Server '{0}' nicht gefunden. Verfügbar:",
                "No se encontró el servidor '{0}'. Disponibles:", "Serveur '{0}' introuvable. Disponibles :",
                "Nie znaleziono serwera '{0}'. Dostępne:", "Servidor '{0}' não encontrado. Disponíveis:",
                "Сервер '{0}' не найден. Доступные:", "Servern '{0}' hittades inte. Tillgängliga:",
                "未找到服务器 '{0}'。可用：", "Сервер '{0}' не знайдено. Доступні:"),

            ["cli.serverNotSpecified"] = L(
                "No server specified. Use --server <name>. Available:",
                "Kein Server angegeben. Verwenden Sie --server <Name>. Verfügbar:",
                "No se indicó ningún servidor. Use --server <nombre>. Disponibles:",
                "Aucun serveur indiqué. Utilisez --server <nom>. Disponibles :",
                "Nie podano serwera. Użyj --server <nazwa>. Dostępne:",
                "Nenhum servidor indicado. Use --server <nome>. Disponíveis:",
                "Не указан сервер. Задайте --server <имя>. Доступные:",
                "Ingen server angiven. Använd --server <namn>. Tillgängliga:",
                "未指定服务器。请使用 --server <名称>。可用：",
                "Сервер не вказано. Використайте --server <ім'я>. Доступні:"),

            ["cli.done.ready"] = L(
                "Done. The client is up to date.", "Fertig. Der Client ist aktuell.",
                "Listo. El cliente está actualizado.", "Terminé. Le client est à jour.",
                "Gotowe. Klient jest aktualny.", "Concluído. O cliente está atualizado.",
                "Готово. Клиент актуален.", "Klart. Klienten är uppdaterad.", "完成。客户端已是最新。",
                "Готово. Клієнт актуальний."),

            // ---------- InjectorLauncher ----------

            ["injector.noGameFolder"] = L(
                "the game folder was not found", "der Spielordner wurde nicht gefunden",
                "no se encontró la carpeta del juego", "le dossier du jeu est introuvable",
                "nie znaleziono folderu gry", "a pasta do jogo não foi encontrada",
                "папка игры не найдена", "spelmappen hittades inte", "未找到游戏文件夹",
                "папку гри не знайдено"),

            ["injector.noExecutable"] = L(
                "no game executable found in the folder (expected {0})",
                "keine ausführbare Spieldatei im Ordner gefunden (erwartet {0})",
                "no se encontró el ejecutable del juego en la carpeta (se esperaba {0})",
                "aucun exécutable du jeu dans le dossier (attendu {0})",
                "nie znaleziono pliku wykonywalnego gry w folderze (oczekiwano {0})",
                "não foi encontrado o executável do jogo na pasta (esperado {0})",
                "в папке не найден исполняемый файл игры (ожидался {0})",
                "ingen körbar spelfil hittades i mappen (förväntade {0})",
                "文件夹中未找到游戏可执行文件（期望 {0}）",
                "у папці не знайдено виконуваний файл гри (очікувався {0})"),

            ["injector.noPreloader"] = L(
                "BepInEx preloader not found: {0}",
                "BepInEx-Preloader nicht gefunden: {0}",
                "No se encontró el preloader de BepInEx: {0}",
                "Préchargeur BepInEx introuvable : {0}",
                "Nie znaleziono preloadera BepInEx: {0}",
                "Preloader do BepInEx não encontrado: {0}",
                "не найден preloader BepInEx: {0}",
                "BepInEx-preloader hittades inte: {0}",
                "未找到 BepInEx 预加载器：{0}",
                "не знайдено BepInEx preloader: {0}"),

            ["injector.noDoorstopLibrary"] = L(
                "Doorstop library not found: {0}",
                "Doorstop-Bibliothek nicht gefunden: {0}",
                "No se encontró la biblioteca de Doorstop: {0}",
                "Bibliothèque Doorstop introuvable : {0}",
                "Nie znaleziono biblioteki Doorstop: {0}",
                "Biblioteca do Doorstop não encontrada: {0}",
                "не найдена библиотека Doorstop: {0}",
                "Doorstop-biblioteket hittades inte: {0}",
                "未找到 Doorstop 库：{0}",
                "не знайдено бібліотеку Doorstop: {0}"),

            ["injector.noProxy"] = L(
                "the injector library is missing from the profile: {0}",
                "die Injektor-Bibliothek fehlt im Profil: {0}",
                "falta la biblioteca del inyector en el perfil: {0}",
                "la bibliothèque d'injection est absente du profil : {0}",
                "brak biblioteki wstrzykiwacza w profilu: {0}",
                "falta a biblioteca do injetor no perfil: {0}",
                "в профиле нет библиотеки инжектора: {0}",
                "injektorbiblioteket saknas i profilen: {0}",
                "配置文件中缺少注入库：{0}",
                "у профілі відсутня бібліотека інжектора: {0}"),

            ["injector.prepareFailed"] = L(
                "could not prepare the game folder: {0}",
                "der Spielordner konnte nicht vorbereitet werden: {0}",
                "no se pudo preparar la carpeta del juego: {0}",
                "impossible de préparer le dossier du jeu : {0}",
                "nie udało się przygotować folderu gry: {0}",
                "não foi possível preparar a pasta do jogo: {0}",
                "не удалось подготовить папку игры: {0}",
                "kunde inte förbereda spelmappen: {0}",
                "无法准备游戏文件夹：{0}",
                "не вдалося підготувати папку гри: {0}"),

            // The game folder needed elevation to prepare (commonly: Steam is under Program
            // Files) and the player declined the UAC prompt — not an error on our side, just "no".
            ["injector.elevationDeclined"] = L(
                "Administrator permission is needed to set up the Steam game folder, and was declined.",
                "Für die Einrichtung des Steam-Spielordners sind Administratorrechte nötig — die Anfrage wurde abgelehnt.",
                "Se necesitan permisos de administrador para preparar la carpeta del juego de Steam, y se denegaron.",
                "Des droits administrateur sont nécessaires pour préparer le dossier du jeu Steam, et ont été refusés.",
                "Do przygotowania folderu gry Steam potrzebne są uprawnienia administratora — odmówiono ich.",
                "São necessárias permissões de administrador para preparar a pasta do jogo Steam, e foram recusadas.",
                "Для подготовки папки игры Steam нужны права администратора — в них было отказано.",
                "Administratörsbehörighet krävs för att förbereda Steam-spelmappen, och nekades.",
                "准备 Steam 游戏文件夹需要管理员权限，但请求被拒绝。",
                "Для налаштування ігрової папки Steam потрібні права адміністратора, і в них було відмовлено."),

            ["injector.elevationFailed"] = L(
                "could not prepare the game folder even with administrator permission: {0}",
                "der Spielordner konnte auch mit Administratorrechten nicht vorbereitet werden: {0}",
                "no se pudo preparar la carpeta del juego ni con permisos de administrador: {0}",
                "impossible de préparer le dossier du jeu même avec les droits administrateur : {0}",
                "nie udało się przygotować folderu gry nawet z uprawnieniami administratora: {0}",
                "não foi possível preparar a pasta do jogo mesmo com permissões de administrador: {0}",
                "не удалось подготовить папку игры даже с правами администратора: {0}",
                "kunde inte förbereda spelmappen även med administratörsbehörighet: {0}",
                "即使拥有管理员权限，也无法准备游戏文件夹：{0}",
                "не вдалося підготувати папку гри навіть із правами адміністратора: {0}"),

            ["cli.done.notReady"] = L(
                "Update finished, but the client is not ready to launch.",
                "Aktualisierung abgeschlossen, aber der Client ist nicht startbereit.",
                "La actualización terminó, pero el cliente no está listo para ejecutarse.",
                "Mise à jour terminée, mais le client n'est pas prêt à être lancé.",
                "Aktualizacja zakończona, ale klient nie jest gotowy do uruchomienia.",
                "Atualização concluída, mas o cliente não está pronto para arrancar.",
                "Обновление завершено, но клиент не готов к запуску.",
                "Uppdateringen är klar, men klienten är inte redo att starta.",
                "更新已完成，但客户端尚未就绪。",
                "Оновлення завершено, але клієнт не готовий до запуску."),

            // ---------- GUI: MainWindow ----------

            ["gui.allServersDown"] = L(
                "All servers are unavailable. The launcher will close.",
                "Alle Server sind nicht erreichbar. Der Launcher wird geschlossen.",
                "Todos los servidores no están disponibles. El launcher se cerrará.",
                "Tous les serveurs sont indisponibles. Le launcher va se fermer.",
                "Wszystkie serwery są niedostępne. Launcher zostanie zamknięty.",
                "Todos os servidores estão indisponíveis. O launcher será fechado.",
                "Все серверы недоступны. Лаунчер будет закрыт.",
                "Alla servrar är otillgängliga. Launchern stängs.",
                "所有服务器均不可用。启动器将关闭。",
                "Усі сервери недоступні. Лаунчер закриється."),

            ["gui.configWriteFailed"] = L(
                "Failed to write config.ini: {0}\nPath: {1}",
                "config.ini konnte nicht geschrieben werden: {0}\nPfad: {1}",
                "No se pudo escribir config.ini: {0}\nRuta: {1}",
                "Impossible d'écrire config.ini : {0}\nChemin : {1}",
                "Nie udało się zapisać config.ini: {0}\nŚcieżka: {1}",
                "Não foi possível gravar config.ini: {0}\nCaminho: {1}",
                "Не удалось записать config.ini: {0}\nПуть: {1}",
                "Det gick inte att skriva config.ini: {0}\nSökväg: {1}",
                "无法写入 config.ini：{0}\n路径：{1}",
                "Не вдалося записати config.ini: {0}\nШлях: {1}"),

            ["gui.noServerSelected"] = L(
                "No server selected. The launcher will close.",
                "Kein Server ausgewählt. Der Launcher wird geschlossen.",
                "No se seleccionó ningún servidor. El launcher se cerrará.",
                "Aucun serveur sélectionné. Le launcher va se fermer.",
                "Nie wybrano serwera. Launcher zostanie zamknięty.",
                "Nenhum servidor selecionado. O launcher será fechado.",
                "Сервер не выбран. Лаунчер будет закрыт.",
                "Ingen server vald. Launchern stängs.",
                "未选择服务器。启动器将关闭。",
                "Сервер не вибрано. Лаунчер закриється."),

            ["gui.noServersToChoose"] = L(
                "No servers available to choose from.",
                "Keine Server zur Auswahl verfügbar.",
                "No hay servidores disponibles para elegir.",
                "Aucun serveur disponible pour la sélection.",
                "Brak serwerów do wyboru.",
                "Não há servidores disponíveis para escolher.",
                "Нет доступных серверов для выбора.",
                "Inga servrar att välja mellan.",
                "没有可供选择的服务器。",
                "Немає серверів для вибору."),

            ["gui.initError"] = L(
                "Initialization error: {0}",
                "Initialisierungsfehler: {0}",
                "Error de inicialización: {0}",
                "Erreur d'initialisation : {0}",
                "Błąd inicjalizacji: {0}",
                "Erro de inicialização: {0}",
                "Ошибка при инициализации: {0}",
                "Initieringsfel: {0}",
                "初始化错误：{0}",
                "Помилка інціалізації: {0}"),

            ["gui.criticalStartupError"] = L(
                "Critical error on startup: {0}",
                "Kritischer Fehler beim Start: {0}",
                "Error crítico al iniciar: {0}",
                "Erreur critique au démarrage : {0}",
                "Krytyczny błąd podczas uruchamiania: {0}",
                "Erro crítico na inicialização: {0}",
                "Критическая ошибка при запуске: {0}",
                "Kritiskt fel vid start: {0}",
                "启动时发生严重错误：{0}",
                "Критична помилка під час запуску: {0}"),

            ["gui.noServersAvailable"] = L(
                "No servers available.",
                "Keine Server verfügbar.",
                "No hay servidores disponibles.",
                "Aucun serveur disponible.",
                "Brak dostępnych serwerów.",
                "Nenhum servidor disponível.",
                "Нет доступных серверов.",
                "Inga servrar tillgängliga.",
                "没有可用的服务器。",
                "Немає доступних серверів."),

            ["gui.serverUnavailableAllMirrors"] = L(
                "Server {0} is unavailable on all mirrors.",
                "Server {0} ist auf allen Mirrors nicht erreichbar.",
                "El servidor {0} no está disponible en ningún mirror.",
                "Le serveur {0} est indisponible sur tous les miroirs.",
                "Serwer {0} jest niedostępny na wszystkich mirrorach.",
                "O servidor {0} está indisponível em todos os mirrors.",
                "Сервер {0} недоступен на всех зеркалах.",
                "Servern {0} är otillgänglig på alla speglar.",
                "服务器 {0} 在所有镜像上均不可用。",
                "Сервер {0} недоступний на всіх дзеркалах."),

            ["gui.serverListLoadFailed"] = L(
                "Failed to load the server list: {0}",
                "Die Serverliste konnte nicht geladen werden: {0}",
                "No se pudo cargar la lista de servidores: {0}",
                "Impossible de charger la liste des serveurs : {0}",
                "Nie udało się wczytać listy serwerów: {0}",
                "Não foi possível carregar a lista de servidores: {0}",
                "Не удалось загрузить список серверов: {0}",
                "Det gick inte att läsa in serverlistan: {0}",
                "无法加载服务器列表：{0}",
                "Не вдалося завантажити список серверів: {0}"),

            ["gui.launcherDownloadFailed"] = L(
                "Failed to download the new launcher.",
                "Der neue Launcher konnte nicht heruntergeladen werden.",
                "No se pudo descargar el nuevo launcher.",
                "Impossible de télécharger le nouveau launcher.",
                "Nie udało się pobrać nowego launchera.",
                "Não foi possível baixar o novo launcher.",
                "Не удалось скачать новый лаунчер.",
                "Det gick inte att hämta den nya launchern.",
                "无法下载新的启动器。",
                "Не вдалося завантажити новий лаунчер."),

            ["gui.launcherUpdateTitle"] = L(
                "Launcher update {0} available",
                "Launcher-Update {0} verfügbar",
                "Actualización del launcher {0} disponible",
                "Mise à jour du launcher {0} disponible",
                "Dostępna aktualizacja launchera {0}",
                "Atualização do launcher {0} disponível",
                "Доступно обновление лончера {0}",
                "Launcher-uppdatering {0} tillgänglig",
                "启动器更新 {0} 可用",
                "Доступне оновлення лаунчера {0}"),

            ["gui.launcherUpdateAvailable"] = L(
                "A new launcher version ({0}) is available.",
                "Eine neue Launcher-Version ({0}) ist verfügbar.",
                "Hay una nueva versión del launcher ({0}) disponible.",
                "Une nouvelle version du launcher ({0}) est disponible.",
                "Dostępna jest nowa wersja launchera ({0}).",
                "Uma nova versão do launcher ({0}) está disponível.",
                "Доступна новая версия лончера ({0}).",
                "En ny launcher-version ({0}) är tillgänglig.",
                "有新的启动器版本（{0}）可用。",
                "Доступна нова версія лаунчера ({0})."),

            ["gui.launcherUpdateNow"] = L(
                "Update now",
                "Jetzt aktualisieren",
                "Actualizar ahora",
                "Mettre à jour",
                "Zaktualizuj teraz",
                "Atualizar agora",
                "Обновить сейчас",
                "Uppdatera nu",
                "立即更新",
                "Оновити зараз"),

            ["gui.launcherUpdateMandatoryNote"] = L(
                "This update is mandatory — the launcher can't keep working correctly on the old version.",
                "Dieses Update ist verpflichtend — der Launcher kann mit der alten Version nicht mehr korrekt funktionieren.",
                "Esta actualización es obligatoria — el launcher no puede seguir funcionando correctamente con la versión anterior.",
                "Cette mise à jour est obligatoire — le launcher ne peut plus fonctionner correctement avec l'ancienne version.",
                "Ta aktualizacja jest obowiązkowa — launcher nie może dalej działać poprawnie na starej wersji.",
                "Esta atualização é obrigatória — o launcher não pode continuar funcionando corretamente na versão antiga.",
                "Это обязательное обновление — на старой версии лончер больше не сможет работать корректно.",
                "Den här uppdateringen är obligatorisk — launchern kan inte fortsätta fungera korrekt på den gamla versionen.",
                "此更新为强制性更新——启动器无法在旧版本上继续正常工作。",
                "Це обов'язкове оновлення — на старій версії лаунчер більше не зможе працювати коректно."),

            ["gui.launcherPostponeAndExit"] = L(
                "Postpone and exit",
                "Verschieben und beenden",
                "Posponer y salir",
                "Reporter et quitter",
                "Odłóż i zamknij",
                "Adiar e sair",
                "Отложить и выйти",
                "Skjut upp och avsluta",
                "推迟并退出",
                "Відкласти і вийти"),

            ["gui.launcherUpdateLater"] = L(
                "Not now",
                "Nicht jetzt",
                "Ahora no",
                "Plus tard",
                "Nie teraz",
                "Agora não",
                "Не сейчас",
                "Inte nu",
                "暂不",
                "Не зараз"),

            ["gui.autoUpdateError"] = L(
                "Auto-update error: {0}",
                "Fehler bei der automatischen Aktualisierung: {0}",
                "Error de actualización automática: {0}",
                "Erreur de mise à jour automatique : {0}",
                "Błąd automatycznej aktualizacji: {0}",
                "Erro de atualização automática: {0}",
                "Ошибка автообновления: {0}",
                "Fel vid automatisk uppdatering: {0}",
                "自动更新错误：{0}",
                "Помилка автооновлення: {0}"),

            ["gui.vikingsCount"] = L(
                "Vikings: {0}",
                "Wikinger: {0}",
                "Vikingos: {0}",
                "Vikings : {0}",
                "Wikingowie: {0}",
                "Vikings: {0}",
                "Викингов: {0}",
                "Vikingar: {0}",
                "维京人：{0}",
                "Вікінгів: {0}"),

            // Placeholder row shown in the player list before the very first server check has
            // come back — distinct from "no vikings online" (a real, checked answer), which
            // this used to claim immediately on launch, before anything had actually been asked.
            ["gui.checkingPlayers"] = L(
                "Checking…", "Wird geprüft…", "Comprobando…", "Vérification…",
                "Sprawdzanie…", "A verificar…", "Проверка…", "Kontrollerar…", "正在检查…",
                "Перевірка…"),

            // ---------- GUI: main tabs ----------

            ["gui.tab.install"] = L(
                "Install", "Installation", "Instalación", "Installation", "Instalacja",
                "Instalação", "Установка", "Installation", "安装", "Встановлення"),

            ["gui.tab.server"] = L(
                "Server", "Server", "Servidor", "Serveur", "Serwer",
                "Servidor", "Сервер", "Server", "服务器", "Сервер"),

            ["gui.tab.mods"] = L(
                "Mods", "Mods", "Mods", "Mods", "Mody",
                "Mods", "Моды", "Mods", "模组", "Моди"),

            ["gui.tab.serverInfo"] = L(
                "About the server", "Über den Server", "Sobre el servidor", "À propos du serveur", "O serwerze",
                "Sobre o servidor", "О сервере", "Om servern", "关于服务器", "Про сервер"),

            ["gui.tab.changelog"] = L(
                "Changelog", "Änderungsprotokoll", "Registro de cambios", "Journal des modifications", "Lista zmian",
                "Changelog", "Changelog", "Ändringslogg", "更新日志", "Список змін"),

            ["gui.clientUnavailableOnMirror"] = L(
                "The client is unavailable for server {0} on the current mirror.",
                "Der Client ist für Server {0} auf dem aktuellen Mirror nicht verfügbar.",
                "El cliente no está disponible para el servidor {0} en el mirror actual.",
                "Le client est indisponible pour le serveur {0} sur le miroir actuel.",
                "Klient jest niedostępny dla serwera {0} na bieżącym mirrorze.",
                "O cliente está indisponível para o servidor {0} no mirror atual.",
                "Клиент не доступен для сервера {0} на текущем зеркале.",
                "Klienten är inte tillgänglig för servern {0} på den aktuella spegeln.",
                "在当前镜像上，服务器 {0} 的客户端不可用。",
                "Клієнт недоступний для сервера {0} на поточному дзеркалі."),

            ["gui.updateStoppedPermissions"] = L(
                "Update stopped: {0}.\n\nFolder: {1}\n\nHow to fix it:\n{2}",
                "Update gestoppt: {0}.\n\nOrdner: {1}\n\nSo beheben Sie das Problem:\n{2}",
                "Actualización detenida: {0}.\n\nCarpeta: {1}\n\nCómo solucionarlo:\n{2}",
                "Mise à jour arrêtée : {0}.\n\nDossier : {1}\n\nComment y remédier :\n{2}",
                "Aktualizacja zatrzymana: {0}.\n\nFolder: {1}\n\nJak to naprawić:\n{2}",
                "Atualização interrompida: {0}.\n\nPasta: {1}\n\nComo corrigir:\n{2}",
                "Обновление остановлено: {0}.\n\nПапка: {1}\n\nКак исправить:\n{2}",
                "Uppdateringen stoppades: {0}.\n\nMapp: {1}\n\nSå här åtgärdar du det:\n{2}",
                "更新已停止：{0}。\n\n文件夹：{1}\n\n解决方法：\n{2}",
                "Оновлення зупинено: {0}.\n\nПапка: {1}\n\nЯк виправити:\n{2}"),

            ["gui.updateStoppedUnsafe"] = L(
                "Update stopped: {0}.\n\nFolder: {1}\n\nAn update deletes everything from the client folder that isn't on the server, so it refuses to run against an unrelated folder.",
                "Update gestoppt: {0}.\n\nOrdner: {1}\n\nEin Update löscht alles aus dem Client-Ordner, was nicht auf dem Server liegt, deshalb wird es nicht auf einem fremden Ordner ausgeführt.",
                "Actualización detenida: {0}.\n\nCarpeta: {1}\n\nUna actualización elimina de la carpeta del cliente todo lo que no esté en el servidor, por lo que se niega a ejecutarse en una carpeta ajena.",
                "Mise à jour arrêtée : {0}.\n\nDossier : {1}\n\nUne mise à jour supprime du dossier client tout ce qui n'est pas sur le serveur, elle refuse donc de s'exécuter sur un dossier étranger.",
                "Aktualizacja zatrzymana: {0}.\n\nFolder: {1}\n\nAktualizacja usuwa z folderu klienta wszystko, czego nie ma na serwerze, dlatego odmawia działania na obcym folderze.",
                "Atualização interrompida: {0}.\n\nPasta: {1}\n\nUma atualização exclui da pasta do cliente tudo o que não está no servidor, por isso ela se recusa a rodar em uma pasta alheia.",
                "Обновление остановлено: {0}.\n\nПапка: {1}\n\nОбновление удаляет из папки клиента всё, чего нет на сервере, поэтому в постороннюю папку оно не запускается.",
                "Uppdateringen stoppades: {0}.\n\nMapp: {1}\n\nEn uppdatering tar bort allt från klientmappen som inte finns på servern, så den vägrar köras mot en främmande mapp.",
                "更新已停止：{0}。\n\n文件夹：{1}\n\n更新会删除客户端文件夹中服务器上没有的所有内容，因此拒绝在无关文件夹中运行。",
                "Оновлення зупинено: {0}.\n\nПапка: {1}\n\nОновлення видаляє з папки клієнта все, чого немає на сервері, тому воно відмовляється запускатися в сторонній папці."),

            ["gui.updateNotStarted"] = L(
                "Update not started: {0}.\n\nWait for it to finish, or close the other launcher.",
                "Update nicht gestartet: {0}.\n\nWarten Sie, bis es abgeschlossen ist, oder schließen Sie den anderen Launcher.",
                "Actualización no iniciada: {0}.\n\nEspere a que termine o cierre el otro launcher.",
                "Mise à jour non démarrée : {0}.\n\nAttendez qu'elle se termine ou fermez l'autre launcher.",
                "Aktualizacja nie została rozpoczęta: {0}.\n\nPoczekaj na jej zakończenie lub zamknij drugiego launchera.",
                "Atualização não iniciada: {0}.\n\nAguarde até terminar ou feche o outro launcher.",
                "Обновление не начато: {0}.\n\nДождитесь окончания или закройте второй лаунчер.",
                "Uppdateringen startades inte: {0}.\n\nVänta tills den är klar eller stäng den andra launchern.",
                "更新未开始：{0}。\n\n请等待其完成，或关闭另一个启动器。",
                "Оновлення не розпочато: {0}.\n\nДочекайтеся завершення або закрийте другий лаунчер."),

            ["gui.configUpdateFailed"] = L(
                "Failed to update config.ini: {0}",
                "config.ini konnte nicht aktualisiert werden: {0}",
                "No se pudo actualizar config.ini: {0}",
                "Impossible de mettre à jour config.ini : {0}",
                "Nie udało się zaktualizować config.ini: {0}",
                "Não foi possível atualizar config.ini: {0}",
                "Не удалось обновить config.ini: {0}",
                "Det gick inte att uppdatera config.ini: {0}",
                "无法更新 config.ini：{0}",
                "Не вдалося оновити config.ini: {0}"),

            ["gui.changelogUnavailable"] = L(
                "Changelog unavailable for this server",
                "Änderungsprotokoll für diesen Server nicht verfügbar",
                "Registro de cambios no disponible para este servidor",
                "Journal des modifications indisponible pour ce serveur",
                "Lista zmian niedostępna dla tego serwera",
                "Changelog indisponível para este servidor",
                "Changelog не доступен для этого сервера",
                "Ändringslogg saknas för den här servern",
                "此服务器的更新日志不可用",
                "Список змін недоступний для цього сервера"),

            ["gui.changelogLoadError"] = L(
                "Error loading changelog: {0}",
                "Fehler beim Laden des Änderungsprotokolls: {0}",
                "Error al cargar el registro de cambios: {0}",
                "Erreur lors du chargement du journal des modifications : {0}",
                "Błąd wczytywania listy zmian: {0}",
                "Erro ao carregar o changelog: {0}",
                "Ошибка загрузки changelog: {0}",
                "Fel vid inläsning av ändringsloggen: {0}",
                "加载更新日志出错：{0}",
                "Помилка завантаження списку змін: {0}"),

            ["gui.serverInfoUnavailable"] = L(
                "Description unavailable for this server",
                "Beschreibung für diesen Server nicht verfügbar",
                "Descripción no disponible para este servidor",
                "Description indisponible pour ce serveur",
                "Opis niedostępny dla tego serwera",
                "Descrição indisponível para este servidor",
                "Описание недоступно для этого сервера",
                "Beskrivning saknas för den här servern",
                "此服务器的说明不可用",
                "Опис недоступний для цього сервера"),

            ["gui.viewModsChangelogTip"] = L(
                "What's new in the mods for this server",
                "Was ist neu bei den Mods für diesen Server",
                "Novedades de los mods de este servidor",
                "Nouveautés des mods de ce serveur",
                "Co nowego w modach na tym serwerze",
                "Novidades dos mods deste servidor",
                "Что нового в модах этого сервера",
                "Vad är nytt i modsen för den här servern",
                "此服务器模组的更新内容",
                "Що нового в модах цього сервера"),

            // DefenderExclusionCheckBox's tooltip on the Install tab — explains when/why the
            // checkbox matters. Used to be the body of a one-time popup (ConfirmationWindow,
            // with defender.title/addButton/notNowButton/uacDeclined/retryButton alongside it);
            // those are gone along with the popup itself, replaced by this permanent checkbox.
            ["defender.message"] = L(
                "Windows Defender scans each newly downloaded mod file, which can make the launcher think it changed. Excluding this folder avoids that — Steam and Defender's regular scans still cover it.",
                "Windows Defender scannt jede neu heruntergeladene Mod-Datei, was den Launcher glauben lassen kann, sie habe sich geändert. Der Ausschluss dieses Ordners verhindert das — Steam und die regulären Scans von Defender prüfen weiterhin.",
                "Windows Defender analiza cada archivo de mod recién descargado, lo que puede hacer que el launcher crea que cambió. Excluir esta carpeta evita eso — Steam y los análisis habituales de Defender siguen cubriéndola.",
                "Windows Defender analyse chaque nouveau fichier de mod téléchargé, ce qui peut faire croire au launcher qu'il a changé. Exclure ce dossier évite cela — Steam et les analyses habituelles de Defender continuent de le couvrir.",
                "Windows Defender skanuje każdy nowo pobrany plik moda, co może sprawić, że launcher pomyśli, że się zmienił. Wykluczenie tego folderu temu zapobiega — Steam i standardowe skany Defendera nadal go obejmują.",
                "O Windows Defender analisa cada arquivo de mod recém-baixado, o que pode fazer o launcher pensar que ele mudou. Excluir esta pasta evita isso — o Steam e as varreduras normais do Defender continuam cobrindo-a.",
                "Windows Defender сканирует каждый скачанный файл мода, из-за чего лончер может решить, что файл изменился. Исключение этой папки убирает проблему — Steam и обычные проверки Defender'а по-прежнему её охватывают.",
                "Windows Defender skannar varje nyligen nedladdad moddfil, vilket kan få launchern att tro att den ändrats. Att undanta den här mappen förhindrar det — Steam och Defenders vanliga skanningar täcker den ändå.",
                "Windows Defender 会扫描每个新下载的模组文件，这可能让启动器误以为文件被更改了。将此文件夹排除可以避免这个问题——Steam 和 Defender 的常规扫描仍会覆盖它。",
                "Windows Defender сканує кожен новий завантажений файл мода, через що лаунчер може вирішити, що файл змінився. Виключення цієї папки усуває проблему — Steam і звичайні перевірки Defender усе одно її охоплюють."),

            ["gui.noPlayersOnline"] = L(
                "No vikings online right now.",
                "Momentan sind keine Wikinger online.",
                "Ahora mismo no hay vikingos en línea.",
                "Aucun viking en ligne pour le moment.",
                "Obecnie żaden wiking nie jest online.",
                "Nenhum viking online no momento.",
                "Сейчас на сервере никого нет.",
                "Inga vikingar är online just nu.",
                "目前没有维京人在线。",
                "Наразі на сервері немає вікінгів."),

            // The Start button's own label — used to be baked directly into start.png's pixels
            // (always Russian, "ИГРАТЬ", regardless of language) until it switched to
            // start-no-text.png, an unlabelled skin with the text drawn on top instead.
            ["gui.playButton"] = L(
                "Play", "Spielen", "Jugar", "Jouer",
                "Graj", "Jogar", "Играть", "Spela", "开始游戏", "Грати"),

            // Main action button before the player has ever opened the Mods tab — names that
            // as the next step regardless of which tab is currently showing (see
            // UpdateWizardButton/MarkModsTabVisited in MainWindow.axaml.cs).
            ["gui.wizardGoToMods"] = L(
                "Go to mods", "Zu den Mods", "Ir a mods", "Aller aux mods", "Przejdź do modów",
                "Ir para mods", "К модам", "Gå till mods", "前往模组", "До модів"),

            // Main action button once Mods has been visited at least once but the client still
            // isn't (fully) installed, shown from any tab *other* than Install — a navigation
            // hint ("go there"), not a promise of what happens next, since that depends on
            // AutoStartAfterValidationCheckBox (see gui.installButton/gui.installAndPlayButton,
            // used instead once already on the Install tab). Once IsClientInstalled is true all
            // three switch to gui.playButton.
            ["gui.wizardGoToInstall"] = L(
                "Go to install", "Zur Installation", "Ir a instalación", "Aller à l'installation",
                "Przejdź do instalacji", "Ir para instalação", "К установке",
                "Gå till installation", "前往安装", "До встановлення"),

            // Main action button on the Install tab itself, not yet installed, with
            // AutoStartAfterValidationCheckBox unchecked — clicking only checks/installs,
            // Play stays a separate click.
            ["gui.installButton"] = L(
                "Install", "Installieren", "Instalar", "Installer",
                "Instaluj", "Instalar", "Установить", "Installera", "安装", "Встановити"),

            // Same button, same tab, with AutoStartAfterValidationCheckBox checked instead —
            // clicking checks/installs and launches right after.
            ["gui.installAndPlayButton"] = L(
                "Install & play", "Installieren & spielen", "Instalar y jugar", "Installer et jouer",
                "Instaluj i graj", "Instalar e jogar", "Установить и играть",
                "Installera och spela", "安装并游玩", "Встановити і грати"),

            ["gui.autoStartAfterValidation"] = L(
                "Launch automatically after validation", "Nach der Prüfung automatisch starten",
                "Iniciar automáticamente tras la validación", "Lancer automatiquement après la validation",
                "Uruchom automatycznie po weryfikacji", "Iniciar automaticamente após a validação",
                "Запускать автоматически после проверки", "Starta automatiskt efter kontroll", "验证后自动启动",
                "Запускати автоматично після перевірки"),

            // Shown on the Install tab before the first check of the session has run, in place
            // of the injector/classic mode line above (which stays empty until then).
            ["gui.installStatus.notCheckedYet"] = L(
                "No check has been run yet.",
                "Es wurde noch keine Prüfung durchgeführt.",
                "Aún no se ha realizado ninguna comprobación.",
                "Aucune vérification n'a encore été effectuée.",
                "Nie przeprowadzono jeszcze żadnej weryfikacji.",
                "Ainda não foi feita nenhuma verificação.",
                "Проверка ещё не выполнялась.",
                "Ingen kontroll har körts än.",
                "尚未进行检查。",
                "Перевірку ще не виконано."),

            ["gui.installMode.injector"] = L(
                "Playing from your Steam installation",
                "Läuft über Ihre Steam-Installation",
                "Se juega desde tu instalación de Steam",
                "Lancé depuis votre installation Steam",
                "Uruchamiane z Twojej instalacji Steam",
                "Jogando a partir da sua instalação do Steam",
                "Запуск из вашей установки Steam",
                "Spelas från din Steam-installation",
                "从您的 Steam 安装运行",
                "Запуск з вашого встановлення Steam"),

            // Rewritten after feedback that the old wording ("nothing is duplicated…") didn't
            // actually say where the files come FROM, and that the still-happens validation
            // step wasn't mentioned at all — both are the two things this tooltip exists for.
            ["gui.installMode.injectorTip"] = L(
                "The launcher will run the game straight from your existing Steam installation at {0} — it won't copy the files into its own folder. Every file there still gets checked against what's expected first.",
                "Der Launcher startet das Spiel direkt aus Ihrer vorhandenen Steam-Installation unter {0} — die Dateien werden nicht in den eigenen Ordner kopiert. Trotzdem wird jede Datei dort zuerst gegen die erwarteten Dateien geprüft.",
                "El launcher ejecutará el juego directamente desde tu instalación de Steam existente en {0} — no copiará los archivos a su propia carpeta. Aun así, comprobará cada archivo ahí contra lo esperado antes de jugar.",
                "Le launcher lancera le jeu directement depuis votre installation Steam existante dans {0} — il ne copiera pas les fichiers dans son propre dossier. Chaque fichier y est quand même vérifié par rapport à ce qui est attendu avant de jouer.",
                "Launcher uruchomi grę bezpośrednio z Twojej istniejącej instalacji Steam w {0} — nie skopiuje plików do swojego folderu. Mimo to każdy plik tam zostanie najpierw sprawdzony względem tego, co jest oczekiwane.",
                "O launcher vai executar o jogo diretamente a partir da sua instalação existente do Steam em {0} — não vai copiar os arquivos para a própria pasta. Mesmo assim, cada arquivo ali é verificado em relação ao esperado antes de jogar.",
                "Лаунчер запустит игру прямо из вашей существующей установки Steam ({0}) — файлы не будут копироваться в его собственную папку. Тем не менее, каждый файл там всё равно будет сверен с ожидаемым перед запуском.",
                "Launchern kör spelet direkt från din befintliga Steam-installation på {0} — filerna kopieras inte till launcherns egen mapp. Varje fil där kontrolleras ändå mot det förväntade innan spelet startar.",
                "启动器将直接从您现有的 Steam 安装（{0}）运行游戏——不会将文件复制到自己的文件夹中。即便如此，这里的每个文件在运行前仍会与预期文件进行校验。",
                "Лаунчер запустить гру прямо з вашого наявного встановлення Steam ({0}) — файли не копіюватимуться в його власну папку. Попри це, кожен файл там усе одно буде звірено з очікуваним перед запуском."),

            ["gui.installMode.classic"] = L(
                "Playing from the launcher's own copy",
                "Spielt aus der eigenen Kopie des Launchers",
                "Se juega desde la copia propia del launcher",
                "Lancé depuis la copie du launcher",
                "Uruchamiane z własnej kopii launchera",
                "Jogando a partir da própria cópia do launcher",
                "Запуск из копии лончера",
                "Spelas från launcherns egen kopia",
                "从启动器自带副本运行",
                "Запуск із власної копії лаунчера"),

            // Same rewrite reasoning as gui.installMode.injectorTip.
            ["gui.installMode.classicTip"] = L(
                "No matching Steam installation was found, so the launcher keeps its own copy of the game files here in the client folder. Every file there still gets checked against what's expected first.",
                "Es wurde keine passende Steam-Installation gefunden, daher behält der Launcher seine eigene Kopie der Spieldateien im Client-Ordner. Trotzdem wird jede Datei dort zuerst gegen die erwarteten Dateien geprüft.",
                "No se encontró ninguna instalación de Steam compatible, así que el launcher guarda su propia copia de los archivos del juego en la carpeta del cliente. Aun así, comprobará cada archivo ahí contra lo esperado antes de jugar.",
                "Aucune installation Steam correspondante n'a été trouvée, donc le launcher garde sa propre copie des fichiers du jeu dans le dossier client. Chaque fichier y est quand même vérifié par rapport à ce qui est attendu avant de jouer.",
                "Nie znaleziono pasującej instalacji Steam, więc launcher przechowuje własną kopię plików gry w folderze klienta. Mimo to każdy plik tam zostanie najpierw sprawdzony względem tego, co jest oczekiwane.",
                "Nenhuma instalação do Steam compatível foi encontrada, então o launcher mantém sua própria cópia dos arquivos do jogo na pasta do cliente. Mesmo assim, cada arquivo ali é verificado em relação ao esperado antes de jogar.",
                "Подходящая установка Steam не найдена, поэтому лаунчер хранит свою собственную копию файлов игры в папке клиента. Тем не менее, каждый файл там всё равно будет сверен с ожидаемым перед запуском.",
                "Ingen matchande Steam-installation hittades, så launchern behåller sin egen kopia av spelfilerna i klientmappen. Varje fil där kontrolleras ändå mot det förväntade innan spelet startar.",
                "未找到匹配的 Steam 安装，因此启动器会在客户端文件夹中保留自己的一份游戏文件副本。即便如此，这里的每个文件在运行前仍会与预期文件进行校验。",
                "Відповідне встановлення Steam не знайдено, тому лаунчер зберігає власну копію файлів гри в папці клієнта. Попри це, кожен файл там усе одно буде звірено з очікуваним перед запуском."),

            ["mods.unavailable"] = L(
                "Mod list unavailable right now — check your connection and try again.",
                "Modliste momentan nicht verfügbar — Verbindung prüfen und erneut versuchen.",
                "Lista de mods no disponible ahora mismo — comprueba tu conexión e inténtalo de nuevo.",
                "Liste des mods indisponible pour le moment — vérifiez votre connexion et réessayez.",
                "Lista modów obecnie niedostępna — sprawdź połączenie i spróbuj ponownie.",
                "Lista de mods indisponível no momento — verifique a sua ligação e tente novamente.",
                "Список модов сейчас недоступен — проверьте соединение и попробуйте ещё раз.",
                "Modlistan är inte tillgänglig just nu — kontrollera anslutningen och försök igen.",
                "模组列表当前不可用——请检查网络连接后重试。",
                "Список модів зараз недоступний — перевірте з'єднання і спробуйте ще раз."),

            ["mods.required"] = L(
                "Required mods",
                "Erforderliche Mods",
                "Mods obligatorios",
                "Mods requis",
                "Wymagane mody",
                "Mods obrigatórios",
                "Обязательные моды",
                "Obligatoriska mods",
                "必装模组",
                "Обов'язкові моди"),

            ["mods.optional"] = L(
                "Optional mods",
                "Optionale Mods",
                "Mods opcionales",
                "Mods optionnels",
                "Opcjonalne mody",
                "Mods opcionais",
                "Опциональные моды",
                "Valfria mods",
                "可选模组",
                "Опціональні моди"),

            ["mods.adminOnly"] = L(
                "Admin-only mods",
                "Nur für Admins",
                "Mods solo para administradores",
                "Mods réservés aux admins",
                "Mody tylko dla adminów",
                "Mods somente para admins",
                "Моды только для админов",
                "Endast för admins",
                "仅管理员模组",
                "Моди лише для адмінів"),

            ["gui.newsLoadFailed"] = L(
                "Failed to load news: {0}",
                "Neuigkeiten konnten nicht geladen werden: {0}",
                "No se pudieron cargar las noticias: {0}",
                "Impossible de charger les actualités : {0}",
                "Nie udało się wczytać aktualności: {0}",
                "Não foi possível carregar as novidades: {0}",
                "Не удалось загрузить новости: {0}",
                "Det gick inte att läsa in nyheterna: {0}",
                "无法加载新闻：{0}",
                "Не вдалося завантажити новини: {0}"),

            ["gui.valheimExeNotFound"] = L(
                "File valheim.exe not found in {0}",
                "Datei valheim.exe nicht gefunden in {0}",
                "No se encontró el archivo valheim.exe en {0}",
                "Fichier valheim.exe introuvable dans {0}",
                "Nie znaleziono pliku valheim.exe w {0}",
                "Arquivo valheim.exe não encontrado em {0}",
                "Файл valheim.exe не найден в {0}",
                "Filen valheim.exe hittades inte i {0}",
                "在 {0} 中未找到 valheim.exe 文件",
                "Файл valheim.exe не знайдено в {0}"),

            ["gui.title.error"] = L(
                "Error", "Fehler", "Error", "Erreur", "Błąd", "Erro", "Ошибка", "Fel", "错误", "Помилка"),

            ["gui.title.noWriteAccess"] = L(
                "No write access",
                "Kein Schreibzugriff",
                "Sin acceso de escritura",
                "Aucun accès en écriture",
                "Brak dostępu do zapisu",
                "Sem acesso de gravação",
                "Нет прав на запись",
                "Ingen skrivbehörighet",
                "没有写入权限",
                "Немає прав на запис"),

            ["gui.title.folderBusy"] = L(
                "Folder busy",
                "Ordner belegt",
                "Carpeta ocupada",
                "Dossier occupé",
                "Folder zajęty",
                "Pasta ocupada",
                "Папка занята",
                "Mappen upptagen",
                "文件夹被占用",
                "Папка зайнята"),

            ["gui.title.launchError"] = L(
                "Launch error",
                "Startfehler",
                "Error de inicio",
                "Erreur de lancement",
                "Błąd uruchamiania",
                "Erro ao iniciar",
                "Ошибка запуска",
                "Startfel",
                "启动错误",
                "Помилка запуску"),

            // ---------- GUI: ServerSelectionWindow ----------

            ["serverSelection.title"] = L(
                "Server selection",
                "Serverauswahl",
                "Selección de servidor",
                "Sélection du serveur",
                "Wybór serwera",
                "Seleção de servidor",
                "Выбор сервера",
                "Serverval",
                "选择服务器",
                "Вибір сервера"),

            ["serverSelection.selectAServer"] = L(
                "Select a server",
                "Server auswählen",
                "Seleccione un servidor",
                "Sélectionnez un serveur",
                "Wybierz serwer",
                "Selecione um servidor",
                "Выберите сервер",
                "Välj en server",
                "请选择服务器",
                "Виберіть сервер"),

            ["serverSelection.ok"] = L(
                "OK", "OK", "Aceptar", "OK", "OK", "OK", "ОК", "OK", "确定", "ОК"),

            // Superseded in Avalonia by gui.copyrightLabel + gui.launcherVersionLabel (split
            // into two lines, only the copyright one clickable) — kept for WPF, which still
            // shows it as the one combined line.
            ["gui.versionLabel"] = L(
                "© OdinSons Team, 2026. Launcher version:",
                "© OdinSons Team, 2026. Launcher-Version:",
                "© OdinSons Team, 2026. Versión del launcher:",
                "© OdinSons Team, 2026. Version du launcher :",
                "© OdinSons Team, 2026. Wersja launchera:",
                "© OdinSons Team, 2026. Versão do launcher:",
                "© OdinSons Team, 2026. Версия лаунчера:",
                "© OdinSons Team, 2026. Launcherversion:",
                "© OdinSons Team, 2026年。启动器版本：",
                "© OdinSons Team, 2026. Версія лаунчера:"),

            ["gui.copyrightLabel"] = L(
                "© OdinSons Team, 2026", "© OdinSons Team, 2026", "© OdinSons Team, 2026",
                "© OdinSons Team, 2026", "© OdinSons Team, 2026", "© OdinSons Team, 2026",
                "© OdinSons Team, 2026", "© OdinSons Team, 2026", "© OdinSons Team，2026年",
                "© OdinSons Team, 2026"),

            ["gui.launcherVersionLabel"] = L(
                "Launcher:", "Launcher:", "Launcher:", "Launcher :",
                "Launcher:", "Launcher:", "Лаунчер:", "Launcher:", "启动器：", "Лаунчер:"),

            ["gui.fullCheckTooltip"] = L(
                "Full client check",
                "Vollständige Client-Prüfung",
                "Verificación completa del cliente",
                "Vérification complète du client",
                "Pełne sprawdzenie klienta",
                "Verificação completa do cliente",
                "Полная проверка клиента",
                "Fullständig klientkontroll",
                "完整客户端检查",
                "Повна перевірка клієнта"),

            // ---------- GUI: window chrome (tooltips, website link, OK button) ----------

            // Used to be a bare "Наш сайт" literal in MainWindow.axaml, never translated at
            // all — the one genuinely missing-from-Loc string this project's own audit found.
            // Shortened from "Our website" to plain "Website" (and its per-language equivalents)
            // once translated — reads just as clearly without the possessive.
            ["gui.websiteLink"] = L(
                "Website", "Website", "Sitio web", "Site web", "Strona",
                "Site", "Вебсайт", "Webbplats", "网站", "Веб-сайт"),

            // MessageBoxWindow's OK button — same bug as gui.websiteLink, a bare "OK" literal
            // in MessageBoxWindow.axaml that never went through Loc at all. Same convention as
            // serverSelection.ok (Spanish "Aceptar" rather than a bare "OK").
            ["gui.okButton"] = L(
                "OK", "OK", "Aceptar", "OK", "OK", "OK", "ОК", "OK", "确定", "ОК"),

            ["gui.tooltip.minimize"] = L(
                "Minimize", "Minimieren", "Minimizar", "Réduire", "Minimalizuj",
                "Minimizar", "Свернуть", "Minimera", "最小化", "Згорнути"),

            ["gui.tooltip.maximize"] = L(
                "Maximize", "Maximieren", "Maximizar", "Agrandir", "Maksymalizuj",
                "Maximizar", "Развернуть", "Maximera", "最大化", "Розгорнути"),

            ["gui.tooltip.restore"] = L(
                "Restore", "Wiederherstellen", "Restaurar", "Restaurer", "Przywróć",
                "Restaurar", "Восстановить", "Återställ", "还原", "Відновити"),

            ["gui.tooltip.close"] = L(
                "Close", "Schließen", "Cerrar", "Fermer", "Zamknij",
                "Fechar", "Закрыть", "Stäng", "关闭", "Закрити"),

            // Windows only (see AutoStartAfterValidationCheckBox_Click's sibling
            // DesktopShortcutCheckBox_Click in MainWindow.axaml.cs) — macOS/Linux builds don't
            // show this checkbox at all, see RuntimePlatform.IsWindows gating in the XAML's
            // code-behind.
            ["gui.desktopShortcut"] = L(
                "Add a desktop shortcut", "Verknüpfung auf dem Desktop erstellen",
                "Añadir un acceso directo al escritorio", "Ajouter un raccourci sur le bureau",
                "Dodaj skrót na pulpicie", "Adicionar um atalho na área de trabalho",
                "Добавить ярлык на рабочий стол", "Lägg till en genväg på skrivbordet", "在桌面添加快捷方式",
                "Додати ярлик на робочий стіл"),

            // Windows only, same pattern/checkbox group as gui.desktopShortcut — see
            // LoadStartMenuShortcutState/StartMenuShortcutCheckBox_Click.
            ["gui.startMenuShortcut"] = L(
                "Add to the Start menu", "Zum Startmenü hinzufügen",
                "Añadir al menú Inicio", "Ajouter au menu Démarrer",
                "Dodaj do menu Start", "Adicionar ao menu Iniciar",
                "Добавить в меню Пуск", "Lägg till i Start-menyn", "添加到开始菜单",
                "Додати до меню Пуск"),

            // Windows only, same checkbox group as gui.desktopShortcut/gui.startMenuShortcut —
            // a permanent, always-reachable equivalent of the one-time
            // MaybeOfferDefenderExclusionAsync popup (defender.title etc.) a player could
            // dismiss with "Not now" and then have no way back to.
            ["gui.defenderExclusionCheckbox"] = L(
                "Add a Windows Defender exclusion", "Windows Defender-Ausnahme hinzufügen",
                "Añadir una exclusión de Windows Defender", "Ajouter une exclusion Windows Defender",
                "Dodaj wykluczenie w Windows Defender", "Adicionar uma exclusão do Windows Defender",
                "Добавить исключение в Защитник Windows", "Lägg till ett Windows Defender-undantag", "添加 Windows Defender 排除项",
                "Додати виключення в Захисник Windows"),

            // Collapsible header above the checkbox group on the Install tab (auto-start,
            // shortcuts, Defender exclusion) — see InstallSettingsHeaderRow in MainWindow.axaml.
            ["gui.installSettingsHeading"] = L(
                "Install settings", "Installationseinstellungen",
                "Ajustes de instalación", "Paramètres d'installation",
                "Ustawienia instalacji", "Configurações de instalação",
                "Настройки установки", "Installationsinställningar", "安装设置",
                "Налаштування встановлення")
        };
    }
}
