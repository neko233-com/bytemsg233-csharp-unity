# Upstream

本仓库基于公开的 [bytemsg233-lib-csharp](https://github.com/neko233-com/bytemsg233-lib-csharp) 运行库：

- 源提交：`c8316e80cf59c6a2992faefc4fc4ddb7f071ed2e`
- 源包版本：`1.0.1`
- 本次保持 `Runtime/*.cs` 内容不变，保留原 MIT License。
- 新增独立仓库说明、构建 / 测试 CI、Unity 资源 meta，以及默认排除独立测试的 assembly definition。

此仓库作为 framework233-game-csharp 工作区的 Unity 命名入口。生成器仍由 [bytemsg233](https://github.com/neko233-com/bytemsg233) 提供；本次没有修改生成器或它的子模块地址。

UPM 包名保持 `com.neko233.bytemsg233`，命名空间与 .NET 程序集保持 `ByteMsg233`，Unity 程序集沿用 `ByteMsg233.Runtime`。本仓库与原运行库提供同一套类型，同一 Unity 项目只能安装其中一个。

后续同步应显式审查上游提交，并重新运行 Tests；避免两个仓库的 Runtime 无记录地分叉。
