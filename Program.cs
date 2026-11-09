using System.Diagnostics;
using SngTrans.Ui;

namespace SngTrans;

static class Program
{
    /// <summary>Kết thúc tác vụ bản SngTrans đang chạy. Trả về "ok" / "none" / "fail".</summary>
    static string KillRunning()
    {
        using var me = Process.GetCurrentProcess();
        bool any = false, fail = false;
        foreach (var p in Process.GetProcessesByName(me.ProcessName))
        {
            try
            {
                if (p.Id == me.Id) continue;
                p.Kill(true);
                p.WaitForExit(3000);
                any = true;
            }
            catch { fail = true; }
            finally { p.Dispose(); }
        }
        Log.Info($"kết thúc tác vụ bản đang chạy -> {(fail ? "lỗi" : any ? "ok" : "không có")}");
        return fail ? "fail" : any ? "ok" : "none";
    }

    static void ShowAlreadyRunning()
    {
        var kill = new TaskDialogButton("Tắt hẳn bản đang chạy");
        var page = new TaskDialogPage
        {
            Caption = "SngTrans",
            Heading = "SngTrans đang chạy rồi",
            Text = "Tìm biểu tượng ở khay hệ thống (góc phải thanh taskbar, có thể nằm trong mũi tên ^).",
            Icon = TaskDialogIcon.Information,
        };
        page.Buttons.Add(kill);
        page.Buttons.Add(TaskDialogButton.Close);
        if (TaskDialog.ShowDialog(page) == kill)
        {
            var res = KillRunning();
            MessageBox.Show(res switch
            {
                "ok" => "Đã tắt SngTrans. Mở lại app để dùng tiếp.",
                "none" => "Không tìm thấy bản đang chạy (có thể đã tắt).",
                _ => "Không tắt được. Mở Task Manager, tìm SngTrans và bấm Kết thúc tác vụ.",
            }, "SngTrans", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    static void Crash(string msg)
    {
        try
        {
            MessageBox.Show($"{msg}\n\nChi tiết trong file:\n{Log.LogPath}", "SngTrans gặp lỗi",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { }
    }

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--debug"))
        {
            Log.Debug = true;
            Win32.AttachParentConsole();
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => { Log.Error("lỗi không bắt được", e.Exception); Crash(e.Exception.Message); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("lỗi nghiêm trọng", e.ExceptionObject as Exception);
            if (e.ExceptionObject is Exception ex) Crash(ex.Message);
        };

        if (args.Contains("--quit"))                  // SngTrans.exe --quit : tắt bản đang chạy rồi thoát
        {
            var res = KillRunning();
            Log.Info($"--quit -> {res}");
            return res is "ok" or "none" ? 0 : 1;
        }

        Log.Info("khởi động SngTrans");
        Updater.CleanupOld();
        Mutex? mutex = null;
        bool first = false;
        int tries = args.Contains("--after-update") ? 40 : 1;       // vừa cập nhật: chờ bản cũ thoát (tối đa ~10 giây)
        for (int i = 0; i < tries; i++)
        {
            mutex?.Dispose();
            mutex = new Mutex(true, @"Local\SngTrans.SingleInstance", out first);
            if (first) break;
            Thread.Sleep(250);
        }
        if (!first)
        {
            mutex?.Dispose();
            try
            {
                using var ev = EventWaitHandle.OpenExisting(TrayApp.ShowEventName);   // bản đang chạy ngầm: bảo nó mở Cài đặt
                ev.Set();
                return 0;
            }
            catch { }
            ShowAlreadyRunning();                                                      // bản cũ không hỗ trợ: hiện hộp thoại như trước
            return 0;
        }
        try
        {
            Application.Run(new TrayApp(args.Contains("--settings")));
            return 0;
        }
        catch (Exception e)
        {
            Log.Error("không khởi động được", e);
            Crash(e.Message);
            return 1;
        }
        finally
        {
            mutex?.Dispose();
        }
    }
}
