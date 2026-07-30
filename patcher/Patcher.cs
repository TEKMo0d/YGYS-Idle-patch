using Mono.Cecil;
using Mono.Cecil.Cil;

// 《愚公移山》Assembly-CSharp.dll 的 IL 补丁。
// 每一处改动都带断言：方法/字段/委托是否存在、指令形态是否符合预期、命中数量是否正确。
// 任何一组打不上都直接抛异常，绝不写出半成品 DLL。
static class Patcher
{
    public static void Apply(string inPath, string outPath)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(inPath)!);

        // 读进内存，这样即使输出覆盖输入也不会占着文件句柄
        var asm = AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(inPath)),
            new ReaderParameters { AssemblyResolver = resolver });
        var mod = asm.MainModule;

        int applied = 0;
        void Ok(string what) { Console.WriteLine($"  [OK] {what}"); applied++; }

        // 补丁打不上一律视为致命错误：直接抛出，绝不写出一个半成品 DLL。
        // （注意不能用 Environment.ExitCode —— 本程序有 return 语句，Main 返回 int，
        //   结尾的隐式 return 0 会把 ExitCode 覆盖掉。）
        void Fail(string what) => throw new Exception($"补丁未应用: {what}（该版本程序集与预期不符）");

        IEnumerable<TypeDefinition> AllTypes(TypeDefinition t)
        {
            yield return t;
            foreach (var n in t.NestedTypes)
                foreach (var x in AllTypes(n)) yield return x;
        }
        var types = mod.Types.SelectMany(AllTypes).ToList();
        TypeDefinition? T(string name) => types.FirstOrDefault(t => t.FullName == name || t.Name == name);

        static void Wipe(MethodDefinition m)
        {
            m.Body.Instructions.Clear();
            m.Body.Variables.Clear();
            m.Body.ExceptionHandlers.Clear();
            m.Body.InitLocals = false;
            // 原 maxstack 可能小于新方法体所需，抬到安全值（偏大无害）
            m.Body.MaxStackSize = Math.Max(m.Body.MaxStackSize, 8);
        }

        // 清空方法体并返回该返回类型的默认值
        static void Stub(MethodDefinition m)
        {
            Wipe(m);
            var il = m.Body.GetILProcessor();
            var rt = m.ReturnType;
            if (rt.MetadataType != MetadataType.Void)
            {
                switch (rt.MetadataType)
                {
                    case MetadataType.Boolean:
                    case MetadataType.Char:
                    case MetadataType.SByte:
                    case MetadataType.Byte:
                    case MetadataType.Int16:
                    case MetadataType.UInt16:
                    case MetadataType.Int32:
                    case MetadataType.UInt32:
                        il.Emit(OpCodes.Ldc_I4_0); break;
                    case MetadataType.Int64:
                    case MetadataType.UInt64:
                        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Conv_I8); break;
                    case MetadataType.Single: il.Emit(OpCodes.Ldc_R4, 0f); break;
                    case MetadataType.Double: il.Emit(OpCodes.Ldc_R8, 0d); break;
                    default: il.Emit(OpCodes.Ldnull); break;
                }
            }
            il.Emit(OpCodes.Ret);
        }

        Console.WriteLine("== 1. 内购改为本地即时发放 ==");
        {
            var cd = T("ChargeDelegate") ?? throw new Exception("找不到 ChargeDelegate");

            // ChargeDelegate.Init() -> 空实现（不初始化已停服的渠道支付 SDK）
            var init = cd.Methods.FirstOrDefault(m => m.Name == "Init" && m.Parameters.Count == 0);
            if (init != null) { Stub(init); Ok("ChargeDelegate.Init() 置空"); } else Fail("ChargeDelegate.Init()");

            // ChargeDelegate.Charge(sku, count) -> 直接触发本地成功回调，不走 JNI
            var charge = cd.Methods.FirstOrDefault(m =>
                m.Name == "Charge" && m.Parameters.Count == 2 &&
                m.Parameters[0].ParameterType.MetadataType == MetadataType.String);
            var evtField = cd.Fields.FirstOrDefault(f => f.Name == "onFinishPendingTransaction");
            var evtType = cd.NestedTypes.FirstOrDefault(t => t.Name == "OnFinishPendingTransaction");
            var invoke = evtType?.Methods.FirstOrDefault(m => m.Name == "Invoke");
            if (charge != null && evtField != null && invoke != null)
            {
                Wipe(charge);
                var il = charge.Body.GetILProcessor();
                var ret = il.Create(OpCodes.Ret);
                il.Emit(OpCodes.Ldsfld, evtField);
                il.Emit(OpCodes.Brfalse_S, ret);
                il.Emit(OpCodes.Ldsfld, evtField);
                il.Emit(OpCodes.Ldarg_0);   // sku  (static 方法, 参数从 arg0 起)
                il.Emit(OpCodes.Ldarg_1);   // count
                il.Emit(OpCodes.Callvirt, invoke);
                il.Append(ret);
                Ok("ChargeDelegate.Charge(string,int) 改为本地直接发放");
            }
            else Fail($"ChargeDelegate.Charge (charge={charge != null}, field={evtField != null}, invoke={invoke != null})");
        }

        Console.WriteLine("== 2. 拆掉真钱付费墙（9~10 关） ==");
        {
            var factory = T("Factory") ?? throw new Exception("找不到 Factory");
            var m = factory.Methods.FirstOrDefault(x => x.Name == "IsLevelLimit" && x.Parameters.Count == 1);
            if (m != null)
            {
                Wipe(m);
                var il = m.Body.GetILProcessor();
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ret);
                Ok("Factory.IsLevelLimit(int) 恒返回 false（全 10 关开放）");
            }
            else Fail("Factory.IsLevelLimit");
        }

        Console.WriteLine("== 3. 干掉微信分享地址的无限重试死循环 ==");
        {
            // 原版 CheckShareUrl() 协程在请求失败时无延迟递归重启自己，服务器已停服
            // => 等于每帧发一次 HTTP 请求的死循环。这里直接不启动它（协程本体留作死代码），
            //    并把 shareURL 固定成官网地址，避免 ShareLink 传 null。
            var wx = T("WeiXinApi") ?? throw new Exception("找不到 WeiXinApi");
            var shareUrlField = wx.Fields.FirstOrDefault(f => f.Name == "shareURL");
            var awake = wx.Methods.FirstOrDefault(m => m.Name == "Awake" && m.Parameters.Count == 0);
            var checkShareUrl = wx.Methods.FirstOrDefault(m => m.Name == "CheckShareUrl");

            if (awake != null && shareUrlField != null && checkShareUrl != null)
            {
                var body = awake.Body;
                var ins = body.Instructions;
                int callIdx = -1;
                for (int i = 0; i < ins.Count; i++)
                    if ((ins[i].OpCode == OpCodes.Call || ins[i].OpCode == OpCodes.Callvirt)
                        && ins[i].Operand is MethodReference mr && mr.Name == "CheckShareUrl")
                    { callIdx = i; break; }

                if (callIdx < 1) throw new Exception("Awake 中找不到 CheckShareUrl 调用");

                // 期望形态: ldarg.0 / ldarg.0 / call CheckShareUrl / call StartCoroutine* / pop / ret
                int start = callIdx - 1;
                if (ins[start].OpCode != OpCodes.Ldarg_0)
                    throw new Exception($"Awake 中 CheckShareUrl 调用前不是 ldarg.0，而是 {ins[start].OpCode}");
                bool startsCoroutine = ins.Skip(callIdx + 1).Take(2)
                    .Any(x => x.Operand is MethodReference m2 && m2.Name.StartsWith("StartCoroutine"));
                if (!startsCoroutine)
                    throw new Exception("CheckShareUrl 调用之后没有紧跟 StartCoroutine，方法体形态与预期不符");
                // 该语句必须是 Awake 的最后一条语句，才能直接截断：
                // 尾部只允许出现 ldarg.0 / 这两个 call / pop / nop / ret
                foreach (var x in ins.Skip(start))
                {
                    bool allowed = x.OpCode == OpCodes.Ldarg_0 || x.OpCode == OpCodes.Pop
                        || x.OpCode == OpCodes.Nop || x.OpCode == OpCodes.Ret
                        || (x.Operand is MethodReference m3
                            && (m3.Name == "CheckShareUrl" || m3.Name.StartsWith("StartCoroutine")));
                    if (!allowed)
                        throw new Exception($"StartCoroutine 语句之后仍有其它代码（{x.OpCode} {x.Operand}），不能直接截断");
                }
                // 不能有跳转落进将被删除的区间
                var doomed = ins.Skip(start).ToHashSet();
                foreach (var x in ins.Take(start))
                {
                    var targets = x.Operand switch
                    {
                        Instruction t => new[] { t },
                        Instruction[] ts => ts,
                        _ => Array.Empty<Instruction>()
                    };
                    if (targets.Any(doomed.Contains))
                        throw new Exception($"有跳转({x.OpCode})指向待删除区间，不能直接截断");
                }

                while (ins.Count > start) ins.RemoveAt(ins.Count - 1);
                var il = body.GetILProcessor();
                il.Emit(OpCodes.Ldstr, "http://www.txwy.tw/ygys");
                il.Emit(OpCodes.Stsfld, shareUrlField);
                il.Emit(OpCodes.Ret);
                body.MaxStackSize = Math.Max(body.MaxStackSize, 8);
                Ok("WeiXinApi.Awake() 不再启动 CheckShareUrl 协程（原版停服后每帧重发一次 HTTP 请求）");
            }
            else Fail($"WeiXinApi (awake={awake != null}, shareURL={shareUrlField != null}, coroutine={checkShareUrl != null})");
        }

        Console.WriteLine("== 4. 去掉启动时的新浪 IP 定位请求 ==");
        {
            int hits = 0;
            foreach (var t in types)
                foreach (var m in t.Methods.Where(m => m.HasBody))
                    foreach (var ins in m.Body.Instructions)
                        if (ins.OpCode == OpCodes.Ldstr && ins.Operand is string s && s.Contains("dpool.sina.com.cn"))
                        {
                            ins.Operand = "file:///offline_no_network";  // 立即失败, 不产生网络流量
                            Console.WriteLine($"       {t.FullName}::{m.Name}");
                            hits++;
                        }
            if (hits > 0) Ok($"替换 {hits} 处 IP 定位 URL"); else Fail("新浪 IP 定位 URL");
        }

        Console.WriteLine("== 5. DataEye 埋点全部置空（服务器已停，避免离线堆积重传） ==");
        {
            var dc = types.Where(t => t.Name.StartsWith("DC") && !t.IsEnum && t.IsClass).ToList();
            int n = 0;
            foreach (var t in dc)
            {
                foreach (var m in t.Methods.Where(m => m.HasBody && m.Name != ".cctor" && m.Name != ".ctor"))
                {
                    // DCConfigParams 的取值方法保留 defaultValue 语义
                    if (t.Name == "DCConfigParams" && m.Name.StartsWith("getParameter") && m.Parameters.Count == 2)
                    {
                        Wipe(m);
                        var il2 = m.Body.GetILProcessor();
                        il2.Emit(OpCodes.Ldarg_1);
                        il2.Emit(OpCodes.Ret);
                    }
                    else Stub(m);
                    n++;
                }
            }
            if (n > 0) Ok($"{dc.Count} 个类 / {n} 个方法置空: {string.Join(", ", dc.Select(x => x.Name))}");
            else Fail("DataEye DC* 类");
        }

        Console.WriteLine("== 6. 分享奖励改为本地直接发放（不拉起微信/接口） ==");
        {
            var wx = T("WeiXinApi") ?? throw new Exception("找不到 WeiXinApi");
            var resultField = wx.Fields.FirstOrDefault(f => f.Name == "wechatApiResult")
                ?? throw new Exception("找不到 WeiXinApi.wechatApiResult 事件字段");
            var resultDelegate = wx.NestedTypes.FirstOrDefault(t => t.Name == "WeiXinApiResult")
                ?? throw new Exception("找不到 WeiXinApiResult 委托类型");
            var resultInvoke = resultDelegate.Methods.FirstOrDefault(m => m.Name == "Invoke")
                ?? throw new Exception("找不到 WeiXinApiResult.Invoke");

            // 7a. 给 WeiXinApi 新增一个静态方法，直接以“成功”结果触发 wechatApiResult 事件。
            //     奖励发放逻辑本来就挂在这个事件上（WeiXin.Awake 里的匿名方法），
            //     原本只有 Java 端分享成功回调 OnWeChatResp(errcode=0) 才会触发它。
            var helper = new MethodDefinition("LocalShareSuccess",
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                mod.TypeSystem.Void);
            {
                var il = helper.Body.GetILProcessor();
                var ret = il.Create(OpCodes.Ret);
                il.Emit(OpCodes.Ldsfld, resultField);
                il.Emit(OpCodes.Brfalse_S, ret);
                il.Emit(OpCodes.Ldsfld, resultField);
                il.Emit(OpCodes.Ldstr, "errcode=0");
                il.Emit(OpCodes.Callvirt, resultInvoke);
                il.Append(ret);
                helper.Body.MaxStackSize = 8;
            }
            wx.Methods.Add(helper);

            // 7b. 把调用 ShareLink 的那个匿名方法（分享确认按钮）改成直接调上面的方法。
            //     顺带绕掉 (texture.mainTexture as Texture2D).EncodeToPNG() —— 它在 ShareLink
            //     之前求值，而那张 UITexture 从没被代码赋过值，为 null 就会 NRE。
            var callers = new List<MethodDefinition>();
            foreach (var t in types)
                foreach (var m in t.Methods.Where(m => m.HasBody))
                    if (m.Body.Instructions.Any(i =>
                            i.Operand is MethodReference mr && mr.Name == "ShareLink"
                            && mr.DeclaringType.Name == "WeiXinApi"))
                        callers.Add(m);

            if (callers.Count != 1)
                throw new Exception($"预期只有 1 处 ShareLink 调用，实际 {callers.Count} 处: " +
                                    string.Join(", ", callers.Select(m => m.FullName)));

            var submit = callers[0];
            Wipe(submit);
            {
                var il = submit.Body.GetILProcessor();
                il.Emit(OpCodes.Call, helper);
                il.Emit(OpCodes.Ret);
            }
            Ok($"新增 WeiXinApi.LocalShareSuccess()，并改写 {submit.DeclaringType.Name}::{submit.Name}");
            Console.WriteLine("       奖励与冷却规则保持原版：主分享首次 +5000 儿孙 / 之后 +20 金币 / 3 小时冷却；");
            Console.WriteLine("       成就分享 +20 金币，每个成就一次（achivmentID*isShare 标记）");
        }

        Console.WriteLine("== 7. 不再读取设备唯一标识（否则缺 READ_PHONE_STATE 会崩） ==");
        {
            // Unity 4 的 SystemInfo.deviceUniqueIdentifier 在 Android 上走
            // TelephonyManager.getDeviceId()，需要 READ_PHONE_STATE —— 原版清单里那条权限
            // 就是 Unity 因为这处调用自动加的。单机版把权限删了，而
            // DataAnalyzeCollect.Awake() 里这个调用没有 try/catch，启动期就会抛 SecurityException。
            // 这三处调用的下游（埋点 / 已停服的兑换码接口）本来就都废了，直接换成常量字符串。
            int hits = 0;
            foreach (var t in types)
                foreach (var m in t.Methods.Where(m => m.HasBody))
                {
                    var il = m.Body.GetILProcessor();
                    foreach (var ins in m.Body.Instructions.ToList())
                        if (ins.OpCode == OpCodes.Call
                            && ins.Operand is MethodReference mr
                            && mr.Name == "get_deviceUniqueIdentifier")
                        {
                            // 两者都是 0 进 1 出，可以直接原地替换
                            il.Replace(ins, il.Create(OpCodes.Ldstr, "offline-device"));
                            Console.WriteLine($"       {t.FullName}::{m.Name}");
                            hits++;
                        }
                }
            if (hits != 3)
                throw new Exception($"预期 3 处 deviceUniqueIdentifier 调用，实际 {hits} 处");
            Ok($"替换 {hits} 处 SystemInfo.deviceUniqueIdentifier -> \"offline-device\"");
        }

        asm.Write(outPath);
        Console.WriteLine($"\n共应用 {applied} 组补丁 -> {outPath}");

        // 回读校验：重新打开写出的程序集并遍历每个方法体。
        // 访问 Body.Instructions 会强制 Cecil 解析 IL，结构性损坏会在这里抛异常。
        {
            var r2 = new DefaultAssemblyResolver();
            r2.AddSearchDirectory(Path.GetDirectoryName(inPath)!);
            using var check = AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(outPath)),
                new ReaderParameters { AssemblyResolver = r2 });
            int methods = 0, instrs = 0;
            foreach (var t in check.MainModule.Types.SelectMany(AllTypes))
                foreach (var m in t.Methods.Where(m => m.HasBody))
                {
                    methods++;
                    instrs += m.Body.Instructions.Count;
                }
            Console.WriteLine($"回读校验通过: {methods} 个方法体 / {instrs} 条指令");
        }

    }
}
