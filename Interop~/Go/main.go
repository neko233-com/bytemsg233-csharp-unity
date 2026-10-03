package main

import (
	"bytes"
	"encoding/hex"
	"flag"
	"fmt"
	"os"
	"sort"

	b "github.com/neko233-com/bytemsg233-lib-go"
)

func main() {
	out := flag.String("out", "", "write canonical Go wire vectors")
	verify := flag.String("verify", "", "verify canonical C# wire vectors")
	flag.Parse()
	vectors := map[string][]byte{}
	w := b.NewWriter()
	for _, n := range []uint64{0, 1, 127, 128, 16384, 4294967295, 18446744073709551615} {
		w.WriteVarint(n)
	}
	w.WriteVarint(b.ZigZagEncode(-9223372036854775808))
	w.WriteFixed32(0x12345678)
	w.WriteFixed64(0x0102030405060708)
	w.WriteStringValue("金币🙂")
	w.WriteBytes([]byte{0, 1, 127, 255})
	vectors["scalars"] = append([]byte(nil), w.Bytes()...)
	w.Reset()
	w.WritePackedVarints([]uint64{0, 127, 128, 18446744073709551615})
	w.WriteDeltaVarints([]uint64{100, 90, 200, 0, 18446744073709551615})
	w.WriteBoolBitset([]bool{true, false, true, true, false, true, false, false, true})
	w.WriteStringList([]string{"", "金币", "🙂"})
	vectors["blocks"] = append([]byte(nil), w.Bytes()...)
	vectors["hello"] = b.AppendProtocolHello(nil, b.ProtocolHello{Version: 233, MinCompatible: 200})
	w.Reset()
	w.WriteHeader(536870911, b.WireVarint)
	w.WriteVarint(233)
	vectors["max_tag"] = append([]byte(nil), w.Bytes()...)
	names := make([]string, 0, len(vectors))
	for name := range vectors {
		names = append(names, name)
	}
	sort.Strings(names)
	var encoded bytes.Buffer
	for _, name := range names {
		fmt.Fprintf(&encoded, "%s=%s\n", name, hex.EncodeToString(vectors[name]))
	}
	if *out != "" {
		if err := os.WriteFile(*out, encoded.Bytes(), 0644); err != nil {
			panic(err)
		}
	}
	if *verify != "" {
		actual, err := os.ReadFile(*verify)
		if err != nil {
			panic(err)
		}
		actual = bytes.ReplaceAll(actual, []byte("\r\n"), []byte("\n"))
		if !bytes.Equal(actual, encoded.Bytes()) {
			panic("C# bytes differ from the pinned Go runtime")
		}
		fmt.Println("PASS C# wire bytes match independently encoded Go vectors")
	}
	if *out == "" && *verify == "" {
		_, _ = os.Stdout.Write(encoded.Bytes())
	}
}
