using System.Text;
using AMD.AutoService.GaragePro.SpAdmin.Cli;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AMD.AutoService.GaragePro.SpAdmin;

/// <summary>
/// spadmin — เครื่องมือทีมงานของอู่ทดลองใช้ (S5) · วิธีใช้/ความปลอดภัยดู tools/spadmin/README.md
/// · class ชื่อเฉพาะ (ไม่ใช่ top-level statements) — โปรเจกต์เทสต์อ้าง assembly นี้คู่กับ API ซึ่งมี <c>Program</c> อยู่แล้ว
/// </summary>
public static class SpAdminProgram
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var parsed = CommandParser.Parse(args);
        if (parsed.Error is not null)
        {
            await Console.Error.WriteLineAsync(parsed.Error);
            return ExitCodes.Usage;
        }

        if (parsed.IsHelp)
        {
            Console.WriteLine(CommandParser.Usage(parsed.HelpFor));
            return ExitCodes.Ok;
        }

        var command = parsed.Command!;
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            using var host = SpAdminHost.Build(command.Verbose);
            if (SpAdminHost.CheckConfiguration(host.Services.GetRequiredService<IConfiguration>()) is { } problem)
            {
                await Console.Error.WriteLineAsync(problem);
                return ExitCodes.NotConfigured;
            }

            return await new CommandRunner(host.Services, Console.Out, Console.Error).RunAsync(command, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            await Console.Error.WriteLineAsync("ยกเลิกแล้ว (Ctrl+C) แถวที่ทำสำเร็จก่อนหน้านี้ถูกบันทึกแล้ว รันคำสั่งเดิมซ้ำได้");
            return ExitCodes.Cancelled;
        }
        catch (SqlException ex)
        {
            await Console.Error.WriteLineAsync(
                $"✗ ฐานข้อมูลตอบกลับผิดพลาด / database error {ex.Number}: {ex.Message}\n" +
                "  ตรวจ VPN/connection string (SelfServe:ConnectionString · LegacyShards:ConnectionStrings:*)");
            if (command.Verbose) await Console.Error.WriteLineAsync(ex.ToString());
            return ExitCodes.Failed;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"✗ {ex.GetType().Name}: {ex.Message}");
            if (command.Verbose) await Console.Error.WriteLineAsync(ex.ToString());
            else await Console.Error.WriteLineAsync("  (ใส่ --verbose เพื่อดูรายละเอียด)");
            return ExitCodes.Failed;
        }
    }
}
