# 愚公移山 2.0 单机版补丁

小学的时候玩过的放置点击游戏，在和群友聊上班摸鱼相关的时候突然想起这游戏，本来想着下回来玩玩但是发现开发商成滚木了，所以找了个包做patch把联网和内购去掉了

let's get happy！

## 构建

JDK 8+

.NET SDK 8+

下载这两个 jar，按下面的文件名放进 `tools/`

- [apktool_3.0.3.jar](https://github.com/iBotPeaches/Apktool/releases/download/v3.0.3/apktool_3.0.3.jar)
- [uber-apk-signer-1.3.0.jar](https://github.com/patrickfav/uber-apk-signer/releases/download/v1.3.0/uber-apk-signer-1.3.0.jar)

```
dotnet run --project patcher
```

patch好的apk在`out/ygys-offline.apk`

```
adb install -r out/ygys-offline.apk
```


## 已知问题

- 兑换码的接口 `weixin.txwy.tw` 没了，输入什么码都会提示失败，暂时没动，有这方面的需求可以提issue

- dex里面一堆没啥用的SDK都还打在包里懒得为了把包改小专门改dex了


## 测试环境

Windows 10 22H2

[WSA_2407.40000.4.0_x64_Release-Nightly-GApps-13.0_Windows_10.7z](https://release-assets.githubusercontent.com/github-production-release-asset/583772808/81c61da7-4499-4482-b758-8e5d25964b98?sp=r&sv=2018-11-09&sr=b&spr=https&se=2026-07-30T01%3A08%3A39Z&rscd=attachment%3B+filename%3DWSA_2407.40000.4.0_x64_Release-Nightly-GApps-13.0_Windows_10.7z&rsct=application%2Foctet-stream&skoid=96c2d410-5711-43a1-aedd-ab1947aa7ab0&sktid=398a6654-997b-47e9-b12b-9515b896b4de&skt=2026-07-30T00%3A08%3A08Z&ske=2026-07-30T01%3A08%3A39Z&sks=b&skv=2018-11-09&sig=wM6Ubus8%2BRFuzK9uA5PAbJL4p991DfU3jHvRusAkHiI%3D&jwt=eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJpc3MiOiJnaXRodWIuY29tIiwiYXVkIjoicmVsZWFzZS1hc3NldHMuZ2l0aHVidXNlcmNvbnRlbnQuY29tIiwia2V5Ijoia2V5MSIsImV4cCI6MTc4NTM3NTI2NCwibmJmIjoxNzg1MzcxNjY0LCJwYXRoIjoicmVsZWFzZWFzc2V0cHJvZHVjdGlvbi5ibG9iLmNvcmUud2luZG93cy5uZXQifQ.Smq_zUCkKV2G4ekK1ZAhMQ9rZcsQE_RmSki_NTBSr8w&response-content-disposition=attachment%3B%20filename%3DWSA_2407.40000.4.0_x64_Release-Nightly-GApps-13.0_Windows_10.7z&response-content-type=application%2Foctet-stream)


## 用到的工具

感谢以下的开源项目，这些开源工作的贡献在项目中起着重要的作用

- [Apktool](https://github.com/iBotPeaches/Apktool)：解包和回包
- [Mono.Cecil](https://github.com/jbevain/cecil)：DLL patch
- [ILSpy](https://github.com/icsharpcode/ILSpy)： 反编译 `Assembly-CSharp.dll`
- [jadx](https://github.com/skylot/jadx)：反编译 `classes.dex`
- [uber-apk-signer](https://github.com/patrickfav/uber-apk-signer)：签名 debug keystore
- [WSABuilds](https://github.com/MustardChef/WSABuilds)：测试环境


## 免责声明

`dec/` 里是完整的游戏本体，我不建议你在此仓库的基础上重新分发

游戏的一切权利仍归权利人所有

~~©~~ 2026 kakenhi
NO RIGHTS RESERVED.

