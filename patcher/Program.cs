using System.Diagnostics;

// 《愚公移山》单机化补丁 —— 一键构建
//
//   dotnet run --project patcher
//
// 仓库里已经放好解包后的目录 dec/（清单改动已经烘焙进去），
// 所以这里只剩三件事：打 DLL 补丁 -> 回包 -> 签名。
//
// 需要 PATH 里有 java（JDK 8+），以及 tools/ 里放好 apktool.jar 和
// uber-apk-signer.jar，下载地址见 README 的「构建」一节。

try
{
    var root = FindRepoRoot();
    Console.WriteLine($"仓库根目录: {root}");

    var dec = Path.Combine(root, "dec");
    var work = Path.Combine(root, "work");
    var outDir = Path.Combine(root, "out");
    var tools = Path.Combine(root, "tools");
    Directory.CreateDirectory(outDir);

    RequireJava();

    var apktool = RequireTool(tools, "apktool.jar",
        "https://github.com/iBotPeaches/Apktool/releases");
    var signer = RequireTool(tools, "uber-apk-signer.jar",
        "https://github.com/patrickfav/uber-apk-signer/releases");

    // 拷到 work/ 再改，保证仓库里的 dec/ 始终干净、可重复构建
    Step("准备工作目录");
    var workDec = Path.Combine(work, "dec");
    if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
    CopyDirectory(dec, workDec);
    Console.WriteLine($"    dec/ -> work/dec（{CountFiles(workDec)} 个文件）");

    Step("DLL 补丁");
    var dll = Path.Combine(workDec, "assets", "bin", "Data", "Managed", "Assembly-CSharp.dll");
    if (!File.Exists(dll)) throw new Exception($"找不到 {dll}");
    Patcher.Apply(dll, dll);

    Step("回包");
    var unsigned = Path.Combine(work, "unsigned.apk");
    Run("java", "-jar", apktool, "b", workDec, "-o", unsigned);

    Step("对齐 + 签名（v1 + v2 + v3）");
    foreach (var stale in Directory.GetFiles(outDir, "*.apk").Concat(Directory.GetFiles(outDir, "*.idsig")))
        File.Delete(stale);
    Run("java", "-jar", signer, "--apks", unsigned, "-o", outDir, "--allowResign");
    foreach (var f in Directory.GetFiles(outDir, "*.idsig")) File.Delete(f);

    var produced = Directory.GetFiles(outDir, "*.apk").Single();
    var final = Path.Combine(outDir, "ygys-offline.apk");
    File.Move(produced, final, overwrite: true);

    Step("完成");
    Console.WriteLine($"    {final}");
    Console.WriteLine($"    {new FileInfo(final).Length / 1024.0 / 1024.0:N2} MiB");
    Console.WriteLine();
    Console.WriteLine("    安装（debug key 签名，与原版签名不同，属于全新安装，不继承原版存档）：");
    Console.WriteLine($"      adb install -r \"{final}\"");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"构建失败: {ex.Message}");
    return 1;
}

static void Step(string msg)
{
    Console.WriteLine();
    Console.WriteLine($"==> {msg}");
}

// 从当前目录逐级向上找含 dec/apktool.yml 的目录，这样在仓库内任何位置执行都行
static string FindRepoRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var d = new DirectoryInfo(start);
        while (d != null)
        {
            if (File.Exists(Path.Combine(d.FullName, "dec", "apktool.yml"))) return d.FullName;
            d = d.Parent;
        }
    }
    throw new Exception("找不到仓库根目录（应含 dec/apktool.yml）。请在仓库目录下执行。");
}

static void RequireJava()
{
    try
    {
        using var p = Process.Start(new ProcessStartInfo("java", "-version")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true
        })!;
        p.WaitForExit();
        if (p.ExitCode != 0) throw new Exception();
    }
    catch
    {
        throw new Exception("PATH 里找不到可用的 java。需要 JDK 8 或更高版本。");
    }
}

static string RequireTool(string toolsDir, string fileName, string releasesUrl)
{
    var path = Path.Combine(toolsDir, fileName);
    if (!File.Exists(path))
        throw new Exception($"缺少 {path}{Environment.NewLine}" +
                            $"    从 {releasesUrl} 下载，改名成 {fileName} 放进 tools/");
    Console.WriteLine($"{fileName}: 已就位");
    return path;
}

static void Run(string exe, params string[] args)
{
    var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
    foreach (var a in args) psi.ArgumentList.Add(a);
    using var p = Process.Start(psi) ?? throw new Exception($"无法启动 {exe}");
    p.WaitForExit();
    if (p.ExitCode != 0) throw new Exception($"{exe} {string.Join(' ', args)} 退出码 {p.ExitCode}");
}

static void CopyDirectory(string src, string dst)
{
    foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
        Directory.CreateDirectory(dir.Replace(src, dst));
    Directory.CreateDirectory(dst);
    foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        File.Copy(file, file.Replace(src, dst), overwrite: true);
}

static int CountFiles(string dir) => Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length;
