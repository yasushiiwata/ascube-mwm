using System.Text;
using Ascube.Mwm.Tools.Commands;

// 起動時に1回。呼ばないと CP932（半角カナ・JIS X 0201 等）が扱えない。
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    switch (args)
    {
        case ["scu", "echo", ..]:
            return await ScuEchoCommand.RunAsync(args[2..]);
        case ["scu", "find", ..]:
            return await ScuFindCommand.RunAsync(args[2..]);
        case ["scu", "preview", ..]:
            return await ScuPreviewCommand.RunAsync(args[2..]);
        case ["scu", "replay", ..]:
            return await ScuReplayCommand.RunAsync(args[2..]);
        case ["admin", "validate", ..]:
            return AdminValidateCommand.Run(args[2..]);
        case ["admin", "explain-query", ..]:
            return await AdminExplainQueryCommand.RunAsync(args[2..]);
        case ["admin", "health", ..]:
            return await AdminHealthCommand.RunAsync(args[2..]);
        case ["admin", "export-support-bundle", ..]:
            return await AdminExportSupportBundleCommand.RunAsync(args[2..]);
        case ["admin", "set-current", ..]:
            return await AdminSetCurrentCommand.RunAsync(args[2..]);
        case ["admin", "clear-current", ..]:
            return await AdminClearCurrentCommand.RunAsync(args[2..]);
    }

    Console.Error.WriteLine("使い方:");
    Console.Error.WriteLine("  mwm-scu  " + ScuEchoCommand.Usage);
    Console.Error.WriteLine("  mwm-scu  " + ScuFindCommand.Usage);
    Console.Error.WriteLine("  mwm-scu  " + ScuPreviewCommand.Usage);
    Console.Error.WriteLine("  mwm-scu  " + ScuReplayCommand.Usage);
    Console.Error.WriteLine("  mwm-admin " + AdminValidateCommand.Usage);
    Console.Error.WriteLine("  mwm-admin " + AdminExplainQueryCommand.Usage);
    Console.Error.WriteLine("  mwm-admin " + AdminHealthCommand.Usage);
    Console.Error.WriteLine("  mwm-admin " + AdminExportSupportBundleCommand.Usage);
    Console.Error.WriteLine("  mwm-admin " + AdminSetCurrentCommand.Usage);
    Console.Error.WriteLine("  mwm-admin " + AdminClearCurrentCommand.Usage);
    return 2;
}
