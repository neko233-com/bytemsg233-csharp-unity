# bytemsg233-csharp-unity

Unity-friendly C# runtime for `bytemsg233`.

本仓库复用现有 C# 运行库，保留 `ByteMsg233` API 和 `com.neko233.bytemsg233` 包名。来源和同步约定见 [UPSTREAM.md](UPSTREAM.md)。不要在同一项目中同时安装本仓库和 `bytemsg233-lib-csharp`。

This repository is designed for two use cases:

- Unity projects through UPM with `com.neko233.bytemsg233`
- generated C# code from `bytemsg233`

The runtime stays small and native-feeling: writer, reader, single-threaded object pool, enum helpers, and clean collection helpers without external dependencies.

## Unity Install

Add this Git URL in Unity Package Manager:

```text
https://github.com/neko233-com/bytemsg233-csharp-unity.git
```

Or add it to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.neko233.bytemsg233": "https://github.com/neko233-com/bytemsg233-csharp-unity.git"
  }
}
```

Copy-based install from the original generator repository (uses its upstream runtime):

```bash
bytemsg233 install-lib csharp --to ./Assets/Plugins/ByteMsg233
```

## Runtime Shape

```csharp
using ByteMsg233;

public enum HeroState
{
    Idle = 0,
    Moving = 1,
    Dead = 2,
}

public sealed class Hero : IByteMsgResettable
{
    private static readonly ByteMsgPool<Hero> Pool = new(() => new Hero());

    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public HeroState State { get; set; } = HeroState.Idle;
    public List<string> Tags { get; set; } = new();

    public static Hero Rent() => Pool.Rent();
    public void Release() => Pool.Return(this);

    public void Reset()
    {
        Id = 0;
        Name = string.Empty;
        State = HeroState.Idle;
        Tags.Clear();
    }

    public byte[] Encode()
    {
        var writer = new ByteMsgWriter();
        writer.WriteUIntField(1, Id);
        writer.WriteStringField(2, Name);
        writer.WriteEnumField(3, (int)State);
        writer.WriteListField(4, Tags, (w, value) => w.WriteString(value));
        return writer.ToArray();
    }

    public static Hero Decode(byte[] data)
    {
        var hero = Rent();
        var reader = new ByteMsgReader(data);

        while (!reader.IsEof)
        {
            var header = reader.ReadFieldHeader();
            switch (header.Tag)
            {
                case 1:
                    hero.Id = (uint)reader.ReadVarint();
                    break;
                case 2:
                    hero.Name = reader.ReadString();
                    break;
                case 3:
                    hero.State = ByteMsgEnum.FromValue<HeroState>((int)reader.ReadVarint());
                    break;
                case 4:
                    hero.Tags = reader.ReadList(r => r.ReadString());
                    break;
                default:
                    reader.SkipField(header.WireType);
                    break;
            }
        }

        return hero;
    }
}
```

## API

- `ByteMsgWriter`: write varint, zigzag, string, bytes, message, list, map, and field helpers
- `ByteMsgReader`: read and skip fields with bounded length checks
- `ByteMsgPool<T>`: Unity-safe object pool for generated models
- `ByteMsgEnum`: enum value restore and validation helpers

## Development

```bash
dotnet build ByteMsg233.csproj -c Release
dotnet run --project Tests/ByteMsg233.Tests.csproj -c Release
dotnet pack ByteMsg233.csproj -c Release --no-build -o artifacts
```

Tests 是 .NET 10 可执行断言测试，不能用一次无测试输出的 `dotnet test` 代替。
Runtime 目标为 .NET Standard 2.1。独立测试程序集在 Unity 中默认排除，请勿定义 `BYTEMSG233_STANDALONE_DOTNET_TESTS_ONLY`。

本次验证覆盖 .NET 构建、编码 / 解码、未知字段跳过、紧凑列表、缓冲区复用和对象池。Unity Editor 导入、IL2CPP / WebGL 与真机仍待集成验证。
