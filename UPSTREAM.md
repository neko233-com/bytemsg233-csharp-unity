# Upstream and synchronization

原始运行库来自 [bytemsg233-lib-csharp](https://github.com/neko233-com/bytemsg233-lib-csharp)，基线提交 c8316e80cf59c6a2992faefc4fc4ddb7f071ed2e，原版本 1.0.1。保留 MIT License。

1.1.0 在基线上增加有界解析、可复用字节缓冲区、严格字段 / UTF-8 / hello 验证、对象池重复归还检测和多目标兼容；Runtime 已有实质修改。

[Server](https://github.com/neko233-com/bytemsg233-csharp-server) 与 [Unity](https://github.com/neko233-com/bytemsg233-csharp-unity) 的 Runtime/*.cs 必须保持相同。两边 eng/runtime-sha256.json 保存统一 LF 的内容 SHA-256；同步必须同时更新源码、测试、版本及清单，并通过各自 CI。Unity 的 meta / asmdef 包装不复制到 Server。

Go 互通使用 bytemsg233-lib-go v1.0.2（72a3ea9794295b170f4e851743c7d8d2487eb564），依赖由 go.mod / go.sum 固定。生成器源码未在此发布中修改。

不要同时安装两个变体或原运行库；它们的 .NET 程序集与命名空间均为 ByteMsg233。
