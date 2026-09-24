# Contributing

## 环境

- Windows 10 1607+ / Server 2019+，x64
- .NET 8 SDK（`dotnet --version` 应为 8.x）

## 常用命令

```powershell
dotnet restore ACCcom.sln
dotnet build ACCcom.sln -c Release        # 零警告零错误是硬门禁
dotnet test ACCcom.sln -c Release         # 当前 1260 个测试，全过才能合入

# 带覆盖率采集时排除吞吐基准（插桩会拖慢热循环导致误报），与 CI 一致：
dotnet test ACCcom.sln -c Release --no-build --collect:"XPlat Code Coverage" --filter "FullyQualifiedName!~RxHotPathBenchmarkTests"
dotnet test tests\ACCcom.Core.Tests -c Release --no-build --filter "FullyQualifiedName~RxHotPathBenchmarkTests"   # 基准单独跑，不采集
dotnet format ACCcom.sln whitespace --verify-no-changes --no-restore
dotnet format ACCcom.sln style --verify-no-changes --no-restore
dotnet format ACCcom.sln analyzers --verify-no-changes --no-restore   # 三项全过才能合入
```

发布单文件（与 CI 一致）：

```powershell
dotnet publish src\ACCcom\ACCcom.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist\
```

## 工程约定

- **提交信息**：Conventional Commits，`scope` 取 `core/ui/mcp/docs/test/build` 等，如 `fix(core): ...`。标题写清楚改了什么，不要写过程。
- **中央包管理**：所有版本只在 `Directory.Packages.props` 里改，`csproj` 中 `PackageReference` 不带 `Version`。
- **零警告门禁**：`Directory.Build.props` 开了 `TreatWarningsAsErrors` + `AnalysisLevel latest` + `EnforceCodeStyleInBuild`，新增代码必须同时满足编译器与风格分析器。
- **可空与异常**：`Nullable enable` 常开；公共 API 对非法输入优先返回 `false`/空结果而非抛异常（参考 `SafePath`、`FileNameSanitizer` 的写法），并配单测锁定契约。
- **路径安全**：来自 HTTP/MCP/触发器的用户文件名必须走 `SafePath.TryCombineUnder`，禁止直接 `Path.Combine`；默认写盘位置放在 `%LOCALAPPDATA%\ACCcom` 下。
- **测试**：新功能/修 Bug 同步加单测（参考 `tests/ACCcom.Core.Tests` 命名 `XxxTests.cs`）；涉及十六进制、空输入、溢出、并发的边界必须覆盖。
- **文档**：README 中的数字（测试数、工具数）与 `docs/guide/*` 的行为描述必须与代码同步更新；配置示例用 `<repo-root>` 占位符，不要提交本机绝对路径；标注日期的设计文档过时后加归档注记，不要直接删。
- **不要提交**：`bin/`、`obj/`、`dist/`、`build/`、`TestResults/`、运行时日志与 `settings.json` 等本地生成文件（均已在 `.gitignore` 中）。
