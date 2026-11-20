using System.Windows;

namespace DesktopDrawer.Bootstrap;

/// <summary>
/// Strings for the installer and the uninstall page. Four languages, picked
/// from the system UI language on first run and remembered afterwards.
/// </summary>
internal static class Lang
{
    public static string[] Options { get; } = ["zh-CN", "zh-TW", "en", "ja"];

    public static string Current { get; private set; } = Detect();

    public static string Detect()
    {
        string? saved = ReadSaved();
        if (saved is not null && Options.Contains(saved))
        {
            return saved;
        }

        string ui = System.Globalization.CultureInfo.CurrentUICulture.Name.ToLowerInvariant();

        if (ui.StartsWith("ja")) return "ja";
        if (ui.StartsWith("zh"))
        {
            return ui.Contains("tw") || ui.Contains("hk") || ui.Contains("hant") ? "zh-TW" : "zh-CN";
        }

        return "en";
    }

    public static void Override(string code)
    {
        Current = Options.Contains(code) ? code : Detect();
    }

    public static void Set(string code)
    {
        Current = Options.Contains(code) ? code : "en";
        Save(Current);
    }

    public static string T(string key)
    {
        bool en = Current == "en";
        bool tw = Current == "zh-TW";
        bool ja = Current == "ja";

        return key switch
        {
            "subtitle" => en ? "A paged desktop" : ja ? "ページ式デスクトップ" : tw ? "分頁式桌面" : "分页式桌面",
            "installTitle" => en ? "Installer" : ja ? "インストーラー" : tw ? "安裝程式" : "安装程序",
            "uninstallTitle" => en ? "Uninstall" : ja ? "アンインストール" : tw ? "解除安裝" : "卸载",
            "checking" => en ? "Checking for the latest version…" : ja ? "最新バージョンを確認しています…" : tw ? "正在取得最新版本…" : "正在获取最新版本…",
            "found" => en ? "Version {0} is available" : ja ? "バージョン {0} があります" : tw ? "發現新版本 {0}" : "发现新版本 {0}",
            "willInstall" => en ? "Desktop Drawer {0} will be downloaded and installed" : ja ? "Desktop Drawer {0} をダウンロードしてインストールします" : tw ? "將下載並安裝 Desktop Drawer {0}" : "将下载并安装 Desktop Drawer {0}",
            "noRelease" => en ? "No stable release is available yet." : ja ? "安定版のリリースがまだありません。" : tw ? "目前沒有可安裝的正式版本。" : "目前没有可安装的正式版本。",
            "noInfo" => en ? "Could not read the release information: {0}" : ja ? "リリース情報を取得できません: {0}" : tw ? "無法取得版本資訊：{0}" : "无法获取版本信息：{0}",
            "downloading" => en ? "Downloading…" : ja ? "ダウンロード中…" : tw ? "正在下載…" : "正在下载…",
            "downloaded" => en ? "{0}% downloaded" : ja ? "{0}% ダウンロード済み" : tw ? "已下載 {0}%" : "已下载 {0}%",
            "verifying" => en ? "Verifying the package…" : ja ? "パッケージを検証しています…" : tw ? "正在校驗安裝包…" : "正在校验安装包…",
            "installing" => en ? "Installing…" : ja ? "インストール中…" : tw ? "正在安裝…" : "正在安装…",
            "installed" => en ? "Installed" : ja ? "インストール完了" : tw ? "安裝完成" : "安装完成",
            "installedNote" => en ? "Desktop Drawer is up to date" : ja ? "Desktop Drawer は最新です" : tw ? "Desktop Drawer 已是最新版本" : "Desktop Drawer 已是最新版本",
            "failed" => en ? "Install failed: {0}" : ja ? "インストールに失敗しました: {0}" : tw ? "安裝失敗：{0}" : "安装失败：{0}",
            "code" => en ? "The installer ended with code {0}." : ja ? "インストーラーがコード {0} で終了しました。" : tw ? "安裝未完成（退出代碼 {0}）。" : "安装未完成（退出代码 {0}）。",
            "install" => en ? "Install" : ja ? "インストール" : tw ? "安裝" : "安装",
            "launch" => en ? "Launch" : ja ? "起動" : tw ? "啟動" : "启动",
            "cancel" => en ? "Cancel" : ja ? "キャンセル" : tw ? "取消" : "取消",
            "close" => en ? "Close" : ja ? "閉じる" : tw ? "關閉" : "关闭",
            "uninstall" => en ? "Uninstall" : ja ? "アンインストール" : tw ? "解除安裝" : "卸载",
            "uninstallTitleText" => en ? "Desktop Drawer will be uninstalled" : ja ? "Desktop Drawer をアンインストールします" : tw ? "將解除安裝 Desktop Drawer" : "将卸载 Desktop Drawer",
            "desktopSafe" => en ? "Files on your desktop are never touched." : ja ? "デスクトップのファイルは削除されません。" : tw ? "桌面上的檔案不會被刪除。" : "桌面上的文件不会被删除。",
            "keepSettings" => en ? "Keep settings and logs" : ja ? "設定とログを保持する" : tw ? "保留設定與日誌" : "保留设置和日志",
            "stopping" => en ? "Closing Desktop Drawer…" : ja ? "Desktop Drawer を終了しています…" : tw ? "正在關閉 Desktop Drawer…" : "正在关闭 Desktop Drawer…",
            "removingFiles" => en ? "Removing program files…" : ja ? "プログラム ファイルを削除しています…" : tw ? "正在刪除程式檔案…" : "正在删除程序文件…",
            "removingSettings" => en ? "Removing settings…" : ja ? "設定を削除しています…" : tw ? "正在刪除設定…" : "正在删除设置…",
            "cleaning" => en ? "Cleaning up…" : ja ? "クリーンアップしています…" : tw ? "正在清理…" : "正在清理…",
            "uninstalled" => en ? "Uninstalled" : ja ? "アンインストール完了" : tw ? "解除安裝完成" : "卸载完成",
            "cleanupNote" => en ? "Any remaining files are removed automatically in a few seconds." : ja ? "残ったファイルは数秒後に自動で削除されます。" : tw ? "若安裝目錄仍有殘留，幾秒後會自動清理。" : "如果安装目录仍有残留，几秒后会自动清理。",
            "uninstallFailed" => en ? "Uninstall failed: {0}" : ja ? "アンインストールに失敗しました: {0}" : tw ? "解除安裝失敗：{0}" : "卸载失败：{0}",
            _ => key
        };
    }

    public static string F(string key, params object[] values) =>
        string.Format(T(key), values);

    private static string ReadSaved()
    {
        try
        {
            object? value = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(@"Software\BEGINWITHF\DesktopDrawerInstaller")
                ?.GetValue("Language");

            return value as string;
        }
        catch
        {
            return null;
        }
    }

    private static void Save(string code)
    {
        try
        {
            Microsoft.Win32.Registry.CurrentUser
                .CreateSubKey(@"Software\BEGINWITHF\DesktopDrawerInstaller")
                ?.SetValue("Language", code);
        }
        catch
        {
            // Remembering the language must never break the installer.
        }
    }
}
