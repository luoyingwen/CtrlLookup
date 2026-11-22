using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CtrlLookup.Dictionary;

namespace CtrlLookup;

internal static class DictionarySetupPrompt
{
    private const string ScriptName = "Setup-Dictionary.cmd";
    private const string Title = "CtrlLookup 离线词典";

    public static void ShowIfMissing()
    {
        var missing = new List<string>(2);
        if (!File.Exists(OfflineDictionary.DefaultPath)) missing.Add("英汉词库（约 66 MB）");
        if (!File.Exists(OfflineDictionary.ChinesePath)) missing.Add("汉英词库（约 4 MB）");
        if (missing.Count == 0) return;

        var details = $"尚未安装：{string.Join("、", missing)}。\n\n不安装也可以继续使用划词预览。";
        var script = Path.Combine(AppContext.BaseDirectory, ScriptName);
        if (!File.Exists(script))
        {
            MessageBox.Show($"{details}\n请从项目目录运行 {ScriptName} 下载词库。", Title,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show($"{details}\n\n是否运行 {ScriptName} 下载缺少的词库？需要联网。", Title,
                MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        try
        {
            // The setup console shows progress and errors; preview stays available during installation.
            Process.Start(new ProcessStartInfo(script) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory })?.Dispose();
        }
        catch (Win32Exception)
        {
            MessageBox.Show($"无法启动下载脚本。请在程序目录中手动运行 {ScriptName}。", Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
