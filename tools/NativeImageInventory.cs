// Read PE metadata only. No DLL loading or execution and no rights approval.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Text;

public sealed class NativeImageFacts
{
    public string Machine { get; set; }
    public bool HasClrHeader { get; set; }
    public bool IlOnly { get; set; }
    public bool HasManagedNativeHeader { get; set; }
    public bool NativeOrMixed { get; set; }
    public string[] Imports { get; set; }
}

public static class NativeImageInventory
{
    public static NativeImageFacts Read(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var pe = new PEReader(stream))
        {
            var headers = pe.PEHeaders;
            if (headers.PEHeader == null) throw new InvalidDataException("Not a PE image");
            var imports = new List<string>();
            var directory = headers.PEHeader.ImportTableDirectory;
            if (directory.RelativeVirtualAddress != 0 && directory.Size != 0)
            {
                var block = pe.GetSectionData(directory.RelativeVirtualAddress);
                var reader = block.GetReader(0, Math.Min(block.Length, directory.Size));
                int count = 0;
                while (reader.RemainingBytes >= 20)
                {
                    uint thunk = reader.ReadUInt32(), stamp = reader.ReadUInt32(), chain = reader.ReadUInt32();
                    int name = reader.ReadInt32(); uint first = reader.ReadUInt32();
                    if (thunk == 0 && stamp == 0 && chain == 0 && name == 0 && first == 0) break;
                    if (++count > 4096 || name <= 0) throw new InvalidDataException("Import metadata bound");
                    var text = pe.GetSectionData(name).GetReader();
                    var bytes = new List<byte>(); bool ended = false;
                    while (text.RemainingBytes > 0 && bytes.Count < 512)
                    {
                        byte b = text.ReadByte(); if (b == 0) { ended = true; break; } bytes.Add(b);
                    }
                    if (!ended) throw new InvalidDataException("Import name bound");
                    imports.Add(Encoding.ASCII.GetString(bytes.ToArray()));
                }
            }
            bool clr = headers.CorHeader != null;
            bool ilOnly = clr && (headers.CorHeader.Flags & CorFlags.ILOnly) != 0;
            bool managedNative = clr && headers.CorHeader.ManagedNativeHeaderDirectory.Size != 0;
            return new NativeImageFacts { Machine = headers.CoffHeader.Machine.ToString(), HasClrHeader = clr,
                IlOnly = ilOnly, HasManagedNativeHeader = managedNative,
                NativeOrMixed = !clr || (!ilOnly && !managedNative), Imports = imports.ToArray() };
        }
    }
}
