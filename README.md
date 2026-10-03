# bytemsg233-csharp-unity

ByteMsg233 的 C# Unity运行库，提供有界二进制编解码、调用方缓冲区复用、对象池和协议 hello。当前版本 **1.1.0**，MIT License。

```sh
dotnet add package ByteMsg233.Unity --version 1.1.0
```

NuGet 提供 netstandard2.0 / netstandard2.1 / net462 / net8.0 / net9.0 / net10.0 六种资产。Runtime 源码使用 C# 7.3；C# 1–14 的编译包消费模式见 [COMPATIBILITY.md](https://github.com/neko233-com/bytemsg233-csharp-unity/blob/main/COMPATIBILITY.md)。

**ByteMsg233.Server、ByteMsg233.Unity 和原 bytemsg233-lib-csharp 提供同名 ByteMsg233 程序集与类型，一个项目只能选择其中一个。** Server 与 Unity 的 Runtime C# 文件保持一致；Server 无 Unity 资源包装。

## Unity UPM

在 Unity Package Manager 添加：

```text
https://github.com/neko233-com/bytemsg233-csharp-unity.git#v1.1.0
```

UPM 包名 com.neko233.bytemsg233，Unity 2021.3+，API 配置 .NET Standard 2.1。保留 ByteMsg233.Runtime asmdef 与原资源 GUID。独立测试默认排除，不要启用 BYTEMSG233_STANDALONE_DOTNET_TESTS_ONLY。
NuGet 包通过宿主管理器导入；UPM 和 NuGet 二选一。Unity Editor / IL2CPP / WebGL / 真机尚未验收。

## 示例

```csharp
using ByteMsg233;

var writer = new ByteMsgWriter(256);
writer.WriteUIntField(1, 233);
writer.WriteStringField(2, "金币");

var reader = new ByteMsgReader(writer.ToArray());
while (!reader.IsEof)
{
    var field = reader.ReadFieldHeader();
    if (field.Tag == 1 && field.WireType == ByteMsgWireType.Varint)
    {
        ulong id = reader.ReadVarint();
    }
    else if (field.Tag == 2 && field.WireType == ByteMsgWireType.LengthDelimited)
    {
        string name = reader.ReadString();
    }
    else
    {
        reader.SkipField(field.WireType);
    }
}
```

解码业务字段时必须验证 WireType；未知字段调用 SkipField。reader 的输入由调用方持有，在读完前不可修改。
需要输出 byte[] 时 ToArray 会复制；ToArraySegment / WrittenSpan 返回当前缓冲区视图，writer 下一次写入可能改变它。

## 边界与生命周期

- reader 检查 varint 溢出、截断、非法字段头、长度和 UTF-8；坏包抛出 FormatException。
- 默认最大字段 16 MiB、集合元素 1,000,000、嵌套深度 64，可用 ByteMsgReaderOptions 收紧或调整。接收端仍需限制完整网络帧长度。
- writer 默认最大缓冲区 16 MiB；可配置容量上限，也可使用固定调用方 byte[]。嵌套写入失败回退已写长度。
- Reset、复用子 reader、复用列表 / 字典支持重复使用；单线程对象池支持预热、容量限制和重复归还检测。
- reader / writer / pool 属于单一调用方，不可被多个线程同时操作。服务端每个并行处理上下文分别持有实例。
- 数字写入、已缓存委托的嵌套写入、预热池和未知字段跳过有专门的零分配回归；字符串解码、ToArray、新建集合及捕获委托仍可能分配，不能宣称全部 API 零 GC。
- 同时提供 packed varint / zigzag、delta、bool bits、string blocks 等紧凑格式。协议 hello 只在连接协商时使用；不强制每条消息添加 socket 外层帧。

## 自动化

```powershell
./eng/verify.ps1 -Frameworks net8.0,net9.0,net10.0 -Interop
# Windows 增加 Framework 与 C# 1–14 消费者编译和运行
./eng/verify.ps1 -Frameworks net10.0,net462 -Legacy -Interop
```

xUnit 覆盖有效数据、10,000 组固定种子随机输入、边界、坏包、复用与并行独立上下文。Go v1.0.2 独立编码向量存于 Interop~，核对双方输出字节。CI 检查测试实际执行数、六种包资产和安装消费，三个操作系统运行 .NET 8 / 9 / 10。

生成器归 [bytemsg233](https://github.com/neko233-com/bytemsg233) 管理；目前审查的 C# 生成器生成 DTO / 枚举 / 池 / 协议常量，不生成二进制 Encode / Decode 方法。业务编解码仍使用本库或由宿主生成，本次不宣称完成全部 Schema 生成协议互通。来源与同步规则见 [UPSTREAM.md](https://github.com/neko233-com/bytemsg233-csharp-unity/blob/main/UPSTREAM.md)。
